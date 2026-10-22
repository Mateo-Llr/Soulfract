#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Core fields, combat profile, and boss state data
        public static readonly Color GenieBlueTint = new Color(45, 145, 235, 255);

        // ==================== CHAMPS PRINCIPAUX ====================
        public Vector2 WorldPos;
        public string Species;
        public string AnimState = "idle";
        public int CurrentFrame = 0;
        public float AnimProgress = 0f;
        public float Facing = 1f;
        public Color Tint;
        public Color HairColor;
        public int HairStyle;
        public int BeardStyle;

        //  CARACTÈRES ALÉATOIRES (paws, face, back, beak, etc. — définis dans species.json)
        // Clé = nom de la feature ; valeur = index de variante choisi (-1 = pas de feature pour cette entité)
        public Dictionary<string, int> RandomFeatureVariant = new();
        // Teinte aléatoire associée, uniquement pour les features marquées "colorable" dans species.json
        public Dictionary<string, Color> RandomFeatureColor = new();
        public float DamageFlash = 0f;

        //  MULTIJOUEUR : identifiant stable utilisé pour cibler les entités sur le réseau
        public Guid NetId = Guid.NewGuid();
        public bool IsSpiritAnimal;
        public int SpiritTotemTileX = int.MinValue;
        public int SpiritTotemTileY = int.MinValue;

        public bool IsInWater { get; private set; }
        private float _legHeight;

        // Statistiques
        public int MaxHP;
        private int _currentHP;
        private float _naturalHealAccumulator;
        private float _bloodDripTimer;
        public int CurrentHP
        {
            get => _currentHP;
            set => _currentHP = value;
        }
        public float Concussion { get; private set; }
        public bool IsKnockedOut { get; private set; }
        public bool CanBeTamedByPlayer
        {
            get
            {
                if (IsKnockedOut)
                    return true;

                string naturalBehavior = SpeciesData.GetSpeciesInfo(Species)?.Behavior ?? Behavior;
                if (string.Equals(naturalBehavior, "hostile", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(Behavior, "hostile", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(naturalBehavior, "neutral", StringComparison.OrdinalIgnoreCase)))
                    return false;

                return !string.Equals(naturalBehavior, "neutral", StringComparison.OrdinalIgnoreCase)
                    || (AiState != NpcAiState.Chase && _attackPhase == AttackPhase.None
                        && !string.Equals(Behavior, "hostile", StringComparison.OrdinalIgnoreCase));
            }
        }

        public void SetHealthSilently(int currentHP, int maxHP)
        {
            MaxHP = maxHP;
            _currentHP = currentHP;
            _bloodDripTimer = 0f;
        }

        public void ApplyConcussion(int amount)
        {
            if (amount <= 0 || !IsAlive || MaxHP <= 0)
                return;

            Concussion = Math.Min(MaxHP * 2f, Concussion + amount);
            if (!IsKnockedOut && Concussion >= CurrentHP)
                KnockOut();
        }

        private void KnockOut()
        {
            IsKnockedOut = true;
            _attackPhase = AttackPhase.None;
            _telegraphTimer = 0f;
            _chargeTimer = 0f;
            _recoveryTimer = 0f;
            _hasDealtDamage = false;
            _currentPath.Clear();
            ClearAlertSignal();
            ResetMoveMomentum();
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            AnimState = "stunned";
        }

        public void SetConcussionSilently(float concussion, bool isKnockedOut)
        {
            Concussion = Math.Clamp(concussion, 0f, Math.Max(0, MaxHP) * 2f);
            IsKnockedOut = isKnockedOut && Concussion > 0f && IsAlive;
            if (IsKnockedOut)
                AnimState = "stunned";
        }

        public void UpdateNaturalHealing(float dt)
        {
            const float HEAL_INTERVAL = 15f;
            if (dt <= 0f || IsPlayer || !IsAlive || MaxHP <= 0 || CurrentHP >= MaxHP)
            {
                _naturalHealAccumulator = 0f;
                return;
            }

            _naturalHealAccumulator += dt;
            if (_naturalHealAccumulator < HEAL_INTERVAL)
                return;

            int healing = (int)(_naturalHealAccumulator / HEAL_INTERVAL);
            _naturalHealAccumulator -= healing * HEAL_INTERVAL;
            CurrentHP = Math.Min(MaxHP, CurrentHP + healing);
            if (CurrentHP >= MaxHP)
                _naturalHealAccumulator = 0f;
        }

        public void UpdateBloodDrips(float dt, int currentHP, int maxHP, string? renderSpecies = null,
            float customScale = 1f, Vector2? visualPosition = null, bool inWater = false)
        {
            if (dt <= 0f || maxHP <= 0 || currentHP <= 0 || currentHP >= maxHP)
            {
                _bloodDripTimer = 0f;
                return;
            }

            float severity = Math.Clamp((maxHP - currentHP) / (float)maxHP, 0f, 1f);
            float interval = MathHelper.Lerp(1.1f, 0.25f, severity);
            _bloodDripTimer += dt;
            if (_bloodDripTimer < interval)
                return;

            _bloodDripTimer %= interval;
            EntityRenderer.SpawnBloodDrips(this, severity, renderSpecies, customScale, visualPosition, inWater);
        }

        public float Speed;
        public float BaseSpeed;
        public float RunSpeed;
        public int Attack;
        public string Behavior;
        public int VisionRange;
        public float AlertDuration;

        // ===== PORTÉES D'ATTAQUE =====
        public float AttackHitRange;     // Distance réelle pour infliger des dégâts

        // ==================== SYSTÈME D'ATTAQUE EN 3 PHASES ====================
        private AttackPhase _attackPhase = AttackPhase.None;
        private Entity? _combatTargetEntity;
        private Entity? _hostileTargetEntity;
        private Entity? _retaliationTargetEntity;
        private float _retaliationTimer;
        private bool _retaliateAgainstPlayer;
        private int _infirmaryBuildingId = -1;
        private float _infirmaryHealAccumulator;
        private float _infirmaryRetryTimer;
        private Entity? _fearTargetEntity;
        private Vector2 _fearTargetPosition;
        private bool _fearTargetIsPlayer;
        private int _fearTargetConnectionId = -1;
        private bool _hasFearTarget;
        private const float RETALIATION_DURATION = 8f;
        private const float RETALIATION_RANGE = 800f;
        private float _telegraphTimer = 0f;
        private float _chargeTimer = 0f;
        private float _recoveryTimer = 0f;
        private Vector2 _lockedDirection;
        private bool _hasDealtDamage = false;

        //  POISON (générique, utilisé par les nuages de spores du bolet, voir
        // Program.SpawnPoisonCloudBurst) : même logique que le poison du joueur
        // (Program.ApplyPlayerPoison / Program_Inventory.cs), mais appliquée directement
        // sur CurrentHP ici puisque les entités n'ont pas de HP "réseau" séparé.
        private float _poisonTimer = 0f;
        private float _poisonTickAccumulator = 0f;
        public bool IsPoisoned => _poisonTimer > 0f;

        public void ApplyPoison(float duration)
        {
            if (duration <= 0f || !IsAlive) return;
            if (_poisonTimer <= 0f || duration > _poisonTimer)
            {
                _poisonTimer = Math.Max(_poisonTimer, duration);
                _poisonTickAccumulator = 0f;
            }
        }

        // Khamsin boss state machine
        private enum KhamsinBossState { Idle, Telegraph, Charge, Recover, Volley }
        private KhamsinBossState _khBossState = KhamsinBossState.Idle;
        private float _khTelegraphTimer = 0f;
        private float _khChargeTimer = 0f;
        private float _khRecoveryTimer = 0f;
        private float _khVolleyTimer = 0f;
        private float _khDashCooldown = 0f;
        private int _khVolleyShotsRemaining = 0;
        private int _khDashBurstRemaining = 0;
        private int _khVolleyBurstRemaining = 0;
        private Vector2 _khDashDirection = Vector2.Zero;

        // ==================== WORM BOSS (ver de terre géant) ====================
        public enum WormBossState { IntroEmerging, IntroRoaring, IntroDiving, Underground, Burrowing, Telegraph, Emerging, Airborne, Diving, SurfaceEmerging, SurfaceRecover, HitStagger, Retreat, Recover }
        public WormBossState WormState => _wmState;
        private WormBossState _wmState = WormBossState.IntroEmerging;
        private enum WormAttackPattern { Lunge, Sweep, Ambush }
        public enum WormGroundMarkType { Wake, Crater, Telegraph }
        public readonly record struct WormGroundMark(WormGroundMarkType Type, Vector2 Position, float Radius, float Age, float Lifetime, float Rotation);
        public readonly record struct WormIntroSegmentPose(Vector2 Position, bool Visible);
        public const int WM_SEGMENT_COUNT = 30;
        public const float WM_SEGMENT_SPACING = 26f;        // distance en pixels visée entre deux segments (trop proches avant : 14 -> 26)
        public const float WM_HEAD_LEAP_DURATION = 0.9f;    // temps que met UN segment (ex: la tête) à traverser l'arc sortie -> entrée
        private const float WM_TELEGRAPH_DURATION = 0.9f;   // tremblement au sol avant la sortie -> fenetre de fuite
        private const float WM_ARC_HEIGHT = 80f;            // hauteur du "coup de cloche"
        private const float WM_RECOVER_DURATION = 0.4f;     // pause sous terre après un saut, avant de replonger vers la sortie
        private const float WM_MIN_EXIT_DISTANCE = 260f;    // distance minimale du joueur pour choisir un point de sortie
        private const float WM_MAX_EXIT_DISTANCE = 420f;
        private const float WM_DAMAGE_RADIUS = 34f;
        private const float WM_INTRO_EMERGE_DURATION = 1.1f;
        private const float WM_INTRO_ROAR_DURATION = 1.25f;
        private const float WM_INTRO_DIVE_DURATION = 1.1f;
        private const float WM_INTRO_SEGMENT_STAGGER = 0.07f;
        private const float WM_INTRO_SEGMENT_RISE_DURATION = 0.58f;
        private const float WM_BURROW_SPEED = 260f;
        private const float WM_BURROW_WAKE_SPACING = 38f;
        private const float WM_SURFACE_RECOVER_DURATION = 1.7f;
        private const float WM_SURFACE_EMERGE_DURATION = 0.75f;
        private const float WM_HIT_STAGGER_DURATION = 0.3f;
        private const float WM_RETREAT_DURATION = 1.1f;
        private const float WM_CRATER_LIFETIME = 16f;       // les trous creusés se comblent/s'effacent en ~16 s (avant : 45 s)
        public const float WM_SEGMENT_HIT_RADIUS = 18f;     // rayon de hitbox d'un segment de corps (à ajuster selon la taille des sprites)
        public const float WM_HEAD_HIT_RADIUS = 24f;        // rayon de hitbox de la tête

        // ==================== SONS DU VER GÉANT ====================
        private Sound? _wormRumbleSound = null;
        private Sound? _wormRoarSound = null;
        private bool _wormRumbleLoaded = false;
        private float _wmTimer = 0f;
        private Vector2 _wmExitPoint;
        private Vector2 _wmEntryPoint;
        private bool _wmEntryPointLocked = false; // true des que la tete commence a sortir -> plus de changement de trajectoire
        private bool _wmHasDealtDamage = false;
        private bool _wmIntroStarted = false;
        private WormAttackPattern? _wmLastAttackPattern;
        private float _wmArcHeight = WM_ARC_HEIGHT;
        private float _wmAttackDamageRadius = WM_DAMAGE_RADIUS;
        private float _wmAttackTelegraphDuration = WM_TELEGRAPH_DURATION;
        private readonly List<WormGroundMark> _wmGroundMarks = new();
        private Vector2 _wmBurrowStart;
        private float _wmBurrowDuration;
        private float _wmBurrowDistance;
        private float _wmNextWakeDistance;
        private Vector2 _wmSurfacePoint;
        // Chaque segment i suit EXACTEMENT le même arc (sortie -> entrée) que la tête, mais démarre
        // avec un retard de i * _wmSegmentStagger secondes. Un segment n'est visible que pendant son
        // propre trajet (t entre 0 et 1) : il n'apparaît qu'à son tour, et ne disparaît que lorsqu'il a
        // fini SON arc, donc au moment où il touche vraiment le sol (jamais en plein vol).
        private float _wmSegmentStagger = 0f;
        private float _wmTotalFlightDuration = 0f;
        private int _wmLastHP = -1;           // détecte les dégâts reçus (déclenche HitStagger)
        private int _wmLastRumbleTick = -1;   // évite de rejouer le son/secousse à chaque frame du Telegraph
        private int _wmLastShakeTick = -1;
        public bool IsWorm => IsBoss && string.Equals(Species, "Worm", StringComparison.OrdinalIgnoreCase);

        // ==================== BOSS DE L'OEIL (Gemme-oeil) ====================
        //  Marque les monstres invoqués par une vague du boss de l'oeil (voir
        // Program.StartEyeBossFight), pour que leur mort inflige des dégâts au boss
        // (voir Program.UpdateEyeBossFight). Le boss lui-même n'est pas une Entity :
        // ses PV sont suivis directement par des champs statiques dans Program.cs, le
        // socle posé dans le monde (tile 118) servant de représentation visuelle.
        public bool EyeBossWaveTag = false;
        public Vector2 WormExitPoint => _wmExitPoint;
        public Vector2 WormEntryPoint => _wmEntryPoint;
        public Vector2 WormSurfacePoint => _wmSurfacePoint;
        public float WormElapsedTime => _wmTimer;
        public float WormSegmentStagger => _wmSegmentStagger;
        public float WormArcHeight => _wmArcHeight;
        public float WormIntroDiveDuration => WM_INTRO_DIVE_DURATION;
        public float WormIntroSegmentStagger => WM_INTRO_SEGMENT_STAGGER;
        public float WormIntroSegmentRiseDuration => WM_INTRO_SEGMENT_RISE_DURATION;
        public float WormRetreatDuration => WM_RETREAT_DURATION;
        public Vector2 WormHitboxCenter => _wmState is WormBossState.Emerging
            or WormBossState.Airborne
            or WormBossState.Diving
            ? WorldPos
            : World.GetEntityGroundPosition(WorldPos);
        public bool WormHeadVisible => _wmState == WormBossState.IntroEmerging
            || _wmState == WormBossState.IntroRoaring
            || _wmState == WormBossState.IntroDiving
            || _wmState == WormBossState.Emerging
            || _wmState == WormBossState.Airborne
            || _wmState == WormBossState.Diving
            || _wmState == WormBossState.SurfaceEmerging
            || _wmState == WormBossState.SurfaceRecover
            || _wmState == WormBossState.HitStagger
            || _wmState == WormBossState.Retreat;
        // Le ver est vulnérable dès qu'une partie de son corps est hors du sol (saut, sortie, pose
        // enroulée, retraite). Seule l'intro (cinématique) et les phases sous terre restent invulnérables.
        public bool WormVulnerable => _wmState is WormBossState.Emerging
            or WormBossState.Airborne
            or WormBossState.Diving
            or WormBossState.SurfaceEmerging
            or WormBossState.SurfaceRecover
            or WormBossState.HitStagger
            or WormBossState.Retreat;
        public bool WormIsInFlight => _wmState is WormBossState.Emerging
            or WormBossState.Airborne
            or WormBossState.Diving;

        // Position d'un point de l'arc de saut (partagée entre le rendu et les hitboxes).
        public static Vector2 WormArcPosition(Vector2 exit, Vector2 entry, float arcHeight, float t)
        {
            Vector2 flat = Vector2.Lerp(exit, entry, t);
            float arc = 4f * arcHeight * t * (1f - t);
            Vector2 groundPosition = World.GetEntityGroundPosition(flat);
            return new Vector2(groundPosition.X, groundPosition.Y - arc);
        }

        // Progression propre au segment i (]0,1[ tant qu'il est visible).
        public static float WormSegmentProgress(float elapsed, int index, float stagger, float headDuration)
            => (elapsed - index * stagger) / headDuration;

        // Remplit la liste avec un cercle de hitbox par partie du corps actuellement exposée.
        // Les positions sont celles du rendu : un segment très haut dans l'arc reste hors de portée.
        public void GetWormHitCircles(List<(Vector2 Center, float Radius)> output)
        {
            output.Clear();
            if (!IsWorm || !WormVulnerable) return;

            if (WormIsInFlight)
            {
                for (int i = 0; i < WM_SEGMENT_COUNT; i++)
                {
                    float t = WormSegmentProgress(_wmTimer, i, _wmSegmentStagger, WM_HEAD_LEAP_DURATION);
                    if (t <= 0.04f || t >= 0.96f) continue; // pas encore sorti / déjà rentré
                    output.Add((WormArcPosition(_wmExitPoint, _wmEntryPoint, _wmArcHeight, t),
                        i == 0 ? WM_HEAD_HIT_RADIUS : WM_SEGMENT_HIT_RADIUS));
                }
                return;
            }

            WormIntroSegmentPose[] poses = GetWormIntroSegmentPoses();
            for (int i = 0; i < poses.Length; i++)
            {
                if (!poses[i].Visible) continue;
                output.Add((World.GetEntityGroundPosition(poses[i].Position),
                    i == 0 ? WM_HEAD_HIT_RADIUS : WM_SEGMENT_HIT_RADIUS));
            }
        }

        private readonly List<(Vector2 Center, float Radius)> _wmHitBuffer = new();

        // Vrai si le cercle touche n'importe quelle partie exposée du ver.
        public bool WormHitTestCircle(Vector2 center, float radius)
        {
            GetWormHitCircles(_wmHitBuffer);
            foreach (var (c, r) in _wmHitBuffer)
            {
                float reach = r + radius;
                if (Vector2.DistanceSquared(c, center) <= reach * reach) return true;
            }
            return false;
        }

        // Vrai si le rectangle (ex : zone d'une arme) touche n'importe quelle partie exposée du ver.
        public bool WormHitTestRect(Rectangle rect)
        {
            GetWormHitCircles(_wmHitBuffer);
            foreach (var (c, r) in _wmHitBuffer)
            {
                float cx = Math.Clamp(c.X, rect.X, rect.X + rect.Width);
                float cy = Math.Clamp(c.Y, rect.Y, rect.Y + rect.Height);
                if ((c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy) <= r * r) return true;
            }
            return false;
        }
        public IReadOnlyList<WormGroundMark> WormGroundMarks => _wmGroundMarks;
        public int WormIntroSegmentCount => 8;

        public WormIntroSegmentPose[] GetWormIntroSegmentPoses()
        {
            Vector2 start = _wmSurfacePoint + new Vector2(0f, 24f);
            Vector2 facing = new Vector2(Facing < 0f ? -1f : 1f, 0f);
            float wobble = _wmState == WormBossState.IntroRoaring
                ? MathF.Sin(_wmTimer * 3.2f) * 3f
                : _wmState == WormBossState.SurfaceRecover
                    ? MathF.Sin(_wmTimer * 4f) * 2.5f
                    : _wmState == WormBossState.HitStagger ? -5f : 0f;
            Vector2 control1 = _wmSurfacePoint + new Vector2(-facing.X * 10f, -65f);
            Vector2 control2 = _wmSurfacePoint + new Vector2(facing.X * (-55f + wobble), -205f);
            Vector2 end = _wmSurfacePoint + new Vector2(facing.X * (30f + wobble), -155f);

            const int curveSteps = 48;
            Vector2[] curve = new Vector2[curveSteps + 1];
            float[] distances = new float[curve.Length];
            for (int step = 0; step <= curveSteps; step++)
            {
                float t = step / (float)curveSteps;
                float inverseT = 1f - t;
                curve[step] = inverseT * inverseT * inverseT * start
                    + 3f * inverseT * inverseT * t * control1
                    + 3f * inverseT * t * t * control2
                    + t * t * t * end;
                if (step > 0)
                    distances[step] = distances[step - 1] + Vector2.Distance(curve[step - 1], curve[step]);
            }

            float headProgress = _wmState switch
            {
                WormBossState.IntroEmerging => Math.Clamp(_wmTimer / WM_INTRO_EMERGE_DURATION, 0f, 1f),
                WormBossState.IntroDiving => 1f - Math.Clamp(_wmTimer / WM_INTRO_DIVE_DURATION, 0f, 1f),
                WormBossState.SurfaceEmerging => Math.Clamp(_wmTimer / WM_SURFACE_EMERGE_DURATION, 0f, 1f),
                WormBossState.Retreat => 1f - Math.Clamp(_wmTimer / WM_RETREAT_DURATION, 0f, 1f),
                _ => 1f
            };
            headProgress = headProgress * headProgress * (3f - 2f * headProgress);
            float headDistance = distances[^1] * headProgress;
            float surfaceDistance = distances[^1];
            for (int i = 1; i < curve.Length; i++)
            {
                if (curve[i].Y > _wmSurfacePoint.Y - 2f)
                    continue;

                float crossing = _wmSurfacePoint.Y - 2f;
                float verticalSpan = curve[i - 1].Y - curve[i].Y;
                float crossingProgress = verticalSpan > 0.001f
                    ? Math.Clamp((curve[i - 1].Y - crossing) / verticalSpan, 0f, 1f)
                    : 0f;
                surfaceDistance = distances[i - 1]
                    + (distances[i] - distances[i - 1]) * crossingProgress;
                break;
            }

            WormIntroSegmentPose[] poses = new WormIntroSegmentPose[WormIntroSegmentCount];
            for (int i = 0; i < poses.Length; i++)
            {
                float segmentDistance = headDistance - i * WM_SEGMENT_SPACING;
                bool visible = segmentDistance >= surfaceDistance && segmentDistance <= distances[^1];
                poses[i] = new WormIntroSegmentPose(
                    SampleWormIntroPath(curve, distances, Math.Max(0f, segmentDistance)),
                    visible);
            }

            return poses;
        }

        private static Vector2 SampleWormIntroPath(Vector2[] curve, float[] distances, float distance)
        {
            if (distance < 0f)
            {
                Vector2 tangent = curve[1] - curve[0];
                if (tangent.LengthSquared() > 0.001f)
                    tangent = Vector2.Normalize(tangent);
                return curve[0] + tangent * distance;
            }

            for (int i = 1; i < distances.Length; i++)
            {
                if (distances[i] < distance)
                    continue;

                float length = distances[i] - distances[i - 1];
                float t = length > 0.001f ? (distance - distances[i - 1]) / length : 0f;
                return Vector2.Lerp(curve[i - 1], curve[i], t);
            }

            return curve[^1];
        }

        // ==================== GULPER BOSS (flotteur aérien) ====================
        // Cycle : Rising (remonte doucement du sol) -> Approach (vole vers la verticale de la
        // cible) -> Telegraph (charge son attaque, immobile en l'air) -> Slam (chute et s'éclate
        // au sol, inflige des dégâts) -> Grounded (étourdi au sol, SEULE fenêtre où il est
        // vulnérable) -> Rising, etc.
        private enum GulperBossState { Rising, Approach, Telegraph, Slam, Grounded }
        private GulperBossState _guState = GulperBossState.Rising;
        // Position logique "au sol" (utilisée pour les déplacements/collisions/dégâts) : la
        // position visuelle réelle (WorldPos) est ensuite dérivée en lui soustrayant _guHeight,
        // exactement comme le fait le ver géant (WormBoss) avec son arc de saut.
        private Vector2 _guGroundPos;
        private float _guHeight = 0f;
        private float _guTimer = 0f;
        private bool _guHasDealtDamage = false;

        private const float GU_HOVER_HEIGHT = 85f;           // hauteur de croisière une fois en vol
        private const float GU_RISE_SPEED = 95f;             // vitesse de remontée (px/s) après un crash (55 -> 95 : boss plus rapide)
        private const float GU_ABOVE_RADIUS = 36f;           // distance horizontale considérée "à la verticale" de la cible
        private const float GU_TELEGRAPH_DURATION = 0.55f;   // temps de charge avant le crash (0.9 -> 0.55)
        private const float GU_SLAM_SPEED = 720f;            // vitesse de chute (px/s) (480 -> 720)
        private const float GU_GROUND_DAMAGE_RADIUS = 55f;   // rayon de dégâts au sol lors du crash
        private const float GU_GROUND_HEIGHT_THRESHOLD = 10f;// en dessous : considéré "au sol" -> vulnérable
        private const float GU_APPROACH_SPEED = 260f;        // vitesse de déplacement en vol vers la cible (150 -> 260)

        public bool IsGulper => IsBoss && string.Equals(Species, "Gulper", StringComparison.OrdinalIgnoreCase);

        // ==================== GENIE BOSS (Génie de la Lampe) ====================
        public bool IsGenie => IsBoss && string.Equals(Species, "Genie", StringComparison.OrdinalIgnoreCase);

        public void EnsureGenieTint()
        {
            if (string.Equals(Species, "Genie", StringComparison.OrdinalIgnoreCase)
                && !IsGenieIllusion
                && !string.Equals(CustomName, "Illusion du Génie", StringComparison.OrdinalIgnoreCase))
                Tint = GenieBlueTint;
        }
        //  Marque les clones/illusions invoqués par le Génie en phase 1 : ils infligent des
        // dégâts réduits, n'ont qu'1 PV (une seule touche les dissipe) et ne comptent jamais
        // comme le "vrai" boss pour la barre de vie (IsBoss reste false sur les clones).
        public bool IsGenieIllusion = false;
        private enum GenieBossState
        {
            Idle, Teleport, ClonesTelegraph, FanTelegraph, FanFiring,
            SandTelegraph, LavaTelegraph,
            LaserTelegraph, LaserSpinning, WishTelegraph, Recover
        }
        private GenieBossState _geState = GenieBossState.Idle;
        private float _geStateTimer = 0f;
        private float _geAttackCooldown = 1.2f;
        private int _gePhase = 1; // 1 = Marchand d'illusions, 2 = Tempête de sable, 3 = Les 3 voeux (enrage)
        private bool _gePhase2Announced = false;
        private bool _gePhase3Announced = false;
        private int _geFanShotsRemaining = 0;
        private float _geFanShotTimer = 0f;
        private List<GenieHazardZone> _geHazardZones = new();
        private float _geLaserAngle = 0f;
        private float _geLaserTimer = 0f;
        private int _geLaserSpins = 0;
        private Vector2 _geWishTargetPos = Vector2.Zero;
        private List<Entity> _geClones = new();
        private float _geCloneCleanupTimer = 0f;
        private const float GE_TELEPORT_MIN_DIST = 130f;
        private const float GE_TELEPORT_MAX_DIST = 240f;
        public float GulperHeight => _guHeight;
        // Vrai uniquement pendant la fenêtre "au sol, étourdi" : c'est le seul moment où on peut le frapper.
        public bool GulperIsGrounded => _guHeight <= GU_GROUND_HEIGHT_THRESHOLD;

        // Certains boss volants (ex: le Gulper) ne peuvent être frappés que lorsqu'ils sont au sol,
        // et il en va de même pour n'importe quelle créature en plein vol (ex : un oiseau) : une
        // arme de corps à corps ne peut pas atteindre une cible en l'air. Propriété générique
        // consultée par le système de combat pour ignorer les coups tant que c'est le cas.
        public bool IsAirborne => (IsGulper && !GulperIsGrounded) || (IsBird && IsBirdFlying);
        public bool IsInvulnerable => IsAirborne || (IsWorm && !WormVulnerable);

        // ==================== SLIME : déplacement en sauts ====================
        private enum SlimeJumpState { Grounded, Charging, Jumping, Landing }
        private SlimeJumpState _slimeJumpState = SlimeJumpState.Grounded;
        private float _slimeChargeTimer = 0f;
        private float _slimeJumpProgress = 0f;
        private Vector2 _slimeJumpStartPos;
        private Vector2 _slimeJumpTargetPos;
        private float _slimeHeight = 0f;
        private float _slimeLandingTimer = 0f;
        private bool _slimeHasDealtDamageThisJump;

        private const float SLIME_ARC_HEIGHT = 60f;
        private const float SLIME_CHARGE_DURATION = 0.35f;   // temps d'élan avant le saut
        private const float SLIME_JUMP_DURATION = 0.55f;     // durée du saut (arc)
        private const float SLIME_LANDING_DURATION = 0.15f;  // pause après atterrissage
        private const float SLIME_MIN_JUMP_DIST = 20f;       // distance minimale pour déclencher un saut
        private const int SLIME_MAX_JUMP_TILES = 3;         // distance maximale du saut en tuiles
        private float SLIME_MAX_JUMP_DIST => SLIME_MAX_JUMP_TILES * Program.TileSize;

        // ==================== OISEAUX : vol optionnel ====================
        //  Les oiseaux marchent normalement au sol (comme n'importe quelle créature), mais
        // peuvent aussi décoller pour se déplacer dans les airs (fuite, envol spontané...).
        // Le rendu reste celui d'un déplacement au sol : on ajoute juste un décalage vertical
        // (_birdFlightHeight, exposé via VisualOffsetY) qui lève le sprite tandis que l'ombre,
        // elle, reste au sol (voir shadowGroundOffset = VisualOffsetY dans DrawEntity).
        private enum BirdFlightState { Grounded, TakingOff, Flying, Landing }
        private enum BirdFlightPattern { Glide, Hop, Zigzag, Circle, VFormation }

        private BirdFlightState _birdFlightState = BirdFlightState.Grounded;
        private BirdFlightPattern _birdFlightPattern = BirdFlightPattern.Glide;
        private float _birdFlightHeight = 0f;
        private float _birdTakeoffTimer = 0f;
        private float _birdLandingTimer = 0f;
        private float _birdFlightDuration = 0f;     // temps restant à voler avant d'envisager d'atterrir
        private float _birdPatternPhase = 0f;       // accumulateur pour zigzag/hop/cercle
        private float _birdTargetFlightHeight = 90f;
        private float _birdRoostDecisionTimer = 4f;
        private (int X, int Y)? _requestedBirdRoost;

        // Vol en cercle autour d'un point (arbre, rocher...) ou d'un autre oiseau
        private Vector2 _birdCircleCenter;
        private float _birdCircleRadius = 80f;
        private float _birdCircleAngle = 0f;
        private float _birdCircleDir = 1f;

        // Vol en V, à la suite d'un meneur (autre oiseau de la même espèce déjà en vol)
        private Entity? _birdFlockLeader = null;
        private Vector2 _birdFlockSlotOffset = Vector2.Zero;
        private float _birdFlockRecheckTimer = 0f;

        //  Durées de décollage/atterrissage effectives pour le vol en cours : mises à l'échelle
        // selon la hauteur visée (voler haut prend logiquement plus de temps à monter/descendre
        // qu'un simple envol à hauteur de toit).
        private float _birdTakeoffDuration = BIRD_TAKEOFF_DURATION;
        private float _birdLandingDuration = BIRD_LANDING_DURATION;

        //  Cap de vol : un oiseau en l'air ne fait jamais de surplace. Tant qu'il plane
        // (Glide/Hop/Zigzag), il dérive en permanence dans cette direction (qui tourne
        // lentement et aléatoirement au fil du temps), que l'IA le fasse par ailleurs avancer
        // (fuite, errance...) ou non (Idle, LookAtPlayer...).
        private Vector2 _birdFlightDir = new Vector2(1f, 0f);
        private const float BIRD_CRUISE_SPEED = 60f;
        private const float BIRD_HEADING_TURN_RATE = 0.5f; // rad/s max, variation aléatoire du cap

        private const float BIRD_TAKEOFF_DURATION = 0.45f;
        private const float BIRD_LANDING_DURATION = 0.35f;
        private const float BIRD_MIN_FLIGHT_HEIGHT = 55f;
        private const float BIRD_LOW_MAX_FLIGHT_HEIGHT = 150f;   // vol "normal", le plus courant
        private const float BIRD_HIGH_MIN_FLIGHT_HEIGHT = 180f;  // vol "haute altitude", plus rare
        private const float BIRD_MAX_FLIGHT_HEIGHT = 480f;       // un oiseau peut vraiment monter très haut s'il le veut
        private const float BIRD_HIGH_ALTITUDE_CHANCE = 0.3f;
        private const float BIRD_MIN_FLIGHT_DURATION = 3.5f;
        private const float BIRD_MAX_FLIGHT_DURATION = 9f;
        private const float BIRD_FLOCK_SEARCH_RADIUS = 220f;

        //  Liste des espèces "oiseau" : marchent par défaut mais peuvent voler. Distinct des
        // créatures "flying" (papillon, libellule, chauve-souris...) qui restent en l'air en
        // permanence et ne sont pas concernées par cette machine à états.
        private static readonly HashSet<string> BirdSpecies = new(StringComparer.OrdinalIgnoreCase)
        {
            "sparrow", "crow", "pigeon", "robin", "seagull"
        };

        public bool IsBird => BirdSpecies.Contains(Species);
        public bool IsBirdFlying => _birdFlightState == BirdFlightState.Flying || _birdFlightState == BirdFlightState.TakingOff;
        public bool TryGetRequestedBirdRoost(out int tileX, out int tileY)
        {
            if (_requestedBirdRoost.HasValue)
            {
                tileX = _requestedBirdRoost.Value.X;
                tileY = _requestedBirdRoost.Value.Y;
                return true;
            }

            tileX = 0;
            tileY = 0;
            return false;
        }

        public float VisualOffsetY => _slimeHeight + _birdFlightHeight; // utilisé pour le rendu

        public bool IsSlime => string.Equals(Species, "slime", StringComparison.OrdinalIgnoreCase);

        private Vector2 _khDashTarget = Vector2.Zero;
        private bool _khHasDealtDamage = false;
        private bool _khHasTeleported = false;
        private const float KH_DASH_TELEGRAPH_DURATION = 0.35f;
        // Augmenter fortement les paramètres du dash pour que Khamsin atteigne sa cible
        private const float KH_DASH_CHARGE_DURATION = 0.5f; // durée du dash (s)
        private const float KH_DASH_RECOVERY_DURATION = 0.25f;
        private const float KH_DASH_BRAKE_DURATION = 0.2f;
        private const float KH_DASH_SPEED = 600f; // vitesse de dash (px/s)

        // Constantes de timing
        private const float TELEGRAPH_DURATION = 0.3f;
        private const float CHARGE_DURATION = 0.4f;
        private const float RECOVERY_DURATION = 0.2f;
        private const float CHARGE_SPEED = 420f;

        // ==================== IA DE COMBAT UNIFIÉE (mêlée + distance) ====================
        //  Un SEUL automate d'attaque pour tous les mobs :
        //      Approche -> Armement (Telegraph) -> Frappe (Charge) -> Récupération (Recovery) -> Approche
        //  Ce qui distingue un gobelin, un loup, un bolet ou un archer n'est plus du code dupliqué
        //  mais un CombatProfile (voir GetCombatProfile) : durées, élan, type d'impact.
        //  Le swing visuel (GetAttackSwingProgress), la frame d'animation du squelette
        //  (SyncAttackAnimation) et l'instant d'impact sont tous dérivés des MÊMES durées :
        //  ils ne peuvent plus se désynchroniser.
        //  Entre deux attaques, les combattants se repositionnent et attendent un nouveau créneau
        //  d'engagement au lieu d'enchaîner leurs coups sans laisser de fenêtre au joueur.
        public const float GOBLIN_ATTACK_REACH = 60f;     // portée de frappe des gobelins (= rayon d'attaque du joueur)
        private const float SWING_IMPACT_PROGRESS = 0.88f; // progression du swing visuel à l'instant de l'impact
        private const float STRIKE_REACH_TOLERANCE = 6f;   // marge de portée à l'impact (frappes de « duelliste »)

        private enum CombatImpactKind { Strike, Spores, Arrow }

        private readonly struct CombatProfile
        {
            public readonly CombatImpactKind Kind;
            public readonly float Windup;         // armement
            public readonly float Strike;         // durée de la phase de frappe (Charge)
            public readonly float ImpactDelay;    // >= 0 : impact unique à cet instant ; < 0 : au premier contact pendant la frappe
            public readonly float Recovery;       // récupération
            public readonly float CooldownMin;    // délai minimal avant de relancer un armement
            public readonly float CooldownJitter; // + variation aléatoire (évite un rythme métronomique)
            public readonly float LungeSpeed;     // 0 = pieds plantés pendant la frappe
            public readonly float Shake;          // secousse d'écran à l'impact sur le joueur
            public readonly bool FrontOnly;       // ne touche que devant lui
            public readonly bool Tracks;          // continue de se rapprocher pendant l'armement s'il est hors de portée

            public CombatProfile(CombatImpactKind kind, float windup, float strike, float impactDelay, float recovery,
                float cooldownMin, float cooldownJitter, float lungeSpeed, bool frontOnly, bool tracks, float shake)
            {
                Kind = kind; Windup = windup; Strike = strike; ImpactDelay = impactDelay; Recovery = recovery;
                CooldownMin = cooldownMin; CooldownJitter = cooldownJitter; LungeSpeed = lungeSpeed;
                FrontOnly = frontOnly; Tracks = tracks; Shake = shake;
            }
        }

        // Gobelins / gardes : frappe précise, pieds plantés, impact unique synchronisé avec le geste.
        private static readonly CombatProfile DuelistProfile = new(CombatImpactKind.Strike,
            windup: 0.26f, strike: 0.08f, impactDelay: 0.08f, recovery: 0.28f,
            cooldownMin: 1.00f, cooldownJitter: 0.80f, lungeSpeed: 0f, frontOnly: true, tracks: true, shake: 3f);

        // Bêtes : armement puis élan en ligne droite, touche au premier contact.
        private static readonly CombatProfile BeastProfile = new(CombatImpactKind.Strike,
            windup: TELEGRAPH_DURATION, strike: CHARGE_DURATION, impactDelay: -1f, recovery: RECOVERY_DURATION,
            cooldownMin: 0.90f, cooldownJitter: 0.90f, lungeSpeed: CHARGE_SPEED, frontOnly: false, tracks: false, shake: 4f);

        // Bolet : immobile, relâche un nuage de spores au début de la phase de frappe.
        private static readonly CombatProfile SporeProfile = new(CombatImpactKind.Spores,
            windup: TELEGRAPH_DURATION, strike: CHARGE_DURATION, impactDelay: 0f, recovery: RECOVERY_DURATION,
            cooldownMin: 0.90f, cooldownJitter: 0.90f, lungeSpeed: 0f, frontOnly: false, tracks: false, shake: 0f);

        // Archers : tir immobile après armement ; 1,0 à 1,6 s entre deux tirs (récupération incluse).
        private static readonly CombatProfile ArcherProfile = new(CombatImpactKind.Arrow,
            windup: RANGED_TELEGRAPH_DURATION, strike: 0f, impactDelay: 0f, recovery: 0.15f,
            cooldownMin: 0.85f, cooldownJitter: 0.6f, lungeSpeed: 0f, frontOnly: false, tracks: false, shake: 0f);

        private sealed class ContextMap
        {
            public const int Slots = 16;
            public static readonly Vector2[] Directions = BuildDirections();

            public readonly float[] Interest = new float[Slots];
            public readonly float[] Danger = new float[Slots];
            public readonly float[] RawDanger = new float[Slots];
            private readonly float[] _smoothedInterest = new float[Slots];

            public float Timer;
            public bool HasResult;
            public Vector2 Direction;
            public float Strength;

            private static Vector2[] BuildDirections()
            {
                var directions = new Vector2[Slots];
                for (int i = 0; i < Slots; i++)
                {
                    float angle = i * (MathF.Tau / Slots);
                    directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }

                return directions;
            }

            public void BeginBuild()
            {
                Array.Clear(Interest, 0, Interest.Length);
                Array.Clear(Danger, 0, Danger.Length);
                Array.Clear(RawDanger, 0, RawDanger.Length);
            }

            public void FinalizeDanger()
            {
                for (int i = 0; i < Slots; i++)
                {
                    float neighbourDanger = MathF.Max(
                        RawDanger[(i + Slots - 1) % Slots],
                        RawDanger[(i + 1) % Slots]);
                    Danger[i] = MathF.Max(RawDanger[i], neighbourDanger * 0.4f);
                }
            }

            public void Reset()
            {
                Array.Clear(_smoothedInterest, 0, _smoothedInterest.Length);
                Timer = 0f;
                HasResult = false;
                Direction = Vector2.Zero;
                Strength = 0f;
            }

            public bool Resolve(float smoothing)
            {
                float minDanger = float.MaxValue;
                for (int i = 0; i < Slots; i++)
                    minDanger = MathF.Min(minDanger, Danger[i]);

                Span<float> maskedInterest = stackalloc float[Slots];
                float bestInterest = 0f;
                int bestIndex = -1;
                for (int i = 0; i < Slots; i++)
                {
                    _smoothedInterest[i] += (Interest[i] - _smoothedInterest[i]) * smoothing;
                    maskedInterest[i] = Danger[i] > minDanger + 0.001f ? 0f : _smoothedInterest[i];
                    if (maskedInterest[i] > bestInterest)
                    {
                        bestInterest = maskedInterest[i];
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || bestInterest < 0.03f)
                {
                    HasResult = false;
                    Strength = 0f;
                    return false;
                }

                float left = maskedInterest[(bestIndex + Slots - 1) % Slots];
                float right = maskedInterest[(bestIndex + 1) % Slots];
                float denominator = left - 2f * bestInterest + right;
                float offset = MathF.Abs(denominator) > 0.0001f
                    ? Math.Clamp(0.5f * (left - right) / denominator, -0.5f, 0.5f)
                    : 0f;
                float angle = (bestIndex + offset) * (MathF.Tau / Slots);

                Direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Strength = Math.Clamp(bestInterest, 0f, 1f);
                HasResult = true;
                return true;
            }
        }

        private const float CONTEXT_UPDATE_INTERVAL = 0.07f;
        private const float CONTEXT_WANDER_INTERVAL = 0.15f;
        private const float CONTEXT_SMOOTHING = 0.55f;
        private const float COMBAT_CONTEXT_RANGE = 260f;
        private const float ORBIT_MIN_EDGE_DISTANCE = 45f;
        private const float ORBIT_APPROACH_BAND = 60f;
        private const float ENGAGE_HESITATION_MIN = 0.5f;
        private const float ENGAGE_HESITATION_MAX = 1.3f;
        private const float ENGAGE_TIMEOUT = 2.5f;
        private const float SEPARATION_RADIUS = 70f;
        private const float WANDER_NOISE_RATE = 0.45f;
        private const float WANDER_MAX_SWAY = 1.6f;
        public static int MaxSimultaneousEngagers = 2;

        private ContextMap? _contextMap;
        private bool _combatActive;
        private bool _isEngaging;
        private float _engageWaitTimer;
        private float _engageTimeout;
        private float _orbitRadiusFactor = 1f;
        private float _strafeFlipTimer;
        private Entity? _engagementTargetEntity;
        private int _engagementPlayerConnectionId = -1;
        private bool _wanderActive;
        private bool _wanderAnchorSet;
        private Vector2 _wanderAnchor;
        private float _wanderBaseHeading;
        private float _wanderNoiseTime;
        private float _wanderNoiseSeed;

        private bool UsesMeanderWander => !IsVillager && !IsTamed && !IsSlime && !IsPlayer
            && !IsPetCompanion && !IsBoss && !IsMounted && !IsRidingMount;

        private int _strafeDir = 1;
        // Après une attaque (ou une charge manquée), on force une phase de repositionnement
        // avant de pouvoir en redéclencher une : évite le "spam" d'attaques dos à dos.
        private float _postAttackCooldown = 0f;

        // ---- Combat à distance (arc) ----
        private const float RANGED_PREFERRED_MIN = 180f;
        private const float RANGED_PREFERRED_MAX = 320f;
        private const float RANGED_ARROW_SPEED = 620f;
        private const float RANGED_TELEGRAPH_DURATION = 0.35f;

        private bool IsGoblinMeleeStyle =>
            string.Equals(Species, "Goblin", StringComparison.OrdinalIgnoreCase) || IsVillageGuard;

        private CombatProfile GetCombatProfile()
        {
            if (HasRangedWeapon) return ArcherProfile;
            if (IsGoblinMeleeStyle) return DuelistProfile;
            if (string.Equals(Species, "bolet", StringComparison.OrdinalIgnoreCase)) return SporeProfile;
            return BeastProfile;
        }

        // Propriétés pour l'UI
        public bool IsChargingAttack => _attackPhase == AttackPhase.Telegraph || _attackPhase == AttackPhase.Charge;
        public float GetChargeProgress()
        {
            if (_attackPhase == AttackPhase.Telegraph)
                return Math.Clamp(_telegraphTimer / Math.Max(0.0001f, GetCombatProfile().Windup), 0f, 1f);
            if (_attackPhase == AttackPhase.Charge)
                return 1f;
            return 0f;
        }

        // Progression UNIQUE du geste d'attaque (0 = repos, 1 = fin du geste), pour toutes les espèces :
        //   Armement  : 0    -> 0.5   (bras levé)
        //   Frappe    : 0.5  -> 0.88  (descente ; 0.88 est atteint EXACTEMENT à l'instant de l'impact
        //                              pour les profils à impact unique)
        //   Récup.    : 0.88 -> 1     (retour au repos)
        public float GetAttackSwingProgress()
        {
            if (_attackPhase == AttackPhase.None) return 0f;
            CombatProfile p = GetCombatProfile();
            switch (_attackPhase)
            {
                case AttackPhase.Telegraph:
                    return Math.Clamp(_telegraphTimer / Math.Max(0.0001f, p.Windup), 0f, 1f) * 0.5f;
                case AttackPhase.Charge:
                    return 0.5f + Math.Clamp(_chargeTimer / Math.Max(0.0001f, p.Strike), 0f, 1f) * (SWING_IMPACT_PROGRESS - 0.5f);
                case AttackPhase.Recovery:
                    return SWING_IMPACT_PROGRESS + Math.Clamp(_recoveryTimer / Math.Max(0.0001f, p.Recovery), 0f, 1f) * (1f - SWING_IMPACT_PROGRESS);
                default:
                    return 0f;
            }
        }

        public void DrawAttackDebugZone()
        {
            if (!IsAlive || IsPlayer || Attack <= 0 || (!_combatActive && Behavior != "hostile" && !IsHostilePet))
                return;

            CombatProfile profile = GetCombatProfile();
            Vector2 origin = World.GetEntityGroundPosition(WorldPos);
            Color outline = new Color(255, 105, 90, 200);

            if (profile.Kind == CombatImpactKind.Arrow)
            {
                Raylib.DrawCircleLines((int)origin.X, (int)origin.Y, RANGED_PREFERRED_MIN, new Color(255, 210, 90, 180));
                Raylib.DrawCircleLines((int)origin.X, (int)origin.Y, RANGED_PREFERRED_MAX, outline);
                return;
            }

            float radius = AttackHitRange + (profile.FrontOnly ? STRIKE_REACH_TOLERANCE : 0f);
            if (radius <= 0f)
                return;

            if (!profile.FrontOnly)
            {
                Raylib.DrawCircleLines((int)origin.X, (int)origin.Y, radius, outline);
                return;
            }

            float centerAngle = Facing >= 0f ? 0f : MathF.PI;
            const float halfAngle = MathF.PI / 2f;
            const int segments = 20;
            Vector2 previous = origin + new Vector2(
                MathF.Cos(centerAngle - halfAngle), MathF.Sin(centerAngle - halfAngle)) * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = centerAngle - halfAngle + MathF.PI * i / segments;
                Vector2 point = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                Raylib.DrawLineEx(previous, point, 2f, outline);
                previous = point;
            }

            Vector2 firstEdge = origin + new Vector2(
                MathF.Cos(centerAngle - halfAngle), MathF.Sin(centerAngle - halfAngle)) * radius;
            Vector2 lastEdge = origin + new Vector2(
                MathF.Cos(centerAngle + halfAngle), MathF.Sin(centerAngle + halfAngle)) * radius;
            Raylib.DrawLineEx(origin, firstEdge, 2f, outline);
            Raylib.DrawLineEx(origin, lastEdge, 2f, outline);
        }

        // Pour compatibilité (si utilisé ailleurs)
        public bool IsWindUp => _attackPhase == AttackPhase.Telegraph || _attackPhase == AttackPhase.Charge;
        public float WindUpTimer => _attackPhase == AttackPhase.Telegraph ? _telegraphTimer : _chargeTimer;
        public float WindUpDuration { get { CombatProfile p = GetCombatProfile(); return p.Windup + p.Strike; } }

        // ==================== AUTRES PROPRIÉTÉS ====================
        public bool IsBoss { get; set; }
        public string? CustomName = null;
        public string RenderSpecies => string.Equals(Species, "Genie", StringComparison.OrdinalIgnoreCase) ? "human" : Species;

    }
}
