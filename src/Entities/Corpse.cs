// Corpse.cs - Corps mort en ragdoll.
//
//  Un corps mort n'est pas une "entité de plus" : c'est un OBJET PHYSIQUE.
//  - Sa pose de départ est la pose EXACTE du squelette au moment de la mort (la même
//    forward-kinematics que celle du rendu des entités) : le corps apparaît donc à la
//    place exacte de la créature, dans la pose exacte qu'elle affichait.
//  - Chaque pièce du squelette est un CORPS RIGIDE (centre de masse + angle + inertie).
//    Les pièces sont reliées par de vraies ARTICULATIONS (un point commun au parent et
//    à l'enfant, avec une plage d'angle), posées au niveau visuel de l'épaule, du coude,
//    de la hanche, du genou, du cou... c'est-à-dire sur l'ancrage de chaque pièce (centre de
//    sa texture), le même point que celui utilisé par le rendu des entités vivantes.
//  - Le corps bascule sous l'effet de la gravité (avec une petite aide pour qu'il finisse
//    bien couché), les membres sont d'abord raides puis se relâchent et s'affalent.
//    Chaque mort est différente : sens, vitesse de chute, élan des membres, raideur.
//  - Il peut glisser et se faire pousser (joueur, véhicules, explosions).
//  - Il disparaît au bout de DefaultLifetime (20 s).
//
//  RENDU : rien n'est redessiné "à la main". Le corps passe par EntityRenderer.DrawEntity,
//  exactement comme une entité vivante (mêmes textures, mêmes équipements, mêmes
//  teintures, mêmes cheveux/yeux), à une seule différence près : la position et la
//  rotation de chaque pièce du squelette sont fournies par la physique au lieu d'être
//  calculées par l'animation.
//
//  CONVENTION : le moteur de rendu (EntityRenderer.DrawEntity) place chaque texture par son
//  CENTRE et la fait tourner autour de ce même centre (origin = taille / 2). La position
//  d'une pièce du squelette est donc le centre de sa texture. La physique travaille sur le
//  centre de masse de la zone visible, et ne convertit en "centre de texture" qu'au moment
//  de transmettre la pose au rendu (SyncOutput). Les articulations sont posées sur ce même
//  centre de texture (voir JointAtTextureCenter), pas sur un bord ni dans un coin.
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Soulfract
{
    public class Corpse
    {
        // ==================== CONSTANTES ====================
        /// <summary>Durée de vie d'un corps au sol (en secondes).</summary>
        public const float DefaultLifetime = 4f;

        private const float Gravity = 1500f;
        private const float FadeDuration = 2.5f;      // fondu avant disparition
        private const float Rad2Deg = 180f / MathF.PI;
        private const float Deg2Rad = MathF.PI / 180f;

        // --- Simulation (pas fixe : le comportement ne dépend plus du framerate) ---
        private const float FixedStep = 1f / 120f;
        private const int SubSteps = 4;               // sous-pas par pas fixe (h = 1/480 s)
        private const int SolverIterations = 2;
        private const int MaxStepsPerFrame = 10;

        private const float LinearDamping = 0.35f;   // amortissement de l'air (par seconde)
        private const float AngularDamping = 0.7f;
        private const float JointDamping = 3f;       // frottement interne des articulations
        private const float GroundFrictionCoef = 0.9f;
        private const float MaxContactPush = 6f;     // correction max d'une pénétration (px / itération)
        private const float MinHalfExtent = 1f;

        // --- Raideur musculaire : le corps est raide à la mort puis se relâche ---
        private const float ToneStrength = 0.08f;

        // --- Aide à la bascule (le corps doit finir couché sur le côté) ---
        private const float AssistDuration = 1.0f;   // après le premier contact avec le sol
        private const float AssistAccel = 14f;       // rad/s²
        private const float MinFallSpeed = 2.5f;     // rad/s : en dessous, on aide

        // --- Mise au repos ---
        private const float MinAwakeTime = 0.8f;
        private const float SleepLinear = 14f;       // px/s
        private const float SleepAngular = 0.4f;     // rad/s
        private const float SleepDelay = 0.4f;

        private const float RootMass = 4.5f;

        // --- Articulations ---
        //  true  : l'articulation est posée sur l'ANCRAGE du squelette de la pièce, c'est-à-dire
        //          le CENTRE de sa texture : exactement le point autour duquel le rendu vivant
        //          fait tourner la pièce, et l'endroit où BasePos la rattache à son parent.
        //  false : ancien comportement, l'articulation est déplacée sur le bord visible de la
        //          pièce (haut du bras, bas de la tête...), donc décentrée / dans un coin.
        private const bool JointAtTextureCenter = true;

        // --- Déplacement dans le PLAN DU SOL (jeu top-down) ---
        //  Le sol est le plan écran (X, Y). L'axe Y de l'écran est la PROFONDEUR (nord/sud) :
        //  une poussée vers le haut de l'écran fait glisser le corps vers le nord, elle ne le
        //  fait pas décoller. La hauteur réelle (saut, chute) reste gérée par la gravité.
        //  Distance de glissement = v² / (2 * PlanarFriction) :
        //  300 px/s -> 25 px, 450 px/s -> 56 px (avec 1800).
        private const float PlanarFriction = 1800f;   // px/s² : décélération au sol
        private const float PlanarAirDrag = 60f;      // px/s² : si le corps n'est pas au sol
        private const float MaxPushSpeed = 450f;      // px/s : vitesse de glissement maximale
        private const float MaxLift = 220f;           // px/s : petit sursaut vertical maximal
        private const float LimbKick = 2.5f;          // rad/s : les membres ballottent quand on pousse

        // --- MOMENTUM conservé à la mort (vitesse de course, knockback...) ---
        //  Le corps garde la vitesse qu'avait la créature vivante : il continue sur son élan
        //  au lieu de s'arrêter net. Plafond plus haut que MaxPushSpeed (qui ne limite que les
        //  poussées ultérieures), et frottement réduit juste après la mort : le corps
        //  roule / glisse d'abord, puis le frottement du sol revient progressivement.
        //  Exemple (frottement x0.3 -> 540 px/s²) : 300 px/s -> ~80 px de glissade.
        private const float MaxMomentumSpeed = 900f;  // px/s : vitesse maximale transmise à la mort
        private const float CarryFrictionScale = 0.3f; // part du frottement du sol juste après la mort
        private const float CarryDuration = 0.8f;      // s : durée du retour au frottement normal
        private const float MinLaunchSpeed = 12f;      // px/s : en dessous, pas d'élan (corps immobile)

        // ==================== IDENTITÉ / RENDU ====================
        public string Species = "human";
        public Color Tint = Color.White;
        public float CustomScale = 1f;
        public Equipment? Equipment;
        public int HairStyle;
        public Color HairColor = Color.Black;
        public int BeardStyle;
        public Dictionary<string, int>? RandomFeatureVariant;
        public Dictionary<string, Color>? RandomFeatureColor;
        public bool IsPlayer;
        public Texture2D UnderwearTexture;
        public Texture2D EyesTexture;
        public Texture2D MouthTexture;

        // ==================== VIE ====================
        public float Age;
        public float Lifetime = DefaultLifetime;

        // ==================== GÉOMÉTRIE ====================
        /// <summary>Niveau du sol sous le corps (Y monde) : aligné sur la ligne d'ombre
        /// des entités, pour que le corps se pose exactement là où l'ombre était.</summary>
        public float GroundY;
        /// <summary>Centre de masse du tronc (os racine) : sert au tri de rendu, aux
        /// collisions, aux explosions et à la caméra.</summary>
        public Vector2 Position;
        /// <summary>Ancrage d'origine du squelette (position d'affichage de la créature).</summary>
        public Vector2 AnchorPosition;
        public float SpeciesScale = 1f;
        /// <summary>Sens de la mise en page d'origine (le corps garde la même "face").</summary>
        public float Facing = 1f;

        // ==================== PHYSIQUE ====================
        private List<AnimalBodyPart> _skeleton = new();

        // Pose transmise au rendu (coin haut-gauche de chaque texture + rotation en degrés).
        private Vector2[] _pos = Array.Empty<Vector2>();
        private float[] _rot = Array.Empty<float>();

        // État des corps rigides (centre de masse de la zone visible, angle en radians).
        private Vector2[] _com = Array.Empty<Vector2>();
        private Vector2[] _comPrev = Array.Empty<Vector2>();
        private float[] _ang = Array.Empty<float>();
        private float[] _angPrev = Array.Empty<float>();
        private Vector2[] _vel = Array.Empty<Vector2>();
        private float[] _angVel = Array.Empty<float>();
        private float[] _invMass = Array.Empty<float>();
        private float[] _invInertia = Array.Empty<float>();

        // Géométrie locale (repère de la pièce, centré sur son centre de masse).
        private Vector2[] _half = Array.Empty<Vector2>();            // demi-taille de la zone visible
        private Vector2[] _texCenterLocal = Array.Empty<Vector2>();  // centre de la texture (pivot du rendu)
        private Vector2[] _texHalf = Array.Empty<Vector2>();         // demi-taille de la texture

        // Articulations (une par pièce non racine).
        private int[] _parent = Array.Empty<int>();
        private int[] _depth = Array.Empty<int>();
        private Vector2[] _jointA = Array.Empty<Vector2>();   // point d'attache dans le repère de l'ENFANT
        private Vector2[] _jointB = Array.Empty<Vector2>();   // même point dans le repère du PARENT
        private float[] _rel0 = Array.Empty<float>();         // angle relatif de repos (rad)
        private float[] _limMin = Array.Empty<float>();       // écart minimal autour du repos (rad, <= 0)
        private float[] _limMax = Array.Empty<float>();       // écart maximal autour du repos (rad, >= 0)
        private bool[] _welded = Array.Empty<bool>();        // pièce solidaire de son parent

        // Sauvegarde de la pose de départ (filet de sécurité si la simulation diverge).
        private Vector2[] _com0 = Array.Empty<Vector2>();
        private float[] _ang0 = Array.Empty<float>();

        private int _root = -1;

        // Glissement dans le plan du sol (translation rigide de tout le corps).
        private Vector2 _planarVel;       // px/s
        private Vector2 _planarOffset;    // déplacement cumulé depuis la création (filet de sécurité)
        private bool _carryMomentum;      // le corps est mort en mouvement : frottement réduit au début

        // Chute / bascule
        private float _fallSign = 1f;
        private float _toppleTarget;      // rad, signé : rotation que le tronc doit atteindre
        private float _rootAng0;
        private float _toneDuration = 0.8f;
        private bool _hasLanded;
        private float _assistTime;

        // Pas de temps / repos
        private float _accumulator;
        private float _simTime;
        private bool _touching;
        private bool _asleep;
        private float _restTimer;

        // ==================== ACCÈS ====================

        /// <summary>Pose courante (coin haut-gauche des textures, monde) : transmise au moteur de rendu.</summary>
        public Vector2[] PartPositions => _pos;
        /// <summary>Rotations courantes (degrés) : transmises au moteur de rendu.</summary>
        public float[] PartRotations => _rot;
        /// <summary>Y de tri pour le rendu (le corps est trié au niveau de ses pieds).</summary>
        public float SortY => GroundY;
        /// <summary>Facteur de fondu (1 = opaque).</summary>
        public float AlphaFactor => Age >= Lifetime - FadeDuration
            ? Math.Clamp((Lifetime - Age) / FadeDuration, 0f, 1f)
            : 1f;

        // ==================== CRÉATION ====================

        /// <summary>
        /// Crée un corps à partir de la pose courante d'un squelette.
        /// <paramref name="impulse"/> (px/s, optionnel) : élan reçu au moment de la mort
        /// (direction du coup, vitesse de la créature). Il fait GLISSER le corps dans le
        /// plan du sol (X et Y écran), sans le lancer en l'air. S'il est non nul, il décide
        /// aussi du sens de la chute.
        /// <paramref name="momentum"/> (px/s, optionnel) : vitesse de la créature AVANT sa mort
        /// (course, déplacement, knockback en cours). Elle s'ajoute à <paramref name="impulse"/> :
        /// le corps garde son élan au lieu de s'arrêter net.
        /// </summary>
        public static Corpse? Create(
            string species,
            Color tint,
            Vector2 drawPos,        // position d'ancrage du squelette (centre du corps)
            float groundY,          // niveau du sol
            float facing,
            string anim,
            int frame,
            float prog,
            float customScale,
            Equipment? equipment,
            int hairStyle,
            Color hairColor,
            int beardStyle,
            Dictionary<string, int>? randomFeatureVariant,
            Dictionary<string, Color>? randomFeatureColor,
            bool isPlayer,
            Texture2D underwearTexture,
            Texture2D eyesTexture,
            Texture2D mouthTexture,
            Vector2 impulse = default,
            Vector2 momentum = default)
        {
            string speciesKey = ResolveSkeletonKey(species);
            if (!SpeciesData.Skeletons.TryGetValue(speciesKey, out var skeleton) || skeleton.Count == 0)
                return null;

            var corpse = new Corpse
            {
                Species = species,
                Tint = tint,
                CustomScale = customScale,
                Equipment = equipment,
                HairStyle = hairStyle,
                HairColor = hairColor,
                BeardStyle = beardStyle,
                RandomFeatureVariant = randomFeatureVariant,
                RandomFeatureColor = randomFeatureColor,
                IsPlayer = isPlayer,
                UnderwearTexture = underwearTexture,
                EyesTexture = eyesTexture,
                MouthTexture = mouthTexture,
                GroundY = groundY,
                Facing = facing,
                AnchorPosition = drawPos,
                _skeleton = skeleton
            };

            // ---------- Échelle de l'espèce (identique à celle du rendu) ----------
            float speciesScale = EntityRenderer.BASE_ENTITY_SCALE;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale *= speciesInfo.Scale;
            speciesScale *= customScale;
            corpse.SpeciesScale = speciesScale;

            corpse.Build(anim, frame, prog, facing, drawPos, impulse + momentum);
            return corpse;
        }

        /// <summary>Construit les corps rigides, les articulations et l'état initial.</summary>
        private void Build(string anim, int frame, float prog, float facing, Vector2 drawPos, Vector2 impulse)
        {
            var skeleton = _skeleton;
            int count = skeleton.Count;
            float scale = SpeciesScale;

            _pos = new Vector2[count];
            _rot = new float[count];
            _com = new Vector2[count];
            _comPrev = new Vector2[count];
            _ang = new float[count];
            _angPrev = new float[count];
            _vel = new Vector2[count];
            _angVel = new float[count];
            _invMass = new float[count];
            _invInertia = new float[count];
            _half = new Vector2[count];
            _texCenterLocal = new Vector2[count];
            _texHalf = new Vector2[count];
            _parent = new int[count];
            _depth = new int[count];
            _jointA = new Vector2[count];
            _jointB = new Vector2[count];
            _rel0 = new float[count];
            _limMin = new float[count];
            _limMax = new float[count];
            _welded = new bool[count];

            var indexMap = new Dictionary<string, int>(count);
            for (int i = 0; i < count; i++) indexMap[skeleton[i].Name] = i;

            // ---------- 1) POSE DE DÉPART : la pose rendue de l'instant de la mort ----------
            var anchors = new Vector2[count];
            var rotDeg = new float[count];
            ComputePose(skeleton, indexMap, anim, frame, prog, facing, drawPos, scale, anchors, rotDeg);

            // ---------- 2) ARBORESCENCE ----------
            // La racine est la pièce sans parent ("body" pour l'humain). Toutes les pièces
            // sans parent d'animation sont rattachées au tronc (épaules, hanches, cou).
            int root = -1;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(skeleton[i].ParentName)) { root = i; break; }
            }
            if (root < 0) root = 0;
            _root = root;

            for (int i = 0; i < count; i++)
            {
                int parent = -1;
                string? pn = skeleton[i].ParentName;
                if (!string.IsNullOrEmpty(pn) && indexMap.TryGetValue(pn, out int pi) && pi != i)
                    parent = pi;
                else if (i != root)
                    parent = root;
                _parent[i] = parent;
            }
            _parent[root] = -1;

            // Une boucle de parentage (donnée corrompue) est coupée : la pièce passe sous le tronc.
            for (int i = 0; i < count; i++)
            {
                int cursor = _parent[i];
                for (int guard = 0; cursor >= 0 && guard <= count; guard++)
                {
                    if (cursor == i) { _parent[i] = root; break; }
                    cursor = _parent[cursor];
                }
            }

            for (int i = 0; i < count; i++)
            {
                int depth = 0;
                int cursor = _parent[i];
                while (cursor >= 0 && depth < count)
                {
                    depth++;
                    cursor = _parent[cursor];
                }
                _depth[i] = depth;
            }

            // ---------- 3) CORPS RIGIDES ----------
            //  Le rectangle du contenu non transparent est exprimé dans le repère de
            //  DESTINATION de la texture (après l'éventuel miroir des sprites de gauche).
            //  Le centre de masse est le centre de ce rectangle visible ; le pivot du rendu
            //  (centre de la texture) en est décalé d'un vecteur fixe dans le repère local.
            for (int i = 0; i < count; i++)
            {
                var bounds = GetVisualBounds(skeleton[i].Texture);
                float texW = bounds.TexW, texH = bounds.TexH;

                float minX, maxX;
                if (facing < 0f)
                {
                    minX = texW - bounds.MaxX;
                    maxX = texW - bounds.MinX;
                }
                else
                {
                    minX = bounds.MinX;
                    maxX = bounds.MaxX;
                }
                float minY = bounds.MinY, maxY = bounds.MaxY;

                _half[i] = new Vector2(
                    MathF.Max((maxX - minX) * 0.5f * scale, MinHalfExtent),
                    MathF.Max((maxY - minY) * 0.5f * scale, MinHalfExtent));

                Vector2 comFromTexCenter = new(
                    ((minX + maxX) * 0.5f - texW * 0.5f) * scale,
                    ((minY + maxY) * 0.5f - texH * 0.5f) * scale);
                _texCenterLocal[i] = -comFromTexCenter;
                _texHalf[i] = new Vector2(texW * 0.5f * scale, texH * 0.5f * scale);

                _ang[i] = rotDeg[i] * Deg2Rad;
                Vector2 texCenter = anchors[i];
                _com[i] = texCenter + RotRad(comFromTexCenter, _ang[i]);

                var profile = GetJointProfile(skeleton[i].Name);
                float mass = i == root ? RootMass : profile.Mass;
                float ix = MathF.Max(_half[i].X, 3f);
                float iy = MathF.Max(_half[i].Y, 3f);
                float inertia = mass * (ix * ix + iy * iy) / 3f;
                _invMass[i] = 1f / mass;
                _invInertia[i] = 1f / inertia;
            }

            // ---------- 4) ARTICULATIONS ----------
            //  Le point d'attache est placé sur l'ancrage de la pièce (centre de sa texture,
            //  cf. JointAtTextureCenter). Il est ensuite exprimé dans le repère des DEUX
            //  pièces : à l'instant 0, l'erreur de l'articulation est exactement nulle
            //  (aucun saut visuel).
            for (int i = 0; i < count; i++)
            {
                int p = _parent[i];
                if (p < 0) continue;

                bool welded = (anchors[i] - anchors[p]).Length() < 1.5f;
                Vector2 jointWorld;
                if (JointAtTextureCenter || welded)
                {
                    //  Centre de la texture (= ancrage du squelette). Même formule que SyncOutput :
                    //  l'articulation coïncide avec le pivot du rendu, rien n'est décalé vers un coin.
                    jointWorld = _com[i] + RotRad(_texCenterLocal[i], _ang[i]);
                }
                else
                {
                    jointWorld = _com[i] + RotRad(ChooseAttachLocal(i, p, skeleton[i].Name), _ang[i]);
                }

                _jointA[i] = RotRad(jointWorld - _com[i], -_ang[i]);
                _jointB[i] = RotRad(jointWorld - _com[p], -_ang[p]);
                _rel0[i] = _ang[i] - _ang[p];
                _welded[i] = welded;

                if (welded)
                {
                    _limMin[i] = 0f;
                    _limMax[i] = 0f;
                }
                else
                {
                    //  Plage d'angle légèrement différente à chaque corps : les poses
                    //  finales ne sont plus identiques d'une mort à l'autre.
                    float limit = GetJointProfile(skeleton[i].Name).LimitDeg * Rand(0.8f, 1.15f) * Deg2Rad;
                    _limMin[i] = -limit;
                    _limMax[i] = limit;
                }
            }

            // ---------- 5) SOL ----------
            //  L'axe Y descend (comme à l'écran) : le corps est "en l'air" quand son
            //  point le plus bas est AU-DESSUS de la ligne de sol.
            float lowest = LowestCornerY();
            bool grounded = lowest >= GroundY - 1f;
            if (lowest > GroundY)
            {
                //  Corps légèrement enfoncé (pieds dessinés sous la ligne d'ombre) : on le
                //  remonte tout de suite, sans lui donner de vitesse.
                float lift = lowest - GroundY;
                for (int i = 0; i < count; i++) _com[i] = new Vector2(_com[i].X, _com[i].Y - lift);
            }

            _com0 = (Vector2[])_com.Clone();
            _ang0 = (float[])_ang.Clone();
            for (int i = 0; i < count; i++)
            {
                _comPrev[i] = _com[i];
                _angPrev[i] = _ang[i];
            }

            // ---------- 6) CHUTE ----------
            //  Sens : celui du coup s'il y en a un, sinon celui où la créature regarde
            //  (avec une chance sur cinq de tomber en arrière).
            //  'impulse' contient ici le total : coup reçu + vitesse que la créature avait en vie.
            bool hasImpulse = impulse.LengthSquared() > MinLaunchSpeed * MinLaunchSpeed;
            float speedFrac = Math.Clamp(impulse.Length() / MaxPushSpeed, 0f, 2f);
            if (hasImpulse && MathF.Abs(impulse.X) > 1f)
                _fallSign = impulse.X > 0f ? 1f : -1f;
            else
            {
                _fallSign = facing >= 0f ? 1f : -1f;
                if (Random.Shared.NextSingle() < 0.2f) _fallSign = -_fallSign;
            }
            _toppleTarget = _fallSign * (90f + Rand(-16f, 16f)) * Deg2Rad;
            _rootAng0 = _ang[root];
            _toneDuration = Rand(0.55f, 0.95f);

            // Élan initial : le corps tourne d'un bloc autour de son point d'appui (s'il est
            // posé) ou de son tronc (s'il tombe de haut). La gravité fait le reste.
            Vector2 pivot;
            float w0;
            if (grounded)
            {
                _hasLanded = true;
                pivot = FindToppleBase();
                w0 = _fallSign * Rand(1.6f, 2.8f) * (1f + 0.5f * speedFrac);   // plus vite = bascule plus vive
            }
            else
            {
                pivot = _com[root];
                w0 = _fallSign * Rand(0.4f, 1.4f) * (1f + 0.5f * speedFrac);
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 r = _com[i] - pivot;
                _vel[i] = new Vector2(-r.Y, r.X) * w0;
                _angVel[i] = w0;

                if (i != root) _angVel[i] += Rand(-2.2f, 2.2f) * (1f + speedFrac);   // les membres partent chacun de leur côté (et fouettent plus si le corps allait vite)
            }

            // L'élan de la mort glisse sur le sol (top-down) : jamais de vitesse verticale.
            _planarVel = hasImpulse ? ClampLength(impulse, MaxMomentumSpeed) : Vector2.Zero;
            _carryMomentum = hasImpulse;

            SyncOutput();
        }

        /// <summary>Renvoie la clé de squelette utilisable pour une espèce donnée.</summary>
        public static string ResolveSkeletonKey(string? species)
        {
            string key = species?.Trim().ToLowerInvariant() ?? "";
            if (SpeciesData.Skeletons.ContainsKey(key)) return key;
            // Le Génie est rendu avec le squelette humain.
            if (key == "genie") return "human";
            return key;
        }

        // ==================== POSE DE DÉPART (FK) ====================
        //  Exactement la même formule que celle du moteur de rendu des entités : la pose
        //  de départ du corps est donc pixel pour pixel celle qui était affichée à
        //  l'instant de la mort (pas de "pop" visuel).

        private static void ComputePose(
            List<AnimalBodyPart> skeleton,
            Dictionary<string, int> indexMap,
            string anim, int frame, float prog,
            float facing, Vector2 drawPos, float speciesScale,
            Vector2[] positions, float[] rotations)
        {
            int count = skeleton.Count;
            var computed = new bool[count];
            var visiting = new bool[count];

            void Compute(int index)
            {
                // "visiting" coupe les boucles de parentage (donnée corrompue).
                if (computed[index] || visiting[index]) return;
                visiting[index] = true;
                var part = skeleton[index];

                Vector2 animOffset = Vector2.Zero;
                float animRot = 0f;
                if (!string.IsNullOrEmpty(anim) && part.Animations != null
                    && part.Animations.TryGetValue(anim, out var frames) && frames.Count > 0)
                {
                    int safeFrame = ((frame % frames.Count) + frames.Count) % frames.Count;
                    int ni = (safeFrame + 1) % frames.Count;
                    animOffset = Vector2.Lerp(
                        new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
                        new Vector2(frames[ni].X, frames[ni].Y), prog) * speciesScale;
                    animRot = frames[safeFrame].Z + (frames[ni].Z - frames[safeFrame].Z) * prog;
                }

                Vector2 fb = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
                Vector2 fa = new(animOffset.X * facing, animOffset.Y);
                Vector2 finalOffset = fb + fa;
                if (facing < 0)
                    finalOffset = new Vector2(-finalOffset.X, finalOffset.Y);

                if (!string.IsNullOrEmpty(part.ParentName) && indexMap.TryGetValue(part.ParentName, out int parentIndex)
                    && parentIndex != index)
                {
                    Compute(parentIndex);
                    float parentRot = rotations[parentIndex];
                    Vector2 parentPos = positions[parentIndex];
                    rotations[index] = parentRot + (part.BaseRot + animRot) * facing;
                    positions[index] = parentPos + RotateVec(finalOffset, parentRot);
                }
                else
                {
                    positions[index] = drawPos + finalOffset;
                    rotations[index] = (part.BaseRot + animRot) * facing;
                }

                computed[index] = true;
            }

            for (int i = 0; i < count; i++) Compute(i);
        }

        // ==================== ENCOMBREMENT VISUEL DES TEXTURES ====================

        private readonly record struct VisualBounds(float MinX, float MinY, float MaxX, float MaxY, float TexW, float TexH);
        private static readonly Dictionary<uint, VisualBounds> _visualBoundsCache = new();

        /// <summary>
        /// Rectangle réellement visible d'une texture (parties non transparentes),
        /// exprimé en PIXELS DE TEXTURE (coin haut-gauche inclus, coin bas-droit
        /// exclus), ainsi que la taille de la texture.
        /// </summary>
        private static VisualBounds GetVisualBounds(Texture2D texture)
        {
            float texW = texture.Width;
            float texH = texture.Height;
            if (texture.Id == 0 || texW <= 0f || texH <= 0f)
                return new VisualBounds(0f, 0f, texW, texH, texW, texH);

            // Le cache est exprimé en PIXELS DE TEXTURE : l'échelle est appliquée à la
            //  lecture, sinon deux entités de tailles différentes partageant la même
            //  texture obtiendraient un encombrement faux.
            if (!_visualBoundsCache.TryGetValue(texture.Id, out var tex))
            {
                tex = new VisualBounds(0f, 0f, texW, texH, texW, texH);
                try
                {
                    Image image = Raylib.LoadImageFromTexture(texture);
                    if (Raylib.IsImageReady(image))
                    {
                        int minX = (int)texW, minY = (int)texH, maxX = -1, maxY = -1;
                        for (int y = 0; y < (int)texH; y++)
                        {
                            for (int x = 0; x < (int)texW; x++)
                            {
                                Color c = Raylib.GetImageColor(image, x, y);
                                if (c.A <= 16) continue;
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                        if (maxX >= minX && maxY >= minY)
                        {
                            tex = new VisualBounds(
                                minX, minY,
                                maxX + 1f, maxY + 1f,
                                texW, texH);
                        }
                        Raylib.UnloadImage(image);
                    }
                }
                catch
                {
                    // Lecture impossible (texture non lisible) : on garde l'estimation par défaut.
                }
                _visualBoundsCache[texture.Id] = tex;
            }

            return tex;
        }

        // ==================== PROFIL D'ARTICULATION ====================

        private static bool Has(string name, string token) =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase);

        /// <summary>Masse relative et amplitude d'angle (± degrés autour du repos) d'une pièce.</summary>
        private static (float Mass, float LimitDeg) GetJointProfile(string partName)
        {
            bool leg = Has(partName, "leg") || Has(partName, "foot") || Has(partName, "paw") || Has(partName, "hoof");
            if (Has(partName, "head")) return (1.4f, 55f);
            if (Has(partName, "bottom")) return (leg ? 1.3f : 1.0f, leg ? 65f : 80f);   // genou / coude
            if (Has(partName, "top")) return (leg ? 2.2f : 1.8f, leg ? 80f : 120f);     // hanche / épaule
            return (0.9f, 85f);
        }

        /// <summary>
        /// Point d'attache d'une pièce sur son parent, dans le repère local de la pièce
        /// (relatif à son centre de masse). C'est l'extrémité visible qui touche le parent :
        /// le haut d'un bras / d'une jambe (épaule, coude, hanche, genou), le bas de la
        /// tête (cou). Pour les autres pièces (queue, aile, oreille...), on prend le milieu
        /// de bord le plus proche du parent.
        /// </summary>
        private Vector2 ChooseAttachLocal(int i, int p, string name)
        {
            Vector2 half = _half[i];
            float reach = MathF.Max(half.X, half.Y) * 1.25f + 3f;

            if (half.Y >= half.X * 0.6f)   // pièce plutôt verticale
            {
                Vector2? preferred = null;
                if (Has(name, "head") || Has(name, "neck")) preferred = new Vector2(0f, half.Y);
                else if (Has(name, "top") || Has(name, "bottom")) preferred = new Vector2(0f, -half.Y);

                if (preferred.HasValue)
                {
                    Vector2 world = _com[i] + RotRad(preferred.Value, _ang[i]);
                    // Garde-fou : l'extrémité choisie doit réellement toucher le parent.
                    if (DistanceToPart(p, world) <= reach) return preferred.Value;
                }
            }

            Span<Vector2> candidates = stackalloc Vector2[4];
            candidates[0] = new Vector2(0f, -half.Y);
            candidates[1] = new Vector2(0f, half.Y);
            candidates[2] = new Vector2(-half.X, 0f);
            candidates[3] = new Vector2(half.X, 0f);

            Vector2 best = candidates[0];
            float bestScore = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                Vector2 world = _com[i] + RotRad(candidates[k], _ang[i]);
                float score = DistanceToPart(p, world) + 0.01f * Vector2.Distance(world, _com[p]);
                if (score < bestScore) { bestScore = score; best = candidates[k]; }
            }
            return best;
        }

        /// <summary>Distance d'un point monde au rectangle visible d'une pièce (0 s'il est dedans).</summary>
        private float DistanceToPart(int p, Vector2 worldPoint)
        {
            Vector2 q = RotRad(worldPoint - _com[p], -_ang[p]);
            float dx = MathF.Max(MathF.Abs(q.X) - _half[p].X, 0f);
            float dy = MathF.Max(MathF.Abs(q.Y) - _half[p].Y, 0f);
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        // ==================== SIMULATION ====================

        public void Update(float dt)
        {
            Age += dt;
            if (dt <= 0f || _com.Length == 0 || _asleep) return;

            // Pas fixe : le comportement est identique à 30, 60 ou 144 FPS.
            _accumulator += MathF.Min(dt, 0.1f);
            int steps = 0;
            while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
            {
                StepSimulation(FixedStep);
                _accumulator -= FixedStep;
                steps++;
                if (_asleep) { _accumulator = 0f; break; }
            }
            if (steps >= MaxStepsPerFrame) _accumulator = 0f;   // trop de retard : on abandonne le surplus

            if (steps > 0) SyncOutput();
        }

        private void StepSimulation(float dt)
        {
            _touching = false;
            float h = dt / SubSteps;
            for (int s = 0; s < SubSteps; s++) SubStep(h);

            ApplyPlanarFriction(dt);

            if (!IsStateValid())
            {
                // Filet de sécurité : jamais de NaN à l'écran. On revient à la pose de départ
                // (à l'endroit où le corps avait glissé).
                _com = (Vector2[])_com0.Clone();
                for (int i = 0; i < _com.Length; i++) _com[i] += _planarOffset;
                _ang = (float[])_ang0.Clone();
                _planarVel = Vector2.Zero;
                Array.Clear(_vel, 0, _vel.Length);
                Array.Clear(_angVel, 0, _angVel.Length);
                _asleep = true;
                return;
            }

            UpdateSleep(dt);
        }

        /// <summary>
        /// Frottement du sol sur le glissement (Coulomb : décélération constante). Le corps
        /// ne freine que s'il touche le sol ; en l'air, seule une traînée légère s'applique.
        /// </summary>
        private void ApplyPlanarFriction(float dt)
        {
            float speed = _planarVel.Length();
            if (speed <= 0f) return;

            //  Juste après la mort, le frottement est réduit puis remonte jusqu'à sa valeur normale.
            float friction = PlanarFriction;
            if (_carryMomentum)
                friction *= CarryFrictionScale + (1f - CarryFrictionScale) * Math.Clamp(_simTime / CarryDuration, 0f, 1f);

            float decel = (_touching ? friction : PlanarAirDrag) * dt;
            if (speed <= decel) { _planarVel = Vector2.Zero; return; }
            _planarVel *= (speed - decel) / speed;
        }

        private void SubStep(float h)
        {
            int n = _com.Length;
            _simTime += h;

            // 0) Glissement dans le plan du sol : TOUT le corps (et la ligne de sol, donc
            //    le tri de rendu et l'ombre) se translate. Fait AVANT la prédiction, donc
            //    _comPrev suit : le déplacement n'est pas vu comme une vitesse de la
            //    simulation verticale et ne perturbe ni la gravité ni le contact.
            if (_planarVel.LengthSquared() > 1e-6f)
            {
                Vector2 d = _planarVel * h;
                for (int i = 0; i < n; i++) _com[i] += d;
                GroundY += d.Y;
                AnchorPosition += d;
                _planarOffset += d;
            }

            ApplyToppleAssist(h);

            // 1) Prédiction : gravité + amortissement.
            float linDamp = MathF.Max(0f, 1f - LinearDamping * h);
            float angDamp = MathF.Max(0f, 1f - AngularDamping * h);
            for (int i = 0; i < n; i++)
            {
                _vel[i].Y += Gravity * h;
                _vel[i] *= linDamp;
                _angVel[i] *= angDamp;

                _comPrev[i] = _com[i];
                _angPrev[i] = _ang[i];
                _com[i] += _vel[i] * h;
                _ang[i] += _angVel[i] * h;
            }

            // 2) Contraintes : articulations (point + angle) puis contact avec le sol.
            //    La raideur musculaire décroît : le corps est raide au début de la chute.
            float tone = MathF.Max(0f, 1f - _simTime / _toneDuration);
            float toneFrac = ToneStrength * tone * tone;
            for (int iter = 0; iter < SolverIterations; iter++)
            {
                SolveJoints(iter == 0 ? toneFrac : 0f);
                SolveContacts();
            }

            // 3) Vitesses déduites des déplacements.
            for (int i = 0; i < n; i++)
            {
                _vel[i] = (_com[i] - _comPrev[i]) / h;
                _angVel[i] = (_ang[i] - _angPrev[i]) / h;
            }

            // 4) Frottement interne des articulations.
            DampJoints(h);
        }

        /// <summary>
        /// Articulations : les deux points d'attache coïncident (le membre reste VRAIMENT
        /// accroché à son parent, au niveau de l'épaule / du coude / de la hanche...), et
        /// l'angle relatif reste dans sa plage.
        /// </summary>
        private void SolveJoints(float toneFrac)
        {
            for (int i = 0; i < _com.Length; i++)
            {
                int p = _parent[i];
                if (p < 0) continue;

                // --- Point commun ---
                Vector2 ra = RotRad(_jointA[i], _ang[i]);
                Vector2 rb = RotRad(_jointB[i], _ang[p]);
                Vector2 e = (_com[i] + ra) - (_com[p] + rb);
                float len = e.Length();
                if (len > 1e-5f)
                {
                    Vector2 n = e / len;
                    float rna = Cross(ra, n);
                    float rnb = Cross(rb, n);
                    float w = _invMass[i] + _invMass[p]
                            + _invInertia[i] * rna * rna + _invInertia[p] * rnb * rnb;
                    if (w > 0f)
                    {
                        Vector2 P = -n * (len / w);
                        _com[i] += P * _invMass[i];
                        _ang[i] += _invInertia[i] * Cross(ra, P);
                        _com[p] -= P * _invMass[p];
                        _ang[p] -= _invInertia[p] * Cross(rb, P);
                    }
                }

                // --- Plage d'angle + raideur musculaire ---
                float wc = _invInertia[i], wp = _invInertia[p];
                float wsum = wc + wp;
                if (wsum <= 0f) continue;

                float rel = WrapRad(_ang[i] - _ang[p] - _rel0[i]);
                float corr = 0f;
                if (rel > _limMax[i]) corr = rel - _limMax[i];
                else if (rel < _limMin[i]) corr = rel - _limMin[i];
                float stiffness = _welded[i] ? 1f : toneFrac;
                corr += (rel - corr) * stiffness;
                if (corr == 0f) continue;

                _ang[i] -= corr * wc / wsum;
                _ang[p] += corr * wp / wsum;
            }
        }

        /// <summary>
        /// Contact avec le sol, pièce par pièce (les 4 coins du rectangle visible) : un
        /// membre peut reposer au sol seul, sans soulever tout le corps. Frottement de
        /// Coulomb au niveau des positions : le corps pivote sur ses pieds puis glisse
        /// et s'arrête.
        /// </summary>
        private void SolveContacts()
        {
            for (int i = 0; i < _com.Length; i++)
            {
                float invM = _invMass[i], invI = _invInertia[i];
                for (int k = 0; k < 4; k++)
                {
                    Vector2 local = CornerLocal(i, k);
                    Vector2 r = RotRad(local, _ang[i]);
                    float pen = _com[i].Y + r.Y - GroundY;
                    if (pen <= 0f) continue;
                    _touching = true;
                    _hasLanded = true;

                    // Normale (0,-1) : (r x n)^2 = r.X^2
                    pen = MathF.Min(pen, MaxContactPush);
                    float w = invM + invI * r.X * r.X;
                    Vector2 P = new(0f, -pen / w);
                    _com[i] += P * invM;
                    _ang[i] += invI * Cross(r, P);

                    // Frottement statique, limité par mu * pénétration.
                    r = RotRad(local, _ang[i]);
                    Vector2 rPrev = RotRad(local, _angPrev[i]);
                    float dx = (_com[i].X + r.X) - (_comPrev[i].X + rPrev.X);
                    if (MathF.Abs(dx) < 1e-6f) continue;

                    float maxCorr = GroundFrictionCoef * pen;
                    float corr = Math.Clamp(-dx, -maxCorr, maxCorr);
                    float wt = invM + invI * r.Y * r.Y;   // tangente (1,0) : (r x t)^2 = r.Y^2
                    Vector2 Pt = new(corr / wt, 0f);
                    _com[i] += Pt * invM;
                    _ang[i] += invI * Cross(r, Pt);
                }
            }
        }

        /// <summary>Les membres ne battent pas indéfiniment : la vitesse de rotation relative s'amortit.</summary>
        private void DampJoints(float h)
        {
            float k = MathF.Min(1f, JointDamping * h);
            for (int i = 0; i < _com.Length; i++)
            {
                int p = _parent[i];
                if (p < 0) continue;
                float wc = _invInertia[i], wp = _invInertia[p];
                float wsum = wc + wp;
                if (wsum <= 0f) continue;

                float d = -(_angVel[i] - _angVel[p]) * k;
                _angVel[i] += d * wc / wsum;
                _angVel[p] -= d * wp / wsum;
            }
        }

        /// <summary>
        /// Petit coup de pouce pour que le corps finisse bien couché sur le côté (c'est le
        /// parti pris visuel du jeu), y compris pour les créatures larges et stables. Il ne
        /// freine jamais : si la gravité fait déjà tomber le corps assez vite, il n'agit pas.
        /// </summary>
        private void ApplyToppleAssist(float h)
        {
            if (!_hasLanded || _assistTime >= AssistDuration) return;
            _assistTime += h;

            float err = (_toppleTarget - (_ang[_root] - _rootAng0)) * _fallSign;   // > 0 : doit encore tourner
            if (err <= 0.02f) return;
            if (_angVel[_root] * _fallSign >= MinFallSpeed) return;

            float alpha = _fallSign * AssistAccel * Math.Clamp(err / 0.4f, 0f, 1f);
            Vector2 pivot = FindToppleBase();
            for (int i = 0; i < _com.Length; i++)
            {
                Vector2 r = _com[i] - pivot;
                _vel[i] += new Vector2(-r.Y, r.X) * (alpha * h);
                _angVel[i] += alpha * h;
            }
        }

        /// <summary>Coin d'appui de la bascule : le point le plus bas, côté où le corps tombe.</summary>
        private Vector2 FindToppleBase()
        {
            Span<Vector2> corners = stackalloc Vector2[4];
            float maxY = float.MinValue;
            for (int i = 0; i < _com.Length; i++)
            {
                GetPartCorners(i, corners);
                for (int k = 0; k < 4; k++)
                    if (corners[k].Y > maxY) maxY = corners[k].Y;
            }

            Vector2 best = _com[_root];
            float bestX = float.MinValue;
            for (int i = 0; i < _com.Length; i++)
            {
                GetPartCorners(i, corners);
                for (int k = 0; k < 4; k++)
                {
                    if (corners[k].Y < maxY - 2f) continue;
                    float score = corners[k].X * _fallSign;
                    if (score > bestX) { bestX = score; best = corners[k]; }
                }
            }
            return best;
        }

        private float LowestCornerY()
        {
            Span<Vector2> corners = stackalloc Vector2[4];
            float maxY = float.MinValue;
            for (int i = 0; i < _com.Length; i++)
            {
                GetPartCorners(i, corners);
                for (int k = 0; k < 4; k++)
                    if (corners[k].Y > maxY) maxY = corners[k].Y;
            }
            return maxY;
        }

        private void UpdateSleep(float dt)
        {
            if (_simTime < MinAwakeTime || !_touching) { _restTimer = 0f; return; }

            float maxV2 = _planarVel.LengthSquared(), maxW = 0f;
            for (int i = 0; i < _com.Length; i++)
            {
                maxV2 = MathF.Max(maxV2, _vel[i].LengthSquared());
                maxW = MathF.Max(maxW, MathF.Abs(_angVel[i]));
            }

            if (maxV2 < SleepLinear * SleepLinear && maxW < SleepAngular)
            {
                _restTimer += dt;
                if (_restTimer >= SleepDelay)
                {
                    _planarVel = Vector2.Zero;
                    Array.Clear(_vel, 0, _vel.Length);
                    Array.Clear(_angVel, 0, _angVel.Length);
                    _asleep = true;
                }
            }
            else
            {
                _restTimer = 0f;
            }
        }

        private bool IsStateValid()
        {
            if (!float.IsFinite(_planarVel.X) || !float.IsFinite(_planarVel.Y)) return false;
            for (int i = 0; i < _com.Length; i++)
            {
                if (!float.IsFinite(_com[i].X) || !float.IsFinite(_com[i].Y) || !float.IsFinite(_ang[i]))
                    return false;
            }
            return true;
        }

        // ==================== POSE TRANSMISE AU RENDU ====================

        /// <summary>
        /// Convertit l'état physique (centres de masse + angles) en pose de rendu : CENTRE
        /// de la texture (pivot du rendu) et rotation en degrés.
        /// </summary>
        private void SyncOutput()
        {
            for (int i = 0; i < _com.Length; i++)
            {
                Vector2 texCenter = _com[i] + RotRad(_texCenterLocal[i], _ang[i]);
                _pos[i] = texCenter;
                _rot[i] = _ang[i] * Rad2Deg;
            }
            Position = _com[_root];
        }

        // ==================== INTERACTIONS ====================

        /// <summary>
        /// Pousse le corps (vitesse en px/s) DANS LE PLAN DU SOL : <paramref name="velocity"/>.X
        /// est l'axe horizontal de l'écran, <paramref name="velocity"/>.Y la profondeur (haut /
        /// bas de l'écran). Le corps glisse puis s'arrête par frottement ; il ne décolle pas.
        /// Les poussées ne s'additionnent PAS : appeler ApplyPush(300) dix fois donne 300 px/s,
        /// pas 3000 (un corps ne peut pas aller plus vite que ce qui le pousse, et un appel
        /// répété à chaque frame ne fait pas s'emballer le corps).
        /// <paramref name="lift"/> (px/s, optionnel) : petit sursaut vertical (explosions).
        /// Un corps au repos est réveillé.
        /// </summary>
        public void ApplyPush(Vector2 velocity, float lift = 0f)
        {
            if (_vel.Length == 0) return;

            float len = velocity.Length();
            bool slide = len > 0.01f;
            bool hop = lift > 0.01f;
            if (!slide && !hop) return;

            if (slide)
            {
                Vector2 dir = velocity / len;
                float wanted = MathF.Min(len, MaxPushSpeed);
                float gain = wanted - Vector2.Dot(_planarVel, dir);   // vitesse réellement ajoutée
                if (gain > 0f)
                {
                    _planarVel = ClampLength(_planarVel + dir * gain, MathF.Max(MaxPushSpeed, _planarVel.Length()));

                    // Les membres ballottent un peu (seulement si la poussée apporte vraiment
                    // de la vitesse, sinon une poussée continue ferait trembler le corps).
                    float k = gain / MaxPushSpeed;
                    for (int i = 0; i < _angVel.Length; i++)
                        if (i != _root) _angVel[i] += Rand(-1f, 1f) * LimbKick * k;
                }
            }

            if (hop)
            {
                float up = MathF.Min(lift, MaxLift);
                for (int i = 0; i < _vel.Length; i++)
                    _vel[i].Y = MathF.Min(_vel[i].Y, -up);   // pas d'empilement non plus
            }

            _asleep = false;
            _restTimer = 0f;
        }

        /// <summary>Nombre de pièces (morceaux) du corps.</summary>
        public int PartCount => _pos.Length;

        /// <summary>Rectangle de collision (poussée) d'une pièce, en coordonnées monde.</summary>
        public Rectangle GetPartHitbox(int i)
        {
            if (i < 0 || i >= _com.Length) return new Rectangle();
            GetPartAabb(i, out float minX, out float minY, out float maxX, out float maxY);
            float pad = 2f;
            return new Rectangle(minX - pad, minY - pad,
                (maxX - minX) + pad * 2f, (maxY - minY) + pad * 2f);
        }

        /// <summary>Pousse une pièce spécifique ; la physique de contrainte propage au reste.</summary>
        public void ApplyPushPart(int partIndex, Vector2 velocity, float lift = 0f)
        {
            if (_vel.Length == 0 || partIndex < 0 || partIndex >= _vel.Length) return;

            float len = velocity.Length();
            bool slide = len > 0.01f;
            bool hop = lift > 0.01f;
            if (!slide && !hop) return;

            if (slide)
            {
                Vector2 dir = velocity / len;
                float wanted = MathF.Min(len, MaxPushSpeed);
                float gain = wanted - Vector2.Dot(_vel[partIndex], dir);
                if (gain > 0f)
                {
                    _vel[partIndex] = ClampLength(_vel[partIndex] + dir * gain, MaxPushSpeed);
                    _angVel[partIndex] += Rand(-1f, 1f) * LimbKick * (gain / MaxPushSpeed);
                }
            }

            if (hop)
            {
                float up = MathF.Min(lift, MaxLift);
                _vel[partIndex].Y = MathF.Min(_vel[partIndex].Y, -up);
            }

            _asleep = false;
            _restTimer = 0f;
        }
        public Rectangle GetPushHitbox()
        {
            GetVisualAabb(out float minX, out float minY, out float maxX, out float maxY);
            float pad = 2f;
            return new Rectangle(minX - pad, minY - pad,
                (maxX - minX) + pad * 2f, (maxY - minY) + pad * 2f);
        }

        private Vector2 CornerLocal(int i, int k) => k switch
        {
            0 => new Vector2(-_half[i].X, -_half[i].Y),
            1 => new Vector2(_half[i].X, -_half[i].Y),
            2 => new Vector2(_half[i].X, _half[i].Y),
            _ => new Vector2(-_half[i].X, _half[i].Y),
        };

        /// <summary>Les quatre coins du rectangle visible d'une pièce, en coordonnées monde (après rotation).</summary>
        private void GetPartCorners(int i, Span<Vector2> corners)
        {
            float cos = MathF.Cos(_ang[i]);
            float sin = MathF.Sin(_ang[i]);
            for (int k = 0; k < 4; k++)
            {
                Vector2 l = CornerLocal(i, k);
                corners[k] = _com[i] + new Vector2(l.X * cos - l.Y * sin, l.X * sin + l.Y * cos);
            }
        }

        private void GetPartAabb(int i, out float minX, out float minY, out float maxX, out float maxY)
        {
            Span<Vector2> corners = stackalloc Vector2[4];
            GetPartCorners(i, corners);

            minX = float.MaxValue; minY = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue;

            for (int k = 0; k < 4; k++)
            {
                if (corners[k].X < minX) minX = corners[k].X;
                if (corners[k].X > maxX) maxX = corners[k].X;
                if (corners[k].Y < minY) minY = corners[k].Y;
                if (corners[k].Y > maxY) maxY = corners[k].Y;
            }
        }

        /// <summary>
        /// Emprise visible du corps (enveloppe de toutes les pièces), en coordonnées
        /// monde. Sert à la zone de poussée et à l'ombre au sol.
        /// </summary>
        private void GetVisualAabb(out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue; minY = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue;

            for (int i = 0; i < _com.Length; i++)
            {
                GetPartAabb(i, out float pminX, out float pminY, out float pmaxX, out float pmaxY);

                if (pminX < minX) minX = pminX;
                if (pminY < minY) minY = pminY;
                if (pmaxX > maxX) maxX = pmaxX;
                if (pmaxY > maxY) maxY = pmaxY;
            }

            if (minX > maxX)
            {
                // Corps sans pièce (cas dégénéré) : on retombe sur la position de l'os racine.
                minX = maxX = Position.X;
                minY = maxY = Position.Y;
            }
        }

        // ==================== RENDU ====================

        /// <summary>
        /// Dessine le corps avec le moteur de rendu des ENTITÉS : mêmes textures, mêmes
        /// équipements, mêmes teintures. Seule la pose diffère (elle vient de la physique).
        /// </summary>
        public void Draw(List<Texture2D> hairBase, List<Texture2D> hairOverlay)
        {
            if (_pos.Length == 0) return;

            int hairIndex = HairStyle;
            if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
            Texture2D hairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
            Texture2D hairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();

            // Ombre dessinée séparément (elle est un DrawEllipse : le shader de
            // dissolution ne s'y applique pas, donc elle continue de s'effacer via AlphaFactor).
            DrawGroundShadow();

            Vector2 anchor = AnchorPosition;
            Color tint = Tint;   // plus de fade d'alpha : la dissolution gère la disparition

            // Progression de la dissolution (0 = intact, 1 = entièrement dissous).
            // Elle démarre pile au début de la fenêtre de fondu.
            float fadeStart = Lifetime - FadeDuration;
            float dissolveProgress = Age < fadeStart
                ? 0f
                : Math.Clamp((Age - fadeStart) / FadeDuration, 0f, 1f);

            bool shaderUsed = false;
            if (dissolveProgress > 0.001f)
            {
                if (EntityRenderer.BeginDissolveShader(dissolveProgress))
                {
                    shaderUsed = true;
                }
                else
                {
                    // Fallback : shader indisponible, on revient au fondu d'alpha classique.
                    float alpha = 1f - dissolveProgress;
                    tint = new Color(tint.R, tint.G, tint.B, (byte)MathF.Round(tint.A * alpha));
                }
            }

            try
            {
                EntityRenderer.DrawEntity(
                    Species,
                    "idle", 0, 0f, Facing,
                    anchor, tint,
                    hairBaseTex, hairOverlayTex, HairColor,
                    SpeciesData.Skeletons,
                    eyes: EyesTexture.Id != 0 ? EyesTexture : EntityRenderer.GetEyesTextureForSpecies(Species),
                    mouth: MouthTexture,
                    customScale: CustomScale,
                    equipment: Equipment,
                    inWater: false,
                    underwearTexture: UnderwearTexture,
                    beardStyle: BeardStyle,
                    forceBlinking: true,
                    randomFeatureVariant: RandomFeatureVariant,
                    randomFeatureColor: RandomFeatureColor,
                    drawShadow: false,
                    posePositions: _pos,
                    poseRotations: _rot);
            }
            finally
            {
                if (shaderUsed) EntityRenderer.EndDissolveShader();
            }
        }

        private void DrawGroundShadow()
        {
            GetVisualAabb(out float minX, out float minY, out float maxX, out float maxY);
            float cx = (minX + maxX) * 0.5f;
            float shadowY = GroundY;
            float rx = MathF.Max(16f, (maxX - minX) * 0.5f);
            float ry = MathF.Max(4f, (maxY - minY) * 0.22f);

            //  L'ombre disparaît un peu plus tôt que la dissolution du corps : sinon
            //  on voit l'ombre intacte sous une silhouette déjà à moitié évaporée,
            //  ce qui trahit l'effet.
            float dissolveProgress = Age < Lifetime - FadeDuration
                ? 0f
                : Math.Clamp((Age - (Lifetime - FadeDuration)) / FadeDuration, 0f, 1f);
            float shadowFade = Math.Clamp(1f - dissolveProgress * 1.6f, 0f, 1f);

            byte a = (byte)MathF.Round(70f * shadowFade);
            Raylib.DrawEllipse((int)cx, (int)shadowY, (int)rx, (int)ry, new Color((byte)0, (byte)0, (byte)0, a));
        }

        // ==================== OUTILS ====================

        private static float Rand(float min, float max) => min + Random.Shared.NextSingle() * (max - min);

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static Vector2 ClampLength(Vector2 v, float max)
        {
            float len2 = v.LengthSquared();
            return len2 <= max * max ? v : v / MathF.Sqrt(len2) * max;
        }

        private static float WrapRad(float r)
        {
            r %= MathF.Tau;
            if (r > MathF.PI) r -= MathF.Tau;
            else if (r < -MathF.PI) r += MathF.Tau;
            return r;
        }

        private static Vector2 RotRad(Vector2 v, float rad)
        {
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private static Vector2 RotateVec(Vector2 v, float angleDegrees) => RotRad(v, angleDegrees * Deg2Rad);
    }

    /// <summary>
    /// Gestion des corps morts : création, mise à jour, durée de vie (20 s) et
    /// interactions (poussée par le joueur, les véhicules, les explosions).
    /// </summary>
    public static class CorpseSystem
    {
        /// <summary>Nombre maximal de corps simultanés (au-delà, les plus anciens sont retirés).</summary>
        public const int MaxCorpses = 24;

        /// <summary>Part de la poussée d'une explosion convertie en sursaut vertical (0 = aucun).</summary>
        private const float ExplosionLiftRatio = 0.15f;

        public static readonly List<Corpse> Corpses = new();

        /// <summary>Corps du joueur local (la caméra le suit pendant la mort).</summary>
        public static Corpse? PlayerCorpse;

        /// <summary>
        /// Crée un corps à partir d'une entité morte. <paramref name="impulse"/> (optionnel) :
        /// élan de la mort en px/s (direction du coup, vitesse de la créature...).
        /// </summary>
        public static Corpse? SpawnFromEntity(Entity entity, Vector2 drawPos, float groundY, Vector2 impulse = default, Vector2 momentum = default)
        {
            if (entity.IsWorm) return null;   // rendu segmenté dédié (pas de squelette)

            return Register(Corpse.Create(
                entity.Species,
                entity.Tint,
                drawPos,
                groundY,
                entity.Facing,
                entity.AnimState,
                entity.CurrentFrame,
                entity.AnimProgress,
                entity.Scale,
                entity.Equipment,
                entity.HairStyle,
                entity.HairColor,
                entity.BeardStyle,
                entity.RandomFeatureVariant,
                entity.RandomFeatureColor,
                isPlayer: false,
                underwearTexture: Program.LeafUnderpantsTexture,
                eyesTexture: EntityRenderer.GetEyesTextureForSpecies(entity.Species),
                mouthTexture: Program.GetMouthTextureFor(entity),
                impulse: impulse,
                momentum: momentum));
        }

        /// <summary>
        /// MULTIJOUEUR : crée le corps d'une entité distante à partir du dernier état
        /// connu envoyé par l'hôte (le client ne simule pas les entités, mais il doit
        /// pouvoir voir les corps au sol comme l'hôte).
        /// </summary>
        public static Corpse? SpawnFromDto(EntityDto dto, Vector2 drawPos, float groundY, Vector2 momentum = default)
        {
            if (string.IsNullOrEmpty(dto.Species)) return null;

            Color tint = new(dto.Tint.R, dto.Tint.G, dto.Tint.B, dto.Tint.A);
            Color hair = new(dto.HairColor.R, dto.HairColor.G, dto.HairColor.B, dto.HairColor.A);
            var equipment = Program.GetNetworkEntityEquipment(dto.NetId, dto.Equip);

            return Register(Corpse.Create(
                dto.Species,
                tint,
                drawPos,
                groundY,
                dto.Facing,
                dto.AnimState,
                dto.AnimFrame,
                dto.AnimProg,
                dto.Scale,
                equipment,
                dto.HairStyle,
                hair,
                dto.BeardStyle,
                null,
                null,
                isPlayer: false,
                underwearTexture: Program.LeafUnderpantsTexture,
                eyesTexture: EntityRenderer.GetEyesTextureForSpecies(dto.Species),
                mouthTexture: default,
                momentum: momentum));
        }

        /// <summary>Crée le corps du joueur au moment de sa mort.</summary>
        public static Corpse? SpawnPlayer(Vector2 drawPos, float groundY, float facing,
            string anim, int frame, float prog, Vector2 impulse = default, Vector2 momentum = default)
        {
            string species = string.IsNullOrWhiteSpace(Program.PlayerMorphSpecies)
                ? "human"
                : Program.PlayerMorphSpecies;
            bool human = string.Equals(species, "human", StringComparison.OrdinalIgnoreCase);

            return Register(Corpse.Create(
                species,
                human ? Program.SkinColor : Program.PlayerMorphTint,
                drawPos,
                groundY,
                facing,
                anim,
                frame,
                prog,
                1f,
                Program.equipment,
                Program.PlayerHairStyle,
                Program.HairColor,
                Program.PlayerBeardStyle,
                Program.PlayerMorphFeatureVariants,
                Program.PlayerMorphFeatureColors,
                isPlayer: true,
                //  Le pagne n'est porté que par l'humain : les autres espèces gardent
                //  leur apparence d'origine (comme à l'écran).
                underwearTexture: human ? Program.LeafUnderpantsTexture : new Texture2D(),
                eyesTexture: human ? Program.EyesTexture : EntityRenderer.GetEyesTextureForSpecies(species),
                mouthTexture: human ? Program.MouthTexture : default,
                impulse: impulse,
                momentum: momentum));
        }

        /// <summary>
        /// MULTIJOUEUR : crée le corps d'un joueur distant mort (vu par les clients).
        /// </summary>
        public static Corpse? SpawnRemotePlayer(Program.LocalPlayer rp, Vector2 momentum = default)
        {
            string species = string.IsNullOrWhiteSpace(rp.SpeciesOverride) ? "human" : rp.SpeciesOverride;
            bool human = string.Equals(species, "human", StringComparison.OrdinalIgnoreCase);

            Color tint = human
                ? (rp.SkinColorOverride ?? Program.SkinColor)
                : (rp.MorphTintOverride ?? Program.PlayerMorphTint);
            Color hair = rp.HairColorOverride ?? Program.HairColor;
            Vector2 drawPos = Program.GetPlayerVisualPosition(rp.Position);
            var equipment = rp.IsNetworkPlayer
                ? Program.GetNetworkPlayerEquipmentFull(rp.Id, rp.Equip)
                : Program.equipment;

            return Register(Corpse.Create(
                species,
                tint,
                drawPos,
                drawPos.Y + (Program.FeetOffsetY - 6f),
                rp.Facing,
                rp.AnimState,
                rp.AnimFrame,
                rp.AnimProg,
                1f,
                equipment,
                rp.HairStyleOverride ?? Program.PlayerHairStyle,
                hair,
                rp.BeardStyleOverride ?? 0,
                rp.MorphFeatureVariantsOverride,
                rp.MorphFeatureColorsOverride,
                isPlayer: false,
                underwearTexture: human ? Program.LeafUnderpantsTexture : new Texture2D(),
                eyesTexture: human ? Program.EyesTexture : EntityRenderer.GetEyesTextureForSpecies(species),
                mouthTexture: human ? Program.MouthTexture : default,
                momentum: momentum));
        }

        private static Corpse? Register(Corpse? corpse)
        {
            if (corpse == null) return null;

            if (corpse.IsPlayer)
            {
                // Un seul corps pour le joueur : l'ancien est retiré.
                if (PlayerCorpse != null)
                    Corpses.Remove(PlayerCorpse);
                PlayerCorpse = corpse;
            }

            Corpses.Add(corpse);

            while (Corpses.Count > MaxCorpses)
            {
                int index = Corpses.FindIndex(c => !c.IsPlayer);
                if (index < 0) break;
                Corpses.RemoveAt(index);
            }

            return corpse;
        }

        /// <summary>Mise à jour de tous les corps (physique + durée de vie).</summary>
        public static void Update(float dt)
        {
            for (int i = Corpses.Count - 1; i >= 0; i--)
            {
                var corpse = Corpses[i];
                corpse.Update(dt);
                if (corpse.Age >= corpse.Lifetime)
                {
                    Corpses.RemoveAt(i);
                    if (ReferenceEquals(PlayerCorpse, corpse))
                        PlayerCorpse = null;
                }
            }
        }

        /// <summary>Souffle d'explosion : poussée radiale dans le plan du sol, avec atténuation
        /// et un léger sursaut (mettre ExplosionLiftRatio à 0 pour le supprimer).</summary>
        public static void Explosion(Vector2 center, float radius, float force)
        {
            foreach (var c in Corpses)
            {
                Vector2 d = c.Position - center;
                float dist = d.Length();
                if (dist >= radius) continue;
                Vector2 dir = dist > 0.01f ? d / dist : new Vector2(0f, -1f);
                float falloff = 1f - dist / radius;
                float power = force * falloff;
                c.ApplyPush(dir * power, power * ExplosionLiftRatio);
            }
        }

        /// <summary>Retire le corps du joueur (réapparition).</summary>
        public static void RemovePlayerCorpse()
        {
            if (PlayerCorpse != null)
            {
                Corpses.Remove(PlayerCorpse);
                PlayerCorpse = null;
            }
        }

        /// <summary>Supprime tous les corps (changement de monde, chargement d'une sauvegarde...).</summary>
        public static void Clear()
        {
            Corpses.Clear();
            PlayerCorpse = null;
        }

        /// <summary>Position suivi par la caméra quand le joueur est mort.</summary>
        public static Vector2 GetPlayerCorpsePosition(Vector2 fallback)
        {
            return PlayerCorpse != null ? PlayerCorpse.Position : fallback;
        }
    }
}