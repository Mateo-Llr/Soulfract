// World.Water.cs
#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
	public static partial class World
	{
		// ═══════════════════════════════════════════════════════════════════
		//  RENDU DE L'EAU PAR SHADER (caustiques procédurales)
		//
		//  PRINCIPE (important, relire avant de retoucher) : l'eau n'est PAS
		//  censée avoir un rendu individuel par tuile. Il faut imaginer une
		//  unique image de bruit, fixe et continue, qui recouvre TOUTE la
		//  carte comme une texture de fond derrière l'écran de jeu — chaque
		//  tuile d'eau n'est qu'un "trou" découpé dans le décor qui laisse
		//  voir un morceau de cette image. Deux tuiles d'eau adjacentes
		//  doivent donc montrer un fragment de la MÊME image continue, peu
		//  importe l'ordre dans lequel elles sont dessinées.
		//
		//  Pour garantir ça de façon fiable, le bruit n'est PAS calculé à
		//  partir de la position du vertex de la tuile (fragPosition) ni
		//  d'un uniform réglé tuile par tuile (uTileOrigin) : les deux
		//  approches précédentes se sont révélées fragiles, car raylib
		//  regroupe (batch) les DrawTexturePro consécutifs de même texture/
		//  shader en un minimum de draw calls réels. Un uniform posé juste
		//  avant chaque DrawTexturePro est une valeur GLOBALE au shader : au
		//  moment du flush du batch, TOUTES les tuiles du batch se
		//  retrouvent dessinées avec la DERNIÈRE valeur posée -> bruit
		//  identique par blocs/colonnes de tuiles. Même en repassant par
		//  fragPosition (position monde via le vertex shader), le résultat
		//  restait incohérent (pas de variation spatiale visible, juste un
		//  effet de pulsation liée à uTime).
		//
		//  LA SOLUTION ROBUSTE : calculer le bruit en ESPACE ÉCRAN, à partir
		//  de gl_FragCoord. C'est une valeur intrinsèque au pixel, fournie
		//  par le GPU pour CHAQUE fragment individuellement, sans dépendre
		//  d'aucun attribut de vertex ni d'aucun uniform réglé par tuile :
		//  aucune interpolation, aucun risque de batching. On reconvertit
		//  ensuite gl_FragCoord en position MONDE via les paramètres de la
		//  caméra (target/offset/zoom), réglés en uniforms UNE SEULE FOIS
		//  PAR FRAME (comme uTime) -> le bruit reste ancré au monde (ne
		//  glisse pas à l'écran quand la caméra bouge) tout en étant garanti
		//  continu et identique pour toutes les tuiles, quel que soit
		//  l'ordre/le regroupement des draw calls.
		// ═══════════════════════════════════════════════════════════════════

		// Vertex shader minimal : on n'a plus besoin de calculer une position
		// monde par-vertex (voir plus haut), donc pas besoin de varying custom.
		// On garde un vertex shader explicite (plutôt que le shader par défaut
		// de raylib) uniquement pour être sûr à 100% des noms d'attributs/
		// varyings utilisés par le fragment shader.
		private const string WaterVertexShaderSrc = @"
#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;

out vec2 fragTexCoord;
out vec4 fragColor;

uniform mat4 mvp;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
";

		private const string WaterFragmentShaderSrc = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform float uTime;
uniform vec3 uColorDeep;
uniform vec3 uColorShallow;
uniform vec3 uColorCaustic;
uniform float uCausticStrength;

// fragColor.r porte la PROFONDEUR de la tuile (0 = bord/plage -> turquoise,
// 1 = eau profonde -> bleu marine), encodée par-vertex côté C# à partir de
// GetHeightAt (bruit d'altitude du monde). C'est une donnée PAR-VERTEX,
// posée au moment du DrawTexturePro de chaque tuile : contrairement à un
// uniform, elle voyage correctement avec chaque tuile même quand raylib
// regroupe plusieurs tuiles dans un seul batch/draw call. fragColor.g/b
// restent inutilisés (255) ; fragColor.a reste l'alpha existant (fade,
// transitions, etc.), inchangé.

// Caméra & fenêtre, pour reconvertir gl_FragCoord (espace écran, réglé UNE
// SEULE FOIS par frame) en position MONDE. Formule inverse de celle que
// raylib utilise pour GetWorldToScreen2D (rotation ignorée : le jeu ne
// tourne pas la caméra) :
//   screen = (world - target) * zoom + offset
//   => world = (screen - offset) / zoom + target
uniform vec2 uCamTarget;
uniform vec2 uCamOffset;
uniform float uCamZoom;
uniform float uScreenHeight;

float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Bruit de valeur 2D (interpolé) — léger et suffisant pour l'effet.
float valueNoise(vec2 p)
{
	vec2 i = floor(p);
	vec2 f = fract(p);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void main()
{
	// gl_FragCoord est en coordonnées fenêtre OpenGL (origine bas-gauche,
	// Y vers le haut) : on le remet dans la convention raylib (origine
	// haut-gauche, Y vers le bas) avant d'inverser la transformation caméra.
	vec2 screenPosRaylib = vec2(gl_FragCoord.x, uScreenHeight - gl_FragCoord.y);
	vec2 worldPos = (screenPosRaylib - uCamOffset) / uCamZoom + uCamTarget;

	// Grille de pixelisation pour garder un style pixel-art : on fige le
	// bruit sur des blocs de pixelStep px avant de l'échantillonner.
	const float pixelStep = 4.0;
	vec2 pixelUV = floor(worldPos / pixelStep) * pixelStep;

	// --- Bruit 0 : WARP lent, distord les UV des couches suivantes pour un
	// mouvement plus organique qu'un simple bruit de valeur qui défile. ---
	vec2 warpUv = pixelUV * 0.004 + vec2(uTime * 0.015, -uTime * 0.011);
	float warpX = valueNoise(warpUv) - 0.5;
	float warpY = valueNoise(warpUv + vec2(31.7, 5.2)) - 0.5;
	vec2 warp = vec2(warpX, warpY) * 24.0;

	// --- Couleur de base : PROFONDEUR RÉELLE de la tuile (bruit d'altitude
	// du monde), pas un bruit procédural indépendant. C'est ce qui fait
	// passer l'eau du turquoise (bord/plage, tuiles hautes) au bleu marine
	// profond (eau profonde, tuiles basses), en suivant fidèlement le
	// contour des côtes généré par le monde.
	float depthFactor = clamp(fragColor.r, 0.0, 1.0);
	vec3 depthColor = mix(uColorShallow, uColorDeep, depthFactor);

	// --- Bruit 1 : léger chatoiement par-dessus la couleur de profondeur ---
	// Fréquence basse -> grandes taches qui s'étendent sur plusieurs
	// tuiles, comme une seule et même image de fond continue. On ne bascule
	// plus entièrement entre deux couleurs : on module juste la luminosité
	// de la couleur de profondeur, pour garder les teintes cohérentes avec
	// la bathymétrie tout en gardant un aspect vivant/texturé.
	vec2 colorUV = (pixelUV + warp) * 0.004 + vec2(uTime * 0.010, uTime * 0.007);
	float colorNoise = valueNoise(colorUV);
	vec3 waterColor = depthColor * (0.88 + 0.24 * colorNoise);

	// --- Bruit 2 : CAUSTIQUES, deux couches qui défilent en sens opposés ---
	// Les filaments lumineux apparaissent là où les deux couches se
	// recoupent (différence proche de 0).
	vec2 causticUv1 = (pixelUV + warp) * 0.014 + vec2(uTime * 0.07, -uTime * 0.05);
	vec2 causticUv2 = (pixelUV + warp) * 0.019 - vec2(uTime * 0.05, uTime * 0.08);
	float n1 = valueNoise(causticUv1);
	float n2 = valueNoise(causticUv2);
	float caustic = 1.0 - abs(n1 - n2) * 2.0;
	caustic = clamp(caustic, 0.0, 1.0);
	caustic = smoothstep(0.6, 0.92, caustic) * uCausticStrength;

	vec3 color = mix(waterColor, uColorCaustic, caustic);

	finalColor = vec4(color, fragColor.a);
}
";

		private static Shader _waterShader;
		private static bool _waterShaderLoaded = false;
		private static bool _waterShaderFailed = false;

		private static int _waterLocTime;
		private static int _waterLocColorDeep;
		private static int _waterLocColorShallow;
		private static int _waterLocColorCaustic;
		private static int _waterLocCausticStrength;
		private static int _waterLocCamTarget;
		private static int _waterLocCamOffset;
		private static int _waterLocCamZoom;
		private static int _waterLocScreenHeight;

		private static Texture2D _waterWhitePixel;
		private static bool _waterWhitePixelLoaded = false;

		// Plage d'ÉLÉVATION (WorldGeneration.GetElevation, valeur continue
		// 0..1, PAS la hauteur entière quantifiée GetHeightAt) utilisée pour
		// dégrader la couleur turquoise -> bleu marine :
		//  - WaterShallowElevation : élévation la plus HAUTE encore considérée
		//    comme eau (juste avant la plage) -> uColorShallow (turquoise).
		//    Alignée sur OCEAN_THRESHOLD de WorldGeneration.cs (0.32f).
		//  - WaterDeepElevation : élévation la plus basse (fond marin) ->
		//    uColorDeep (bleu marine). 0 = minimum théorique du bruit.
		// On utilise l'élévation continue plutôt que GetHeightAt (int) car
		// GetHeightAt = floor(elevation*8) ne produit que 3 paliers bruts
		// (0/1/2) pour toute l'eau -> dégradé beaucoup trop grossier/plat.
		private const float WaterShallowElevation = 0.32f; // = OCEAN_THRESHOLD
		private const float WaterDeepElevation = 0.0f;

		// Auparavant BeginShaderMode/EndShaderMode étaient appelés à CHAQUE tuile
		// d'eau, ce qui force raylib à flusher son batch de rendu à chaque fois
		// (très coûteux dès qu'il y a beaucoup d'eau à l'écran). On ne bascule
		// maintenant le mode shader qu'aux transitions eau <-> non-eau : voir
		// EnsureWaterShaderActive / EnsureWaterShaderInactive, appelées depuis
		// la boucle de rendu (World.Core.cs) autour des tuiles d'eau.
		private static bool _waterShaderActive = false;

		private static void EnsureWaterShaderActive()
		{
			EnsureWaterShaderLoaded();
			if (_waterShaderFailed) return;
			if (!_waterShaderActive)
			{
				Raylib.BeginShaderMode(_waterShader);
				_waterShaderActive = true;
			}
		}

		private static void EnsureWaterShaderInactive()
		{
			if (_waterShaderActive)
			{
				Raylib.EndShaderMode();
				_waterShaderActive = false;
			}
		}

		private static void EnsureWaterShaderLoaded()
		{
			if (_waterShaderLoaded || _waterShaderFailed) return;
			_waterShaderLoaded = true;

			_waterShader = Raylib.LoadShaderFromMemory(WaterVertexShaderSrc, WaterFragmentShaderSrc);

			// LoadShaderFromMemory retombe sur l'id du shader par défaut de raylib (>0)
			// si la compilation échoue : on vérifie donc explicitement que nos uniforms
			// existent bien plutôt que de se fier uniquement à _waterShader.Id != 0.
			_waterLocTime = Raylib.GetShaderLocation(_waterShader, "uTime");
			_waterLocColorDeep = Raylib.GetShaderLocation(_waterShader, "uColorDeep");
			_waterLocColorShallow = Raylib.GetShaderLocation(_waterShader, "uColorShallow");
			_waterLocColorCaustic = Raylib.GetShaderLocation(_waterShader, "uColorCaustic");
			_waterLocCausticStrength = Raylib.GetShaderLocation(_waterShader, "uCausticStrength");
			_waterLocCamTarget = Raylib.GetShaderLocation(_waterShader, "uCamTarget");
			_waterLocCamOffset = Raylib.GetShaderLocation(_waterShader, "uCamOffset");
			_waterLocCamZoom = Raylib.GetShaderLocation(_waterShader, "uCamZoom");
			_waterLocScreenHeight = Raylib.GetShaderLocation(_waterShader, "uScreenHeight");

			if (_waterShader.Id == 0 || _waterLocTime == -1 || _waterLocColorDeep == -1)
			{
				Console.WriteLine(" [Water] Échec de compilation/lien du shader d'eau : repli sur le rendu uni.");
				_waterShaderFailed = true;
				return;
			}

			// uColorShallow = turquoise (bord/plage, tuiles hautes)
			// uColorDeep    = bleu marine profond (eau profonde, tuiles basses)
			SetShaderColor(_waterLocColorDeep, new Vector3(0.02f, 0.09f, 0.28f));
			SetShaderColor(_waterLocColorShallow, new Vector3(0.10f, 0.75f, 0.68f));
			SetShaderColor(_waterLocColorCaustic, new Vector3(0.85f, 0.97f, 1.00f));
			Raylib.SetShaderValue(_waterShader, _waterLocCausticStrength, 0.35f, ShaderUniformDataType.Float);

			// Console.WriteLine(" [Water] Shader de caustiques chargé avec succès.");
		}

		private static void SetShaderColor(int loc, Vector3 rgb01)
		{
			Raylib.SetShaderValue(_waterShader, loc, rgb01, ShaderUniformDataType.Vec3);
		}

		private static void EnsureWaterWhitePixelLoaded()
		{
			if (_waterWhitePixelLoaded) return;
			_waterWhitePixelLoaded = true;
			Image img = Raylib.GenImageColor(1, 1, Color.White);
			_waterWhitePixel = Raylib.LoadTextureFromImage(img);
			Raylib.UnloadImage(img);
		}

		// Réglé UNE SEULE FOIS PAR FRAME, avant la boucle de tuiles — jamais
		// tuile par tuile. C'est ce qui garantit qu'aucun batching de raylib
		// ne peut faire "sauter" la valeur entre deux tuiles d'eau : toutes
		// les tuiles dessinées cette frame partagent exactement les mêmes
		// uniforms de temps/caméra, et c'est gl_FragCoord (par-pixel, géré
		// par le GPU) qui fait toute la différenciation spatiale.
		private static void UpdateWaterShaderTime(float currentTime)
		{
			EnsureWaterShaderLoaded();
			if (_waterShaderFailed) return;

			Raylib.SetShaderValue(_waterShader, _waterLocTime, currentTime, ShaderUniformDataType.Float);

			Camera2D camera = Program.GetCurrentCamera();
			if (_waterLocCamTarget != -1)
			{
				float[] target = { camera.Target.X, camera.Target.Y };
				Raylib.SetShaderValue(_waterShader, _waterLocCamTarget, target, ShaderUniformDataType.Vec2);
			}
			if (_waterLocCamOffset != -1)
			{
				float[] offset = { camera.Offset.X, camera.Offset.Y };
				Raylib.SetShaderValue(_waterShader, _waterLocCamOffset, offset, ShaderUniformDataType.Vec2);
			}
			if (_waterLocCamZoom != -1)
			{
				float zoom = camera.Zoom == 0 ? 1f : camera.Zoom;
				Raylib.SetShaderValue(_waterShader, _waterLocCamZoom, zoom, ShaderUniformDataType.Float);
			}
			if (_waterLocScreenHeight != -1)
			{
				float screenH = Raylib.GetScreenHeight();
				Raylib.SetShaderValue(_waterShader, _waterLocScreenHeight, screenH, ShaderUniformDataType.Float);
			}
		}

		private static void DrawWaterTileShader(int worldX, int worldY, int drawY, int ts, byte alpha)
		{
			EnsureWaterShaderLoaded();

			if (_waterShaderFailed)
			{
				Raylib.DrawRectangle(worldX * ts, drawY, ts, ts, new Color((byte)20, (byte)70, (byte)130, alpha));
				return;
			}

			EnsureWaterWhitePixelLoaded();

			// Profondeur de CETTE tuile, encodée dans le canal rouge du tint
			// (donnée par-vertex : correcte même si raylib regroupe plusieurs
			// tuiles dans un même batch, contrairement à un uniform réglé ici).
			// 0 = élévation haute (bord/plage) -> turquoise, 1 = élévation
			// basse (eau profonde) -> bleu marine. On utilise l'élévation
			// CONTINUE (WorldGeneration.GetElevation), pas GetHeightAt (int
			// quantifié sur seulement 3 paliers pour toute l'eau), pour un
			// dégradé lisse.
			// L'élévation fait partie du climat déjà calculé par la préparation de la
			// grille de rendu. Repasser directement par GetElevation ici recalculait
			// plusieurs octaves de bruit pour chaque tuile d'eau, à chaque frame.
			float elevation = GetClimateAt(worldX, worldY).elevation;
			float depthFactor = (WaterShallowElevation - elevation) / (WaterShallowElevation - WaterDeepElevation);
			depthFactor = Math.Clamp(depthFactor, 0f, 1f);
			byte depthByte = (byte)(depthFactor * 255f);

			// Aucun uniform réglé ici : c'est tout l'intérêt de l'approche par
			// gl_FragCoord pour le BRUIT (voir le grand commentaire en tête de
			// fichier). La profondeur, elle, est une vraie donnée par-tuile :
			// elle voyage via la couleur du vertex (tint), pas via un uniform,
			// donc elle reste correcte tuile par tuile malgré le batching.
			Raylib.DrawTexturePro(_waterWhitePixel,
				new Rectangle(0, 0, 1, 1),
				new Rectangle(worldX * ts, drawY, ts, ts),
				Vector2.Zero, 0, new Color(depthByte, (byte)255, (byte)255, alpha));
		}

		public static void UnloadWaterShader()
		{
			if (_waterShaderLoaded && !_waterShaderFailed) Raylib.UnloadShader(_waterShader);
			if (_waterWhitePixelLoaded) Raylib.UnloadTexture(_waterWhitePixel);
			_waterShaderLoaded = false;
			_waterShaderFailed = false;
			_waterWhitePixelLoaded = false;
		}
	}
}
