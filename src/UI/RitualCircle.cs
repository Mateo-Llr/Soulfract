// RitualCircle.cs - Interaction directe avec le cercle de rituel (plus de menu : on dépose les
// offrandes directement sur les pointes de l'étoile, en jeu, puis on invoque un démon).
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public enum RitualAnimationState
    {
        None,
        ItemsLevitate,
        ItemsGather,
        ItemSpheres,
        BloodDrain,
        Explosion,
        DemonRise
    }

    public class RitualAnimationItem
    {
        public Item Item;
        public Vector2 StartPosWorld;
        public Vector2 CenterPosWorld;
        public Vector2 CurrentPosWorld;
        public float Progress;
        public float Speed = 3f;
        public float Angle;
        public float OrbitalRadius = 50f;
        public bool IsCollected = false;
        public float Rotation = 0f;
        public float Scale = 0f;
        public float TrailTimer = 0f;
        public float SwayPhase;

        public RitualAnimationItem(Item item, Vector2 startWorld, Vector2 centerWorld)
        {
            Item = item;
            StartPosWorld = startWorld;
            CenterPosWorld = centerWorld;
            CurrentPosWorld = startWorld;
            Progress = 0f;
            Angle = 0f;
            SwayPhase = (float)(new Random().NextDouble() * Math.PI * 2);
        }
    }

    public static class RitualCircleUI
    {
        private const int SLOT_COUNT = 5;                   // 5 bras d'étoile = 5 slots d'offrande
        private const float STAR_RADIUS_WORLD = 55f;         // distance du centre du cercle à chaque pointe
        private const float HOVER_RADIUS_WORLD = 100f;       // rayon (monde) de détection du survol souris
        private const float INTERACT_RADIUS_WORLD = 150f;    // distance max joueur <-> cercle pour interagir
        private const float SLOT_HIT_RADIUS_SCREEN = 22f;    // rayon de clic (écran) sur un point d'offrande
        private const int ICON_SIZE = 28;
        private const float RENDER_ELEVATION_WORLD = Program.TileSize * 2f; // les offrandes/l'UI du cercle flottent 2 tuiles au-dessus du sol
        private const float ITEM_VISUAL_ELEVATION_WORLD = Program.TileSize * 2f; // les items déposés sont dessinés 2 tuiles plus haut que leur point d'ancrage réel

        private static readonly Color COLOR_ACCENT = new Color(180, 100, 80, 255);
        private static readonly Color COLOR_BUTTON = new Color(150, 50, 80, 255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color(200, 80, 100, 255);
        private static readonly Color COLOR_BUTTON_DISABLED = new Color(80, 50, 60, 200);
        private static readonly Color COLOR_COUNTER_BG = new Color(20, 15, 25, 200);

        // Offrandes déposées, par cercle actif (clé = position en tuile du cercle)
        private static Dictionary<(int x, int y), Item?[]> _offerings = new();

        // Cercle actuellement survolé par la souris (pour afficher compteur + bouton SUMMON)
        private static (int x, int y)? _hoveredTile = null;
        private static Vector2 _hoveredCenterWorld;

        // Animation d'invocation
        private static RitualAnimationState _animationState = RitualAnimationState.None;
        private static float _animationTimer = 0f;
        private static float _animationSpeed = 1f;
        private static List<RitualAnimationItem> _animItems = new();
        private static List<Particle> _bloodParticles = new();
        private static List<Particle> _sparkParticles = new();
        private static Vector2 _ritualWorldPos = Vector2.Zero;      // position AU SOL (spawn du démon, chunk...)
        private static Vector2 _ritualAnimWorldPos = Vector2.Zero;  // position ÉLEVÉE utilisée pour tout le rendu visuel de l'animation
        private static bool _craftingInProgress = false;
        private static float _demonRiseBob = 0f;
        private static (int x, int y) _summoningTile;
        private static float _coreCharge = 0f;       // 0..1, intensité du noyau d'énergie au centre du cercle
        private static float _shockwaveRadius = 0f;  // rayon de l'onde de choc de l'explosion
        private static float _flashAlpha = 0f;        // flash d'impact au moment de l'explosion

        // Conservé pour compatibilité (permet à IsMouseOverAnyUI de savoir si le bouton bloque le clic)
        public static bool IsOpen => _craftingInProgress;

        public static void Initialize(DraggedItem? sharedDraggedItem = null)
        {
            _offerings.Clear();
            _hoveredTile = null;
            _craftingInProgress = false;
            _animationState = RitualAnimationState.None;
            _animItems.Clear();
            _bloodParticles.Clear();
            _sparkParticles.Clear();
            _coreCharge = 0f;
            _shockwaveRadius = 0f;
            _flashAlpha = 0f;
        }

        // ==================== HELPERS ====================

        private static Item?[] GetOrCreateOfferings((int x, int y) tile)
        {
            if (!_offerings.TryGetValue(tile, out var arr))
            {
                arr = new Item?[SLOT_COUNT];
                _offerings[tile] = arr;
            }
            return arr;
        }

        private static int CountFilled(Item?[] arr)
        {
            int n = 0;
            foreach (var it in arr) if (it != null) n++;
            return n;
        }

        // Le cercle de rituel est une grosse tuile de 5x5, mais son ancre logique (position renvoyée
        // par World.GetActiveRitualCircleTiles) correspond à la rangée du BAS du sprite : celui-ci
        // s'étend visuellement vers le haut depuis cette ancre (voir World.DrawWorld, objet 4000).
        // On décale donc le centre "logique" utilisé pour le survol/l'interaction de 2 tuiles vers le
        // haut, afin qu'il tombe au milieu des 4 rangées du HAUT du sprite plutôt que des 4 du bas.
        private const float CIRCLE_CENTER_Y_OFFSET_TILES = 2f;

        private static Vector2 GetTileCenterWorld((int x, int y) tile)
        {
            int ts = Program.TileSize;
            return new Vector2(tile.x * ts + ts / 2f, tile.y * ts + ts / 2f - CIRCLE_CENTER_Y_OFFSET_TILES * ts);
        }

        // Les 5 pointes de l'étoile, en coordonnées monde, autour du centre du cercle
        // Les points situés sous le centre sont légèrement "remontés" (écrasement vertical) pour
        // éviter que les deux pointes du bas de l'étoile ne paraissent trop basses par rapport au cercle.
        private const float STAR_VERTICAL_SQUASH = 0.6f;

        private static Vector2[] GetStarPointsWorld(Vector2 center)
        {
            var pts = new Vector2[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                float angle = -MathF.PI / 2f + i * (MathF.PI * 2f / SLOT_COUNT);
                float sin = MathF.Sin(angle);
                float squashedSin = sin > 0f ? sin * STAR_VERTICAL_SQUASH : sin;
                pts[i] = center + new Vector2(MathF.Cos(angle) * STAR_RADIUS_WORLD, squashedSin * STAR_RADIUS_WORLD);
            }
            return pts;
        }

        // Position visuelle réelle des points d'offrande : les items sont dessinés 2 tuiles plus haut
        // que leur point d'ancrage (utilisé pour le tri par Y et l'interaction "logique"), pour bien
        // flotter au-dessus du sol tout en gardant une profondeur de tri cohérente avec leur tuile.
        private static Vector2[] GetElevatedStarPointsWorld(Vector2 center)
        {
            var pts = GetStarPointsWorld(center);
            var elevation = new Vector2(0, -ITEM_VISUAL_ELEVATION_WORLD);
            for (int i = 0; i < pts.Length; i++) pts[i] += elevation;
            return pts;
        }

        private static Item? GetHeldItem()
        {
            if (Program.hotbarSlot < 0) return null;
            return InventoryRenderer.GetHotbarItem(Program.hotbarSlot);
        }

        private static void ConsumeOneHeldItem()
        {
            var slots = InventoryRenderer.InventorySlots;
            if (Program.hotbarSlot >= 0 && Program.hotbarSlot < slots.Count)
            {
                var slot = slots[Program.hotbarSlot];
                if (!slot.IsEmpty)
                {
                    slot.Count--;
                    if (slot.Count <= 0) slot.Clear();
                }
            }
        }

        // Le bouton est dessiné en coordonnées MONDE (voir Draw()), donc sa taille "physique" est
        // exprimée en unités monde ; le test de clic doit lui aussi se faire en coordonnées monde
        // pour rester juste quel que soit le niveau de zoom de la caméra.
        private const float SUMMON_BTN_WORLD_W = 140f;
        private const float SUMMON_BTN_WORLD_H = 38f;

        private static Rectangle GetSummonButtonRect(Vector2 centerWorld)
        {
            return new Rectangle(centerWorld.X - SUMMON_BTN_WORLD_W / 2f, centerWorld.Y + 62, SUMMON_BTN_WORLD_W, SUMMON_BTN_WORLD_H);
        }

        public static bool IsMouseOverButton(Vector2 mouseScreen)
        {
            if (_craftingInProgress || _hoveredTile == null) return false;
            Camera2D camera = Program.GetCurrentCamera();
            Vector2 centerWorld = _hoveredCenterWorld + new Vector2(0, -RENDER_ELEVATION_WORLD);
            Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouseScreen, camera);
            Rectangle summonBtn = GetSummonButtonRect(centerWorld);
            return Raylib.CheckCollisionPointRec(mouseWorld, summonBtn);
        }

        // ==================== UPDATE ====================

        public static void Update()
        {
            if (_craftingInProgress)
            {
                UpdateRitualAnimation();
                return;
            }

            var circleTiles = World.GetActiveRitualCircleTiles();
            if (circleTiles.Count == 0)
            {
                _hoveredTile = null;
                return;
            }

            Camera2D camera = Program.GetCurrentCamera();
            Vector2 mouseScreen = Raylib.GetMousePosition();
            Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouseScreen, camera);
            Vector2 playerPos = Program.GetPlayerPosition();

            // Trouver le cercle le plus proche du curseur (s'il est dans le rayon de survol)
            (int x, int y)? best = null;
            float bestDist = float.MaxValue;
            foreach (var tile in circleTiles)
            {
                Vector2 c = GetTileCenterWorld(tile);
                float d = Vector2.Distance(mouseWorld, c);
                if (d <= HOVER_RADIUS_WORLD && d < bestDist)
                {
                    bestDist = d;
                    best = tile;
                }
            }
            _hoveredTile = best;
            if (best == null) return;

            var tileVal = best.Value;
            _hoveredCenterWorld = GetTileCenterWorld(tileVal);
            var offerings = GetOrCreateOfferings(tileVal);
            Vector2 elevation = new Vector2(0, -RENDER_ELEVATION_WORLD);
            var starPoints = GetElevatedStarPointsWorld(_hoveredCenterWorld);

            bool canInteract = Vector2.Distance(playerPos, _hoveredCenterWorld) <= INTERACT_RADIUS_WORLD;
            if (!canInteract) return;

            // ---- Clic droit sur un point rempli : on récupère l'item ----
            // (les items sont dessinés 2 tuiles plus haut : le clic vise leur vraie position visuelle)
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                for (int i = 0; i < SLOT_COUNT; i++)
                {
                    if (offerings[i] == null) continue;
                    Vector2 screenPt = Raylib.GetWorldToScreen2D(starPoints[i], camera);
                    if (Vector2.Distance(mouseScreen, screenPt) <= SLOT_HIT_RADIUS_SCREEN)
                    {
                        var item = offerings[i]!;
                        Program.AddItemToInventory(item, 1);
                        offerings[i] = null;
                        break;
                    }
                }
            }

            // ---- Touche E avec un item en main : remplit le point vide le plus proche du curseur ----
            if (Program.KeyBindings.IsPressed(Program.GameAction.Interact))
            {
                var heldItem = GetHeldItem();
                if (heldItem != null)
                {
                    int bestSlot = -1;
                    float bestSlotDist = float.MaxValue;
                    for (int i = 0; i < SLOT_COUNT; i++)
                    {
                        if (offerings[i] != null) continue;
                        Vector2 screenPt = Raylib.GetWorldToScreen2D(starPoints[i], camera);
                        float d = Vector2.Distance(mouseScreen, screenPt);
                        if (d < bestSlotDist)
                        {
                            bestSlotDist = d;
                            bestSlot = i;
                        }
                    }
                    if (bestSlot != -1)
                    {
                        offerings[bestSlot] = new Item(heldItem.Name, 1, heldItem.BaseColor, heldItem.Icon, heldItem.CustomColor);
                        ConsumeOneHeldItem();
                    }
                }
            }

            // ---- Bouton SUMMON ----
            int filled = CountFilled(offerings);
            Vector2 centerWorldBtn = _hoveredCenterWorld + elevation;
            Rectangle summonBtn = GetSummonButtonRect(centerWorldBtn);
            bool hoverBtn = Raylib.CheckCollisionPointRec(mouseWorld, summonBtn);
            if (hoverBtn && filled >= SLOT_COUNT && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                StartRitual(tileVal);
            }
        }

        // ==================== DRAW ====================

        // À appeler AVANT World.DrawWorld() (donc avant BeginMode2D côté appelant, ou juste après :
        // peu importe, seul l'ordre par rapport à DrawWorld compte) : les offrandes posées sont
        // injectées dans le tri par Y du monde, pour apparaître comme de vrais objets au sol
        // (devant/derrière le joueur et les autres éléments selon leur profondeur), au lieu d'être
        // plaquées par-dessus dans une passe séparée.
        public static void QueueGroundItems()
        {
            foreach (var kvp in _offerings)
            {
                if (_craftingInProgress && kvp.Key.Equals(_summoningTile)) continue;
                Vector2 center = GetTileCenterWorld(kvp.Key);
                var groundPts = GetStarPointsWorld(center);
                var elevatedPts = GetElevatedStarPointsWorld(center);
                for (int i = 0; i < SLOT_COUNT; i++)
                {
                    var item = kvp.Value[i];
                    if (item == null) continue;
                    Vector2 drawPt = elevatedPts[i];
                    float sortY = groundPts[i].Y;
                    World.QueueExternalRenderItem(sortY, () => DrawOfferingItem(drawPt, item));
                }
            }
        }

        // Doit être appelée DANS BeginMode2D (coordonnées monde) : le rendu du cercle de rituel
        // fait maintenant partie du monde du jeu (comme les icônes d'interaction sur les tuiles),
        // et non plus d'une passe d'interface plaquée par-dessus après EndMode2D.
        public static void Draw()
        {
            Vector2 elevation = new Vector2(0, -RENDER_ELEVATION_WORLD);

            if (_craftingInProgress)
            {
                DrawRitualAnimation();
                return;
            }

            if (_hoveredTile == null) return;

            var offerings = GetOrCreateOfferings(_hoveredTile.Value);
            int filled = CountFilled(offerings);
            Vector2 centerWorld = _hoveredCenterWorld + elevation;

            // Compteur X/5 au-dessus du cercle
            string counter = $"{filled}/{SLOT_COUNT}";
            int cw = FontManager.MeasureText(counter, 22);
            Raylib.DrawRectangleRounded(new Rectangle(centerWorld.X - cw / 2f - 12, centerWorld.Y - 100, cw + 24, 34), 0.3f, 6, COLOR_COUNTER_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(centerWorld.X - cw / 2f - 12, centerWorld.Y - 100, cw + 24, 34), 0.3f, 6, 1, COLOR_ACCENT);
            FontManager.DrawText(counter, (int)(centerWorld.X - cw / 2f), (int)centerWorld.Y - 93, 22, filled >= SLOT_COUNT ? new Color(255, 130, 130, 255) : Color.White);

            // Bouton SUMMON en dessous du cercle (coordonnées monde, y compris pour le survol :
            // ça reste juste quel que soit le zoom de la caméra)
            Rectangle btn = GetSummonButtonRect(centerWorld);
            Camera2D camera = Program.GetCurrentCamera();
            Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
            bool hoverBtn = Raylib.CheckCollisionPointRec(mouseWorld, btn);
            bool canSummon = filled >= SLOT_COUNT;
            Color btnColor = !canSummon ? COLOR_BUTTON_DISABLED : (hoverBtn ? COLOR_BUTTON_HOVER : COLOR_BUTTON);

            Raylib.DrawRectangleRounded(new Rectangle(btn.X + 2, btn.Y + 2, btn.Width, btn.Height), 0.25f, 8, new Color(0, 0, 0, 100));
            Raylib.DrawRectangleRounded(btn, 0.25f, 8, btnColor);
            Raylib.DrawRectangleRoundedLines(btn, 0.25f, 8, 2, canSummon ? COLOR_ACCENT : new Color(120, 80, 90, 150));

            string btnText = canSummon ? " INVOQUER" : "SUMMON";
            int tw = FontManager.MeasureText(btnText, 16);
            FontManager.DrawText(btnText, (int)(btn.X + (btn.Width - tw) / 2f), (int)(btn.Y + 11), 16, Color.White);
        }

        // Dessine l'item directement au sol, sans "case"/cercle autour : il doit se fondre dans le
        // décor comme n'importe quel objet posé au sol (feuille, outil tombé, etc.).
        private static void DrawOfferingItem(Vector2 worldPt, Item item)
        {
            int size = ICON_SIZE;
            ItemRenderer.DrawItemPadded(item, (int)(worldPt.X - size / 2f), (int)(worldPt.Y - size / 2f), size, 2);
        }

        // ==================== INVOCATION ====================

        private static void StartRitual((int x, int y) tile)
        {
            var offerings = GetOrCreateOfferings(tile);
            if (CountFilled(offerings) < SLOT_COUNT) return;

            _summoningTile = tile;
            _ritualWorldPos = GetTileCenterWorld(tile);
            _ritualAnimWorldPos = _ritualWorldPos + new Vector2(0, -RENDER_ELEVATION_WORLD);
            _craftingInProgress = true;
            _animationState = RitualAnimationState.ItemsLevitate;
            _animationTimer = 0f;
            _animItems.Clear();
            _bloodParticles.Clear();
            _sparkParticles.Clear();
            _demonRiseBob = 0f;
            _coreCharge = 0f;
            _shockwaveRadius = 0f;
            _flashAlpha = 0f;

            var starPoints = GetStarPointsWorld(_ritualAnimWorldPos);
            const float ORBIT_RADIUS = 38f; // rayon d'orbite unique pour tous les items : cohérent, pas de "saut" à une distance arbitraire
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                var item = offerings[i];
                if (item == null) continue;
                Vector2 startWorld = starPoints[i];
                // Angle basé sur la position réelle du bras d'étoile de l'item (et non tiré au hasard) :
                // chaque item glisse vers le centre en suivant sa propre ligne radiale, au lieu de
                // sauter vers un point aléatoire ailleurs sur le cercle, ce qui rendait le rassemblement
                // incohérent (certains items semblaient parcourir une distance bien plus grande que d'autres).
                float orbAngle = -MathF.PI / 2f + i * (MathF.PI * 2f / SLOT_COUNT);
                float orbRadius = ORBIT_RADIUS;
                Vector2 orbWorld = _ritualAnimWorldPos + new Vector2(MathF.Cos(orbAngle), MathF.Sin(orbAngle)) * orbRadius;
                var animItem = new RitualAnimationItem(item, startWorld, orbWorld);
                animItem.Angle = orbAngle;
                animItem.OrbitalRadius = orbRadius;
                _animItems.Add(animItem);
            }

            // Les offrandes sont consommées dès le lancement du rituel ; le cercle redevient libre
            _offerings[tile] = new Item?[SLOT_COUNT];
        }

        private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);
        private static float EaseInOutSine(float t) { t = Math.Clamp(t, 0f, 1f); return -(MathF.Cos(MathF.PI * t) - 1f) / 2f; }

        private static void UpdateRitualAnimation()
        {
            float dt = Raylib.GetFrameTime();
            _animationTimer += dt * _animationSpeed;
            switch (_animationState)
            {
                case RitualAnimationState.ItemsLevitate:
                    bool allFloating = true;
                    foreach (var item in _animItems)
                    {
                        item.SwayPhase += dt * 2.5f;
                        if (item.Progress < 1f)
                        {
                            item.Progress += dt * 1.8f;
                            if (item.Progress > 1f) item.Progress = 1f;
                            float eased = EaseOutCubic(item.Progress);
                            float yOffset = -eased * 46f;
                            float sway = MathF.Sin(item.SwayPhase) * 4f * eased;
                            item.CurrentPosWorld = new Vector2(item.StartPosWorld.X + sway, item.StartPosWorld.Y + yOffset);
                            item.Scale = eased;
                            item.Rotation = MathF.Sin(item.SwayPhase * 0.6f) * 8f;
                            allFloating = false;
                        }
                        else
                        {
                            // petit flottement une fois levée, pour éviter que les items restent figés
                            float bob = MathF.Sin(item.SwayPhase) * 3f;
                            item.CurrentPosWorld = new Vector2(item.StartPosWorld.X + MathF.Sin(item.SwayPhase) * 4f,
                                item.StartPosWorld.Y - 46f + bob);
                            item.Rotation = MathF.Sin(item.SwayPhase * 0.6f) * 8f;
                        }
                        item.TrailTimer -= dt;
                        if (item.TrailTimer <= 0f)
                        {
                            item.TrailTimer = 0.05f;
                            _sparkParticles.Add(new Particle(item.CurrentPosWorld, new Vector2(0, -8f), new Color(220, 160, 120, 200), 2, 0.4f));
                        }
                    }
                    _coreCharge = MathHelper.Lerp(_coreCharge, 0.15f, dt * 2f);
                    if (allFloating && _animationTimer > 0.9f)
                    {
                        _animationState = RitualAnimationState.ItemsGather;
                        _animationTimer = 0f;
                        foreach (var item in _animItems) item.Progress = 0f;
                    }
                    break;

                case RitualAnimationState.ItemsGather:
                    bool allGathered = true;
                    foreach (var item in _animItems)
                    {
                        if (item.IsCollected) continue;
                        allGathered = false;
                        item.Progress = Math.Min(1f, item.Progress + dt * 2.2f);
                        float t = EaseInOutSine(item.Progress);

                        // Trajectoire courbe (arc) plutôt qu'une ligne droite : plus lisible et plus élégant
                        Vector2 origin = new Vector2(item.StartPosWorld.X, item.StartPosWorld.Y - 46f);
                        Vector2 target = item.CenterPosWorld;
                        Vector2 straight = Vector2.Lerp(origin, target, t);
                        Vector2 perp = new Vector2(-(target.Y - origin.Y), target.X - origin.X);
                        if (perp.LengthSquared() > 0.001f) perp = Vector2.Normalize(perp);
                        float arc = MathF.Sin(t * MathF.PI) * 18f;
                        item.CurrentPosWorld = straight + perp * arc;
                        item.Scale = MathHelper.Lerp(1f, 0.7f, t);
                        item.SwayPhase += dt * 5f;
                        item.Rotation = MathF.Sin(item.SwayPhase) * 10f;

                        item.TrailTimer -= dt;
                        if (item.TrailTimer <= 0f)
                        {
                            item.TrailTimer = 0.03f;
                            _sparkParticles.Add(new Particle(item.CurrentPosWorld, Vector2.Zero, new Color(220, 120, 140, 220), 2, 0.3f));
                        }

                        if (item.Progress >= 1f) item.IsCollected = true;
                    }
                    _coreCharge = MathHelper.Lerp(_coreCharge, 0.35f, dt * 3f);
                    if (allGathered && _animationTimer > 0.35f)
                    {
                        _animationState = RitualAnimationState.ItemSpheres;
                        _animationTimer = 0f;
                        Program.ApplyScreenShake(3f, 0.15f);
                    }
                    break;

                case RitualAnimationState.ItemSpheres:
                    foreach (var item in _animItems)
                    {
                        if (!item.IsCollected) continue;
                        item.Angle += dt * 4f;
                        item.CurrentPosWorld = item.CenterPosWorld + new Vector2(MathF.Cos(item.Angle), MathF.Sin(item.Angle)) * item.OrbitalRadius;
                        item.SwayPhase += dt * 4f;
                        item.Rotation = MathF.Sin(item.SwayPhase) * 10f;
                        item.Scale = 0.7f + MathF.Sin((float)Raylib.GetTime() * 6f + item.Angle) * 0.08f;

                        item.TrailTimer -= dt;
                        if (item.TrailTimer <= 0f)
                        {
                            item.TrailTimer = 0.04f;
                            _sparkParticles.Add(new Particle(item.CurrentPosWorld, Vector2.Zero, new Color(255, 140, 100, 200), 2, 0.25f));
                        }
                    }
                    if (Random.Shared.NextDouble() < 0.35)
                    {
                        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                        float radius = Random.Shared.Next(50, 90);
                        Vector2 pos = _ritualAnimWorldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                        _sparkParticles.Add(new Particle(pos, Vector2.Zero, new Color(255, 100, 100, 255), 3, 0.3f));
                    }
                    _coreCharge = MathHelper.Lerp(_coreCharge, 0.55f, dt * 2f);
                    if (_animationTimer > 1.1f)
                    {
                        _animationState = RitualAnimationState.BloodDrain;
                        _animationTimer = 0f;
                    }
                    break;

                case RitualAnimationState.BloodDrain:
                    // Les items fusionnent réellement ici : ils rétrécissent et se fondent dans le noyau
                    foreach (var item in _animItems)
                    {
                        item.Angle += dt * 6f;
                        item.OrbitalRadius = MathHelper.Lerp(item.OrbitalRadius, 6f, dt * 2.5f);
                        item.CurrentPosWorld = item.CenterPosWorld + new Vector2(MathF.Cos(item.Angle), MathF.Sin(item.Angle)) * item.OrbitalRadius;
                        item.Scale = MathHelper.Lerp(item.Scale, 0f, dt * 2.2f);
                        item.SwayPhase += dt * 6f;
                        item.Rotation = MathF.Sin(item.SwayPhase) * 14f;
                    }
                    if (Random.Shared.NextDouble() < 0.5)
                    {
                        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                        float radius = Random.Shared.Next(40, 100);
                        Vector2 pos = _ritualAnimWorldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                        Vector2 velocity = (_ritualAnimWorldPos - pos) * 2.2f;
                        _bloodParticles.Add(new Particle(pos, velocity, new Color(180, 30, 40, 220), 5, 0.5f));
                    }
                    for (int i = _bloodParticles.Count - 1; i >= 0; i--)
                    {
                        _bloodParticles[i].Update(dt);
                        if (!_bloodParticles[i].IsAlive) _bloodParticles.RemoveAt(i);
                    }
                    _coreCharge = MathHelper.Lerp(_coreCharge, 1f, dt * 1.3f);
                    if (_animationTimer > 1.4f)
                    {
                        _animationState = RitualAnimationState.Explosion;
                        _animationTimer = 0f;
                        _shockwaveRadius = 0f;
                        _flashAlpha = 1f;
                    }
                    break;

                case RitualAnimationState.Explosion:
                    if (_animationTimer < 0.03f)
                    {
                        for (int i = 0; i < 50; i++)
                        {
                            float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                            float speed = Random.Shared.Next(120, 340);
                            Vector2 vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                            Color color = new Color(255, 50 + Random.Shared.Next(0, 130), 40 + Random.Shared.Next(0, 60), 255);
                            Program.GetParticleList().Add(new Particle(_ritualAnimWorldPos, vel, color, 6, 0.55f));
                        }
                        Program.ApplyScreenShake(15f, 0.4f);
                    }
                    _shockwaveRadius += dt * 340f;
                    _flashAlpha = MathHelper.Lerp(_flashAlpha, 0f, dt * 9f);
                    _bloodParticles.Clear();
                    _animItems.Clear();
                    if (_animationTimer > 0.5f)
                    {
                        _animationState = RitualAnimationState.DemonRise;
                        _animationTimer = 0f;
                    }
                    break;

                case RitualAnimationState.DemonRise:
                    _demonRiseBob += dt * 8f;
                    _coreCharge = MathHelper.Lerp(_coreCharge, 0f, dt * 3f);
                    if (_animationTimer > 0.8f)
                    {
                        SpawnDemon(_ritualAnimWorldPos);
                        _craftingInProgress = false;
                        _animationState = RitualAnimationState.None;
                        _animItems.Clear();
                        _bloodParticles.Clear();
                        _sparkParticles.Clear();
                        _coreCharge = 0f;
                        _shockwaveRadius = 0f;
                        _flashAlpha = 0f;
                    }
                    break;
            }
        }

        //  Fait apparaître le démon invoqué à la place du "craft" d'autrefois.
        private static void SpawnDemon(Vector2 worldPos)
        {
            int ts = Program.TileSize;
            int tileX = (int)(worldPos.X / ts);
            int tileY = (int)(worldPos.Y / ts);
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            if (groundId == 10 || groundId == 11)
            {
                Program.AddNotification(new Notification("Le rituel a échoué : impossible d'invoquer dans l'eau.", new Color(200, 60, 220, 255), 3f));
                return;
            }

            var demon = new Entity(worldPos, "Demon", false);
            demon.Behavior = "hostile";
            Program.GetEntities().Add(demon);

            var chunk = World.GetChunkAt(tileX, tileY);
            if (chunk != null) chunk.Entities.Add(demon);

            Program.AddNotification(new Notification(" Le rituel est accompli... un démon a répondu à l'appel !", new Color(200, 60, 220, 255), 3f));
        }

        private static void DrawRitualAnimation()
        {
            Vector2 centerWorld = _ritualAnimWorldPos;
            float time = (float)Raylib.GetTime();

            bool showCircle = _animationState != RitualAnimationState.DemonRise;
            if (showCircle)
            {
                // Halo doux qui grossit avec la charge accumulée (plus discret au début, plus intense
                // juste avant l'explosion), pour donner une vraie sensation de montée en puissance.
                float haloR = 70f + _coreCharge * 35f;
                Color haloOuter = new Color((byte)200, (byte)70, (byte)70, (byte)(40 + _coreCharge * 60));
                Color haloInner = new Color((byte)220, (byte)90, (byte)70, (byte)0);
                Raylib.DrawCircleGradient((int)centerWorld.X, (int)centerWorld.Y, haloR, haloOuter, haloInner);

                // Anneau extérieur pulsant
                for (int i = 0; i < 3; i++)
                {
                    float pulse = 0.8f + 0.2f * MathF.Sin(time * 5f + i);
                    float radius = (60 + i * 15) * pulse;
                    Raylib.DrawCircleLines((int)centerWorld.X, (int)centerWorld.Y, radius, new Color(180, 80, 100, (int)(150 - i * 30)));
                }

                // Anneau de runes tournant, en pointillés, pour un look "cercle magique" plus lisible
                const int RUNE_TICKS = 16;
                float runeRadius = 42f;
                for (int i = 0; i < RUNE_TICKS; i++)
                {
                    float a = time * 0.8f + i * (MathF.PI * 2f / RUNE_TICKS);
                    Vector2 p1 = centerWorld + new Vector2(MathF.Cos(a), MathF.Sin(a)) * runeRadius;
                    Vector2 p2 = centerWorld + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (runeRadius + 7f);
                    Raylib.DrawLineEx(p1, p2, 2f, new Color(220, 140, 110, 200));
                }

                // Noyau d'énergie au centre : grossit et s'éclaircit avec _coreCharge, c'est lui qui
                // "reçoit" visuellement les items qui fusionnent avant de tout faire exploser.
                if (_coreCharge > 0.01f)
                {
                    float coreR = 8f + _coreCharge * 22f;
                    float flicker = 0.9f + 0.1f * MathF.Sin(time * 14f);
                    Color coreOuter = new Color((byte)255, (byte)110, (byte)90, (byte)(_coreCharge * 220));
                    Color coreInner = new Color((byte)255, (byte)230, (byte)200, (byte)(_coreCharge * 255));
                    Raylib.DrawCircleGradient((int)centerWorld.X, (int)centerWorld.Y, coreR * flicker, coreInner, coreOuter);
                }
            }

            foreach (var particle in _bloodParticles)
            {
                byte alpha = (byte)(particle.GetAlpha() * 255);
                Raylib.DrawCircle((int)particle.Position.X, (int)particle.Position.Y, particle.Size, new Color(particle.Color.R, particle.Color.G, particle.Color.B, alpha));
            }

            foreach (var particle in _sparkParticles)
            {
                particle.Update(Raylib.GetFrameTime());
                if (particle.IsAlive)
                {
                    byte alpha = (byte)(particle.GetAlpha() * 255);
                    Raylib.DrawCircle((int)particle.Position.X, (int)particle.Position.Y, particle.Size, new Color(particle.Color.R, particle.Color.G, particle.Color.B, alpha));
                }
            }
            _sparkParticles.RemoveAll(p => !p.IsAlive);

            // Les items en cours de fusion : halo doux + icône tournante à l'échelle, visibles à
            // chaque étape (lévitation, rassemblement, orbite ET fusion finale dans le noyau).
            foreach (var item in _animItems)
            {
                if (item.Scale <= 0.01f) continue;
                Vector2 worldPos = item.CurrentPosWorld;
                float size = 28f * item.Scale;

                Raylib.DrawCircleGradient((int)worldPos.X, (int)worldPos.Y, size * 0.85f,
                    new Color((byte)255, (byte)150, (byte)120, (byte)(120 * item.Scale)), new Color((byte)255, (byte)150, (byte)120, (byte)0));

                if (item.Item.Icon.Id != 0)
                {
                    var src = new Rectangle(0, 0, item.Item.Icon.Width, item.Item.Icon.Height);
                    var dst = new Rectangle(worldPos.X, worldPos.Y, size, size);
                    Raylib.DrawTexturePro(item.Item.Icon, src, dst, new Vector2(size / 2f, size / 2f), item.Rotation, Color.White);
                }
                else
                {
                    Raylib.DrawRectanglePro(new Rectangle(worldPos.X, worldPos.Y, size, size),
                        new Vector2(size / 2f, size / 2f), item.Rotation, item.Item.DisplayColor);
                }
            }

            // Onde de choc + flash au moment de l'explosion
            if (_animationState == RitualAnimationState.Explosion)
            {
                if (_shockwaveRadius > 0f)
                {
                    float shockAlpha = Math.Clamp(1f - _shockwaveRadius / 140f, 0f, 1f);
                    Raylib.DrawCircleLines((int)centerWorld.X, (int)centerWorld.Y, _shockwaveRadius, new Color((byte)255, (byte)200, (byte)150, (byte)(shockAlpha * 200)));
                    Raylib.DrawCircleLines((int)centerWorld.X, (int)centerWorld.Y, _shockwaveRadius * 0.7f, new Color((byte)255, (byte)120, (byte)90, (byte)(shockAlpha * 160)));
                }
                if (_flashAlpha > 0.01f)
                {
                    Raylib.DrawCircleGradient((int)centerWorld.X, (int)centerWorld.Y, 90f,
                        new Color((byte)255, (byte)240, (byte)220, (byte)(_flashAlpha * 255)), new Color((byte)255, (byte)240, (byte)220, (byte)0));
                }
            }

            // Le démon qui s'élève du sol, en lueur, juste avant d'apparaître pour de bon
            if (_animationState == RitualAnimationState.DemonRise)
            {
                float t = _animationTimer;
                float riseHeight = -60f * Math.Min(1f, t / 0.8f);
                float bob = MathF.Sin(_demonRiseBob) * 4f;
                Vector2 pos = new Vector2(centerWorld.X, centerWorld.Y + riseHeight + bob);
                float glowAlpha = Math.Min(1f, t / 0.3f);
                Color glow = new Color((byte)200, (byte)40, (byte)220, (byte)(glowAlpha * 220));
                Raylib.DrawCircleGradient((int)pos.X, (int)pos.Y, 32, glow, new Color((byte)200, (byte)40, (byte)220, (byte)0));
                Raylib.DrawCircle((int)pos.X, (int)pos.Y, 14, new Color((byte)30, (byte)10, (byte)20, (byte)(glowAlpha * 255)));
            }
        }
    }
}