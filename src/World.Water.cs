// World.Water.cs
#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
	public static partial class World
	{
		// ═══════════════════════════════════════════════════════════════════
		//  EAU STYLE "PIXEL ART TOP-DOWN" (cf. https://youtu.be/pcd4DdqO_BM)
		//
		//  Trois effets, tous calculés dans UN SEUL fragment shader :
		//
		//   1) CAUSTIQUES FINES : deux réseaux de Voronoï animés (cellules qui
		//      "respirent"), dont on ne garde que les arêtes très fines, à
		//      faible contraste -> le léger filet de lumière du fond de l'eau.
		//      + quelques scintillements ponctuels (sparkles).
		//
		//   2) MOUSSE DE RIVAGE : un réseau de Voronoï plus gros dont les arêtes
		//      sont dessinées en blanc pur dans une bande près de la côte. Les
		//      cellules les plus éloignées du rivage disparaissent au hasard
		//      (bord irrégulier), et la bande avance/recule avec une "marée".
		//      La distance au rivage est calculée EXACTEMENT dans le shader à
		//      partir de deux masques de bits passés dans la couleur du vertex
		//      (voir ComputeWaterShoreMasks) : aucune texture, aucun uniform
		//      par tuile -> compatible avec le batching de raylib.
		//
		//   3) SILLAGE : une trajectoire continue (24 points) dont le contour blanc s'élargit en V et s'estompe,
		//      émis derrière le joueur quand il nage (et en idle, doucement).
		//      émise derrière le joueur ; points passés en uniforms (réglés 1 fois / frame).
		//
		//  Le bruit est calculé en espace ÉCRAN (gl_FragCoord) reconverti en
		//  position MONDE via la caméra : continu entre toutes les tuiles,
		//  indépendant de l'ordre / du batching des draw calls.
		//  Tout est exprimé en UNITÉS DE TUILE puis quantifié sur une grille
		//  de "pixels d'art" (WaterArtPixelsPerTile) pour rester pixel-perfect.
		//
		//  Encodage du tint (couleur du vertex) de chaque tuile d'eau :
		//    R = profondeur (0 plage .. 1 profond)
		//    G = masque de bits des 8 voisins directs qui sont de la TERRE
		//    B = masque de bits des 4 voisins à distance 2 (N,E,S,W) = TERRE
		//    A = opacité (inchangée)
		//  fragTexCoord (0..1 sur la tuile, source rect 1x1) donne la position
		//  locale dans la tuile, utilisée pour la distance exacte au rivage.
		// ═══════════════════════════════════════════════════════════════════

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
uniform vec3  uColorDeep;
uniform vec3  uColorShallow;
uniform vec3  uColorMid;
uniform vec3  uColorCaustic;
uniform vec3  uColorFoam;
uniform float uCausticStrength;

uniform float uTileSize;     // taille d'une tuile en pixels monde
uniform float uArtPixels;    // pixels d'art par tuile (grille de quantification)
uniform float uFoamWidth;    // largeur max de la mousse, en tuiles

uniform vec2  uCamTarget;
uniform vec2  uCamOffset;
uniform float uCamZoom;
uniform float uScreenHeight;

// Sillage : points successifs de la trajectoire du joueur, du plus RECENT (0)
// au plus ancien (23). xy = position (pixels monde), z = age normalise 0..1 (<0 = inactif).
uniform vec3 uTrail[24];
// Sillages des entites : les points sont groupes par entite; w identifie le groupe.
uniform vec4 uEntityTrail[24];

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

vec2 hash22(vec2 p)
{
    return vec2(hash21(p), hash21(p + vec2(17.13, 91.7)));
}

// Bruit de valeur. mod(...,256) garde de la precision float loin de l'origine.
float vnoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash21(mod(i, 256.0));
    float b = hash21(mod(i + vec2(1.0, 0.0), 256.0));
    float c = hash21(mod(i + vec2(0.0, 1.0), 256.0));
    float d = hash21(mod(i + vec2(1.0, 1.0), 256.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

// Voronoi anime. Retourne (F1, F2, hash de la cellule la plus proche).
vec3 voronoi(vec2 p, float t)
{
    vec2 ip = floor(p);
    vec2 fp = fract(p);
    float f1 = 8.0;
    float f2 = 8.0;
    float id = 0.0;
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            vec2 g = vec2(float(i), float(j));
            vec2 cell = mod(ip + g, 256.0);
            vec2 h = hash22(cell);
            vec2 o = 0.5 + 0.45 * sin(t + 6.2831 * h);
            vec2 r = g + o - fp;
            float d = dot(r, r);
            if (d < f1) { f2 = f1; f1 = d; id = hash21(cell + 3.7); }
            else if (d < f2) { f2 = d; }
        }
    }
    return vec3(sqrt(f1), sqrt(f2), id);
}

// Distance (en tuiles) d'un point local p au carre unite [mn, mn+1]
float rectDist(vec2 p, vec2 mn)
{
    vec2 d = max(max(mn - p, p - (mn + 1.0)), 0.0);
    return length(d);
}

const vec2 NEAR_OFF[8] = vec2[8](
    vec2( 0.0, -1.0), vec2( 1.0, -1.0), vec2( 1.0,  0.0), vec2( 1.0,  1.0),
    vec2( 0.0,  1.0), vec2(-1.0,  1.0), vec2(-1.0,  0.0), vec2(-1.0, -1.0));

const vec2 FAR_OFF[4] = vec2[4](
    vec2( 0.0, -2.0), vec2( 2.0,  0.0), vec2( 0.0,  2.0), vec2(-2.0,  0.0));

float shoreDistance(vec2 p, int nearMask, int farMask)
{
    float d = 9.0;
    for (int i = 0; i < 8; i++)
        if (((nearMask >> i) & 1) == 1) d = min(d, rectDist(p, NEAR_OFF[i]));
    for (int i = 0; i < 4; i++)
        if (((farMask >> i) & 1) == 1) d = min(d, rectDist(p, FAR_OFF[i]));
    return d;
}

void main()
{
    // ── Position monde du pixel (continue sur toute la carte) ──────────
    vec2 screenPos = vec2(gl_FragCoord.x, uScreenHeight - gl_FragCoord.y);
    vec2 worldPos = (screenPos - uCamOffset) / uCamZoom + uCamTarget;

    // Quantification sur la grille de pixels d'art, en unites de tuile
    vec2 tileP = worldPos / uTileSize;
    vec2 pxP = (floor(tileP * uArtPixels) + 0.5) / uArtPixels;

    // ── Couleur de base : quasi uniforme, leger degrade de profondeur ──
    // Profondeur 0 (cote) -> 1 (fond) : turquoise -> bleu moyen -> bleu marine.
    // Leger grain par pixel pour adoucir les paliers entre tuiles (pixel-art).
    float depth = clamp(fragColor.r + (hash21(floor(pxP * uArtPixels) + 5.1) - 0.5) * 0.04, 0.0, 1.0);
    float dA = smoothstep(0.0, 0.55, depth);
    float dB = smoothstep(0.35, 1.0, depth);
    vec3 color = mix(mix(uColorShallow, uColorMid, dA), uColorDeep, dB);
    color *= 0.97 + 0.06 * vnoise(pxP * 0.35 + vec2(uTime * 0.03, -uTime * 0.02));

    // ── 1) Caustiques fines : arêtes de deux Voronoi, tres discretes ──
    vec3 v1 = voronoi(pxP * 1.35 + vec2(uTime * 0.05, uTime * 0.03), uTime * 0.55);
    vec3 v2 = voronoi(pxP * 2.10 + vec2(-uTime * 0.04, uTime * 0.06) + 7.3, uTime * 0.75 + 2.0);
    float e1 = 1.0 - smoothstep(0.0, 0.075, v1.y - v1.x);
    float e2 = 1.0 - smoothstep(0.0, 0.055, v2.y - v2.x);
    float caustic = clamp(e1 * 0.8 + e2 * 0.5, 0.0, 1.0);
    // pixel-art : on durcit (2 niveaux) plutot qu'un degrade doux
    caustic = step(0.45, caustic) * 0.65 + step(0.15, caustic) * 0.35;
    color = mix(color, uColorCaustic, caustic * 0.30 * uCausticStrength * (1.0 - 0.65 * depth));

    // Scintillements ponctuels
    vec2 spark = floor(pxP * uArtPixels);
    float sp = hash21(spark + floor(uTime * 2.5) * 13.7);
    color = mix(color, uColorFoam, step(0.9975, sp) * 0.55);

    // ── 2) Mousse de rivage ────────────────────────────────────────────
    int nearMask = int(fragColor.g * 255.0 + 0.5);
    int farMask  = int(fragColor.b * 255.0 + 0.5);

    if (nearMask != 0 || farMask != 0)
    {
        // Position locale dans la tuile (0..1), quantifiee comme le reste
        vec2 local = (floor(fragTexCoord * uArtPixels) + 0.5) / uArtPixels;
        float shore = shoreDistance(local, nearMask, farMask);

        // Maree : la mousse avance / recule lentement, et le front est irregulier
        float tide = 0.5 + 0.5 * sin(uTime * 1.1 + dot(pxP, vec2(0.45, 0.25)));
        float front = uFoamWidth * (0.78 + 0.22 * tide);
        float wob = (vnoise(pxP * 1.6 + vec2(0.0, uTime * 0.25)) - 0.5) * 0.35;
        float dn = shore / front + wob;

        vec3 fv = voronoi(pxP * 3.2 + vec2(uTime * 0.04, 0.0), uTime * 0.5 + 11.0);
        float edge = fv.y - fv.x;

        float lineW = mix(0.24, 0.09, smoothstep(0.0, 1.0, dn));
        float line = step(edge, lineW);
        float keep = step(fv.z, 1.05 - dn * 1.1);   // cellules lointaines retirees au hasard
        float solid = step(dn, 0.10);               // liseré blanc plein contre la cote

        float foam = max(solid, line * keep * step(dn, 1.0));

        // leger voile blanc dans les cellules tout pres du bord
        color = mix(color, uColorFoam, clamp(1.0 - dn, 0.0, 1.0) * 0.12);
        color = mix(color, uColorFoam, foam);
    }

    // ── 3) Sillage continu derriere le joueur ──────────────────────────
    // Chaque segment [point recent, point ancien] est un capsule dont le rayon
    // grandit avec l'age ; on dessine son CONTOUR (|distance - rayon| ~ 0) :
    // on obtient deux lignes qui s'ecartent en V et s'estompent, sans cercles isoles.
    float wake = 0.0;
    float hw = 0.55 / uArtPixels;
    for (int i = 0; i < 23; i++)
    {
        vec3 a = uTrail[i];
        vec3 b = uTrail[i + 1];
        if (a.z < 0.0 || b.z < 0.0) continue;
        vec2 A = a.xy / uTileSize;
        vec2 B = b.xy / uTileSize;
        vec2 ba = B - A;
        if (dot(ba, ba) > 4.0) continue;           // teleportation : on coupe le sillage
        vec2 pa = pxP - A;
        float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
        float d = length(pa - ba * h);
        float age = clamp(mix(a.z, b.z, h), 0.0, 1.0);
        float radius = mix(0.06, 0.60, sqrt(age));
        wake = max(wake, step(abs(d - radius), hw) * (1.0 - age));
    }
    color = mix(color, uColorFoam, wake * 0.9);

    float entityWake = 0.0;
    for (int i = 0; i < 24; i++)
    {
        vec4 point = uEntityTrail[i];
        if (point.z < 0.0) continue;

        float age = clamp(point.z, 0.0, 1.0);
        float radius = mix(0.06, 0.60, sqrt(age));
        float distanceToPoint = length(pxP - point.xy / uTileSize);
        float ring = step(abs(distanceToPoint - radius), hw) * (1.0 - age);
        entityWake = max(entityWake, ring);
    }

    for (int i = 0; i < 23; i++)
    {
        vec4 point = uEntityTrail[i];
        vec4 nextPoint = uEntityTrail[i + 1];
        if (point.z < 0.0 || nextPoint.z < 0.0 || abs(point.w - nextPoint.w) > 0.5) continue;

        vec2 A = point.xy / uTileSize;
        vec2 B = nextPoint.xy / uTileSize;
        vec2 ba = B - A;
        if (dot(ba, ba) > 4.0) continue;
        vec2 pa = pxP - A;
        float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
        float d = length(pa - ba * h);
        float segmentAge = clamp(mix(point.z, nextPoint.z, h), 0.0, 1.0);
        float segmentRadius = mix(0.06, 0.60, sqrt(segmentAge));
        entityWake = max(entityWake, step(abs(d - segmentRadius), hw) * (1.0 - segmentAge));
    }
    color = mix(color, uColorFoam, entityWake * 0.9);

    finalColor = vec4(color, fragColor.a);
}
";

		private static Shader _waterShader;
		private static bool _waterShaderLoaded = false;
		private static bool _waterShaderFailed = false;

		private static int _waterLocTime;
		private static int _waterLocColorDeep;
		private static int _waterLocColorShallow;
		private static int _waterLocColorMid;
		private static int _waterLocColorCaustic;
		private static int _waterLocColorFoam;
		private static int _waterLocCausticStrength;
		private static int _waterLocTileSize;
		private static int _waterLocArtPixels;
		private static int _waterLocFoamWidth;
		private static int _waterLocCamTarget;
		private static int _waterLocCamOffset;
		private static int _waterLocCamZoom;
		private static int _waterLocScreenHeight;
		private static readonly int[] _waterLocRipple = new int[WaterMaxRipples];
		private static readonly int[] _waterLocEntityRipple = new int[WaterMaxRipples];

		private static Texture2D _waterWhitePixel;
		private static bool _waterWhitePixelLoaded = false;

		// ───────────────────────── RÉGLAGES ─────────────────────────────
		/// <summary>Pixels d'art par tuile (16 = tuiles 16x16). Règle la finesse des motifs.</summary>
		private const float WaterArtPixelsPerTile = 16f;
		/// <summary>Largeur max de la bande de mousse, en tuiles (max utile : ~1.5, limité par les masques).</summary>
		private const float WaterFoamWidthTiles = 1.4f;
		/// <summary>Dessiner l'ancienne bordure bleue (water_border.png) sur la terre ? Faux = mousse seule.</summary>
		private const bool WaterLegacyBorderEnabled = false;

		/// <summary>Ajustement fin vertical (pixels monde) de l'origine des ondulations. Négatif = plus haut.</summary>
		private const float WaterRippleYOffsetPx = 0f;

		private const int WaterMaxRipples = 24; // points du sillage
		private const float WaterRippleLifetime = 1.3f;     // secondes
		private const float WaterRippleSpawnDistTiles = 0.15f; // distance entre deux ondulations en nageant
		private const float WaterRippleIdleInterval = 1.0f; // intervalle quand le joueur est immobile

		// Doit rester aligné sur OCEAN_THRESHOLD de WorldGeneration.cs (0.32f). Copié ici car la constante
		// d'origine n'est pas accessible depuis cette classe.
		private const float WaterShallowElevation = 0.32f;
		// Élévation à partir de laquelle l'eau est au plus sombre (océan profond ~0.15 dans WorldGeneration).
		private const float WaterDeepElevation = 0.10f;

		// ───────────────────────── ONDULATIONS ──────────────────────────
		private static readonly Vector2[] _rippleCenter = new Vector2[WaterMaxRipples];
		private static readonly float[] _rippleBirth = MakeInactiveRippleBirths();
		private readonly record struct EntityWaterRipple(Vector2 Position, float Birth);
		private static readonly Dictionary<Guid, List<EntityWaterRipple>> _entityWaterTrails = new();
		private static float[] MakeInactiveRippleBirths()
		{
			var a = new float[WaterMaxRipples];
			Array.Fill(a, -9999f);
			return a;
		}
		private static int _rippleNext = 0;
		private static Vector2 _lastRipplePos;
		private static float _lastRippleTime = -999f;
		private static bool _hasLastRipple = false;


		/// <summary>Ajoute une ondulation à la position monde (pixels). Utilisable aussi pour les animaux, projectiles, pluie...</summary>
		public static void AddWaterRipple(Vector2 worldPos, float currentTime)
		{
			_rippleCenter[_rippleNext] = worldPos;
			_rippleBirth[_rippleNext] = currentTime;
			_rippleNext = (_rippleNext + 1) % WaterMaxRipples;
		}

		// ───────────────── MASQUES DE RIVAGE (par tuile d'eau) ──────────
		// Ordre des bits : doit correspondre à NEAR_OFF / FAR_OFF du shader.
		private static readonly (int dx, int dy)[] ShoreNear =
		{
			(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)
		};
		private static readonly (int dx, int dy)[] ShoreFar =
		{
			(0, -2), (2, 0), (0, 2), (-2, 0)
		};

		private static bool IsShoreLand(int[,] groundIds, int x, int y, int w, int h)
		{
			if (x < 0 || y < 0 || x >= w || y >= h) return false;
			int id = groundIds[x, y];
			// 10 = eau ; 0 = tuile non éclairée / non chargée (ne doit pas créer de mousse)
			return id != 10 && id != 0;
		}

		private static void ComputeWaterShoreMasks(int[,] groundIds, int ix, int iy, int w, int h, out byte nearMask, out byte farMask)
		{
			int n = 0, f = 0;
			for (int i = 0; i < ShoreNear.Length; i++)
				if (IsShoreLand(groundIds, ix + ShoreNear[i].dx, iy + ShoreNear[i].dy, w, h)) n |= 1 << i;
			for (int i = 0; i < ShoreFar.Length; i++)
				if (IsShoreLand(groundIds, ix + ShoreFar[i].dx, iy + ShoreFar[i].dy, w, h)) f |= 1 << i;
			nearMask = (byte)n;
			farMask = (byte)f;
		}

		// ─────────────────── GESTION DU MODE SHADER ─────────────────────
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

			_waterLocTime = Raylib.GetShaderLocation(_waterShader, "uTime");
			_waterLocColorDeep = Raylib.GetShaderLocation(_waterShader, "uColorDeep");
			_waterLocColorShallow = Raylib.GetShaderLocation(_waterShader, "uColorShallow");
			_waterLocColorMid = Raylib.GetShaderLocation(_waterShader, "uColorMid");
			_waterLocColorCaustic = Raylib.GetShaderLocation(_waterShader, "uColorCaustic");
			_waterLocColorFoam = Raylib.GetShaderLocation(_waterShader, "uColorFoam");
			_waterLocCausticStrength = Raylib.GetShaderLocation(_waterShader, "uCausticStrength");
			_waterLocTileSize = Raylib.GetShaderLocation(_waterShader, "uTileSize");
			_waterLocArtPixels = Raylib.GetShaderLocation(_waterShader, "uArtPixels");
			_waterLocFoamWidth = Raylib.GetShaderLocation(_waterShader, "uFoamWidth");
			_waterLocCamTarget = Raylib.GetShaderLocation(_waterShader, "uCamTarget");
			_waterLocCamOffset = Raylib.GetShaderLocation(_waterShader, "uCamOffset");
			_waterLocCamZoom = Raylib.GetShaderLocation(_waterShader, "uCamZoom");
			_waterLocScreenHeight = Raylib.GetShaderLocation(_waterShader, "uScreenHeight");
			for (int i = 0; i < WaterMaxRipples; i++)
			{
				_waterLocRipple[i] = Raylib.GetShaderLocation(_waterShader, $"uTrail[{i}]");
				_waterLocEntityRipple[i] = Raylib.GetShaderLocation(_waterShader, $"uEntityTrail[{i}]");
			}

			if (_waterShader.Id == 0 || _waterLocTime == -1 || _waterLocColorDeep == -1)
			{
				Console.WriteLine(" [Water] Échec de compilation/lien du shader d'eau : repli sur le rendu uni.");
				_waterShaderFailed = true;
				return;
			}

			// Palette tirée de la référence : turquoise vif, très peu de dégradé.
			SetShaderColor(_waterLocColorShallow, new Vector3(0.00f, 0.80f, 0.70f)); // côte
			SetShaderColor(_waterLocColorMid, new Vector3(0.00f, 0.52f, 0.62f));     // profondeur moyenne
			SetShaderColor(_waterLocColorDeep, new Vector3(0.02f, 0.18f, 0.42f));    // fond
			SetShaderColor(_waterLocColorCaustic, new Vector3(0.55f, 0.97f, 0.90f));
			SetShaderColor(_waterLocColorFoam, new Vector3(1.00f, 1.00f, 1.00f));
			Raylib.SetShaderValue(_waterShader, _waterLocCausticStrength, 1.0f, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_waterShader, _waterLocArtPixels, WaterArtPixelsPerTile, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_waterShader, _waterLocFoamWidth, WaterFoamWidthTiles, ShaderUniformDataType.Float);
		}

		private static void SetShaderColor(int loc, Vector3 rgb01)
		{
			if (loc == -1) return;
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

		// Appelé UNE SEULE FOIS par frame, avant la boucle de tuiles.
		private static void UpdateWaterShaderTime(float currentTime, Vector2 playerPos, bool isPlayerInWater, List<Entity> entities)
		{
			EnsureWaterShaderLoaded();
			if (_waterShaderFailed) return;

			int ts = Program.TileSize;
			UpdateEntityWaterTrails(entities, currentTime, ts);

			// ── Émission des ondulations derrière le joueur ──
			if (isPlayerInWater)
			{
				// Le sprite du joueur ET les tuiles d'eau sont dessinés remontés de (hauteur * ts / 4)
				// (voir DrawWorld : playerYOffset / drawY). Sans cette correction, l'ondulation était
				// émise dans la grille "à plat" donc trop bas à l'écran.
				int liftPx = Program.GetPlayerGroundHeight(playerPos) * ts / 4;
				Vector2 feet = new Vector2(playerPos.X, playerPos.Y + Program.FeetOffsetY - liftPx + WaterRippleYOffsetPx);
				bool spawn = !_hasLastRipple;
				if (!spawn)
				{
					float moved = Vector2.Distance(feet, _lastRipplePos);
					if (moved >= ts * WaterRippleSpawnDistTiles) spawn = true;                       // il nage
					else if (currentTime - _lastRippleTime >= WaterRippleIdleInterval) spawn = true; // il flotte
					if (moved > ts * 40f) spawn = true; // téléportation
				}
				if (spawn)
				{
					AddWaterRipple(feet, currentTime);
					_lastRipplePos = feet;
					_lastRippleTime = currentTime;
					_hasLastRipple = true;
				}
			}
			else
			{
				_hasLastRipple = false;
			}

			Raylib.SetShaderValue(_waterShader, _waterLocTime, currentTime, ShaderUniformDataType.Float);
			if (_waterLocTileSize != -1)
				Raylib.SetShaderValue(_waterShader, _waterLocTileSize, (float)ts, ShaderUniformDataType.Float);

			// Du plus récent (index 0) au plus ancien : le shader relie les points consécutifs.
			for (int k = 0; k < WaterMaxRipples; k++)
			{
				if (_waterLocRipple[k] == -1) continue;
				int idx = ((_rippleNext - 1 - k) % WaterMaxRipples + WaterMaxRipples) % WaterMaxRipples;
				float age = (currentTime - _rippleBirth[idx]) / WaterRippleLifetime;
				Vector3 r = (age >= 0f && age < 1f)
					? new Vector3(_rippleCenter[idx].X, _rippleCenter[idx].Y, age)
					: new Vector3(0f, 0f, -1f);
				Raylib.SetShaderValue(_waterShader, _waterLocRipple[k], r, ShaderUniformDataType.Vec3);
			}

			var activeEntityTrails = _entityWaterTrails
				.Where(entry => entry.Value.Count > 0)
				.OrderByDescending(entry => entry.Value[^1].Birth)
				.Take(WaterMaxRipples)
				.ToList();
			int entityTrailSlots = Math.Max(1, WaterMaxRipples / Math.Max(1, activeEntityTrails.Count));
			int entityTrailIndex = 0;
			for (int group = 0; group < activeEntityTrails.Count && entityTrailIndex < WaterMaxRipples; group++)
			{
				List<EntityWaterRipple> trail = activeEntityTrails[group].Value;
				int sampleCount = Math.Min(entityTrailSlots, trail.Count);
				for (int sample = 0; sample < sampleCount && entityTrailIndex < WaterMaxRipples; sample++)
				{
					EntityWaterRipple ripple = trail[trail.Count - 1 - sample];
					float age = (currentTime - ripple.Birth) / WaterRippleLifetime;
					Vector4 value = age >= 0f && age < 1f
						? new Vector4(ripple.Position.X, ripple.Position.Y, age, group)
						: new Vector4(0f, 0f, -1f, group);
					if (_waterLocEntityRipple[entityTrailIndex] != -1)
						Raylib.SetShaderValue(_waterShader, _waterLocEntityRipple[entityTrailIndex], value, ShaderUniformDataType.Vec4);
					entityTrailIndex++;
				}
			}
			for (; entityTrailIndex < WaterMaxRipples; entityTrailIndex++)
			{
				if (_waterLocEntityRipple[entityTrailIndex] != -1)
					Raylib.SetShaderValue(_waterShader, _waterLocEntityRipple[entityTrailIndex], new Vector4(0f, 0f, -1f, -1f), ShaderUniformDataType.Vec4);
			}

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

		private static void UpdateEntityWaterTrails(List<Entity> entities, float currentTime, int tileSize)
		{
			var activeEntityIds = new HashSet<Guid>();
			foreach (Entity entity in entities)
			{
				if (!entity.IsAlive || !entity.IsInWater)
					continue;

				activeEntityIds.Add(entity.NetId);
				if (!_entityWaterTrails.TryGetValue(entity.NetId, out List<EntityWaterRipple>? trail))
				{
					trail = new List<EntityWaterRipple>();
					_entityWaterTrails.Add(entity.NetId, trail);
				}
				trail.RemoveAll(ripple => currentTime - ripple.Birth >= WaterRippleLifetime);

				Vector2 worldPos = entity.WorldPos;
				int tileX = (int)MathF.Floor(worldPos.X / tileSize);
				int tileY = (int)MathF.Floor(worldPos.Y / tileSize);
				float lift = GetHeightAt(tileX, tileY) * tileSize / 4f;
				Vector2 feet = new(worldPos.X, worldPos.Y + Program.FeetOffsetY - lift + WaterRippleYOffsetPx);
				bool spawn = trail.Count == 0;
				if (!spawn)
				{
					EntityWaterRipple latest = trail[^1];
					float moved = Vector2.Distance(feet, latest.Position);
					spawn = moved >= tileSize * WaterRippleSpawnDistTiles
						|| currentTime - latest.Birth >= WaterRippleIdleInterval
						|| moved > tileSize * 40f;
				}
				if (spawn)
					trail.Add(new EntityWaterRipple(feet, currentTime));
			}

			foreach (Guid entityId in _entityWaterTrails.Keys.Where(id => !activeEntityIds.Contains(id)).ToArray())
				_entityWaterTrails.Remove(entityId);
		}

		private static void DrawWaterTileShader(int worldX, int worldY, int drawY, int ts, byte alpha, byte shoreNear, byte shoreFar)
		{
			EnsureWaterShaderLoaded();

			if (_waterShaderFailed)
			{
				Raylib.DrawRectangle(worldX * ts, drawY, ts, ts, new Color((byte)0, (byte)200, (byte)175, alpha));
				return;
			}

			EnsureWaterWhitePixelLoaded();

			float elevation = GetClimateAt(worldX, worldY).elevation;
			float depthFactor = (WaterShallowElevation - elevation) / (WaterShallowElevation - WaterDeepElevation);
			depthFactor = Math.Clamp(depthFactor, 0f, 1f);
			byte depthByte = (byte)(depthFactor * 255f);

			// R = profondeur, G/B = masques de rivage, A = opacité (voir en-tête).
			Raylib.DrawTexturePro(_waterWhitePixel,
				new Rectangle(0, 0, 1, 1),
				new Rectangle(worldX * ts, drawY, ts, ts),
				Vector2.Zero, 0, new Color(depthByte, shoreNear, shoreFar, alpha));
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
