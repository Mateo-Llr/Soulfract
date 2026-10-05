// Explosive.cs - Version top-down avec hauteur apparente
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public class ThrowableProjectile
    {
        public Vector2 Position;      // Position au sol (X, Y monde)
        public Vector2 TargetPosition; // Position cible (là où le curseur visait)
        public Vector2 StartPosition;  // Position de départ
        public float Height = 0f;      // Hauteur apparente (pour l'effet de saut)
        public float MaxHeight = 80f;  // Hauteur maximale de la parabole
        public float Progress = 0f;    // Progression de 0 à 1
        public float Speed = 0.6f;     // Vitesse de progression (secondes pour arriver)
        public bool IsAlive = true;
        public bool HasExploded = false;
        public int ItemId;
        
        // Pour l'effet visuel
        public float Rotation = 0f;
        public float RotationSpeed = 720f;
        
        public ThrowableProjectile(Vector2 startPos, Vector2 targetPos, float power, int itemId)
        {
            StartPosition = startPos;
            TargetPosition = targetPos;
            Position = startPos;
            ItemId = itemId;
            
            // La puissance influence la vitesse et la hauteur
            // power = 0.3 à 1.0
            float normalizedPower = Math.Clamp(power, 0.3f, 1f);
            Speed = 0.35f + normalizedPower * 0.25f; // Entre 0.4 et 0.6 seconde
            MaxHeight = 40f + normalizedPower * 80f; // Entre 40 et 120 pixels
            
            Progress = 0f;
            Height = 0f;
            Rotation = 0f;
        }
        
        public void Update(float dt)
        {
            if (!IsAlive) return;
            
            // Avancer la progression
            Progress += dt / Speed;
            
            if (Progress >= 1f)
            {
                Position = TargetPosition;
                // La dynamite explose ; les autres objets jetables s'arrêtent à l'impact.
                if (!HasExploded)
                {
                    if (GameData.ItemDatabase.TryGetValue(ItemId, out var itemData) && itemData.Type == ItemType.Throwable)
                        Explode();
                    else
                        ImpactNonExplosiveThrowable();
                }
                return;
            }
            
            // Position intermédiaire (lerp linéaire)
            float t = Progress;
            Position = Vector2.Lerp(StartPosition, TargetPosition, t);
            
            // Hauteur en forme de parabole (sinus)
            // t=0 -> hauteur 0, t=0.5 -> hauteur max, t=1 -> hauteur 0
            float parabolaHeight = MathF.Sin(t * MathF.PI) * MaxHeight;
            Height = parabolaHeight;
            
            // Rotation pendant le vol
            Rotation += RotationSpeed * dt;
            
            // Ajouter une petite traînée de fumée quand la hauteur est basse
            if (parabolaHeight < 15f && Progress > 0.1f && Progress < 0.9f)
            {
                if (Random.Shared.NextDouble() < 0.3)
                {
                    SpawnSmokeTrail();
                }
            }
        }

        private void ImpactNonExplosiveThrowable()
        {
            HasExploded = true;
            IsAlive = false;

            const int impactDamage = 2;
            foreach (var entity in Program.GetEntities())
            {
                if (!entity.IsAlive || entity.Species == "human" || entity.IsPlayer || entity.IsTamed || entity.IsInvulnerable)
                    continue;

                Rectangle hitbox = World.GetEntityDamageHitbox(entity, Program.TileSize);
                if (!Raylib.CheckCollisionPointRec(Position, hitbox))
                    continue;

                entity.CurrentHP -= impactDamage;
                entity.OnHit(Position);

                Vector2 knockbackDirection = entity.WorldPos - Position;
                if (knockbackDirection.LengthSquared() > 0.01f)
                    knockbackDirection = Vector2.Normalize(knockbackDirection);
                else
                    knockbackDirection = new Vector2(1f, 0f);
                entity.ApplyKnockback(knockbackDirection, 35f);

                Program.AddFloatingDamage(new Vector2(entity.WorldPos.X, entity.WorldPos.Y - 20f), impactDamage, false, isEnemy: true);
                if (!entity.IsAlive)
                {
                    foreach (var (id, quantity, metadata) in GameData.GetAnimalDrops(entity.Species))
                        Program.GiveItemToPlayer(id, quantity, entity.WorldPos, null, metadata);
                }
                break;
            }
        }
        
        private void SpawnSmokeTrail()
        {
            Random rand = new Random();
            Vector2 smokePos = Position;
            Vector2 velocity = new Vector2(
                (float)(rand.NextDouble() - 0.5) * 30f,
                (float)(rand.NextDouble() - 0.5) * 20f - 20f
            );
            Color smokeColor = new Color(80, 80, 80, 150);
            float size = rand.Next(3, 7);
            float lifetime = rand.Next(300, 600) / 1000f;
            
            Program.GetParticleList().Add(new Particle(smokePos, velocity, smokeColor, size, lifetime));
        }
        
        public void Explode()
		{
			if (HasExploded) return;
			HasExploded = true;
			IsAlive = false;
			
			int explosionRadius = 3; // Rayon en tuiles
			int centerTileX = (int)(Position.X / Program.TileSize);
			int centerTileY = (int)(Position.Y / Program.TileSize);
			
			// ========== NOUVEAU : DÉGÂTS AUX ANIMAUX ==========
			float explosionDamage = 25f; // Dégâts de base de l'explosion
			float damageRadius = explosionRadius * Program.TileSize; // Rayon en pixels
			Program.ApplyScreenShake(12f, 0.3f);
			
			// Récupérer toutes les entités dans le rayon
			var entities = Program.GetEntities();
			foreach (var entity in entities)
			{
				if (!entity.IsAlive) continue;
				if (entity.Species == "human") continue; // Ne pas blesser le joueur (optionnel)
				
				// Calculer la distance entre l'explosion et l'entité
				float distance = Vector2.Distance(Position, entity.WorldPos);
				if (distance <= damageRadius)
				{
					// Dégâts décroissants avec la distance (plus on est proche, plus on prend cher)
					float damageMultiplier = 1f - (distance / damageRadius);
					int damage = (int)(explosionDamage * damageMultiplier);
					damage = Math.Max(5, damage); // Minimum 5 dégâts
					
					// Infliger les dégâts
					entity.CurrentHP -= damage;
					entity.OnHit(Position);

					// Appliquer un knockback (repousser l'entité loin de l'explosion)
					Vector2 knockbackDir = entity.WorldPos - Position;
					if (knockbackDir.Length() > 0.01f)
						knockbackDir = Vector2.Normalize(knockbackDir);
					else
						knockbackDir = new Vector2(1, 0);
					entity.ApplyKnockback(knockbackDir, 150f);
					
					// Ajouter des dégâts flottants
					Vector2 damagePos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y - 20);
                    Program.AddFloatingDamage(damagePos, damage, damage >= 15, isEnemy: true);
					
					// Si l'entité meurt, donner ses drops
					if (!entity.IsAlive)
					{
						foreach (var (id, qty, _) in GameData.GetAnimalDrops(entity.Species))
						{
							Program.GiveItemToPlayer(id, qty, Position);
						}
					}
				}
			}
			// ========== FIN DE L'AJOUT ==========
			
			// Destruction des tuiles (code existant)
			var destroyedTiles = new List<(int x, int y, int objectId)>();

			for (int dx = -explosionRadius; dx <= explosionRadius; dx++)
			{
				for (int dy = -explosionRadius; dy <= explosionRadius; dy++)
				{
					int tileX = centerTileX + dx;
					int tileY = centerTileY + dy;
					float dist = MathF.Sqrt(dx * dx + dy * dy);
					if (dist > explosionRadius) continue;
					
					int objectId = World.GetObjectIdAt(tileX, tileY);
					if (objectId == 0) continue;
					
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData == null) continue;
					
					// Ne pas détruire les blocs indestructibles
					if (tileData.Name == "Bedrock") continue;
					
					destroyedTiles.Add((tileX, tileY, objectId));
				}
			}

			// Récupérer les drops et détruire les tuiles
			foreach (var (tileX, tileY, objectId) in destroyedTiles)
			{
				//  AJOUT : Récupérer l'overlay de minerai avant destruction
				int mineralOverlayId = World.GetOverlayAt(tileX, tileY);
				if (mineralOverlayId != 0 && mineralOverlayId != 82)
				{
					int mineralItemId = GetMineralItemId(mineralOverlayId);
					if (mineralItemId != 0)
					{
						int qty = Random.Shared.Next(1, 3);
                        Program.GiveItemToPlayer(mineralItemId, qty, Position, pickupCooldown: 0f);
						// Optionnel : notification
						if (GameData.ItemDatabase.TryGetValue(mineralItemId, out var mineralData))
							Program.AddItemNotification(mineralData.Name, qty, mineralData.Color);
					}
					World.RemoveOverlay(tileX, tileY);
				}
				else if (World.HasTileMeta(tileX, tileY, "ore"))
				{
					// Ore metadata no longer forces a mineral item drop; preserve the block's normal drops.
					World.RemoveTileMeta(tileX, tileY, "ore");
				}
				
				// Drops normaux du bloc
				var drops = WorldTileRegistry.GetDrops(objectId);
				foreach (var (dropId, qty) in drops)
				{
                    Program.GiveItemToPlayer(dropId, qty, Position, pickupCooldown: 0f);
				}
				World.RemovePlacedObject(tileX, tileY);
				
				string key = $"{tileX}_{tileY}";
				Program.GetDestroyedObjects()?.Add(key);
			}
			
			// Particules d'explosion
			SpawnExplosionParticles(Position, explosionRadius);
		}
		
		private int GetMineralItemId(int overlayId)
		{
			return overlayId switch
			{
				1001 => GameData.GetItemId("iron_ore"), // Iron ore
				1002 => GameData.GetItemId("copper_ore"), // Copper ore
				1003 => GameData.GetItemId("coal"), // Coal ore
				1004 => GameData.GetItemId("garnet"), // Garnet
				1005 => GameData.GetItemId("amber"), // Amber
				1006 => GameData.GetItemId("topaze"), // Topaz
				1007 => GameData.GetItemId("onyx"), // Onyx
				1008 => GameData.GetItemId("opale"), // Opal
				1009 => GameData.GetItemId("obsidian"), // Obsidian
				_ => 0
			};
		}
        
        private void SpawnExplosionParticles(Vector2 center, int radius)
        {
            Random rand = new Random();
            int particleCount = 50 + radius * 15;
            
            for (int i = 0; i < particleCount; i++)
            {
                float angle = (float)(rand.NextDouble() * Math.PI * 2);
                float speed = rand.Next(80, 250);
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                
                float offsetX = (float)(rand.NextDouble() - 0.5) * radius * Program.TileSize;
                float offsetY = (float)(rand.NextDouble() - 0.5) * radius * Program.TileSize;
                Vector2 pos = new Vector2(center.X + offsetX, center.Y + offsetY);
                
                float size = rand.Next(4, 12);
                float lifetime = rand.Next(400, 900) / 1000f;
                
                Color color;
                int colorType = rand.Next(3);
                if (colorType == 0)
                    color = new Color(255, 100 + rand.Next(0, 100), 50, 255);
                else if (colorType == 1)
                    color = new Color(255, 200 + rand.Next(0, 55), 50, 255);
                else
                    color = new Color(255, 50 + rand.Next(0, 100), 50, 255);
                
                Program.GetParticleList().Add(new Particle(pos, velocity, color, size, lifetime));
            }
            
            // Fumée
            for (int i = 0; i < 15; i++)
            {
                float angle = (float)(rand.NextDouble() * Math.PI * 2);
                float speed = rand.Next(40, 120);
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                
                float offsetX = (float)(rand.NextDouble() - 0.5) * radius * Program.TileSize;
                float offsetY = (float)(rand.NextDouble() - 0.5) * radius * Program.TileSize;
                Vector2 pos = new Vector2(center.X + offsetX, center.Y + offsetY);
                
                float size = rand.Next(8, 18);
                float lifetime = rand.Next(700, 1300) / 1000f;
                Color smokeColor = new Color(40, 40, 40, 200);
                
                Program.GetParticleList().Add(new Particle(pos, velocity, smokeColor, size, lifetime));
            }
        }
        
        public void Draw()
        {
            if (!IsAlive) return;
            
            // Calculer la position d'écran avec la hauteur
            // La hauteur fait "monter" la dynamite vers le haut de l'écran
            Vector2 drawPos = new Vector2(Position.X, Position.Y - Height);
            
            if (GameData.ItemDatabase.TryGetValue(ItemId, out var itemData) && itemData.Icon.Id != 0)
            {
                float size = 28f;
                Rectangle srcRect = new Rectangle(0, 0, itemData.Icon.Width, itemData.Icon.Height);
                Rectangle destRect = new Rectangle(drawPos.X - size/2, drawPos.Y - size/2, size, size);
                Vector2 origin = new Vector2(size/2, size/2);
                
                // Ombre portée au sol
                float shadowAlpha = 0.5f * (1f - Height / MaxHeight);
                Raylib.DrawCircle((int)Position.X, (int)Position.Y, 8, new Color(0, 0, 0, (int)(80 * shadowAlpha)));
                
                // La dynamite avec rotation
                Raylib.DrawTexturePro(itemData.Icon, srcRect, destRect, origin, Rotation, Color.White);
            }
            else
            {
                // Fallback
                Raylib.DrawCircle((int)drawPos.X, (int)drawPos.Y, 8, new Color(200, 50, 50, 255));
                Raylib.DrawCircle((int)Position.X, (int)Position.Y, 6, new Color(0, 0, 0, 80));
            }
        }
    }
}