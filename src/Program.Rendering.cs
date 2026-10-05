// Program.Rendering.cs
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

		public static Texture2D LootbagTexture = new Texture2D();

		public static Texture2D EyesTexture = new Texture2D();

		public static Texture2D MouthTexture = new Texture2D();

		public static Texture2D MouthOpenTexture = new Texture2D();

		public static Texture2D KhamsinBulletTexture = new Texture2D();

		public static Texture2D TreeLeafTexture = new Texture2D();

		private static List<Particle> _particles = new();

		private static Texture2D _uiBarConnectionTex;

		// Nuages
		
		private static Texture2D _pedalTexture;

		private static Texture2D _pedalPushTexture;

		private static Texture2D _dashboardTexture;

		private static Texture2D _arrowTextureCar;

		private static readonly Dictionary<string, Texture2D> _languageFlagTextures = new(StringComparer.OrdinalIgnoreCase);

		// Texture for remote player pointers
		private static Texture2D _serverPointerTex = new Texture2D();

		static Texture2D missingTexture;

		public static List<Texture2D> hairBaseTextures = new();

		public static List<Texture2D> hairBackTextures = new();

		public static List<Texture2D> hairOverlayTextures = new();

		public static Texture2D NoneStyleIcon = new Texture2D();

		public static List<Texture2D> beardBaseTextures = new();

		public static List<Texture2D> beardOverlayTextures = new();

		const float PLAYER_DEATH_PARTICLE_RISE = 90f;

		//  Découpe un texte en lignes qui tiennent dans maxWidth (mesure via Raylib.MeasureText,
        // donc cohérent avec le rendu réel de FontManager.DrawText).
        static List<string> WrapTextToWidth(string text, int fontSize, int maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }

            var words = text.Split(' ');
            string current = "";
            foreach (var word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (Raylib.MeasureText(candidate, fontSize) > maxWidth && current.Length > 0)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }

		//  Animation à reculons : compteur "brut" qui avance toujours normalement dans le temps ;
        // _animFrame/_animProg (ci-dessus) sont ensuite dérivés de ces valeurs brutes, à l'endroit
        // ou à l'envers selon _animReversed, sans rien changer au reste du moteur de rendu.
        static int _animRawFrame = 0;

		// radians/seconde
		private static Texture2D _eyeShieldTexture = new Texture2D();

		public static Texture2D _arrowTexture;

		private static Texture2D _gearTexture;
		private static Texture2D _tooltipWaterIcon;
		private static Texture2D _tooltipFireIcon;

		private static Texture2D _speechBubbleTexture;

		// alternances par seconde (environ)
		public static Texture2D GetMouthTextureFor(Entity e)
		{
			if (!string.Equals(e.RenderSpecies, "human", StringComparison.OrdinalIgnoreCase)) return default;
			if (e.IsTalking && MouthOpenTexture.Id != 0)
			{
				double phase = Raylib.GetTime() * MOUTH_FLAP_SPEED + (e.NetId.GetHashCode() & 0xFF) * 0.13;
				bool open = ((long)Math.Floor(phase) & 1) == 0;
				return open ? MouthOpenTexture : MouthTexture;
			}
			return MouthTexture;
		}

		public static List<Particle> GetParticleList() => _particles;

		public static List<Texture2D> EyeTextures = new();

		// Animation de danse
		static bool _isDancing = false;

		static Texture2D _haloTexture;

		public static Texture2D LeafUnderpantsTexture;

		public static List<Texture2D> EyeBaseTextures = new();

		// textures avec "R" (colorables)
		public static List<Texture2D> EyeOverlayTextures = new();

		// textures sans "R" (détails) [LEGACY - conservé pour compat]

		// ===== NOUVEAU SYSTÈME D'YEUX EN 5 COUCHES (+ la tête) =====
		// Ordre d'affichage (du fond vers l'avant) :
		//   1. EyeWhiteTextures     (Humaneyes{N}.png)     - blanc des yeux
		//   2. PupilLeftTextures    (Humanlpupil{N}.png)   - pupille gauche
		//    + PupilRightTextures   (Humanrpupil{N}.png)   - pupille droite
		//   3. Humanhead (texture de la tête, gérée par le squelette)
		//   4. EyebrowLeftTextures  (Humanleyebrow{N}.png) - sourcil gauche
		//    + EyebrowRightTextures (Humanreyebrow{N}.png) - sourcil droit
		public static List<Texture2D> EyeWhiteTextures = new();

		public static List<Texture2D> PupilLeftTextures = new();

		public static List<Texture2D> PupilRightTextures = new();

		public static List<Texture2D> EyebrowLeftTextures = new();

		public static List<Texture2D> EyebrowRightTextures = new();

		// Yeux fermés (clignement) : Humaneyes{N}closed.png
		public static List<Texture2D> EyeClosedTextures = new();

		// Décalage horizontal de base appliqué aux pupilles : la texture de pupille fait 1 unité de large
		// pour un emplacement d'œil de 2 unités, donc sans ce décalage la pupille peut sembler "collée"
		// à un bord de l'œil. On la décale de base vers la gauche à l'écran (vers la droite si le
		// personnage regarde vers la gauche), en plus du mouvement vers le curseur.
		public const float PUPIL_BASE_OFFSET_X = 0.5f;

		// Barre d'onglets du créateur de personnage (assets/gui/creator_bar_left.png, creator_bar_mid.png, creator_bar_right.png)
		static Texture2D _creatorBarLeft;

		static Texture2D _creatorBarMid;

		static Texture2D _creatorBarRight;

		static List<Texture2D> _selectTextures = new List<Texture2D>();

		public static List<Texture2D> carBodyTextures = new List<Texture2D>();

		public static List<Texture2D> wheelTextures = new List<Texture2D>();

		// Point d'ancrage utilisé pour la visée de la tête / du bras. Il est placé à
		// hauteur du buste/tête du personnage, pas au niveau de l'empreinte au sol,
		// pour que l'angle de visée reflète la vraie taille du sprite.
		public static Vector2 GetPlayerAimOrigin(Vector2 playerPos)
		{
			Vector2 visualPos = GetPlayerVisualPosition(playerPos);
			string speciesName = string.IsNullOrWhiteSpace(PlayerMorphSpecies) ? "human" : PlayerMorphSpecies;
			string speciesKey = speciesName.ToLowerInvariant();
			if (SpeciesData.Skeletons.TryGetValue(speciesKey, out var skeleton) && skeleton.Any(part => part.Name == "head"))
			{
				var (headPosition, _) = EntityRenderer.GetGlobalPartTransform(
					speciesName, _animState, _animFrame, _animProg, 1f,
					visualPos, SpeciesData.Skeletons, "head", _isPlayerInWater);
				return headPosition;
			}

			return visualPos + new Vector2(0f, -40f);
		}

		private static void ApplyEmote(string emote)
		{
			_currentEmote = emote;
			_isDancing = false;
			if (emote == "sit_floor")
			{
				// Keep the player in place but change animation to sitting on the floor.
				_animState = "sit_floor";
				_animFrame = 0;
				_animProg = 0f;
			}
			else if (emote == "sleep")
			{
				_animState = "sleep";
				_animFrame = 0;
				_animProg = 0f;
			}
			else if (emote == "wavedance")
			{
				_animState = "wavedance";
				_animFrame = 0;
				_animProg = 0f;
			}
			else if (emote == "hula_dance")
			{
				_animState = "hula_dance";
				_animFrame = 0;
				_animProg = 0f;
			}
		}

		private static void TriggerPlayerFootstepEffects()
		{
			SpawnPlayerFootstepParticle();
			PlayPlayerWalkSound();
		}

		private static void DrawCaveTransitionBars()
	{
		if (_caveTransitionPhase == CaveTransitionPhase.None)
			return;

		int screenWidth = Raylib.GetScreenWidth();
		int screenHeight = Raylib.GetScreenHeight();
		float maxBarHeight = screenHeight * 0.18f;
		float barHeight = maxBarHeight * _caveTransitionBarProgress;
		int barHeightInt = (int)MathF.Round(barHeight);
		if (barHeightInt <= 0)
			return;

		Color barColor = new Color(0, 0, 0, 245);
		Raylib.DrawRectangle(0, 0, screenWidth, barHeightInt, barColor);
		Raylib.DrawRectangle(0, screenHeight - barHeightInt, screenWidth, barHeightInt, barColor);
	}

		private static void ApplyFullscreenToggle()
		{
			_fullscreen = !_fullscreen;
			if (_fullscreen)
			{
				ScreenWidth = Raylib.GetMonitorWidth(0);
				ScreenHeight = Raylib.GetMonitorHeight(0);
				Raylib.SetWindowSize(ScreenWidth, ScreenHeight);
				Raylib.ToggleFullscreen();
			}
			else
			{
				Raylib.ToggleFullscreen();
				ScreenWidth = 1280;
				ScreenHeight = 720;
				Raylib.SetWindowSize(ScreenWidth, ScreenHeight);
			}
			//  Le masque de lumière doit être recréé à la bonne taille dès le prochain
			// DrawLighting : sans ça, la première frame après la bascule plein écran peut
			// utiliser une taille obsolète et laisser une bande non recouverte en bas de l'écran.
			InvalidateLightMask();
		}

		public static List<(string Effect, float Remaining, float Total)> GetActiveBoosts()
        {
            var boosts = new List<(string, float, float)>();

            if (_speedPotionTimer > 0f)
            {
                boosts.Add(("speed", _speedPotionTimer, _speedPotionTotalDuration));
            }

            if (_strengthPotionTimer > 0f)
            {
                boosts.Add(("strength", _strengthPotionTimer, _strengthPotionTimerMax));
            }

            if (_resistancePotionTimer > 0f)
            {
                boosts.Add(("resistance", _resistancePotionTimer, _resistancePotionTimerMax));
            }

            if (_lightPotionTimer > 0f)
            {
                boosts.Add(("light", _lightPotionTimer, _lightPotionTimerMax));
            }

            if (_heatResistPotionTimer > 0f)
            {
                boosts.Add(("heatresist", _heatResistPotionTimer, _heatResistPotionTimerMax));
            }

            if (_coldResistPotionTimer > 0f)
            {
                boosts.Add(("coldresist", _coldResistPotionTimer, _coldResistPotionTimerMax));
            }

            if (_invisibilityPotionTimer > 0f)
            {
                boosts.Add(("invisibility", _invisibilityPotionTimer, _invisibilityPotionTimerMax));
            }

            if (_satietyTimer > 0f)
            {
                boosts.Add(("satiety", _satietyTimer, SATIETY_DURATION));
            }

            if (_poisonTimer > 0f)
            {
                float poisonTotal = _poisonDuration > 0f ? _poisonDuration : _poisonTimer;
                boosts.Add(("poison", _poisonTimer, poisonTotal));
            }

            if (HasFullArmorSet(CRAB_ARMOR_SET_IDS))
            {
                boosts.Add(("crab_companion", 0f, 0f));
            }

            return boosts;
        }

		static void SpawnPickupParticles(Vector2 position)
		{
			int particleCount = Random.Shared.Next(5, 10);
			for (int i = 0; i < particleCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(50, 120);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float size = Random.Shared.Next(2, 5);
				float lifetime = Random.Shared.Next(200, 400) / 1000f;
				Color color = new Color(255, 220, 100, 200);
				_particles.Add(new Particle(position, velocity, color, size, lifetime));
			}
		}

		static RenderTexture2D _lightMask;

		static void DrawPolygon(Vector2[] points, Color color)
		{
			if (points.Length < 3) return;
			for (int i = 1; i < points.Length - 1; i++)
			{
				Raylib.DrawTriangle(points[0], points[i], points[i + 1], color);
			}
		}

		static void SpawnDeathParticles(Vector2 position, Color baseColor)
		{
			int particleCount = Random.Shared.Next(24, 36);
			for (int i = 0; i < particleCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(60, 190);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float size = Random.Shared.Next(6, 14);
				float lifetime = Random.Shared.Next(700, 1200) / 1000f;
				float highlight = Random.Shared.Next(25, 46) / 100f;
				Color particleColor = new Color(
					(byte)(baseColor.R + (255 - baseColor.R) * highlight),
					(byte)(baseColor.G + (255 - baseColor.G) * highlight),
					(byte)(baseColor.B + (255 - baseColor.B) * highlight), (byte)255);
				_particles.Add(new Particle(position, velocity, particleColor, size, lifetime));
			}
		}

		static void SpawnPlayerDeathEffect(Vector2 position)
		{
			for (int i = 0; i < 70; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
				float speed = Random.Shared.Next(80, 220);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				velocity.Y -= Random.Shared.Next(40, 120);
				float size = Random.Shared.Next(3, 8);
				float lifetime = 1.4f + (float)Random.Shared.NextDouble() * 0.8f;
				Color color = new Color(90, 140, 255, 240);
				_particles.Add(new Particle(position, velocity, color, size, lifetime));
			}
		}

		static void UpdatePlayerDeathParticles(float dt)
		{
			for (int i = _particles.Count - 1; i >= 0; i--)
			{
				var p = _particles[i];
				p.Position += new Vector2(0f, -PLAYER_DEATH_PARTICLE_RISE * dt * 0.35f);
				p.Update(dt);
				if (!p.IsAlive)
					_particles.RemoveAt(i);
			}
		}

		static void DrawPlayerDeathParticles()
		{
			foreach (var particle in _particles)
			{
				float alpha = particle.GetAlpha();
				byte alphaByte = (byte)(alpha * 255);
				Color drawColor = new Color(particle.Color.R, particle.Color.G, particle.Color.B, alphaByte);
				Raylib.DrawCircle((int)particle.Position.X, (int)particle.Position.Y, particle.Size, drawColor);
			}
		}

		private static void SpawnCatchParticles(Vector2 pos)
		{
			Random rand = new Random();
			for (int i = 0; i < 12; i++)
			{
				float angle = (float)(rand.NextDouble() * Math.PI * 2);
				float speed = rand.Next(50, 150);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float size = rand.Next(3, 7);
				float lifetime = rand.Next(300, 600) / 1000f;
				Color color = new Color(255, 220, 100, 200);
				_particles.Add(new Particle(pos, velocity, color, size, lifetime));
			}
		}

		private static void DrawCarDashboard(Car car)
		{
			if (PlayerCurrentCar == null) return;
			if (_dashboardTexture.Id == 0) return;
			
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			
			float dashboardSize = 160f;
			float dashboardX = (sw - dashboardSize) / 2f;
			float dashboardY = sh - dashboardSize + 25f;
			
			// Fond du dashboard
			Rectangle srcRect = new Rectangle(0, 0, _dashboardTexture.Width, _dashboardTexture.Height);
			Rectangle destRect = new Rectangle(dashboardX, dashboardY, dashboardSize, dashboardSize);
			Raylib.DrawTexturePro(_dashboardTexture, srcRect, destRect, Vector2.Zero, 0, Color.White);
			
			float currentSpeed = MathF.Sqrt(car.VelocityX * car.VelocityX + car.VelocityY * car.VelocityY);
			const float SPEED_FACTOR = 0.1f;
			int speedKmh = (int)(currentSpeed * SPEED_FACTOR);
			float rpm = Math.Clamp((currentSpeed / CAR_MAX_SPEED) * 6f, 0f, 6f);
			
			// Aiguille de vitesse (à droite)
			if (_arrowTextureCar.Id != 0)
			{
				float minAngle = -135f;
				float maxAngle = 135f;
				float maxDisplaySpeed = 200f;
				float speedPercent = Math.Clamp(speedKmh / maxDisplaySpeed, 0f, 1f);
				float arrowAngle = minAngle + (maxAngle - minAngle) * speedPercent;
				
				// Centre du compteur de vitesse
				float centerX = dashboardX + dashboardSize * 0.68f;
				float centerY = dashboardY + dashboardSize * 0.55f;
				float arrowLength = dashboardSize * 0.25f;
				
				Rectangle arrowSrcRect = new Rectangle(0, 0, _arrowTextureCar.Width, _arrowTextureCar.Height);
				Rectangle arrowDestRect = new Rectangle(
					centerX - arrowLength / 2f,
					centerY - arrowLength / 2f,
					arrowLength,
					arrowLength
				);
				Vector2 origin = new Vector2(arrowLength / 2f, arrowLength / 2f);
				
				Raylib.DrawTexturePro(_arrowTextureCar, arrowSrcRect, arrowDestRect, origin, arrowAngle, Color.White);
			}
			
			// Affichage des valeurs
			string speedText = speedKmh.ToString();
			int fontSize = 28;
			int textWidth = FontManager.MeasureText(speedText, fontSize);
			float speedX = dashboardX + dashboardSize * 0.68f - textWidth / 2f;
			float speedY = dashboardY + dashboardSize * 0.72f;
			FontManager.DrawText(speedText, (int)speedX, (int)speedY, fontSize, Color.White);
			
			string rpmText = $"{rpm:F1}";
			int rpmFontSize = 22;
			int rpmWidth = FontManager.MeasureText(rpmText, rpmFontSize);
			float rpmX = dashboardX + dashboardSize * 0.32f - rpmWidth / 2f;
			float rpmY = dashboardY + dashboardSize * 0.72f;
			FontManager.DrawText(rpmText, (int)rpmX, (int)rpmY, rpmFontSize, new Color(200, 200, 100, 255));
		}

		private static void DrawPedal(Texture2D tex, float x, float y, float size)
		{
			if (tex.Id == 0) return;
			
			Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
			Rectangle destRect = new Rectangle(x, y, size, size);
			Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0, Color.White);
		}

		private static void DrawPedalWithPressure(Texture2D tex, float x, float y, float size, string label, float pressure)
		{
			// Ombre portée (plus sombre si enfoncé)
			byte shadowAlpha = (byte)(80 + pressure * 80);
			Raylib.DrawRectangleRounded(
				new Rectangle(x + 4, y + 4, size, size), 
				0.15f, 8, 
				new Color((byte)0, (byte)0, (byte)0, shadowAlpha)
			);
			
			// Texture de la pédale
			Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
			Rectangle destRect = new Rectangle(x, y, size, size);
			Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0, Color.White);
			
			// Indicateur de pression (barre de progression)
			if (pressure > 0.05f)
			{
				float barWidth = size * 0.7f;
				float barHeight = 4f;
				float barX = x + (size - barWidth) / 2f;
				float barY = y + size + 6f;
				
				Raylib.DrawRectangleRounded(
					new Rectangle(barX, barY, barWidth, barHeight), 
					0.5f, 4, 
					new Color(50, 50, 50, 180)
				);
				Raylib.DrawRectangleRounded(
					new Rectangle(barX, barY, barWidth * pressure, barHeight), 
					0.5f, 4, 
					new Color(100, 255, 100, 220)
				);
			}
		}

		static void UpdateSelectAnimation(float dt)
		{
			_selectAnimTimer += dt;
			if (_selectAnimTimer >= _selectAnimSpeed)
			{
				_selectAnimTimer = 0f;
				_selectAnimFrame = (_selectAnimFrame + 1) % _selectTextures.Count;
			}
		}

		public static void DrawCarWithWheels(Vector2 pos, float angle, float wheelAngle, float wheelSpinAngle)
		{
			if (carBodyTextures.Count == 0) return;
			float layerHeightStep = 1.0f;
			float bodyScale = 2.5f;
			float carWidth = 48f;
			float carLength = 96f;
			Vector2 wheelFL_Offset = new Vector2(-carWidth * 1.0f, -carLength * 0.65f);
			Vector2 wheelFR_Offset = new Vector2(carWidth * 1.0f, -carLength * 0.65f);
			Vector2 wheelRL_Offset = new Vector2(-carWidth * 1.0f, carLength * 0.75f);
			Vector2 wheelRR_Offset = new Vector2(carWidth * 1.0f, carLength * 0.75f);
			float wheelScale = bodyScale * 0.9f;
			float frontWheelAngle = wheelAngle;
			DrawWheel3D(pos, angle, wheelRL_Offset, 0f, wheelSpinAngle, wheelScale);
			DrawWheel3D(pos, angle, wheelRR_Offset, 0f, wheelSpinAngle, wheelScale);
			DrawWheel3D(pos, angle, wheelFL_Offset, frontWheelAngle, wheelSpinAngle, wheelScale);
			DrawWheel3D(pos, angle, wheelFR_Offset, frontWheelAngle, wheelSpinAngle, wheelScale);
			int totalLayers = carBodyTextures.Count;
			for (int i = 0; i < totalLayers; i++)
			{
				var tex = carBodyTextures[i];
				if (tex.Id == 0) continue;
				float brightness = (float)(i + 1) / totalLayers;
				brightness = MathF.Pow(brightness, 0.7f);
				brightness = Math.Max(brightness, 0.25f);
				Color tint = new Color((int)(255 * brightness), (int)(255 * brightness), (int)(255 * brightness), 255);
				float yOffset = i * layerHeightStep;
				Vector2 drawPos = new Vector2(pos.X, pos.Y - yOffset);
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(drawPos.X, drawPos.Y, tex.Width * bodyScale, tex.Height * bodyScale);
				Vector2 origin = new Vector2(destRect.Width / 2, destRect.Height / 2);
				Raylib.DrawTexturePro(tex, srcRect, destRect, origin, angle, tint);
			}
		}

		static void DrawWheel3D(Vector2 carPos, float carAngle, Vector2 localOffset, float steerAngle, float spinAngle, float wheelScale)
		{
			if (wheelTextures.Count == 0) return;
			float layerHeightStep = 0.8f;
			float rad = carAngle * MathF.PI / 180f;
			float cos = MathF.Cos(rad);
			float sin = MathF.Sin(rad);
			Vector2 rotatedOffset = new Vector2(localOffset.X * cos - localOffset.Y * sin, localOffset.X * sin + localOffset.Y * cos);
			Vector2 wheelBasePos = carPos + rotatedOffset;
			float totalAngle = carAngle + steerAngle;
			for (int layer = 0; layer < wheelTextures.Count; layer++)
			{
				Texture2D wheelTex = wheelTextures[layer];
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

		public static void DrawCar(Vector2 pos, float angle)
		{
			if (carBodyTextures.Count == 0) return;
			float layerHeightStep = 1.0f;
			float scale = 2.5f;
			for (int i = 0; i < carBodyTextures.Count; i++)
			{
				var tex = carBodyTextures[i];
				if (tex.Id == 0) continue;
				float yOffset = i * layerHeightStep * scale;
				Vector2 drawPos = new Vector2(pos.X, pos.Y - yOffset);
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(drawPos.X, drawPos.Y, tex.Width * scale, tex.Height * scale);
				Vector2 origin = new Vector2(destRect.Width / 2, destRect.Height / 2);
				Raylib.DrawTexturePro(tex, srcRect, destRect, origin, angle, Color.White);
			}
		}

		static void SafeDrawTexture(Texture2D tex, Vector2 pos, float rot, float scale, Color tint)
		{
		if (tex.Id == 0 || tex.Width <= 0 || tex.Height <= 0)
			return;

		if (float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
		if (float.IsNaN(pos.X) || float.IsInfinity(pos.X)) pos.X = 0;
		if (float.IsNaN(pos.Y) || float.IsInfinity(pos.Y)) pos.Y = 0;

		try
		{
			Raylib.DrawTextureEx(tex, pos, rot, scale, tint);
		}
		catch (AccessViolationException ex)
		{
			Console.WriteLine($" SafeDrawTexture: accès invalide sur texture (Id={tex.Id}) - {ex.Message}");
		}
	}
    }
}
