// Car.cs - Entité voiture avec hitbox et collisions avec les entités
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public class Car
    {
        public Vector2 Position { get; set; }
        public float Angle { get; set; } // degrés
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float AngularVelocity { get; set; } // degrés par seconde
        public float WheelRotation { get; set; } // angle de braquage des roues avant
        public float WheelSpin { get; set; } // rotation des roues

        // Constantes physiques - version rapide et dynamique
        private const float ACCEL = 900f;
        private const float FRICTION = 0.94f;
        public const float MAX_SPEED = 450f;
        private const float ANGULAR_ACCEL = 450f;
        private const float MAX_ANGULAR_SPEED = 200f;
        private const float TURN_SPEED = 360f;
        private const float ANGULAR_FRICTION = 0.88f;
        public const float CAR_WIDTH = 48f;
        public const float CAR_HEIGHT = 96f;

        // Référence au joueur qui conduit (null si libre)
        public Entity? Driver { get; set; }

        public bool IsOccupied => Driver != null;

        public Car(Vector2 pos, float angle = 0f)
        {
            Position = pos;
            Angle = angle;
            VelocityX = 0f;
            VelocityY = 0f;
            AngularVelocity = 0f;
            WheelRotation = 0f;
            WheelSpin = 0f;
        }

        /// <summary>
        /// Retourne les quatre coins de la voiture orientée selon la position et l'angle donnés.
        /// </summary>
        public Vector2[] GetCorners(Vector2 pos, float angle)
        {
            float rad = angle * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            Vector2 half = new Vector2(CAR_WIDTH / 2, CAR_HEIGHT / 2);
            Vector2[] corners = new Vector2[4];
            corners[0] = new Vector2(-half.X, -half.Y);
            corners[1] = new Vector2( half.X, -half.Y);
            corners[2] = new Vector2( half.X,  half.Y);
            corners[3] = new Vector2(-half.X,  half.Y);
            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].X * cos - corners[i].Y * sin;
                float y = corners[i].X * sin + corners[i].Y * cos;
                corners[i] = new Vector2(pos.X + x, pos.Y + y);
            }
            return corners;
        }

        /// <summary>
        /// Teste l'intersection entre un rectangle orienté (défini par ses quatre coins) et une AABB.
        /// Rendue publique et statique pour utilisation dans World.
        /// </summary>
        public static bool IntersectOrientedRectangleWithAABB(Vector2[] corners, Rectangle aabb)
        {
            // Vérifier si un coin du rectangle orienté est dans l'AABB
            foreach (var c in corners)
                if (c.X >= aabb.X && c.X <= aabb.X + aabb.Width &&
                    c.Y >= aabb.Y && c.Y <= aabb.Y + aabb.Height)
                    return true;

            // Vérifier si un coin de l'AABB est dans le rectangle orienté
            Vector2[] aabbCorners = new Vector2[]
            {
                new Vector2(aabb.X, aabb.Y),
                new Vector2(aabb.X + aabb.Width, aabb.Y),
                new Vector2(aabb.X + aabb.Width, aabb.Y + aabb.Height),
                new Vector2(aabb.X, aabb.Y + aabb.Height)
            };
            foreach (var c in aabbCorners)
                if (IsPointInOrientedRectangle(c, corners))
                    return true;

            // Test des axes séparateurs (approximation suffisante pour des rectangles)
            // Vérification rapide des boîtes englobantes
            float carMinX = corners.Min(c => c.X);
            float carMaxX = corners.Max(c => c.X);
            float carMinY = corners.Min(c => c.Y);
            float carMaxY = corners.Max(c => c.Y);
            if (carMaxX < aabb.X || carMinX > aabb.X + aabb.Width ||
                carMaxY < aabb.Y || carMinY > aabb.Y + aabb.Height)
                return false;

            return true;
        }

        private static bool IsPointInOrientedRectangle(Vector2 p, Vector2[] corners)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i];
                Vector2 b = corners[(i + 1) % 4];
                Vector2 ab = b - a;
                Vector2 ap = p - a;
                float cross = ab.X * ap.Y - ab.Y * ap.X;
                if (cross < 0) return false;
            }
            return true;
        }

        public void Update(float dt, HashSet<string> destroyedObjects)
        {
            if (Driver == null) return;

            bool isPlayer = (Driver == Program.GetPlayerEntity());
            if (!isPlayer) return;

            bool keyUp = Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up);
            bool keyDown = Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down);
            bool keyLeft = Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left);
            bool keyRight = Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right);

            float accelInput = (keyUp ? 1f : 0f) - (keyDown ? 1f : 0f);
            bool isAccelerating = keyUp || keyDown;
            float turnInput = 0f;
            if (isAccelerating)
            {
                turnInput = (keyRight ? 1f : 0f) - (keyLeft ? 1f : 0f);
            }

            // Direction du mouvement actuel (pour détecter si on recule)
            float currentSpeed = MathF.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
            bool isMovingForward = currentSpeed > 5f && Vector2.Dot(new Vector2(VelocityX, VelocityY), new Vector2(MathF.Sin(Angle * MathF.PI / 180f), -MathF.Cos(Angle * MathF.PI / 180f))) > 0;

            // Physique linéaire
            float rad = Angle * MathF.PI / 180f;
            Vector2 dir = new Vector2(MathF.Sin(rad), -MathF.Cos(rad));

            VelocityX += dir.X * accelInput * ACCEL * dt;
            VelocityY += dir.Y * accelInput * ACCEL * dt;

            VelocityX *= FRICTION;
            VelocityY *= FRICTION;

            float speed = MathF.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
            if (speed > MAX_SPEED)
            {
                VelocityX = (VelocityX / speed) * MAX_SPEED;
                VelocityY = (VelocityY / speed) * MAX_SPEED;
            }

            // Physique angulaire avec inversion du braquage en marche arrière
            float targetAngularAccel = 0f;
            if (isAccelerating && turnInput != 0f)
            {
                float turnMultiplier = isMovingForward ? 1f : -1f;
                targetAngularAccel = turnInput * turnMultiplier * ANGULAR_ACCEL;
            }

            AngularVelocity += targetAngularAccel * dt;
            AngularVelocity *= ANGULAR_FRICTION;
            AngularVelocity = Math.Clamp(AngularVelocity, -MAX_ANGULAR_SPEED, MAX_ANGULAR_SPEED);
            Angle += AngularVelocity * dt;

            // Déplacement avec résolution de collision par axe
            Vector2 move = new Vector2(VelocityX * dt, VelocityY * dt);
            Vector2 newPos = Position;

            // Test du mouvement en X
            Vector2 testPosX = new Vector2(Position.X + move.X, Position.Y);
            if (!IsColliding(testPosX, destroyedObjects))
            {
                newPos.X = testPosX.X;
            }
            else
            {
                VelocityX = 0f; // Bloquer la vélocité X
            }

            // Test du mouvement en Y
            Vector2 testPosY = new Vector2(newPos.X, Position.Y + move.Y);
            if (!IsColliding(testPosY, destroyedObjects))
            {
                newPos.Y = testPosY.Y;
            }
            else
            {
                VelocityY = 0f; // Bloquer la vélocité Y
            }

            // Appliquer la nouvelle position
            Position = newPos;

            // ===== GESTION DES COLLISIONS AVEC LES ENTITÉS (AVEC DÉGÂTS) =====
            float currentSpeedAfterMove = MathF.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
            if (currentSpeedAfterMove > 5f) // Seulement si la voiture roule
            {
                Vector2[] carCorners = GetCorners(Position, Angle);
                foreach (var entity in Program.GetEntities())
                {
                    // Ignorer le conducteur
                    if (entity == Driver) continue;
                    if (!entity.IsAlive) continue;

                    Rectangle entityRect = World.GetEntityDamageHitbox(entity, Program.TileSize);
                    if (IntersectOrientedRectangleWithAABB(carCorners, entityRect))
                    {
                        // Direction de poussée : du centre de la voiture vers l'entité
                        Vector2 pushDir = entity.WorldPos - Position;
                        if (pushDir.LengthSquared() < 0.01f)
                            pushDir = new Vector2(1, 0);
                        pushDir = Vector2.Normalize(pushDir);

                        // ===== DÉGÂTS EN FONCTION DE LA VITESSE =====
                        float speedKmh = currentSpeedAfterMove * 0.1f;
                        int damage = (int)(speedKmh * 0.1f);
                        damage = Math.Clamp(damage, 1, 30);

                        entity.CurrentHP -= damage;
                        entity.OnHit(Position);

                        Vector2 damagePos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y - 20);
                        Program.AddFloatingDamage(damagePos, damage, damage >= 10);

                        float impulse = currentSpeedAfterMove * 0.8f;
                        entity.ApplyKnockback(pushDir, impulse);

                        // Déplacement immédiat pour éviter l'enfoncement
                        float separation = 10f;
                        entity.WorldPos += pushDir * separation;

                        if (!entity.IsAlive)
                        {
                            foreach (var (id, qty, _) in GameData.GetAnimalDrops(entity.Species))
                            {
                                Program.GiveItemToPlayer(id, qty, entity.WorldPos);
                            }
                        }
                    }
                }
            }

            // Animation des roues
            WheelSpin += speed * dt * 0.02f;
            float maxSteerAngle = 35f;
            float speedFactor = Math.Clamp(1f - (speed / MAX_SPEED) * 0.7f, 0.3f, 1f);
            float targetSteerAngle = 0f;
            if (isAccelerating && turnInput != 0f)
            {
                float turnMultiplier = isMovingForward ? 1f : -1f;
                targetSteerAngle = turnInput * turnMultiplier * maxSteerAngle * speedFactor;
            }
            float steerLerpSpeed = 8f;
            WheelRotation = MathHelper.Lerp(WheelRotation, targetSteerAngle, steerLerpSpeed * dt);
        }

        private bool IsColliding(Vector2 newPos, HashSet<string> destroyedObjects)
        {
            int ts = Program.TileSize;
            float rad = Angle * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            Vector2 half = new Vector2(CAR_WIDTH / 2, CAR_HEIGHT / 2);
            Vector2[] corners = new Vector2[4];
            corners[0] = new Vector2(-half.X, -half.Y);
            corners[1] = new Vector2( half.X, -half.Y);
            corners[2] = new Vector2( half.X,  half.Y);
            corners[3] = new Vector2(-half.X,  half.Y);
            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].X * cos - corners[i].Y * sin;
                float y = corners[i].X * sin + corners[i].Y * cos;
                corners[i] = new Vector2(newPos.X + x, newPos.Y + y);
            }
            float minX = corners.Min(c => c.X);
            float maxX = corners.Max(c => c.X);
            float minY = corners.Min(c => c.Y);
            float maxY = corners.Max(c => c.Y);
            int startX = (int)Math.Floor(minX / ts);
            int endX   = (int)Math.Ceiling(maxX / ts);
            int startY = (int)Math.Floor(minY / ts);
            int endY   = (int)Math.Ceiling(maxY / ts);
            for (int x = startX; x <= endX; x++)
            for (int y = startY; y <= endY; y++)
            {
                string key = $"{x}_{y}";
                if (destroyedObjects.Contains(key)) continue;
                int tileId = World.GetObjectIdAt(x, y);
                if (tileId == 0) tileId = World.GetGroundTileIdAt(x, y);
                var tileData = WorldTileRegistry.GetTile(tileId);
                if (tileData == null || tileData.Walkable) continue;
                int heightTile = World.GetHeightAt(x, y);
                int drawY = y * ts - (heightTile * ts / 4);
                Rectangle tileRect = new Rectangle(x * ts, drawY, ts, ts);
                if (IntersectOrientedRectangleWithAABB(corners, tileRect))
                    return true;
            }
            // Vérifier les collisions avec d'autres voitures
            foreach (var other in Program.Cars)
            {
                if (other == this) continue;
                if (other.IsOccupied) continue; // on peut ajuster
                // Vérifier si les boîtes englobantes se chevauchent
                if (Vector2.Distance(newPos, other.Position) < CAR_WIDTH + 10)
                    return true;
            }
            return false;
        }

        public void Draw()
        {
            if (Program.carBodyTextures.Count == 0) return;
            float layerHeightStep = 1.0f;
            float bodyScale = 2.5f;

            // 1. Dessiner les roues d'abord (elles seront sous la carrosserie)
            DrawWheels();

            // 2. Dessiner la carrosserie par-dessus
            int totalLayers = Program.carBodyTextures.Count;
            for (int i = 0; i < totalLayers; i++)
            {
                var tex = Program.carBodyTextures[i];
                if (tex.Id == 0) continue;
                float brightness = (float)(i + 1) / totalLayers;
                brightness = MathF.Pow(brightness, 0.7f);
                brightness = Math.Max(brightness, 0.25f);
                Color tint = new Color((int)(255 * brightness), (int)(255 * brightness), (int)(255 * brightness), 255);
                float yOffset = i * layerHeightStep;
                Vector2 drawPos = new Vector2(Position.X, Position.Y - yOffset);
                Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
                Rectangle destRect = new Rectangle(drawPos.X, drawPos.Y, tex.Width * bodyScale, tex.Height * bodyScale);
                Vector2 origin = new Vector2(destRect.Width / 2, destRect.Height / 2);
                Raylib.DrawTexturePro(tex, srcRect, destRect, origin, Angle, tint);
            }
        }
        
        public void DrawDebugHitbox()
        {
            Vector2[] corners = GetCorners(Position, Angle);
            Color hitboxColor = Color.Green;
            for (int i = 0; i < 4; i++)
            {
                Vector2 start = corners[i];
                Vector2 end = corners[(i + 1) % 4];
                Raylib.DrawLineEx(start, end, 2, hitboxColor);
            }
            Raylib.DrawCircle((int)Position.X, (int)Position.Y, 5, Color.Red);
            float rad = Angle * MathF.PI / 180f;
            Vector2 dir = new Vector2(MathF.Sin(rad), -MathF.Cos(rad));
            Vector2 dirEnd = Position + dir * 40f;
            Raylib.DrawLineEx(Position, dirEnd, 2, Color.Yellow);
        }

        private void DrawWheels()
        {
            if (Program.wheelTextures.Count == 0) return;
            float bodyScale = 2.5f;
            float carWidth = 48f;
            float carLength = 96f;
            Vector2 wheelFL_Offset = new Vector2(-carWidth * 1.0f, -carLength * 0.65f);
            Vector2 wheelFR_Offset = new Vector2(carWidth * 1.0f, -carLength * 0.65f);
            Vector2 wheelRL_Offset = new Vector2(-carWidth * 1.0f, carLength * 0.75f);
            Vector2 wheelRR_Offset = new Vector2(carWidth * 1.0f, carLength * 0.75f);
            float wheelScale = bodyScale * 0.9f;
            float frontWheelAngle = WheelRotation;
            DrawWheel(Position, Angle, wheelRL_Offset, 0f, WheelSpin, wheelScale);
            DrawWheel(Position, Angle, wheelRR_Offset, 0f, WheelSpin, wheelScale);
            DrawWheel(Position, Angle, wheelFL_Offset, frontWheelAngle, WheelSpin, wheelScale);
            DrawWheel(Position, Angle, wheelFR_Offset, frontWheelAngle, WheelSpin, wheelScale);
        }

        private void DrawWheel(Vector2 carPos, float carAngle, Vector2 localOffset, float steerAngle, float spinAngle, float wheelScale)
        {
            if (Program.wheelTextures.Count == 0) return;
            float layerHeightStep = 0.8f;
            float rad = carAngle * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            Vector2 rotatedOffset = new Vector2(localOffset.X * cos - localOffset.Y * sin, localOffset.X * sin + localOffset.Y * cos);
            Vector2 wheelBasePos = carPos + rotatedOffset;
            float totalAngle = carAngle + steerAngle;
            for (int layer = 0; layer < Program.wheelTextures.Count; layer++)
            {
                Texture2D wheelTex = Program.wheelTextures[layer];
                if (wheelTex.Id == 0) continue;
                float yOffset = layer * layerHeightStep;
                Vector2 wheelPos = new Vector2(wheelBasePos.X, wheelBasePos.Y - yOffset);
                Rectangle srcRect = new Rectangle(0, 0, wheelTex.Width, wheelTex.Height);
                Rectangle destRect = new Rectangle(wheelPos.X, wheelPos.Y, wheelTex.Width * wheelScale, wheelTex.Height * wheelScale);
                Vector2 origin = new Vector2(destRect.Width / 2, destRect.Height / 2);
                Color wheelColor = (steerAngle == 0f && Math.Abs(spinAngle) < 0.1f) ? new Color(200, 200, 200, 255) : Color.White;
                Raylib.DrawTexturePro(wheelTex, srcRect, destRect, origin, totalAngle, wheelColor);
            }
        }

        public CarSaveData GetSaveData()
        {
            return new CarSaveData
            {
                PosX = Position.X,
                PosY = Position.Y,
                Angle = Angle,
                VelocityX = VelocityX,
                VelocityY = VelocityY,
                AngularVelocity = AngularVelocity,
                WheelRotation = WheelRotation,
                WheelSpin = WheelSpin
            };
        }

        public void LoadSaveData(CarSaveData data)
        {
            Position = new Vector2(data.PosX, data.PosY);
            Angle = data.Angle;
            VelocityX = data.VelocityX;
            VelocityY = data.VelocityY;
            AngularVelocity = data.AngularVelocity;
            WheelRotation = data.WheelRotation;
            WheelSpin = data.WheelSpin;
        }
    }

    [System.Serializable]
    public class CarSaveData
    {
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float Angle { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float AngularVelocity { get; set; }
        public float WheelRotation { get; set; }
        public float WheelSpin { get; set; }
    }
}