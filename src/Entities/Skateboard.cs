// Skateboard.cs - Skateboard posable librement au sol (hors grille), enfourchable et pilotable.
//
// Différences volontaires avec la voiture :
//   * Physique en vitesse vectorielle (pas d'angle/vélocité angulaire) : pas de dérive de direction,
//     pas de marche arrière "inversée", pas de coincement dans les angles.
//   * Frottements/accélérations exprimés PAR SECONDE (frame-rate indépendant).
//   * Collisions : cercle vs rectangles de collision réels des objets (World.GetCollisionRect)
//     avec sous-pas et glissement le long des murs. Pas de rectangle orienté.
//   * Une seule mise à jour par frame.
//   * Le sprite est compose de calques empiles et orientes selon un cap a 8 directions.
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public class Skateboard
    {
        // ============ RÉGLAGES ============

        /// <summary>Clé de l'item dans items.json (celle qu'on passe à GameData.GetItemId).</summary>
        public const string ITEM_KEY = "skateboard";

        /// <summary>Vitesse max = vitesse de course du joueur x ce multiplicateur.</summary>
        /// <summary>true : utilise World.IsColliding (comme le joueur). false : test par tuiles interne (voir DebugCollisions).</summary>
        public static bool UseWorldCollision = true;

        /// <summary>Avec UseWorldCollision = false : affiche en console quelle tuile bloque la planche.</summary>
        public static bool DebugCollisions = false;

        public const float SPEED_MULT = 1.2f;
        public const float DEFAULT_TOP_SPEED = 300f;

        private const float ACCEL_FACTOR = 1.6f;            // x TopSpeed / s  (~0.6 s pour atteindre la vitesse max)
        private const float BRAKE_FACTOR = 2.8f;            // x TopSpeed / s  (quand on appuie à l'opposé du mouvement)
        private const float COAST_FACTOR = 0.35f;           // x TopSpeed / s  (on lâche les touches : on glisse ~3 s)
        private const float ROLL_AWAY_DECEL_FACTOR = 1.2f;  // x TopSpeed / s  (planche abandonnée : elle finit de rouler)
        private const float MIN_ROLL_SPEED = 6f;            // en dessous, vitesse forcée à 0
        private const float MIN_TURN_SPEED = 12f;
        private const float TURN_SPEED_DEGREES_PER_SECOND = 220f;

        private const float FOOTPRINT_RADIUS_TILES = 0.28f; // rayon de collision (en tuiles)
        private const float DRAW_WIDTH_TILES = 1.5f;        // largeur affichée (en tuiles)
        private const float LAYER_HEIGHT_STEP = 1f;
        private const float PICKUP_HALF_SIZE_TILES = DRAW_WIDTH_TILES / 2f;
        private const int MAX_TEXTURE_LAYERS = 64;

        // ============ ÉTAT ============

        public Vector2 Position;
        public Vector2 Velocity;
        /// <summary>Angle en degrés depuis le haut de l'image, positif dans le sens horaire.</summary>
        public float Heading;
        public bool IsRidden;
        public float TopSpeed = DEFAULT_TOP_SPEED;

        public Skateboard(Vector2 pos, float heading = 0f)
        {
            Position = pos;
            Heading = NormalizeAngle(heading);
        }

        public static float FootprintRadius => Program.TileSize * FOOTPRINT_RADIUS_TILES;
        public static float PickupHalfSizeTiles => PICKUP_HALF_SIZE_TILES;

        // ============ PHYSIQUE ============

        /// <summary>
        /// inputDir : direction demandée (non normalisée acceptée, zéro = aucune touche).
        /// Ignoré si la planche n'est pas montée.
        /// </summary>
        public void Update(float dt, HashSet<string> destroyedObjects, Vector2 inputDir)
        {
            if (dt <= 0f) return;

            if (IsRidden && inputDir.LengthSquared() > 0.01f)
            {
                Vector2 dir = Vector2.Normalize(inputDir);
                float accel = TopSpeed * ACCEL_FACTOR;
                // Appuyer à l'opposé du mouvement = freiner plus fort (sinon on "dérape" trop longtemps)
                if (Vector2.Dot(Velocity, dir) < 0f) accel = TopSpeed * BRAKE_FACTOR;
                Velocity = MoveToward(Velocity, dir * TopSpeed, accel * dt);
            }
            else
            {
                float decel = TopSpeed * (IsRidden ? COAST_FACTOR : ROLL_AWAY_DECEL_FACTOR);
                Velocity = MoveToward(Velocity, Vector2.Zero, decel * dt);
            }

            if (Velocity.LengthSquared() < MIN_ROLL_SPEED * MIN_ROLL_SPEED)
                Velocity = Vector2.Zero;

            // Monté : le cap suit la direction voulue (les touches), même à l'arrêt ou contre un mur.
            // Sans touche, on garde le dernier cap. Abandonnée et en train de rouler : elle suit sa vitesse.
            if (IsRidden)
            {
                if (inputDir.LengthSquared() > 0.01f) TurnToward(inputDir, dt);
            }
            else if (Velocity.LengthSquared() >= MIN_TURN_SPEED * MIN_TURN_SPEED)
            {
                TurnToward(Velocity, dt);
            }

            if (Velocity != Vector2.Zero)
                MoveWithCollision(Velocity * dt, destroyedObjects);
        }

        private static Vector2 MoveToward(Vector2 current, Vector2 target, float maxDelta)
        {
            Vector2 d = target - current;
            float len = d.Length();
            if (len <= maxDelta || len < 0.0001f) return target;
            return current + d / len * maxDelta;
        }

        /// <summary>Tourne progressivement le cap vers la direction donnée (les sprites pointent vers le haut).</summary>
        private void TurnToward(Vector2 direction, float dt)
        {
            float target = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI + 90f;
            float difference = NormalizeAngle(target - Heading + 180f) - 180f;
            float maxTurn = TURN_SPEED_DEGREES_PER_SECOND * dt;
            Heading = NormalizeAngle(Heading + Math.Clamp(difference, -maxTurn, maxTurn));
        }

        private static float NormalizeAngle(float angle)
            => (angle % 360f + 360f) % 360f;

        private void MoveWithCollision(Vector2 delta, HashSet<string> destroyedObjects)
        {
            float radius = FootprintRadius;

            // Sous-pas : jamais plus d'un demi-rayon par pas => impossible de traverser un mur fin
            int steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / (radius * 0.5f)));
            Vector2 step = delta / steps;

            // Si la planche est déjà dans un obstacle (bloc posé dessus), on la laisse en sortir librement
            bool stuck = IsBlocked(Position, radius, destroyedObjects);

            for (int i = 0; i < steps; i++)
            {
                if (step.X != 0f)
                {
                    Vector2 t = new Vector2(Position.X + step.X, Position.Y);
                    if (stuck || !IsBlocked(t, radius, destroyedObjects)) Position = t;
                    else Velocity.X = 0f;
                }
                if (step.Y != 0f)
                {
                    Vector2 t = new Vector2(Position.X, Position.Y + step.Y);
                    if (stuck || !IsBlocked(t, radius, destroyedObjects)) Position = t;
                    else Velocity.Y = 0f;
                }
                if (Velocity == Vector2.Zero) break;
            }
        }

        // ============ COLLISIONS AVEC LE MONDE ============

        /// <summary>Un cercle centré en pos touche-t-il un obstacle (objet solide ou sol non praticable) ?</summary>
        public static bool IsBlocked(Vector2 pos, float radius, HashSet<string> destroyedObjects)
        {
            // Même collision que le joueur à pied : la planche se comporte comme lui, sans règles en double.
            if (UseWorldCollision && Program.TryWorldCollisionAt(pos, destroyedObjects, out bool worldBlocked))
                return worldBlocked;

            int ts = Program.TileSize;
            int sx = (int)MathF.Floor((pos.X - radius) / ts);
            int ex = (int)MathF.Floor((pos.X + radius) / ts);
            int sy = (int)MathF.Floor((pos.Y - radius) / ts);
            int ey = (int)MathF.Floor((pos.Y + radius) / ts);

            for (int x = sx; x <= ex; x++)
            for (int y = sy; y <= ey; y++)
            {
                int id = destroyedObjects.Contains($"{x}_{y}") ? 0 : World.GetObjectIdAt(x, y);
                bool isObject = id != 0;
                if (!isObject) id = World.GetGroundTileIdAt(x, y);

                var tileData = WorldTileRegistry.GetTile(id);
                if (tileData == null || tileData.Walkable) continue;

                Rectangle rect;
                if (isObject)
                {
                    // Rectangle de collision réel de l'objet (ex : tronc d'arbre), pas toute la tuile
                    var (hbX, hbY, hbW, hbH) = World.GetCollisionRect(x, y, tileData, ts);
                    if (hbW <= 0 || hbH <= 0) continue;
                    rect = new Rectangle(hbX, hbY, hbW, hbH);
                }
                else
                {
                    rect = new Rectangle(x * ts, y * ts, ts, ts); // eau, vide, etc. : toute la tuile
                }

                if (CircleHitsRect(pos, radius, rect))
                {
                    if (DebugCollisions)
                        Console.WriteLine($"[Skateboard] bloquée par {(isObject ? "objet" : "sol")} id={id} '{tileData.Name}' tuile ({x},{y}) rect=({rect.X},{rect.Y},{rect.Width},{rect.Height})");
                    return true;
                }
            }
            return false;
        }

        private static bool CircleHitsRect(Vector2 c, float r, Rectangle rect)
        {
            float nx = Math.Clamp(c.X, rect.X, rect.X + rect.Width);
            float ny = Math.Clamp(c.Y, rect.Y, rect.Y + rect.Height);
            float dx = c.X - nx;
            float dy = c.Y - ny;
            return dx * dx + dy * dy < r * r;
        }

        // ============ RENDU ============

        private static List<Texture2D>? _textures;

        private static List<Texture2D> Textures
        {
            get
            {
                if (_textures != null) return _textures;
                _textures = new List<Texture2D>();
                for (int i = 0; i < MAX_TEXTURE_LAYERS; i++)
                {
                    string path = $"assets/extras/skateboard_body{i}.png";
                    if (!File.Exists(path)) break;
                    Texture2D texture = Raylib.LoadTexture(path);
                    Raylib.SetTextureFilter(texture, TextureFilter.Point);
                    _textures.Add(texture);
                }
                if (_textures.Count == 0)
                    Console.WriteLine("[Skateboard] Aucun calque charge. Attendu : assets/extras/skateboard_body0.png, skateboard_body1.png, etc.");
                else
                    Console.WriteLine($"[Skateboard] {_textures.Count} calque(s) charge(s).");
                return _textures;
            }
        }

        public void Draw()
        {
            int ts = Program.TileSize;

            // Ombre au sol
            Raylib.DrawEllipse((int)Position.X, (int)Position.Y + 2, (int)(ts * 0.36f), (int)(ts * 0.12f), new Color(0, 0, 0, 70));

            List<Texture2D> textures = Textures;
            if (textures.Count == 0) return;

            Texture2D referenceTexture = textures[0];
            float scale = ts * DRAW_WIDTH_TILES / referenceTexture.Width;
            float rotation = Heading;
            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D texture = textures[i];
                if (texture.Id == 0) continue;

                float brightness = MathF.Pow((float)(i + 1) / textures.Count, 0.7f);
                brightness = Math.Max(brightness, 0.25f);
                Color tint = new Color((int)(255 * brightness), (int)(255 * brightness), (int)(255 * brightness), 255);
                float y = Position.Y - i * LAYER_HEIGHT_STEP;
                Rectangle source = new Rectangle(0, 0, texture.Width, texture.Height);
                Rectangle destination = new Rectangle(
                    MathF.Round(Position.X), MathF.Round(y),
                    texture.Width * scale, texture.Height * scale);
                Vector2 origin = new Vector2(destination.Width / 2f, destination.Height / 2f);
                Raylib.DrawTexturePro(texture, source, destination, origin, rotation, tint);
            }
        }

        /// <summary>
        /// Élément de rendu trié par Y (voir VehicleRendering.cs). sortY vient de Program.GetSkateboardSortY :
        /// légèrement sous celui du joueur, pour qu'il passe devant la planche quand il est monté dessus.
        /// </summary>
        public RenderItem ToRenderItem(float sortY)
        {
            return new RenderItem
            {
                Y = sortY,
                SortLayer = 0,
                Kind = RenderItemKind.External,
                DrawAction = Draw
            };
        }

        public void DrawDebugHitbox()
        {
            Raylib.DrawCircleLines((int)Position.X, (int)Position.Y, FootprintRadius, Color.Green);
            Raylib.DrawLineEx(Position, Position + Velocity * 0.2f, 2f, Color.Yellow);
        }

        // ============ SAUVEGARDE ============

        public SkateboardSaveData GetSaveData() => new SkateboardSaveData
        {
            PosX = Position.X,
            PosY = Position.Y,
            Heading = (int)MathF.Round(Heading / 45f) % 8,
            HeadingAngle = Heading,
            VelocityX = Velocity.X,
            VelocityY = Velocity.Y
        };

        public static Skateboard FromSaveData(SkateboardSaveData d)
        {
            float heading = d.HeadingAngle ?? d.Heading * 45f;
            return new Skateboard(new Vector2(d.PosX, d.PosY), heading)
            {
                Velocity = new Vector2(d.VelocityX, d.VelocityY)
            };
        }
    }

    [System.Serializable]
    public class SkateboardSaveData
    {
        public float PosX { get; set; }
        public float PosY { get; set; }
        public int Heading { get; set; }
        public float? HeadingAngle { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
    }

    // =====================================================================================
    //  COLLE AVEC Program : liste des planches, placement, monter/descendre, conduite.
    //  (Program est partielle : ce code a accès à _playerPos, _animState, etc.)
    // =====================================================================================
    public static partial class Program
    {
        public static readonly List<Skateboard> Skateboards = new();

        /// <summary>Planche actuellement chevauchée par le joueur local (null = à pied).</summary>
        public static Skateboard? PlayerSkateboard { get; private set; }

        private const float SKATE_MOUNT_RANGE_TILES = 1.1f;
        private static Vector2 _skateHitboxDim = Vector2.Zero;

        // ---------- PLACEMENT (clic gauche avec l'item en main) ----------

        /// <summary>Pose la planche exactement sous la souris. Retourne true si elle a été posée (consommer l'item).</summary>
        public static bool TryPlaceSkateboard(Vector2 worldPos)
        {
            int ts = TileSize;

            if (PlayerSkateboard != null)
            {
                activeNotifications.Add(new Notification("Descends d'abord de ta planche.", new Color(255, 100, 100, 255), 1.5f));
                return false;
            }

            if (Skateboard.IsBlocked(worldPos, Skateboard.FootprintRadius, destroyedObjects))
            {
                activeNotifications.Add(new Notification("Impossible de poser le skateboard ici.", new Color(255, 100, 100, 255), 1.5f));
                return false;
            }

            foreach (var other in Skateboards)
            {
                if (Vector2.Distance(other.Position, worldPos) < ts * 0.8f)
                {
                    activeNotifications.Add(new Notification("Il y a déjà un skateboard ici.", new Color(255, 100, 100, 255), 1.5f));
                    return false;
                }
            }

            Skateboards.Add(new Skateboard(worldPos, _playerFacing > 0 ? 90f : 270f));
            return true;
        }

        // ---------- MONTER / DESCENDRE / RAMASSER (touche Interagir) ----------

        /// <summary>
        /// À appeler quand Interagir est pressé. Retourne true si l'appui a été consommé
        /// (monté, descendu ou ramassé) : le reste des interactions doit alors être ignoré.
        /// Shift + Interagir près d'une planche libre = la ramasser dans l'inventaire.
        /// </summary>
        public static bool HandleSkateboardInteract(Vector2 hitboxDim)
        {
            _skateHitboxDim = hitboxDim;

            if (PlayerSkateboard != null)
            {
                DismountSkateboard();
                return true;
            }

            if (_isSitting || MountedAnimal != null || PlayerCurrentCar != null || PlayerCurrentWagonTile.HasValue)
                return false;

            Vector2 feet = GetPlayerFeetPosition(_playerPos);
            float range = TileSize * SKATE_MOUNT_RANGE_TILES;
            Skateboard? nearest = null;
            float best = range;
            foreach (var sb in Skateboards)
            {
                float d = Vector2.Distance(sb.Position, feet);
                if (d <= best) { best = d; nearest = sb; }
            }
            if (nearest == null) return false;

            if (Raylib.IsKeyDown(KeyboardKey.LeftShift))
            {
                Skateboards.Remove(nearest);
                GiveItemToPlayer(GameData.GetItemId(Skateboard.ITEM_KEY), 1, nearest.Position);
                return true;
            }

            // Monter
            _isDancing = false;
            _currentEmote = null;
            _isEmoteWheelOpen = false;

            nearest.IsRidden = true;
            nearest.TopSpeed = GetPlayerRunSpeed() * Skateboard.SPEED_MULT;
            PlayerSkateboard = nearest;
            return true;
        }

        public static bool TryPickUpSkateboardAt(Vector2 worldPos)
        {
            Skateboard? target = null;
            float bestDistanceSquared = float.PositiveInfinity;
            float halfSize = TileSize * Skateboard.PickupHalfSizeTiles;

            foreach (var skateboard in Skateboards)
            {
                if (skateboard.IsRidden) continue;

                Vector2 delta = worldPos - skateboard.Position;
                float rotation = -skateboard.Heading * MathF.PI / 180f;
                float cos = MathF.Cos(rotation);
                float sin = MathF.Sin(rotation);
                float localX = delta.X * cos - delta.Y * sin;
                float localY = delta.X * sin + delta.Y * cos;
                if (MathF.Abs(localX) > halfSize || MathF.Abs(localY) > halfSize) continue;

                float distanceSquared = delta.LengthSquared();
                if (distanceSquared < bestDistanceSquared)
                {
                    target = skateboard;
                    bestDistanceSquared = distanceSquared;
                }
            }

            if (target == null) return false;

            Skateboards.Remove(target);
            GiveItemToPlayer(GameData.GetItemId(Skateboard.ITEM_KEY), 1, target.Position);
            return true;
        }

        /// <summary>
        /// Fait descendre le joueur (sûr à appeler de n'importe où : mort, téléportation, bateau, etc.).
        /// La planche garde la moitié de sa vitesse et finit de rouler toute seule.
        /// </summary>
        public static void DismountSkateboard()
        {
            var sb = PlayerSkateboard;
            if (sb == null) return;

            sb.IsRidden = false;
            sb.Velocity *= 0.5f;
            PlayerSkateboard = null;

            Vector2 landing = sb.Position - GetPlayerFeetPosition(Vector2.Zero);

            // Si le joueur atterrit dans un obstacle, on le décale vers un endroit libre proche
            if (_skateHitboxDim != Vector2.Zero && World.IsColliding(landing, _skateHitboxDim, destroyedObjects))
            {
                bool found = false;
                for (int ring = 1; ring <= 2 && !found; ring++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * MathF.PI / 4f;
                        Vector2 c = landing + new Vector2(MathF.Cos(a), MathF.Sin(a)) * TileSize * 0.6f * ring;
                        if (!World.IsColliding(c, _skateHitboxDim, destroyedObjects))
                        {
                            landing = c;
                            found = true;
                            break;
                        }
                    }
                }
            }

            _playerPos = landing;
            _animState = "idle";
            _animFrame = 0;
            _animProg = 0f;
        }

        // ---------- CONDUITE (appelé chaque frame tant que le joueur est monté) ----------

        public static void UpdateSkateboardRide(float dt, ref float facing, Vector2 hitboxDim)
        {
            var sb = PlayerSkateboard;
            if (sb == null) return;
            _skateHitboxDim = hitboxDim;

            Vector2 input = ReadSkateboardInput();
            sb.TopSpeed = GetPlayerRunSpeed() * Skateboard.SPEED_MULT;
            sb.Update(dt, destroyedObjects, input);

            // Les pieds du joueur sont posés sur la planche
            _playerPos = sb.Position - GetPlayerFeetPosition(Vector2.Zero);

            if (input.X > 0.01f) facing = 1f;
            else if (input.X < -0.01f) facing = -1f;

            _animState = "idle";
            _animFrame = 0;
            _animProg = 0f;
        }

        private static Vector2 ReadSkateboardInput()
        {
            if (ChatSystem.IsOpen) return Vector2.Zero;

            Vector2 d = Vector2.Zero;
            bool keyboard = false;
            if (KeyBindings.IsDown(GameAction.MoveUp)) { d.Y -= 1f; keyboard = true; }
            if (KeyBindings.IsDown(GameAction.MoveDown)) { d.Y += 1f; keyboard = true; }
            if (KeyBindings.IsDown(GameAction.MoveLeft)) { d.X -= 1f; keyboard = true; }
            if (KeyBindings.IsDown(GameAction.MoveRight)) { d.X += 1f; keyboard = true; }

            if (!keyboard && TryGetPrimaryGamepadIndex(out int gp))
            {
                float ax = Raylib.GetGamepadAxisMovement(gp, GamepadAxis.LeftX);
                float ay = Raylib.GetGamepadAxisMovement(gp, GamepadAxis.LeftY);
                if (MathF.Sqrt(ax * ax + ay * ay) > 0.15f) d = new Vector2(ax, ay);
            }
            return d;
        }

        /// <summary>Clé de tri en Y, équivalente à celle d'un joueur dont les pieds seraient sur la planche.</summary>
        public static float GetSkateboardSortY(Skateboard sb)
            => sb.Position.Y - GetPlayerFeetPosition(Vector2.Zero).Y + FeetOffsetY - 0.5f;

        /// <summary>
        /// Collision du monde pour la planche, identique à celle du joueur : on teste World.IsColliding
        /// avec la hitbox du joueur, placé là où ses pieds seraient sur la planche.
        /// Retourne false tant que la hitbox du joueur n'est pas connue (on retombe alors sur le test par tuiles).
        /// </summary>
        public static bool TryWorldCollisionAt(Vector2 boardPos, HashSet<string> destroyed, out bool blocked)
        {
            blocked = false;
            if (_skateHitboxDim == Vector2.Zero) return false;
            Vector2 asPlayerPos = boardPos - GetPlayerFeetPosition(Vector2.Zero);
            blocked = World.IsColliding(asPlayerPos, _skateHitboxDim, destroyed);
            return true;
        }

        // ---------- PLANCHES ABANDONNÉES (appelé chaque frame, à côté de la boucle des voitures) ----------

        public static void UpdateLooseSkateboards(float dt, Vector2 hitboxDim = default)
        {
            if (hitboxDim != Vector2.Zero) _skateHitboxDim = hitboxDim;
            foreach (var sb in Skateboards)
            {
                if (sb.IsRidden) continue; // déjà mise à jour par UpdateSkateboardRide
                if (sb.Velocity == Vector2.Zero) continue;
                sb.Update(dt, destroyedObjects, Vector2.Zero);
            }
        }

        // ---------- SAUVEGARDE ----------

        public static List<SkateboardSaveData> GetSkateboardsSaveData()
            => Skateboards.Select(s => s.GetSaveData()).ToList();

        public static void LoadSkateboardsSaveData(IEnumerable<SkateboardSaveData>? data)
        {
            PlayerSkateboard = null;
            Skateboards.Clear();
            if (data == null) return;
            foreach (var d in data) Skateboards.Add(Skateboard.FromSaveData(d));
        }
    }
}
