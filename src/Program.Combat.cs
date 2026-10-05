// Program.Combat.cs
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {

		private static Texture2D _serverPvpOnIcon;

		private static Texture2D _serverPvpOffIcon;

		private static Texture2D _healthBarWingTexture;

		private static Texture2D _healthBarWingColorTexture;

		private static Dictionary<string, Texture2D> _bossCursorCache = new Dictionary<string, Texture2D>();

		// temps écoulé depuis le dernier sprint
        
        //  FAIM / SOIF : diminuent lentement au cours du temps. À 0, le joueur
        // perd doucement de la vie (faim) tant qu'elles restent à 0.
        public static float playerHunger = 100f;

		private const float STARVATION_DAMAGE_PER_SEC = 2f;

		// dégâts/seconde quand faim ET/OU soif = 0
        private static float _starvationDamageAccumulator = 0f;

		//  Déduit une cause de mort "élémentaire" plausible à partir de l'espèce de
        // l'attaquant. Mapping volontairement simple et centralisé ici : à affiner au fil
        // de l'ajout de nouvelles créatures/boss si on veut des messages plus précis.
        public static DeathCause InferDeathCauseFromSpecies(string species)
        {
            return species?.ToLowerInvariant() switch
            {
                "khamsin" => DeathCause.Fire,
                "worm" => DeathCause.Earth,
                "gulper" => DeathCause.Water,
                "scorpio" => DeathCause.Poison,
                "wolf" or "bear" or "goblin" => DeathCause.Physical,
                _ => DeathCause.Physical
            };
        }

		// Nom/cause de la dernière source de dégâts reçue par le joueur local, utilisés
        // pour composer le message d'écran de mort au moment où playerHP tombe à 0.
        static string _lastDamageSourceName = "l'obscurité";

		static DeathCause _lastDamageCause = DeathCause.Generic;

		//  Réinitialise et démarre la séquence d'écran de mort (appelé une fois, au moment
        // où playerHP tombe à 0 - voir DamagePlayer).
        static void StartDeathScreen()
        {
            _deathVictimName = "Vous";
            _deathMessage = BuildDeathMessage(_deathVictimName, _lastDamageSourceName, _lastDamageCause);
            _deathScreenPhase = DeathScreenPhase.Vignette;
            _deathScreenPhaseTimer = 0f;
            _deathMessageCharsShown = 0;
            _deathMessageCharTimer = 0f;
        }

		static float attackCooldown = 0f;

		static float attackAnim = 0f;

		static bool isAttacking = false;

		public static Entity? CurrentBoss = null;

		private static bool _eyeBossIntroPlaying = false;

		private static float _eyeBossIntroTimer = 0f;

		private const float EYE_BOSS_INTRO_DURATION = 1.1f;

		private static (int x, int y) _eyeBossTile = (-1, -1);

		private static Vector2 _eyeBossCenter;

		private static int _eyeBossHP = 0;

		private static int _eyeBossMaxHP = 1;

		private static int _eyeBossWaveIndex = -1;

		private static List<Entity> _eyeBossWaveEntities = new List<Entity>();

		private static bool _eyeBossWaitingNextWave = false;

		private static float _eyeBossNextWaveTimer = 0f;

		private const float EYE_BOSS_WAVE_DELAY = 3f;

		//  Boucliers tournant autour de l'oeil (remplace la barre de vie du boss) : un bouclier
		// se casse à chaque vague terminée, laissant l'oeil nu pour la toute dernière vague.
		private static int _eyeBossShieldTotal = 0;

		private static int _eyeBossShieldsRemaining = 0;

		private static float _eyeBossShieldRotation = 0f;

		private const float EYE_BOSS_SHIELD_RADIUS = 68f;

		private const float EYE_BOSS_SHIELD_ROTATION_SPEED = 0.7f;

		//  Vagues de monstres du boss de l'oeil : gobelins, araignées, squelettes.
		private static readonly (string species, int count)[][] _eyeBossWaves = new (string species, int count)[][]
		{
			new (string species, int count)[] { ("goblin", 4) },
			new (string species, int count)[] { ("spider", 5) },
			new (string species, int count)[] { ("goblin", 3), ("spider", 3) },
			new (string species, int count)[] { ("skeleton", 4), ("goblin", 2) },
			new (string species, int count)[] { ("goblin", 3), ("spider", 3), ("skeleton", 3) },
		};

		//  PVP : quand activé pour CE joueur, les autres ne voient plus son pseudo
		// ni sa barre de vie au-dessus de sa tête (pour ne pas pouvoir le localiser).
		// Propagé aux autres via PlayerTransform.PvpEnabled (champ "State"/"Roster").
		public static bool LocalPvpEnabled = false;

		static Dictionary<string, CarouselPreviewCache> _carouselPreviewCache = new Dictionary<string, CarouselPreviewCache>();

		public const float ATTACK_RADIUS = 60f;

		public const float ATTACK_ANGLE = 75f;

		public const float ATTACK_KNOCKBACK = 80f;

		static List<FloatingDamage> _floatingDamages = new();

		static float _attackSwingProgress = 0f;

		static float _attackSwingSpeed = 3.5f;

		private static Vector2 GetViewportMouseWorldPosition(Camera2D camera, Rectangle viewport)
		{
			Vector2 mouseScreen = Raylib.GetMousePosition();
			mouseScreen -= new Vector2(viewport.X, viewport.Y);
			return Raylib.GetScreenToWorld2D(mouseScreen, camera);
		}

		private const int MAX_FLOATING_DAMAGES = 50;

		// À ajouter en haut de Program.cs

		public static void AddFloatingDamage(Vector2 worldPos, int damage, bool isCritical = false, bool isEnemy = false, bool isPlayer = false)
		{
			//  VÉRIFICATION 1 : Position valide
			if (float.IsNaN(worldPos.X) || float.IsNaN(worldPos.Y) ||
				float.IsInfinity(worldPos.X) || float.IsInfinity(worldPos.Y))
			{
				Console.WriteLine($" AddFloatingDamage ignoré: position invalide ({worldPos.X}, {worldPos.Y})");
				return;
			}
			
			//  VÉRIFICATION 2 : Dégâts valides
			if (damage <= 0) damage = 1;
			
			//  VÉRIFICATION 3 : Taille de la liste
			if (_floatingDamages.Count > MAX_FLOATING_DAMAGES)
			{
				_floatingDamages.RemoveAt(0);
			}
			
			Color color;
			Color? outline = null;
			if (isEnemy)
			{
				// Dégâts infligés à un ennemi : blanc avec contour noir
				color = Color.White;
				outline = new Color(0, 0, 0, 255);
			}
			else if (isPlayer)
			{
				// Dégâts subis par le joueur : rouge avec contour rouge foncé
				color = new Color(255, 60, 60, 255);
				outline = new Color(90, 0, 0, 255);
			}
			else
			{
				color = isCritical ? new Color(255, 100, 50, 255) : new Color(255, 200, 100, 255);
				if (damage >= 10) color = new Color(255, 80, 80, 255);
			}
			
			var fd = new FloatingDamage(worldPos, damage, color);
			fd.OutlineColor = outline;
			_floatingDamages.Add(fd);
		}

		/// <summary>Joue un son de coup (punch.mp3 / punchN.mp3, choisi au hasard) lorsque le joueur
		/// frappe effectivement quelque chose (un ennemi/une entité touchée par l'attaque).</summary>
		private static void PlayPunchSound()
		{
			InitializePunchSounds();
			if (_punchSounds.Count == 0) return;

			int index = Random.Shared.Next(_punchSounds.Count);
			Sound sound = _punchSounds[index];
			if (!Raylib.IsSoundReady(sound)) return;

			Raylib.SetSoundVolume(sound, 0.55f);
			Raylib.SetSoundPitch(sound, GetRandomSoundPitch());
			Raylib.PlaySound(sound);
		}

		private static void InitializeDiscordRichPresence()
		{
			const string APPLICATION_ID = "1532723289418371194";

			try
			{
				_discordClient = new DiscordRpcClient(APPLICATION_ID);
				_discordClient.Initialize();

				_discordClient.SetPresence(new RichPresence()
				{
					Details = "En train d'explorer Soulfract",
					State = "Aventure en cours",
					Assets = new Assets()
					{
						LargeImageKey = "Soulfract_logo", // Optionnel, si vous avez configuré une image sur le portail Discord
						LargeImageText = "Soulfract - Survie & Aventure"
					}
				});

				Console.WriteLine("[Discord] Rich Presence initialisée avec succès.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Discord] Erreur d'initialisation : {ex.Message}");
			}
		}

		// ==================== BOSS DE L'OEIL ====================

		//  Dégâts infligés au boss lorsqu'un monstre de vague est tué.
		private static int GetEyeBossMonsterDamage(string species)
		{
			return species switch
			{
				"goblin" => 15,
				"spider" => 12,
				"skeleton" => 20,
				_ => 10
			};
		}

		//  Particules qui convergent depuis tout autour vers le centre de l'oeil.
		private static void SpawnEyeBossConvergeParticles(Vector2 center)
		{
			int count = 60;
			for (int i = 0; i < count; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float dist = 140f + (float)Random.Shared.NextDouble() * 180f;
				Vector2 startPos = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
				float speed = 220f + (float)Random.Shared.NextDouble() * 160f;
				Vector2 toCenter = center - startPos;
				if (toCenter.LengthSquared() < 1f) toCenter = new Vector2(1, 0);
				Vector2 velocity = Vector2.Normalize(toCenter) * speed;
				float size = Random.Shared.Next(3, 7);
				float lifetime = EYE_BOSS_INTRO_DURATION + 0.4f;
				Color color = Random.Shared.NextDouble() > 0.5
					? new Color(210, 110, 240, 255)
					: new Color(150, 70, 210, 255);
				var p = new Particle(startPos, velocity, color, size, lifetime);
				p.TargetPosition = center;
				p.UseTargetLanding = true;
				_particles.Add(p);
			}
		}

		//  Explosion de particules au centre de l'oeil (fin de la convergence, et victoire).
		private static void SpawnEyeBossExplosionParticles(Vector2 center)
		{
			int count = 55;
			for (int i = 0; i < count; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(120, 340);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float size = Random.Shared.Next(5, 13);
				float lifetime = 0.5f + (float)Random.Shared.NextDouble() * 0.5f;
				Color color = Random.Shared.NextDouble() > 0.4
					? new Color(230, 160, 255, 255)
					: new Color(255, 255, 255, 255);
				_particles.Add(new Particle(center, velocity, color, size, lifetime));
			}
			ApplyScreenShake(7f, 0.35f);
		}

		//  Position actuelle du bouclier d'index "index" (0 = premier posé, tourne avec les autres).
		private static Vector2 GetEyeBossShieldPosition(int index)
		{
			float angle = (2f * MathF.PI * index / Math.Max(1, _eyeBossShieldTotal)) + _eyeBossShieldRotation;
			return _eyeBossCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * EYE_BOSS_SHIELD_RADIUS;
		}

		//  Fait "éclater" le bouclier restant le plus externe dans un nuage de particules,
		// appelé une fois par vague terminée (voir UpdateEyeBossFight). La toute dernière vague
		// ne casse plus rien : il n'en reste déjà aucun, l'oeil est nu.
		private static void BreakEyeBossShield()
		{
			if (_eyeBossShieldsRemaining <= 0) return;
			int index = _eyeBossShieldsRemaining - 1;
			SpawnEyeBossShieldBreakParticles(GetEyeBossShieldPosition(index));
			_eyeBossShieldsRemaining--;
			ApplyScreenShake(2.5f, 0.2f);
		}

		//  Nuage de particules à l'endroit où un bouclier vient de se casser.
		private static void SpawnEyeBossShieldBreakParticles(Vector2 pos)
		{
			int count = 26;
			for (int i = 0; i < count; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(90, 260);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float size = Random.Shared.Next(3, 8);
				float lifetime = 0.35f + (float)Random.Shared.NextDouble() * 0.4f;
				Color color = Random.Shared.NextDouble() > 0.4
					? new Color(140, 200, 240, 255)
					: new Color(230, 245, 255, 255);
				_particles.Add(new Particle(pos, velocity, color, size, lifetime));
			}
		}

		//  Petit nuage de poussière à l'endroit exact où un monstre de vague apparaît, pour
		// masquer son apparition soudaine (voir SpawnEyeBossWave).
		private static void SpawnEyeBossMobSpawnParticles(Vector2 pos)
		{
			int count = 16;
			for (int i = 0; i < count; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(40, 140);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float size = Random.Shared.Next(4, 9);
				float lifetime = 0.35f + (float)Random.Shared.NextDouble() * 0.3f;
				Color color = Random.Shared.NextDouble() > 0.5
					? new Color(120, 60, 150, 255)
					: new Color(80, 40, 100, 255);
				_particles.Add(new Particle(pos, velocity, color, size, lifetime));
			}
		}

		//  Fait apparaître une vague de monstres autour du socle.
		private static void SpawnEyeBossWave(int waveIndex)
		{
			if (waveIndex < 0 || waveIndex >= _eyeBossWaves.Length) return;
			_eyeBossWaveEntities.Clear();

			int ts = Program.TileSize;
			var rand = Random.Shared;
			foreach (var (species, count) in _eyeBossWaves[waveIndex])
			{
				int spawned = 0;
				int attempts = 0;
				while (spawned < count && attempts < count * 20)
				{
					attempts++;
					float angle = (float)(rand.NextDouble() * Math.PI * 2);
					float dist = 260f + (float)rand.NextDouble() * 180f;
					Vector2 spawnPos = _eyeBossCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
					int tX = (int)(spawnPos.X / ts);
					int tY = (int)(spawnPos.Y / ts);
					int groundId = World.GetGroundTileIdAt(tX, tY);
					var ground = WorldTileRegistry.GetTile(groundId);
					if (ground == null || !ground.Walkable) continue;
					if (World.GetObjectIdAt(tX, tY) != 0) continue;

					SpawnEyeBossMobSpawnParticles(spawnPos);

					var mob = new Entity(spawnPos, species, false);
					mob.Behavior = "hostile";
					mob.EyeBossWaveTag = true;
					GetEntities().Add(mob);
					var chunk = World.GetChunkAt(tX, tY);
					if (chunk != null) chunk.Entities.Add(mob);
					_eyeBossWaveEntities.Add(mob);
					spawned++;
				}
			}
			AddNotification(new Notification($" Vague {waveIndex + 1}/{_eyeBossWaves.Length} !", new Color(220, 100, 100, 255), 2.5f));
		}

		static List<Entity> GetEntitiesInAttackCone(Vector2 playerPos, Vector2 attackDir, float attackRange, float attackConeAngle)
		{
			var hitEntities = new List<Entity>();
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			Vector2 playerDim = new Vector2(ts * 1.6f, ts * 0.5f);
			Rectangle playerHitbox = new Rectangle(
				playerVisualPos.X - playerDim.X / 2f,
				playerVisualPos.Y - playerDim.Y / 2f,
				playerDim.X,
				playerDim.Y
			);
			Vector2 attackOrigin = new Vector2(
				playerHitbox.X + playerHitbox.Width / 2,
				playerHitbox.Y + playerHitbox.Height / 2
			);
			foreach (var entity in entities)
			{
				if (!entity.IsAlive) continue;
				if (entity.Species == "human") continue;
				//  Un pet "ami" (apprivoisé par le joueur, ou offert par quête à un PNJ non
				// hostile) reste protégé. En revanche, le pet d'un PNJ hostile (ex : le loup
				// d'un gobelin) est une cible légitime au même titre que son maître : "les amis
				// de mes ennemis sont mes ennemis" (voir Entity.IsHostilePet).
				if (entity.IsTamed && !entity.IsHostilePet) continue;
				if (entity.CarriedByConnectionId != -1) continue; // portée par un client : pas une cible
				if (entity.IsInvulnerable) continue; // ex: Gulper en vol -> ne peut être frappé qu'au sol
				Rectangle entityHitbox = World.GetEntityDamageHitbox(entity, ts);
				Vector2 closestPoint;
				if (Raylib.CheckCollisionPointRec(attackOrigin, entityHitbox))
					closestPoint = new Vector2(entityHitbox.X + entityHitbox.Width / 2f, entityHitbox.Y + entityHitbox.Height / 2f);
				else
				{
					float closestX = Math.Clamp(attackOrigin.X, entityHitbox.X, entityHitbox.X + entityHitbox.Width);
					float closestY = Math.Clamp(attackOrigin.Y, entityHitbox.Y, entityHitbox.Y + entityHitbox.Height);
					closestPoint = new Vector2(closestX, closestY);
				}
				Vector2 toClosest = closestPoint - attackOrigin;
				float distanceSq = toClosest.LengthSquared();
				float attackRangeSq = attackRange * attackRange;
				if (distanceSq > attackRangeSq) continue;
				if (toClosest.LengthSquared() > 0.0001f)
				{
					float angleToEntity = MathF.Atan2(toClosest.Y, toClosest.X) * 180f / MathF.PI;
					float attackDirAngle = MathF.Atan2(attackDir.Y, attackDir.X) * 180f / MathF.PI;
					float angleDiff = Math.Abs(angleToEntity - attackDirAngle);
					angleDiff = Math.Min(angleDiff, 360 - angleDiff);
					if (angleDiff > attackConeAngle / 2f) continue;
				}
				hitEntities.Add(entity);
			}
			return hitEntities;
		}

		//  PVP : équivalent de GetEntitiesInAttackCone mais pour les joueurs distants.
		// Un joueur n'est une cible valide que si LUI ET l'attaquant ont activé le PVP
		// (consentement mutuel).
		static List<LocalPlayer> GetPvpTargetsInAttackCone(Vector2 playerPos, Vector2 attackDir, float attackRange, float attackConeAngle)
		{
			var hits = new List<LocalPlayer>();
			if (!Program.LocalPvpEnabled) return hits; // il faut soi-même avoir activé le PVP pour pouvoir frapper
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 attackOrigin = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			float attackRangeSq = attackRange * attackRange;
			foreach (var rp in NetworkManager.GetRemotePlayersAsLocalPlayers())
			{
				if (!rp.Connected || !rp.PvpEnabled) continue; // la cible doit aussi avoir activé le PVP
				Rectangle damageHitbox = World.GetEntityDamageHitbox(rp.Position, rp.SpeciesOverride ?? "human", ts);
				Vector2 closestPoint;
				if (Raylib.CheckCollisionPointRec(attackOrigin, damageHitbox))
					closestPoint = new Vector2(damageHitbox.X + damageHitbox.Width / 2f, damageHitbox.Y + damageHitbox.Height / 2f);
				else
				{
					float closestX = Math.Clamp(attackOrigin.X, damageHitbox.X, damageHitbox.X + damageHitbox.Width);
					float closestY = Math.Clamp(attackOrigin.Y, damageHitbox.Y, damageHitbox.Y + damageHitbox.Height);
					closestPoint = new Vector2(closestX, closestY);
				}
				Vector2 toTarget = closestPoint - attackOrigin;
				float distanceSq = toTarget.LengthSquared();
				if (distanceSq > attackRangeSq) continue;
				if (toTarget.LengthSquared() > 0.0001f)
				{
					float angleToTarget = MathF.Atan2(toTarget.Y, toTarget.X) * 180f / MathF.PI;
					float attackDirAngle = MathF.Atan2(attackDir.Y, attackDir.X) * 180f / MathF.PI;
					float angleDiff = Math.Abs(angleToTarget - attackDirAngle);
					angleDiff = Math.Min(angleDiff, 360 - angleDiff);
					if (angleDiff > attackConeAngle / 2f) continue;
				}
				hits.Add(rp);
			}
			return hits;
		}

		//  MULTIJOUEUR : équivalent de GetEntitiesInAttackCone pour un client distant, qui ne
		// possède pas d'objets Entity simulés localement (Program.entities est vide côté
		// client) mais reçoit des snapshots EntityDto du host. Renvoie les DTOs touchés.
		static List<EntityDto> GetRemoteEntitiesInAttackCone(Vector2 playerPos, Vector2 attackDir, float attackRange, float attackConeAngle)
		{
			var hitEntities = new List<EntityDto>();
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			Vector2 playerDim = new Vector2(ts * 1.6f, ts * 0.5f);
			Rectangle playerHitbox = new Rectangle(
				playerVisualPos.X - playerDim.X / 2f,
				playerVisualPos.Y - playerDim.Y / 2f,
				playerDim.X,
				playerDim.Y
			);
			Vector2 attackOrigin = new Vector2(
				playerHitbox.X + playerHitbox.Width / 2,
				playerHitbox.Y + playerHitbox.Height / 2
			);
			foreach (var dto in NetworkManager.GetRemoteEntities())
			{
				if (dto.HP <= 0) continue;
				if (dto.Species == "human") continue;
				//  Voir GetEntitiesInAttackCone ci-dessus : même exception pour les pets d'un
				// PNJ hostile côté client distant. NOTE : nécessite un champ IsHostilePet sur
				// EntityDto (non fourni dans les fichiers transmis, probablement dans le code
				// réseau) synchronisé depuis Entity.IsHostilePet côté host.
				if (dto.IsTamed && !dto.IsHostilePet) continue;
				if (dto.IsCarried) continue;
				Rectangle entityHitbox = World.GetEntityDamageHitbox(new Vector2(dto.PosX, dto.PosY), dto.Species, ts, dto.Scale);
				Vector2 closestPoint;
				if (Raylib.CheckCollisionPointRec(attackOrigin, entityHitbox))
					closestPoint = new Vector2(entityHitbox.X + entityHitbox.Width / 2f, entityHitbox.Y + entityHitbox.Height / 2f);
				else
				{
					float closestX = Math.Clamp(attackOrigin.X, entityHitbox.X, entityHitbox.X + entityHitbox.Width);
					float closestY = Math.Clamp(attackOrigin.Y, entityHitbox.Y, entityHitbox.Y + entityHitbox.Height);
					closestPoint = new Vector2(closestX, closestY);
				}
				Vector2 toClosest = closestPoint - attackOrigin;
				float distanceSq = toClosest.LengthSquared();
				float attackRangeSq = attackRange * attackRange;
				if (distanceSq > attackRangeSq) continue;
				if (toClosest.LengthSquared() > 0.0001f)
				{
					float angleToEntity = MathF.Atan2(toClosest.Y, toClosest.X) * 180f / MathF.PI;
					float attackDirAngle = MathF.Atan2(attackDir.Y, attackDir.X) * 180f / MathF.PI;
					float angleDiff = Math.Abs(angleToEntity - attackDirAngle);
					angleDiff = Math.Min(angleDiff, 360 - angleDiff);
					if (angleDiff > attackConeAngle / 2f) continue;
				}
				hitEntities.Add(dto);
			}
			return hitEntities;
		}

		static void UpdateOptionsMenu()
		{
			// Capture de touche en cours de réassignation : la prochaine touche pressée
			// devient la nouvelle touche de l'action, ou Echap annule sans fermer le menu.
			if (_rebindingAction != null)
			{
				// Capture d'une touche clavier ou d'un bouton souris
				int key = Raylib.GetKeyPressed();
				if (key != 0)
				{
					KeyBindings.Rebind(_rebindingAction.Value.action, _rebindingAction.Value.index, new InputBinding(InputType.Keyboard, key));
					_rebindingAction = null;
					return;
				}

				// Capture d'un bouton souris (on ignore le clic qui a déclenché le rebind en vérifiant qu'il est relâché)
				for (int i = 0; i < 5; i++) // MouseButton.Left = 0, Right = 1, Middle = 2
				{
					if (Raylib.IsMouseButtonPressed((MouseButton)i))
					{
						KeyBindings.Rebind(_rebindingAction.Value.action, _rebindingAction.Value.index, new InputBinding(InputType.Mouse, i));
						_rebindingAction = null;
						return;
					}
				}

				if (TryGetPrimaryGamepadIndex(out int gamepadIndex))
				{
					foreach (var button in KeyBindings.CapturableGamepadButtons)
					{
						if (Raylib.IsGamepadButtonPressed(gamepadIndex, button))
						{
							KeyBindings.Rebind(_rebindingAction.Value.action, _rebindingAction.Value.index,
								new InputBinding(InputType.GamepadButton, (int)button));
							_rebindingAction = null;
							return;
						}
					}

					if (KeyBindings.TryGetGamepadAxisDirection(gamepadIndex, out GamepadAxisDirection direction))
					{
						KeyBindings.Rebind(_rebindingAction.Value.action, _rebindingAction.Value.index,
							new InputBinding(InputType.GamepadAxis, (int)direction));
						_rebindingAction = null;
						return;
					}
				}

				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_rebindingAction = null;
					return;
				}
			}

			// ECHAP ramène toujours vers l'endroit d'où le menu a été ouvert
			// (menu principal si ouvert depuis là, pause/partie en cours sinon).
			if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				RequestOptionsBack();
				return;
			}

			if (_optionsTab != _lastOptionsTab)
			{
				_lastOptionsTab = _optionsTab;
				_optionsLanguageScrollOffset = 0f;
			}

			if (Raylib.IsKeyPressed(KeyboardKey.Right)) _masterVolume = Math.Min(100, _masterVolume + 5);
			if (Raylib.IsKeyPressed(KeyboardKey.Left)) _masterVolume = Math.Max(0, _masterVolume - 5);
			Raylib.SetMasterVolume(_masterVolume / 100f);

			if (_optionsTab == 0)
			{
				Vector2 mousePos = Raylib.GetMousePosition();
				int sw = Raylib.GetScreenWidth();
				int sh = Raylib.GetScreenHeight();
				int panelW = 560, panelH = 520;
				int px = (sw - panelW) / 2;
				int py = (sh - panelH) / 2;

				int tabY = py + 76;
				int contentY = tabY + 36 + 30;
				int sliderY = contentY + 36;
				int fsY = sliderY + 60;
				int languageY = fsY + 70;

				int langCount = Localization.AvailableLanguages.Count;
				float flagSize = 80f;
				float colSpacing = 20f;
				float rowSpacing = 20f;
				int cols = 4;
				int rows = (langCount + cols - 1) / cols;
				float gridWidth = cols * flagSize + (cols - 1) * colSpacing;
				float gridHeight = rows * flagSize + (rows - 1) * rowSpacing;

				float gridX = px + 50;
				float gridY = languageY + 42;
				float availableHeight = panelH - (gridY - py) - 40;
				bool needsScroll = gridHeight > availableHeight;
				_optionsLanguageMaxScroll = needsScroll ? gridHeight - availableHeight : 0f;

				if (needsScroll)
				{
					Rectangle gridArea = new Rectangle(gridX, gridY, panelW - 100, availableHeight);
					if (Raylib.CheckCollisionPointRec(mousePos, gridArea))
					{
						float wheel = Raylib.GetMouseWheelMove();
						if (wheel != 0f)
						{
							_optionsLanguageScrollOffset -= wheel * 20f;
							_optionsLanguageScrollOffset = Math.Clamp(_optionsLanguageScrollOffset, 0f, _optionsLanguageMaxScroll);
						}
					}
				}
			}

			// Le reste (onglets, curseur de volume, bouton Retour) est géré à la souris
			// directement dans DrawOptionsMenu, comme pour le menu pause.
		}

		//  PORTAGE DE CRÉATURES : ramasse la créature portable (species.json -> "portable": true)
		// la plus proche du curseur, si elle est à portée. Retourne true en cas de succès.
		private static bool TryPickupCreature(Vector2 playerPos, Camera2D camera, Rectangle? viewport = null, int playerIndex = 0)
		{
			if (_carriedCreature != null || _carriedFurniture != null) return false;

			Vector2 mouseWorld = viewport.HasValue
				? GetViewportMouseWorldPosition(camera, viewport.Value)
				: Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			int ts = Program.TileSize;

			Entity? closest = null;
			float bestDist = ts * 1.5f; // rayon de sélection autour du curseur

			foreach (var e in entities)
			{
				if (!e.IsAlive) continue;
				if (!SpeciesData.IsPortable(e.Species)) continue;
				if (MountedAnimal == e) continue; // on ne porte pas l'animal qu'on est en train de monter

				float dist = Vector2.Distance(mouseWorld, e.WorldPos);
				if (dist < bestDist)
				{
					bestDist = dist;
					closest = e;
				}
			}

			if (closest == null) return false;

			// Vérifier que le joueur est assez proche de la créature elle-même
			if (Vector2.Distance(playerPos, closest.WorldPos) > ts * 2.5f) return false;

			entities.Remove(closest);
			closest.WakeFromCarry();
			_carriedCreature = closest;
			return true;
		}

		//  MULTIJOUEUR : équivalent client de TryPickupCreature. Le client n'a pas d'objets
		// Entity (Program.entities est vide chez lui) : il cherche donc la créature portable la
		// plus proche du curseur parmi les snapshots reçus du host (EntityDto), puis envoie une
		// demande de portage - c'est le host qui valide et fait réellement suivre l'entité.
		// On considère la demande acceptée de façon optimiste côté affichage (la créature
		// disparaîtra des snapshots suivants si le host confirme, ou continuera d'apparaître à
		// sa place si le host a refusé - dans ce dernier cas on retente simplement plus tard).
		private static bool TryPickupCreatureClient(Vector2 playerPos, Camera2D camera, Rectangle? viewport = null)
		{
			if (_clientCarriedNetId != null) return false;

			Vector2 mouseWorld = viewport.HasValue
				? GetViewportMouseWorldPosition(camera, viewport.Value)
				: Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			int ts = Program.TileSize;

			EntityDto? closest = null;
			float bestDist = ts * 1.5f;

			foreach (var dto in NetworkManager.GetRemoteEntities())
			{
				if (dto.HP <= 0) continue;
				if (!SpeciesData.IsPortable(dto.Species)) continue;
				if (dto.IsCarried) continue;

				float dist = Vector2.Distance(mouseWorld, new Vector2(dto.PosX, dto.PosY));
				if (dist < bestDist)
				{
					bestDist = dist;
					closest = dto;
				}
			}

			if (closest == null) return false;
			if (Vector2.Distance(playerPos, new Vector2(closest.PosX, closest.PosY)) > ts * 2.5f) return false;
			if (!Guid.TryParse(closest.NetId, out var netId)) return false;

			NetworkManager.RequestPickupCreature(netId);
			_clientCarriedNetId = closest.NetId;
			_clientCarriedSpecies = closest.Species;
			return true;
		}

		// Essaie de ramasser un meuble, sinon une créature portable, sous le curseur.
		private static bool TryPickupFurnitureOrCreature(Vector2 playerPos, Camera2D camera, Rectangle? viewport = null, int playerIndex = 0)
		{
			if (NetworkManager.IsClient)
				return TryPickupCreatureClient(playerPos, camera, viewport);
			if (TryPickupFurniture(playerPos, camera, viewport, playerIndex)) return true;
			return TryPickupCreature(playerPos, camera, viewport, playerIndex);
		}

		//  MULTIJOUEUR : point d'entrée utilisé par l'IA des entités pour infliger des dégâts
		// au joueur qu'elles ciblent réellement. targetConnectionId = -1 signifie "le joueur
		// local du host" (comportement solo inchangé) ; sinon on relaie les dégâts au client
		// concerné, qui les applique lui-même sur son propre HP via DamagePlayer.
public static void DamagePlayerTarget(int amount, int targetConnectionId, string? sourceName = null, DeathCause cause = DeathCause.Generic, float poisonDuration = 0f)
        {
            if (targetConnectionId == -1 || !NetworkManager.IsHost)
            {
                DamagePlayer(amount, sourceName, cause);
                if (poisonDuration > 0f)
                    ApplyPlayerPoison(poisonDuration);
                return;
            }
            NetworkManager.SendPlayerDamage(targetConnectionId, amount, poisonDuration);
		}

		/// <summary>
		/// Fait réapparaître le joueur après une mort sans ragdoll : réinitialise les PV,
		/// la position, et rend le contrôle au joueur.
		/// </summary>
		public static void RespawnPlayer()
		{
			if (!IsPlayerRagdolled) return;
			IsPlayerRagdolled = false;
			_playerDeathPendingRespawn = false;
			_playerDeathTimer = 0f;
			
			//  RÉINITIALISER TOUTES LES JAUGES
			playerHP = playerMaxHP;
			playerStamina = playerMaxStamina;
			staminaLocked = false;
			playerHunger = playerMaxHunger;
			playerThirst = playerMaxThirst;
			_starvationDamageAccumulator = 0f;
			
			_playerPos = _worldSpawnPos;
			World.IsUnderground = false;
			World.ForceExitDungeon();
			_cameraNeedsImmediateCenter = true;
		}

		// ═══════════════════════════════════════════════════════════════════
		// COMPAGNONS D'ARMURE : tant qu'un set complet est équipé, une petite créature
		// apprivoisée apparaît, suit le joueur (via le système Follow existant, voir
		// UpdateTamedAnimalTargets ci-dessous) et attaque automatiquement quiconque
		// l'attaque (voir NotifyPlayerAttackedByEntity, appelé depuis Entity.cs à chaque
		// coup porté au joueur). Elle disparaît dès qu'une pièce du set est retirée.
		// ═══════════════════════════════════════════════════════════════════
		private static readonly int[] CRAB_ARMOR_SET_IDS = { 253, 254, 255 };

		// ═══════════════════════════════════════════════════════════════════
		// "LES AMIS DE MES ENNEMIS SONT MES ENNEMIS" : synchronise chaque pet appartenant
		// à un PNJ (ex : loup de compagnie d'un gobelin, voir Entity.AddPet/OwnerNpcId) avec
		// l'hostilité de son maître. Tant que le maître est hostile ET cherche activement le
		// joueur, le pet passe en TamedAnimalMode.Attack (il chargera comme n'importe quel
		// monstre, voir la logique `shouldChase` dans Entity.Update). Dans tous les cas, tant
		// que le maître est hostile, le pet est marqué IsHostilePet et devient une cible valide
		// pour le joueur (voir GetEntitiesInAttackCone plus bas) — y compris si le maître est
		// actuellement en train de fuir : c'est le statut "hostile" du maître qui compte, pas
		// son état de poursuite du moment.
		// ═══════════════════════════════════════════════════════════════════
		private static void UpdateOwnedPetHostility()
		{
			//  MULTIJOUEUR : seul le host simule vraiment les PNJ/pets (Program.entities est
			// vide côté client) ; le résultat (IsHostilePet, TamedBehavior) devra être répercuté
			// aux clients via le snapshot réseau habituel, comme le reste de l'état des entités.
			if (NetworkManager.IsClient) return;

			foreach (var pet in entities)
			{
				if (!pet.IsAlive || !pet.IsTamed || pet.OwnerNpcId == null) continue;

				Entity? owner = null;
				foreach (var candidate in entities)
				{
					if (candidate.NetId == pet.OwnerNpcId.Value) { owner = candidate; break; }
				}

				pet.SyncHostilityWithOwner(owner);
			}
		}

		private static void UpdateArmorCompanions(float dt)
		{
			//  MULTIJOUEUR : seul le host simule les entités ; un client ne doit pas faire
			// apparaître sa propre copie locale du compagnon (elle ne serait jamais synchronisée).
			if (NetworkManager.IsClient) return;

			bool wantsCrab = HasFullArmorSet(CRAB_ARMOR_SET_IDS);

			if (!wantsCrab)
			{
				if (_crabCompanion != null)
				{
					if (_crabCompanion.IsAlive)
						Entity.SpawnKhamsinSmoke(_crabCompanion.WorldPos, 0.5f); // nuage de fumée à la disparition
					entities.Remove(_crabCompanion);
					_crabCompanion = null;
				}
				_crabRespawnCooldown = 0f;
				return;
			}

			if (_crabCompanion != null && !_crabCompanion.IsAlive)
				_crabCompanion = null; // mort au combat : on retentera l'apparition ci-dessous

			if (_crabCompanion == null)
			{
				if (_crabRespawnCooldown > 0f)
				{
					_crabRespawnCooldown -= dt;
					return;
				}
				SpawnCrabCompanion();
				_crabRespawnCooldown = 3f;
			}
		}

		/// <summary>
		/// Appelée depuis Entity.cs à chaque coup porté au joueur (voir les appels à
		/// Program.DamagePlayerTarget dans le combat de mêlée standard). Signale au(x)
		/// compagnon(s) actif(s) de riposter contre l'agresseur.
		/// </summary>
		public static void NotifyPlayerAttackedByEntity(Entity attacker, int targetConnectionId)
		{
			// targetConnectionId == -1 signifie "joueur local" (voir DamagePlayerTarget) : le
			// compagnon ne défend que CE joueur, pas un client distant touché ailleurs.
			if (targetConnectionId != -1) return;
			if (attacker == null || !attacker.IsAlive) return;
			if (_crabCompanion != null && _crabCompanion.IsAlive)
				_crabCompanion.PetCombatTarget = attacker;
		}

		private static void UpdateAnimalBreeding(float dt, float currentTime)
		{
		// Suspension globale si demandé
		if (!AnimalBreedingEnabled) return;

		// MISE À JOUR DES GESTATIONS (toujours effectuée, à chaque frame)
		var entitiesList = GetEntities();
			for (int i = 0; i < entitiesList.Count; i++)
			{
				var e = entitiesList[i];
				if (e.GestationTimer.HasValue)
				{
					e.GestationTimer -= dt;
					if (e.GestationTimer <= 0f)
					{
						e.GestationTimer = null;
						SpawnBabyNear(e);
					}
				}
			}

			//  COOLDOWN POUR LA RECHERCHE DE PARTENAIRES (augmenté)
			if (_breedingCooldown > 0f)
			{
				_breedingCooldown -= dt;
				return;
			}
			_breedingCooldown = BREEDING_CHECK_INTERVAL;

			//  AJOUT : Vérifier si c'est la nuit (darkness > 0.6)
			float darkness = GetDarknessAlpha();
			bool isNight = darkness > 0.6f;
			
			//  AJOUT : Si c'est la nuit, réduire drastiquement les chances (ou les annuler)
			float nightMultiplier = isNight ? 0.05f : 1.0f; // 95% de réduction la nuit

			// Recherche de partenaires (ne s'exécute que toutes les X secondes)
			for (int i = 0; i < entitiesList.Count; i++)
			{
				var e = entitiesList[i];
				if (e == null || !e.IsAlive) continue;
				if (e.Species == "human") continue;
				if (e.IsBaby) continue;
				if (e.GestationTimer.HasValue) continue;

				Entity? partner = null;
				float bestDist = BREEDING_DISTANCE;
				for (int j = 0; j < entitiesList.Count; j++)
				{
					if (i == j) continue;
					var other = entitiesList[j];
					if (!other.IsAlive) continue;
					if (other.Species != e.Species) continue;
					if (other.IsBaby) continue;
					if (other.GestationTimer.HasValue) continue;

					float dist = Vector2.Distance(e.WorldPos, other.WorldPos);
					if (dist < bestDist)
					{
						bestDist = dist;
						partner = other;
					}
				}

				if (partner != null && bestDist < BREEDING_DISTANCE)
				{
					//  Appliquer le multiplicateur de nuit
					float breedChance = 0.5f * nightMultiplier; // 0.5 devient 0.025 la nuit (2.5%)
					if (Random.Shared.NextDouble() < breedChance)
					{
						e.GestationTimer = GESTATION_DURATION;
					}
					else
					{
						partner.GestationTimer = GESTATION_DURATION;
					}
				}
			}
		}

		private static void SpawnBabyNear(Entity parent)
		{
			float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 50f;
			float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 50f;
			Vector2 babyPos = parent.WorldPos + new Vector2(offsetX, offsetY);

			var baby = new Entity(babyPos, parent.Species, false);
			baby.IsBaby = true;
			baby.Age = 0f;
			baby.GrowthTime = parent.GrowthTime; // hérite du temps de croissance de l'espèce
			// Réduire les stats pour un bébé
			baby.MaxHP = (int)(parent.MaxHP * 0.5f);
			baby.CurrentHP = baby.MaxHP;
			baby.Attack = (int)(parent.Attack * 0.3f);
			baby.Speed = parent.Speed * 0.7f;
			baby.Scale = 0.6f; // On va ajouter un champ Scale dans Entity (voir plus bas)
			// Éviter qu'un bébé soit commerçant
			baby.IsTrader = false;
			baby.IsTamed = parent.IsTamed;
			baby.OwnerName = parent.OwnerName;
			baby.TamedBehavior = parent.TamedBehavior;
			baby.HomePosition = parent.HomePosition;

			GetEntities().Add(baby);

			// Ajouter le bébé au chunk correspondant
			int tileX = (int)(babyPos.X / TileSize);
			int tileY = (int)(babyPos.Y / TileSize);
			var chunk = World.GetChunkAt(tileX, tileY);
			if (chunk != null)
				chunk.Entities.Add(baby);

		}

		public static void AddFloatingHeal(Vector2 worldPos, int amount)
		{
			if (float.IsNaN(worldPos.X) || float.IsNaN(worldPos.Y) ||
				float.IsInfinity(worldPos.X) || float.IsInfinity(worldPos.Y))
				return;
			
			if (amount <= 0) amount = 1;
			
			if (_floatingDamages.Count > MAX_FLOATING_DAMAGES)
				_floatingDamages.RemoveAt(0);
			
			Color color = new Color(100, 255, 100, 255); // Vert pour les soins
			
			var fd = new FloatingDamage(worldPos, amount, color);
			fd.OutlineColor = new Color(0, 90, 0, 255); // Contour vert foncé
			_floatingDamages.Add(fd);
		}

		// Sélecteur de couleurs : prévisualisation + 3 sliders
		private static void DrawColorPicker(int x, int y, string label, ref Color color, float scale = 1f)
		{
			// Synchroniser les sliders avec la couleur actuelle
			var (currentH, currentS, currentV) = ColorToHsv(color);
			if (Math.Abs(currentH - _hue) > 0.01f || Math.Abs(currentS - _saturation) > 0.01f || Math.Abs(currentV - _value) > 0.01f)
			{
				_hue = currentH;
				_saturation = currentS;
				_value = currentV;
			}

			DrawCreationText(label, x, y, Math.Max(10, (int)MathF.Round(14 * scale)), Color.White);

			int previewSize = Math.Max(30, (int)MathF.Round(50 * scale));
			int previewX = x;
			int previewY = y + 22;

			// Aperçu de la couleur
			Raylib.DrawRectangle(previewX, previewY, previewSize, previewSize, color);
			Raylib.DrawRectangleLines(previewX, previewY, previewSize, previewSize, new Color(80, 70, 50, 200));

			int sliderWidth = Math.Max(100, (int)MathF.Round(180 * scale));
			int sliderHeight = Math.Max(5, (int)MathF.Round(8 * scale));
			int sliderX = previewX + previewSize + Math.Max(8, (int)MathF.Round(15 * scale));
			int sliderStartY = previewY;

			// Slider de teinte
			DrawCreationText("Teinte", sliderX, sliderStartY - Math.Max(6, (int)MathF.Round(8 * scale)), Math.Max(9, (int)MathF.Round(11 * scale)));
			bool hueChanged = DrawSlider(sliderX, sliderStartY, sliderWidth, sliderHeight, ref _hue, HueGradient);

			// Slider de saturation
			int sliderGap = Math.Max(16, (int)MathF.Round(25 * scale));
			DrawCreationText("Saturation", sliderX, sliderStartY + sliderGap - Math.Max(6, (int)MathF.Round(8 * scale)), Math.Max(9, (int)MathF.Round(11 * scale)));
			bool satChanged = DrawSlider(sliderX, sliderStartY + sliderGap, sliderWidth, sliderHeight, ref _saturation, SaturationGradient);

			// Slider de luminosité
			DrawCreationText("Luminosité", sliderX, sliderStartY + sliderGap * 2 - Math.Max(6, (int)MathF.Round(8 * scale)), Math.Max(9, (int)MathF.Round(11 * scale)));
			bool valChanged = DrawSlider(sliderX, sliderStartY + sliderGap * 2, sliderWidth, sliderHeight, ref _value, ValueGradient);

			if (hueChanged || satChanged || valChanged)
			{
				color = HsvToColor(_hue, _saturation, _value);
			}
		}

		public static float GetAttackSwingProgress() => _attackSwingProgress;
    }
}
