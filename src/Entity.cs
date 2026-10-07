// Entity.cs - Début de la classe avec NetId
#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public enum NpcAiState { Idle, WalkToTarget, Wander, Flee, Chase, LookAtPlayer, Follow, GoHome, Sitting,
        GoToChestWithdraw, GoToChestDeposit, GoToMerchant, GoToGroundItem, GoToVillageCenter,
        GoToInfirmary, HealingAtInfirmary }

    public enum ProfessionType
    {
        None,
        Farmer,
        Blacksmith,
        Lumberjack,
        Hunter,
        Alchemist,
        Tailor,
        Miner,
        Cook,
        Guard
    }

    public static class ProfessionData
    {
        // Les stations de métier non liées à un village sont gérées par la recherche de
        // tuiles de station et des réservations dans World. On garde ici la table de
        // correspondance pour les professions qui utilisent des objets spécifiques.
        public static readonly Dictionary<ProfessionType, int[]> StationObjectIds = new()
        {
            // IDs à compléter lors de la définition exacte des stations de fabrication.
        };

        // Catalogue de vente: chaque métier vend des objets cohérents avec sa fonction,
        // sans dépendre d'un filtrage fragile sur les chaînes de caractères du nom.
        public static readonly Dictionary<ProfessionType, HashSet<string>> SaleItemKeys = new()
        {
            [ProfessionType.Farmer] = new(StringComparer.OrdinalIgnoreCase)
            {
                "apple", "bread", "carrot", "wheat", "flour", "mushroom", "berry",
                "wooden_hoe", "scythe", "watering_can", "seeds", "fertilizer"
            },
            [ProfessionType.Blacksmith] = new(StringComparer.OrdinalIgnoreCase)
            {
                "iron_ore", "iron_ingot", "coal", "copper_ore", "bronze", "hammer",
                "wooden_axe", "wooden_pickaxe", "stone_sword", "iron_sword", "iron_axe", "iron_pickaxe"
            },
            [ProfessionType.Lumberjack] = new(StringComparer.OrdinalIgnoreCase)
            {
                "log", "plank", "stick", "rope", "wooden_axe", "wooden_shovel", "scythe", "wooden_pickaxe"
            },
            [ProfessionType.Hunter] = new(StringComparer.OrdinalIgnoreCase)
            {
                "arrow", "bow", "stone_sword", "wooden_sword", "meat", "raw_meat", "hide",
                "trap", "leather"
            },
            [ProfessionType.Alchemist] = new(StringComparer.OrdinalIgnoreCase)
            {
                "potion", "herb", "mushroom", "honey", "glass", "bottle", "glassbottle_small", "milk"
            },
            [ProfessionType.Tailor] = new(StringComparer.OrdinalIgnoreCase)
            {
                "cotton", "cloth", "thread", "leather", "bandage", "fabric", "robe", "hat", "boots"
            },
            [ProfessionType.Miner] = new(StringComparer.OrdinalIgnoreCase)
            {
                "iron_ore", "coal", "copper_ore", "gold_ore", "stone", "rock", "wooden_pickaxe", "hammer", "iron_pickaxe"
            },
            [ProfessionType.Cook] = new(StringComparer.OrdinalIgnoreCase)
            {
                "apple", "bread", "carrot", "meat", "fish", "mushroom", "milk", "butter", "flour", "water_bucket"
            }
        };
    }

    public enum AttackPhase
    {
        None,
        Telegraph,   // Immobile 0.3s
        Charge,      // Lunge 0.4s
        Strike,      // Coup direct sans lancée : la cible est déjà au corps à corps
        Recovery     // Immobile 0.2s
    }

    public enum TamedAnimalMode { Follow, Stay, Roam, Attack }

    public class Entity
    {
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

        public void SetHealthSilently(int currentHP, int maxHP)
        {
            MaxHP = maxHP;
            _currentHP = currentHP;
            _bloodDripTimer = 0f;
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
        private float _khSalvoTimer = 0f;
        private float _khAttackTimer = 0f;
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
        public enum WormBossState { Underground, Telegraph, Emerging, Airborne, Diving, Recover }
        public WormBossState WormState => _wmState;
        private WormBossState _wmState = WormBossState.Underground;
        public const int WM_SEGMENT_COUNT = 30;
        public const float WM_SEGMENT_SPACING = 26f;        // distance en pixels visée entre deux segments (trop proches avant : 14 -> 26)
        public const float WM_HEAD_LEAP_DURATION = 0.9f;    // temps que met UN segment (ex: la tête) à traverser l'arc sortie -> entrée
        private const float WM_TELEGRAPH_DURATION = 0.9f;   // tremblement au sol avant la sortie -> fenetre de fuite
        private const float WM_ARC_HEIGHT = 80f;            // hauteur du "coup de cloche"
        private const float WM_RECOVER_DURATION = 0.4f;     // pause sous terre après un saut, avant de replonger vers la sortie
        private const float WM_MIN_EXIT_DISTANCE = 260f;    // distance minimale du joueur pour choisir un point de sortie
        private const float WM_MAX_EXIT_DISTANCE = 420f;
        private const float WM_DAMAGE_RADIUS = 34f;

        // ==================== SONS DU VER GÉANT ====================
        private Sound? _wormRumbleSound = null;
        private bool _wormRumbleLoaded = false;
        private float _wmTimer = 0f;
        private Vector2 _wmExitPoint;
        private Vector2 _wmEntryPoint;
        private bool _wmEntryPointLocked = false; // true des que la tete commence a sortir -> plus de changement de trajectoire
        private bool _wmHasDealtDamage = false;
        // Chaque segment i suit EXACTEMENT le même arc (sortie -> entrée) que la tête, mais démarre
        // avec un retard de i * _wmSegmentStagger secondes. Un segment n'est visible que pendant son
        // propre trajet (t entre 0 et 1) : il n'apparaît qu'à son tour, et ne disparaît que lorsqu'il a
        // fini SON arc, donc au moment où il touche vraiment le sol (jamais en plein vol).
        private float _wmSegmentStagger = 0f;
        private float _wmTotalFlightDuration = 0f;
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
        public float WormElapsedTime => _wmTimer;
        public float WormSegmentStagger => _wmSegmentStagger;
        public float WormArcHeight => WM_ARC_HEIGHT;
        public bool WormHeadVisible => _wmState == WormBossState.Emerging || _wmState == WormBossState.Airborne || _wmState == WormBossState.Diving;

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
        //  Marque les clones/illusions invoqués par le Génie en phase 1 : ils infligent des
        // dégâts réduits, n'ont qu'1 PV (une seule touche les dissipe) et ne comptent jamais
        // comme le "vrai" boss pour la barre de vie (IsBoss reste false sur les clones).
        public bool IsGenieIllusion = false;
        private enum GenieBossState
        {
            Idle, Teleport, ClonesTelegraph, FanTelegraph, FanFiring,
            SandTelegraph, SandActive, LavaTelegraph, LavaActive,
            LaserTelegraph, LaserSpinning, WishTelegraph, WishStrike, Recover
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
        public bool IsInvulnerable => IsAirborne;

        // ==================== SLIME : déplacement en sauts ====================
        private enum SlimeJumpState { Grounded, Charging, Jumping, Landing }
        private SlimeJumpState _slimeJumpState = SlimeJumpState.Grounded;
        private float _slimeChargeTimer = 0f;
        private float _slimeJumpProgress = 0f;
        private Vector2 _slimeJumpStartPos;
        private Vector2 _slimeJumpTargetPos;
        private float _slimeHeight = 0f;
        private float _slimeLandingTimer = 0f;

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
        private const float KH_DASH_RANGE = 360f;

        // Constantes de timing
        private const float TELEGRAPH_DURATION = 0.3f;
        private const float CHARGE_DURATION = 0.4f;
        private const float RECOVERY_DURATION = 0.2f;
        private const float CHARGE_SPEED = 420f;

        //  Coup direct (Strike) : quand la cible est déjà à portée de contact
        // (AttackHitRange), inutile de faire attendre puis charger/foncer — l'entité
        // tente juste un coup rapide sur place, comme un vrai coup au corps à corps.
        private const float STRIKE_WINDUP = 0.08f;   // très bref temps avant l'impact du coup
        private const float STRIKE_DURATION = 0.18f; // durée totale du coup avant la récupération

        // ==================== IA DE COMBAT (approche / cercle / recul) ====================
        //  Plutôt que de foncer en permanence droit sur la cible, l'entité en Chase alterne
        // entre plusieurs sous-états de déplacement tant qu'elle n'a pas déclenché d'attaque.
        // Cela casse le pattern "charge constante" qui rendait le combat prévisible et
        // désagréable, en s'inspirant de l'approche "poke / reposition / commit" des jeux
        // d'action (Zelda, Dark Souls...).
        private enum CombatMoveState { Approach, Strafe, Retreat }
        private CombatMoveState _combatMoveState = CombatMoveState.Approach;
        private float _combatDecisionTimer = 0f;
        private int _strafeDir = 1;
        private float _combatRangeWobble = 0f;
        private float _combatRangeChangeTimer = 0f;
        // Après une attaque (ou une charge manquée), on force une phase de repositionnement
        // avant de pouvoir en redéclencher une : évite le "spam" d'attaques dos à dos.
        private float _postAttackCooldown = 0f;

        // ---- Combat à distance (arc) ----
        private const float RANGED_PREFERRED_MIN = 180f;
        private const float RANGED_PREFERRED_MAX = 320f;
        private const float RANGED_RETREAT_DIST = 140f;
        private const float RANGED_ARROW_SPEED = 620f;
        private const float RANGED_TELEGRAPH_DURATION = 0.35f;
        private bool _rangedTelegraphing = false;
        private float _rangedTelegraphTimer = 0f;
        private float _rangedShootCooldown = 0f;

        // Propriétés pour l'UI
        public bool IsChargingAttack => _attackPhase == AttackPhase.Telegraph || _attackPhase == AttackPhase.Charge;
        public float GetChargeProgress()
        {
            if (_attackPhase == AttackPhase.Telegraph)
                return Math.Clamp(_telegraphTimer / TELEGRAPH_DURATION, 0f, 1f);
            if (_attackPhase == AttackPhase.Charge)
                return 1f;
            return 0f;
        }

        public float GetAttackSwingProgress()
        {
            bool isGoblinStyle = string.Equals(Species, "Goblin", StringComparison.OrdinalIgnoreCase) || IsVillageGuard;
            return _attackPhase switch
            {
                AttackPhase.Telegraph => isGoblinStyle
                    ? Math.Clamp(_telegraphTimer / TELEGRAPH_DURATION, 0f, 1f) * 0.5f
                    : Math.Clamp(_telegraphTimer / (TELEGRAPH_DURATION + CHARGE_DURATION), 0f, 0.4f),
                AttackPhase.Charge => 0.4f + Math.Clamp(_chargeTimer / CHARGE_DURATION, 0f, 1f) * 0.4f,
                AttackPhase.Recovery => (isGoblinStyle ? 0.5f : 0.8f)
                    + Math.Clamp(_recoveryTimer / RECOVERY_DURATION, 0f, 1f) * (isGoblinStyle ? 0.5f : 0.2f),
                _ => 0f
            };
        }

        // Pour compatibilité (si utilisé ailleurs)
        public bool IsWindUp => _attackPhase == AttackPhase.Telegraph || _attackPhase == AttackPhase.Charge;
        public float WindUpTimer => _attackPhase == AttackPhase.Telegraph ? _telegraphTimer : _chargeTimer;
        public float WindUpDuration => TELEGRAPH_DURATION + CHARGE_DURATION;

        // ==================== AUTRES PROPRIÉTÉS ====================
        public bool IsBoss { get; set; }
        public string? CustomName = null;
        public string RenderSpecies => string.Equals(Species, "Genie", StringComparison.OrdinalIgnoreCase) ? "human" : Species;

        // ==================== SYSTÈME DE QUÊTES ====================
        // Prénom du PNJ (villageois humain). Attribué automatiquement par QuestManager.
        public string? FirstName = null;
        // Quête actuellement proposée/en cours PAR ce PNJ (il est le "donneur").
        // null = ce PNJ n'a rien à proposer pour le moment.
        public Quest? ActiveQuest = null;
        // Nom affiché : prénom si défini, sinon CustomName, sinon rien.
        public string? DisplayName => !string.IsNullOrEmpty(FirstName) ? FirstName : CustomName;
        public bool IsTrader { get; set; } = false;
        public List<int> TraderItems { get; set; } = new List<int>();
        public float Friendship { get; set; } = 0f;
        public string SocialTrait { get; private set; } = "calme";
        public string SocialHabit { get; private set; } = "observer";
        public string SocialDesire { get; private set; } = "quelques instants de repos";
        public string SocialRelation { get; private set; } = "la paix du village";
        public string SocialRainPreference { get; private set; } = "Je préfère rester au sec.";
        public string SocialFavoriteAnimal { get; private set; } = "les chats";
        private readonly List<string> _socialKnownNpcNames = new();
        public bool HasStableVillageHome => IsVillager && HomeBuildingId > 0 && HomeBuildingId != TentHomeBuildingId;
        public bool IsHomeless => IsVillager && (HomeBuildingId < 0 || HomeBuildingId == TentHomeBuildingId);
        public ProfessionType Profession { get; set; } = ProfessionType.None;
        public bool HasProfession => Profession != ProfessionType.None;
        public float Scale { get; set; } = 1.0f;
        public bool IsInterestedInFood { get; set; }
        public int FollowOrder = -1;
        public bool IsBaby { get; set; } = false;
        public float Age { get; set; } = 0f;
        public float GrowthTime { get; set; } = 600f;
        public float? GestationTimer { get; set; } = null;
        //  SYSTÈME DE FAIM (PNJ villageois uniquement) : monte lentement avec le temps, remise
        // à zéro en mangeant (item pris dans l'inventaire, retiré du coffre commun de la
        // maison, ou acheté chez un marchand le jour). Cf. bloc "routine" dans Update() et
        // World.Houses.cs pour la logique de recherche/consommation.
        public float Hunger = 0f;
        public const float HUNGER_MAX = 100f;
        public const float HUNGER_THRESHOLD_SEEK_FOOD = 60f;
        // Valeur sentinelle : le PNJ a une tente comme domicile, mais pas une maison
        // avec bâtiment, coffre ou intérieur géré par World.Houses.
        public const int TentHomeBuildingId = -2;
        public Vector2 HomePosition;
        public Vector2 TentPosition;
        //  Identifiant du bâtiment (maison) correspondant à HomePosition, quand cette
        // position est une case intérieure réservée via World.FindNearestFreeHouseSpot.
        // Permet de libérer la case (World.ReleaseHouseSpot) si le PNJ meurt ou déménage.
        public int HomeBuildingId = -1;
        public Vector2? WorkplacePosition = null;
        public int WorkZoneId = -1;
        private Vector2? _farmerWorkTarget = null;
        public bool IsVillager => Species == "human" && !IsPlayer;
        public bool IsVillageGuard => IsVillager && Profession == ProfessionType.Guard && !IsTamed;
        public Entity? CombatTargetEntity => _combatTargetEntity;
        public Equipment? Equipment { get; set; }
        public ContainerInventoryData Inventory { get; set; } = new(8, 4);
        public float SlimeIncubationSeconds { get; set; }
        public const float SlimeGemDuplicationSeconds = 180f;

        public InventorySlot GetSlimeStorageSlot()
        {
            if (Inventory.Slots.Count == 0)
                Inventory = new ContainerInventoryData(1, 1);
            return Inventory.Slots[0];
        }

        public void UpdateSlimeGemIncubation(float dt)
        {
            if (!IsSlime || !IsTamed || dt <= 0f) return;

            InventorySlot slot = GetSlimeStorageSlot();
            if (slot.IsEmpty || slot.Item == null || !IsSlimeDuplicableGem(slot.Item))
            {
                SlimeIncubationSeconds = 0f;
                return;
            }

            if (!GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var itemData)
                || slot.Count >= itemData.StackSize)
            {
                SlimeIncubationSeconds = 0f;
                return;
            }

            SlimeIncubationSeconds += dt;
            while (SlimeIncubationSeconds >= SlimeGemDuplicationSeconds && slot.Count < itemData.StackSize)
            {
                slot.Count++;
                SlimeIncubationSeconds -= SlimeGemDuplicationSeconds;
            }
        }

        public static bool IsSlimeDuplicableGem(Item item)
        {
            string key = GameData.GetItemKey(GameData.GetItemId(item.Name));
            return key is "gem_purple" or "gem_orange" or "gem_yellow" or "crystal" or "sapphire"
                or "emerald" or "ruby" or "amber" or "topaze" or "onyx" or "opale";
        }

        //  Vrai tant que ce PNJ est en dialogue/commerce avec le JOUEUR (QuestDialogUI / TraderUI).
        // Distinct de IsTalking (qui sert aussi aux conversations PNJ-PNJ) : sert uniquement à
        // figer le déplacement du PNJ pendant qu'il parle au joueur.
        public bool IsInPlayerDialogue { get; set; } = false;
        public bool IsTalking { get; set; } = false;
        public float TalkTimer { get; set; } = 0f;
        public float TalkCooldown { get; set; } = 0f;
        public string? TalkText { get; set; } = null;
        public Entity? CurrentTalkPartner { get; set; } = null;

        public TamedAnimalMode TamedBehavior = TamedAnimalMode.Follow;
        public bool IsTamed = false;
        public string? OwnerName = null;
        //  NetId du PNJ propriétaire quand ce tamed appartient à un PNJ plutôt qu'au joueur
        // (ex: don d'animal via une quête TameAndBring). OwnerName reste un nom d'affichage
        // (utilisé pour l'UI/le tri par ancien code), mais c'est OwnerNpcId qui permet de
        // retrouver précisément l'entité PNJ propriétaire pour le comportement "Follow"
        // (voir Program.UpdateNpcOwnedPetTargets) - un nom d'affichage n'est pas unique et ne
        // suffit pas à identifier quel PNJ suivre en cas d'homonymes.
        public Guid? OwnerNpcId = null;

        //  "Les amis de mes ennemis sont mes ennemis" : vrai tant que ce pet appartient à un
        // PNJ (OwnerNpcId != null, donc pas au joueur) dont le Behavior est "hostile" (ex : le
        // loup de compagnie d'un gobelin). Recalculé chaque frame par
        // Program.UpdateOwnedPetHostility, jamais persisté (dérivé de l'état du propriétaire).
        // Deux effets :
        //   - le joueur peut le frapper comme un monstre normal (voir GetEntitiesInAttackCone),
        //     alors qu'un IsTamed "ami" (apprivoisé par le joueur, ou offert par quête à un PNJ
        //     non hostile) reste protégé ;
        //   - dès que son maître se met à pourchasser/attaquer le joueur, il bascule lui aussi
        //     en TamedAnimalMode.Attack pour riposter.
        public bool IsHostilePet = false;

        public List<Entity> Pets = new List<Entity>(); // Animaux apprivoisés par ce PNJ

        //  MONTURE DE COMPAGNIE (ex : loup de compagnie d'un gobelin) : référence directe vers
        // un des `Pets` de ce PNJ que celui-ci sait chevaucher pour se déplacer plus vite. Posée
        // par AddPet(pet, asMount: true). Lien réciproque : OwnedMount.OwnerNpcId == this.NetId.
        public Entity? OwnedMount = null;
        //  Pendant du champ Entity.OwnerNpcId, mais dans l'autre sens (propriétaire -> monture) :
        // seul ce NetId est persisté en sauvegarde (voir SaveSystem), `OwnedMount` lui-même est
        // une référence runtime résolue après chargement via ResolveOwnedMountReference.
        public Guid? OwnedMountNetId = null;
        //  Vrai tant que ce PNJ chevauche activement OwnedMount. Contrairement au joueur
        // (voir Program.MountedAnimal), c'est le PNJ qui garde la main sur sa propre IA et son
        // pathfinding pendant la monte : c'est la monture qui recopie sa position chaque frame
        // (voir Update, section "MONTURE PILOTÉE PAR UN PNJ").
        public bool IsRidingMount = false;

        public void AddPet(Entity pet, bool asMount = false)
        {
            if (pet == null) return;
            // NOTE : on n'ajoute volontairement PAS `pet` à `this.Pets` ici. `pet` est déjà une
            // entité à part entière, présente dans la liste globale `entities` et dans son chunk
            // (comme n'importe quel animal apprivoisé par le joueur) : l'y ajouter une deuxième
            // fois dans `Pets` le ferait être sauvegardé deux fois (une fois comme entité de
            // chunk normale, une fois nichée sous ce PNJ), et donc restauré en double au
            // chargement (deux objets Entity distincts partageant le même NetId). Le suivi se
            // fait via OwnerNpcId (voir plus bas), pas via cette liste.
            pet.IsTamed = true;
            pet.OwnerName = this.DisplayName ?? this.FirstName ?? "Villageois";
            pet.OwnerNpcId = this.NetId;
            pet.TamedBehavior = TamedAnimalMode.Follow;
            pet.Behavior = "tamed";
            pet.HomePosition = this.HomePosition; // Suit le PNJ chez lui
            // Le suivi effectif (AiTarget mis à jour chaque frame vers la position du PNJ) est
            // géré par Program.UpdateNpcOwnedPetTargets, symétrique de UpdateTamedAnimalTargets
            // qui gère déjà le cas des animaux apprivoisés par le joueur.

            //  Monture : ex. un loup de compagnie qu'un gobelin sait chevaucher. On exige que
            // l'espèce soit réellement Mountable (voir SpeciesData) - sinon `asMount` est ignoré
            // silencieusement, le pet reste un simple compagnon au sol.
            if (asMount && pet.IsMountable)
            {
                OwnedMount = pet;
                OwnedMountNetId = pet.NetId;
            }
        }

        //  Synchronise IsHostilePet et TamedBehavior de CE pet avec l'état actuel de son
        // propriétaire PNJ (`owner`, déjà résolu par l'appelant via OwnerNpcId). À appeler
        // chaque frame pour tous les pets possédés par un PNJ (voir Program.UpdateOwnedPetHostility,
        // symétrique de UpdateNpcOwnedPetTargets qui gère déjà le suivi de position).
        // Ne touche jamais un pet apprivoisé par le joueur (OwnerNpcId == null) : ce cas n'est
        // pas concerné, TamedBehavior y reste entièrement piloté par les ordres du joueur.
        public void SyncHostilityWithOwner(Entity? owner)
        {
            bool ownerIsHostile = owner != null && owner.IsAlive && owner.Behavior == "hostile";
            IsHostilePet = ownerIsHostile;

            if (ownerIsHostile)
            {
                //  Le pet riposte dès que son maître est effectivement en train de pourchasser
                // le joueur (ou vient de le repérer, ShowAlertIcon), pas simplement parce que le
                // maître EST hostile de nature : un gobelin qui n'a pas encore vu le joueur ne
                // doit pas faire charger son loup à l'aveugle.
                bool ownerWantsPlayer = owner!.AiState == NpcAiState.Chase || owner.ShowAlertIcon;
                TamedBehavior = ownerWantsPlayer ? TamedAnimalMode.Attack : TamedAnimalMode.Follow;
            }
            else if (TamedBehavior == TamedAnimalMode.Attack)
            {
                //  Le maître n'est plus hostile (mort, ou pet donné à un PNJ pacifique via une
                // quête) : le pet arrête d'attaquer et redevient un simple compagnon.
                TamedBehavior = TamedAnimalMode.Follow;
            }
        }

        //  À appeler une fois, après chargement d'une sauvegarde (pour chaque entité possédant
        // un OwnedMountNetId mais pas encore de référence OwnedMount résolue), avec la liste
        // complète des entités rechargées - typiquement au même endroit que la résolution
        // existante d'OwnerNpcId (voir Program.UpdateNpcOwnedPetTargets). Symétrique et
        // indépendante de cette dernière : ne pas la déclencher plusieurs fois par entité.
        public void ResolveOwnedMountReference(IEnumerable<Entity> allEntities)
        {
            if (OwnedMount != null || OwnedMountNetId == null) return;
            OwnedMount = allEntities.FirstOrDefault(e => e.NetId == OwnedMountNetId.Value);
        }

        //  COMPAGNON D'ARMURE (ex : petit crabe du set "Crabe") : entité apprivoisée spéciale
        // qui suit le joueur en permanence et attaque automatiquement quiconque l'attaque,
        // tant que le set d'armure correspondant reste intégralement équipé (voir
        // Program.UpdateArmorCompanions). Se comporte comme IsTamed pour l'affichage/le
        // ciblage, mais possède sa propre routine d'IA (UpdatePetCompanionAI), séparée du
        // reste du système Follow/Attack, pour pouvoir cibler une entité arbitraire plutôt
        // que le joueur.
        public bool IsPetCompanion = false;
        public Entity? PetCombatTarget = null;
        private float _petAttackTimer = 0f;

        //  GUILDE : true tant que ce PNJ affiche l'icône "guild_joinable" au-dessus de sa
        // tête (il propose de rejoindre notre guilde). Se déclenche une fois l'amitié au
        // maximum. Redevient false dès qu'il rejoint (ou si l'offre expire).
        public bool WantsToJoinGuild = false;
        //  Vrai une fois qu'il a effectivement rejoint la guilde du joueur. Un membre de
        // guilde se comporte exactement comme un animal apprivoisé (IsTamed + TamedBehavior :
        // Follow/Stay/Roam/Attack, ordres, défense) mais reste distinct pour l'UI/les quêtes.
        public bool IsGuildMember = false;

        //  Regard (pupilles + sourcils) : chaque PNJ regarde sa propre cible (joueur en dialogue,
        // direction de marche, ou regard aléatoire à l'arrêt).
        public Vector2 PupilLeftOffset = Vector2.Zero;
        public Vector2 PupilRightOffset = Vector2.Zero;
        public Vector2 EyebrowLeftOffset = Vector2.Zero;
        public Vector2 EyebrowRightOffset = Vector2.Zero;
        private float _idleLookTimer = 0f;
        private Vector2 _idleLookTarget = Vector2.Zero;
        private static Random _gazeRandom = new Random();

        //  Clignement des yeux : chaque PNJ a son propre minuteur (indépendant de celui du
        // joueur, géré dans Program.cs) pour éviter que tout le monde cligne en même temps.
        public bool IsBlinking { get; private set; } = false;
        private float _blinkTimer = 0f;
        private float _nextBlinkDelay = -1f; // -1 = pas encore initialisé
        private static Random _blinkRandom = new Random();
        private const float BLINK_DURATION = 0.15f;

        private void UpdateBlink(float dt)
        {
            if (_nextBlinkDelay < 0f)
            {
                // Décalage initial aléatoire pour désynchroniser les PNJ dès leur apparition.
                _nextBlinkDelay = 6f + (float)_blinkRandom.NextDouble() * 9f;
                return;
            }

            if (IsBlinking)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f)
                {
                    IsBlinking = false;
                    _nextBlinkDelay = 6f + (float)_blinkRandom.NextDouble() * 9f;
                }
            }
            else
            {
                _nextBlinkDelay -= dt;
                if (_nextBlinkDelay <= 0f)
                {
                    IsBlinking = true;
                    _blinkTimer = BLINK_DURATION;
                }
            }
        }

        //  À appeler quand le dialogue avec le joueur se termine : la vitesse avait été mise à
        // 0 pendant l'interaction (voir Update), il faut la restaurer explicitement car rien
        // d'autre ne le fait tant que l'état IA (AiState) ne change pas de lui-même.
        public void ResumeMovementAfterDialogue()
        {
            Speed = (AiState == NpcAiState.Chase) ? RunSpeed : BaseSpeed;
        }

        // Libère un PNJ de l'état de gel imposé par le dialogue ou la conversation, afin
        // qu'il puisse réellement repartir en WalkToTarget / Follow / circle formation
        // lors de l'invitation à la table de cartes. La priorité est de couper toute
        // relation sociale en cours (conversation PNJ-PNJ ou dialogue joueur-PNJ) au lieu
        // de laisser le PNJ rester "suspendu" dans la routine de discussion ou de suivi.
        public void ReleaseDialogueFreeze()
        {
            IsInPlayerDialogue = false;
            IsTalking = false;

            if (CurrentTalkPartner != null)
            {
                Entity partner = CurrentTalkPartner;
                partner.IsInPlayerDialogue = false;
                partner.IsTalking = false;
                partner.CurrentTalkPartner = null;
                partner.TalkText = null;
                partner.TalkTimer = 0f;
                partner.TalkCooldown = 0f;
                partner.ResumeMovementAfterDialogue();
            }

            CurrentTalkPartner = null;
            TalkText = null;
            TalkTimer = 0f;
            TalkCooldown = 0f;
            ResumeMovementAfterDialogue();
        }

        private void InitializeWormRumbleSound()
        {
            if (_wormRumbleLoaded) return;
            _wormRumbleLoaded = true;

            string soundPath = Path.Combine("assets", "sounds", "worm_rumbling.mp3");
            if (!File.Exists(soundPath)) return;

            _wormRumbleSound = Raylib.LoadSound(soundPath);
        }

        private void PlayWormRumbleSound(float volume = 1f, float pitch = 1f)
        {
            if (_wormRumbleSound == null || !Raylib.IsSoundReady(_wormRumbleSound.Value))
                return;

            float distance = Vector2.Distance(WorldPos, Program.GetPlayerPosition());
            float maxDistance = 1200f;
            float volumeFinal = Math.Clamp(volume * (1f - (distance / maxDistance)), 0.05f, volume);

            Raylib.SetSoundVolume(_wormRumbleSound.Value, volumeFinal);
            Raylib.SetSoundPitch(_wormRumbleSound.Value, pitch);
            Raylib.PlaySound(_wormRumbleSound.Value);
        }

        public bool IsPlayer = false;
        public NpcAiState AiState = NpcAiState.Idle;
        public Vector2 AiTarget;

        private static readonly string[] _npcCharacterTraits =
        {
            "plutôt calme",
            "un peu rêveur",
            "toujours de bonne humeur",
            "très observateur",
            "mélancolique",
            "assez curieux",
            "travailleur",
            "serein"
        };

        private static readonly string[] _npcCharacterHabits =
        {
            "faire le tour du village sans but précis",
            "se tenir au chaud en regardant les autres",
            "raconter des histoires sans fin",
            "ranger ses affaires avec soin",
            "observer les fenêtres et les cheminées",
            "s'arrêter pour écouter les moindres rires",
            "répéter sa routine comme un rituel",
            "prendre le temps d'écouter avant de parler"
        };

        private static readonly string[] _npcCharacterDesires =
        {
            "un peu de paix et de silence",
            "une bonne tasse chaude au coin du feu",
            "des conversations simples sans se presser",
            "une journée sans accroc",
            "voir le village un peu plus vivant",
            "quelque chose de nouveau à raconter",
            "des voisins qui se souviennent de notre nom",
            "juste le temps de se reposer"
        };

        private static readonly string[] _npcCharacterRelations =
        {
            "la bonne humeur du village",
            "les petits gestes des voisins",
            "les gens qui prennent le temps de discuter",
            "les réunions de fin de journée",
            "les histoires qu'on se raconte au détour d'un chemin",
            "les petits signes polis, sans grand discours",
            "les rires tardifs dans les maisons",
            "les gens qui savent écouter sans se presser"
        };

        private static readonly string[] _npcRainPreferences =
        {
            "J'adore la pluie ; elle rend les promenades plus tranquilles.",
            "La pluie me plaît, surtout quand je peux l'écouter depuis un abri.",
            "Je préfère les journées ensoleillées, mais une petite pluie ne me dérange pas.",
            "Je n'aime pas trop la pluie : tout devient froid et boueux.",
            "La pluie me rend mélancolique, même si j'aime bien son odeur après l'averse.",
            "Je trouve les orages impressionnants, tant qu'ils ne durent pas toute la nuit."
        };

        private static readonly string[] _npcFavoriteAnimals =
        {
            "les chats", "les loups", "les renards", "les ratons laveurs",
            "les chevaux", "les ours", "les cochons", "les poules"
        };

        private static readonly string[] _npcIdleTalkLines =
        {
            "Je crois que le village est plus agréable quand il y a du monde autour.",
            "Il y a des jours où le silence me semble plus agréable que les nouvelles.",
            "J'ai déjà vu pire, mais ce matin il y a quelque chose de vraiment paisible dans l'air.",
            "J'aime quand chaque rue semble avoir son petit rythme, comme une chanson.",
            "Les bonnes journées, c'est surtout celles où on ne se dépêche pas trop.",
            "J'ai l'impression que les gens se parlent plus doucement quand le vent tombe.",
            "On finit par connaître les habitudes de chacun, et ça rassure un peu.",
            "Le matin, j'aime observer les gens avant de décider quoi leur dire."
        };

        internal void InitializeSocialProfile()
        {
            if (!string.Equals(Species, "human", StringComparison.OrdinalIgnoreCase) || IsPlayer)
            {
                SocialTrait = "discret";
                SocialHabit = "observer";
                SocialDesire = "un peu de repos";
                SocialRelation = "la paix du village";
                return;
            }

            var rng = new Random(NetId.GetHashCode() ^ 0x5EED);
            SocialTrait = _npcCharacterTraits[rng.Next(_npcCharacterTraits.Length)];
            SocialHabit = _npcCharacterHabits[rng.Next(_npcCharacterHabits.Length)];
            SocialDesire = _npcCharacterDesires[rng.Next(_npcCharacterDesires.Length)];
            SocialRelation = _npcCharacterRelations[rng.Next(_npcCharacterRelations.Length)];
            SocialRainPreference = _npcRainPreferences[rng.Next(_npcRainPreferences.Length)];
            SocialFavoriteAnimal = _npcFavoriteAnimals[rng.Next(_npcFavoriteAnimals.Length)];
        }

        private static string PickSocialVariation(params string[] lines)
        {
            return lines[Random.Shared.Next(lines.Length)];
        }

        private IEnumerable<Entity> GetRelevantSocialContacts()
        {
            return Program.GetEntities()
                .Where(e => !ReferenceEquals(e, this)
                    && e.IsVillager
                    && !string.IsNullOrWhiteSpace(e.DisplayName)
                    && (IsHomeless ? e.IsHomeless : e.HasStableVillageHome))
                .Distinct();
        }

        private string GetSocialKnownNpcName()
        {
            if (_socialKnownNpcNames.Count > 0)
            {
                int index = Random.Shared.Next(_socialKnownNpcNames.Count);
                return _socialKnownNpcNames[index];
            }

            var candidates = GetRelevantSocialContacts()
                .Select(e => e.DisplayName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
                return IsHomeless ? "quelqu'un du camp" : "quelqu'un du village";

            return candidates[Random.Shared.Next(candidates.Count)];
        }

        public void RememberSocialContact(Entity? other)
        {
            if (other == null || ReferenceEquals(this, other) || !other.IsVillager)
                return;

            string? name = other.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (_socialKnownNpcNames.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                return;

            _socialKnownNpcNames.Add(name);
            if (_socialKnownNpcNames.Count > 6)
                _socialKnownNpcNames.RemoveAt(0);
        }

        public void RememberRandomSocialContact()
        {
            var candidates = GetRelevantSocialContacts().ToList();
            if (candidates.Count == 0)
                return;

            RememberSocialContact(candidates[Random.Shared.Next(candidates.Count)]);
        }

        public string GetSocialOpeningLine()
        {
            string knownNpc = GetSocialKnownNpcName();

            if (IsHomeless)
            {
                return PickSocialVariation(
                    $"Je suis plutôt {SocialTrait}. En général, je {SocialHabit}. {SocialRainPreference} Et j'ai un faible pour {SocialFavoriteAnimal}. Ici, je garde aussi un œil sur {knownNpc}.",
                    $"On me dit {SocialTrait}, et c'est vrai que j'ai l'habitude de {SocialHabit}. {SocialRainPreference} Au fait, mon animal préféré, c'est {SocialFavoriteAnimal}.",
                    $"Je suis plutôt {SocialTrait} ; j'essaie de {SocialHabit} même quand les journées sont longues. {SocialRainPreference} J'aime bien {SocialFavoriteAnimal}, ça me rappelle les bons côtés du village.");
            }

            return PickSocialVariation(
                $"Je suis plutôt {SocialTrait}. En général, je {SocialHabit}. {SocialRainPreference} Et j'ai un faible pour {SocialFavoriteAnimal}. Je me souviens bien de {knownNpc}, aussi.",
                $"On me dit {SocialTrait}, et j'ai l'habitude de {SocialHabit}. {SocialRainPreference} Mon animal préféré, c'est {SocialFavoriteAnimal}. Ça fait déjà quelques petites choses à raconter, non ?",
                $"Je ne sais pas si tu l'avais remarqué, mais je suis plutôt {SocialTrait}. J'aime bien {SocialFavoriteAnimal} et les journées où personne ne se presse. {SocialRainPreference}");
        }

        public string GetSocialReply(string topic)
        {
            string knownNpc = GetSocialKnownNpcName();

            if (IsHomeless)
            {
                return topic switch
                {
                    "village" => PickSocialVariation(
                        $"Le village est joli vu d'ici, surtout après la pluie. {SocialRainPreference} {knownNpc} m'en raconte parfois les petites histoires.",
                        $"Je regarde souvent les lumières du village depuis les abris. {SocialRainPreference} Ça me donne l'impression d'en faire partie, même d'un peu loin.",
                        $"Un jour, j'aimerais avoir un coin à moi là-bas. En attendant, je profite des bons moments et je laisse la météo faire son spectacle."),
                    "self" => PickSocialVariation(
                        $"Je suis plutôt {SocialTrait}. J'essaie de {SocialHabit}, même quand je n'ai pas grand-chose à moi.",
                        $"Sans maison stable, on apprend vite ce qui compte vraiment. Moi, j'aime {SocialFavoriteAnimal} et les gens qui prennent le temps d'écouter.",
                        $"Je ne suis pas toujours facile à cerner, mais {knownNpc} sait que je suis quelqu'un qui aime {SocialHabit}."),
                    "others" => PickSocialVariation(
                        $"Les animaux m'aident à oublier les soucis. J'ai un faible pour {SocialFavoriteAnimal} ; ils ont leur caractère, eux aussi.",
                        $"Je me demande parfois quel animal choisirait de rester près de moi. Peut-être {SocialFavoriteAnimal} ? {knownNpc} dit que j'y pense trop.",
                        $"Les gens du camp se souviennent des visages. Moi, je me souviens surtout de ceux qui aiment {SocialFavoriteAnimal}."),
                    "wish" => PickSocialVariation(
                        $"J'ai envie de {SocialDesire}. Ce serait plus facile avec un toit, mais on peut déjà rêver un peu.",
                        $"Si je pouvais choisir, j'essaierais de {SocialHabit}, puis je prendrais le temps de {SocialDesire}.",
                        $"Pour l'instant, mon souhait est simple : {SocialDesire}. Et peut-être revoir {knownNpc} demain."),
                    "leave" => PickSocialVariation(
                        $"Merci d'avoir pris le temps de discuter. {knownNpc} sera content d'apprendre que quelqu'un s'est arrêté me parler.",
                        $"À la prochaine ! Ça fait du bien de parler d'autre chose que de chercher un abri.",
                        $"Je vais garder cette conversation en tête. Reviens quand tu veux, je serai probablement dans le coin."),
                    _ => PickSocialVariation(
                        $"Oh, j'aime bien parler de tout et de rien. Par exemple, mon animal préféré, c'est {SocialFavoriteAnimal}.",
                        $"Tu savais que {knownNpc} et moi, on n'est pas d'accord sur la pluie ? {SocialRainPreference}",
                        $"Je pourrais te raconter des histoires toute la journée, mais je vais commencer par celle de {SocialFavoriteAnimal}.")
                };
            }

            return topic switch
            {
                "village" => PickSocialVariation(
                    $"Le village a une drôle d'ambiance quand il pleut. {SocialRainPreference} Toi, tu préfères quel temps ?",
                    $"J'aime bien observer les rues et les fenêtres. {SocialRainPreference} {knownNpc} prétend que je remarque toujours la météo avant tout le monde.",
                    $"Le village paraît différent à chaque saison. Moi, je peux rester des heures à écouter la pluie ou le vent.",
                    $"Je trouve que la météo donne son humeur au village. {SocialRainPreference}",
                    $"Je me demande ce que {knownNpc} fait les jours de pluie. Moi, je trouve toujours quelque chose à observer."),
                "self" => PickSocialVariation(
                    $"Je suis plutôt {SocialTrait}, même si ça ne se voit pas toujours. J'aime {SocialHabit}.",
                    $"Je crois que mon caractère vient surtout de mes petites habitudes : j'ai l'habitude de {SocialHabit}.",
                    $"Si tu veux tout savoir, j'ai tendance à {SocialHabit}. C'est peut-être pour ça qu'on me trouve {SocialTrait}.",
                    $"J'ai mes moments de calme et mes moments de curiosité. Mais au fond, je reste plutôt {SocialTrait}.",
                    $"Les gens me voient comme quelqu'un de {SocialTrait}. {knownNpc}, lui, dit que je suis surtout quelqu'un qui aime {SocialHabit}."),
                "others" => PickSocialVariation(
                    $"Mon animal préféré, c'est {SocialFavoriteAnimal}. Ils ont une façon bien à eux de comprendre les gens.",
                    $"J'ai toujours eu un faible pour {SocialFavoriteAnimal}. {knownNpc} comprend très bien pourquoi.",
                    $"Si je pouvais passer la journée avec un animal, je choisirais {SocialFavoriteAnimal}. Et toi ?",
                    $"Les animaux ont des caractères aussi différents que les habitants. J'aime particulièrement {SocialFavoriteAnimal}.",
                    $"Je m'arrête souvent pour regarder les animaux. Surtout {SocialFavoriteAnimal} ; je ne m'en lasse pas."),
                "wish" => PickSocialVariation(
                    $"En ce moment, j'ai envie de {SocialDesire}. Rien d'extraordinaire, juste quelque chose qui me ferait du bien.",
                    $"Mon petit rêve du moment ? {SocialDesire}. Et une conversation comme celle-ci, ça aide déjà.",
                    $"J'aimerais {SocialDesire}. Peut-être que je trouverai une bonne occasion d'ici quelques jours.",
                    $"Je souhaite surtout {SocialDesire}. Je sais, ce n'est pas très grandiose, mais c'est sincère.",
                    $"Si j'avais une journée rien qu'à moi, je commencerais par {SocialDesire}. Tu ferais quoi, toi ?"),
                "leave" => PickSocialVariation(
                    $"Merci pour cette conversation. Je raconterai peut-être à {knownNpc} qu'on a parlé.",
                    $"À la prochaine ! Ça m'a fait plaisir de discuter un peu, la journée passe mieux comme ça.",
                    $"Je vais te laisser filer. Reviens quand tu veux, j'aurai sûrement une nouvelle histoire.",
                    $"On devrait refaire ça un de ces jours. Les discussions simples sont souvent les meilleures.",
                    $"Prends soin de toi ! Et si tu croises {knownNpc}, dis-lui que je lui dois encore une histoire."),
                _ => PickSocialVariation(
                    $"Je pourrais te parler de {SocialFavoriteAnimal} pendant des heures. {SocialRainPreference}",
                    $"À propos, {knownNpc} et moi ne sommes pas d'accord sur la pluie. {SocialRainPreference}",
                    $"J'ai toujours une histoire en tête, mais je ne sais jamais par laquelle commencer. Tu veux parler d'animaux ?",
                    $"Les petites choses font les meilleures conversations : une averse, un animal curieux, ou un voisin bavard.",
                    $"Je garde toujours un œil sur les détails. C'est comme ça qu'on finit par avoir quelque chose à raconter.")
            };
        }

        public float GetSocialFriendshipGain(string topic)
        {
            return topic switch
            {
                "village" => 0.03f,
                "self" => 0.05f,
                "others" => 0.04f,
                "wish" => 0.05f,
                "leave" => 0.01f,
                _ => 0.02f
            };
        }

        public string GetRandomIdleTalkLine()
        {
            string[] lines = new[]
            {
                $"Je suis plutôt {SocialTrait}, mais certains jours j'aime juste écouter le village respirer.",
                $"J'essaie de {SocialHabit}, même si ça n'a l'air de rien.",
                $"J'ai envie de {SocialDesire}, sans grande histoire.",
                $"{SocialRainPreference} Je crois que le temps influence l'humeur de tout le monde.",
                $"Je pourrais passer des heures avec {SocialFavoriteAnimal}. Ils sont fascinants.",
                $"Je me demande ce que {GetSocialKnownNpcName()} pense de {SocialFavoriteAnimal}.",
                $"Même une petite averse peut rendre le village plus joli, tu ne trouves pas ?",
                $"On finit par remarquer les petites habitudes des gens. C'est sans doute ce qui fait le charme d'ici.",
                $"Parfois, le mieux c'est de parler de presque rien et de se sentir bien quand même.",
                $"Le bon voisinage, c'est surtout savoir rester simple avec les autres.",
                $"J'aime bien regarder les gens passer ; il y a toujours quelque chose d'intéressant à observer.",
                $"Je ne sais pas si c'est important, mais j'ai l'impression que la journée est meilleure quand on prend le temps d'écouter."
            };

            return lines[Random.Shared.Next(lines.Length)];
        }

        // ==================== ABANDON DE POURSUITE (leash) ====================
        //  Position de l'entité au moment où elle a COMMENCÉ à pourchasser (pas son domicile :
        //  une entité sans HomePosition, comme la plupart des monstres, doit quand même pouvoir
        //  revenir à l'endroit d'où elle est partie plutôt que suivre le joueur à l'infini).
        private Vector2 _chaseStartPos;
        //  Temps total passé en Chase depuis le début de CETTE poursuite (remis à zéro à chaque
        //  nouvelle poursuite, pas juste à chaque frame où AiState == Chase).
        private float _chaseElapsedTime = 0f;
        //  Courte période de grâce après un abandon de poursuite (semée ou trop loin/trop
        //  longtemps) : empêche shouldChase de repasser immédiatement à true si le joueur se
        //  trouve encore par hasard à portée de vision au moment où on lâche l'affaire (sinon
        //  l'entité repartirait en Chase à la frame suivante, annulant l'abandon).
        private float _chaseGiveUpCooldown = 0f;
        //  Rayon (en pixels) au-delà duquel l'entité considère avoir perdu le joueur de vue si
        //  celui-ci s'éloigne du point où la poursuite a commencé, ET que la poursuite dure
        //  depuis plus de CHASE_LEASH_TIME. Ne s'applique qu'à cette combinaison durée+distance :
        //  une poursuite qui s'éloigne beaucoup mais reste courte, ou qui dure longtemps sans
        //  trop s'éloigner (ex : le joueur recule en cercle), n'est pas interrompue.
        private const float CHASE_LEASH_RANGE = 700f;
        private const float CHASE_LEASH_TIME = 10f;
        //  Durée de la période de grâce ci-dessus.
        private const float CHASE_GIVEUP_COOLDOWN_DURATION = 4f;

        // ==================== SYSTÈME DE PRIORITÉS DE L'IA ====================
        //  Chaque "envie" du PNJ (fuir, combattre, manger, rentrer chez soi, discuter...)
        // possède un niveau de priorité fixe. À chaque frame, la logique ne peut changer
        // l'objectif courant (AiState/AiTarget) que si :
        //   1) la nouvelle envie a une priorité STRICTEMENT supérieure à l'objectif en cours
        //      (elle "interrompt" immédiatement, ex : la survie coupe court à tout le reste), OU
        //   2) le verrou de l'objectif courant (_goalLockTimer) est retombé à zéro (l'objectif
        //      en cours a eu le temps de s'exprimer un minimum avant de pouvoir être remplacé
        //      par quelque chose de priorité égale ou inférieure).
        // Cela évite qu'une entité oscille sans arrêt entre deux objectifs concurrents
        // (ex : "aller manger" vs "rentrer se coucher" qui se coupent l'un l'autre à chaque frame).
        public static class NpcPriority
        {
            public const int Medical = 110;    // blessure : rejoindre l'infirmerie et récupérer
            public const int Survival = 100;   // vie en danger (PV critiques) : fuir, quoi qu'il arrive
            public const int Combat = 90;    // pourchasser/combattre une cible hostile repérée
            public const int AlertFreeze = 85;    // vient de repérer une menace, marque un temps d'arrêt
            public const int Play = 80;    // invité à jouer avec le joueur (table de Pouilleux) : priorité
                                            // très haute, ne cède que devant un vrai danger (survie/combat/alerte)
            public const int GoHome = 75;    // nuit tombée, doit rentrer se coucher : priorité haute, passe
                                              // devant une discussion en cours mais cède face à une invitation à jouer
            public const int OwnerCommand = 60;    // ordre du joueur sur un animal apprivoisé
            public const int Food = 50;    // attiré par de la nourriture tenue par le joueur
            public const int Forage = 45;    // ramasser un objet au sol repéré à proximité (doit
                                              // être AU-DESSUS de Routine, qui se réclame en continu :
                                              // sinon Forage ne gagnerait la main que par pur hasard
                                              // de timing sur le verrou, ce qui le rend quasi inopérant)
            public const int Routine = 40;    // routine quotidienne et travail principal du villageois
            public const int Social = 30;     // discussion avec un autre PNJ : priorité basse, réservée
                                              // uniquement aux PNJ qui n'ont rien de plus important à faire
            public const int Wander = 10;    // déambulation libre
            public const int Idle = 0;     // ne rien faire
        }
        private int _currentGoalPriority = NpcPriority.Idle;
        private float _goalLockTimer = 0f;
        // Priorités réellement revendiquées pendant le dernier tick, pour le debug F3.
        private readonly HashSet<int> _debugPlannedPriorities = new();
        private enum RoutineTaskKind { DepositItems, VisitVillageCenter }
        private readonly List<RoutineTaskKind> _routineTaskQueue = new();
        private RoutineTaskKind? _activeRoutineTask;
        // Durée minimale (s) pendant laquelle un objectif nouvellement pris reste protégé
        // d'un remplacement par un objectif de priorité égale ou inférieure.
        private const float DEFAULT_GOAL_LOCK = 0.6f;

        //  Tente de prendre la main sur la décision de l'entité avec l'envie "priority".
        // Renvoie true (et pose le verrou) si c'est autorisé, false sinon — dans ce cas
        // l'appelant ne doit PAS toucher à AiState/AiTarget.
        private bool TryCommitGoal(int priority, float lockDuration = DEFAULT_GOAL_LOCK)
        {
            if (priority < NpcPriority.Combat && IsGoalOnCooldown(priority))
                return false;

            if (priority == _currentGoalPriority)
            {
                _goalLockTimer = Math.Max(_goalLockTimer, lockDuration);
                _debugPlannedPriorities.Add(priority);
                return true;
            }

            if (priority > _currentGoalPriority
                || (_currentGoalPriority == NpcPriority.Idle && AiState == NpcAiState.Idle))
            {
                if (priority > _currentGoalPriority && _activeRoutineTask.HasValue)
                {
                    var interruptedTask = _activeRoutineTask.Value;
                    _activeRoutineTask = null;
                    QueueRoutineTask(interruptedTask);
                }

                _currentGoalPriority = priority;
                _goalLockTimer = lockDuration;
                _debugPlannedPriorities.Add(priority);
                return true;
            }
            return false;
        }

        //  Certains évènements (coup reçu, alerte déclenchée...) doivent forcer la main sans
        // condition : ils choisissent eux-mêmes la priorité à imposer.
        private void ForceGoal(int priority, float lockDuration = DEFAULT_GOAL_LOCK)
        {
            _currentGoalPriority = priority;
            _goalLockTimer = lockDuration;
            _debugPlannedPriorities.Add(priority);
        }

        //  Priorité actuellement "possédée" par ce PNJ (voir NpcPriority). Exposée en lecture
        // seule pour que d'autres PNJ puissent, avant de le solliciter (ex : lui proposer une
        // conversation), vérifier qu'il n'est pas déjà occupé par quelque chose de plus important.
        public int CurrentGoalPriority => _currentGoalPriority;

        //  Point d'entrée public permettant à un système EXTÉRIEUR à l'IA (ex : CartesGameUI,
        // qui fait suivre puis former un cercle avec les PNJ invités à jouer) de revendiquer la
        // main sur AiState/AiTarget avec une priorité donnée, en respectant exactement les mêmes
        // règles d'interruption/verrouillage que le reste de l'IA (voir TryCommitGoal ci-dessus).
        // L'appelant ne doit modifier AiState/AiTarget QUE si cette méthode renvoie true, et doit
        // idéalement l'appeler à CHAQUE frame tant qu'il veut garder la main (le verrou expire
        // sinon et laisse la place à n'importe quelle autre envie).
        public bool TryClaimAiPriority(int priority, float lockDuration = DEFAULT_GOAL_LOCK)
        {
            return TryCommitGoal(priority, lockDuration);
        }

        //  Abandonne proprement l'envie en cours quand elle s'avère irréalisable (chemin
        // introuvable après plusieurs essais, ou chemin trouvé mais plus aucune progression
        // dessus). Repasse le PNJ en Idle avec sa priorité au plus bas, ce qui permet à
        // Decide() de retenter au prochain tick et de choisir autre chose (déambuler,
        // rejoindre un banc, discuter...) au lieu de rester figé sur place avec juste la
        // ligne rouge de debug (AiTarget) sans ligne verte (chemin).
        //  Mémorise brièvement la priorité abandonnée pour éviter que Decide() ne la
        // reprenne instantanément à la frame suivante (thrashing).
        private void ReleaseCurrentGoalLock()
        {
            _currentGoalPriority = NpcPriority.Idle;
            _goalLockTimer = 0f;
            _debugPlannedPriorities.Clear();
        }

        private void QueueRoutineTask(RoutineTaskKind task)
        {
            if (_activeRoutineTask == task || _routineTaskQueue.Contains(task))
                return;

            int insertAt = task == RoutineTaskKind.DepositItems ? 0 : _routineTaskQueue.Count;
            _routineTaskQueue.Insert(insertAt, task);
            if (task == RoutineTaskKind.DepositItems)
            {
                _chestBlockedLogged = false;
                Console.WriteLine($"[NPC-CHEST] QUEUE npc={NetId} home={HomeBuildingId} state={AiState} pos={WorldPos} inventory={GetChestInventoryLog()}");
            }
        }

        public void NotifyItemPickedUp()
        {
            if (IsPlayer || IsTamed || Species != "human" || HomeBuildingId < 0)
                return;

            if (_chestDepositRetryCooldown > 0f)
                return;

            _hasDepositedTonight = false;
            QueueRoutineTask(RoutineTaskKind.DepositItems);
        }

        private bool TryStartQueuedRoutineTask()
        {
            if (_activeRoutineTask.HasValue || _routineTaskQueue.Count == 0)
                return false;

            if (AiState != NpcAiState.Idle && AiState != NpcAiState.Wander)
            {
                if (_routineTaskQueue[0] == RoutineTaskKind.DepositItems && !_chestBlockedLogged)
                {
                    _chestBlockedLogged = true;
                    Console.WriteLine($"[NPC-CHEST] BLOCKED npc={NetId} reason=state state={AiState} home={HomeBuildingId} pos={WorldPos}");
                }
                return false;
            }

            var task = _routineTaskQueue[0];
            if (task == RoutineTaskKind.VisitVillageCenter && _villageCenterVisitCooldown > 0f)
                return false;

            _routineTaskQueue.RemoveAt(0);

            if (!TryCommitGoal(NpcPriority.Routine))
            {
                _routineTaskQueue.Insert(0, task);
                return false;
            }

            if (task == RoutineTaskKind.DepositItems)
            {
                if (HomeBuildingId < 0 || !World.HasAnyDepositableItem(this))
                {
                    _hasDepositedTonight = true;
                    return false;
                }

                var chestApproachPos = World.GetHouseChestApproachPosition(HomeBuildingId, this);
                if (!chestApproachPos.HasValue)
                {
                    Console.WriteLine($"[NPC-CHEST] NO_APPROACH npc={NetId} home={HomeBuildingId} pos={WorldPos} houseKnown={World.Houses.ContainsKey(HomeBuildingId)}");
                    QueueRoutineTask(task);
                    ReleaseCurrentGoalLock();
                    return false;
                }

                PrepareHatForNightDeposit();
                _activeRoutineTask = task;
                AiState = NpcAiState.GoToChestDeposit;
                AiTarget = chestApproachPos.Value;
                _currentPath.Clear();
                _pathRecalcTimer = 0f;
                _chestArrivalLogged = false;
                _chestBlockedLogged = false;
                _chestDepositFullRetryCount = 0;
                _chestDepositStuckRetryCount = 0;
                AnimState = "walk";
                Speed = BaseSpeed;
                Console.WriteLine($"[NPC-CHEST] START npc={NetId} home={HomeBuildingId} pos={WorldPos} target={AiTarget} inventory={GetChestInventoryLog()}");
                return true;
            }

            var villageCenter = World.FindNearestVillageCenter(WorldPos, 1600f);
            if (villageCenter == null)
                return false;

            _activeRoutineTask = task;
            _targetVillageCenter = villageCenter;
            AiState = NpcAiState.GoToVillageCenter;
            AiTarget = PickVillageCenterActivityTarget(villageCenter);
            _villageCenterVisitCooldown = RandomRange(100f, 160f);
            _currentPath.Clear();
            AnimState = "walk";
            Speed = BaseSpeed;
            return true;
        }

        private void PrepareHatForNightDeposit()
        {
            if (Program.GetDarknessAlpha() <= 0.6f || _nightStoredHatName != null)
                return;

            var hat = Equipment?.GetZoneItem(BodyZone.TopOfHead);
            if (hat == null || !World.ContainerHasSpaceForItem(Inventory, hat.Name))
                return;

            var removedHat = Equipment.UnequipFromBody(hat);
            if (removedHat == null)
                return;

            int stored = World.StackItemIntoContainer(Inventory, removedHat, 1);
            if (stored > 0)
                _nightStoredHatName = removedHat.Name;
            else
                Equipment.EquipOnBody(removedHat, out _);
        }

        private void RestoreHatAfterNight()
        {
            if (string.IsNullOrEmpty(_nightStoredHatName))
                return;

            var hat = World.TakeItemFromHouseChest(HomeBuildingId, _nightStoredHatName);
            if (hat != null && Equipment.EquipOnBody(hat, out var displaced))
            {
                foreach (var item in displaced)
                    World.StackItemIntoContainer(Inventory, item, item.Count);
                _nightStoredHatName = null;
            }
        }

        private void CompleteRoutineTask(RoutineTaskKind task)
        {
            if (_activeRoutineTask == task)
                _activeRoutineTask = null;
        }

        private void AbandonCurrentGoal(string debugReason)
        {
            if (_infirmaryBuildingId >= 0)
            {
                World.ReleaseInfirmary(_infirmaryBuildingId, NetId);
                _infirmaryBuildingId = -1;
                _infirmaryHealAccumulator = 0f;
                _infirmaryRetryTimer = 4f;
            }

            if (_activeRoutineTask.HasValue)
            {
                var abandonedTask = _activeRoutineTask.Value;
                _activeRoutineTask = null;
                if (abandonedTask == RoutineTaskKind.DepositItems)
                {
                    _chestDepositRetryCooldown = CHEST_DEPOSIT_RETRY_COOLDOWN;
                    Console.WriteLine($"[NPC-CHEST] GIVE_UP_PATH npc={NetId} home={HomeBuildingId} reason={debugReason} retryIn={CHEST_DEPOSIT_RETRY_COOLDOWN:F0}s inventory={GetChestInventoryLog()}");
                }
                else
                {
                    QueueRoutineTask(abandonedTask);
                }
            }

            if (_walkingToBench)
            {
                World.ReleaseBench(_reservedBench);
                _reservedBench = null;
                _walkingToBench = false;
            }

            // Une tente n'est pas un bâtiment pathfindable : si son point de repos est
            // momentanément inaccessible, le PNJ dort sur place au lieu de relancer
            // GoHome à chaque frame.
            if (_currentGoalPriority == NpcPriority.GoHome && HomeBuildingId == TentHomeBuildingId)
            {
                HomePosition = WorldPos;
                AiTarget = WorldPos;
                AiState = NpcAiState.Idle;
                AnimState = "idle";
                _currentPath.Clear();
                _pathFailStreak = 0;
                _pathRecalcTimer = 0f;
                _sleepTimer = 0f;
                _currentGoalPriority = NpcPriority.Idle;
                _goalLockTimer = 0f;
                return;
            }

            _lastAbandonedPriority = _currentGoalPriority;
            _abandonedGoalCooldown = ABANDON_GOAL_COOLDOWN;

            _currentGoalPriority = NpcPriority.Idle;
            _goalLockTimer = 0f;
            AiState = NpcAiState.Idle;
            AnimState = "idle";
            AiTarget = WorldPos;
            _currentPath.Clear();
            _pathFailStreak = 0;
            _pathRecalcTimer = 0f;
            _stuckProgressTimer = 0f;
            AiTimer = 0f; // permet à Decide() de reprendre la main dès la prochaine frame

#if DEBUG_NPC_AI
            Console.WriteLine($"[NPC AI] {DisplayName ?? Species} abandonne son objectif ({debugReason})");
#endif
        }

        //  Une envie de priorité "priority" tout juste abandonnée reste bloquée pendant
        // ABANDON_GOAL_COOLDOWN secondes : à utiliser dans les conditions de Decide() aux
        // côtés de TryCommitGoal pour ne pas re-choisir immédiatement une cible qu'on vient
        // de juger inaccessible.
        private bool IsGoalOnCooldown(int priority)
        {
            return _abandonedGoalCooldown > 0f && priority == _lastAbandonedPriority;
        }

        private Vector2 GetFearTargetPosition()
        {
            if (_fearTargetIsPlayer)
            {
                if (_fearTargetConnectionId >= 0
                    && NetworkManager.TryGetConnectedPlayerPosition(_fearTargetConnectionId, out var remotePlayerPosition))
                    return remotePlayerPosition;
                return _fearTargetConnectionId < 0 ? Program.GetPlayerPosition() : _fearTargetPosition;
            }
            if (_fearTargetEntity != null && _fearTargetEntity.IsAlive)
                return _fearTargetEntity.WorldPos;
            return _fearTargetPosition;
        }

        //  Variante inconditionnelle de TryClaimAiPriority : impose la priorité et rafraîchit le
        // verrou à CHAQUE appel, même si cette même priorité était déjà détenue. À utiliser quand
        // l'appelant doit absolument garantir que AiState/AiTarget seront appliqués cette frame
        // (sinon le PNJ reste figé sur son ancienne cible jusqu'à expiration du verrou précédent),
        // tout en empêchant simultanément toute envie de priorité inférieure de reprendre la main
        // pendant les "trous" d'expiration du verrou. Contrairement à TryClaimAiPriority, ne
        // renvoie rien à vérifier : l'appelant peut toujours modifier AiState/AiTarget juste après.
        public void ForceAiPriority(int priority, float lockDuration = DEFAULT_GOAL_LOCK)
        {
            ForceGoal(priority, lockDuration);
        }
        //  MULTIJOUEUR : identifiant de connexion du joueur actuellement ciblé par cette entité
        // (-1 = joueur local du host). Positionné par la boucle de simulation (host) avant
        // chaque appel à Update(), pour que les dégâts de mêlée soient envoyés au bon joueur.
        public int AiTargetConnectionId = -1;
        //  MULTIJOUEUR : -1 si personne ne la porte, sinon l'id de connexion du client qui la
        // porte (le host se sert toujours de _carriedCreature/entities.Remove pour SON propre
        // portage local). Tant que porté par un client, l'entité reste dans "entities" (donc
        // toujours diffusée aux autres joueurs) mais son IA est suspendue et sa position suit
        // celle du porteur.
        public int CarriedByConnectionId = -1;
        public float AiTimer = 0f;
        public bool IsAlerted = false;
        public float AlertTimer = 0f;
        public Vector2 AlertOrigin;

        //  SIGNAL D'ALERTE ("effet d'écho") : quand une entité hostile repère sa cible pour la
        // première fois (par vue directe, pas par écho), elle affiche brièvement l'icône
        // exclamation.png au-dessus de sa tête et programme l'émission d'un signal qui, après un
        // petit délai aléatoire, va réveiller les entités hostiles alentour (rayon
        // ALERT_SIGNAL_RANGE_TILES) même si elles n'ont pas encore vu la cible elles-mêmes.
        // Chaque entité qui reçoit le signal le retransmet à son tour après son propre délai
        // aléatoire, ce qui propage l'alerte de proche en proche ("écho") plutôt que de réveiller
        // instantanément toute la zone d'un coup.
        public bool ShowAlertIcon = false;
        public float AlertIconTimer = 0f;
        private float _pendingSignalDelay = -1f;   // -1 = aucune émission programmée
        private Vector2 _pendingSignalTarget;
        private int _pendingSignalTargetConnectionId = -1;
        public bool HasPropagatedSignal = false;   // évite qu'une même entité ne relaie le signal plusieurs fois
        public const float ALERT_SIGNAL_RANGE_TILES = 8f;
        private const float ALERT_ICON_DURATION = 0.35f;

        // Assise sur un banc (villageois)
        private (int x, int y, int index)? _reservedBench = null;
        //  SYSTÈME DE FAIM : marchand ciblé pendant un trajet NpcAiState.GoToMerchant (résolu à la
        // prise de décision, revalidé à l'arrivée au cas où il serait mort/parti entre-temps).
        private Guid _targetMerchantNetId = Guid.Empty;
        //  SYSTÈME DE RAMASSAGE : objet au sol ciblé pendant un trajet NpcAiState.GoToGroundItem
        // (revalidé à l'arrivée : l'objet a pu être ramassé par quelqu'un d'autre entre-temps).
        private Guid _targetGroundItemNetId = Guid.Empty;
        private VillageCenter? _targetVillageCenter;
        private float _villageCenterVisitCooldown;

        //  Rythme de travail : un villageois qui a un métier travaille par "services" puis fait
        // une pause (centre du village, banc, balade...) avant de retourner à son poste.
        //   _workShiftTimer     : temps de travail restant avant la prochaine pause
        //   _workBreakCooldown  : > 0 = en pause (la routine de métier est suspendue)
        private float _workShiftTimer = 120f;
        private float _workBreakCooldown = 0f;
        private float _workplaceRetryCooldown = 0f;
        private Vector2? _workApproachPos = null;
        //  Throttle du scan des objets au sol (pas besoin de le refaire à chaque frame).
        private float _forageScanTimer = 0f;
        //  Ramassage PASSIF (indépendant du système de priorités) : throttle à part, vérifié
        // systématiquement à chaque frame (au pire toutes les 0.25s), quel que soit l'AiState en
        // cours. Sert de filet de sécurité garanti : même si le trajet actif de ramassage
        // (GoToGroundItem) ne se déclenche pas, un objet suffisamment proche est quand même
        // récupéré au passage.
        private float _passivePickupScanTimer = 0f;
        //  Empêche de redéposer l'inventaire au coffre à chaque frame tant que le PNJ reste chez lui ;
        // réarmé dès qu'il repart (distToHome >= 25f).
        private bool _hasDepositedTonight = false;
        private bool _chestArrivalLogged = false;
        private bool _chestBlockedLogged = false;
        private string? _nightStoredHatName;
        //  Nombre de tentatives consécutives où GetHouseChestApproachPosition n'a renvoyé
        // aucune case praticable, avant de renoncer pour la nuit (voir UpdateSleepState).
        private int _chestApproachRetryCount = 0;
        private const int CHEST_APPROACH_MAX_RETRIES = 5;
        //  Nombre de tentatives consécutives d'arrivée au coffre où DepositExcessItemsToChest
        // n'a pu déposer AUCUN item (coffre plein) alors qu'il en reste dans l'inventaire.
        // Sans plafond, GetGoalArrivalConfig(GoToChestDeposit) renvoyait Retry indéfiniment :
        // le PNJ restait planté devant un coffre plein pour toujours (bug "bloqué à l'infini").
        private int _chestDepositFullRetryCount = 0;
        private const int CHEST_DEPOSIT_FULL_MAX_RETRIES = 3;
        //  Temporise le renvoi vers un coffre déjà plein. IMPORTANT : on ne pouvait pas se
        // contenter de mettre _hasDepositedTonight à true en cas d'abandon "coffre plein",
        // car World.HasAnyDepositableItem(this) reste vrai (les objets sont toujours sur le
        // PNJ) et le correctif "un item peut être ajouté après coup" (voir plus bas,
        // "_hasDepositedTonight && HasAnyDepositableItem") remettait alors IMMÉDIATEMENT le
        // flag à false au tick suivant, ce qui relançait aussitôt GoToChestDeposit vers le
        // même coffre toujours plein : boucle infinie de trajets identiques au lieu d'un
        // simple blocage sur place. Ce cooldown, lui, n'est jamais court-circuité par la
        // présence d'objets dans l'inventaire : il force une vraie pause avant de retenter.
        private float _chestDepositRetryCooldown = 0f;
        private const float CHEST_DEPOSIT_RETRY_COOLDOWN = 45f;
        //  Nombre de fois consécutives où la détection "aucune progression" (voir
        // STUCK_PROGRESS_*) s'est déclenchée pendant un GoToChestDeposit. Avant, ce cas se
        // contentait de vider _currentPath et de retenter indéfiniment sans jamais abandonner :
        // un PNJ physiquement coincé près du point d'approche (bousculé par d'autres PNJ,
        // obstacle dynamique) restait donc bloqué pour toujours au lieu de finir par renoncer.
        private int _chestDepositStuckRetryCount = 0;
        private const int CHEST_DEPOSIT_STUCK_MAX_RETRIES = 3;
        private Vector2 _benchSeatVisualPos;
        private bool _walkingToBench = false;
        private float _sitTimer = 0f;
        private bool _morningDeparturePending = false;
        private float _tentRestCooldown = 0f;
        private float _tentSleepTimer = 0f;
        private const float CAVE_TENT_SLEEP_DURATION = 30f;
        private bool IsCaveTentResident => World.IsUnderground && HomeBuildingId == TentHomeBuildingId;

        public int PreferredFoodId = 0;

        // Knockback
        public Vector2 KnockbackVelocity = Vector2.Zero;
        public bool IsKnockedBack = false;

        // Monture
        public bool IsMounted { get; set; } = false;
        public Entity? Rider { get; set; } = null;
        public bool IsMountable
        {
            get
            {
                var info = SpeciesData.GetSpeciesInfo(Species);
                if (info != null) return info.Mountable;
                return Species == "Pig" || Species == "Horse" || Species == "Bear" || Species == "Wolf";
            }
        }

        public void Mount(Entity rider)
        {
            IsMounted = true;
            Rider = rider;
            Behavior = "mounted";
            AiState = NpcAiState.Idle;
        }

        public void Dismount()
        {
            IsMounted = false;
            Rider = null;
            Behavior = "tamed";
            Decide();
        }

        //  MONTURE PILOTÉE PAR UN PNJ : distance à partir de laquelle un PNJ possédant une
        // monture (OwnedMount) juge le trajet assez long pour valoir la peine de monter dessus
        // plutôt que d'y aller à pied.
        private const float MOUNT_TRAVEL_DISTANCE = 220f;
        //  La monture doit être raisonnablement proche du PNJ pour être montée sans
        // téléportation visible ; au-delà, il part à pied et le loup le rejoindra en Follow.
        private const float MOUNT_MAX_DISTANCE_TO_RIDER = 260f;

        //  Si ce PNJ possède une monture libre, vivante, non montée et Mountable, et que la
        // cible `target` est assez loin, il grimpe dessus pour parcourir la distance plus vite.
        // Retourne true si la monte a bien eu lieu. À appeler au moment où l'IA décide de partir
        // vers une destination lointaine (ex : entrée en Chase).
        private bool TryMountOwnedPet(Vector2 target)
        {
            if (IsRidingMount || OwnedMount == null || !OwnedMount.IsAlive) return false;
            if (OwnedMount.IsMounted || !OwnedMount.IsMountable) return false;
            if (Vector2.DistanceSquared(WorldPos, target) < MOUNT_TRAVEL_DISTANCE * MOUNT_TRAVEL_DISTANCE) return false;
            if (Vector2.DistanceSquared(WorldPos, OwnedMount.WorldPos) > MOUNT_MAX_DISTANCE_TO_RIDER * MOUNT_MAX_DISTANCE_TO_RIDER) return false;

            OwnedMount.Mount(this);
            IsRidingMount = true;
            // Le PNJ hérite de la vitesse de course de sa monture (typiquement plus rapide que
            // la sienne) ; voir aussi le boost appliqué dans MoveTowardTarget.
            Speed = OwnedMount.RunSpeed > 0 ? OwnedMount.RunSpeed : Speed;
            return true;
        }

        //  Met pied à terre : appelé dès que le combat rapproché commence, que la cible est
        // atteinte/perdue, ou que la monture meurt/disparaît sous le PNJ.
        public void DismountOwnedMount()
        {
            if (!IsRidingMount) return;
            OwnedMount?.Dismount();
            IsRidingMount = false;
            Speed = SpeciesData.GetSpeed(Species);
        }

        // Pathfinding
        private List<Vector2> _currentPath = new List<Vector2>();
        private float _pathRecalcTimer = 0f;
        private const float PATH_RECALC_INTERVAL = 0.5f;

        // ==================== DÉTECTION DE BLOCAGE (PNJ figé) ====================
        //  Deux façons différentes pour un PNJ de rester "coincé" avec une ligne rouge de
        // debug (AiTarget) mais sans ligne verte (aucun chemin trouvé ou plus aucune
        // progression) :
        //   1) Pathfinder.FindPath échoue plusieurs fois de suite pour la même envie
        //      (cible inaccessible, entourée, derrière un mur sans porte, etc.)
        //   2) un chemin EST trouvé, mais le PNJ ne progresse plus dessus (poussé par un
        //      autre PNJ, obstacle dynamique apparu entre-temps, cas limite du mouvement...)
        //  Dans les deux cas, on abandonne l'envie en cours au bout d'un délai/nombre
        // d'essais raisonnable plutôt que de laisser le PNJ retenter indéfiniment sur
        // place : voir AbandonCurrentGoal().
        private int _pathFailStreak = 0;
        private const int MAX_PATH_FAIL_STREAK = 3;

        private Vector2 _stuckProgressCheckPos;
        private float _stuckProgressTimer = 0f;
        private const float STUCK_PROGRESS_CHECK_INTERVAL = 1.5f;
        private const float STUCK_PROGRESS_MIN_DISTANCE = 12f; // doit avancer d'au moins ça en un intervalle

        //  Empêche de reprendre immédiatement la même envie tout juste abandonnée (sinon
        // Decide() la re-choisit à la frame suivante et on boucle en pas de porte).
        private int _lastAbandonedPriority = NpcPriority.Idle;
        private float _abandonedGoalCooldown = 0f;
        private const float ABANDON_GOAL_COOLDOWN = 4f;

        private static int ComputeDynamicPathMargin(Vector2 source, Vector2 target)
        {
            float distance = Vector2.Distance(source, target);
            int tiles = (int)Math.Ceiling(distance / Program.TileSize);
            int margin = 4 + tiles / 8;
            return Math.Clamp(margin, 4, 20);
        }

        private void ResetPathfinding()
        {
            _currentPath.Clear();
            _pathRecalcTimer = 0f;
        }

        // Comportement des insectes (à conserver si vous en avez)
        private Vector2 _dragonflyTarget = Vector2.Zero;
        private float _dragonflyDashProgress = 1f;
        private Vector2 _dragonflyStartPos = Vector2.Zero;
        private float _dragonflyDashDuration = 0.6f;
        private float _dragonflyDashTimer = 0f;
        private const float DRAGONFLY_MIN_DISTANCE = 3f;

        // Autres timers
        private float _fearCooldown = 0f;
        private float _lookAtPlayerTimer = 0f;
        private float _continuousFearTimer = 0f;
        private Vector2 _lastKnownPlayerPos;
        private float _sleepTimer = 0f;
        private float _sleepWakeCooldown = 0f;
        private bool _isSleeping = false;
        private Vector2 _sleepVisualPosition = Vector2.Zero;
        //  Fallback nocturne si aucune maison ne propose de lit/chaise : le PNJ doit quand même
        // avoir une pose de repos valide au sol (assis ou endormi), au lieu de rester figé debout
        // comme un simple arrêt d'animation. Voir GetSleepVisualPosition.
        private bool _groundResting = false;
        //  Repos nocturne sans lit disponible : le PNJ s'assoit sur une chaise/banc libre de sa
        // maison plutôt que de "dormir" debout sur place (ce qui, sans animation dédiée, ressemble
        // à une simple pause figée). Voir GetSleepVisualPosition. La chaise réservée pour l'occasion
        // est mémorisée dans _reservedBench (même registre que l'assise sociale de journée) afin de
        // la libérer proprement au réveil.
        private bool _restingInChair = false;
        private const float SLEEP_TRANSITION_DELAY = 3f;
        private float _zzzTimer = 0f;
        private const float ZZZ_INTERVAL = 0.8f;
        private float _zzzCurrentInterval = 0.8f;

        private Vector2 _lastPosForParticles;
        private float _stepParticleCooldown = 0f;
        private const float STEP_PARTICLE_INTERVAL = 0.2f;
        private float _homeSearchTimer = 0f;
        private float _poopDropTimer = 0f;
        private float _eggDropTimer = 0f;
        private const float POOP_DROP_INTERVAL = 6f;
        private const float EGG_DROP_INTERVAL = 6f;
        private const float POOP_DROP_CHANCE = 0.02f;
        private const float EGG_DROP_CHANCE = 0.02f;

        private float _idleSoundCooldown = 0f;
        private List<Sound> _idleSounds = new();
        private bool _idleSoundLoaded = false;
        private string _idleSoundSlug = "";
        private List<Sound> _hurtSounds = new();
        private bool _hurtSoundLoaded = false;
        private List<Sound> _attackSounds = new();
        private bool _attackSoundLoaded = false;

        // ==================== CONSTRUCTEUR ====================
        //  `skipHomeAutoAssign` : à utiliser quand l'appelant s'apprête à réassigner lui-même
        // HomeBuildingId/HomePosition juste après la construction (cas du chargement d'une
        // sauvegarde, voir SaveSystem.RestoreEntityFromSave). Sans ce paramètre, CHAQUE PNJ humain
        // rechargé déclenchait ici une recherche + RÉSERVATION d'une case de lit dans la maison la
        // plus proche de sa position (HomePosition valant Vector2.Zero par défaut à la construction,
        // avant que le vrai HomeBuildingId/HomePosition sauvegardés ne soient réappliqués par
        // l'appelant) : s'il faisait nuit, le PNJ partait même immédiatement en GoHome vers cette
        // maison temporaire. Le vrai HomeBuildingId/HomePosition écrasait bien ces valeurs juste
        // après, MAIS ni AiState/AiTarget (qui gardaient l'ordre de marche vers la mauvaise maison)
        // ni la case fraîchement réservée (jamais libérée, puisque plus personne n'y fait
        // référence) n'étaient nettoyés. Au chargement d'une sauvegarde faite de nuit, ça se
        // traduisait par : tous les villageois humains partent d'abord vers la maison la plus
        // proche du point où ils ont été recréés (souvent la même, si plusieurs PNJ sont proches
        // les uns des autres), puis, une fois Decide()/le système de priorité repris la main avec
        // le VRAI HomeBuildingId, repartent vers leur vraie maison — en plus de "geler" une case de
        // lit à chaque rechargement, jamais relâchée.
        public Entity(Vector2 pos, string species, bool isPlayer = false, bool skipHomeAutoAssign = false)
        {
            WorldPos = pos;
            Species = species;
            AiTarget = pos;
            AiTimer = 0f;
            IsPlayer = isPlayer;

            var speciesInfo = SpeciesData.GetSpeciesInfo(species);
            if (speciesInfo != null)
            {
                PreferredFoodId = speciesInfo.PreferredFoodId;
                MaxHP = speciesInfo.MaxHp;
                Speed = speciesInfo.Speed;
                BaseSpeed = speciesInfo.Speed;
                RunSpeed = speciesInfo.RunSpeed > 0 ? speciesInfo.RunSpeed : BaseSpeed * 1.5f;
                Attack = speciesInfo.Attack;
                Behavior = speciesInfo.Behavior;
                VisionRange = speciesInfo.VisionRange;
                AlertDuration = speciesInfo.AlertDuration;
                AttackHitRange = 25f;
                if (string.Equals(species, "Goblin", StringComparison.OrdinalIgnoreCase))
                {
                    AttackHitRange = 90f;
                }
                GrowthTime = speciesInfo.GrowthTime;
            }
            else
            {
                PreferredFoodId = 0;
                MaxHP = 10;
                Speed = 45f;
                BaseSpeed = 45f;
                RunSpeed = 70f;
                Attack = 1;
                Behavior = "passive";
                VisionRange = 0;
                AlertDuration = 0f;
                AttackHitRange = 25;
                GrowthTime = 600f;
            }

            _legHeight = SpeciesData.LegHeights.GetValueOrDefault(species, 0f);

            SpeciesData.ColorPreset? colorPreset = null;
            string renderSpecies = RenderSpecies;

            if (string.Equals(renderSpecies, "human", StringComparison.OrdinalIgnoreCase))
            {
                Tint = Program.SkinColorPalette[Random.Shared.Next(Program.SkinColorPalette.Length)];
                HairColor = new Color(Random.Shared.Next(30, 200), Random.Shared.Next(20, 160), Random.Shared.Next(0, 80), 255);
                HairStyle = Random.Shared.Next(0, Math.Max(1, Program.hairBaseTextures.Count));
                BeardStyle = Random.Shared.Next(0, Math.Max(1, Program.beardBaseTextures.Count));
                EnsureHumanEyeColor();
            }
            else
            {
                //  Tire un préset d'apparence (couleur + features figées) selon PresetChance, sinon reste null
                // et l'apparence sera 100% aléatoire comme avant.
                Biome? spawnBiome = null;
                if (Program.TileSize > 0)
                {
                    float tileX = WorldPos.X / Program.TileSize;
                    float tileY = WorldPos.Y / Program.TileSize;
                    spawnBiome = World.GetBiomeAt(tileX, tileY);
                }
                colorPreset = SpeciesData.PickColorPreset(renderSpecies, spawnBiome);
                Tint = GetRandomSkinColor(renderSpecies, colorPreset);
                HairStyle = 0;
                BeardStyle = 0;
                HairColor = Color.Black;
            }

            AssignRandomFeatures(colorPreset, RandomFeatureVariant, RandomFeatureColor, renderSpecies);

            _idleSoundSlug = BuildIdleSoundSlug(colorPreset);
            InitializeIdleSound();

            CurrentHP = MaxHP;

            if (species.ToLower() == "ogre" && !isPlayer)
            {
                IsBoss = true;
                MaxHP = 120;
                CurrentHP = MaxHP;
                Attack = 12;
            }
            else if (species.ToLower() == "worm" && !isPlayer)
            {
                IsBoss = true;
                MaxHP = 400;
                CurrentHP = MaxHP;
                Attack = 18;
                _wmState = WormBossState.Underground;
            }
            else if (species.ToLower() == "gulper" && !isPlayer)
            {
                // Stats (MaxHP, Attack, etc.) déjà définies dans species.json : on active juste
                // le comportement de boss et on initialise sa machine à états.
                IsBoss = true;
                _guState = GulperBossState.Rising;
                _guGroundPos = WorldPos;
                _guHeight = 0f;
            }
            else if (species.ToLower() == "genie" && !isPlayer)
            {
                MaxHP = 850;
                CurrentHP = MaxHP;
                Attack = 14;
                Behavior = "hostile";
                VisionRange = Math.Max(VisionRange, 3);
                AlertDuration = 1.5f;
                AttackHitRange = 25f;
                Speed = 150f;
                BaseSpeed = 150f;
                RunSpeed = 320f;
                Scale = 1.15f;
                IsBoss = true;
                _geState = GenieBossState.Idle;
                _geAttackCooldown = 1.5f; // petit délai avant la première attaque, le temps de l'intro
            }
            else
            {
                IsBoss = false;
            }

            if (IsTamed)
            {
                Behavior = "tamed";
            }

            if (species.ToLower() == "human" && !isPlayer)
            {
                AssignProfessionAndTrade(allowGuard: !skipHomeAutoAssign);
                ApplyNpcEquipmentAndInventory();
                InitializeSocialProfile();
                if (Profession == ProfessionType.Guard)
                {
                    ConfigureVillageGuardStats();
                    CurrentHP = MaxHP;
                    EquipVillageGuard();
                }
                if (World.IsUnderground)
                    _tentRestCooldown = 45f + (float)(Random.Shared.NextDouble() * 120f);
                if (Profession == ProfessionType.Farmer)
                    GiveStarterSeedsToFarmer();
            }

            //  Les gobelins générés (voir World.LoadOrGenerateChunk, hordes rares) sont
            // toujours hostiles et reçoivent une arme au hasard (épée ou arc), qui détermine
            // ensuite leur style de combat dans ChaseBehavior (mêlée vs. tir à distance).
            if (species.ToLower() == "goblin" && !isPlayer)
            {
                Behavior = "hostile";
                if (VisionRange <= 0)
                    VisionRange = 7;
                EquipGoblinWeapon();
            }

            Decide();

            if (!skipHomeAutoAssign && species.ToLower() == "human" && !isPlayer && !World.IsUnderground
                && HomePosition == Vector2.Zero && !IsTamed)
            {
                var foundHome = World.FindNearestFreeHouseSpot(pos, 2400f);
                if (foundHome.HasValue)
                {
                    HomePosition = foundHome.Value.spotPos;
                    HomeBuildingId = foundHome.Value.buildingId;
                    float darkness = Program.GetDarknessAlpha();
                    if (darkness > 0.6f)
                    {
                        AiState = NpcAiState.GoHome;
                        AiTarget = GetHomeEntryTarget();
                        AnimState = "walk";
                        Speed = BaseSpeed;
                        // Pas de HashSet "destroyed" disponible ici (construction) : le trajet
                        // A* réel sera calculé au prochain tick d'Update, _currentPath étant
                        // encore vide (voir la branche GoHome dans Update).
                    }
                }
            }

        }

        public void EnsureHumanEyeColor()
        {
            if (!string.Equals(RenderSpecies, "human", StringComparison.OrdinalIgnoreCase) || RandomFeatureColor.ContainsKey("__eyeColor"))
                return;

            RandomFeatureColor["__eyeColor"] = new[]
            {
                new Color(45, 35, 25, 255),
                new Color(90, 60, 35, 255),
                new Color(70, 120, 155, 255),
                new Color(95, 145, 95, 255),
                new Color(125, 165, 185, 255)
            }[Random.Shared.Next(5)];
        }

        private string BuildIdleSoundSlug(SpeciesData.ColorPreset? colorPreset)
        {
            string baseName = !string.IsNullOrWhiteSpace(colorPreset?.Name)
                ? colorPreset.Name
                : Species;

            return NormalizeSoundSlug(baseName);
        }

        private string NormalizeSoundSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string normalized = value.Trim().ToLowerInvariant();
            normalized = normalized.Replace(" ", "_");
            normalized = normalized.Replace("-", "_");
            normalized = normalized.Replace("'", string.Empty);
            normalized = normalized.Replace("(", string.Empty).Replace(")", string.Empty);
            normalized = normalized.Replace("/", "_");

            return normalized;
        }

        private void InitializeIdleSound()
        {
            if (IsPlayer || Species == "human" || _idleSoundLoaded || string.IsNullOrWhiteSpace(_idleSoundSlug))
                return;

            foreach (string candidate in GetIdleSoundCandidates())
            {
                var soundPaths = FindIdleSoundFiles(candidate);
                if (soundPaths.Count == 0)
                    continue;

                foreach (string path in soundPaths)
                {
                    Sound sound = Raylib.LoadSound(path);
                    if (Raylib.IsSoundReady(sound))
                        _idleSounds.Add(sound);
                }

                if (_idleSounds.Count > 0)
                {
                    _idleSoundLoaded = true;
                    _idleSoundCooldown = 10f + (float)(Random.Shared.NextDouble() * 12f);
                    return;
                }
            }
        }

        private List<string> FindIdleSoundFiles(string candidate)
        {
            var result = new List<string>();
            string baseName = $"{candidate}_idle";
            string soundsDir = Path.Combine("assets", "sounds");

            if (!Directory.Exists(soundsDir))
                return result;

            foreach (string extension in new[] { ".mp3", ".wav" })
            {
                string directPath = Path.Combine(soundsDir, $"{baseName}{extension}");
                if (File.Exists(directPath) && !result.Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase)))
                    result.Add(directPath);

                var files = Directory.GetFiles(soundsDir, $"{baseName}*{extension}", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string path in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fileName.Length > baseName.Length &&
                        fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                        fileName.Substring(baseName.Length).All(char.IsDigit))
                    {
                        string stem = Path.GetFileNameWithoutExtension(path);
                        if (!result.Any(existing => string.Equals(Path.GetFileNameWithoutExtension(existing), stem, StringComparison.OrdinalIgnoreCase)))
                            result.Add(path);
                    }
                }
            }

            return result;
        }

        private List<string> GetIdleSoundCandidates()
        {
            var candidates = new List<string>();
            string presetSlug = NormalizeSoundSlug(_idleSoundSlug);
            string speciesSlug = NormalizeSoundSlug(Species);

            if (!string.IsNullOrWhiteSpace(presetSlug))
                candidates.Add(presetSlug);

            if (!string.IsNullOrWhiteSpace(speciesSlug) && !string.Equals(presetSlug, speciesSlug, StringComparison.OrdinalIgnoreCase))
                candidates.Add(speciesSlug);

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void TryPlayIdleSound(Vector2 playerPos)
        {
            if (IsPlayer || Species == "human" || !_idleSoundLoaded || _idleSounds.Count == 0)
                return;

            int index = Random.Shared.Next(_idleSounds.Count);
            Sound sound = _idleSounds[index];
            if (!Raylib.IsSoundReady(sound))
                return;

            float distance = Vector2.Distance(WorldPos, playerPos);
            float maxDistance = 900f;
            float volume = Math.Clamp(1f - (distance / maxDistance), 0.05f, 1f);

            Raylib.SetSoundVolume(sound, volume);
            Raylib.SetSoundPitch(sound, 0.95f + (float)(Random.Shared.NextDouble() * 0.1f));
            Raylib.PlaySound(sound);
            _idleSoundCooldown = 16f + (float)(Random.Shared.NextDouble() * 20f);
        }

        /// <summary>Cherche les fichiers assets/sounds/{candidate}{suffix}.mp3 et {candidate}{suffix}N.mp3
        /// (variantes numérotées) - version générique utilisée par les sons idle et hurt.</summary>
        private List<string> FindSuffixedSoundFiles(string candidate, string suffix)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(candidate)) return result;

            string baseName = $"{candidate}{suffix}";
            string soundsDir = Path.Combine("assets", "sounds");

            if (!Directory.Exists(soundsDir))
                return result;

            foreach (string extension in new[] { ".mp3", ".wav" })
            {
                string directPath = Path.Combine(soundsDir, $"{baseName}{extension}");
                if (File.Exists(directPath) && !result.Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase)))
                    result.Add(directPath);

                var files = Directory.GetFiles(soundsDir, $"{baseName}*{extension}", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string path in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fileName.Length > baseName.Length &&
                        fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                        fileName.Substring(baseName.Length).All(char.IsDigit))
                    {
                        string stem = Path.GetFileNameWithoutExtension(path);
                        if (!result.Any(existing => string.Equals(Path.GetFileNameWithoutExtension(existing), stem, StringComparison.OrdinalIgnoreCase)))
                            result.Add(path);
                    }
                }
            }

            return result;
        }

        /// <summary>Charge les sons de blessure ({couleur/preset}_hurtN.mp3 puis, à défaut,
        /// {espèce}_hurtN.mp3), une seule fois par entité, la première fois qu'elle est touchée.</summary>
        private void InitializeHurtSound()
        {
            if (IsPlayer || _hurtSoundLoaded)
                return;
            _hurtSoundLoaded = true; // on ne retente pas le scan du dossier à chaque coup, même si rien trouvé

            foreach (string candidate in GetIdleSoundCandidates())
            {
                var soundPaths = FindSuffixedSoundFiles(candidate, "_hurt");
                if (soundPaths.Count == 0)
                    continue;

                foreach (string path in soundPaths)
                {
                    Sound sound = Raylib.LoadSound(path);
                    if (Raylib.IsSoundReady(sound))
                        _hurtSounds.Add(sound);
                }

                if (_hurtSounds.Count > 0)
                    return;
            }
        }

        private void InitializeAttackSound()
        {
            if (IsPlayer || _attackSoundLoaded)
                return;
            _attackSoundLoaded = true;

            foreach (string candidate in GetIdleSoundCandidates())
            {
                var soundPaths = FindSuffixedSoundFiles(candidate, "_attack");
                if (soundPaths.Count == 0)
                    continue;

                foreach (string path in soundPaths)
                {
                    Sound sound = Raylib.LoadSound(path);
                    if (Raylib.IsSoundReady(sound))
                        _attackSounds.Add(sound);
                }

                if (_attackSounds.Count > 0)
                    return;
            }
        }

        private void PlayAttackSound()
        {
            InitializeAttackSound();
            if (_attackSounds.Count == 0)
                return;

            int index = Random.Shared.Next(_attackSounds.Count);
            Sound sound = _attackSounds[index];
            if (!Raylib.IsSoundReady(sound))
                return;

            float distance = Vector2.Distance(WorldPos, Program.GetPlayerPosition());
            float maxDistance = 900f;
            float volume = Math.Clamp(1f - (distance / maxDistance), 0.05f, 1f);

            Raylib.SetSoundVolume(sound, volume);
            Raylib.SetSoundPitch(sound, 0.95f + (float)(Random.Shared.NextDouble() * 0.1f));
            Raylib.PlaySound(sound);
        }

        /// <summary>Joue un son de blessure aléatoire ({espèce}_hurtN.mp3) lorsque l'entité est touchée.
        /// Le volume diminue avec la distance au joueur, comme pour le son idle.</summary>
        private void PlayHurtSound()
        {
            InitializeHurtSound();
            if (_hurtSounds.Count == 0)
                return;

            int index = Random.Shared.Next(_hurtSounds.Count);
            Sound sound = _hurtSounds[index];
            if (!Raylib.IsSoundReady(sound))
                return;

            float distance = Vector2.Distance(WorldPos, Program.GetPlayerPosition());
            float maxDistance = 900f;
            float volume = Math.Clamp(1f - (distance / maxDistance), 0.05f, 1f);

            Raylib.SetSoundVolume(sound, volume);
            Raylib.SetSoundPitch(sound, 0.92f + (float)(Random.Shared.NextDouble() * 0.16f));
            Raylib.PlaySound(sound);
        }

        //  Donne à un gobelin une arme au hasard : épée en bois (item 16) pour un combattant
        // de mêlée, ou une arme à distance (arc ou mousquet) pour un tireur. C'est cette arme,
        // une fois en main, qui pilote le choix entre MeleeCombatBehavior et RangedCombatBehavior.
        private void EquipGoblinWeapon()
        {
            Equipment ??= new Equipment();
            int[] rangedWeaponIds = { 66, 257 };
            int weaponId = Random.Shared.NextDouble() < 0.5 ? rangedWeaponIds[Random.Shared.Next(rangedWeaponIds.Length)] : 16;
            if (GameData.ItemDatabase.TryGetValue(weaponId, out var data))
            {
                var item = new Item(data.Name, 1, data.Color, data.Icon);
                Equipment.EquipItem(item, EquipmentSlot.MainHand);
                Equipment.LoadEquipmentTextures();
            }
        }

        public void ConfigureVillageGuardStats()
        {
            MaxHP = 100;
            Attack = 8;
            VisionRange = 18;
            AttackHitRange = 65f;
        }

        internal void EquipVillageGuard()
        {
            Equipment ??= new Equipment();
            Equipment.ClearAllEquipment();

            foreach (string itemKey in new[] { "crusader_helmet", "crusader_chestplate", "crusader_leggings" })
            {
                if (!GameData.ItemDatabaseByKey.TryGetValue(itemKey, out var armorData))
                    continue;

                var armorItem = new Item(armorData.Name, 1, armorData.Color, armorData.Icon);
                Equipment.EquipOnBody(armorItem, out _);
            }

            if (GameData.ItemDatabaseByKey.TryGetValue("wooden_sword", out var weaponData))
            {
                var weapon = new Item(weaponData.Name, 1, weaponData.Color, weaponData.Icon);
                Equipment.EquipItem(weapon, EquipmentSlot.MainHand);
            }

            Equipment.LoadEquipmentTextures();
        }

        //  Vrai si l'arme actuellement en main est une arme de tir.
        private bool HasRangedWeapon => Equipment?.MainHand != null && GameData.IsRangedWeapon(Equipment.MainHand.Name);

        private void ApplyNpcEquipmentAndInventory()
        {
            Equipment ??= new Equipment();
            Equipment.ClearAllEquipment();

            var eligibleItems = GameData.ItemDatabase.Values
                .Where(d => d.Type != ItemType.None && d.Type != ItemType.Resource && d.Type != ItemType.Food &&
                            d.Type != ItemType.Placeable && d.Type != ItemType.Throwable && d.Type != ItemType.WallCovering)
                .ToList();

            var hazmat = eligibleItems.FirstOrDefault(d => string.Equals(d.Key, "hazmat_suit", StringComparison.OrdinalIgnoreCase));
            if (hazmat.ID != 0 && Random.Shared.NextDouble() < 0.05)
            {
                var hazmatItem = new Item(hazmat.Name, 1, hazmat.Color, hazmat.Icon);
                ApplyRandomDyeColors(hazmatItem, hazmat);
                Equipment.EquipOnBody(hazmatItem, out _);
            }
            else
            {
                var coat = eligibleItems.FirstOrDefault(d => string.Equals(d.Key, "coat", StringComparison.OrdinalIgnoreCase));
                var jeans = eligibleItems.FirstOrDefault(d => string.Equals(d.Key, "jean", StringComparison.OrdinalIgnoreCase));

                if (coat.ID != 0)
                {
                    var coatItem = new Item(coat.Name, 1, coat.Color, coat.Icon);
                    coatItem.SetMetadataValue("sleeves", new[] { "short", "long", "none" }[Random.Shared.Next(3)]);
                    coatItem.SetMetadataValue("opening", new[] { "open", "closed", "lace_up" }[Random.Shared.Next(3)]);
                    ApplyRandomDyeColors(coatItem, coat);
                    Equipment.EquipOnBody(coatItem, out _);
                }

                if (jeans.ID != 0)
                {
                    var jeansItem = new Item(jeans.Name, 1, jeans.Color, jeans.Icon);
                    ApplyRandomDyeColors(jeansItem, jeans);
                    Equipment.EquipOnBody(jeansItem, out _);
                }

                var accessoryZones = new[]
                {
                    BodyZone.TopOfHead, BodyZone.Back, BodyZone.Face, BodyZone.Neck,
                    BodyZone.Waist, BodyZone.Ears, BodyZone.Feet
                };

                foreach (var zone in accessoryZones)
                {
                    if (Equipment.GetZoneItem(zone) != null || Random.Shared.NextDouble() >= 0.2) continue;

                    var chosen = eligibleItems
                        .Where(d => d.CoveredZones.Contains(zone)
                            && !string.Equals(d.Key, "hazmat_suit", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(_ => Random.Shared.Next())
                        .FirstOrDefault();
                    if (chosen.ID == 0) continue;

                    var item = new Item(chosen.Name, 1, chosen.Color, chosen.Icon);
                    ApplyRandomDyeColors(item, chosen);
                    Equipment.EquipOnBody(item, out _);
                }
            }

            EnsureNpcClothing();

            // ===== ÉQUIPEMENT DES EMPLACEMENTS FONCTIONNELS (main, sac à dos...) =====
            var functionalSlots = new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Backpack };
            foreach (var slot in functionalSlots)
            {
                var candidates = eligibleItems
                    .Where(d => d.HasEquipSlot && d.EquipSlot == slot)
                    .OrderBy(_ => Random.Shared.Next())
                    .ToList();

                if (candidates.Count == 0) continue;

                var chosen = candidates.First();
                var item = new Item(chosen.Name, 1, chosen.Color, chosen.Icon);
                ApplyRandomDyeColors(item, chosen);
                Equipment.EquipItem(item, slot);
            }

            // Charger les textures d'équipement
            Equipment.LoadEquipmentTextures();

            // L'inventaire commence vide. Les objets doivent provenir d'une action réelle
            // (ramassage, production, échange ou cadeau), jamais d'un tirage aléatoire dans
            // toute la base d'items.
            Inventory = new ContainerInventoryData(8, 4);
        }

        private void EnsureNpcClothing()
        {
            if (IsPlayer || !string.Equals(Species, "human", StringComparison.OrdinalIgnoreCase))
                return;

            Equipment ??= new Equipment();
            EnsureNpcClothingZone(BodyZone.Torso, "coat", "leaf_shirt");
            EnsureNpcClothingZone(BodyZone.Legs, "jean");
        }

        private void EnsureNpcClothingZone(BodyZone zone, params string[] itemKeys)
        {
            if (Equipment!.GetZoneItem(zone) != null)
                return;

            foreach (string itemKey in itemKeys)
            {
                if (!GameData.ItemDatabaseByKey.TryGetValue(itemKey, out var itemData))
                    continue;

                var clothing = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
                if (Equipment.EquipOnBody(clothing, out _))
                    return;
            }
        }

        /// <summary>
        /// Si l'item est teignable, lui génère des couleurs personnalisées aléatoires (une par
        /// couche teignable) afin que les PNJ n'apparaissent pas tous avec les couleurs par
        /// défaut de leurs habits. Appelé une seule fois, à l'équipement de l'item au spawn.
        /// </summary>
        private static void ApplyRandomDyeColors(Item item, ItemData itemData)
        {
            if (!itemData.IsDyeable) return;

            // Nombre de couches réellement teignables (voir ItemRenderer.DrawItem) : la couche 1
            // n'est jamais teintée directement, seules les couches 2..DyeLayers le sont. Pour les
            // items "dye simple" (DyeLayers == 1), on garde tout de même 1 couleur : c'est elle qui
            // déclenche la couche de teinte supplémentaire (voir la logique _1.png).
            int dyeSlots = Math.Max(itemData.DyeLayers - 1, 1);

            for (int i = 0; i < dyeSlots; i++)
                item.SetLayerColor(i, RandomDyeColor());
        }

        /// <summary>Génère une couleur aléatoire agréable (évite les teintes trop sombres ou trop délavées).</summary>
        private static Color RandomDyeColor()
        {
            return new Color(
                (byte)Random.Shared.Next(40, 256),
                (byte)Random.Shared.Next(40, 256),
                (byte)Random.Shared.Next(40, 256),
                (byte)255);
        }

        // ==================== MÉTHODES PUBLIQUES ====================
        public string GetDisplayName() => !string.IsNullOrEmpty(FirstName) ? FirstName : !string.IsNullOrEmpty(CustomName) ? CustomName : Species;

        public string GetProfessionDisplayName() => Profession switch
        {
            ProfessionType.Farmer => Localization.Get("profession.farmer"),
            ProfessionType.Blacksmith => Localization.Get("profession.blacksmith"),
            ProfessionType.Lumberjack => Localization.Get("profession.lumberjack"),
            ProfessionType.Hunter => Localization.Get("profession.hunter"),
            ProfessionType.Alchemist => Localization.Get("profession.alchemist"),
            ProfessionType.Tailor => Localization.Get("profession.tailor"),
            ProfessionType.Miner => Localization.Get("profession.miner"),
            ProfessionType.Cook => Localization.Get("profession.cook"),
            ProfessionType.Guard => Localization.GetOrDefault("profession.guard", "Garde"),
            _ => Localization.Get("profession.trader")
        };

        public string GetProfessionDescription() => Profession switch
        {
            ProfessionType.Farmer => Localization.Get("profession.farmer.description"),
            ProfessionType.Blacksmith => Localization.Get("profession.blacksmith.description"),
            ProfessionType.Lumberjack => Localization.Get("profession.lumberjack.description"),
            ProfessionType.Hunter => Localization.Get("profession.hunter.description"),
            ProfessionType.Alchemist => Localization.Get("profession.alchemist.description"),
            ProfessionType.Tailor => Localization.Get("profession.tailor.description"),
            ProfessionType.Miner => Localization.Get("profession.miner.description"),
            ProfessionType.Cook => Localization.Get("profession.cook.description"),
            ProfessionType.Guard => Localization.GetOrDefault("profession.guard.description", "Défend le village contre les créatures hostiles."),
            _ => Localization.Get("profession.trader.description")
        };

        public void AssignProfessionAndTrade(ProfessionType? forcedProfession = null, bool allowGuard = true)
        {
            if (Species != "human" || IsPlayer || IsTamed)
            {
                Profession = ProfessionType.None;
                TraderItems = new List<int>();
                IsTrader = false;
                return;
            }

            Profession = forcedProfession ?? (allowGuard && Random.Shared.NextDouble() < 0.1
                ? ProfessionType.Guard
                : Random.Shared.NextDouble() < 0.7 ? PickRandomProfession() : ProfessionType.None);
            if (Profession == ProfessionType.None)
            {
                TraderItems = new List<int>();
                IsTrader = false;
                return;
            }

            if (Profession == ProfessionType.Guard)
            {
                TraderItems = new List<int>();
                IsTrader = false;
                return;
            }

            InitTraderItems();
            IsTrader = TraderItems.Count > 0;
        }

        /// <summary>
        /// Donne 1 à 2 stacks de graines à un PNJ fermier fraîchement créé, sans quoi
        /// TryPlantSeedInFieldZone ne trouve jamais rien à planter (l'inventaire des
        /// PNJ est rempli d'objets aléatoires, sans lien avec la profession) et les
        /// champs générés restent vides pour toujours.
        /// </summary>
        private void GiveStarterSeedsToFarmer()
        {
            if (GameData.ItemDatabase == null || GameData.ItemDatabase.Count == 0)
                return;

            var seedDatas = GameData.ItemDatabase.Values
                .Where(d => d.IsSeed && d.CropId != 0)
                .OrderBy(_ => Random.Shared.Next())
                .Take(2)
                .ToList();

            foreach (var seedData in seedDatas)
            {
                var emptySlot = Inventory.Slots.FirstOrDefault(s => s.IsEmpty);
                if (emptySlot == null)
                    break;

                int qty = Math.Max(1, Math.Min(seedData.StackSize, 5));
                emptySlot.Item = new Item(seedData.Name, qty, seedData.Color, seedData.Icon);
                emptySlot.Count = qty;
            }

        }

        public void AssignWorkplace()
        {
            if (Profession == ProfessionType.None) return;

            if (ProfessionData.StationObjectIds.TryGetValue(Profession, out var stationIds) && stationIds.Length > 0)
            {
                if (HomeBuildingId >= 0 && World.Houses.TryGetValue(HomeBuildingId, out var ownHouse))
                {
                    var found = World.FindStationTileInBounds(ownHouse.Bounds, stationIds);
                    if (found.HasValue)
                    {
                        WorkplacePosition = found.Value;
                        WorkZoneId = -1;
                        return;
                    }
                }

                var nearest = World.FindNearestUnclaimedStation(HomePosition, stationIds, 1500f, NetId);
                if (nearest.HasValue)
                {
                    WorkplacePosition = nearest.Value;
                    WorkZoneId = -1;
                    return;
                }

                return;
            }

            if (World.ProfessionZoneKind.TryGetValue(Profession, out var zoneKind))
            {
                //  Avec des villages pouvant s'étendre sur plusieurs centaines de
                // tuiles, la distance par défaut de FindNearestFreeWorkSlot (2000
                // unités ≈ 60 tuiles) est souvent trop courte pour relier une maison
                // à la zone de champ, qui peut être placée loin du centre : le
                // fermier ne trouvait alors jamais de poste, silencieusement.
                var slot = World.FindNearestFreeWorkSlot(zoneKind, HomePosition, 8000f);
                if (slot.HasValue)
                {
                    WorkplacePosition = slot.Value.slotPos;
                    WorkZoneId = slot.Value.zoneId;
                    if (Profession == ProfessionType.Farmer)
                        Console.WriteLine($"[Farming] PNJ {NetId} : poste de travail assigné, zone id={slot.Value.zoneId}, position={slot.Value.slotPos}.");
                }
                else if (Profession == ProfessionType.Farmer)
                {
                    if (HomePosition != Vector2.Zero && World.TryCreateFieldNear(HomePosition))
                    {
                        AssignWorkplace();
                        return;
                    }

                    Console.WriteLine($"[Farming] PNJ {NetId} : AUCUN emplacement de champ libre trouvé près de HomePosition={HomePosition} (zones Field existantes : {World.WorkZones.Values.Count(z => z.Kind == WorkZoneKind.Field)}).");
                }
            }
        }

        private ProfessionType PickRandomProfession()
        {
            var values = Enum.GetValues<ProfessionType>()
                .Where(v => v != ProfessionType.None && v != ProfessionType.Guard)
                .ToArray();
            return values.Length == 0 ? ProfessionType.None : values[Random.Shared.Next(values.Length)];
        }

        public void InitTraderItems()
        {
            try
            {
                TraderItems = new List<int>();
                var professionItems = BuildProfessionItemIds();
                foreach (var itemId in professionItems)
                {
                    if (!TraderItems.Contains(itemId))
                        TraderItems.Add(itemId);
                    if (TraderItems.Count >= 8)
                        break;
                }

                // Les PNJ de métier ne doivent pas se retrouver avec un stock "générique" en plus
                // de leurs objets de métier. La liste de fallback ne doit servir que si le métier
                // ne fournit aucun objet cohérent (ou si l'on est dans le cas d'un marchand non
                // spécialisé), sinon on conserve strictement le pool de profession.
                if (TraderItems.Count == 0)
                {
                    var fallbackIds = GameData.ItemDatabase?.Keys
                        .Where(id => id > 0)
                        .OrderBy(_ => Random.Shared.Next())
                        .Take(12)
                        .ToList() ?? new List<int>();

                    foreach (var itemId in fallbackIds)
                    {
                        if (!TraderItems.Contains(itemId))
                            TraderItems.Add(itemId);
                        if (TraderItems.Count >= 8)
                            break;
                    }
                }

                IsTrader = TraderItems.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur dans InitTraderItems : {ex}");
                TraderItems = new List<int> { 1, 2, 3 };
                IsTrader = TraderItems.Count > 0;
            }
        }

        public List<int> GetProfessionItemIds() => BuildProfessionItemIds();

        private List<int> BuildProfessionItemIds()
        {
            var result = new List<int>();
            if (GameData.ItemDatabase == null || GameData.ItemDatabase.Count == 0)
                return result;

            var candidates = GameData.ItemDatabase.Values
                .Where(d => d.ID != 0 && d.Type != ItemType.None && d.Type != ItemType.Placeable && d.Type != ItemType.WallCovering)
                .ToList();

            if (candidates.Count == 0)
                return result;

            var professionKeys = ProfessionData.SaleItemKeys.TryGetValue(Profession, out var keys)
                ? keys
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matched = candidates
                .Where(item => MatchesProfessionSale(item, professionKeys, Profession))
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            foreach (var item in matched)
            {
                if (!result.Contains(item.ID))
                    result.Add(item.ID);
                if (result.Count >= 8)
                    break;
            }

            return result;
        }

        private static bool MatchesProfessionSale(ItemData item, HashSet<string> professionKeys, ProfessionType profession)
        {
            if (item.ID == 0)
                return false;

            if (professionKeys.Count > 0 && !string.IsNullOrWhiteSpace(item.Key) && professionKeys.Contains(item.Key))
                return true;

            return profession switch
            {
                ProfessionType.Farmer => item.Type == ItemType.Food || item.Type == ItemType.Resource || item.ToolKind == ToolType.Hoe || item.ToolKind == ToolType.Scythe || item.ToolKind == ToolType.WateringCan,
                ProfessionType.Blacksmith => item.Type == ItemType.Tool || item.Type == ItemType.Weapon || item.Type == ItemType.Armor || item.ToolKind == ToolType.Hammer || item.ToolKind == ToolType.Pickaxe,
                ProfessionType.Lumberjack => item.Type == ItemType.Tool || item.Type == ItemType.Resource || item.ToolKind == ToolType.Axe || item.ToolKind == ToolType.Shovel,
                ProfessionType.Hunter => item.Type == ItemType.Weapon || item.Type == ItemType.Resource || item.ToolKind == ToolType.Fishing || item.ToolKind == ToolType.Net,
                ProfessionType.Alchemist => item.Type == ItemType.Food || item.Type == ItemType.Resource || item.IsLightSource || item.Type == ItemType.Rune,
                ProfessionType.Tailor => item.Type == ItemType.Armor || item.Type == ItemType.Resource || item.ToolKind == ToolType.Scissors,
                ProfessionType.Miner => item.Type == ItemType.Tool || item.Type == ItemType.Resource || item.ToolKind == ToolType.Pickaxe || item.ToolKind == ToolType.Hammer,
                ProfessionType.Cook => item.Type == ItemType.Food || item.Type == ItemType.Resource,
                _ => true
            };
        }

        public void ApplyKnockback(Vector2 direction, float force)
        {
            if (_isSleeping)
                WakeFromSleep(8f);

            KnockbackVelocity = direction * force;
            IsKnockedBack = true;
        }

        public void ApplyPushImpulse(Vector2 impulse)
        {
            if (_isSleeping)
                WakeFromSleep(8f);

            KnockbackVelocity += impulse;
            IsKnockedBack = true;
        }

        public void WakeFromInteraction()
        {
            if (_isSleeping)
                WakeFromSleep(8f);
        }

        private void WakeFromSleep(float awakeDuration)
        {
            _isSleeping = false;
            _sleepVisualPosition = Vector2.Zero;
            _groundResting = false;
            _sleepTimer = 0f;
            _zzzTimer = 0f;
            _sleepWakeCooldown = Math.Max(_sleepWakeCooldown, awakeDuration);
            ReleaseCurrentGoalLock();
            AnimState = "idle";

            if (_restingInChair)
            {
                World.ReleaseBench(_reservedBench);
                _reservedBench = null;
                _restingInChair = false;
            }

            var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
            Speed = speciesInfo?.Speed ?? BaseSpeed;
            Decide();
        }

        //  Décompte de l'icône d'alerte au-dessus de la tête, et émission différée du signal
        // d'écho une fois le petit délai aléatoire écoulé.
        private void UpdateAlertSignal(float dt, Vector2 playerPos)
        {
            if (ShowAlertIcon)
            {
                AlertIconTimer -= dt;
                if (AlertIconTimer <= 0f)
                    ShowAlertIcon = false;
            }

            if (_pendingSignalDelay >= 0f)
            {
                _pendingSignalDelay -= dt;
                if (_pendingSignalDelay <= 0f)
                {
                    _pendingSignalDelay = -1f;
                    AlertSignalManager.Emit(this, _pendingSignalTarget, _pendingSignalTargetConnectionId);
                }
            }
        }

        //  Appelée quand l'entité vient tout juste de repérer sa cible elle-même (pas via écho) :
        // affiche l'icône exclamation.png et programme, après un court délai aléatoire, l'envoi
        // du signal aux entités hostiles proches.
        public void TriggerAlertSignal(Vector2 targetPos, int targetConnectionId)
        {
            //  Déjà en poursuite directe / combat : pas besoin de ré-afficher l'icône
            //  ni de reprogrammer un signal de relai supplémentaire. Le point d'exclamation
            //  ne doit servir qu'au moment où l'entité vient de détecter la menace, pas à chaque
            //  coup d'écho de poursuite déjà engagée.
            if (AiState == NpcAiState.Chase)
                return;

            ShowAlertIcon = true;
            AlertIconTimer = ALERT_ICON_DURATION;

            if (_pendingSignalDelay < 0f)
            {
                _pendingSignalDelay = (float)(Random.Shared.NextDouble() * 0.6 + 0.2); // 0.2s à 0.8s
                _pendingSignalTarget = targetPos;
                _pendingSignalTargetConnectionId = targetConnectionId;
            }
        }

        //  Appelée quand l'entité reçoit le signal d'une entité voisine ("écho") : elle se met à
        // pourchasser la cible sans l'avoir vue elle-même, affiche à son tour l'icône, puis
        // programme son propre relais après un nouveau délai aléatoire, propageant ainsi
        // l'alerte de proche en proche.
        public void ReceiveAlertSignal(Vector2 targetPos, int targetConnectionId)
        {
            if (!IsAlive || IsTamed || (Behavior != "hostile" && Behavior != "neutral")) return;
            if (AiState == NpcAiState.Chase)
                return; // déjà en poursuite active : ne pas re-warn pendant que le combat est engagé
            if (HasPropagatedSignal) return; // déjà alertée et déjà relayée
            if (_currentGoalPriority > NpcPriority.Combat) return; // ex : en train de fuir pour sa survie, ignore l'écho

            ResetPathfinding();
            ForceGoal(NpcPriority.Combat, 1.2f);
            AiTarget = targetPos;
            AiTargetConnectionId = targetConnectionId;
            Speed = RunSpeed;
            AiTimer = 2f;
            IsAlerted = true;
            AlertTimer = AlertDuration;

            //  Alerté par relais (écho) plutôt que par détection directe : la cible peut être
            // loin, donc même logique de monte que la détection directe (voir plus haut).
            TryMountOwnedPet(targetPos);

            ShowAlertIcon = true;
            AlertIconTimer = ALERT_ICON_DURATION;

            if (!HasPropagatedSignal)
            {
                HasPropagatedSignal = true;
                _pendingSignalDelay = (float)(Random.Shared.NextDouble() * 0.6 + 0.2);
                _pendingSignalTarget = targetPos;
                _pendingSignalTargetConnectionId = targetConnectionId;
            }
        }

        public void OnHit(Vector2? attackerPos = null, Entity? attackerEntity = null, bool attackerIsPlayer = true, int attackerConnectionId = -1)
        {
            Vector2 hitPos = attackerPos ?? Program.GetPlayerPosition();
            _fearTargetEntity = attackerEntity;
            _fearTargetIsPlayer = attackerEntity == null && attackerIsPlayer;
            _fearTargetConnectionId = _fearTargetIsPlayer ? attackerConnectionId : -1;
            _fearTargetPosition = attackerEntity?.WorldPos ?? hitPos;
            _hasFearTarget = true;
            bool canRetaliate = IsVillageGuard || IsSpiritAnimal || Behavior == "hostile" || Behavior == "neutral"
                || (IsTamed && TamedBehavior == TamedAnimalMode.Attack);
            //  CORRECTIF : un animal apprivoisé par le joueur ne riposte jamais contre son maître
            // (seuls les pets "hostiles" d'un PNJ hostile, IsHostilePet, gardent ce réflexe).
            bool retaliateAgainstPlayerAllowed = !IsTamed || IsHostilePet;
            if (!canRetaliate)
            {
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = false;
                _retaliationTimer = 0f;
            }
            else if (attackerEntity != null)
            {
                _retaliationTargetEntity = attackerEntity;
                _retaliateAgainstPlayer = false;
                _retaliationTimer = RETALIATION_DURATION;
            }
            else if (attackerIsPlayer && retaliateAgainstPlayerAllowed)
            {
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = true;
                _retaliationTimer = RETALIATION_DURATION;
            }
            DamageFlash = 0.2f;
            PlayHurtSound(); //  Son de blessure {espèce}_hurtN.mp3 (aléatoire) à chaque coup reçu

            if (_isSleeping)
                WakeFromSleep(8f);

            //  Une entité qui vient de se prendre un coup considère toujours sa vie en danger :
            // ceci prend le pas sur n'importe quel autre objectif en cours (routine, nourriture,
            // discussion...), et verrouille ce nouvel objectif un court instant pour éviter qu'il
            // ne soit aussitôt écrasé par la logique de routine de la même frame.
            bool isVillageGuard = IsVillageGuard;
            //  CORRECTIF : un animal apprivoisé n'a JAMAIS le réflexe de fuir (priorité Survie à
            // 100 qui écrasait OwnerCommand=60 et le laissait ignorer les ordres du joueur).
            bool aboutToFlee = !isVillageGuard && !IsSpiritAnimal && Behavior != "hostile" && Behavior != "neutral" && !IsTamed;
            bool aboutToTurnHostile = Behavior == "neutral" && !IsTamed;
            if (aboutToFlee)
                ForceGoal(NpcPriority.Survival, 3f);
            else if (aboutToTurnHostile || Behavior == "hostile")
                ForceGoal(NpcPriority.Combat, 1.5f);

            if (Behavior == "neutral" && !IsTamed)
            {
                Behavior = "hostile";
                ResetPathfinding();
                AiState = NpcAiState.Chase;
                AiTarget = attackerEntity?.WorldPos ?? _fearTargetPosition;
                AiTargetConnectionId = attackerEntity == null ? attackerConnectionId : -1;
                Speed = RunSpeed;
                _chaseStartPos = WorldPos;
                _chaseElapsedTime = 0f;
                IsAlerted = true;
                AlertTimer = AlertDuration;
                if (IsVillager && TryStartInfirmaryCare())
                    return;
                if (attackerEntity == null && attackerIsPlayer)
                    TriggerAlertSignal(AiTarget, AiTargetConnectionId);
                return;
            }

            if (IsVillager && TryStartInfirmaryCare())
                return;

            if (Behavior != "hostile" && !isVillageGuard && !IsSpiritAnimal && !IsTamed)
            {
                ResetPathfinding();
                IsAlerted = true;
                AlertTimer = AlertDuration;
                AlertOrigin = WorldPos;
                _lastKnownPlayerPos = hitPos;
                _fearCooldown = 5f;
                _continuousFearTimer = 0f;

                Vector2 fleeTargetPos = GetFearTargetPosition();
                Vector2 fleeDir = WorldPos - fleeTargetPos;
                if (fleeDir.Length() > 0.01f) fleeDir = Vector2.Normalize(fleeDir);
                else fleeDir = new Vector2(1, 0);
                Vector2 fleeTarget = WorldPos + fleeDir * 300f;
                AiTarget = fleeTarget;
                AiState = NpcAiState.Flee;
                _attackPhase = AttackPhase.None;
                AiTimer = 8f;
                Speed = RunSpeed;
            }
        }

        private bool TryStartInfirmaryCare()
        {
            if (!IsVillager || IsTamed || !IsAlive || CurrentHP >= MaxHP)
                return false;
            if (_infirmaryBuildingId >= 0)
                return true;

            if (!World.TryReserveNearestInfirmary(this, out int buildingId, out Vector2 approachPos))
            {
                _infirmaryRetryTimer = 5f;
                return false;
            }

            _infirmaryBuildingId = buildingId;
            _infirmaryHealAccumulator = 0f;
            _infirmaryRetryTimer = 0f;
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            _attackPhase = AttackPhase.None;
            _currentPath.Clear();
            _pathRecalcTimer = 0f;
            _pathFailStreak = 0;
            AiTarget = approachPos;
            AiState = NpcAiState.GoToInfirmary;
            Speed = RunSpeed > 0f ? RunSpeed : BaseSpeed;
            AnimState = "walk";
            ForceGoal(NpcPriority.Medical, 3f);
            return true;
        }

        public void UpdateWaterStatus()
        {
            int tileX = (int)(WorldPos.X / Program.TileSize);
            int tileY = (int)(WorldPos.Y / Program.TileSize);
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            IsInWater = (groundId == 10);
        }

        public Vector2 FeetPos => new(WorldPos.X, WorldPos.Y + Program.FeetOffsetY);
        public bool IsSleeping => _isSleeping;
        public Vector2 VisualWorldPos => _isSleeping && _sleepVisualPosition != Vector2.Zero
            ? _sleepVisualPosition
            : WorldPos;
        public bool IsAlive => CurrentHP > 0;

        private float GetFleeRadius()
        {
            return VisionRange * Program.TileSize;
        }

        private float GetFleeTriggerRadius()
        {
            return GetFleeRadius() * 0.5f;
        }

        private float GetFleeSafeRadius()
        {
            return GetFleeRadius() * 1.15f;
        }

        public void ForceWakeFromTent()
        {
            if (!_isSleeping || HomeBuildingId != TentHomeBuildingId)
                return;

            _isSleeping = false;
            _sleepVisualPosition = Vector2.Zero;
            _sleepTimer = 0f;
            _tentSleepTimer = 0f;
            _zzzTimer = 0f;
            ReleaseCurrentGoalLock();
            AnimState = "idle";
            Speed = BaseSpeed;
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            _currentPath.Clear();
            _morningDeparturePending = true;
        }

        public void WakeFromCarry()
        {
            if (!_isSleeping)
                return;

            _isSleeping = false;
            _groundResting = false;
            _sleepVisualPosition = Vector2.Zero;
            _sleepTimer = 0f;
            _zzzTimer = 0f;
            ReleaseCurrentGoalLock();
            AnimState = "idle";
            if (_restingInChair)
            {
                World.ReleaseBench(_reservedBench);
                _reservedBench = null;
                _restingInChair = false;
            }

            var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
            if (speciesInfo != null)
                Speed = speciesInfo.Speed;
            Decide();
        }

        // ==================== HELPERS DE DÉCISION ====================
        //  Applique un nouvel objectif de mouvement/repos en une seule fois. Remplace les ~7 lignes
        // (AiState/AiTarget/AiTimer/AnimState/Speed/_currentPath.Clear/_pathRecalcTimer) qui étaient
        // recopiées à l'identique dans chaque branche de l'ancien Decide().
        private void SetGoal(NpcAiState state, Vector2 target, float timer, string anim = "walk", float? speed = null)
        {
            AiState = state;
            AiTarget = target;
            AiTimer = timer;
            AnimState = anim;
            Speed = speed ?? BaseSpeed;
            _currentPath.Clear();
            _pathRecalcTimer = PATH_RECALC_INTERVAL;
        }

        private static float RandomRange(float min, float max) => (float)(min + Random.Shared.NextDouble() * (max - min));

        //  Tire un choix parmi plusieurs options pondérées (les poids n'ont pas besoin de sommer à 1).
        // Remplace les anciennes cascades de `if (Random.Shared.NextDouble() < x) { ...; return; }`
        // indépendantes les unes des autres, où chaque test "mangeait" une partie du hasard du
        // précédent : la distribution réelle ne correspondait plus du tout aux pourcentages lus
        // dans le code, ce qui rendait le comportement des PNJ imprévisible et impossible à régler.
        private static T WeightedChoice<T>(params (T value, float weight)[] options)
        {
            float total = 0f;
            foreach (var (_, w) in options) total += w;
            float roll = (float)(Random.Shared.NextDouble() * total);
            float acc = 0f;
            foreach (var (value, w) in options)
            {
                acc += w;
                if (roll < acc) return value;
            }
            return options[^1].value;
        }

        private enum VillagerIdleChoice { VisitVillageCenter, SitOnBench, RoutineWalk, Idle, Wander }

        //  Point d'entrée de l'IA "de fond" : choisit une envie (Wander/Idle/routine) quand rien de
        // plus important (survie, faim, discussion forcée, etc.) ne réclame déjà la main. Délègue
        // chaque famille de comportement (villageois / slime hostile / reste) à sa propre méthode
        // au lieu d'un unique bloc de 150 lignes imbriquées.
        public void Decide()
        {
            if (AiState == NpcAiState.Chase || AiState == NpcAiState.Flee || AiState == NpcAiState.Follow
                || AiState == NpcAiState.GoToInfirmary || AiState == NpcAiState.HealingAtInfirmary)
                return;

            //  Decide() choisit toujours une envie de "fond" (Wander/Idle/routine intérieure) :
            // on relâche le verrou de priorité pour que la prochaine envie plus importante
            // (nourriture, discussion, danger...) puisse à nouveau prendre la main normalement.
            _currentGoalPriority = NpcPriority.Idle;
            _goalLockTimer = 0f;
            Speed = BaseSpeed;

            if (IsVillager && HomePosition != Vector2.Zero)
            {
                DecideVillagerIdle();
                return;
            }

            if (IsSlime && !IsTamed)
            {
                DecideHostileSlimeIdle();
                return;
            }

            DecideDefaultIdle();
        }

        //  Comportement "de fond" d'un villageois : routine diurne autour du village, repos
        // nocturne chez lui.
        private void DecideVillagerIdle()
        {
            bool isDaytime = IsCaveTentResident
                ? _tentRestCooldown > 0f
                : Program.GetDarknessAlpha() <= 0.6f;

            //  La nuit, un villageois qui a un logement se repose chez lui au lieu de repartir en
            // balade : le système GoHome (dans Update) le ramènera de toute façon vers HomePosition
            // dès qu'il s'en éloigne, autant éviter l'aller-retour inutile et le flicker "un pas
            // dehors puis demi-tour" que ça provoquerait.
            if (!isDaytime)
            {
                SetGoal(NpcAiState.Idle, WorldPos, RandomRange(3f, 7f), "idle");
                return;
            }

            bool canDoRoutine = !IsTalking && CurrentTalkPartner == null && TalkCooldown <= 0f;
            if (!canDoRoutine)
                return; // occupé (discussion...) : Decide() sera rappelé au prochain tick

            if (_morningDeparturePending)
            {
                _morningDeparturePending = false;
                SetGoal(NpcAiState.WalkToTarget, PickVillagerRoutineTarget(), RandomRange(6f, 12f));
                return;
            }

            //  Les villageois qui se trouvent encore chez eux en plein jour doivent sortir
            // de leur maison comme une vraie routine du matin, sans attendre un tirage
            // aléatoire "Idle" qui les laisse figés dans leur intérieur toute la journée.
            if (IsInsideAssignedHouse() && !IsTalking && CurrentTalkPartner == null)
            {
                SetGoal(NpcAiState.WalkToTarget, PickVillagerRoutineTarget(), RandomRange(4f, 8f));
                return;
            }

            if (TryHandleVillagerProfessionRoutine()) return;
            if (TryStartQueuedRoutineTask()) return;

            //  Décision pondérée unique (voir WeightedChoice ci-dessus) : la plupart du temps le
            // villageois part vers une destination de routine ; plus rarement il va au centre du
            // village, s'assoit sur un banc, déambule sans but précis, ou ne fait rien.
            var choice = WeightedChoice(
                (VillagerIdleChoice.VisitVillageCenter, _villageCenterVisitCooldown <= 0f ? 0.25f : 0f),
                (VillagerIdleChoice.SitOnBench, 0.10f),
                (VillagerIdleChoice.RoutineWalk, 0.40f),
                (VillagerIdleChoice.Idle, 0.08f),
                (VillagerIdleChoice.Wander, 0.17f));

            switch (choice)
            {
                case VillagerIdleChoice.VisitVillageCenter:
                    QueueRoutineTask(RoutineTaskKind.VisitVillageCenter);
                    if (TryStartQueuedRoutineTask())
                        return;
                    goto case VillagerIdleChoice.RoutineWalk; // pas de centre à proximité : repli

                case VillagerIdleChoice.SitOnBench:
                    var freeBench = World.FindNearestFreeBench(WorldPos, 700f);
                    if (freeBench.HasValue)
                    {
                        _reservedBench = freeBench.Value.key;
                        _benchSeatVisualPos = freeBench.Value.seatPos;
                        _walkingToBench = true;
                        SetGoal(NpcAiState.WalkToTarget, freeBench.Value.approachPos, 20f);
                        return;
                    }
                    goto case VillagerIdleChoice.RoutineWalk; // pas de banc libre à proximité : repli

                case VillagerIdleChoice.RoutineWalk:
                    //  Vraie destination praticable choisie à l'avance et atteinte via le
                    // pathfinding, plutôt qu'une direction aléatoire qui pouvait foncer dans les
                    // obstacles.
                    SetGoal(NpcAiState.WalkToTarget, PickVillagerRoutineTarget(), RandomRange(3f, 7f));
                    return;

                case VillagerIdleChoice.Idle:
                    SetGoal(NpcAiState.Idle, WorldPos, RandomRange(3f, 9f), "idle");
                    return;

                case VillagerIdleChoice.Wander:
                    SetGoal(NpcAiState.Wander,
                        IsCaveTentResident ? PickCaveTentRoutineTarget() : PickWanderDestination(HomePosition, 60f, 150f),
                        RandomRange(5f, 15f), "walk", SpeciesData.GetSpeed(Species));
                    return;
            }
        }

        //  Slime sauvage non hostile (ou hors de portée d'agression) : déambule librement. La
        // poursuite proprement dite (Behavior == "hostile" à portée) reste gérée dans Update()/Chase.
        private void DecideHostileSlimeIdle()
        {
            if (Behavior == "hostile" && VisionRange > 0)
                return; // le comportement de poursuite sera géré par Chase dans Update()

            SetGoal(NpcAiState.Wander, PickWanderDestination(WorldPos, 80f, 220f), 5f, "walk", SpeciesData.GetSpeed(Species));
        }

        //  Comportement par défaut pour tout ce qui n'est ni villageois ni slime (animaux,
        // monstres sans routine spécifique...) : alterne simplement idle et déambulation.
        private void DecideDefaultIdle()
        {
            if (Random.Shared.NextDouble() < 0.4)
            {
                AiState = NpcAiState.Idle;
                AiTimer = RandomRange(2f, 6f);
                Speed = SpeciesData.GetSpeed(Species);
                return;
            }

            SetGoal(NpcAiState.Wander, PickWanderDestination(WorldPos, 80f, 220f), RandomRange(4f, 12f), "walk", SpeciesData.GetSpeed(Species));
        }

        //  OPTIMISATION PERF (villages) : PickVillagerRoutineTarget est appelée depuis Decide(),
        // c'est-à-dire potentiellement une fois toutes les 3-10s PAR villageois. Avec ~20
        // villageois, ça se traduisait par un scan LINQ (.Where + .OrderBy(random) + .ToList())
        // de TOUTE la liste Program.entities (pas seulement les villageois - tous les animaux,
        // monstres etc. chargés autour) quasiment chaque seconde, avec un vrai tri O(n log n)
        // juste pour piocher UN élément au hasard. On remplace par une boucle manuelle +
        // "reservoir sampling" (choisir un élément uniformément au hasard parmi un flux, sans
        // connaître sa taille à l'avance et sans construire de liste intermédiaire) : un seul
        // passage, aucune allocation, aucun tri.
        //  Alterne travail et pause : renvoie true tant que le villageois est en pause. Quand le
        // service de travail est écoulé, déclenche une nouvelle pause (pendant laquelle
        // DecideVillagerIdle choisit centre du village / banc / balade...).
        private bool IsOnWorkBreak()
        {
            if (_workBreakCooldown > 0f)
                return true;

            if (_workShiftTimer <= 0f)
            {
                _workBreakCooldown = RandomRange(40f, 90f);
                _workShiftTimer = RandomRange(150f, 300f);
                return true;
            }

            return false;
        }

        private bool TryHandleVillagerProfessionRoutine()
        {
            if (!IsVillager || IsTalking || CurrentTalkPartner != null || TalkCooldown > 0f)
                return false;

            if (Profession == ProfessionType.Farmer)
            {
                //  Le fermier prend lui aussi des pauses (sinon il ne quitte jamais son champ).
                if (IsOnWorkBreak())
                    return false;

                if (WorkplacePosition == null || WorkZoneId < 0)
                    AssignWorkplace();

                if (WorkplacePosition.HasValue)
                {
                    AiState = NpcAiState.WalkToTarget;
                    AiTarget = WorkplacePosition.Value;
                    AiTimer = 45f;
                    AnimState = "walk";
                    Speed = BaseSpeed;
                    _currentPath.Clear();
                    _pathRecalcTimer = PATH_RECALC_INTERVAL;
                    return true;
                }

                return false;
            }

            //  Métiers à zone de travail (bûcheron, mineur, chasseur) : allaient auparavant JAMAIS
            // à leur poste (seul le fermier était géré). Ils rejoignent maintenant leur emplacement,
            // y restent un moment, puis font une pause avant de revenir.
            if (Profession != ProfessionType.None && Profession != ProfessionType.Guard
                && World.ProfessionZoneKind.ContainsKey(Profession))
            {
                if (IsOnWorkBreak())
                    return false;

                if (WorkplacePosition == null)
                {
                    if (_workplaceRetryCooldown > 0f)
                        return false;
                    _workplaceRetryCooldown = 30f; // évite de relancer la recherche à chaque Decide()
                    AssignWorkplace();
                }

                if (WorkplacePosition.HasValue)
                {
                    if (!_workApproachPos.HasValue)
                    {
                        var destroyed = Program.GetDestroyedObjects();
                        _workApproachPos = Pathfinder.IsWorldPositionWalkable(WorkplacePosition.Value, destroyed, Species)
                            ? WorkplacePosition.Value
                            : Pathfinder.FindNearestWalkablePosition(WorkplacePosition.Value, destroyed, Species, 4)
                              ?? WorkplacePosition.Value;
                    }

                    // Déjà au poste : il "travaille" sur place un moment avant la prochaine décision.
                    if (Vector2.DistanceSquared(WorldPos, _workApproachPos.Value) < 90f * 90f)
                    {
                        SetGoal(NpcAiState.Idle, WorldPos, RandomRange(12f, 30f), "idle");
                        return true;
                    }

                    SetGoal(NpcAiState.WalkToTarget, _workApproachPos.Value, 45f);
                    return true;
                }
            }

            if (IsTrader && _villageCenterVisitCooldown <= 0f)
            {
                var villageCenter = World.FindNearestVillageCenter(WorldPos, 1600f);
                if (villageCenter != null)
                {
                    _targetVillageCenter = villageCenter;
                    AiState = NpcAiState.GoToVillageCenter;
                    AiTarget = PickVillageCenterActivityTarget(villageCenter);
                    _villageCenterVisitCooldown = RandomRange(100f, 160f);
                    AiTimer = 90f;
                    AnimState = "walk";
                    Speed = BaseSpeed;
                    _currentPath.Clear();
                    _pathRecalcTimer = PATH_RECALC_INTERVAL;
                    return true;
                }
            }

            return false;
        }

        private Vector2 PickVillageCenterActivityTarget(VillageCenter center)
        {
            int tileSize = Program.TileSize;
            Vector2 centerPos = World.GetVillageCenterWorldPosition(center);
            float maxRadius = Math.Min(tileSize * 5.5f, center.ExchangeRadius - 12f);
            float minRadius = Math.Min(tileSize * 2.25f, maxRadius * 0.55f);
            if (maxRadius <= 0f || minRadius <= 0f)
                return WorldPos;

            uint stableOffset = unchecked((uint)NetId.GetHashCode());
            float baseAngle = (stableOffset % 16) / 16f * MathF.Tau;
            float angleStep = MathF.PI * (3f - MathF.Sqrt(5f));
            HashSet<string> destroyed = Program.GetDestroyedObjects();

            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = baseAngle + attempt * angleStep;
                float radius = RandomRange(minRadius, maxRadius);
                Vector2 candidate = centerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                int tileX = (int)MathF.Floor(candidate.X / tileSize);
                int tileY = (int)MathF.Floor(candidate.Y / tileSize);
                var ground = WorldTileRegistry.GetTile(World.GetGroundTileIdAt(tileX, tileY));

                if (ground == null || !ground.Walkable || World.GetObjectIdAt(tileX, tileY) != 0)
                    continue;
                if (!Pathfinder.IsWorldPositionWalkable(candidate, destroyed, Species))
                    continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species))
                    continue;
                if (IsSpotCrowded(candidate, 55f))
                    continue;

                return candidate;
            }

            return WorldPos;
        }

        private bool TryFarmerWorkCycle()
        {
            if (Profession != ProfessionType.Farmer)
                return false;

            if (WorkZoneId >= 0 && World.WorkZones.TryGetValue(WorkZoneId, out var zone) && zone.Kind == WorkZoneKind.Field)
            {
                if (_farmerWorkTarget.HasValue && Vector2.DistanceSquared(WorldPos, _farmerWorkTarget.Value) < 70f * 70f)
                {
                    if (TryHarvestReadyCropInZone(zone, _farmerWorkTarget.Value))
                    {
                        Console.WriteLine($"[Farming] PNJ {NetId} a récolté sur {(_farmerWorkTarget.Value / Program.TileSize)} dans la zone {WorkZoneId}.");
                        _farmerWorkTarget = null;
                        AiTimer = 0f;
                        return true;
                    }
                    if (TryWaterDryingCropInZone(zone, _farmerWorkTarget.Value))
                    {
                        Console.WriteLine($"[Farming] PNJ {NetId} a arrosé {(_farmerWorkTarget.Value / Program.TileSize)} sur la zone {WorkZoneId}.");
                        _farmerWorkTarget = null;
                        AiTimer = 0f;
                        return true;
                    }
                    if (TryTillFieldTile(zone, _farmerWorkTarget.Value))
                    {
                        Console.WriteLine($"[Farming] PNJ {NetId} a labouré {(_farmerWorkTarget.Value / Program.TileSize)} dans la zone {WorkZoneId}.");
                        _farmerWorkTarget = null;
                        AiTimer = 0f;
                        return true;
                    }
                    if (TryPlantSeedInFieldZone(zone, _farmerWorkTarget.Value))
                    {
                        Console.WriteLine($"[Farming] PNJ {NetId} a planté sur {(_farmerWorkTarget.Value / Program.TileSize)} dans la zone {WorkZoneId}.");
                        _farmerWorkTarget = null;
                        AiTimer = 0f;
                        return true;
                    }

                    _farmerWorkTarget = null;
                }

                if (TryFindFarmerActionTarget(zone, out var targetPos))
                {
                    _farmerWorkTarget = targetPos;
                    AiState = NpcAiState.WalkToTarget;
                    AiTarget = targetPos;
                    AnimState = "walk";
                    _currentPath.Clear();
                    _pathRecalcTimer = PATH_RECALC_INTERVAL;
                    return false;
                }

                Console.WriteLine($"[Farming] PNJ {NetId} est sur sa zone {WorkZoneId} mais n'a rien trouvé à faire (pas de récolte prête, rien à arroser, pas de case libre ou plus de graines).");
            }

            return false;
        }

        private bool TryTillFieldTile(VillageWorkZone zone, Vector2 targetPos)
        {
            if (zone.TilledTileCount >= zone.MaxTilledTiles)
                return false;

            int tileX = (int)MathF.Floor(targetPos.X / Program.TileSize);
            int tileY = (int)MathF.Floor(targetPos.Y / Program.TileSize);
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            var groundTile = WorldTileRegistry.GetTile(groundId);
            if (groundTile == null || !groundTile.Diggable || groundId == 27 || groundId == 29)
                return false;
            if (World.GetObjectIdAt(tileX, tileY) != 0 || World.GetCropDataAt(tileX, tileY) != null)
                return false;

            World.SetGroundTile(tileX, tileY, 27);
            zone.TilledTileCount++;
            return true;
        }

        private bool TryFindFarmerActionTarget(VillageWorkZone zone, out Vector2 targetPos)
        {
            targetPos = Vector2.Zero;
            int minTileX = (int)MathF.Floor((zone.AnchorPos.X - zone.Radius) / Program.TileSize) - 1;
            int maxTileX = (int)MathF.Ceiling((zone.AnchorPos.X + zone.Radius) / Program.TileSize) + 1;
            int minTileY = (int)MathF.Floor((zone.AnchorPos.Y - zone.Radius) / Program.TileSize) - 1;
            int maxTileY = (int)MathF.Ceiling((zone.AnchorPos.Y + zone.Radius) / Program.TileSize) + 1;

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    int objectId = World.GetObjectIdAt(x, y);
                    var tileData = WorldTileRegistry.GetTile(objectId);
                    if (tileData != null && tileData.IsCrop)
                    {
                        var cropData = World.GetCropDataAt(x, y);
                        if (cropData != null && cropData.AccumulatedGrowthTime >= tileData.GrowthTime)
                        {
                            targetPos = new Vector2((x + 0.5f) * Program.TileSize, (y + 0.5f) * Program.TileSize);
                            return true;
                        }
                    }
                }
            }

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    int groundId = World.GetGroundTileIdAt(x, y);
                    if (groundId != 27)
                        continue;

                    var cropData = World.GetCropDataAt(x, y);
                    if (cropData == null)
                        continue;

                    var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
                    if (tileData == null || !tileData.IsCrop)
                        continue;
                    if (cropData.AccumulatedGrowthTime >= tileData.GrowthTime)
                        continue;

                    targetPos = new Vector2((x + 0.5f) * Program.TileSize, (y + 0.5f) * Program.TileSize);
                    return true;
                }
            }

            var seedSlot = Inventory.Slots.FirstOrDefault(slot =>
                !slot.IsEmpty && slot.Item != null &&
                GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var data) &&
                data.IsSeed && data.CropId != 0);

            if (seedSlot == null || seedSlot.Item == null)
                return TryFindUntilledFieldTile(zone, minTileX, maxTileX, minTileY, maxTileY, out targetPos);

            int itemId = GameData.GetItemId(seedSlot.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var seedData) || seedData.CropId == 0)
                return false;

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    int groundId = World.GetGroundTileIdAt(x, y);
                    if (groundId != 27 && groundId != 29)
                        continue;
                    if (World.GetObjectIdAt(x, y) != 0)
                        continue;
                    if (World.GetCropDataAt(x, y) != null)
                        continue;

                    targetPos = new Vector2((x + 0.5f) * Program.TileSize, (y + 0.5f) * Program.TileSize);
                    return true;
                }
            }

            return TryFindUntilledFieldTile(zone, minTileX, maxTileX, minTileY, maxTileY, out targetPos);
        }

        private bool TryFindUntilledFieldTile(VillageWorkZone zone, int minTileX, int maxTileX, int minTileY, int maxTileY, out Vector2 targetPos)
        {
            targetPos = Vector2.Zero;
            if (zone.TilledTileCount >= zone.MaxTilledTiles)
                return false;

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    int groundId = World.GetGroundTileIdAt(x, y);
                    var groundTile = WorldTileRegistry.GetTile(groundId);
                    if (groundTile == null || !groundTile.Diggable)
                        continue;
                    if (groundId == 27 || groundId == 29 || World.GetObjectIdAt(x, y) != 0 || World.GetCropDataAt(x, y) != null)
                        continue;

                    targetPos = new Vector2((x + 0.5f) * Program.TileSize, (y + 0.5f) * Program.TileSize);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Réarrose une culture déjà plantée mais dont la terre est redevenue sèche
        /// (ground id 27) avant d'avoir fini de pousser. Sans cette étape, la terre
        /// s'assèche au bout de FARMLAND_DRYING_TIME (300s) alors que la plupart des
        /// cultures ont un GrowthTime bien supérieur (items.json), et la croissance
        /// reste figée indéfiniment faute de PNJ pour la réarroser.
        /// </summary>
        private bool TryWaterDryingCropInZone(VillageWorkZone zone, Vector2? targetPos = null)
        {
            int minTileX = (int)MathF.Floor((zone.AnchorPos.X - zone.Radius) / Program.TileSize) - 1;
            int maxTileX = (int)MathF.Ceiling((zone.AnchorPos.X + zone.Radius) / Program.TileSize) + 1;
            int minTileY = (int)MathF.Floor((zone.AnchorPos.Y - zone.Radius) / Program.TileSize) - 1;
            int maxTileY = (int)MathF.Ceiling((zone.AnchorPos.Y + zone.Radius) / Program.TileSize) + 1;

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    if (targetPos.HasValue)
                    {
                        int targetTileX = (int)MathF.Floor(targetPos.Value.X / Program.TileSize);
                        int targetTileY = (int)MathF.Floor(targetPos.Value.Y / Program.TileSize);
                        if (x != targetTileX || y != targetTileY)
                            continue;
                    }

                    int groundId = World.GetGroundTileIdAt(x, y);
                    if (groundId != 27)
                        continue;

                    var cropData = World.GetCropDataAt(x, y);
                    if (cropData == null)
                        continue;

                    var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
                    if (tileData == null || !tileData.IsCrop)
                        continue;
                    if (cropData.AccumulatedGrowthTime >= tileData.GrowthTime)
                        continue;

                    World.WaterFarmland(x, y);
                    return true;
                }
            }

            return false;
        }

        private bool TryHarvestReadyCropInZone(VillageWorkZone zone, Vector2? targetPos = null)
        {
            int minTileX = (int)MathF.Floor((zone.AnchorPos.X - zone.Radius) / Program.TileSize) - 1;
            int maxTileX = (int)MathF.Ceiling((zone.AnchorPos.X + zone.Radius) / Program.TileSize) + 1;
            int minTileY = (int)MathF.Floor((zone.AnchorPos.Y - zone.Radius) / Program.TileSize) - 1;
            int maxTileY = (int)MathF.Ceiling((zone.AnchorPos.Y + zone.Radius) / Program.TileSize) + 1;

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    if (targetPos.HasValue)
                    {
                        int targetTileX = (int)MathF.Floor(targetPos.Value.X / Program.TileSize);
                        int targetTileY = (int)MathF.Floor(targetPos.Value.Y / Program.TileSize);
                        if (x != targetTileX || y != targetTileY)
                            continue;
                    }

                    int objectId = World.GetObjectIdAt(x, y);
                    var tileData = WorldTileRegistry.GetTile(objectId);
                    if (tileData == null || !tileData.IsCrop)
                        continue;

                    var cropData = World.GetCropDataAt(x, y);
                    if (cropData == null)
                        continue;

                    if (cropData.AccumulatedGrowthTime < tileData.GrowthTime)
                        continue;

                    int qty = Random.Shared.Next(tileData.HarvestMinQty, tileData.HarvestMaxQty + 1);
                    if (qty <= 0)
                        qty = 1;

                    int harvestItemId = GameData.GetItemId(tileData.HarvestItemId);
                    if (GameData.ItemDatabase.TryGetValue(harvestItemId, out var harvestData))
                    {
                        var harvestItem = new Item(harvestData.Name, qty, harvestData.Color, harvestData.Icon);
                        World.StackItemIntoContainer(Inventory, harvestItem, qty);
                    }

                    if (tileData.RegrowsAfterHarvest)
                    {
                        World.BeginCropRegrowth(x, y, tileData, cropData);
                    }
                    else
                    {
                        World.RemovePlacedObject(x, y);
                    }
                    return true;
                }
            }

            return false;
        }

        private bool TryPlantSeedInFieldZone(VillageWorkZone zone, Vector2? targetPos = null)
        {
            var seedSlot = Inventory.Slots.FirstOrDefault(slot =>
                !slot.IsEmpty && slot.Item != null &&
                GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var data) &&
                data.IsSeed && data.CropId != 0);

            if (seedSlot == null || seedSlot.Item == null)
                return false;

            int itemId = GameData.GetItemId(seedSlot.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var seedData) || seedData.CropId == 0)
                return false;

            int minTileX = (int)MathF.Floor((zone.AnchorPos.X - zone.Radius) / Program.TileSize) - 1;
            int maxTileX = (int)MathF.Ceiling((zone.AnchorPos.X + zone.Radius) / Program.TileSize) + 1;
            int minTileY = (int)MathF.Floor((zone.AnchorPos.Y - zone.Radius) / Program.TileSize) - 1;
            int maxTileY = (int)MathF.Ceiling((zone.AnchorPos.Y + zone.Radius) / Program.TileSize) + 1;

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    if (targetPos.HasValue)
                    {
                        int targetTileX = (int)MathF.Floor(targetPos.Value.X / Program.TileSize);
                        int targetTileY = (int)MathF.Floor(targetPos.Value.Y / Program.TileSize);
                        if (x != targetTileX || y != targetTileY)
                            continue;
                    }

                    int groundId = World.GetGroundTileIdAt(x, y);
                    if (groundId != 27 && groundId != 29)
                        continue;
                    if (World.GetObjectIdAt(x, y) != 0)
                        continue;
                    if (World.GetCropDataAt(x, y) != null)
                        continue;

                    World.PlantCrop(x, y, seedData.CropId, Program.GetGameTime());
                    seedSlot.Count -= 1;
                    if (seedSlot.Count <= 0)
                    {
                        seedSlot.Item = null;
                        seedSlot.Count = 0;
                    }
                    return true;
                }
            }

            return false;
        }

        //  Un villageois est "engagé" s'il est en pleine discussion : personne d'autre ne doit
        // venir se joindre à lui (sinon on retrouve des attroupements).
        private static bool IsEngagedInConversation(Entity e) =>
            e.IsTalking || e.CurrentTalkPartner != null;

        private Vector2 PickVillagerRoutineTarget()
        {
            if (IsCaveTentResident)
                return PickCaveTentRoutineTarget();

            const int maxAttempts = 10;
            const float crowdRadius = 80f;
            const float socialChance = 0.15f;

            Vector2 anchor = HomePosition != Vector2.Zero ? HomePosition : WorldPos;
            HashSet<string> destroyed = Program.GetDestroyedObjects();

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Vector2 candidate;
                float a = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);

                // Rejoindre quelqu'un : rare, seulement sur les premiers essais, et jamais un PNJ
                // déjà en conversation / chez lui / endormi.
                if (attempt < 3 && Random.Shared.NextDouble() < socialChance && TryPickSocialAnchor(out var friendPos))
                {
                    float r = 90f + (float)(Random.Shared.NextDouble() * 80f); // à côté, pas dessus
                    candidate = friendPos + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
                }
                else
                {
                    float r = 140f + (float)(Random.Shared.NextDouble() * 320f);
                    candidate = anchor + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
                }

                if (Vector2.DistanceSquared(candidate, anchor) < 90f * 90f) continue;
                if (!Pathfinder.IsWorldPositionWalkable(candidate, destroyed, Species)) continue;
                if (IsSpotCrowded(candidate, crowdRadius)) continue;

                return candidate;
            }

            // Repli : courte déambulation autour de la maison (jamais "rester planté").
            return PickWanderDestination(anchor, 60f, 150f);
        }

        // Choisit un villageois dehors, éveillé et libre (jamais en conversation ni chez lui).
        private bool TryPickSocialAnchor(out Vector2 pos)
        {
            const float socialRangeSq = 900f * 900f;
            Entity? picked = null;
            int seen = 0;
            var all = Program.entities;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (ReferenceEquals(e, this) || !e.IsAlive || !e.IsVillager || e.IsTamed) continue;
                if (e.HomePosition == Vector2.Zero || e._isSleeping) continue;
                if (IsEngagedInConversation(e)) continue;
                if (Vector2.DistanceSquared(WorldPos, e.WorldPos) >= socialRangeSq) continue;
                if (e.IsInsideAssignedHouse()) continue;

                seen++;
                if (Random.Shared.Next(seen) == 0) picked = e;
            }

            pos = picked?.WorldPos ?? Vector2.Zero;
            return picked != null;
        }

        // Vrai si le point est déjà occupé / visé par un autre villageois, ou s'il est trop près
        // d'une conversation en cours.
        private bool IsSpotCrowded(Vector2 point, float radius)
        {
            float rSq = radius * radius;
            float talkRadius = radius + 90f;
            float talkRSq = talkRadius * talkRadius;
            var all = Program.entities;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (ReferenceEquals(e, this) || !e.IsAlive || !e.IsVillager) continue;

                if (IsEngagedInConversation(e) && Vector2.DistanceSquared(point, e.WorldPos) < talkRSq)
                    return true;

                if (Vector2.DistanceSquared(point, e.WorldPos) < rSq)
                    return true;

                if ((e.AiState == NpcAiState.WalkToTarget || e.AiState == NpcAiState.GoToVillageCenter)
                    && Vector2.DistanceSquared(point, e.AiTarget) < rSq)
                    return true;
            }
            return false;
        }

        private bool HasCaveRouteTo(Vector2 destination)
        {
            if (Vector2.DistanceSquared(WorldPos, destination) < 55f * 55f)
                return true;

            return Pathfinder.FindPath(WorldPos, destination, Program.GetDestroyedObjects(), 12, Species).Count > 0;
        }

        private Vector2 PickCaveTentRoutineTarget()
        {
            const int maxAttempts = 12;
            int tileSize = Program.TileSize;
            HashSet<string> destroyed = Program.GetDestroyedObjects();

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float distance = 90f + (float)(Random.Shared.NextDouble() * 180f);
                Vector2 candidate = WorldPos + new Vector2(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance);
                int tileX = (int)MathF.Floor(candidate.X / tileSize);
                int tileY = (int)MathF.Floor(candidate.Y / tileSize);
                var ground = WorldTileRegistry.GetTile(World.GetGroundTileIdAt(tileX, tileY));
                if (ground == null || !ground.Walkable || World.GetObjectIdAt(tileX, tileY) != 0)
                    continue;
                if (!Pathfinder.IsWorldPositionWalkable(candidate, destroyed, Species))
                    continue;
                if (HasCaveRouteTo(candidate))
                    return candidate;
            }

            return WorldPos;
        }

        private void StartTalking(Entity? partner = null)
        {
            IsTalking = true;
            TalkTimer = 2.2f + (float)(Random.Shared.NextDouble() * 1.4f);
            TalkCooldown = 6f + (float)(Random.Shared.NextDouble() * 8f);

            var lines = new[]
            {
                GetRandomIdleTalkLine(),
                $"{(string.IsNullOrWhiteSpace(DisplayName) ? "Je" : DisplayName)} me disais juste que {SocialHabit}.",
                $"J'ai l'impression que {SocialRelation}. C'est étrange comme ça donne le rythme de la journée.",
                $"{(string.IsNullOrWhiteSpace(DisplayName) ? "On" : DisplayName)} dit souvent que {SocialDesire}, et je trouve que ça a du sens.",
                $"Il y a des jours où on a envie de parler sans rien de précis, juste pour se sentir moins seul.",
                $"Le village a son propre tempo. Parfois, il suffit d'écouter pour comprendre qui va bien et qui ne va pas.",
                $"Je suis {SocialTrait}, donc je prends mon temps avant de juger quoi que ce soit."
            };
            TalkText = lines[Random.Shared.Next(lines.Length)];

            //  Filet de sécurité : même si le partenaire semblait libre au moment de la sélection
            // (voir filtre plus haut), il a pu changer d'occupation entre-temps (ex : invité à
            // jouer la même frame). On ne le fait basculer en conversation que s'il peut lui aussi
            // revendiquer la priorité Social ; sinon ce PNJ parle simplement dans le vide.
            if (partner != null && partner.TryCommitGoal(NpcPriority.Social))
            {
                CurrentTalkPartner = partner;
                partner.IsTalking = true;
                partner.TalkTimer = TalkTimer;
                partner.TalkCooldown = TalkCooldown;
                partner.TalkText = TalkText;
                partner.CurrentTalkPartner = this;
            }
            else
            {
                CurrentTalkPartner = null;
            }
        }

        //  Appelé depuis World.DrawWorld, DANS le bloc BeginMode2D de la caméra (même endroit
        // que le dessin des hitboxes de debug) : on dessine donc directement en coordonnées
        // MONDE, Raylib applique lui-même la transformation caméra (zoom/offset/shake). Ne PAS
        // repasser par Raylib.GetWorldToScreen2D ici : ça double-transformerait les coordonnées
        // (positions fausses dès que la caméra est zoomée/dézoomée) puisqu'on est déjà dans un
        // repère caméra actif.
        public void DrawPath()
        {
            // Ligne rouge : trajectoire actuelle -> point de destination final (AiTarget).
            // C'est la ligne à surveiller pour repérer un PNJ qui coupe à travers un mur au
            // lieu de suivre le chemin calculé (le chemin réel suivi est en vert ci-dessous ;
            // s'il diverge nettement de la ligne rouge, ou si la ligne rouge traverse un mur
            // sans qu'un chemin vert la double, c'est le signe d'un bug de pathfinding).
            if (AiTarget != Vector2.Zero && AiTarget != WorldPos)
            {
                Raylib.DrawLineEx(WorldPos, AiTarget, 2, Color.Red);
                Raylib.DrawCircle((int)AiTarget.X, (int)AiTarget.Y, 5, Color.Red);
            }

            if (_currentPath.Count > 0)
            {
                //  Sur les états de coffre (GoToChestDeposit / GoToChestWithdraw), le chemin
                // A* peut être recalculé ou réinitialisé avant que la cible logique ne soit
                // totalement atteinte, ce qui laisse le jaune sur un ancien nœud de chemin au
                // lieu de la vraie destination actuelle. Pour le debug F3, on affiche
                // explicitement la cible logique de l'IA pour ces états, tout en laissant
                // la ligne verte représenter le chemin réellement calculé.
                bool useLogicalTargetForDebug = AiState == NpcAiState.GoToChestDeposit
                    || AiState == NpcAiState.GoToChestWithdraw;

                //  Le premier point de _currentPath est la position du PNJ au moment où le
                // chemin a été calculé, pas sa position actuelle (le PNJ a bougé depuis, parfois
                // beaucoup si _pathRecalcTimer n'a pas encore expiré). On trace donc explicitement
                // le premier segment depuis WorldPos (position réelle actuelle) vers ce premier
                // nœud, plutôt que de partir du nœud lui-même.
                Raylib.DrawLineEx(WorldPos, _currentPath[0], 3, Color.Green);
                for (int i = 0; i < _currentPath.Count - 1; i++)
                {
                    Raylib.DrawLineEx(_currentPath[i], _currentPath[i + 1], 3, Color.Green);
                }

                Vector2 debugEnd = useLogicalTargetForDebug && AiTarget != Vector2.Zero
                    ? AiTarget
                    : _currentPath[_currentPath.Count - 1];
                Raylib.DrawCircle((int)debugEnd.X, (int)debugEnd.Y, 5, Color.Yellow);
            }
        }

        //  Liste ordonnée (du moins important au plus important) de toutes les priorités
        // possibles de l'IA, avec un libellé court pour l'affichage debug F3. Construite une
        // seule fois (static readonly) plutôt qu'à chaque frame/chaque PNJ.
        private static readonly (int Priority, string Label)[] _priorityDebugOrder =
        {
            (NpcPriority.Idle,         "Idle"),
            (NpcPriority.Wander,       "Déambuler"),
            (NpcPriority.Routine,      "Routine"),
            (NpcPriority.Forage,       "Ramasser"),
            (NpcPriority.Food,         "Nourriture"),
            (NpcPriority.OwnerCommand, "Ordre proprio"),
            (NpcPriority.Social,       "Discuter"),
            (NpcPriority.GoHome,       "Rentrer"),
            (NpcPriority.Play,         "Jouer"),
            (NpcPriority.AlertFreeze,  "Alerte"),
            (NpcPriority.Combat,       "Combat"),
            (NpcPriority.Survival,     "Survie"),
        };

        //  Appelée depuis World.DrawWorld en mode debug (F3), juste à côté de DrawPath().
        // Affiche au-dessus de la tête du PNJ la pile des priorités possibles, triées du
        // MOINS important (en haut) au PLUS important (en bas, juste au-dessus de la tête)
        // comme demandé : ça donne d'un coup d'œil "où" se situe la décision actuelle du PNJ
        // dans l'échelle des priorités, et donc pourquoi il fait ce qu'il fait (ou pourquoi
        // il vient de le lâcher, cf. AbandonCurrentGoal / _lastAbandonedPriority).
        public void DrawPriorityDebugLabels()
        {
            var displayedPriorities = new List<(int Priority, string Label)>();
            int displayedCurrentPriority = GetDebugCurrentPriority();
            foreach (var priorityEntry in _priorityDebugOrder)
            {
                if (priorityEntry.Priority == displayedCurrentPriority
                    || _debugPlannedPriorities.Contains(priorityEntry.Priority))
                {
                    displayedPriorities.Add(priorityEntry);
                }
            }

            int ts = Program.TileSize;
            int tileX = (int)(WorldPos.X / ts);
            int tileY = (int)(WorldPos.Y / ts);
            int height = World.GetHeightAt(tileX, tileY);
            float yOffset = -height * ts / 4;
            Vector2 headPos = new Vector2(WorldPos.X, WorldPos.Y + yOffset - ts * 0.9f);

            const int fontSize = 11;
            const int lineHeight = 13;
            int totalLines = displayedPriorities.Count + 1; // +1 pour la ligne d'état (AiState)
            float startY = headPos.Y - totalLines * lineHeight;

            for (int i = 0; i < displayedPriorities.Count; i++)
            {
                var (priority, label) = displayedPriorities[i];
                bool isCurrent = priority == displayedCurrentPriority;
                Color color = isCurrent
                    ? new Color(80, 255, 120, 255)
                    : new Color(255, 220, 100, 255);
                string text = isCurrent ? $"> {label} ({priority})" : $"+ {label} ({priority})";

                float lineY = startY + i * lineHeight;
                int textWidth = Raylib.MeasureText(text, fontSize);
                Raylib.DrawText(text, (int)(headPos.X - textWidth / 2f), (int)lineY, fontSize, color);
            }

            // Dernière ligne, juste au-dessus de la tête : l'état concret en cours (AiState),
            // qui traduit la priorité choisie en une action réelle (aller au banc, fuir...).
            string stateText = $"[{AiState}]";
            int stateWidth = Raylib.MeasureText(stateText, fontSize);
            float stateY = startY + displayedPriorities.Count * lineHeight;
            Raylib.DrawText(stateText, (int)(headPos.X - stateWidth / 2f), (int)stateY, fontSize, Color.SkyBlue);
        }

        private int GetDebugCurrentPriority()
        {
            if (_currentGoalPriority != NpcPriority.Idle)
                return _currentGoalPriority;

            return AiState switch
            {
                NpcAiState.Wander => NpcPriority.Wander,
                NpcAiState.Flee => NpcPriority.Survival,
                NpcAiState.Chase => NpcPriority.Combat,
                NpcAiState.Follow => NpcPriority.OwnerCommand,
                NpcAiState.GoHome => NpcPriority.GoHome,
                NpcAiState.GoToInfirmary or NpcAiState.HealingAtInfirmary => NpcPriority.Medical,
                NpcAiState.GoToChestWithdraw => NpcPriority.Food,
                NpcAiState.GoToChestDeposit => NpcPriority.Routine,
                NpcAiState.GoToMerchant => NpcPriority.Food,
                NpcAiState.GoToGroundItem => NpcPriority.Forage,
                NpcAiState.WalkToTarget => NpcPriority.Routine,
                _ => NpcPriority.Idle
            };
        }

        public void DrawChargeBar()
        {
            if (!IsChargingAttack) return;

            int ts = Program.TileSize;
            int tileX = (int)(WorldPos.X / ts);
            int tileY = (int)(WorldPos.Y / ts);
            int height = World.GetHeightAt(tileX, tileY);
            float yOffset = -height * ts / 4;
            Vector2 visualPos = new Vector2(WorldPos.X, WorldPos.Y + yOffset);

            float barWidth = 50f;
            float barHeight = 6f;
            float barX = visualPos.X - barWidth / 2f;
            float barY = visualPos.Y - ts * 0.8f - 10f;

            // Fond
            Raylib.DrawRectangleRounded(
                new Rectangle(barX, barY, barWidth, barHeight),
                0.3f, 6,
                new Color(0, 0, 0, 180)
            );

            // Bordure
            Raylib.DrawRectangleRoundedLines(
                new Rectangle(barX, barY, barWidth, barHeight),
                0.3f, 6, 1,
                new Color(100, 100, 100, 200)
            );

            float progress = GetChargeProgress();
            if (progress > 0.01f)
            {
                float pulse = 0.8f + 0.2f * MathF.Sin((float)Raylib.GetTime() * 12f);
                Color chargeColor = new Color(
                    (byte)(200 * pulse),
                    (byte)(180 * pulse),
                    (byte)(50 * pulse),
                    (byte)255
                );

                Raylib.DrawRectangleRounded(
                    new Rectangle(barX + 2, barY + 2, (barWidth - 4) * progress, barHeight - 4),
                    0.2f, 6,
                    chargeColor
                );
            }
        }

        // ==================== UPDATE ====================
        /// <summary>
        /// Détermine où ce PNJ doit regarder (pupilles + sourcils), selon un ordre de priorité :
        /// 1. En train de parler à quelqu'un (joueur ou autre PNJ) -> regarde cet interlocuteur.
        /// 2. En train de se déplacer -> regarde dans la direction de son trajet.
        /// 3. Sinon (immobile, ne fait rien) -> regarde occasionnellement autour de lui,
        ///    éventuellement vers une entité qui passe à proximité.
        /// </summary>
        private void UpdateGaze(float dt, Vector2 playerPos)
        {
            //  OPTIMISATION PERF : le calcul fin du regard (pupilles/sourcils qui suivent une
            // cible) est purement cosmétique et coûte plusieurs trigonométries par oeil, par PNJ,
            // par frame. Un PNJ très loin du joueur (donc hors écran dans un grand village) n'a
            // pas besoin de cette précision : on garde le regard neutre et on sort tout de suite.
            const float GAZE_DETAIL_RANGE = 900f;
            if (Vector2.DistanceSquared(WorldPos, playerPos) > GAZE_DETAIL_RANGE * GAZE_DETAIL_RANGE)
            {
                PupilLeftOffset = Vector2.Zero;
                PupilRightOffset = Vector2.Zero;
                EyebrowLeftOffset = Vector2.Zero;
                EyebrowRightOffset = Vector2.Zero;
                return;
            }

            Vector2? gazeTarget = null;

            if (IsTalking)
            {
                if (CurrentTalkPartner != null)
                    gazeTarget = Program.GetPlayerVisualPosition(CurrentTalkPartner.WorldPos);
                else
                    gazeTarget = Program.GetPlayerVisualPosition(playerPos); // parle au joueur
            }
            else if (AnimState == "walk" && AiTarget != Vector2.Zero && AiTarget != WorldPos)
            {
                // Regarde un peu en avant sur son chemin
                Vector2 dir = AiTarget - WorldPos;
                if (dir.LengthSquared() > 0.01f)
                {
                    dir = Vector2.Normalize(dir);
                    gazeTarget = WorldPos + dir * 80f;
                }
            }
            else
            {
                // Immobile / inactif : glances aléatoires autour de lui, ou vers une entité qui passe
                _idleLookTimer -= dt;
                if (_idleLookTimer <= 0f)
                {
                    _idleLookTimer = 2.5f + (float)_gazeRandom.NextDouble() * 4.5f; // 2.5 à 7s entre chaque glance

                    //  OPTIMISATION PERF : le scan "qui passe à proximité ?" coûte O(nombre
                    // d'entités) et n'a d'intérêt visuel que pour les PNJ que le joueur regarde
                    // réellement. Loin de la caméra/joueur, on se contente d'un regard aléatoire
                    // (beaucoup moins cher) plutôt que de scanner toutes les entités du monde —
                    // utile dès qu'un village compte plusieurs dizaines d'habitants.
                    const float PASSERBY_SCAN_RANGE = 500f;
                    Entity? passerby = null;
                    if (Vector2.DistanceSquared(WorldPos, playerPos) < PASSERBY_SCAN_RANGE * PASSERBY_SCAN_RANGE)
                    {
                        float bestDistSq = 220f * 220f;
                        foreach (var other in Program.entities)
                        {
                            if (ReferenceEquals(other, this) || !other.IsAlive) continue;
                            float dSq = Vector2.DistanceSquared(WorldPos, other.WorldPos);
                            if (dSq < bestDistSq)
                            {
                                bestDistSq = dSq;
                                passerby = other;
                            }
                        }
                    }

                    if (passerby != null && _gazeRandom.NextDouble() < 0.6)
                    {
                        _idleLookTarget = passerby.WorldPos;
                    }
                    else
                    {
                        float angle = (float)(_gazeRandom.NextDouble() * Math.PI * 2.0);
                        _idleLookTarget = WorldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 60f;
                    }
                }
                gazeTarget = _idleLookTarget;
            }

            if (!gazeTarget.HasValue)
            {
                PupilLeftOffset = Vector2.Zero;
                PupilRightOffset = Vector2.Zero;
                EyebrowLeftOffset = Vector2.Zero;
                EyebrowRightOffset = Vector2.Zero;
                return;
            }

            Vector2 headWorldPos = WorldPos;
            Vector2 leftEyeWorldPos = headWorldPos + new Vector2(-Program.EYE_LOCAL_OFFSET_X * Facing, Program.EYE_LOCAL_OFFSET_Y);
            Vector2 rightEyeWorldPos = headWorldPos + new Vector2(Program.EYE_LOCAL_OFFSET_X * Facing, Program.EYE_LOCAL_OFFSET_Y);

            PupilLeftOffset = Program.ComputeGazeOffset(leftEyeWorldPos, gazeTarget.Value, Program.PUPIL_MAX_OFFSET_X, Program.PUPIL_MAX_OFFSET_Y);
            PupilRightOffset = Program.ComputeGazeOffset(rightEyeWorldPos, gazeTarget.Value, Program.PUPIL_MAX_OFFSET_X, Program.PUPIL_MAX_OFFSET_Y);

            float browMaxY = Program.PUPIL_MAX_OFFSET_Y * Program.EYEBROW_Y_INTENSITY_FACTOR;
            EyebrowLeftOffset = new Vector2(0f, Program.ComputeGazeOffset(leftEyeWorldPos, gazeTarget.Value, 0f, browMaxY).Y);
            EyebrowRightOffset = new Vector2(0f, Program.ComputeGazeOffset(rightEyeWorldPos, gazeTarget.Value, 0f, browMaxY).Y);
        }

        public bool ShouldRunFullAiUpdate(Vector2 playerPos, int updateIndex)
        {
            if (IsPlayer || IsPetCompanion || IsBoss)
                return true;

            if (IsTalking || IsAlerted || ShowAlertIcon || CurrentTalkPartner != null)
                return true;

            if (AiState == NpcAiState.Chase || AiState == NpcAiState.Flee || AiState == NpcAiState.Follow ||
                AiState == NpcAiState.GoHome || AiState == NpcAiState.GoToInfirmary ||
                AiState == NpcAiState.HealingAtInfirmary || AiState == NpcAiState.GoToChestWithdraw ||
                AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToMerchant ||
                AiState == NpcAiState.GoToGroundItem || AiState == NpcAiState.GoToVillageCenter)
            {
                return true;
            }

            if (_currentGoalPriority >= NpcPriority.Combat)
                return true;

            float distSq = Vector2.DistanceSquared(WorldPos, playerPos);
            //  CORRECTIF : 700px (~17 tuiles) ne couvrait qu'une fraction de la zone réellement
            // affichée à l'écran (jusqu'à MAX_RENDER_TILES_X = 120 tuiles de large côté rendu,
            // voir World_Core.cs). Des PNJ parfaitement visibles, mais au-delà de 700px du
            // joueur, tombaient donc dans le mode dégradé (1 update sur 4) et paraissaient figés
            // - le simple fait de s'approcher les "débloquait" en les faisant repasser sous le
            // seuil. 2200px (~55 tuiles) couvre confortablement la zone visible dans les
            // résolutions/zooms courants tout en gardant l'optimisation utile pour les PNJ
            // vraiment hors champ (fond de village, zones adjacentes non regardées).
            const float NEARBY_NPC_AI_DISTANCE_SQ = 2200f * 2200f;
            if (distSq <= NEARBY_NPC_AI_DISTANCE_SQ)
                return true;

            //  Budget important : les PNJ lointains et inactifs ne recalculent pas tout leur AI
            // chaque frame. On laisse le moteur se concentrer sur les PNJ proches du joueur ou
            // déjà engagés dans une action urgente, ce qui coupe fortement les pics de coût quand
            // le village est rempli de dizaines d'habitants.
            return (updateIndex & 3) == 0;
        }

        public void Update(float dt, HashSet<string> destroyed, Vector2 playerPos, int playerHeldItemId)
        {
            _debugPlannedPriorities.Clear();

            //  Filet de sécurité : si la monture est morte/a disparu pendant qu'on la chevauchait
            // (ex : tuée par le joueur sous son cavalier), on remet le PNJ à pied immédiatement
            // plutôt que de le laisser courir à la vitesse d'une monture qui n'existe plus.
            if (IsRidingMount && (OwnedMount == null || !OwnedMount.IsAlive))
                DismountOwnedMount();

            if (Species == "human" || Species == "goblin")
                UpdateGaze(dt, playerPos);

            //  Vol des oiseaux : indépendant de l'état d'IA courant (Flee, Wander, Chase...),
            // pour qu'un oiseau parti en l'air continue son vol/atterrissage même s'il change
            // d'état entre-temps (ex : il finit de fuir puis repart en Wander avant d'atterrir).
            if (IsBird)
                UpdateBirdFlight(dt, destroyed);

            if (DamageFlash > 0f)
            {
                DamageFlash -= dt;
                if (DamageFlash < 0f) DamageFlash = 0f;
            }

            //  POISON : tic régulier tant que l'entité est vivante et empoisonnée (voir
            // ApplyPoison, déclenché entre autres par les nuages de spores du bolet).
            if (_poisonTimer > 0f && IsAlive)
            {
                _poisonTimer = Math.Max(0f, _poisonTimer - dt);
                _poisonTickAccumulator += dt;
                if (_poisonTickAccumulator >= Program.POISON_TICK_INTERVAL)
                {
                    int ticks = (int)(_poisonTickAccumulator / Program.POISON_TICK_INTERVAL);
                    _poisonTickAccumulator -= ticks * Program.POISON_TICK_INTERVAL;
                    for (int i = 0; i < ticks && IsAlive; i++)
                    {
                        int poisonDamage = (int)Program.GetPoisonDamagePerTick();
                        CurrentHP -= poisonDamage;
                        OnHit();
                        Program.AddFloatingDamage(new Vector2(WorldPos.X, WorldPos.Y - 20f), poisonDamage, false, isEnemy: true);
                    }
                }
                if (_poisonTimer <= 0f)
                {
                    _poisonTimer = 0f;
                    _poisonTickAccumulator = 0f;
                }
            }

            UpdateBlink(dt);
            UpdateAlertSignal(dt, playerPos);

            if (_infirmaryRetryTimer > 0f)
                _infirmaryRetryTimer = Math.Max(0f, _infirmaryRetryTimer - dt);
            if (IsVillager && !IsTamed && IsAlive && CurrentHP < MaxHP
                && _infirmaryBuildingId < 0 && _infirmaryRetryTimer <= 0f)
                TryStartInfirmaryCare();

            if (!IsPlayer && IsAlive)
            {
                if (string.Equals(Species, "Pig", StringComparison.OrdinalIgnoreCase))
                {
                    _poopDropTimer -= dt;
                    if (_poopDropTimer <= 0f)
                    {
                        _poopDropTimer = POOP_DROP_INTERVAL + (float)(Random.Shared.NextDouble() * 4f);
                        if (Random.Shared.NextDouble() < POOP_DROP_CHANCE)
                        {
                            Vector2 dropPos = new Vector2(WorldPos.X, WorldPos.Y + 12f);
                            Program.DropItemOnGround(dropPos, GameData.GetItemId("poo"), 1);
                        }
                    }
                }
                else if (string.Equals(Species, "Chicken", StringComparison.OrdinalIgnoreCase))
                {
                    _eggDropTimer -= dt;
                    if (_eggDropTimer <= 0f)
                    {
                        _eggDropTimer = EGG_DROP_INTERVAL + (float)(Random.Shared.NextDouble() * 4f);
                        if (Random.Shared.NextDouble() < EGG_DROP_CHANCE)
                        {
                            Vector2 dropPos = new Vector2(WorldPos.X, WorldPos.Y + 12f);
                            Program.DropItemOnGround(dropPos, GameData.GetItemId("egg"), 1);
                        }
                    }
                }
            }

            //  Position du propriétaire AVANT que playerPos ne soit éventuellement remplacée par
            // celle d'une cible de combat (utilisée par les ordres des animaux apprivoisés).
            Vector2 petOwnerPos = playerPos;
            bool isPlayerOwnedPet = IsTamed && OwnerNpcId == null && !IsHostilePet && !IsPetCompanion;

            //  CORRECTIF : un apprivoisé n'a le droit de riposter/poursuivre QUE dans son mode
            // Attack. Sinon une riposte armée avant un ordre Follow/Stay/Roam le laissait
            // continuer la poursuite (ordre ignoré).
            if (IsTamed && TamedBehavior != TamedAnimalMode.Attack && _retaliationTimer > 0f)
            {
                _retaliationTimer = 0f;
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = false;
                _fearTargetConnectionId = -1;
            }
            if (isPlayerOwnedPet && _retaliateAgainstPlayer)
            {
                _retaliationTimer = 0f;
                _retaliateAgainstPlayer = false;
                _fearTargetConnectionId = -1;
            }

            if (_retaliationTimer > 0f)
                _retaliationTimer = Math.Max(0f, _retaliationTimer - dt);

            bool isRetaliating = _retaliationTimer > 0f;
            _combatTargetEntity = null;
            if (isRetaliating && _retaliateAgainstPlayer)
            {
                playerPos = GetFearTargetPosition();
                AiTargetConnectionId = _fearTargetConnectionId;
                if (Vector2.DistanceSquared(WorldPos, playerPos) > RETALIATION_RANGE * RETALIATION_RANGE)
                {
                    _retaliationTimer = 0f;
                    _retaliateAgainstPlayer = false;
                    _fearTargetConnectionId = -1;
                    isRetaliating = false;
                }
                else
                {
                    _retaliationTimer = RETALIATION_DURATION;
                }
            }
            else if (isRetaliating && _retaliationTargetEntity != null && _retaliationTargetEntity.IsAlive
                && Vector2.DistanceSquared(WorldPos, _retaliationTargetEntity.WorldPos) <= RETALIATION_RANGE * RETALIATION_RANGE)
            {
                _combatTargetEntity = _retaliationTargetEntity;
                _retaliationTimer = RETALIATION_DURATION;
            }
            else if (isRetaliating)
            {
                _retaliationTimer = 0f;
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = false;
                isRetaliating = false;
            }

            //  CORRECTIF : mode Attack d'un animal du joueur. Avant, RIEN ne lui choisissait de
            // cible : shouldChase devenait vrai dès que le joueur était dans sa vision, et comme
            // _combatTargetEntity restait null, ChaseBehavior/MeleeCombat visaient... le joueur.
            bool petAttackActive = isPlayerOwnedPet && TamedBehavior == TamedAnimalMode.Attack;
            if (petAttackActive && _combatTargetEntity == null)
            {
                _combatTargetEntity = FindPlayerPetTarget(petOwnerPos, dt);
                if (_combatTargetEntity != null)
                    playerPos = _combatTargetEntity.WorldPos;
            }

            bool isGoblin = string.Equals(Species, "goblin", StringComparison.OrdinalIgnoreCase) && !IsTamed;
            if (!isRetaliating && (IsVillageGuard || IsSpiritAnimal || isGoblin))
            {
                float targetRange = IsVillageGuard || IsSpiritAnimal ? 800f : 500f;
                float nearestTargetDistanceSq = targetRange * targetRange;
                Vector2 spiritTotemPosition = new((SpiritTotemTileX + 0.5f) * Program.TileSize, (SpiritTotemTileY + 0.5f) * Program.TileSize);
                bool IsVillageThreat(Entity target) => target.Behavior == "hostile"
                    && !string.Equals(target.Species, "human", StringComparison.OrdinalIgnoreCase)
                    && target.CombatTargetEntity?.IsVillager == true
                    && (!IsSpiritAnimal || (SpiritTotemTileX != int.MinValue && SpiritTotemTileY != int.MinValue
                        && Vector2.DistanceSquared(spiritTotemPosition, target.WorldPos) <= targetRange * targetRange));

                if (_hostileTargetEntity != null)
                {
                    Entity lockedTarget = _hostileTargetEntity;
                    bool isValidLockedTarget = lockedTarget.IsAlive && !lockedTarget.IsTamed && !lockedTarget.IsPlayer
                        && (IsVillageGuard || IsSpiritAnimal
                            ? IsVillageThreat(lockedTarget)
                            : lockedTarget.IsVillager);
                    float lockedTargetDistanceSq = Vector2.DistanceSquared(WorldPos, lockedTarget.WorldPos);

                    if (isValidLockedTarget && lockedTargetDistanceSq <= nearestTargetDistanceSq)
                    {
                        _combatTargetEntity = lockedTarget;
                        nearestTargetDistanceSq = lockedTargetDistanceSq;
                    }
                    else
                    {
                        _hostileTargetEntity = null;
                    }
                }

                if (_combatTargetEntity == null)
                {
                    foreach (var candidate in Program.GetEntities())
                    {
                        if (ReferenceEquals(candidate, this) || !candidate.IsAlive || candidate.IsTamed || candidate.IsPlayer)
                            continue;

                        bool isValidTarget = IsVillageGuard || IsSpiritAnimal
                            ? IsVillageThreat(candidate)
                            : candidate.IsVillager;
                        if (!isValidTarget) continue;

                        float candidateDistanceSq = Vector2.DistanceSquared(WorldPos, candidate.WorldPos);
                        if (candidateDistanceSq >= nearestTargetDistanceSq) continue;

                        nearestTargetDistanceSq = candidateDistanceSq;
                        _combatTargetEntity = candidate;
                        _hostileTargetEntity = candidate;
                    }
                }

                if (_combatTargetEntity != null)
                    playerPos = _combatTargetEntity.WorldPos;
            }

            float distToPlayerSq = Vector2.DistanceSquared(WorldPos, playerPos);

            if (IsBoss && !IsPlayer && !IsTamed)
            {
                Behavior = "hostile";
                if (VisionRange <= 0)
                    VisionRange = 3;
                if (AlertDuration <= 0f)
                    AlertDuration = 2f;
            }

            float darkness = Program.GetDarknessAlpha();
            UpdateSleepState(darkness, dt);

            // Gestion du sommeil
            if (_isSleeping)
            {
                const float SLEEP_ANIM_SPEED = 0.5f;
                AnimProgress += dt * SLEEP_ANIM_SPEED;
                if (AnimProgress >= 1f)
                {
                    AnimProgress = 0f;
                    int frameCount = 8;
                    if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
                    {
                        var firstPart = skeleton.FirstOrDefault();
                        if (firstPart != null && firstPart.Animations.TryGetValue("sleep", out var frames) && frames.Count > 0)
                            frameCount = frames.Count;
                    }
                    CurrentFrame = (CurrentFrame + 1) % frameCount;
                }

                _zzzTimer += dt;
                if (_zzzTimer >= _zzzCurrentInterval)
                {
                    _zzzTimer = 0f;
                    SpawnZzzParticle();
                }
                return;
            }

            // Dans une grotte, un humain sans maison peut s'attribuer une tente proche.
            // Les anciens PNJ générés avant cette règle peuvent aussi être réparés ici.
            if (!IsPlayer && Species == "human" && !IsTamed && World.IsUnderground
                && (HomeBuildingId != TentHomeBuildingId || TentPosition == Vector2.Zero || HomePosition == Vector2.Zero)
                && (HomeBuildingId < 0 || !World.Houses.ContainsKey(HomeBuildingId)))
            {
                Vector2? tentHome = World.FindNearestTentHome(WorldPos, 12);
                Vector2? tentSleepPosition = World.FindNearestTentSleepPosition(WorldPos, 12);
                if (tentHome.HasValue)
                {
                    HomePosition = tentHome.Value;
                    TentPosition = tentSleepPosition ?? tentHome.Value;
                    HomeBuildingId = TentHomeBuildingId;
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                }
                else
                {
                    // Une ancienne position de maison ne doit pas rendre le PNJ prisonnier
                    // d'un trajet impossible lorsqu'il n'existe aucun abri souterrain proche.
                    HomePosition = Vector2.Zero;
                }
            }

            bool hasHomeAssignment = HomeBuildingId >= 0
                && World.Houses.ContainsKey(HomeBuildingId)
                && World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var assignedSpots)
                && assignedSpots.Any(spot => Vector2.DistanceSquared(spot.WorldPos, HomePosition) < 16f);

            // Une maison pleine n'est pas une maison invalide pour le PNJ déjà logé dedans.
            // La saturation ne doit empêcher QUE l'attribution d'un nouveau PNJ, pas forcer
            // le relogement du PNJ actuel. On revalide seulement la case assignée / la maison
            // elle-même, pas la capacité globale de l'édifice.
            if (!hasHomeAssignment || HomePosition == Vector2.Zero)
            {
                if (HomeBuildingId >= 0 && World.Houses.ContainsKey(HomeBuildingId) && HomePosition != Vector2.Zero)
                {
                    HomePosition = Vector2.Zero;
                }
                else
                {
                    HomePosition = Vector2.Zero;
                    HomeBuildingId = -1;
                }
            }

            // Si un humain de surface sans foyer trouve une maison libre, il décide d'y habiter.
            if (!IsPlayer && Species == "human" && !IsTamed && !World.IsUnderground && HomePosition == Vector2.Zero)
            {
                _homeSearchTimer -= dt;
                if (_homeSearchTimer <= 0f)
                {
                    _homeSearchTimer = 4f + (float)(Random.Shared.NextDouble() * 6f);
                    var foundHome = World.FindNearestFreeHouseSpot(WorldPos, 2400f);
                    if (foundHome.HasValue)
                    {
                        HomePosition = foundHome.Value.spotPos;
                        HomeBuildingId = foundHome.Value.buildingId;
                        AiState = NpcAiState.GoHome;
                        AiTarget = GetHomeEntryTarget();
                        _currentPath.Clear();
                        _pathRecalcTimer = 0f;
                        AnimState = "walk";
                        Speed = BaseSpeed;
                    }
                }
            }

            // Knockback
            if (IsKnockedBack && KnockbackVelocity.LengthSquared() > 0.01f)
            {
                Vector2 move = KnockbackVelocity * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;
                KnockbackVelocity *= 0.85f;
                if (KnockbackVelocity.LengthSquared() < 5f)
                {
                    IsKnockedBack = false;
                    KnockbackVelocity = Vector2.Zero;
                }

                float kbAnimSpeed = 8f;
                AnimProgress += dt * kbAnimSpeed;
                if (AnimProgress >= 1f)
                {
                    AnimProgress = 0f;
                    CurrentFrame = (CurrentFrame + 1) % 8;
                }
                // Allow the AI and other update logic to continue while knocked back.
                // Previously the method returned here which could permanently stun
                // an entity under rapid repeated hits. Removing the return lets
                // the entity resume behaviors (movement/attacks/targets) even
                // while knockback velocities are applied.
            }

            //  COMPAGNON D'ARMURE : IA totalement séparée (suit le joueur / attaque
            // l'agresseur signalé par Program.NotifyPlayerAttackedByEntity), on ne passe
            // donc jamais par le reste de la logique villageois/hostile/apprivoisé ci-dessous.
            if (IsPetCompanion)
            {
                UpdatePetCompanionAI(dt, destroyed, playerPos);
                return;
            }

            // Comportement insectes volants (à conserver)
            var speciesInfoForFlight = SpeciesData.GetSpeciesInfo(Species);
            if (speciesInfoForFlight != null && speciesInfoForFlight.CanBeCaught)
            {
                // ... votre code existant pour les insectes ...
                // (je ne l'ai pas réécrit ici pour garder le code lisible)
            }

            // Croissance des bébés
            if (IsBaby && !_isSleeping)
            {
                Age += dt;
                if (Age >= GrowthTime)
                {
                    IsBaby = false;
                    Scale = 1.0f;
                    var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
                    if (speciesInfo != null)
                    {
                        MaxHP = speciesInfo.MaxHp;
                        CurrentHP = MaxHP;
                        Attack = speciesInfo.Attack;
                        Speed = speciesInfo.Speed;
                        BaseSpeed = speciesInfo.Speed;
                        RunSpeed = speciesInfo.RunSpeed;
                    }
                }
            }

            if (IsTalking)
            {
                TalkTimer -= dt;
                if (TalkTimer <= 0f)
                {
                    IsTalking = false;
                    TalkTimer = 0f;
                    TalkText = null;
                    if (CurrentTalkPartner != null && CurrentTalkPartner.CurrentTalkPartner == this)
                    {
                        CurrentTalkPartner.IsTalking = false;
                        CurrentTalkPartner.TalkTimer = 0f;
                        CurrentTalkPartner.TalkText = null;
                        CurrentTalkPartner.CurrentTalkPartner = null;
                    }
                    CurrentTalkPartner = null;
                }
            }
            else
            {
                TalkCooldown -= dt;
                //  IMPORTANT : le "TryCommitGoal(NpcPriority.Social)" ci-dessous est ce qui empêche
                // cette envie spontanée de discuter d'écraser un objectif plus important déjà en
                // cours (ex : un PNJ invité à jouer une partie de Pouilleux, qui détient la
                // priorité Play, largement au-dessus de Social) — avant ce correctif, ce bloc
                // modifiait AiState sans aucune vérification et pouvait donc faire décrocher un
                // PNJ en plein milieu d'une invitation, même juste après avoir cliqué dessus.
                if (TalkCooldown <= 0f && !IsTamed && Species == "human" && !IsPlayer && HomePosition != Vector2.Zero
                    && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToChestWithdraw
                    && TryCommitGoal(NpcPriority.Social))
                {
                    const float talkDistance = 70f;
                    Entity? closestTalkTarget = null;
                    float closestDistSq = talkDistance * talkDistance;

                    foreach (var other in Program.entities)
                    {
                        if (!ReferenceEquals(other, this) && other.Species == "human" && !other.IsPlayer && !other.IsTamed &&
                            !other.IsTalking && other.HomePosition != Vector2.Zero &&
                            other.CurrentTalkPartner == null &&
                            other.CurrentGoalPriority < NpcPriority.Social) // ne pas déranger un PNJ déjà occupé par plus important (jouer, rentrer, etc.)
                        {
                            float distSq = Vector2.DistanceSquared(WorldPos, other.WorldPos);
                            if (distSq < closestDistSq)
                            {
                                closestDistSq = distSq;
                                closestTalkTarget = other;
                            }
                        }
                    }

                    if (closestTalkTarget != null)
                    {
                        float dist = Vector2.Distance(WorldPos, closestTalkTarget.WorldPos);
                        if (dist <= talkDistance)
                        {
                            StartTalking(closestTalkTarget);
                        }
                        else
                        {
                            AiState = NpcAiState.WalkToTarget;
                            AiTarget = closestTalkTarget.WorldPos;
                            _currentPath.Clear();
                            AnimState = "walk";
                            Speed = BaseSpeed;
                        }
                    }
                    else if (Random.Shared.NextDouble() < 0.002f)
                    {
                        StartTalking();
                    }
                    else
                    {
                        _debugPlannedPriorities.Remove(NpcPriority.Social);
                    }
                }
            }

            // Comportement des villageois
            if (!IsPlayer && Species == "human" && !IsTamed)
            {
                bool hasHome = HomePosition != Vector2.Zero;
                bool isNight = IsCaveTentResident ? _tentRestCooldown <= 0f : darkness > 0.6f;
                float distToHome = hasHome ? Vector2.Distance(WorldPos, HomePosition) : float.MaxValue;
                bool isTentHome = HomeBuildingId == TentHomeBuildingId;
                bool isAtHome = hasHome && (isTentHome
                    ? distToHome < 25f
                    : IsInsideAssignedHouse());

                // Un item peut être ajouté à l'inventaire après le passage initial au coffre
                // (par exemple quand le joueur donne des objets au PNJ pendant la journée).
                // Dans ce cas la tâche du soir doit redevenir active immédiatement.
                if (_hasDepositedTonight && World.HasAnyDepositableItem(this) && _chestDepositRetryCooldown <= 0f)
                    _hasDepositedTonight = false;

                //  Une tente sert de lieu de repos périodique, pas de prison nocturne : après
                // s'être reposé, le PNJ peut explorer la grotte et parler aux autres membres
                // du camp jusqu'à sa prochaine fenêtre de sommeil.
                if (isTentHome && isNight && _tentRestCooldown <= 0f && !isAtHome)
                {
                    if (AiState != NpcAiState.GoHome)
                    {
                        ForceGoal(NpcPriority.GoHome);
                        AiState = NpcAiState.GoHome;
                        _currentPath.Clear();
                        _pathRecalcTimer = 0f;
                        AnimState = "walk";
                        Speed = BaseSpeed;
                    }
                    AiTarget = GetHomeEntryTarget();
                }

                {
                    //  RAMASSAGE PASSIF : indépendant du système de priorités (voir commentaire sur
                    // _passivePickupScanTimer). Se déclenche systématiquement, peu importe l'AiState
                    // en cours (discussion, routine, trajet actif...), ce qui garantit qu'un PNJ ne
                    // reste jamais planté à côté d'un objet ramassable sans le prendre.
                    _passivePickupScanTimer -= dt;
                    if (_passivePickupScanTimer <= 0f)
                    {
                        _passivePickupScanTimer = 0.25f;
                        Program.TryNpcAttractNearby(this, GroundItem.DefaultPickupRadius);
                        Program.TryNpcPassivePickupNearby(this, 40f);
                    }

                    //  SYSTÈME DE FAIM : monte lentement avec le temps (~11 min pour atteindre le
                    // seuil avec les valeurs par défaut). Uniquement pour les villageois non apprivoisés
                    // (les compagnons du joueur ne gèrent pas leur propre faim ici).
                    Hunger = Math.Min(HUNGER_MAX, Hunger + dt * 0.15f);

                    //  Priorité Food (50) : passe devant la routine (40) mais cède face à une discussion
                    // en cours (Social, 70) ou tout ce qui est plus urgent (combat, fuite...).
                    if (Hunger >= HUNGER_THRESHOLD_SEEK_FOOD
                        && AiState != NpcAiState.GoToChestWithdraw && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToMerchant
                        && TryCommitGoal(NpcPriority.Food))
                    {
                        bool foodGoalActive = false;
                        var carriedFood = Inventory.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item != null
                            && GameData.ItemDatabase.TryGetValue(GameData.GetItemId(s.Item.Name), out var d) && d.Type == ItemType.Food);
                        if (carriedFood != null)
                        {
                            Console.WriteLine($"[NPC-CHEST] EAT npc={NetId} item={carriedFood.Item!.Name} countBefore={carriedFood.Count}");
                            Hunger = 0f;
                            carriedFood.Count -= 1;
                            if (carriedFood.Count <= 0) { carriedFood.Item = null; carriedFood.Count = 0; }
                        }
                        else if (HomeBuildingId >= 0 && World.HouseChestHasFood(HomeBuildingId))
                        {
                            var chestApproachPos = World.GetHouseChestApproachPosition(HomeBuildingId, this);
                            if (chestApproachPos.HasValue)
                            {
                                AiState = NpcAiState.GoToChestWithdraw;
                                AiTarget = chestApproachPos.Value;
                                _currentPath.Clear();
                                AnimState = "walk";
                                Speed = BaseSpeed;
                                foodGoalActive = true;
                            }
                        }
                        else if (darkness <= 0.6f)
                        {
                            var merchant = World.FindNearestFoodMerchant(WorldPos, 2000f);
                            if (merchant != null)
                            {
                                AiState = NpcAiState.GoToMerchant;
                                AiTarget = merchant.WorldPos;
                                _targetMerchantNetId = merchant.NetId;
                                _currentPath.Clear();
                                AnimState = "walk";
                                Speed = BaseSpeed;
                                foodGoalActive = true;
                            }
                        }
                        if (!foodGoalActive)
                        {
                            _debugPlannedPriorities.Remove(NpcPriority.Food);
                            _currentGoalPriority = NpcPriority.Idle;
                            _goalLockTimer = 0f;
                        }
                    }

                    //  SYSTÈME DE RAMASSAGE : scan périodique des objets au sol à proximité.
                    _forageScanTimer -= dt;
                    if (_forageScanTimer <= 0f)
                    {
                        _forageScanTimer = 1.5f + (float)Random.Shared.NextDouble();
                        if (AiState != NpcAiState.GoToGroundItem && AiState != NpcAiState.GoToChestDeposit)
                        {
                            //  IMPORTANT : on cherche l'objet AVANT de revendiquer la priorité.
                            // Revendiquer Forage (qui doit rester au-dessus de Routine pour
                            // pouvoir l'interrompre quand il y a vraiment quelque chose à
                            // ramasser) alors qu'aucun objet n'est trouvé laissait
                            // _currentGoalPriority bloqué sur Forage sans aucun objectif actif :
                            // Routine ne pouvait plus jamais reprendre la main (40 < 45), et le
                            // PNJ restait Idle en permanence sans pouvoir aller au coffre.
                            var groundItem = Program.FindNearestGroundItemForNpc(WorldPos, 350f, this);
                            if (groundItem != null && TryCommitGoal(NpcPriority.Forage))
                            {
                                AiState = NpcAiState.GoToGroundItem;
                                AiTarget = groundItem.Position;
                                _targetGroundItemNetId = groundItem.NetId;
                                _currentPath.Clear();
                                AnimState = "walk";
                                Speed = BaseSpeed;
                            }
                        }
                    }

                    if (AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToChestWithdraw
                        && CurrentTalkPartner != null && CurrentTalkPartner.IsAlive && TryCommitGoal(NpcPriority.Social))
                    {
                        float distToTalkPartner = Vector2.Distance(WorldPos, CurrentTalkPartner.WorldPos);
                        if (distToTalkPartner > 55f)
                        {
                            AiState = NpcAiState.WalkToTarget;
                            AiTarget = CurrentTalkPartner.WorldPos;
                            _currentPath.Clear();
                            AnimState = "walk";
                            Speed = BaseSpeed;
                        }
                        else if (distToTalkPartner <= 55f && !IsTalking)
                        {
                            StartTalking(CurrentTalkPartner);
                        }
                    }
                    else if (hasHome && CurrentTalkPartner == null && TryCommitGoal(NpcPriority.Routine))
                    {
                        float distToHomeInRoutine = Vector2.Distance(WorldPos, HomePosition);
                        bool isRoutineAtHome = isTentHome
                            ? distToHomeInRoutine < 25f
                            : isAtHome;

                        if (isRoutineAtHome)
                        {
                            if (!_hasDepositedTonight && HomeBuildingId >= 0 && AiState != NpcAiState.GoToChestDeposit
                                && _chestDepositRetryCooldown <= 0f)
                            {
                                if (World.HasAnyDepositableItem(this))
                                {
                                    QueueRoutineTask(RoutineTaskKind.DepositItems);
                                    TryStartQueuedRoutineTask();
                                }
                                else
                                {
                                    _hasDepositedTonight = true;
                                }
                            }

                            if (AiState != NpcAiState.Idle && AiState != NpcAiState.GoHome
                                && AiState != NpcAiState.GoToGroundItem && AiState != NpcAiState.GoToChestWithdraw
                                && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToMerchant
                                && AiState != NpcAiState.WalkToTarget && AiState != NpcAiState.Sitting)
                            {
                                AiState = NpcAiState.Idle;
                                AiTarget = WorldPos;
                                _currentPath.Clear();
                            }
                            else if (AiState == NpcAiState.Idle && AiTimer <= 0f && !_isSleeping)
                            {
                                if (!isNight)
                                {
                                    Decide();
                                    return;
                                }

                                AiTimer = (float)(Random.Shared.NextDouble() * 10f + 3f);

                                if (HomeBuildingId >= 0 && Random.Shared.NextDouble() < 0.35)
                                {
                                    var seat = World.FindNearestFreeHouseSeat(HomeBuildingId, WorldPos, 200f);
                                    if (seat.HasValue)
                                    {
                                        _reservedBench = seat.Value.key;
                                        _benchSeatVisualPos = seat.Value.seatPos;
                                        _walkingToBench = true;
                                        AiState = NpcAiState.WalkToTarget;
                                        AiTarget = seat.Value.approachPos;
                                        AiTimer = 20f;
                                        AnimState = "walk";
                                        Speed = BaseSpeed;
                                        _currentPath.Clear();
                                        _pathRecalcTimer = PATH_RECALC_INTERVAL;
                                        return;
                                    }
                                }

                                if (HomeBuildingId >= 0 && Random.Shared.NextDouble() < 0.7)
                                {
                                    var spot = World.PickRandomInteriorSpot(HomeBuildingId);
                                    if (spot != Vector2.Zero)
                                    {
                                        AiState = NpcAiState.WalkToTarget;
                                        AiTarget = spot;
                                        AnimState = "walk";
                                        Speed = BaseSpeed;
                                        _currentPath.Clear();
                                        _pathRecalcTimer = PATH_RECALC_INTERVAL;
                                        return;
                                    }
                                }
                            }
                        }
                    }

                    if (hasHome)
                    {
                        float distToHomeForCheck = Vector2.Distance(WorldPos, HomePosition);
                        bool isAwayFromHome = isTentHome
                            ? distToHomeForCheck >= 25f
                            : !isAtHome;

                        //  CORRECTIF : le jour peut se lever pendant qu'un villageois est encore
                        // en train de rentrer (AiState == GoHome) sans être arrivé. Ce cas n'était
                        // traité auparavant que dans un bloc mort, imbriqué sous
                        // TryCommitGoal(NpcPriority.Routine) : comme GoHome (75) est une priorité
                        // supérieure à Routine (40), ce TryCommitGoal échouait systématiquement
                        // tant que AiState == GoHome, donc ce code n'était en réalité JAMAIS
                        // exécuté. Un villageois déjà tout près de chez lui quand le jour se lève
                        // continuait donc bêtement jusqu'à sa porte avant de pouvoir reprendre une
                        // activité de jour, au lieu de rebrousser chemin tout de suite. On place
                        // ici le même contrôle, au bon endroit (hors de toute condition sur
                        // Routine), pour qu'il s'exécute réellement.
                        if (!isNight && AiState == NpcAiState.GoHome && distToHomeForCheck < 30f)
                        {
                            ReleaseCurrentGoalLock();
                            AiState = NpcAiState.Idle;
                            AiTarget = WorldPos;
                            AiTimer = 0f;
                            AnimState = "idle";
                            _currentPath.Clear();
                        }

                        if (isNight && isAwayFromHome && AiState != NpcAiState.Flee && AiState != NpcAiState.Chase
                            && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToChestWithdraw
                            && AiState != NpcAiState.GoHome
                            && TryCommitGoal(NpcPriority.GoHome))
                        {
                            AiState = NpcAiState.GoHome;
                            AiTarget = GetHomeEntryTarget();
                            _currentPath.Clear();
                            _pathRecalcTimer = 0f;
                            AnimState = "walk";
                            Speed = BaseSpeed;
                        }
                    }
                }
            }

            // Particules de pas
            if (!IsPlayer && !_isSleeping)
            {
                float movedDist = Vector2.Distance(WorldPos, _lastPosForParticles);
                if (movedDist > 5f)
                {
                    _stepParticleCooldown -= dt;
                    if (_stepParticleCooldown <= 0f)
                    {
                        SpawnFootstepParticle();
                        _stepParticleCooldown = STEP_PARTICLE_INTERVAL;
                        _lastPosForParticles = WorldPos;
                    }
                }
            }

            if (!IsPlayer && Species != "human" && !_isSleeping && !IsInPlayerDialogue && !IsTalking &&
                (AiState == NpcAiState.Idle || AnimState == "idle") && !IsKnockedBack)
            {
                _idleSoundCooldown -= dt;
                if (_idleSoundCooldown <= 0f && Random.Shared.NextDouble() < 0.08)
                {
                    TryPlayIdleSound(playerPos);
                }
            }

            // Comportement apprivoisé
            //  Tant que l'animal est monté, c'est le joueur (via Program.cs) qui contrôle
            // directement sa position/son animation : on ne laisse plus l'IA (Follow/Roam/...)
            // décider d'un déplacement, sinon l'animal "tire" contre les commandes du joueur.
            if (IsTamed && !IsMounted)
            {
                UpdateTamedOrders(petOwnerPos);
                UpdateFollowSpeed();
            }

            UpdateWaterStatus();

            if (IsRidingMount && AiState == NpcAiState.Idle)
                DismountOwnedMount();

            if (IsBoss && string.Equals(Species, "Khamsin", StringComparison.OrdinalIgnoreCase))
            {
                UpdateKhamsinBoss(dt, destroyed, playerPos);
                return;
            }

            if (IsWorm)
            {
                UpdateWormBoss(dt, destroyed, playerPos);
                return;
            }

            if (IsGulper)
            {
                UpdateGulperBoss(dt, destroyed, playerPos);
                return;
            }

            if (IsGenie)
            {
                UpdateGenieBoss(dt, destroyed, playerPos);
                return;
            }

            //  PNJ en dialogue/commerce avec le joueur : il arrête simplement de se déplacer
            // (l'IA de mouvement/pathfinding est mise en pause) le temps de l'interaction. Tout
            // le reste (animation idle, clignement, dégâts, sommeil...) continue normalement,
            // et il reprend son comportement habituel dès la fin du dialogue.
            if (IsInPlayerDialogue)
            {
                Speed = 0f;
                AnimState = "idle";
            }

            // Animation
            //  Un animal monté n'a plus d'AiState de déplacement (il reste Idle, contrôlé par
            // le joueur) : on se base donc aussi sur AnimState ("walk"/"run", fixé directement
            // par Program.cs pendant la monte) pour que l'animation suive le mouvement imposé
            // par le joueur.
            float animSpeed;
            if (AnimState == "run")
                animSpeed = 12f;
            else if (AnimState == "walk" || AiState == NpcAiState.WalkToTarget || AiState == NpcAiState.Wander ||
                     AiState == NpcAiState.Flee || AiState == NpcAiState.Chase ||
                     AiState == NpcAiState.LookAtPlayer || AiState == NpcAiState.Follow ||
                     AiState == NpcAiState.GoHome || AiState == NpcAiState.GoToChestWithdraw ||
                     AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToMerchant ||
                     AiState == NpcAiState.GoToGroundItem || AiState == NpcAiState.GoToVillageCenter)
                animSpeed = 8f;
            else
                animSpeed = 2f;
            AnimProgress += dt * animSpeed;
            if (AnimProgress >= 1f)
            {
                AnimProgress = 0f;
                int frameCount = 8;
                if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
                {
                    var firstPart = skeleton.FirstOrDefault();
                    if (firstPart != null && firstPart.Animations.TryGetValue(AnimState, out var frames) && frames.Count > 0)
                        frameCount = frames.Count;
                }
                if (frameCount == 0) frameCount = 1;
                CurrentFrame = (CurrentFrame + 1) % frameCount;

                if (!IsMounted && Speed > 0.01f &&
                    Program.ShouldTriggerFootstepFrame(AnimState, CurrentFrame, frameCount))
                    Program.PlayEntityFootstepSound();
            }

            if (IsPlayer) return;

            //  L'animal est monté par le JOUEUR : sa position/orientation/animation sont
            // entièrement pilotées par Program.cs à partir des touches du joueur (voir
            // MountedAnimal). On s'arrête ici pour qu'aucune IA (pathfinding, Decide(),
            // poursuite...) ne vienne déplacer l'animal de son propre chef pendant qu'il
            // est monté (Rider == null ⇒ convention "monté par le joueur", voir Mount()).
            if (IsMounted && Rider == null) return;

            //  MONTURE PILOTÉE PAR UN PNJ (Rider != null, ex : loup de compagnie d'un gobelin) :
            // contrairement au joueur, il n'y a pas de code dédié dans Program.cs pour ça — le
            // cavalier garde entièrement sa propre IA/pathfinding (Chase, GoHome...), et c'est la
            // monture qui se contente de recopier sa position/orientation chaque frame, avec un
            // léger décalage de selle. Si le cavalier a disparu ou a mis pied à terre entre-temps
            // (cas limite : mort du cavalier, sauvegarde partielle...), on descend par sécurité.
            if (IsMounted)
            {
                if (Rider == null || !Rider.IsAlive || !Rider.IsRidingMount)
                {
                    Dismount();
                    return;
                }

                Vector2 seatOffset = new Vector2(0f, -6f); // la monture se cale juste sous son cavalier
                WorldPos = Rider.WorldPos + seatOffset;
                Facing = Rider.Facing;
                AnimState = Rider.Speed > BaseSpeed * 1.2f ? "run" : (Rider.Speed > 0.01f ? "walk" : "idle");

                // Si cette monture est chevauchée par un PNJ qui poursuit une cible, elle peut
                // également participer au combat en attaquant la même cible que son maître.
                if (Rider.AiState == NpcAiState.Chase && Rider.OwnedMount == this)
                {
                    AiTarget = Rider.AiTarget;
                    AiTargetConnectionId = Rider.AiTargetConnectionId;

                    float distToTarget = Vector2.Distance(WorldPos, AiTarget);
                    if (_attackPhase != AttackPhase.None || distToTarget <= Math.Max(AttackHitRange * 2f, 250f))
                    {
                        if (HasRangedWeapon)
                            RangedCombatBehavior(dt, destroyed, AiTarget);
                        else
                            MeleeCombatBehavior(dt, destroyed, AiTarget);
                    }
                }
                return;
            }

            if (IsInPlayerDialogue)
            {
                Speed = 0f;
                return;
            }

            // ==================== IA ====================
            if (_goalLockTimer > 0f)
            {
                _goalLockTimer -= dt;
                if (_goalLockTimer < 0f) _goalLockTimer = 0f;
            }
            if (_fearCooldown > 0f) _fearCooldown -= dt;

            //  PRIORITÉ ABSOLUE : vie en danger. Si les PV sont critiques et qu'une menace
            // (le joueur) se trouve à proximité, l'entité fuit quel que soit ce qu'elle était
            // en train de faire (combat, routine, nourriture, discussion...). Ce contrôle est
            // fait avant tout le reste de la logique de décision pour éviter qu'une entité ne
            // reste "coincée" à hésiter entre attaquer/rentrer chez elle et fuir pour sa survie.
            if (!IsVillageGuard && !IsBoss && !IsTamed && IsAlive && MaxHP > 0)
            {
                float hpPercent = (float)CurrentHP / MaxHP;
                float fleeTriggerRadius = GetFleeTriggerRadius();
                Vector2 fearTargetPos = GetFearTargetPosition();
                float distanceToThreatSq = Vector2.DistanceSquared(WorldPos, fearTargetPos);
                bool threatNearby = _hasFearTarget && distanceToThreatSq < fleeTriggerRadius * fleeTriggerRadius;
                if (hpPercent <= 0.25f && threatNearby && TryCommitGoal(NpcPriority.Survival, 2.5f))
                {
                    Vector2 fleeDir = WorldPos - fearTargetPos;
                    fleeDir = fleeDir.LengthSquared() > 0.01f ? Vector2.Normalize(fleeDir) : new Vector2(1, 0);
                    AiState = NpcAiState.Flee;
                    _attackPhase = AttackPhase.None;
                    AiTarget = fearTargetPos + fleeDir * GetFleeSafeRadius();
                    AiTimer = 6f;
                    Speed = RunSpeed;
                    ShowAlertIcon = false;
                }
            }

            if (!IsPlayer && Program.IsPlayerRagdolled
                && AiState != NpcAiState.GoToInfirmary && AiState != NpcAiState.HealingAtInfirmary)
            {
                Speed = 0f;
                AnimState = "idle";
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                _currentPath.Clear();
                return;
            }

            //  Décompte de la période de grâce après un abandon de poursuite (voir plus bas).
            if (_chaseGiveUpCooldown > 0f)
            {
                _chaseGiveUpCooldown -= dt;
                if (_chaseGiveUpCooldown < 0f) _chaseGiveUpCooldown = 0f;
            }

            bool shouldChase = false;
            if (_chaseGiveUpCooldown <= 0f)
            {
                if (Behavior == "hostile" && !IsTamed)
                {
                    float visionDistance = VisionRange * Program.TileSize;
                    shouldChase = (VisionRange > 0 && distToPlayerSq < visionDistance * visionDistance);
                }
                else if (Behavior == "neutral" && IsAlerted && !IsTamed)
                {
                    float visionDistance = VisionRange * Program.TileSize;
                    shouldChase = (VisionRange > 0 && distToPlayerSq < visionDistance * visionDistance);
                }
                else if (IsTamed && IsHostilePet && TamedBehavior == TamedAnimalMode.Attack)
                {
                    //  Uniquement les pets d'un PNJ hostile chargent le joueur. Pour un animal du
                    // joueur, seule _combatTargetEntity (voir FindPlayerPetTarget) déclenche la poursuite.
                    float visionDistance = VisionRange * Program.TileSize;
                    shouldChase = (VisionRange > 0 && distToPlayerSq < visionDistance * visionDistance);
                }
            }

            if (isRetaliating || _combatTargetEntity != null)
                shouldChase = true;

            if ((IsVillageGuard || IsSpiritAnimal) && _combatTargetEntity == null && !isRetaliating && AiState == NpcAiState.Chase)
            {
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                _attackPhase = AttackPhase.None;
                _currentGoalPriority = NpcPriority.Idle;
                _goalLockTimer = 0f;
                _currentPath.Clear();
                Speed = 0f;
            }

            //  ABANDON DE POURSUITE : évite qu'une entité pourchasse le joueur à l'infini.
            // Deux cas distincts, vérifiés uniquement pendant une poursuite en cours :
            //   1) "Semée" : le joueur s'est suffisamment éloigné (au-delà du double de la
            //      distance à laquelle l'entité le repère normalement) pour qu'on considère
            //      qu'elle l'a perdu de vue - abandon immédiat, quelle que soit la durée.
            //   2) Poursuite trop longue ET trop éloignée du point de départ : le joueur reste
            //      à portée mais la course-poursuite dure depuis un moment ET a entraîné
            //      l'entité loin de là où elle se trouvait avant d'attaquer - elle renonce et
            //      rentre plutôt que de suivre indéfiniment (utile si le joueur recule sans
            //      jamais sortir complètement de portée).
            // Dans les deux cas : retour au point de départ de la poursuite (_chaseStartPos),
            // pas au domicile (HomePosition), qui peut être vide ou très éloigné pour un monstre.
            if (AiState == NpcAiState.Chase)
            {
                _chaseElapsedTime += dt;

                float visionDistance = VisionRange * Program.TileSize;
                bool petOwnTarget = (petAttackActive || IsSpiritAnimal) && _combatTargetEntity != null;
                bool lostSight = !isRetaliating && !petOwnTarget && visionDistance > 0f
                    && distToPlayerSq > (visionDistance * 2f) * (visionDistance * 2f);
                bool strayedTooFar = !isRetaliating && !petOwnTarget && _chaseElapsedTime > CHASE_LEASH_TIME
                    && Vector2.DistanceSquared(WorldPos, _chaseStartPos) > (CHASE_LEASH_RANGE) * (CHASE_LEASH_RANGE);

                if (lostSight || strayedTooFar)
                {
                    shouldChase = false;
                    _chaseGiveUpCooldown = CHASE_GIVEUP_COOLDOWN_DURATION;
                    IsAlerted = false; // sinon une entité "neutral" repartirait en Chase dès la fin du cooldown
                    HasPropagatedSignal = false;
                    _attackPhase = AttackPhase.None;

                    AiState = NpcAiState.WalkToTarget;
                    AiTarget = _chaseStartPos != Vector2.Zero ? _chaseStartPos : WorldPos;
                    AiTimer = 20f; // large marge ; Decide() reprend la main normalement une fois arrivée
                    Speed = BaseSpeed;
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                    _currentGoalPriority = NpcPriority.Idle;
                    _goalLockTimer = 0f;
                    ShowAlertIcon = false;
                }
            }

            if (ShowAlertIcon && !IsSlime && _currentGoalPriority <= NpcPriority.AlertFreeze)
            {
                // Tant que l'entité est alertée visuellement, elle ne se déplace pas.
                // (Priorité "AlertFreeze" : pas besoin de TryCommitGoal ici, ce court arrêt
                // ne fait que geler le mouvement sans changer l'objectif de fond.)
                if (_currentGoalPriority <= NpcPriority.AlertFreeze)
                {
                    Speed = 0f;
                    AnimState = "idle";
                    AiTarget = WorldPos;
                    _currentPath.Clear();
                }
            }
            else if (shouldChase && TryCommitGoal(NpcPriority.Combat, 1.2f))
            {
                if (AiState != NpcAiState.Chase)
                {
                    AiState = NpcAiState.Chase;
                    AiTarget = playerPos;
                    Speed = RunSpeed;

                    //  Nouvelle poursuite : on mémorise le point de départ et on repart de
                    // zéro sur la durée, pour l'abandon "leash" ci-dessus.
                    _chaseStartPos = WorldPos;
                    _chaseElapsedTime = 0f;

                    //  Le joueur est loin : si ce PNJ possède une monture (ex : loup de
                    // compagnie d'un gobelin), il grimpe dessus pour le rattraper plus vite
                    // plutôt que de courir à pied (voir TryMountOwnedPet/OwnedMount).
                    TryMountOwnedPet(playerPos);

                    //  Détection directe (pas via écho) : affiche l'icône d'alerte et programme
                    // l'émission du signal qui va réveiller les entités hostiles proches.
                    if (Behavior == "hostile" && !IsTamed)
                    {
                        TriggerAlertSignal(playerPos, AiTargetConnectionId);
                    }
                }
                AiTarget = playerPos;
                AiTimer = 2f;
            }
            else if (AiState == NpcAiState.Chase)
            {
                // Tant que le PNJ est en poursuite, retenter la montée si la cible
                // s'éloigne suffisamment après un départ initial trop proche.
                TryMountOwnedPet(AiTarget);
            }
            else if (AiState != NpcAiState.Chase && HasPropagatedSignal)
            {
                // La poursuite est bien terminée (plus en Chase) : on autorise l'entité à relayer
                // à nouveau un futur signal d'alerte.
                HasPropagatedSignal = false;
            }

            // Attirance par la nourriture
            const float FOOD_ATTRACTION_RANGE = 180f;
            if (PreferredFoodId != 0 && AiState != NpcAiState.Chase && AiState != NpcAiState.Flee && !IsTamed)
            {
                bool hasPreferredFood = (playerHeldItemId == PreferredFoodId);
                IsInterestedInFood = hasPreferredFood;

                if (hasPreferredFood && distToPlayerSq < FOOD_ATTRACTION_RANGE * FOOD_ATTRACTION_RANGE && TryCommitGoal(NpcPriority.Food))
                {
                    if (AiState != NpcAiState.Follow)
                    {
                        AiState = NpcAiState.Follow;
                        AiTarget = playerPos;
                    }
                    else
                    {
                        AiTarget = playerPos;
                    }
                }
                else if (!hasPreferredFood && AiState == NpcAiState.Follow && _currentGoalPriority <= NpcPriority.Food)
                {
                    IsInterestedInFood = false;
                    _currentGoalPriority = NpcPriority.Idle;
                    _goalLockTimer = 0f;
                    Decide();
                }
            }
            else
            {
                IsInterestedInFood = false;
                if (AiState == NpcAiState.Follow && _currentGoalPriority <= NpcPriority.Food)
                {
                    _currentGoalPriority = NpcPriority.Idle;
                    _goalLockTimer = 0f;
                    Decide();
                }
            }

            AiTimer -= dt;

            if (_villageCenterVisitCooldown > 0f)
                _villageCenterVisitCooldown = Math.Max(0f, _villageCenterVisitCooldown - dt);

            if (_workBreakCooldown > 0f)
                _workBreakCooldown = Math.Max(0f, _workBreakCooldown - dt);
            else if (_workShiftTimer > 0f)
                _workShiftTimer -= dt;

            if (_workplaceRetryCooldown > 0f)
                _workplaceRetryCooldown = Math.Max(0f, _workplaceRetryCooldown - dt);

            if (_chestDepositRetryCooldown > 0f)
                _chestDepositRetryCooldown = Math.Max(0f, _chestDepositRetryCooldown - dt);

            if (_abandonedGoalCooldown > 0f)
            {
                _abandonedGoalCooldown -= dt;
                if (_abandonedGoalCooldown < 0f)
                    _abandonedGoalCooldown = 0f;
            }

            if (_walkingToBench && AiState == NpcAiState.WalkToTarget)
            {
                if (Vector2.DistanceSquared(WorldPos, AiTarget) < (70f) * (70f))
                {
                    StartSitting();
                }
                else if (AiTimer <= 0f)
                {
                    World.ReleaseBench(_reservedBench);
                    _reservedBench = null;
                    _walkingToBench = false;
                    AiState = NpcAiState.Idle;
                    AnimState = "idle";
                    AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                }
            }

            //  IMPORTANT : tout état dont le déplacement passe par MoveTowardTarget() doit
            // figurer ici, sinon _currentPath reste vide pour toujours (il n'est jamais
            // rempli qu'ici, ailleurs on ne fait que le vider avec _currentPath.Clear() en
            // changeant d'état) et MoveTowardTarget renvoie immédiatement sans bouger le PNJ
            // (voir son "if (_currentPath.Count == 0) { AnimState = "idle"; return; }").
            // GoToChestWithdraw, GoToChestDeposit, GoToMerchant et GoToGroundItem utilisent
            // tous MoveTowardTarget mais étaient absents de cette liste : le PNJ restait donc
            // figé sur place en permanence pour ces 4 états, quel que soit AiTarget.
            //
            //  FILET DE SÉCURITÉ (vitesse) : chaque site du code qui décide d'un de ces états
            // est censé remettre Speed à une valeur non nulle (BaseSpeed/RunSpeed) avant de le
            // faire. Mais avec des dizaines de points de décision dispersés dans l'IA, il suffit
            // d'un seul oubli (ex : GoHome assigné juste après un Idle où Speed valait 0) pour
            // qu'un PNJ reste planté indéfiniment tout en "pensant" activement à un objectif :
            // MoveTowardTarget calcule bien un chemin et une cible, mais le déplacement réel
            // (dir * Speed * dt) est nul. Plutôt que de compter sur chaque appelant pour ne
            // jamais l'oublier, on garantit ici, à l'endroit unique où TOUS ces états convergent,
            // qu'un PNJ qui a "quelque chose en tête" a toujours de quoi s'y rendre : dès qu'il
            // est dans un état de déplacement mais que Speed est resté à 0, on le corrige
            // immédiatement (le PNJ agit sans attendre un correctif tiers).
            bool isMovementState = IsMovementState(AiState);
            if (isMovementState && Speed <= 0f)
            {
                Speed = BaseSpeed > 0f ? BaseSpeed : SpeciesData.GetSpeed(Species);
            }

            if (isMovementState)
            {
                if (_pathRecalcTimer > 0f)
                {
                    _pathRecalcTimer -= dt;
                    if (_pathRecalcTimer < 0f)
                        _pathRecalcTimer = 0f;
                }

                //  Détection "chemin trouvé mais plus aucune progression" : indépendante du
                // recalcul de chemin ci-dessous, elle tourne en continu pendant que le PNJ
                // est censé se déplacer. Si sa position n'a quasiment pas bougé sur tout un
                // intervalle de contrôle, c'est qu'il est physiquement coincé (bousculé,
                // obstacle dynamique apparu sur le chemin déjà calculé, etc.) même si
                // _currentPath n'est pas vide.
                if (_currentPath.Count > 0)
                {
                    _stuckProgressTimer += dt;
                    if (_stuckProgressTimer >= STUCK_PROGRESS_CHECK_INTERVAL)
                    {
                        float moved = Vector2.Distance(WorldPos, _stuckProgressCheckPos);
                        _stuckProgressTimer = 0f;
                        _stuckProgressCheckPos = WorldPos;

                        if (moved < STUCK_PROGRESS_MIN_DISTANCE)
                        {
                            //  CORRECTIF : GoToChestDeposit se contentait de vider le chemin et de
                            // retenter indéfiniment sans jamais abandonner, contrairement à tous les
                            // autres états. Un PNJ physiquement coincé en permanence près de son point
                            // d'approche (bousculé par d'autres PNJ convergeant vers le même coffre,
                            // obstacle dynamique persistant) restait donc bloqué pour toujours au lieu
                            // de finir par renoncer comme n'importe quel autre trajet. On plafonne
                            // maintenant les tentatives, comme pour MAX_PATH_FAIL_STREAK.
                            if (AiState == NpcAiState.GoToChestDeposit
                                && ++_chestDepositStuckRetryCount < CHEST_DEPOSIT_STUCK_MAX_RETRIES)
                            {
                                _currentPath.Clear();
                                _pathRecalcTimer = 0f;
                                _stuckProgressTimer = 0f;
                            }
                            else
                            {
                                if (AiState == NpcAiState.GoToChestDeposit)
                                    _chestDepositStuckRetryCount = 0;
                                AbandonCurrentGoal("aucune progression sur le chemin (bloqué physiquement)");
                            }
                            goto skipPathRecompute; // AiState est repassé à Idle : rien d'autre à faire ce tick
                        }
                    }
                }
                else
                {
                    _stuckProgressTimer = 0f;
                    _stuckProgressCheckPos = WorldPos;
                }

                if (_currentPath.Count > 0 && _pathRecalcTimer > 0f)
                {
                    // conserver
                }
                else
                {
                    if (!Pathfinder.TryReservePathRequest())
                    {
                        // Le quota global protège le thread principal contre les pics d'A*.
                        // Ce n'est pas un échec de chemin : on laisse le PNJ retenter bientôt.
                        //  CORRECTIF FAMINE : un délai fixe (0.05f) faisait retenter TOUTES les
                        // entités recalées exactement au même tick, dans le même ordre de boucle
                        // qu'avant (voir Program.Update) - le même sous-ensemble d'entités
                        // gagnait donc indéfiniment le quota pendant que les autres restaient
                        // plantées sur place sans jamais obtenir de chemin. La gigue aléatoire
                        // désynchronise les tentatives suivantes pour que chacune finisse par
                        // passer devant les autres au moins une fois.
                        _pathRecalcTimer = 0.05f + (float)(Random.Shared.NextDouble() * 0.1f);
                        goto skipPathRecompute;
                    }

                    //  OPTIMISATION PERF (villages avec beaucoup de PNJ) : le calcul de chemin
                    // A* (Pathfinder.FindPath) est l'opération la plus coûteuse de l'IA d'un PNJ.
                    // Avant, tous les PNJ en mouvement le relançaient en rythme sur exactement le
                    // même intervalle (PATH_RECALC_INTERVAL), ce qui créait un "pic" de calcul
                    // périodique dès qu'une dizaine de villageois marchaient en même temps
                    // (effet "thundering herd"). Deux correctifs :
                    //   1) un peu de gigue (jitter) aléatoire sur l'intervalle pour étaler ces
                    //      recalculs sur plusieurs frames au lieu de les synchroniser ;
                    //   2) les PNJ loin du joueur (donc hors écran ou en périphérie) recalculent
                    //      leur chemin beaucoup moins souvent : la précision du pathfinding n'a
                    //      d'importance visuelle que pour ce que le joueur regarde réellement.
                    //  CORRECTIF : mêmes seuils trop bas que NEARBY_NPC_AI_DISTANCE_SQ (voir
                    // ShouldRunFullAiUpdate) - 350/700px coupaient déjà la fréquence de recalcul
                    // pour des PNJ encore nettement visibles à l'écran, aggravant l'impression de
                    // blocage (chemin périmé non rafraîchi). Alignés sur la même distance
                    // "réellement hors écran" que le seuil d'IA complète ci-dessus.
                    float distanceFactor = 1f;
                    float distToPlayerForPath = Vector2.Distance(WorldPos, playerPos);
                    if (distToPlayerForPath > 2200f)
                        distanceFactor = 4f;   // très loin / hors écran : recalculs bien plus rares
                    else if (distToPlayerForPath > 1100f)
                        distanceFactor = 2f;   // périphérie de l'écran : deux fois moins souvent

                    float jitter = 1f + ((float)Random.Shared.NextDouble() - 0.5f) * 0.4f; // ±20%
                    _pathRecalcTimer = PATH_RECALC_INTERVAL * distanceFactor * jitter;

                    //  GoHome utilise désormais exactement la même IA de pathfinding
                    // (Pathfinder.FindPath) que les autres états, jusqu'à HomePosition
                    // directement. Si la recherche vers la case intérieure échoue, on tente
                    // au moins de trouver un chemin jusqu'à la porte de la maison.
                    int searchMargin = ComputeDynamicPathMargin(WorldPos, AiTarget);
                    _currentPath = Pathfinder.FindPath(WorldPos, AiTarget, destroyed, searchMargin, Species);

                    if (_currentPath.Count == 0 && AiState == NpcAiState.GoHome && HomeBuildingId >= 0 && World.Houses.TryGetValue(HomeBuildingId, out var homeHouse))
                    {
                        var doorPath = Pathfinder.FindPath(WorldPos, homeHouse.DoorWorldPos, destroyed, searchMargin, Species);
                        if (doorPath.Count > 0)
                        {
                            _currentPath = doorPath;
                        }
                        else
                        {
                            // Aucun chemin A* vers l'intérieur ni vers la porte pour le moment :
                            // ne pas passer en Idle tout de suite, mais compter l'échec comme les
                            // autres états ci-dessous (voir bloc commun juste après).
                            _currentPath.Clear();
                            _pathRecalcTimer = PATH_RECALC_INTERVAL * 2f * distanceFactor; // attendre un peu avant de retenter
                        }
                    }

                    //  BLOC COMMUN À TOUS LES ÉTATS (y compris GoHome) : si FindPath n'a rien
                    // renvoyé, on compte l'échec. Au bout de MAX_PATH_FAIL_STREAK essais
                    // consécutifs, la cible est considérée comme injoignable pour l'instant :
                    // on abandonne l'envie plutôt que de laisser le PNJ figé indéfiniment avec
                    // une ligne rouge de debug sans ligne verte associée.
                    if (_currentPath.Count == 0)
                    {
                        _pathFailStreak++;
                        if (_pathFailStreak >= MAX_PATH_FAIL_STREAK)
                        {
                            AbandonCurrentGoal($"chemin introuvable après {_pathFailStreak} essais");
                        }
                        else if (AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw)
                        {
                            _pathRecalcTimer = PATH_RECALC_INTERVAL * 2f * distanceFactor;
                        }
                    }
                    else
                    {
                        _pathFailStreak = 0;
                    }
                }
            }

        skipPathRecompute:

            // Exécution des états IA
            if (IsTalking)
            {
                //  Le PNJ s'arrête net et s'oriente vers son interlocuteur pendant qu'il
                // parle (au lieu de continuer son trajet en cours). S'il parle "dans le vide"
                // (pas de CurrentTalkPartner : petite réplique aléatoire), on le fait regarder
                // vers le joueur, comme le fait déjà UpdateGaze dans ce cas.
                AnimState = "idle";
                Vector2 lookTarget = CurrentTalkPartner != null ? CurrentTalkPartner.WorldPos : playerPos;
                float dx = lookTarget.X - WorldPos.X;
                if (MathF.Abs(dx) > 0.01f)
                    Facing = dx >= 0f ? 1f : -1f;
            }
            else if (ShowAlertIcon && !IsSlime && _currentGoalPriority <= NpcPriority.AlertFreeze)
            {
                AnimState = "idle";
            }
            else switch (AiState)
            {
                case NpcAiState.Idle:
                    AnimState = "idle";
                    if (AiTimer <= 0f) Decide();
                    break;
                case NpcAiState.WalkToTarget:
                    PerformMovement(dt, destroyed);
                    break;
                case NpcAiState.Wander:
                    if (IsSlime)
                        UpdateSlimeMovement(dt, destroyed);
                    else
                        Wander(dt, destroyed);
                    break;
                case NpcAiState.Flee:
                    if (IsSlime)
                    {
                        // Slimes should flee by jumping, not by running.
                        // Ensure AiTarget is set (OnHit sets it), then use the slime jump movement.
                        UpdateSlimeMovement(dt, destroyed);

                        // Reuse Flee end conditions: if far enough, stop fleeing.
                        Vector2 fearTargetPos = GetFearTargetPosition();
                        float distToThreat = Vector2.Distance(WorldPos, fearTargetPos);
                        if (distToThreat > GetFleeSafeRadius())
                        {
                            _continuousFearTimer = 0f;
                            AiState = NpcAiState.LookAtPlayer;
                            _lookAtPlayerTimer = 1.5f;
                            Speed = SpeciesData.GetSpeed(Species);
                        }
                        else if (AiTimer <= 0f)
                        {
                            AiTimer = 2f;
                        }
                    }
                    else
                    {
                        Flee(dt, destroyed, playerPos);
                    }
                    break;
                case NpcAiState.Chase:
                    if (IsSlime)
                    {
                        AiTarget = playerPos;
                        UpdateSlimeMovement(dt, destroyed);
                    }
                    else
                        ChaseBehavior(dt, destroyed, _combatTargetEntity?.WorldPos ?? playerPos);
                    break;
                case NpcAiState.LookAtPlayer:
                    LookAtPlayer(dt, destroyed, playerPos);
                    break;
                case NpcAiState.Follow:
                    PerformMovement(dt, destroyed);
                    break;
                case NpcAiState.GoHome:
                    if (IsInsideAssignedHouse())
                    {
                        ReleaseCurrentGoalLock();
                        AiState = NpcAiState.Idle;
                        AiTarget = WorldPos;
                        AnimState = "idle";
                        Speed = 0f;
                        _currentPath.Clear();
                    }
                    else
                    {
                        MoveTowardTarget(dt, destroyed, arrivalRadius: 20f);
                    }
                    break;
                case NpcAiState.GoToInfirmary:
                    if (Vector2.DistanceSquared(WorldPos, AiTarget) <= 35f * 35f)
                    {
                        AiState = NpcAiState.HealingAtInfirmary;
                        AiTarget = WorldPos;
                        _currentPath.Clear();
                        _infirmaryHealAccumulator = 0f;
                        Speed = 0f;
                        AnimState = "sit";
                    }
                    else
                    {
                        MoveTowardTarget(dt, destroyed, arrivalRadius: 25f);
                    }
                    break;
                case NpcAiState.HealingAtInfirmary:
                    HealAtInfirmary(dt);
                    break;
                case NpcAiState.Sitting:
                    AnimState = "sit";
                    _sitTimer -= dt;
                    if (_sitTimer <= 0f)
                        StopSitting();
                    break;
                case NpcAiState.GoToChestWithdraw:
                case NpcAiState.GoToChestDeposit:
                case NpcAiState.GoToMerchant:
                case NpcAiState.GoToGroundItem:
                case NpcAiState.GoToVillageCenter:
                    UpdateGoalArrival(dt, destroyed);
                    break;
            }
        }

        private void HealAtInfirmary(float dt)
        {
            if (_infirmaryBuildingId < 0)
            {
                ReleaseCurrentGoalLock();
                AiState = NpcAiState.Idle;
                Decide();
                return;
            }

            Speed = 0f;
            AnimState = "sit";
            _infirmaryHealAccumulator += dt * 5f;
            int healing = (int)_infirmaryHealAccumulator;
            if (healing <= 0)
                return;

            _infirmaryHealAccumulator -= healing;
            CurrentHP = Math.Min(MaxHP, CurrentHP + healing);
            if (CurrentHP < MaxHP)
                return;

            World.ReleaseInfirmary(_infirmaryBuildingId, NetId);
            _infirmaryBuildingId = -1;
            _infirmaryHealAccumulator = 0f;
            _hasFearTarget = false;
            _fearTargetEntity = null;
            _fearTargetIsPlayer = false;
            _fearTargetConnectionId = -1;
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            ReleaseCurrentGoalLock();
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            AiTimer = 0f;
            Decide();
        }

        //  Résultat d'une action de fin d'objectif (voir UpdateGoalArrival / GetGoalArrivalConfig).
        //  Complete : l'objectif est rempli, le PNJ redevient Idle et oublie sa cible.
        //  Retry : le PNJ reste sur place et retente la même action au prochain tick
        //          (ex : coffre plein, il faut réessayer plutôt qu'abandonner l'objectif).
        private enum GoalArrivalResult { Complete, Retry }

        //  ==================== ARRIVÉE À UN OBJECTIF (unifié) ====================
        //  Point d'entrée UNIQUE pour tous les états "le PNJ va quelque part pour y faire
        //  quelque chose" (retirer/déposer au coffre, acheter chez le marchand, ramasser un
        //  objet au sol, échanger au centre du village). Avant, chaque état dupliquait à la
        //  main : le test de distance d'arrivée, l'appel à MoveTowardTarget avec SON propre
        //  rayon, et le nettoyage (AiState = Idle, AiTarget = WorldPos, _currentPath.Clear()).
        //  Un correctif oublié dans un état ne l'était jamais appliqué aux autres — c'est ce
        //  qui produisait des PNJ qui restaient figés sur certains trajets et pas d'autres.
        //  Désormais la règle est unique et vaut pour tous les états listés ci-dessus :
        //      le PNJ a une intention (AiTarget) → tant qu'il n'est pas arrivé, il marche
        //      (MoveTowardTarget) → arrivé, il exécute l'action de fin propre à son état
        //      (GetGoalArrivalConfig) → Complete referme l'objectif, Retry le fait rester.
        private void UpdateGoalArrival(float dt, HashSet<string> destroyed)
        {
            var (arrivalRadius, onArrive) = GetGoalArrivalConfig(AiState);

            if (Vector2.DistanceSquared(WorldPos, AiTarget) < arrivalRadius * arrivalRadius)
            {
                GoalArrivalResult result = onArrive != null ? onArrive() : GoalArrivalResult.Complete;
                if (result == GoalArrivalResult.Complete)
                {
                    // Refermer l'objectif, ET relâcher la priorité qui le protégeait — sans ça,
                    // _currentGoalPriority restait bloqué (ex: Forage) après un ramassage
                    // pourtant réussi, empêchant Routine de reprendre la main pour aller
                    // déposer l'objet au coffre (le PNJ semblait alors figé en Idle).
                    ReleaseCurrentGoalLock();
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    _currentPath.Clear();
                }
                else // Retry : rester sur place, retenter dès le prochain tick
                {
                    _pathRecalcTimer = 0f;
                }
            }
            else
            {
                MoveTowardTarget(dt, destroyed, arrivalRadius: arrivalRadius);
            }
        }

        //  Table de configuration : pour chaque état "objectif", le rayon d'arrivée et
        //  l'action à exécuter une fois arrivé. C'est le SEUL endroit à modifier pour changer
        //  ce qui se passe quand un PNJ atteint sa cible pour un état donné.
        private (float arrivalRadius, Func<GoalArrivalResult>? onArrive) GetGoalArrivalConfig(NpcAiState state)
        {
            switch (state)
            {
                case NpcAiState.GoToChestWithdraw:
                    return (20f, () =>
                    {
                        if (HomeBuildingId >= 0)
                            World.TryWithdrawFoodFromChest(HomeBuildingId, this);
                        return GoalArrivalResult.Complete;
                    });

                case NpcAiState.GoToChestDeposit:
                    return (35f, () =>
                    {
                        bool logArrival = !_chestArrivalLogged;
                        if (logArrival)
                        {
                            _chestArrivalLogged = true;
                            Console.WriteLine($"[NPC-CHEST] ARRIVAL npc={NetId} home={HomeBuildingId} pos={WorldPos} target={AiTarget} distance={Vector2.Distance(WorldPos, AiTarget):F1} inventoryBefore={GetChestInventoryLog()}");
                        }

                        int deposited = 0;
                        if (HomeBuildingId >= 0)
                            deposited = World.DepositExcessItemsToChest(HomeBuildingId, this, logArrival);

                        bool hasRemainingItems = World.HasAnyDepositableItem(this);
                        if (!hasRemainingItems)
                        {
                            if (logArrival)
                                Console.WriteLine($"[NPC-CHEST] COMPLETE npc={NetId} home={HomeBuildingId} inventoryAfter={GetChestInventoryLog()}");
                            _hasDepositedTonight = true;
                            _chestDepositFullRetryCount = 0;
                            CompleteRoutineTask(RoutineTaskKind.DepositItems);
                            return GoalArrivalResult.Complete;
                        }

                        //  CORRECTIF : si CETTE tentative n'a réussi à déposer STRICTEMENT AUCUN
                        // item (coffre déjà plein pour tous les types transportés), retenter
                        // indéfiniment ne sert à rien : le coffre ne se videra pas tout seul
                        // pendant que le PNJ attend devant. Avant ce correctif, hasRemainingItems
                        // restait vrai pour toujours et GetGoalArrivalConfig renvoyait Retry sans
                        // aucune limite : le PNJ restait planté devant le coffre à l'infini,
                        // bloquant au passage sa priorité Routine pour le reste de la nuit. On
                        // plafonne donc les tentatives "coffre plein" : au bout de quelques essais
                        // consécutifs sans le moindre dépôt, on abandonne pour ce soir en gardant
                        // les objets restants sur le PNJ (rien n'est perdu, il retentera demain).
                        if (deposited == 0)
                        {
                            _chestDepositFullRetryCount++;
                            if (_chestDepositFullRetryCount >= CHEST_DEPOSIT_FULL_MAX_RETRIES)
                            {
                                if (logArrival)
                                    Console.WriteLine($"[NPC-CHEST] GIVE_UP_FULL npc={NetId} home={HomeBuildingId} inventoryRemaining={GetChestInventoryLog()}");
                                // Ne PAS utiliser _hasDepositedTonight ici : il serait immédiatement
                                // remis à false par le correctif "objet ajouté après coup" puisque
                                // HasAnyDepositableItem reste vrai, ce qui relancerait aussitôt un
                                // nouveau trajet vers le même coffre plein (boucle infinie de
                                // trajets au lieu d'un blocage sur place, mais tout aussi bloquant).
                                _chestDepositRetryCooldown = CHEST_DEPOSIT_RETRY_COOLDOWN;
                                _chestDepositFullRetryCount = 0;
                                CompleteRoutineTask(RoutineTaskKind.DepositItems);
                                return GoalArrivalResult.Complete;
                            }
                        }
                        else
                        {
                            _chestDepositFullRetryCount = 0;
                        }

                        if (logArrival)
                            Console.WriteLine($"[NPC-CHEST] RETRY npc={NetId} home={HomeBuildingId} inventoryRemaining={GetChestInventoryLog()}");

                        // Le coffre peut être partiellement plein (certains types déposés, d'autres
                        // non) : on retente au lieu d'abandonner tout de suite, pour ne pas laisser
                        // le PNJ dormir avec des objets sur lui alors qu'il reste de la place.
                        return GoalArrivalResult.Retry;
                    });

                case NpcAiState.GoToMerchant:
                    return (55f, () =>
                    {
                        Program.TryBuyFoodFromMerchant(this, _targetMerchantNetId);
                        _targetMerchantNetId = Guid.Empty;
                        return GoalArrivalResult.Complete;
                    });

                case NpcAiState.GoToGroundItem:
                    return (25f, () =>
                    {
                        Program.TryNpcPickupGroundItem(this, _targetGroundItemNetId);
                        _targetGroundItemNetId = Guid.Empty;
                        return GoalArrivalResult.Complete;
                    });

                case NpcAiState.GoToVillageCenter:
                    return (65f, () =>
                    {
                        if (_targetVillageCenter != null)
                            World.ExchangeItemsAtVillageCenter(this, _targetVillageCenter);
                        _targetVillageCenter = null;
                        CompleteRoutineTask(RoutineTaskKind.VisitVillageCenter);
                        _villageCenterVisitCooldown = RandomRange(100f, 160f);

                        if (Profession == ProfessionType.Farmer && TryFarmerWorkCycle())
                        {
                            AiTimer = 10f;
                            return GoalArrivalResult.Complete;
                        }

                        AiTimer = RandomRange(4f, 9f);
                        return GoalArrivalResult.Complete;
                    });

                default:
                    // Ne devrait pas arriver : tout état routé vers UpdateGoalArrival doit
                    // avoir sa configuration ici. Par sécurité, on considère l'arrivée comme
                    // terminale plutôt que de boucler indéfiniment sur un état inconnu.
                    return (65f, null);
            }
        }

        private bool IsInsideAssignedHouse()
        {
            if (HomeBuildingId < 0 || !World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var spots))
                return false;

            int tileX = (int)(WorldPos.X / Program.TileSize);
            int tileY = (int)MathF.Floor((WorldPos.Y + Program.FeetOffsetY) / Program.TileSize);
            return spots.Any(spot => spot.Tile.x == tileX && spot.Tile.y == tileY);
        }

        private string GetChestInventoryLog()
        {
            return string.Join(",", Inventory.Slots
                .Where(slot => !slot.IsEmpty && slot.Item != null)
                .Select(slot => $"{slot.Item!.Name}x{slot.Count}"));
        }

        //  BUGFIX : cette fonction renvoyait auparavant systématiquement la case intérieure la
        // plus proche de la porte de la maison — la MÊME case pour tous les habitants d'une même
        // maison. Résultat : tous les colocataires convergeaient vers ce point unique en rentrant
        // le soir, se bloquaient mutuellement (collision) et s'arrêtaient groupés au même endroit
        // au lieu d'aller chacun vers son propre lit. `HomePosition` est déjà la case intérieure
        // individuellement réservée à ce PNJ (voir World.FindNearestFreeHouseSpot à la création) :
        // c'est elle qu'il faut viser en priorité. Le repli "case la plus proche de la porte" ne
        // sert plus qu'aux cas où ce PNJ n'a exceptionnellement pas de HomePosition valide.
        private Vector2 GetHomeEntryTarget()
        {
            if (HomeBuildingId >= 0 && HomePosition != Vector2.Zero
                && World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var ownSpots)
                && ownSpots.Any(spot => Vector2.DistanceSquared(spot.WorldPos, HomePosition) < 4f))
            {
                return HomePosition;
            }

            if (HomeBuildingId >= 0
                && World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var spots)
                && spots.Count > 0
                && World.Houses.TryGetValue(HomeBuildingId, out var house))
            {
                return spots
                    .OrderBy(spot => Vector2.DistanceSquared(spot.WorldPos, house.DoorWorldPos))
                    .First()
                    .WorldPos;
            }

            return HomePosition;
        }

        private void StartSitting()
		{
			_walkingToBench = false;
			AiState = NpcAiState.Sitting;
			AnimState = "sit";
			WorldPos = _benchSeatVisualPos;
			_sitTimer = (float)(Random.Shared.NextDouble() * 10f + 8f);
			// La clé est déjà stockée dans _reservedBench lors de la réservation
		}

        private void StopSitting()
		{
			if (_reservedBench.HasValue)
				World.ReleaseBench(_reservedBench.Value);
			_reservedBench = null;
			_walkingToBench = false;
			AiState = NpcAiState.Idle;
			AnimState = "idle";
			AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
			
			//  Chercher une position libre à côté du banc
			Vector2 newPos = FindFreePositionNear(_benchSeatVisualPos);
			if (newPos != Vector2.Zero)
			{
				WorldPos = newPos;
			}
			else
			{
				// Fallback : se déplacer un peu plus loin
				WorldPos = _benchSeatVisualPos + new Vector2(Program.TileSize * 0.8f, 0);
			}
		}

		//  Nouvelle méthode pour trouver une position libre autour du banc
		private Vector2 FindFreePositionNear(Vector2 centerPos)
		{
			int ts = Program.TileSize;
			float searchRadius = ts * 1.5f;
			int attempts = 0;
			int maxAttempts = 50;
			
			while (attempts < maxAttempts)
			{
				attempts++;
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float distance = ts * 0.6f + (float)(Random.Shared.NextDouble() * (searchRadius - ts * 0.6f));
				Vector2 candidate = centerPos + new Vector2(
					MathF.Cos(angle) * distance,
					MathF.Sin(angle) * distance
				);
				
				int tileX = (int)(candidate.X / ts);
				int tileY = (int)(candidate.Y / ts);
				
				// Vérifier que la position est valide
				int groundId = World.GetGroundTileIdAt(tileX, tileY);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile == null || !groundTile.Walkable) continue;
				
				// Vérifier qu'il n'y a pas d'obstacle
				if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
				
				// Vérifier qu'on ne reste pas sur le banc (pas de collision)
				if (World.IsCollidingEntity(candidate, Program.GetDestroyedObjects(), Species))
					continue;
				
				return candidate;
			}
			
			return Vector2.Zero;
		}


        //  Choisit un point de destination valide (sol praticable, pas d'objet bloquant)
        // autour d'un point d'ancrage, pour que le wander utilise l'IA de pathfinding
        // au lieu de foncer dans une direction aléatoire.
        private Vector2 PickWanderDestination(Vector2 anchor, float minRadius, float maxRadius)
        {
            int ts = Program.TileSize;
            int attempts = 0;
            int maxAttempts = 20;

            while (attempts < maxAttempts)
            {
                attempts++;
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                float distance = minRadius + (float)(Random.Shared.NextDouble() * (maxRadius - minRadius));
                Vector2 candidate = anchor + new Vector2(
                    MathF.Cos(angle) * distance,
                    MathF.Sin(angle) * distance
                );

                int tileX = (int)(candidate.X / ts);
                int tileY = (int)(candidate.Y / ts);

                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var groundTile = WorldTileRegistry.GetTile(groundId);
                if (groundTile == null || !groundTile.Walkable) continue;

                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;

                return candidate;
            }

            // Repli : rester sur place, l'IA redécidera au prochain cycle
            return anchor;
        }

        // ==================== IA - MOUVEMENTS ====================
        //  Le trajet "rentrer à la maison" (AiState.GoHome) n'a plus de logique dédiée : il
        // passe par le même appel générique à Pathfinder.FindPath(WorldPos, AiTarget, ...) que
        // WalkToTarget/Chase/Wander, dans Update, avec AiTarget = HomePosition. La tuile de la
        // porte est traitée comme franchissable par le pathfinder (voir World.IsDoorTile dans
        // Pathfinding.IsSingleTileWalkable), donc l'A* la traverse tout seul pour amener le PNJ
        // jusqu'à sa case intérieure, en contournant réellement les murs plutôt qu'en fonçant
        // dedans en ligne droite.

        //  Liste UNIQUE des états "le PNJ a une intention de déplacement et doit suivre
        //  _currentPath / MoveTowardTarget". Avant, cette même liste était recopiée à la main
        //  à deux endroits (garde-fou de vitesse, puis recalcul de chemin) : un état de
        //  déplacement ajouté plus tard risquait de n'être ajouté qu'à l'un des deux, laissant
        //  le PNJ "penser" à un objectif sans jamais recalculer de chemin ni avoir de vitesse.
        //  Désormais il n'y a qu'un seul endroit à mettre à jour quand un nouvel état de
        //  déplacement est introduit.
        private static bool IsMovementState(NpcAiState state)
        {
            switch (state)
            {
                case NpcAiState.WalkToTarget:
                case NpcAiState.Chase:
                case NpcAiState.GoHome:
                case NpcAiState.Wander:
                case NpcAiState.Follow:
                case NpcAiState.GoToChestWithdraw:
                case NpcAiState.GoToChestDeposit:
                case NpcAiState.GoToInfirmary:
                case NpcAiState.GoToMerchant:
                case NpcAiState.GoToGroundItem:
                case NpcAiState.GoToVillageCenter:
                    return true;
                default:
                    return false;
            }
        }

        void MoveTowardTarget(float dt, HashSet<string> destroyed, float arrivalRadius = 65f)
        {
            if (AiTarget == WorldPos)
            {
                if (AiState == NpcAiState.WalkToTarget)
                    AiTimer = Math.Min(AiTimer, RandomRange(1f, 3f)); // pas de longue immobilité (timer de 20s hérité)
                AiState = NpcAiState.Idle;
                return;
            }

            // Aucun chemin A* disponible : attendre le prochain recalcul plutôt que de
            // se diriger directement vers la cible, ce qui ferait traverser les obstacles.
            // Cela concerne aussi les trajets sociaux, les balades et les suivis, pas seulement
            // le retour à la maison.
            if (_currentPath.Count == 0)
            {
                //  Les états de combat ne doivent pas être annulés simplement parce qu'il n'y a
                //  plus de nœud de chemin et que la cible est à portée d'attaque. Le combat a sa
                //  propre logique d'attaque / télégraph et doit pouvoir rester en "Chase" tant
                //  que le joueur est dans la zone de combat, sans se figer sur une tuile.
                if (AiState == NpcAiState.Chase)
                {
                    AnimState = "walk";
                    return;
                }

                if (Vector2.DistanceSquared(WorldPos, AiTarget) < arrivalRadius * arrivalRadius)
                {
                    if (AiState == NpcAiState.WalkToTarget)
                        AiTimer = Math.Min(AiTimer, RandomRange(1f, 3f)); // pas de longue immobilité (timer de 20s hérité)
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    AnimState = "idle";
                    return;
                }

                AnimState = "idle";
                return;
            }

            float effectiveArrivalRadius = (AiState == NpcAiState.Chase) ? AttackHitRange : arrivalRadius;

            Vector2 target = AiTarget;

            // If we have a computed path, follow its first node
            if (_currentPath.Count > 0)
            {
                //  CORRECTIF : le tout premier nœud d'un chemin fraîchement calculé est TOUJOURS
                // la position exacte du PNJ au moment du calcul (voir Pathfinder.ReconstructPath,
                // qui force path[0] = start), pas une vraie case à atteindre. Or FindPathInternal
                // ne valide JAMAIS la marchabilité de sa propre case de départ (seuls les voisins
                // explorés passent par grid.IsBlocked) : un chemin est donc renvoyé même quand le
                // PNJ se tient actuellement sur une case qui échouerait le test de clearance
                // (une entité large - cheval, vache... - collée à un mur ou un arbre exige que les
                // cases voisines soient libres aussi). La revalidation ci-dessous, elle, applique
                // ce test complet : sur ce nœud d'ancrage, elle échouait donc perpétuellement sur
                // la propre position du PNJ, jamais sur une vraie case à venir. Résultat observé :
                // chemin vidé (_currentPath.Clear()) puis recalculé immédiatement (_pathRecalcTimer
                // = 0f) à chaque frame, retrouvant systématiquement le même chemin (rien n'a
                // bougé) et échouant de la même façon - le PNJ ne progresse jamais, AiTarget (ligne
                // rouge debug) reste affiché mais _currentPath (ligne verte) est vide à chaque
                // rendu, et AnimState oscille walk/idle au gré du quota global de calcul de chemin
                // (TryReservePathRequest). Un simple coup de pouce du joueur déplace le PNJ hors de
                // cette case limite et débloque tout, ce qui confirmait le diagnostic. On avance
                // donc directement sur ce nœud d'ancrage (rien à valider, le PNJ y est déjà) avant
                // toute revalidation, exactement comme le fera de toute façon la logique d'arrivée
                // de nœud juste après pour tout nœud à distance quasi nulle.
                if (Vector2.DistanceSquared(_currentPath[0], WorldPos) <= 0.01f)
                    _currentPath.RemoveAt(0);
            }

            if (_currentPath.Count > 0)
            {
                //  Revalidation légère : si la case visée par le prochain nœud vient de devenir
                // bloquée (objet posé, mur reconstruit, etc.) depuis le calcul du chemin, on ne
                // continue pas à foncer dessus en attendant le prochain recalcul (jusqu'à 0.5s,
                // voir _pathRecalcTimer) — c'est exactement ce qui donnait l'impression que le
                // PNJ "traversait" l'obstacle : la ligne verte pointait vers une case qui n'était
                // plus praticable, et MoveTowardTarget continuait de s'y diriger jusqu'à ce que
                // IsCollidingEntity le stoppe physiquement dessus.
                if (!Pathfinder.IsWorldPositionWalkable(_currentPath[0], destroyed, Species))
                {
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                    AnimState = "walk";
                    return;
                }
                target = _currentPath[0];
            }

            Vector2 dir = target - WorldPos;
            float distanceToTarget = dir.Length();

            //  Le rayon d'arrivée à un nœud doit tenir compte de la taille réelle de la hitbox
            // de l'espèce (un cheval ou une vache ne peut pas "raser" un coin de mur comme une poule) :
            // plus l'entité est large, plus elle a besoin de marge pour ne pas accrocher les angles.
            Vector2 entityDim = World.GetEntityDimensions(Species);
            float entityHalfDiag = MathF.Sqrt(entityDim.X * entityDim.X + entityDim.Y * entityDim.Y) * 0.5f;
            float nodeArrivalRadius = MathF.Max(18f, entityHalfDiag * 0.75f);
            if (_currentPath.Count > 0)
            {
                // If close enough to the node, pop it and proceed
                if (distanceToTarget < nodeArrivalRadius)
                {
                    _currentPath.RemoveAt(0);
                    if (_currentPath.Count == 0)
                    {
                        // If this was the final node, check final arrival
                        if (Vector2.DistanceSquared(WorldPos, AiTarget) < (effectiveArrivalRadius) * (effectiveArrivalRadius))
                        {
                            if (AiState == NpcAiState.GoHome
                                || AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw
                                || AiState == NpcAiState.GoToMerchant || AiState == NpcAiState.GoToGroundItem
                                || AiState == NpcAiState.GoToVillageCenter)
                            {
                                _currentPath.Clear();
                                return;
                            }

                            if (AiState != NpcAiState.Chase)
                            {
                                if (_walkingToBench && _reservedBench.HasValue)
                                    StartSitting();
                                else if (Profession == ProfessionType.Farmer)
                                {
                                    if (TryFarmerWorkCycle())
                                    {
                                        AiState = NpcAiState.Idle;
                                        AnimState = "idle";
                                        AiTimer = 0f;
                                    }
                                    else if (_farmerWorkTarget.HasValue)
                                    {
                                        AiState = NpcAiState.WalkToTarget;
                                        AiTarget = _farmerWorkTarget.Value;
                                        AnimState = "walk";
                                        _currentPath.Clear();
                                        _pathRecalcTimer = PATH_RECALC_INTERVAL;
                                    }
                                    else
                                    {
                                        AiState = NpcAiState.Idle;
                                        AnimState = "idle";
                                    }
                                }
                                else
                                {
                                    ReleaseCurrentGoalLock();
                                    AiState = NpcAiState.Idle;
                                    AnimState = "idle";
                                }
                            }
                            _currentPath.Clear();
                            return;
                        }
                    }
                    // continue to next update to compute movement towards next node
                }
            }

            // Recompute direction if node was popped
            if (_currentPath.Count > 0)
            {
                dir = _currentPath[0] - WorldPos;
                distanceToTarget = dir.Length();
            }

            // If close enough to final target
            if (_currentPath.Count == 0 && distanceToTarget < effectiveArrivalRadius)
            {
                if (AiState == NpcAiState.GoHome
                    || AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw
                    || AiState == NpcAiState.GoToMerchant || AiState == NpcAiState.GoToGroundItem
                    || AiState == NpcAiState.GoToVillageCenter)
                {
                    _currentPath.Clear();
                    return;
                }

                if (AiState != NpcAiState.Chase)
                {
                    if (_walkingToBench && _reservedBench.HasValue)
                    {
                        StartSitting();
                    }
                    else if (Profession == ProfessionType.Farmer)
                    {
                        if (TryFarmerWorkCycle())
                        {
                            AiState = NpcAiState.Idle;
                            AnimState = "idle";
                            AiTimer = 0f;
                        }
                        else if (_farmerWorkTarget.HasValue)
                        {
                            AiState = NpcAiState.WalkToTarget;
                            AiTarget = _farmerWorkTarget.Value;
                            AnimState = "walk";
                            _currentPath.Clear();
                            _pathRecalcTimer = PATH_RECALC_INTERVAL;
                        }
                        else
                        {
                            AiState = NpcAiState.Idle;
                            AnimState = "idle";
                            AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                        }
                    }
                    else
                    {
                        ReleaseCurrentGoalLock();
                        AiState = NpcAiState.Idle;
                        AnimState = "idle";
                        AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                    }
                }
                _currentPath.Clear();
                return;
            }

            if (distanceToTarget > 0.01f)
                dir /= distanceToTarget;
            else
                dir = new Vector2(1, 0);

            //  Un PNJ à dos de monture se déplace à la vitesse de sa monture plutôt qu'à la
            // sienne (typiquement plus rapide) - voir TryMountOwnedPet.
            float effectiveSpeed = Speed;
            if (IsRidingMount && OwnedMount != null && OwnedMount.IsAlive)
                effectiveSpeed = Math.Max(effectiveSpeed, OwnedMount.RunSpeed);

            Vector2 move = dir * (IsInWater ? effectiveSpeed * 0.5f : effectiveSpeed) * dt;
            Facing = move.X >= 0 ? 1f : -1f;

            // Si l'entité chevauche sa monture, elle doit être assise
            if (IsRidingMount)
            {
                AnimState = "sit";
            }
            else
            {
                float runThreshold = BaseSpeed * 1.2f;
                if (Speed > runThreshold)
                    AnimState = "run";
                else
                    AnimState = "walk";
            }

            Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
            bool movedX = false;
            if (!World.IsCollidingEntity(newPosX, destroyed, Species))
            {
                WorldPos.X += move.X;
                movedX = true;
            }

            Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
            bool movedY = false;
            if (!World.IsCollidingEntity(newPosY, destroyed, Species))
            {
                WorldPos.Y += move.Y;
                movedY = true;
            }

            if (!movedX && !movedY)
            {
                //  Blocage complet (coin de mur) : avant d'abandonner et de recalculer tout le
                // chemin, on tente un léger glissement latéral perpendiculaire à la direction
                // voulue. Ça évite l'effet "collé/accroché" contre l'angle et laisse l'entité
                // se dégager naturellement de la géométrie qui dépasse sa hitbox.
                Vector2 tangent = new Vector2(-dir.Y, dir.X);
                float slideDist = MathF.Max(4f, (IsInWater ? Speed * 0.5f : Speed) * dt);

                Vector2 slidePosPositive = WorldPos + tangent * slideDist;
                Vector2 slidePosNegative = WorldPos - tangent * slideDist;

                bool positiveCloser = Vector2.DistanceSquared(slidePosPositive, target) <= Vector2.DistanceSquared(slidePosNegative, target);
                Vector2 firstTry = positiveCloser ? slidePosPositive : slidePosNegative;
                Vector2 secondTry = positiveCloser ? slidePosNegative : slidePosPositive;

                if (!World.IsCollidingEntity(firstTry, destroyed, Species))
                {
                    WorldPos = firstTry;
                }
                else if (!World.IsCollidingEntity(secondTry, destroyed, Species))
                {
                    WorldPos = secondTry;
                }
                else if (_currentPath.Count > 0)
                {
                    // Vraiment coincé : sortir d'une position de spawn invalide avant de recalculer le chemin.
                    // La recherche teste les tuiles par distance croissante et valide la hitbox réelle.
                    Vector2? recoveryPosition = Pathfinder.FindNearestWalkablePosition(WorldPos, destroyed, Species);
                    if (recoveryPosition.HasValue)
                        WorldPos = recoveryPosition.Value;

                    // Abandonner ce chemin et en recalculer un nouveau immédiatement.
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                }
            }
        }

        void PerformMovement(float dt, HashSet<string> destroyed, float arrivalRadius = 65f)
        {
            if (IsSlime)
                UpdateSlimeMovement(dt, destroyed);
            else
                MoveTowardTarget(dt, destroyed, arrivalRadius);
        }

        void Wander(float dt, HashSet<string> destroyed)
        {
            //  Envol spontané, sans menace : donne un peu de vie au monde (un oiseau qui
            // s'envole brièvement puis se repose plus loin), indépendamment de toute fuite.
            if (IsBird && _birdFlightState == BirdFlightState.Grounded && Random.Shared.NextDouble() < 0.0015)
                StartBirdTakeoff();

            // Petite chance d'interrompre la balade pour repartir en Idle (variation naturelle du comportement)
            if (Random.Shared.NextDouble() < 0.004)
            {
                AiState = NpcAiState.Idle;
                AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                AnimState = "idle";
                _currentPath.Clear();
                return;
            }

            //  Le wander suit désormais le chemin calculé par le pathfinding vers AiTarget,
            // exactement comme WalkToTarget, au lieu d'avancer en ligne droite dans une direction
            // aléatoire (ce qui causait les NPC coincés contre les murs/obstacles).
            MoveTowardTarget(dt, destroyed, arrivalRadius: 30f);
        }

        void Flee(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            Vector2 fearTargetPos = GetFearTargetPosition();
            Vector2 fleeDir = WorldPos - fearTargetPos;
            if (fleeDir.Length() > 0.01f)
                fleeDir = Vector2.Normalize(fleeDir);
            else
                fleeDir = new Vector2(1, 0);

            //  Un oiseau qui fuit décolle logiquement plutôt que de courir au sol : dès que la
            // fuite démarre, on déclenche l'envol (StartBirdTakeoff ne fait rien s'il est déjà
            // en l'air), dans la direction opposée à la menace. Le déplacement horizontal
            // ci-dessous reste inchangé, seule la hauteur (gérée par UpdateBirdFlight, appelée
            // depuis Update()) change le rendu.
            if (IsBird)
                StartBirdTakeoff(fleeDir);

            Speed = RunSpeed;

            Vector2 move = fleeDir * Speed * dt;
            Facing = move.X >= 0 ? 1f : -1f;

            // Si l'entité chevauche sa monture, elle doit être assise
            if (IsRidingMount)
            {
                AnimState = "sit";
            }
            else if (IsBirdFlying)
            {
                // En vol, on garde une anim de déplacement classique (le rendu ajoute juste le
                // décalage vertical) : "walk" suffit, inutile de forcer un "run" au sol.
                AnimState = "walk";
            }
            else
            {
                float runThreshold = BaseSpeed * 1.2f;
                if (Speed > runThreshold)
                    AnimState = "run";
                else
                    AnimState = "walk";
            }

            Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
            if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                WorldPos.X += move.X;

            Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
            if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                WorldPos.Y += move.Y;

            float distToThreat = Vector2.Distance(WorldPos, fearTargetPos);

            if (distToThreat > GetFleeSafeRadius())
            {
                _continuousFearTimer = 0f;
                AiState = NpcAiState.LookAtPlayer;
                _lookAtPlayerTimer = 1.5f;
                Speed = SpeciesData.GetSpeed(Species);
            }
            else if (AiTimer <= 0f)
            {
                AiTimer = 2f;
            }
        }

        void LookAtPlayer(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            Vector2 fearTargetPos = GetFearTargetPosition();
            Facing = fearTargetPos.X >= WorldPos.X ? 1f : -1f;
            AnimState = "idle";

            _lookAtPlayerTimer -= dt;

            if (_lookAtPlayerTimer <= 0f)
            {
                IsAlerted = false;
                Decide();
            }
        }

        // ==================== IA DE COMBAT ====================
        //  Point d'entrée commun : aiguille vers un style de combat à distance (arc) ou de
        // mêlée selon l'arme actuellement portée. Les tamed animals / monstres sans arme
        // suivent toujours le comportement de mêlée (charge + morsure/griffe).
        private void DamageCombatTarget(Entity target)
        {
            if (!target.IsAlive) return;

            int damage = Math.Max(1, Attack);
            damage = target.Equipment?.GetDamageAfterArmor(damage) ?? damage;
            target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
            target.OnHit(WorldPos, this, attackerIsPlayer: false);

            Vector2 knockbackDirection = target.WorldPos - WorldPos;
            if (knockbackDirection.LengthSquared() > 0.01f)
                knockbackDirection = Vector2.Normalize(knockbackDirection);
            else
                knockbackDirection = new Vector2(Facing, 0f);
            target.ApplyKnockback(knockbackDirection, 35f);
            Program.AddFloatingDamage(new Vector2(target.WorldPos.X, target.WorldPos.Y - 20f), damage, damage >= 10);

            if (!target.IsAlive)
            {
                foreach (var (id, quantity, metadata) in GameData.GetAnimalDrops(target.Species))
                    Program.GiveItemToPlayer(id, quantity, target.WorldPos, null, metadata);
            }
        }

        void ChaseBehavior(float dt, HashSet<string> destroyed, Vector2 targetPos)
        {
            if (IsSlime)
            {
                AiTarget = targetPos;
                UpdateSlimeMovement(dt, destroyed);
                return;
            }

            if (HasRangedWeapon)
                RangedCombatBehavior(dt, destroyed, targetPos);
            else
                MeleeCombatBehavior(dt, destroyed, targetPos);
        }

        // ==================== COMBAT DE MÊLÉE (approche simple -> attaque) ====================
        void MeleeCombatBehavior(float dt, HashSet<string> destroyed, Vector2 targetPos)
        {
            bool isGoblin = string.Equals(Species, "Goblin", StringComparison.OrdinalIgnoreCase);
            bool usesGoblinMeleeStyle = isGoblin || IsVillageGuard;
            bool isbolet = string.Equals(Species, "bolet", StringComparison.OrdinalIgnoreCase);
            Rectangle targetDamageHitbox = _combatTargetEntity != null
                ? World.GetEntityDamageHitbox(_combatTargetEntity, Program.TileSize)
                : World.GetEntityDamageHitbox(targetPos, "human", Program.TileSize);
            float distToPlayer = World.GetDistanceToDamageHitbox(WorldPos, targetDamageHitbox);
            _combatRangeChangeTimer -= dt;
            if (_combatRangeChangeTimer <= 0f)
            {
                _combatRangeChangeTimer = 0.8f + (float)Random.Shared.NextDouble() * 1.4f;
                _combatRangeWobble = ((float)Random.Shared.NextDouble() * 2f - 1f) * 0.18f;
            }

            float baseAttackTriggerRange = isGoblin || IsVillageGuard ? Math.Max(90f, AttackHitRange) : AttackHitRange;
            float attackTriggerRange = Math.Max(AttackHitRange * 0.9f, baseAttackTriggerRange * (1f + _combatRangeWobble));

            //  Un PNJ monté reste sur sa monture tant que son objectif de poursuite est actif.
            //  Il ne descendra qu'une fois passé en Idle ou ayant perdu sa cible.

            // ---- Phase 1 : Télégraphie (immobile) ----
            if (_attackPhase == AttackPhase.Telegraph)
            {
                _telegraphTimer += dt;
                AiTarget = targetPos;
                Speed = usesGoblinMeleeStyle ? Math.Max(BaseSpeed * 0.5f, 60f) : 0f;
                
                if (IsRidingMount)
                    AnimState = "sit";
                else
                    AnimState = usesGoblinMeleeStyle ? "walk" : "attack";

                if (usesGoblinMeleeStyle && !(IsMounted && Rider != null))
                {
                    PerformMovement(dt, destroyed, attackTriggerRange);
                }

                if (_telegraphTimer >= TELEGRAPH_DURATION)
                {
                    if (usesGoblinMeleeStyle)
                    {
                        _attackPhase = AttackPhase.Recovery;
                        _recoveryTimer = 0f;
                        _hasDealtDamage = false;
                    }
                    else
                    {
                        _attackPhase = AttackPhase.Charge;
                        _chargeTimer = 0f;
                        _hasDealtDamage = false;
                    }
                }
                return;
            }

            // ---- Phase 2 : Charge (lunge) ----
            if (_attackPhase == AttackPhase.Charge)
            {
                _chargeTimer += dt;

                if (!isbolet)
                {
                    // Déplacement en ligne droite dans la direction verrouillée.
                    Vector2 chargeMove = _lockedDirection * CHARGE_SPEED * dt;
                    Vector2 newPosX = WorldPos + new Vector2(chargeMove.X, 0);
                    if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                        WorldPos.X += chargeMove.X;
                    Vector2 newPosY = WorldPos + new Vector2(0, chargeMove.Y);
                    if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                        WorldPos.Y += chargeMove.Y;

                    if (_lockedDirection.X != 0)
                        Facing = Math.Sign(_lockedDirection.X);
                }
                else
                {
                    // Le bolet ne charge pas : il reste immobile et relâche simplement ses
                    // nuages de spores autour de lui (voir plus bas).
                    Speed = 0f;
                }

                // Infliger les dégâts une seule fois, en utilisant AttackHitRange.
                //  Le bolet ne vise pas un coup précis : une fois qu'il s'est arrêté pour
                // attaquer (télégraphie terminée), il relâche son nuage de spores quoi qu'il
                // arrive, même si la cible s'est entre-temps éloignée hors de portée.
                if (!_hasDealtDamage && (isbolet || distToPlayer < AttackHitRange))
                {
                    if (string.Equals(Species, "bolet", StringComparison.OrdinalIgnoreCase))
                    {
                        //  Le bolet ne frappe pas directement : il relâche une salve de
                        // nuages de spores empoisonnées autour de lui (voir
                        // Program.SpawnPoisonCloudBurst). Ce sont les nuages eux-mêmes qui
                        // empoisonnent les entités (dont le joueur) qui s'y attardent.
                        Program.SpawnPoisonCloudBurst(WorldPos, this, AiTargetConnectionId);
                        Program.NotifyPlayerAttackedByEntity(this, AiTargetConnectionId);
                    }
                    else
                    {
                        float poisonDuration = string.Equals(Species, "scorpio", StringComparison.OrdinalIgnoreCase) ? 6f : 0f;
                        if (_combatTargetEntity != null)
                            DamageCombatTarget(_combatTargetEntity);
                        else
                        {
                            Program.DamagePlayerTarget(Attack, AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species), poisonDuration);
                            Program.NotifyPlayerAttackedByEntity(this, AiTargetConnectionId);
                            Program.ApplyScreenShake(4f, 0.15f);
                        }
                    }
                    _hasDealtDamage = true;
                }

                if (_chargeTimer >= CHARGE_DURATION)
                {
                    _attackPhase = AttackPhase.Recovery;
                    _recoveryTimer = 0f;
                    Speed = 0f;
                    AnimState = "idle";
                }
                return;
            }

            // ---- Phase 3 : Recovery (immobile) ----
            if (_attackPhase == AttackPhase.Recovery)
            {
                _recoveryTimer += dt;
                AiTarget = targetPos;
                Speed = usesGoblinMeleeStyle ? Math.Max(BaseSpeed * 0.35f, 45f) : 0f;
                
                if (IsRidingMount)
                    AnimState = "sit";
                else
                    AnimState = usesGoblinMeleeStyle ? "walk" : "idle";

                if (usesGoblinMeleeStyle && !(IsMounted && Rider != null))
                {
                    PerformMovement(dt, destroyed, attackTriggerRange);
                }

                if (usesGoblinMeleeStyle && !_hasDealtDamage && _recoveryTimer >= 0.08f && distToPlayer < AttackHitRange)
                {
                    float poisonDuration = string.Equals(Species, "scorpio", StringComparison.OrdinalIgnoreCase) ? 6f : 0f;
                    if (_combatTargetEntity != null)
                        DamageCombatTarget(_combatTargetEntity);
                    else
                    {
                        Program.DamagePlayerTarget(Attack, AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species), poisonDuration);
                        Program.NotifyPlayerAttackedByEntity(this, AiTargetConnectionId);
                        Program.ApplyScreenShake(2f, 0.1f);
                    }
                    _hasDealtDamage = true;
                }

                if (_recoveryTimer >= RECOVERY_DURATION)
                {
                    _attackPhase = AttackPhase.None;
                    Speed = RunSpeed;
                    _postAttackCooldown = usesGoblinMeleeStyle ? 0.25f : 0.15f;
                }
                return;
            }

            // ---- Phase 0 : approche directe ----
            AiTarget = targetPos;
            if (_postAttackCooldown > 0f)
            {
                _postAttackCooldown -= dt;
                Speed = BaseSpeed;
                AnimState = "walk";
                return;
            }

            Vector2 dirToPlayer = targetPos - WorldPos;
            float distNow = dirToPlayer.Length();
            if (distNow > 0.01f) dirToPlayer /= distNow;
            else dirToPlayer = new Vector2(Facing, 0);

            Facing = dirToPlayer.X >= 0 ? 1f : -1f;

            if (distToPlayer > attackTriggerRange)
            {
                Speed = RunSpeed;
                PerformMovement(dt, destroyed, attackTriggerRange);
                AnimState = "run";
                return;
            }

            Speed = BaseSpeed;
            AnimState = "walk";

            if (distToPlayer < attackTriggerRange * 1.02f && _attackPhase == AttackPhase.None)
            {
                _attackPhase = AttackPhase.Telegraph;
                _telegraphTimer = 0f;
                Speed = usesGoblinMeleeStyle ? Math.Max(BaseSpeed * 0.5f, 60f) : 0f;
                AnimState = usesGoblinMeleeStyle ? "walk" : "attack";

                if (usesGoblinMeleeStyle)
                    PlayAttackSound();

                Vector2 dirToTarget = targetPos - WorldPos;
                if (dirToTarget.Length() > 0.01f)
                    _lockedDirection = Vector2.Normalize(dirToTarget);
                else
                    _lockedDirection = new Vector2(Facing, 0);

                _hasDealtDamage = false;
            }
        }

        // ==================== COMBAT À DISTANCE (arc) ====================
        //  Un tireur essaie de rester dans une fenêtre de distance confortable : s'il est trop
        // près il recule, trop loin il approche, et dans la bonne fenêtre il alterne entre
        // tourner autour de la cible et s'arrêter pour tirer une flèche.
        void RangedCombatBehavior(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            float distToPlayer = Vector2.Distance(WorldPos, playerPos);

            if (_rangedShootCooldown > 0f) _rangedShootCooldown -= dt;

            Vector2 dirToPlayer = playerPos - WorldPos;
            if (dirToPlayer.Length() > 0.01f) dirToPlayer = Vector2.Normalize(dirToPlayer);
            else dirToPlayer = new Vector2(Facing, 0);
            Facing = dirToPlayer.X >= 0 ? 1f : -1f;

            // ---- Tir en cours : immobile le temps de la télégraphie, puis lâche la flèche ----
            if (_rangedTelegraphing)
            {
                _rangedTelegraphTimer += dt;
                Speed = 0f;
                AnimState = IsRidingMount ? "sit" : "attack";

                if (_rangedTelegraphTimer >= RANGED_TELEGRAPH_DURATION)
                {
                    _rangedTelegraphing = false;
                    Vector2 shootDir = dirToPlayer;
                    Program.SpawnHostileArrow(WorldPos, shootDir, RANGED_ARROW_SPEED, Attack, _combatTargetEntity, this, AiTargetConnectionId);
                    _rangedShootCooldown = (float)(Random.Shared.NextDouble() * 0.6f + 1.0f); // 1–1.6s
                    // Après avoir tiré, on se replace un peu avant de retirer.
                    _combatMoveState = CombatMoveState.Strafe;
                    _combatDecisionTimer = 0.6f;
                }
                return;
            }

            // ---- Trop près : recul prioritaire ----
            if (distToPlayer < RANGED_RETREAT_DIST)
            {
                Speed = RunSpeed;
                Vector2 away = WorldPos - dirToPlayer * (Speed * dt);
                if (!World.IsCollidingEntity(away, destroyed, Species))
                    WorldPos = away;
                AnimState = IsRidingMount ? "sit" : "walk";
                return;
            }

            // ---- Trop loin : se rapprocher jusqu'à la fenêtre de tir ----
            if (distToPlayer > RANGED_PREFERRED_MAX)
            {
                AiTarget = playerPos;
                Speed = BaseSpeed;
                MoveTowardTarget(dt, destroyed, RANGED_PREFERRED_MAX * 0.8f);
                return;
            }

            // ---- Dans la fenêtre idéale : tourner autour de la cible, et de temps en temps
            // s'arrêter pour tirer ----
            _combatDecisionTimer -= dt;
            if (_combatDecisionTimer <= 0f)
            {
                _strafeDir = Random.Shared.NextDouble() < 0.5 ? 1 : -1;
                _combatDecisionTimer = (float)(Random.Shared.NextDouble() * 1.0f + 0.8f);
            }

            bool inShootWindow = distToPlayer >= RANGED_PREFERRED_MIN && distToPlayer <= RANGED_PREFERRED_MAX;
            if (inShootWindow && _rangedShootCooldown <= 0f)
            {
                _rangedTelegraphing = true;
                _rangedTelegraphTimer = 0f;
                Speed = 0f;
                AnimState = IsRidingMount ? "sit" : "attack";
                return;
            }

            Speed = BaseSpeed;
            Vector2 tangent = new Vector2(-dirToPlayer.Y, dirToPlayer.X) * _strafeDir;
            Vector2 strafePos = WorldPos + tangent * (Speed * dt);
            if (!World.IsCollidingEntity(strafePos, destroyed, Species))
                WorldPos = strafePos;
            AnimState = IsRidingMount ? "sit" : "walk";
        }

        // ==================== MÉTHODES PRIVÉES ====================
        private void UpdateKhamsinBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsBoss || !string.Equals(Species, "Khamsin", StringComparison.OrdinalIgnoreCase)) return;

            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            float distToPlayer = Vector2.Distance(WorldPos, playerPos);

            if (_khBossState == KhamsinBossState.Telegraph)
            {
                _khTelegraphTimer -= dt;
                Speed = 0f;
                AnimState = "attack";
                if (_khTelegraphTimer <= 0f)
                {
                    _khBossState = KhamsinBossState.Charge;
                    _khChargeTimer = KH_DASH_CHARGE_DURATION;
                    _khHasDealtDamage = false;
                    AnimState = "attack";
                }
                return;
            }

            if (_khBossState == KhamsinBossState.Charge)
            {
                _khChargeTimer -= dt;
                float brakeFactor = 1f;
                if (_khChargeTimer <= KH_DASH_BRAKE_DURATION)
                {
                    brakeFactor = Math.Clamp(_khChargeTimer / KH_DASH_BRAKE_DURATION, 0f, 1f);
                }

                Vector2 move = _khDashDirection * KH_DASH_SPEED * brakeFactor * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;

                if (_khDashDirection.X != 0f)
                    Facing = Math.Sign(_khDashDirection.X);

                if (!_khHasDealtDamage && distToPlayer < 48f)
                {
                    Program.DamagePlayerTarget(Math.Max(6, Attack), AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species));
                    Program.ApplyScreenShake(6f, 0.18f);
                    _khHasDealtDamage = true;
                }

                if (_khChargeTimer <= 0f)
                {
                    _khBossState = KhamsinBossState.Recover;
                    _khRecoveryTimer = KH_DASH_RECOVERY_DURATION;
                    _khHasTeleported = false;
                    Speed = 0f;
                    AnimState = "idle";
                }
                return;
            }

            if (_khBossState == KhamsinBossState.Recover)
            {
                _khRecoveryTimer -= dt;
                Speed = 0f;
                AnimState = "idle";
                if (_khRecoveryTimer <= 0f)
                {
                    if (!_khHasTeleported)
                    {
                        TeleportKhamsinAroundPlayer(playerPos, destroyed);
                        Program.ApplyScreenShake(3.2f, 0.12f);
                        _khHasTeleported = true;
                        _khRecoveryTimer = KH_DASH_RECOVERY_DURATION;
                    }
                    else if (_khDashBurstRemaining > 0)
                    {
                        StartKhamsinDash(playerPos, hpPercent);
                        _khDashBurstRemaining--;
                    }
                    else if (_khVolleyBurstRemaining > 0)
                    {
                        StartKhamsinVolley(playerPos, hpPercent);
                        _khVolleyBurstRemaining--;
                    }
                    else
                    {
                        _khBossState = KhamsinBossState.Idle;
                        _khDashCooldown = MathHelper.Lerp(0.7f, 0.3f, 1f - hpPercent);
                        _khDashBurstRemaining = GetDashBurstCount(hpPercent);
                        _khVolleyBurstRemaining = GetVolleyBurstCount(hpPercent);
                        Speed = 0f;
                    }
                }
                return;
            }

            if (_khBossState == KhamsinBossState.Volley)
            {
                _khVolleyTimer -= dt;
                Speed = 0f;
                AnimState = "idle";
                if (_khVolleyTimer <= 0f)
                {
                    _khVolleyShotsRemaining--;
                    if (_khVolleyShotsRemaining > 0)
                    {
                        FireKhamsinSalvo(playerPos);
                        _khVolleyTimer = MathHelper.Lerp(0.32f, 0.16f, 1f - hpPercent);
                    }
                    else if (_khVolleyBurstRemaining > 0)
                    {
                        StartKhamsinVolley(playerPos, hpPercent);
                        _khVolleyBurstRemaining--;
                    }
                    else
                    {
                        _khBossState = KhamsinBossState.Idle;
                        _khDashCooldown = MathHelper.Lerp(0.7f, 0.3f, 1f - hpPercent);
                        _khDashBurstRemaining = GetDashBurstCount(hpPercent);
                        _khVolleyBurstRemaining = GetVolleyBurstCount(hpPercent);
                    }
                }
                return;
            }

            if (_khDashCooldown > 0f) _khDashCooldown -= dt;
            if (_khBossState == KhamsinBossState.Idle)
            {
                if (_khDashBurstRemaining <= 0 && _khVolleyBurstRemaining <= 0)
                {
                    _khDashBurstRemaining = GetDashBurstCount(hpPercent);
                    _khVolleyBurstRemaining = GetVolleyBurstCount(hpPercent);
                }

                if (_khDashBurstRemaining > 0 && _khDashCooldown <= 0f)
                {
                    StartKhamsinDash(playerPos, hpPercent);
                    _khDashBurstRemaining--;
                    return;
                }
            }

            if (distToPlayer > 70f)
            {
                Vector2 dir = playerPos - WorldPos;
                if (dir.Length() > 0.01f)
                    dir = Vector2.Normalize(dir);
                else
                    dir = new Vector2(1f, 0f);

                Vector2 move = dir * Math.Min(RunSpeed, 140f) * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;
                Facing = dir.X >= 0f ? 1f : -1f;
                AnimState = "run";
            }
            else
            {
                AnimState = "idle";
            }
        }

        private void FireKhamsinSalvo(Vector2 playerPos)
        {
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            int count = Math.Clamp(4 + (int)Math.Round((1f - hpPercent) * 8f), 4, 12);
            float speed = 240f + (1f - hpPercent) * 120f;
            Color color = new Color(255, 220, 180, 255);

            for (int i = 0; i < count; i++)
            {
                float angle = i * (MathF.PI * 2f / count);
                Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = new KhamsinProjectile(WorldPos, dir, speed, Math.Max(4f, Attack * 0.6f), color, color);
                Program.AddSpellProjectile(projectile);
            }

            Program.ApplyScreenShake(2.4f, 0.1f);
        }

        private void StartKhamsinDash(Vector2 playerPos, float hpPercent)
        {
            _khBossState = KhamsinBossState.Telegraph;
            _khTelegraphTimer = KH_DASH_TELEGRAPH_DURATION;
            _khHasTeleported = false;
            Vector2 direction = playerPos - WorldPos;
            if (direction.Length() > 0.01f)
                direction = Vector2.Normalize(direction);
            else
                direction = new Vector2(Facing, 0f);
            _khDashDirection = direction;
            // Augmenter la distance du dash de 1.5x pour un dash plus long
            float dashDistance = (260f + (1f - hpPercent) * 190f) * 2f * 1.5f;
            _khDashTarget = playerPos + direction * dashDistance;
            _khHasDealtDamage = false;
            AnimState = "attack";
            Speed = 0f;
        }

        private void StartKhamsinVolley(Vector2 playerPos, float hpPercent)
        {
            _khBossState = KhamsinBossState.Volley;
            _khVolleyShotsRemaining = Math.Clamp(3 + (int)Math.Round((1f - hpPercent) * 2f), 3, 5);
            _khVolleyTimer = MathHelper.Lerp(0.36f, 0.16f, 1f - hpPercent);
            FireKhamsinSalvo(playerPos);
            _khVolleyShotsRemaining--;
            Speed = 0f;
            AnimState = "idle";
        }

        private int GetDashBurstCount(float hpPercent)
        {
            return 3 + (int)Math.Round((1f - hpPercent) * 2f);
        }

        private int GetVolleyBurstCount(float hpPercent)
        {
            return 2 + (int)Math.Round((1f - hpPercent) * 1f);
        }

        private void TeleportKhamsinAroundPlayer(Vector2 playerPos, HashSet<string> destroyed)
        {
            SpawnKhamsinSmoke(WorldPos, 1f);
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = 150f + (float)(Random.Shared.NextDouble() * 70f);
                Vector2 candidate = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(candidate.X / Program.TileSize);
                int tileY = (int)(candidate.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;
                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species)) continue;
                WorldPos = candidate;
                SpawnKhamsinSmoke(candidate, 1f);
                return;
            }
        }

        public static void SpawnKhamsinSmoke(Vector2 pos, float intensity)
        {
            int count = (int)Math.Round(36f + intensity * 28f);
            float spreadX = 34f + intensity * 18f;
            float spreadY = 56f + intensity * 28f;
            float baseHeight = pos.Y - 36f;

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 30f + (float)(Random.Shared.NextDouble() * 110f) * intensity;
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                velocity.Y -= 18f + (float)(Random.Shared.NextDouble() * 12f);

                float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * spreadX;
                float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * spreadY;
                Vector2 spawnPos = new Vector2(pos.X + offsetX, baseHeight + offsetY);

                float size = 8f + (float)(Random.Shared.NextDouble() * 12f) + intensity * 6f;
                float lifetime = 0.35f + (float)(Random.Shared.NextDouble() * 0.45f);
                Program.GetParticleList().Add(new Particle(spawnPos, velocity, new Color(90, 90, 90, 220), size, lifetime));
            }
        }

        // ==================== GENIE BOSS : state machine ====================
        //  Boss en 3 phases, basées sur le pourcentage de vie restant :
        //   Phase 1 (100% -> 65%) "Le Marchand d'illusions" : téléportations, clones illusoires, volées de projectiles
        //   Phase 2 (65% -> 30%)  "Tempête de sable" : tourbillons de sable au sol + volées plus rapides
        //   Phase 3 (30% -> 0%)   "Les 3 voeux" (enrage) : + vitesse, sol de lave, laser tournant, voeu ultime
        private void UpdateGenieBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsGenie) return;

            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            float distToPlayer = Vector2.Distance(WorldPos, playerPos);

            UpdateGenieHazardZones(dt, playerPos);
            UpdateGenieClones(dt);

            // ---- Transitions de phase ----
            if (_gePhase == 1 && hpPercent <= 0.65f)
            {
                _gePhase = 2;
            }
            if (_gePhase == 2 && hpPercent <= 0.30f)
            {
                _gePhase = 3;
            }
            if (_gePhase >= 2 && !_gePhase2Announced)
            {
                _gePhase2Announced = true;
                Program.AddNotification(new Notification(" Le Génie invoque une tempête de sable !", new Color(230, 190, 110, 255), 3f));
                Program.ApplyScreenShake(4f, 0.25f);
                SpawnGenieSmoke(WorldPos, 1.3f);
            }
            if (_gePhase >= 3 && !_gePhase3Announced)
            {
                _gePhase3Announced = true;
                Program.AddNotification(new Notification(" \"TROIS VOEUX, TROIS CHÂTIMENTS !\" Le Génie entre en fureur !", new Color(255, 90, 60, 255), 3.5f));
                Program.ApplyScreenShake(7f, 0.35f);
                SpawnGenieSmoke(WorldPos, 2f);
                RunSpeed *= 1.4f;
            }

            switch (_geState)
            {
                case GenieBossState.Teleport:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "idle";
                        if (_geStateTimer <= 0f)
                        {
                            TeleportGenieAroundPlayer(playerPos, destroyed);
                            Program.ApplyScreenShake(2.6f, 0.12f);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.35f;
                        }
                        return;
                    }
                case GenieBossState.ClonesTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            SpawnGenieClones(playerPos, destroyed);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.5f;
                        }
                        return;
                    }
                case GenieBossState.FanTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            _geState = GenieBossState.FanFiring;
                            _geFanShotsRemaining = _gePhase == 1 ? 2 : (_gePhase == 2 ? 3 : 4);
                            _geFanShotTimer = 0f;
                        }
                        return;
                    }
                case GenieBossState.FanFiring:
                    {
                        Speed = 0f;
                        AnimState = "attack";
                        _geFanShotTimer -= dt;
                        if (_geFanShotTimer <= 0f)
                        {
                            FireGenieSalvo(playerPos);
                            _geFanShotsRemaining--;
                            _geFanShotTimer = MathHelper.Lerp(0.42f, 0.22f, 1f - hpPercent);
                            if (_geFanShotsRemaining <= 0)
                            {
                                _geState = GenieBossState.Recover;
                                _geStateTimer = 0.45f;
                            }
                        }
                        return;
                    }
                case GenieBossState.SandTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            SpawnGenieHazardWave(playerPos, isSand: true);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.5f;
                        }
                        return;
                    }
                case GenieBossState.LavaTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            SpawnGenieHazardWave(playerPos, isSand: false);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.5f;
                        }
                        return;
                    }
                case GenieBossState.LaserTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        SpawnGenieSmoke(WorldPos, 0.35f);
                        if (_geStateTimer <= 0f)
                        {
                            _geState = GenieBossState.LaserSpinning;
                            _geLaserAngle = 0f;
                            _geLaserTimer = 0f;
                            _geLaserSpins = 0;
                            Program.ApplyScreenShake(3f, 0.15f);
                        }
                        return;
                    }
                case GenieBossState.LaserSpinning:
                    {
                        Speed = 0f;
                        AnimState = "attack";
                        _geLaserTimer -= dt;
                        _geLaserAngle += dt * 3.4f; // vitesse de rotation du laser
                        if (_geLaserTimer <= 0f)
                        {
                            FireGenieLaserTick();
                            _geLaserTimer = 0.06f;
                            _geLaserSpins++;
                        }
                        if (_geLaserSpins >= 55) // ~3.3s de rotation
                        {
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.6f;
                        }
                        return;
                    }
                case GenieBossState.WishTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if ((int)(_geStateTimer * 10f) % 2 == 0)
                            SpawnGenieWishWarningParticles(_geWishTargetPos);
                        if (_geStateTimer <= 0f)
                        {
                            ExecuteGenieWishStrike(playerPos);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.9f;
                        }
                        return;
                    }
                case GenieBossState.Recover:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "idle";
                        if (_geStateTimer <= 0f)
                        {
                            _geState = GenieBossState.Idle;
                            _geAttackCooldown = _gePhase == 1
                                ? MathHelper.Lerp(1.4f, 0.9f, 1f - hpPercent)
                                : (_gePhase == 2 ? MathHelper.Lerp(1.0f, 0.6f, 1f - hpPercent) : MathHelper.Lerp(0.7f, 0.35f, 1f - hpPercent));
                        }
                        return;
                    }
            }

            // ---- État Idle : se replace, puis lance la prochaine attaque selon la phase ----
            if (_geAttackCooldown > 0f) _geAttackCooldown -= dt;
            if (_geAttackCooldown <= 0f)
            {
                StartNextGenieAttack(playerPos, destroyed, hpPercent);
                return;
            }

            // Se maintient à distance moyenne du joueur entre deux attaques (comme un lanceur de sorts)
            float desiredDist = _gePhase >= 3 ? 170f : 210f;
            if (distToPlayer > desiredDist + 40f)
            {
                Vector2 dir = Vector2.Normalize(playerPos - WorldPos == Vector2.Zero ? new Vector2(1, 0) : playerPos - WorldPos);
                Vector2 move = dir * Math.Min(RunSpeed, 160f) * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species)) WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species)) WorldPos.Y += move.Y;
                Facing = dir.X >= 0f ? 1f : -1f;
                AnimState = "run";
            }
            else if (distToPlayer < desiredDist - 40f)
            {
                Vector2 dir = Vector2.Normalize(WorldPos - playerPos == Vector2.Zero ? new Vector2(1, 0) : WorldPos - playerPos);
                Vector2 move = dir * Math.Min(RunSpeed, 130f) * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species)) WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species)) WorldPos.Y += move.Y;
                Facing = dir.X <= 0f ? 1f : -1f; // reste tourné vers le joueur en reculant
                AnimState = "walk";
            }
            else
            {
                Facing = (playerPos.X - WorldPos.X) >= 0f ? 1f : -1f;
                AnimState = "idle";
            }
        }

        private void StartNextGenieAttack(Vector2 playerPos, HashSet<string> destroyed, float hpPercent)
        {
            List<GenieBossState> pool;
            if (_gePhase == 1)
                pool = new List<GenieBossState> { GenieBossState.Teleport, GenieBossState.Teleport, GenieBossState.ClonesTelegraph, GenieBossState.FanTelegraph };
            else if (_gePhase == 2)
                pool = new List<GenieBossState> { GenieBossState.Teleport, GenieBossState.FanTelegraph, GenieBossState.SandTelegraph, GenieBossState.SandTelegraph, GenieBossState.ClonesTelegraph };
            else
            {
                pool = new List<GenieBossState> { GenieBossState.Teleport, GenieBossState.FanTelegraph, GenieBossState.LavaTelegraph, GenieBossState.LaserTelegraph };
                // Le voeu ultime ne revient qu'une fois toutes les ~4 attaques pour rester spectaculaire sans spammer
                if (Random.Shared.Next(0, 4) == 0) pool.Add(GenieBossState.WishTelegraph);
            }

            GenieBossState chosen = pool[Random.Shared.Next(pool.Count)];
            switch (chosen)
            {
                case GenieBossState.Teleport:
                    _geState = GenieBossState.Teleport;
                    _geStateTimer = 0.25f;
                    SpawnGenieSmoke(WorldPos, 0.6f);
                    break;
                case GenieBossState.ClonesTelegraph:
                    _geState = GenieBossState.ClonesTelegraph;
                    _geStateTimer = 0.55f;
                    break;
                case GenieBossState.FanTelegraph:
                    _geState = GenieBossState.FanTelegraph;
                    _geStateTimer = 0.4f;
                    break;
                case GenieBossState.SandTelegraph:
                    _geState = GenieBossState.SandTelegraph;
                    _geStateTimer = 0.5f;
                    break;
                case GenieBossState.LavaTelegraph:
                    _geState = GenieBossState.LavaTelegraph;
                    _geStateTimer = 0.45f;
                    break;
                case GenieBossState.LaserTelegraph:
                    _geState = GenieBossState.LaserTelegraph;
                    _geStateTimer = 0.65f;
                    break;
                case GenieBossState.WishTelegraph:
                    _geState = GenieBossState.WishTelegraph;
                    _geStateTimer = 1.3f;
                    _geWishTargetPos = playerPos;
                    Program.AddNotification(new Notification(" Le Génie formule un voeu... ÉLOIGNEZ-VOUS !", new Color(255, 120, 60, 255), 1.6f));
                    break;
            }
        }

        private void TeleportGenieAroundPlayer(Vector2 playerPos, HashSet<string> destroyed)
        {
            SpawnGenieSmoke(WorldPos, 1f);
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = GE_TELEPORT_MIN_DIST + (float)(Random.Shared.NextDouble() * (GE_TELEPORT_MAX_DIST - GE_TELEPORT_MIN_DIST));
                Vector2 candidate = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(candidate.X / Program.TileSize);
                int tileY = (int)(candidate.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;
                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species)) continue;
                WorldPos = candidate;
                SpawnGenieSmoke(candidate, 1f);
                return;
            }
        }

        private void SpawnGenieClones(Vector2 playerPos, HashSet<string> destroyed)
        {
            int cloneCount = _gePhase == 1 ? 2 : 3;
            for (int i = 0; i < cloneCount; i++)
            {
                float angle = (float)(i * (Math.PI * 2f / cloneCount) + Random.Shared.NextDouble() * 0.6);
                float dist = 90f + (float)(Random.Shared.NextDouble() * 60f);
                Vector2 pos = WorldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(pos.X / Program.TileSize);
                int tileY = (int)(pos.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;

                var clone = new Entity(pos, "Genie", false)
                {
                    CustomName = "Illusion du Génie",
                    IsGenieIllusion = true,
                    IsBoss = false,
                    Behavior = "hostile",
                    Attack = Math.Max(2, Attack / 3),
                    VisionRange = 6,
                    AttackHitRange = 42f,
                    AlertDuration = 0.4f,
                    Speed = 140f,
                    BaseSpeed = 140f,
                    RunSpeed = 180f,
                    Scale = 1.05f,
                    // léger scintillement pour distinguer une illusion du vrai génie, comme demandé
                    Tint = new Color(255, 255, 255, 210)
                };
                clone.SetHealthSilently(1, 1);
                Program.GetEntities().Add(clone);
                var chunk = World.GetChunkAt(tileX, tileY);
                if (chunk != null) chunk.Entities.Add(clone);
                _geClones.Add(clone);
                SpawnGenieSmoke(pos, 0.7f);
            }
            _geCloneCleanupTimer = 7f;
        }

        private void UpdateGenieClones(float dt)
        {
            if (_geClones.Count == 0) return;
            _geCloneCleanupTimer -= dt;
            if (_geCloneCleanupTimer <= 0f)
            {
                foreach (var clone in _geClones)
                {
                    if (clone != null && clone.IsAlive)
                    {
                        SpawnGenieSmoke(clone.WorldPos, 0.5f);
                        clone.SetHealthSilently(0, clone.MaxHP); // se dissipe, comme une illusion qui s'évanouit
                    }
                }
                _geClones.Clear();
            }
            else
            {
                _geClones.RemoveAll(c => c == null || !c.IsAlive);
            }
        }

        private void FireGenieSalvo(Vector2 playerPos)
        {
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            int count = Math.Clamp(5 + (int)Math.Round((1f - hpPercent) * 7f), 5, 12);
            float speed = 230f + (1f - hpPercent) * 130f;
            Color color = new Color(230, 190, 255, 255); // violet-doré, distinct des projectiles de Khamsin

            Vector2 toPlayer = playerPos - WorldPos;
            float baseAngle = toPlayer.Length() > 0.01f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : 0f;
            float spread = MathF.PI * 0.6f; // éventail dirigé vers le joueur plutôt qu'un cercle complet

            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0.5f : (float)i / (count - 1);
                float angle = baseAngle - spread / 2f + spread * t;
                Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = new KhamsinProjectile(WorldPos, dir, speed, Math.Max(5f, Attack * 0.5f), color, color);
                Program.AddSpellProjectile(projectile);
            }
            Program.ApplyScreenShake(2f, 0.08f);
        }

        private void FireGenieLaserTick()
        {
            // 4 faisceaux en croix qui tournent ensemble : un rayon "laser" simulé par une rafale
            // continue de petits projectiles rapides, alignés sur _geLaserAngle.
            Color color = new Color(255, 230, 120, 255);
            float speed = 420f;
            for (int arm = 0; arm < 4; arm++)
            {
                float angle = _geLaserAngle + arm * (MathF.PI / 2f);
                Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = new KhamsinProjectile(WorldPos, dir, speed, Math.Max(4f, Attack * 0.4f), color, color);
                Program.AddSpellProjectile(projectile);
            }
        }

        private void SpawnGenieHazardWave(Vector2 playerPos, bool isSand)
        {
            int count = isSand ? (4 + Random.Shared.Next(0, 2)) : (5 + Random.Shared.Next(0, 2));
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = (float)(Random.Shared.NextDouble() * 220f);
                Vector2 pos = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                _geHazardZones.Add(new GenieHazardZone
                {
                    Pos = pos,
                    Radius = isSand ? 65f : 78f,
                    TelegraphTimer = isSand ? 0.9f : 0.55f,
                    ActiveTimer = isSand ? 2.4f : 2.8f,
                    IsSand = isSand,
                    DamageTickTimer = 0f
                });
            }
        }

        private void UpdateGenieHazardZones(float dt, Vector2 playerPos)
        {
            if (_geHazardZones.Count == 0) return;
            for (int i = _geHazardZones.Count - 1; i >= 0; i--)
            {
                var zone = _geHazardZones[i];
                if (zone.TelegraphTimer > 0f)
                {
                    zone.TelegraphTimer -= dt;
                    if (Random.Shared.NextDouble() < 0.3)
                    {
                        Color warnColor = zone.IsSand ? new Color(210, 180, 120, 160) : new Color(255, 110, 40, 160);
                        Program.GetParticleList().Add(new Particle(
                            zone.Pos + new Vector2((float)(Random.Shared.NextDouble() - 0.5) * zone.Radius, (float)(Random.Shared.NextDouble() - 0.5) * zone.Radius * 0.5f),
                            new Vector2(0, -12f), warnColor, 5f, 0.3f));
                    }
                    if (zone.TelegraphTimer <= 0f) { /* devient active dès la frame suivante */ }
                    continue;
                }

                if (zone.ActiveTimer > 0f)
                {
                    zone.ActiveTimer -= dt;
                    zone.DamageTickTimer -= dt;

                    // Particules d'ambiance de la zone active
                    if (Random.Shared.NextDouble() < 0.5)
                    {
                        Color fxColor = zone.IsSand ? new Color(220, 190, 130, 200) : new Color(255, 130, 30, 220);
                        float rx = (float)(Random.Shared.NextDouble() - 0.5) * zone.Radius * 1.6f;
                        float ry = (float)(Random.Shared.NextDouble() - 0.5) * zone.Radius * 0.8f;
                        Program.GetParticleList().Add(new Particle(zone.Pos + new Vector2(rx, ry), new Vector2(0, -20f), fxColor, 6f, 0.4f));
                    }

                    float dist = Vector2.Distance(zone.Pos, playerPos);
                    if (dist <= zone.Radius && zone.DamageTickTimer <= 0f)
                    {
                        float dmg = zone.IsSand ? Math.Max(3f, Attack * 0.25f) : Math.Max(6f, Attack * 0.5f);
                        Program.DamagePlayerTarget((int)MathF.Ceiling(dmg), AiTargetConnectionId, zone.IsSand ? "un tourbillon de sable" : "une flaque de lave", Program.InferDeathCauseFromSpecies(zone.IsSand ? "worm" : "khamsin"));
                        zone.DamageTickTimer = 0.5f;
                    }
                }

                if (zone.IsExpired) _geHazardZones.RemoveAt(i);
            }
        }

        private void SpawnGenieWishWarningParticles(Vector2 pos)
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = 60f + (float)(Random.Shared.NextDouble() * 40f);
                Vector2 spawnPos = pos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                Vector2 velocity = (pos - spawnPos) * 1.8f; // converge vers le centre : lecture claire du danger
                Program.GetParticleList().Add(new Particle(spawnPos, velocity, new Color(255, 90, 40, 220), 6f, 0.35f));
            }
        }

        private void ExecuteGenieWishStrike(Vector2 playerPos)
        {
            float radius = 130f;
            SpawnGenieSmoke(_geWishTargetPos, 2.2f);
            Program.ApplyScreenShake(9f, 0.4f);

            for (int i = 0; i < 40; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 80f + (float)(Random.Shared.NextDouble() * 220f);
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                Program.GetParticleList().Add(new Particle(_geWishTargetPos, velocity, new Color(255, 200, 90, 255), 9f, 0.5f));
            }

            float dist = Vector2.Distance(_geWishTargetPos, playerPos);
            if (dist <= radius)
            {
                float dmg = Math.Max(25f, Attack * 2.2f);
                Program.DamagePlayerTarget((int)MathF.Ceiling(dmg), AiTargetConnectionId, "le voeu du Génie", Program.InferDeathCauseFromSpecies("khamsin"));
            }
        }

        public static void SpawnGenieSmoke(Vector2 pos, float intensity)
        {
            int count = (int)Math.Round(30f + intensity * 24f);
            float spreadX = 30f + intensity * 16f;
            float spreadY = 50f + intensity * 24f;
            float baseHeight = pos.Y - 34f;

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 26f + (float)(Random.Shared.NextDouble() * 100f) * intensity;
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                velocity.Y -= 16f + (float)(Random.Shared.NextDouble() * 14f);

                float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * spreadX;
                float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * spreadY;
                Vector2 spawnPos = new Vector2(pos.X + offsetX, baseHeight + offsetY);

                float size = 7f + (float)(Random.Shared.NextDouble() * 11f) + intensity * 5f;
                float lifetime = 0.35f + (float)(Random.Shared.NextDouble() * 0.4f);
                // Fumée dorée/violette caractéristique du Génie, plutôt que le gris de Khamsin
                Color color = Random.Shared.NextDouble() < 0.5
                    ? new Color(255, 210, 110, 220)
                    : new Color(190, 120, 220, 210);
                Program.GetParticleList().Add(new Particle(spawnPos, velocity, color, size, lifetime));
            }
        }

        // ==================== WORM BOSS : state machine ====================
        private void UpdateWormBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsWorm) return;
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);

            // Initialiser le son de rumbling si nécessaire
            InitializeWormRumbleSound();

            switch (_wmState)
            {
                case WormBossState.Underground:
                {
                    // Suit le joueur sous terre (position logique invisible), pas de rendu du corps.
                    Speed = 0f;
                    AnimState = "idle";
                    _wmTimer -= dt;
                    if (_wmTimer <= 0f)
                    {
                        StartWormTelegraph(playerPos, destroyed);
                    }
                    return;
                }

                case WormBossState.Telegraph:
                {
                    // Le sol tremble au point de sortie : le joueur voit venir l'attaque et peut s'écarter.
                    _wmTimer -= dt;
                    Speed = 0f;

                    // RUMBLING : son de tremblement de sol
                    // Le son s'intensifie plus on approche de la fin du telegraph
                    float telegraphProgress = 1f - (_wmTimer / WM_TELEGRAPH_DURATION);
                    float rumbleVolume = 0.3f + telegraphProgress * 0.9f; // 0.3 -> 1.2
                    float rumblePitch = 0.8f + telegraphProgress * 0.3f;   // 0.8 -> 1.1

                    // Jouer le rumbling toutes les 0.3 secondes pendant la télégraphie
                    if ((int)(_wmTimer * 10f) % 3 == 0)
                    {
                        PlayWormRumbleSound(rumbleVolume, rumblePitch);
                    }

                    // Effet de secousse du sol
                    if ((int)(_wmTimer * 10f) % 2 == 0)
                    {
                        Program.ApplyScreenShake(1.2f + telegraphProgress * 0.8f, 0.05f);
                    }

                    if (_wmTimer <= 0f)
                    {
                        _wmState = WormBossState.Emerging;
                        _wmTimer = 0f;
                        _wmEntryPointLocked = true; // la tête sort : plus de changement de point d'entrée
                        _wmHasDealtDamage = false;
                        WorldPos = _wmExitPoint;
                        AnimState = "attack";

                        // Jouer un dernier rumbling fort juste avant l'émergence
                        PlayWormRumbleSound(1.5f, 1.1f);

                        // Décalage temporel entre segments, calculé pour viser ~WM_SEGMENT_SPACING px
                        // entre deux segments consécutifs le long du trajet sortie -> entrée.
                        float horizontalDist = Vector2.Distance(_wmExitPoint, _wmEntryPoint);
                        float horizontalSpeed = horizontalDist / Math.Max(0.05f, WM_HEAD_LEAP_DURATION);
                        _wmSegmentStagger = WM_SEGMENT_SPACING / Math.Max(1f, horizontalSpeed);
                        // Durée totale de la manœuvre : le temps pour la tête + le retard cumulé du
                        // dernier segment, pour qu'il ait lui aussi le temps de finir SON propre arc.
                        _wmTotalFlightDuration = WM_HEAD_LEAP_DURATION + (WM_SEGMENT_COUNT - 1) * _wmSegmentStagger;

                        SpawnKhamsinSmoke(_wmExitPoint, 1.4f); // réutilise le FX de poussière existant
                        Program.ApplyScreenShake(4f, 0.15f);
                    }
                    return;
                }

                case WormBossState.Emerging:
                case WormBossState.Airborne:
                case WormBossState.Diving:
                {
                    _wmTimer += dt;

                    // La tête suit son propre arc parabolique (sortie -> entrée), et se fige une fois
                    // arrivée : elle ne "recule" pas pendant que les segments suivants finissent le leur.
                    float headT = Math.Clamp(_wmTimer / WM_HEAD_LEAP_DURATION, 0f, 1f);
                    Vector2 flatPos = Vector2.Lerp(_wmExitPoint, _wmEntryPoint, headT);
                    float arc = 4f * WM_ARC_HEIGHT * headT * (1f - headT); // 0 aux extrémités, max au milieu
                    Vector2 headPos = new Vector2(flatPos.X, flatPos.Y - arc);
                    WorldPos = headPos;

                    if (headT < 0.35f) _wmState = WormBossState.Emerging;
                    else if (headT > 0.65f) _wmState = WormBossState.Diving;
                    else _wmState = WormBossState.Airborne;

                    // Dégâts si la tête (ou son voisinage immédiat) passe trop près du joueur pendant le saut.
                    if (!_wmHasDealtDamage && headT < 1f)
                    {
                        if (Vector2.DistanceSquared(headPos, playerPos) < (WM_DAMAGE_RADIUS) * (WM_DAMAGE_RADIUS))
                        {
                            Program.DamagePlayerTarget(Math.Max(8, Attack), AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species));
                            Program.ApplyScreenShake(6f, 0.18f);
                            _wmHasDealtDamage = true;
                        }
                    }

                    Facing = (_wmEntryPoint.X - _wmExitPoint.X) >= 0f ? 1f : -1f;
                    AnimState = "attack";

                    // La manœuvre se termine seulement quand le DERNIER segment a fini son propre arc
                    // (donc a vraiment touché le sol) — voir EntityRenderer.DrawWormBoss pour le détail
                    // du calcul par segment (décalé de i * WormSegmentStagger).
                    if (_wmTimer >= _wmTotalFlightDuration)
                    {
                        _wmState = WormBossState.Recover;
                        _wmTimer = WM_RECOVER_DURATION;
                        _wmEntryPointLocked = false;
                        WorldPos = _wmEntryPoint;
                        SpawnKhamsinSmoke(_wmEntryPoint, 1.4f);
                        Program.ApplyScreenShake(4f, 0.15f);
                    }
                    return;
                }

                case WormBossState.Recover:
                {
                    Speed = 0f;
                    AnimState = "idle";
                    _wmTimer -= dt;
                    if (_wmTimer <= 0f)
                    {
                        _wmState = WormBossState.Underground;
                        // Cooldown sous terre plus court à bas HP -> boss plus agressif en fin de combat.
                        _wmTimer = MathHelper.Lerp(0.7f, 0.25f, 1f - hpPercent);
                    }
                    return;
                }
            }
        }

        private void UpdateSlimeMovement(float dt, HashSet<string> destroyed)
        {
            if (!IsSlime) return;

            switch (_slimeJumpState)
            {
                case SlimeJumpState.Grounded:
                    // Si on a une cible et qu'elle est assez éloignée, on lance un saut
                    if (AiTarget != Vector2.Zero && Vector2.DistanceSquared(WorldPos, AiTarget) > (SLIME_MIN_JUMP_DIST) * (SLIME_MIN_JUMP_DIST))
                    {
                        _slimeJumpState = SlimeJumpState.Charging;
                        _slimeChargeTimer = SLIME_CHARGE_DURATION;
                        _slimeJumpStartPos = WorldPos;
                        // Clamp target to maximum jump distance
                        Vector2 toTarget = AiTarget - WorldPos;
                        float dist = toTarget.Length();
                        if (dist > SLIME_MAX_JUMP_DIST && dist > 0.01f)
                        {
                            toTarget = Vector2.Normalize(toTarget) * SLIME_MAX_JUMP_DIST;
                            _slimeJumpTargetPos = WorldPos + toTarget;
                        }
                        else
                        {
                            _slimeJumpTargetPos = AiTarget;
                        }
                    }
                    break;

                case SlimeJumpState.Charging:
                    _slimeChargeTimer -= dt;
                    if (_slimeChargeTimer <= 0f)
                    {
                        _slimeJumpState = SlimeJumpState.Jumping;
                        _slimeJumpProgress = 0f;
                        // On peut aussi ajuster la cible si elle a changé entre-temps,
                        // en la clampant à la portée maximale du slime.
                        Vector2 toTarget2 = AiTarget - _slimeJumpStartPos;
                        float dist2 = toTarget2.Length();
                        if (dist2 > SLIME_MAX_JUMP_DIST && dist2 > 0.01f)
                        {
                            toTarget2 = Vector2.Normalize(toTarget2) * SLIME_MAX_JUMP_DIST;
                            _slimeJumpTargetPos = _slimeJumpStartPos + toTarget2;
                        }
                        else
                        {
                            _slimeJumpTargetPos = AiTarget;
                        }
                    }
                    break;

                case SlimeJumpState.Jumping:
                    _slimeJumpProgress += dt / SLIME_JUMP_DURATION;
                    if (_slimeJumpProgress >= 1f)
                    {
                        _slimeJumpProgress = 1f;
                        _slimeJumpState = SlimeJumpState.Landing;
                        _slimeLandingTimer = SLIME_LANDING_DURATION;
                        _slimeHeight = 0f;
                        WorldPos = _slimeJumpTargetPos;
                    }
                    else
                    {
                        // Position horizontale interpolée
                        Vector2 newPos = Vector2.Lerp(_slimeJumpStartPos, _slimeJumpTargetPos, _slimeJumpProgress);
                        // Vérification des collisions horizontales (optionnel)
                        Vector2 move = newPos - WorldPos;
                        Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                        if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                            WorldPos.X += move.X;
                        Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                        if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                            WorldPos.Y += move.Y;

                        // Hauteur parabolique
                        float t = _slimeJumpProgress;
                        _slimeHeight = 4f * SLIME_ARC_HEIGHT * t * (1f - t);
                    }
                    break;

                case SlimeJumpState.Landing:
                    _slimeLandingTimer -= dt;
                    if (_slimeLandingTimer <= 0f)
                    {
                        _slimeJumpState = SlimeJumpState.Grounded;
                        _slimeHeight = 0f;
                        // On peut décider d'une nouvelle cible après atterrissage (ex: Wander)
                    }
                    break;
            }

            // Mise à jour de l'orientation (face à la cible si on saute)
            if (AiTarget != Vector2.Zero && _slimeJumpState != SlimeJumpState.Grounded)
            {
                float dx = AiTarget.X - WorldPos.X;
                if (MathF.Abs(dx) > 0.01f)
                    Facing = dx >= 0f ? 1f : -1f;
            }

            // Pas d'animation spéciale, on garde "idle"
            AnimState = "idle";
            Speed = 0f; // pas de déplacement linéaire
        }

        //  Déclenche un envol si l'oiseau est actuellement au sol. Appelée depuis Flee()
        // (fuite = décollage quasi systématique) et depuis Wander()/Decide() (envol spontané,
        // plus rare, pour donner un peu de vie aux abords même sans menace).
        private void StartBirdTakeoff(Vector2? preferredDir = null)
        {
            if (!IsBird || _birdFlightState != BirdFlightState.Grounded) return;

            _birdFlightState = BirdFlightState.TakingOff;

            // Cap initial : dans la direction de fuite si on décolle pour échapper au joueur,
            // sinon dans la direction actuellement "regardée" (Facing), avec une petite
            // variation aléatoire pour ne pas avoir des envols parfaitement rectilignes.
            Vector2 baseDir = preferredDir.HasValue && preferredDir.Value.LengthSquared() > 0.0001f
                ? Vector2.Normalize(preferredDir.Value)
                : new Vector2(Facing, 0f);
            float baseAngle = MathF.Atan2(baseDir.Y, baseDir.X);
            baseAngle += (float)(Random.Shared.NextDouble() - 0.5) * 0.6f;
            _birdFlightDir = new Vector2(MathF.Cos(baseAngle), MathF.Sin(baseAngle));

            //  La plupart des envols restent à hauteur "normale" (au-dessus des arbres/toits),
            // mais un oiseau peut aussi choisir de monter bien plus haut s'il le souhaite —
            // plus rare, mais bien plus spectaculaire.
            bool highAltitude = Random.Shared.NextDouble() < BIRD_HIGH_ALTITUDE_CHANCE;
            float minH = highAltitude ? BIRD_HIGH_MIN_FLIGHT_HEIGHT : BIRD_MIN_FLIGHT_HEIGHT;
            float maxH = highAltitude ? BIRD_MAX_FLIGHT_HEIGHT : BIRD_LOW_MAX_FLIGHT_HEIGHT;
            _birdTargetFlightHeight = minH + (float)Random.Shared.NextDouble() * (maxH - minH);

            // Monter/descendre plus haut prend logiquement plus de temps.
            float heightFactor = MathF.Sqrt(_birdTargetFlightHeight / BIRD_MIN_FLIGHT_HEIGHT);
            _birdTakeoffDuration = BIRD_TAKEOFF_DURATION * heightFactor;
            _birdLandingDuration = BIRD_LANDING_DURATION * heightFactor;
            _birdTakeoffTimer = _birdTakeoffDuration;

            // Un vol en haute altitude dure aussi un peu plus longtemps avant d'envisager
            // de redescendre (le temps d'en profiter).
            _birdFlightDuration = BIRD_MIN_FLIGHT_DURATION +
                (float)Random.Shared.NextDouble() * (BIRD_MAX_FLIGHT_DURATION - BIRD_MIN_FLIGHT_DURATION);
            if (highAltitude)
                _birdFlightDuration *= 1.4f;

            _birdPatternPhase = 0f;
            _birdFlockLeader = null;
            _birdFlockRecheckTimer = 0f;

            // Choix du style de vol : par défaut on tire parmi les patterns "solo". Le
            // regroupement en V ou en cercle est décidé un peu plus tard (TryJoinFlock),
            // une fois que l'oiseau est effectivement en l'air et peut voir ses voisins.
            var solo = new[] { BirdFlightPattern.Glide, BirdFlightPattern.Hop, BirdFlightPattern.Zigzag };
            _birdFlightPattern = solo[Random.Shared.Next(solo.Length)];
        }

        public void EscapeFromTreeHit(Vector2 sourceWorldPos)
        {
            if (!IsBird || _birdFlightState != BirdFlightState.Grounded) return;

            Vector2 fleeDirection = WorldPos - sourceWorldPos;
            if (fleeDirection.LengthSquared() < 0.0001f)
                fleeDirection = new Vector2(Facing, 0f);

            StartBirdTakeoff(fleeDirection);
        }

        //  Cherche un autre oiseau de la même espèce déjà en vol à proximité pour former un
        // petit groupe : soit en V (à la suite, façon oies migratrices), soit en cercle autour
        // d'un point commun (façon vautours/corbeaux tournoyant au-dessus d'un élément).
        private void TryJoinFlock()
        {
            if (_birdFlockLeader != null && _birdFlockLeader.IsAlive && _birdFlockLeader.IsBirdFlying)
                return; // déjà dans un groupe valide

            _birdFlockLeader = null;
            var candidates = Program.GetEntities();
            Entity? bestLeader = null;
            float bestDistSq = BIRD_FLOCK_SEARCH_RADIUS * BIRD_FLOCK_SEARCH_RADIUS;

            foreach (var other in candidates)
            {
                if (other == this || !other.IsAlive) continue;
                if (!string.Equals(other.Species, Species, StringComparison.OrdinalIgnoreCase)) continue;
                if (!other.IsBirdFlying) continue;

                float distSq = Vector2.DistanceSquared(WorldPos, other.WorldPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestLeader = other;
                }
            }

            if (bestLeader == null) return;

            // Un meneur qui suit déjà quelqu'un ne peut pas être suivi (on évite les chaînes
            // trop longues / les boucles) : on prend alors la tête de sa propre chaîne.
            while (bestLeader!._birdFlockLeader != null && bestLeader._birdFlockLeader.IsAlive && bestLeader._birdFlockLeader.IsBirdFlying)
                bestLeader = bestLeader._birdFlockLeader;
            if (bestLeader == this) return;

            _birdFlockLeader = bestLeader;

            bool goCircle = Random.Shared.NextDouble() < 0.5;
            if (goCircle)
            {
                _birdFlightPattern = BirdFlightPattern.Circle;
                _birdCircleCenter = bestLeader.WorldPos;
                _birdCircleRadius = 50f + (float)Random.Shared.NextDouble() * 60f;
                _birdCircleAngle = (float)(Random.Shared.NextDouble() * MathF.Tau);
                _birdCircleDir = Random.Shared.NextDouble() < 0.5 ? 1f : -1f;
            }
            else
            {
                _birdFlightPattern = BirdFlightPattern.VFormation;
                // Position dans le V : alternance gauche/droite, en retrait derrière le meneur.
                int slot = 1 + Random.Shared.Next(3);
                float side = Random.Shared.NextDouble() < 0.5 ? -1f : 1f;
                _birdFlockSlotOffset = new Vector2(side * slot * 26f, slot * 22f);
            }
        }

        private void UpdateBirdFlight(float dt, HashSet<string> destroyed)
        {
            if (!IsBird) return;

            switch (_birdFlightState)
            {
                case BirdFlightState.Grounded:
                    _birdFlightHeight = 0f;
                    return; // déplacement au sol géré normalement par MoveTowardTarget/Flee

                case BirdFlightState.TakingOff:
                    _birdTakeoffTimer -= dt;
                    float takeoffT = 1f - Math.Clamp(_birdTakeoffTimer / _birdTakeoffDuration, 0f, 1f);
                    _birdFlightHeight = _birdTargetFlightHeight * takeoffT;
                    if (_birdTakeoffTimer <= 0f)
                    {
                        _birdFlightState = BirdFlightState.Flying;
                        _birdFlightHeight = _birdTargetFlightHeight;
                    }
                    break;

                case BirdFlightState.Flying:
                    _birdFlightDuration -= dt;
                    _birdPatternPhase += dt;

                    // Petite chance de rejoindre/rester dans un groupe, revérifiée
                    // périodiquement (pas tous les frames, pour rester léger en perf).
                    _birdFlockRecheckTimer -= dt;
                    if (_birdFlockRecheckTimer <= 0f)
                    {
                        _birdFlockRecheckTimer = 1.5f;
                        if (_birdFlockLeader == null && Random.Shared.NextDouble() < 0.4)
                            TryJoinFlock();
                    }

                    ApplyBirdFlightPattern(dt);

                    if (_birdFlightDuration <= 0f)
                    {
                        _birdFlightState = BirdFlightState.Landing;
                        _birdLandingTimer = _birdLandingDuration;
                        _birdFlockLeader = null;
                    }
                    break;

                case BirdFlightState.Landing:
                    _birdLandingTimer -= dt;
                    float landT = Math.Clamp(_birdLandingTimer / _birdLandingDuration, 0f, 1f);
                    _birdFlightHeight = _birdTargetFlightHeight * landT;
                    if (_birdLandingTimer <= 0f)
                    {
                        _birdFlightState = BirdFlightState.Grounded;
                        _birdFlightHeight = 0f;
                    }
                    break;
            }
        }

        //  Applique le style de vol choisi. Un oiseau qui plane (Glide/Hop/Zigzag) ne fait
        // JAMAIS de surplace : il dérive en permanence le long de _birdFlightDir, un cap qui
        // tourne lentement et aléatoirement au fil du temps pour un vol naturel, qu'il soit
        // par ailleurs en train de fuir, d'errer ou simplement de regarder le joueur. Cette
        // dérive s'ajoute au déplacement horizontal éventuel de l'IA (Flee/MoveTowardTarget) :
        // même quand rien d'autre ne le ferait avancer (Idle, LookAtPlayer...), il continue de
        // planer, puis finit par atterrir au bout de sa durée de vol (voir UpdateBirdFlight).
        private void ApplyBirdFlightPattern(float dt)
        {
            // Le cap dérive légèrement au fil du temps, jamais parfaitement rectiligne.
            float turn = (float)(Random.Shared.NextDouble() - 0.5) * BIRD_HEADING_TURN_RATE * dt;
            float dirAngle = MathF.Atan2(_birdFlightDir.Y, _birdFlightDir.X) + turn;
            _birdFlightDir = new Vector2(MathF.Cos(dirAngle), MathF.Sin(dirAngle));

            switch (_birdFlightPattern)
            {
                case BirdFlightPattern.Glide:
                    // Vol plané : avance régulièrement, légère ondulation lente de la hauteur.
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Sin(_birdPatternPhase * 1.2f) * 6f;
                    WorldPos += _birdFlightDir * BIRD_CRUISE_SPEED * dt;
                    Facing = _birdFlightDir.X >= 0 ? 1f : -1f;
                    break;

                case BirdFlightPattern.Hop:
                    // "Sautille" dans les airs : petits bonds rythmés (façon moineau) tout en
                    // avançant, plus marqués que le vol plané.
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Abs(MathF.Sin(_birdPatternPhase * 5f)) * 18f;
                    WorldPos += _birdFlightDir * BIRD_CRUISE_SPEED * 0.8f * dt;
                    Facing = _birdFlightDir.X >= 0 ? 1f : -1f;
                    break;

                case BirdFlightPattern.Zigzag:
                {
                    // Trajectoire en zigzag : avance le long du cap tout en oscillant de part et
                    // d'autre (perpendiculairement au cap), plus une hauteur qui varie vite pour
                    // accentuer l'effet erratique.
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Sin(_birdPatternPhase * 4f) * 12f;
                    WorldPos += _birdFlightDir * BIRD_CRUISE_SPEED * 1.1f * dt;
                    Vector2 perp = new Vector2(-_birdFlightDir.Y, _birdFlightDir.X);
                    WorldPos += perp * MathF.Sin(_birdPatternPhase * 6f) * 40f * dt;
                    Facing = _birdFlightDir.X >= 0 ? 1f : -1f;
                    break;
                }

                case BirdFlightPattern.Circle:
                {
                    // Tourne autour d'un point fixe (ex : un meneur, un arbre...) à rayon constant.
                    if (_birdFlockLeader != null && _birdFlockLeader.IsAlive)
                        _birdCircleCenter = _birdFlockLeader.WorldPos;

                    _birdCircleAngle += _birdCircleDir * dt * 1.1f; // vitesse angulaire
                    WorldPos = _birdCircleCenter + new Vector2(
                        MathF.Cos(_birdCircleAngle) * _birdCircleRadius,
                        MathF.Sin(_birdCircleAngle) * _birdCircleRadius * 0.5f); // ellipse, vue de dessus
                    Facing = MathF.Cos(_birdCircleAngle + MathF.PI / 2f) >= 0 ? 1f : -1f;
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Sin(_birdPatternPhase * 1.5f) * 5f;
                    break;
                }

                case BirdFlightPattern.VFormation:
                {
                    // Suit le meneur avec un léger retard, en gardant une position en retrait
                    // (façon vol en V des oies/canards migrateurs).
                    if (_birdFlockLeader != null && _birdFlockLeader.IsAlive && _birdFlockLeader.IsBirdFlying)
                    {
                        Vector2 desired = _birdFlockLeader.WorldPos + _birdFlockSlotOffset * _birdFlockLeader.Facing;
                        WorldPos = Vector2.Lerp(WorldPos, desired, Math.Clamp(dt * 3f, 0f, 1f));
                        Facing = _birdFlockLeader.Facing;
                        _birdFlightHeight = _birdFlockLeader._birdFlightHeight;
                    }
                    else
                    {
                        // Le meneur a disparu/atterri : on repasse en vol plané solo.
                        _birdFlightPattern = BirdFlightPattern.Glide;
                    }
                    break;
                }
            }
        }

        private void UpdateGulperBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsGulper) return;
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);

            switch (_guState)
            {
                case GulperBossState.Grounded:
                {
                    // Vient de s'écraser au sol : immobile et VULNÉRABLE. C'est la seule fenêtre pour le frapper.
                    Speed = 0f;
                    AnimState = "idle";
                    _guTimer -= dt;
                    if (_guTimer <= 0f)
                        _guState = GulperBossState.Rising;
                    break;
                }

                case GulperBossState.Rising:
                {
                    // Remonte doucement depuis le sol : redevient invulnérable dès qu'il quitte le sol
                    // (voir GulperIsGrounded / IsInvulnerable).
                    Speed = 0f;
                    AnimState = "idle";
                    _guHeight += GU_RISE_SPEED * dt;
                    if (_guHeight >= GU_HOVER_HEIGHT)
                    {
                        _guHeight = GU_HOVER_HEIGHT;
                        _guState = GulperBossState.Approach;
                    }
                    break;
                }

                case GulperBossState.Approach:
                {
                    // En vol : se rapproche pour se positionner à la verticale de sa cible.
                    AnimState = "run";
                    Vector2 toPlayer = playerPos - _guGroundPos;
                    float distXY = toPlayer.Length();

                    if (distXY > GU_ABOVE_RADIUS)
                    {
                        Vector2 dir = distXY > 0.01f ? Vector2.Normalize(toPlayer) : new Vector2(Facing, 0f);
                        float moveSpeed = Math.Max(RunSpeed > 0f ? RunSpeed : Speed, GU_APPROACH_SPEED);
                        Vector2 move = dir * moveSpeed * dt;

                        Vector2 newPosX = _guGroundPos + new Vector2(move.X, 0);
                        if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                            _guGroundPos.X += move.X;
                        Vector2 newPosY = _guGroundPos + new Vector2(0, move.Y);
                        if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                            _guGroundPos.Y += move.Y;

                        Facing = dir.X >= 0f ? 1f : -1f;
                    }
                    else
                    {
                        // Il est désormais à la verticale de sa cible : il commence à charger son attaque.
                        _guState = GulperBossState.Telegraph;
                        _guTimer = GU_TELEGRAPH_DURATION;
                        _guHasDealtDamage = false;
                        AnimState = "attack";
                    }
                    break;
                }

                case GulperBossState.Telegraph:
                {
                    // Immobile en l'air au-dessus de la cible, il charge son attaque quelques instants :
                    // le joueur voit venir le crash et peut s'écarter avant qu'il ne se laisse tomber.
                    Speed = 0f;
                    AnimState = "attack";
                    _guTimer -= dt;

                    Vector2 toPlayer = playerPos - _guGroundPos;
                    if (toPlayer.Length() > 0.01f)
                        Facing = toPlayer.X >= 0f ? 1f : -1f;

                    if (_guTimer <= 0f)
                    {
                        // Le point de crash se fige ICI : il chute à la verticale, sans plus poursuivre la cible.
                        _guState = GulperBossState.Slam;
                        _guHasDealtDamage = false;
                        AnimState = "attack";
                    }
                    break;
                }

                case GulperBossState.Slam:
                {
                    // Chute rapide et s'éclate contre le sol.
                    AnimState = "attack";
                    _guHeight -= GU_SLAM_SPEED * dt;

                    if (_guHeight <= 0f)
                    {
                        _guHeight = 0f;

                        if (!_guHasDealtDamage)
                        {
                            float distToPlayer = Vector2.Distance(_guGroundPos, playerPos);
                            if (distToPlayer < GU_GROUND_DAMAGE_RADIUS)
                            {
                                Program.DamagePlayerTarget(Math.Max(8, Attack), AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species));
                            }
                            Program.ApplyScreenShake(5f, 0.16f);
                            SpawnKhamsinSmoke(_guGroundPos, 1.2f); // réutilise le FX de poussière existant
                            _guHasDealtDamage = true;
                        }

                        // Étourdi au sol : la récupération est plus courte qu'avant (boss globalement
                        // plus rapide), et encore plus courte à bas HP (boss plus agressif en fin de combat).
                        _guState = GulperBossState.Grounded;
                        _guTimer = MathHelper.Lerp(1.2f, 0.7f, 1f - hpPercent);
                        AnimState = "idle";
                    }
                    break;
                }
            }

            // Synchronise la position visuelle/logique : le vol est simulé en soustrayant _guHeight
            // à la position au sol (même principe que l'arc du WormBoss), donc aucun changement n'est
            // nécessaire côté rendu ou hitbox : ils suivent WorldPos comme pour toute autre entité.
            WorldPos = new Vector2(_guGroundPos.X, _guGroundPos.Y - _guHeight);
        }

        // Choisit un point de sortie à distance raisonnable du joueur, et fige déjà le point d'entrée
        // (généralement proche du joueur aussi, pour que le ver "saute sur sa cible").
        // Comme demandé : une fois la tête sortie (_wmEntryPointLocked = true), ce point ne bougera plus.
        private void StartWormTelegraph(Vector2 playerPos, HashSet<string> destroyed)
        {
            _wmExitPoint = FindWormGroundPoint(playerPos, WM_MIN_EXIT_DISTANCE, WM_MAX_EXIT_DISTANCE, destroyed)
                           ?? playerPos + new Vector2(WM_MIN_EXIT_DISTANCE, 0f);

            // Le point d'entrée vise la position (prédite) du joueur, avec une légère marge
            // pour laisser une vraie fenêtre de fuite pendant le Telegraph + Emerging.
            Vector2 towardExit = _wmExitPoint - playerPos;
            Vector2 biasAwayFromExit = towardExit.Length() > 0.01f ? -Vector2.Normalize(towardExit) : Vector2.Zero;
            Vector2 desiredEntry = playerPos + biasAwayFromExit * 40f;

            _wmEntryPoint = FindWormGroundPoint(desiredEntry, 0f, 90f, destroyed) ?? desiredEntry;
            _wmEntryPointLocked = false; // se fige seulement au passage en Emerging

            _wmState = WormBossState.Telegraph;
            _wmTimer = WM_TELEGRAPH_DURATION;
        }

        // Cherche une position "au sol" (tuile marchable, pas d'obstacle) dans un anneau de distance
        // [minDist, maxDist] autour d'un centre. Réutilise les mêmes checks que TeleportKhamsinAroundPlayer.
        private Vector2? FindWormGroundPoint(Vector2 center, float minDist, float maxDist, HashSet<string> destroyed)
        {
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = minDist + (float)(Random.Shared.NextDouble() * Math.Max(1f, maxDist - minDist));
                Vector2 candidate = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(candidate.X / Program.TileSize);
                int tileY = (int)(candidate.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;
                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species)) continue;
                return candidate;
            }
            return null;
        }

        // ==================== ORDRES DES ANIMAUX APPRIVOISÉS ====================
        private TamedAnimalMode? _lastAppliedTamedMode;
        private float _petTargetScanTimer;
        private const float PET_TARGET_RANGE = 450f;        // rayon de détection autour du pet
        private const float PET_OWNER_LEASH = 700f;         // cible valide seulement si proche du maître
        private const float PET_FOLLOW_RESUME_DISTANCE = 100f; // repart au-delà (rayon d'arrivée = 65 px)

        //  Sélectionne (avec verrouillage) l'ennemi le plus proche d'un animal du joueur en mode
        // Attack : monstre hostile, ni apprivoisé, ni villageois, proche du pet ET du maître.
        private Entity? FindPlayerPetTarget(Vector2 ownerPos, float dt)
        {
            static bool Valid(Entity t) => t.IsAlive && !t.IsPlayer && !t.IsVillager && !t.IsVillageGuard
                && t.Behavior == "hostile" && (!t.IsTamed || t.IsHostilePet);

            if (_hostileTargetEntity != null)
            {
                var locked = _hostileTargetEntity;
                if (Valid(locked)
                    && Vector2.DistanceSquared(WorldPos, locked.WorldPos) <= (PET_TARGET_RANGE * 1.5f) * (PET_TARGET_RANGE * 1.5f)
                    && Vector2.DistanceSquared(ownerPos, locked.WorldPos) <= PET_OWNER_LEASH * PET_OWNER_LEASH)
                    return locked;
                _hostileTargetEntity = null;
            }

            _petTargetScanTimer -= dt;
            if (_petTargetScanTimer > 0f) return null;
            _petTargetScanTimer = 0.4f; // pas de scan complet de la liste à chaque frame

            Entity? best = null;
            float bestSq = PET_TARGET_RANGE * PET_TARGET_RANGE;
            foreach (var c in Program.GetEntities())
            {
                if (ReferenceEquals(c, this) || !Valid(c)) continue;
                if (Vector2.DistanceSquared(ownerPos, c.WorldPos) > PET_OWNER_LEASH * PET_OWNER_LEASH) continue;
                float d = Vector2.DistanceSquared(WorldPos, c.WorldPos);
                if (d >= bestSq) continue;
                bestSq = d;
                best = c;
            }
            _hostileTargetEntity = best;
            return best;
        }

        //  Point UNIQUE d'application des ordres du propriétaire (Follow/Stay/Roam/Attack). Les
        // ordres reprennent la main de façon INCONDITIONNELLE (ForceGoal) au lieu de passer par
        // TryCommitGoal, que n'importe quelle priorité résiduelle (Survie, Combat...) faisait échouer.
        //  Appelée par le menu radial (TamedAnimalInteraction) : applique un ordre PROPREMENT.
        // Avant, le menu posait TamedBehavior à la main et, pour Attack, laissait Behavior =
        // "hostile" à vie (même après un ordre Suivre/Attendre), ce qui gardait l'animal dans la
        // logique des monstres (riposte, priorité Combat, cible des gardes...).
        public void ApplyTamedOrder(TamedAnimalMode mode)
        {
            TamedBehavior = mode;
            Behavior = "tamed";
            FollowOrder = -1; // force la réattribution propre de la file de suivi
            _hostileTargetEntity = null;
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            _hasFearTarget = false;
            _fearTargetEntity = null;
            _fearTargetIsPlayer = false;
            _fearTargetConnectionId = -1;
            IsAlerted = false;
            ShowAlertIcon = false;
            _attackPhase = AttackPhase.None;
            ReleaseCurrentGoalLock();
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            AiTimer = 0f;
            _currentPath.Clear();
            _pathRecalcTimer = 0f;
            Speed = BaseSpeed;
            AnimState = "idle";
        }

        private void UpdateTamedOrders(Vector2 ownerPos)
        {
            // Anciennes sauvegardes : un animal resté "hostile" après un ordre Attaque.
            if (Behavior != "tamed") Behavior = "tamed";

            bool modeChanged = _lastAppliedTamedMode != TamedBehavior;
            _lastAppliedTamedMode = TamedBehavior;

            bool attackMode = TamedBehavior == TamedAnimalMode.Attack;
            bool hasCombatTarget = _combatTargetEntity != null;
            bool mustLeaveCombat = AiState == NpcAiState.Chase
                && (!attackMode || (!hasCombatTarget && !IsHostilePet));
            bool leftoverPanic = !attackMode
                && (AiState == NpcAiState.Flee || AiState == NpcAiState.LookAtPlayer);

            if (mustLeaveCombat || leftoverPanic)
            {
                _attackPhase = AttackPhase.None;
                _hostileTargetEntity = null;
                IsAlerted = false;
                ShowAlertIcon = false;
                ReleaseCurrentGoalLock();
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                _currentPath.Clear();
                _pathRecalcTimer = 0f;
                Speed = BaseSpeed;
                AnimState = "idle";
            }
            else if (modeChanged)
            {
                _pathRecalcTimer = 0f;
                _currentPath.Clear();
            }

            // Un apprivoisé (hors mode Attack) ne "s'alerte" plus : sinon AlertFreeze le gèle sur place.
            if (!attackMode) { IsAlerted = false; ShowAlertIcon = false; }

            switch (TamedBehavior)
            {
                case TamedAnimalMode.Stay:
                    ForceGoal(NpcPriority.OwnerCommand);
                    if (AiState != NpcAiState.Idle)
                    {
                        AiState = NpcAiState.Idle;
                        AiTarget = WorldPos;
                        _currentPath.Clear();
                    }
                    Speed = 0f;
                    AnimState = "idle";
                    AiTimer = Math.Max(AiTimer, 1f); // empêche Decide() de le faire déambuler
                    break;

                case TamedAnimalMode.Follow:
                    FollowOwnerOrder(ownerPos);
                    break;

                case TamedAnimalMode.Roam:
                    if (modeChanged || AiState == NpcAiState.Follow)
                    {
                        // Sort de Follow/Stay : Decide() choisira une déambulation libre.
                        ReleaseCurrentGoalLock();
                        AiState = NpcAiState.Idle;
                        AiTarget = WorldPos;
                        AiTimer = 0f;
                    }
                    break;

                case TamedAnimalMode.Attack:
                    // En attente d'un ennemi (pas de cible), un animal du joueur reste près de lui.
                    if (!hasCombatTarget && !IsHostilePet)
                        FollowOwnerOrder(ownerPos);
                    break;
            }
        }

        //  Suit le propriétaire avec une marge (hystérésis) : une fois arrivé, le pet reste Idle
        // (et non plus Follow/Idle en alternance à chaque frame, ce qui relançait un A* par frame)
        // et ne repart que lorsque le maître s'éloigne.
        private void FollowOwnerOrder(Vector2 ownerPos)
        {
            ForceGoal(NpcPriority.OwnerCommand);
            if (AiState == NpcAiState.Follow) return;

            //  On compare à AiTarget (position de formation calculée par
            // Program.UpdateTamedAnimalTargets : le joueur pour le 1er, le pet précédent pour les
            // suivants) et non au joueur, sinon les derniers de la file, naturellement loin du joueur,
            // repasseraient en Follow à chaque frame.
            bool ownerFar = Vector2.DistanceSquared(WorldPos, AiTarget)
                > PET_FOLLOW_RESUME_DISTANCE * PET_FOLLOW_RESUME_DISTANCE;
            if (AiState != NpcAiState.Idle || ownerFar)
            {
                AiState = NpcAiState.Follow;
                _pathRecalcTimer = 0f;
                if (Speed <= 0f) Speed = BaseSpeed;
            }
            else
            {
                AiTimer = Math.Max(AiTimer, 0.5f); // reste près du maître, pas de balade via Decide()
            }
        }

        private void UpdateFollowSpeed()
        {
            if (IsTamed && (TamedBehavior == TamedAnimalMode.Follow || TamedBehavior == TamedAnimalMode.Attack)
                && AiState == NpcAiState.Follow)
            {
                float distToTarget = Vector2.Distance(WorldPos, AiTarget);
                Speed = distToTarget > 250 ? RunSpeed : BaseSpeed;
            }
        }

        // ==================== COMPAGNON D'ARMURE (ex : petit crabe) ====================
        private const float PET_ATTACK_RANGE = 34f;          // distance de mêlée pour infliger des dégâts
        private const float PET_AGGRO_GIVE_UP_RANGE = 500f;  // cible trop loin du joueur -> abandon
        private const float PET_ATTACK_COOLDOWN = 0.9f;
        private const int PET_ATTACK_DAMAGE = 3;
        private const float PET_FOLLOW_STOP_DISTANCE = 55f;  // ne vient pas coller aux pieds du joueur

        /// <summary>
        /// IA du compagnon (voir IsPetCompanion) : suit le joueur (en réutilisant la position de
        /// formation calculée par Program.UpdateTamedAnimalTargets, via AiTarget) tant qu'aucune
        /// cible n'est assignée, et se rue sur PetCombatTarget dès que
        /// Program.NotifyPlayerAttackedByEntity en assigne une (le joueur vient d'être attaqué).
        /// </summary>
        private void UpdatePetCompanionAI(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            UpdateWaterStatus();

            if (_petAttackTimer > 0f) _petAttackTimer -= dt;

            // Abandonne la cible si elle est morte ou si elle s'est trop éloignée du joueur.
            if (PetCombatTarget != null)
            {
                if (!PetCombatTarget.IsAlive || Vector2.DistanceSquared(PetCombatTarget.WorldPos, playerPos) > (PET_AGGRO_GIVE_UP_RANGE) * (PET_AGGRO_GIVE_UP_RANGE))
                    PetCombatTarget = null;
            }

            Vector2 desiredTarget;
            float desiredSpeed;

            if (PetCombatTarget != null)
            {
                var target = PetCombatTarget;
                float distToTarget = Vector2.Distance(WorldPos, target.WorldPos);
                if (distToTarget > 0.01f)
                    Facing = (target.WorldPos.X - WorldPos.X) >= 0 ? 1f : -1f;

                if (distToTarget > PET_ATTACK_RANGE)
                {
                    desiredTarget = target.WorldPos;
                    desiredSpeed = RunSpeed > 0f ? RunSpeed : BaseSpeed * 1.6f;
                    AnimState = "walk";
                }
                else
                {
                    desiredTarget = WorldPos; // reste en place pour frapper
                    desiredSpeed = 0f;
                    AnimState = "attack";

                    if (_petAttackTimer <= 0f)
                    {
                        _petAttackTimer = PET_ATTACK_COOLDOWN;
                        target.CurrentHP -= PET_ATTACK_DAMAGE;
                        target.OnHit(WorldPos, this, attackerIsPlayer: false);

                        Vector2 kb = target.WorldPos - WorldPos;
                        kb = kb.LengthSquared() > 0.01f ? Vector2.Normalize(kb) : new Vector2(Facing, 0);
                        target.ApplyKnockback(kb, 90f);

                        int ts = Program.TileSize;
                        int th = World.GetHeightAt((int)(target.WorldPos.X / ts), (int)(target.WorldPos.Y / ts));
                        Vector2 targetVisualPos = new Vector2(target.WorldPos.X, target.WorldPos.Y - th * ts / 4f);
                        Program.AddFloatingDamage(targetVisualPos, PET_ATTACK_DAMAGE, false, isEnemy: true);

                        if (!target.IsAlive)
                        {
                            PetCombatTarget = null;
                            var speciesInfo = SpeciesData.GetSpeciesInfo(target.Species);
                            if (speciesInfo == null || !speciesInfo.CanBeCaught)
                            {
                                foreach (var (id, qty, _) in GameData.GetAnimalDrops(target.Species))
                                    Program.GiveItemToPlayer(id, qty, target.WorldPos);
                            }
                        }
                    }
                }
            }
            else
            {
                // Pas de cible : suit le joueur (formation gérée par Program.UpdateTamedAnimalTargets),
                // mais s'arrête à une distance confortable au lieu de venir se coller aux pieds du joueur.
                desiredTarget = AiTarget;
                float distToFollow = Vector2.Distance(WorldPos, desiredTarget);
                if (distToFollow > PET_FOLLOW_STOP_DISTANCE)
                {
                    desiredSpeed = distToFollow > 250f ? RunSpeed : BaseSpeed;
                    AnimState = "walk";
                    Facing = (desiredTarget.X - WorldPos.X) >= 0 ? 1f : -1f;
                }
                else
                {
                    desiredSpeed = 0f;
                    AnimState = "idle";
                }
            }

            Speed = desiredSpeed;

            if (desiredSpeed > 0f)
            {
                Vector2 dir = desiredTarget - WorldPos;
                float dist = dir.Length();
                if (dist > 4f)
                {
                    dir /= dist;
                    Vector2 move = dir * desiredSpeed * dt;
                    if (move.Length() > dist) move = dir * dist;

                    Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                    if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                        WorldPos.X += move.X;
                    Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                    if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                        WorldPos.Y += move.Y;
                }
            }

            // Animation (retombe sur "idle" si l'espèce n'a pas l'animation demandée).
            float animSpeed = AnimState == "walk" ? 8f : (AnimState == "attack" ? 10f : 2f);
            AnimProgress += dt * animSpeed;
            if (AnimProgress >= 1f)
            {
                AnimProgress = 0f;
                int frameCount = 8;
                if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
                {
                    var firstPart = skeleton.FirstOrDefault();
                    string animKey = (firstPart != null && firstPart.Animations.ContainsKey(AnimState)) ? AnimState : "idle";
                    if (firstPart != null && firstPart.Animations.TryGetValue(animKey, out var frames) && frames.Count > 0)
                        frameCount = frames.Count;
                }
                if (frameCount == 0) frameCount = 1;
                CurrentFrame = (CurrentFrame + 1) % frameCount;
            }
        }

        //  Couleur de peau/fourrure aléatoire par entité, tirée dans la plage [ColorMin, ColorMax]
        // définie dans species.json (colorMin/colorMax), si l'espèce est marquée "tintable".
        // Si un préset est fourni et définit sa propre plage de couleur, celle-ci est prioritaire.
        // Sinon, retombe sur la couleur fixe historique (GetColorForSpecies).
        public static void GenerateRandomAppearance(
            string species,
            Vector2 position,
            out Color tint,
            Dictionary<string, int> randomFeatureVariant,
            Dictionary<string, Color> randomFeatureColor)
        {
            string renderSpecies = string.Equals(species, "Genie", StringComparison.OrdinalIgnoreCase) ? "human" : species;
            SpeciesData.ColorPreset? colorPreset = null;

            if (string.Equals(renderSpecies, "human", StringComparison.OrdinalIgnoreCase))
            {
                tint = Program.SkinColorPalette[Random.Shared.Next(Program.SkinColorPalette.Length)];
            }
            else
            {
                Biome? spawnBiome = null;
                if (Program.TileSize > 0)
                {
                    spawnBiome = World.GetBiomeAt(position.X / Program.TileSize, position.Y / Program.TileSize);
                }
                colorPreset = SpeciesData.PickColorPreset(renderSpecies, spawnBiome);
                tint = GetRandomSkinColor(renderSpecies, colorPreset);
            }

            AssignRandomFeatures(colorPreset, randomFeatureVariant, randomFeatureColor, renderSpecies);
        }

        private static Color GetRandomSkinColor(string species, SpeciesData.ColorPreset? preset = null)
        {
            if (preset != null && preset.ColorMin.HasValue && preset.ColorMax.HasValue)
                return RandomColorInRange(preset.ColorMin.Value, preset.ColorMax.Value);

            var info = SpeciesData.GetSpeciesInfo(species);
            if (info != null && info.Tintable)
                return RandomColorInRange(info.ColorMin, info.ColorMax);

            return GetColorForSpecies(species);
        }

        private static Color RandomColorInRange(Color min, Color max)
        {
            int rMin = Math.Min(min.R, max.R);
            int rMax = Math.Max(min.R, max.R);
            int gMin = Math.Min(min.G, max.G);
            int gMax = Math.Max(min.G, max.G);
            int bMin = Math.Min(min.B, max.B);
            int bMax = Math.Max(min.B, max.B);

            return new Color(
                Random.Shared.Next(rMin, rMax + 1),
                Random.Shared.Next(gMin, gMax + 1),
                Random.Shared.Next(bMin, bMax + 1),
                255);
        }

        private static Color GetColorForSpecies(string species)
        {
            return species switch
            {
                "pig" => new Color(255, 200, 200, 255),
                "sheep" => new Color(240, 240, 240, 255),
                "cow" => new Color(210, 170, 100, 255),
                "chicken" => new Color(255, 240, 180, 255),
                "duck" => new Color(220, 200, 120, 255),
                "wolf" => new Color(180, 180, 200, 255),
                "bear" => new Color(160, 120, 80, 255),
                "goat" => new Color(200, 180, 150, 255),
                "horse" => new Color(180, 140, 100, 255),
                "bat" => new Color(80, 80, 100, 255),
                "skeleton" => new Color(200, 200, 210, 255),
                _ => Color.White
            };
        }

        //  CARACTÈRES ALÉATOIRES : tire au sort, pour chaque feature déclarée dans species.json
        // pour cette espèce, si l'entité la possède et quelle variante elle affiche.
        // Si un préset est fourni, il peut forcer la présence/absence, la variante et la couleur
        // de chaque feature ; sinon le tirage 100% aléatoire habituel s'applique.
        private static void AssignRandomFeatures(
            SpeciesData.ColorPreset? preset,
            Dictionary<string, int> randomFeatureVariant,
            Dictionary<string, Color> randomFeatureColor,
            string? renderSpeciesOverride = null)
        {
            randomFeatureVariant.Clear();
            randomFeatureColor.Clear();

            string speciesKey = (renderSpeciesOverride ?? "").ToLowerInvariant();
            if (!SpeciesData.Species.TryGetValue(speciesKey, out var info) || info.RandomFeatures == null)
                return;

            foreach (var feature in info.RandomFeatures)
            {
                if (string.IsNullOrWhiteSpace(feature.name)) continue;

                int variantCount = SpeciesData.GetFeatureVariantCount(speciesKey, feature.name);

                SpeciesData.PresetFeature? presetFeature = null;
                preset?.Features.TryGetValue(feature.name, out presetFeature);

                bool hasFeature;
                if (presetFeature?.Enabled == true)
                    hasFeature = variantCount > 0;
                else if (presetFeature?.Enabled == false)
                    hasFeature = false;
                else
                    hasFeature = variantCount > 0 && Random.Shared.NextDouble() < feature.chance;

                int chosenVariant = -1;
                if (hasFeature)
                {
                    chosenVariant = (presetFeature != null && presetFeature.ForceVariant && presetFeature.Variant < variantCount)
                        ? presetFeature.Variant
                        : Random.Shared.Next(0, variantCount);
                }

                randomFeatureVariant[feature.name] = chosenVariant;

                if (hasFeature && feature.colorable)
                {
                    Color cMin = presetFeature?.ColorMin ?? new Color(40, 40, 40, 255);
                    Color cMax = presetFeature?.ColorMax ?? new Color(240, 240, 240, 255);
                    randomFeatureColor[feature.name] = RandomColorInRange(cMin, cMax);
                }
            }

            //  Couleur d'yeux customisée : priorité au préset, sinon plage de l'espèce, sinon pas de teinte (blanc).
            // Stockée sous une clé réservée dans RandomFeatureColor pour être transmise à EntityRenderer
            // sans changer la signature de DrawEntity.
            Color? eyeMin = preset?.EyeColorMin ?? info.EyeColorMin;
            Color? eyeMax = preset?.EyeColorMax ?? info.EyeColorMax;
            if (eyeMin.HasValue && eyeMax.HasValue)
                randomFeatureColor["__eyeColor"] = RandomColorInRange(eyeMin.Value, eyeMax.Value);
        }

        private void SpawnFootstepParticle()
        {
            int tileX = (int)(WorldPos.X / Program.TileSize);
            int tileY = (int)(WorldPos.Y / Program.TileSize);
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            Color particleColor = World.GetParticleColorForGround(groundId);

            int height = World.GetHeightAt(tileX, tileY);
            float yOffset = -height * Program.TileSize / 4;
            Vector2 footPos = new Vector2(WorldPos.X, WorldPos.Y + yOffset + Program.FeetOffsetY);

            Random rand = new Random();
            int count = rand.Next(1, 3);
            for (int i = 0; i < count; i++)
            {
                Vector2 vel = new Vector2(
                    (float)(rand.NextDouble() - 0.5) * 40f,
                    -(float)(rand.NextDouble() * 30f + 10f)
                );
                float size = rand.Next(2, 5);
                float lifetime = rand.Next(300, 600) / 1000f;
                Program.GetParticleList().Add(new Particle(footPos, vel, particleColor, size, lifetime));
            }
        }

        private void SpawnZzzParticle()
        {
            if (!_isSleeping) return;

            float headOffsetX = (float)(Random.Shared.NextDouble() - 0.5) * 15f;
            float headOffsetY = -35f - (float)(Random.Shared.NextDouble() * 10f);
            Vector2 zzzPos = new Vector2(WorldPos.X + headOffsetX, WorldPos.Y + headOffsetY);

            Vector2 velocity = new Vector2(
                (float)(Random.Shared.NextDouble() - 0.5) * 15f,
                -(float)(Random.Shared.NextDouble() * 20f + 15f)
            );

            float size = Random.Shared.Next(12, 20);
            float lifetime = Random.Shared.Next(800, 1400) / 1000f;

            Color zzzColor = new Color((byte)220, (byte)220, (byte)180, (byte)Random.Shared.Next(180, 255));

            Program.GetParticleList().Add(new ZzzParticle(zzzPos, velocity, zzzColor, size, lifetime));

            _zzzCurrentInterval = (float)(Random.Shared.NextDouble() * 1.0f + 0.5f);
        }

        private void UpdateSleepState(float darkness, float dt)
        {
            if (!IsInPlayerDialogue)
                _sleepWakeCooldown = Math.Max(0f, _sleepWakeCooldown - dt);

            bool isTentResident = IsCaveTentResident;
            if (HomeBuildingId == TentHomeBuildingId && _tentRestCooldown > 0f)
                _tentRestCooldown = Math.Max(0f, _tentRestCooldown - dt);
            bool isNight = isTentResident ? _tentRestCooldown <= 0f : darkness > 0.6f;

            //  Un oiseau ne peut pas dormir en plein vol : il doit forcément se poser au sol
            // d'abord. S'il fait nuit alors qu'il est encore en l'air, on force un atterrissage
            // anticipé (au lieu d'attendre la fin normale du vol) pour qu'il puisse ensuite
            // s'endormir comme n'importe quelle autre créature au sol.
            if (IsBird && IsBirdFlying)
            {
                if (isNight && _birdFlightState == BirdFlightState.Flying)
                {
                    _birdFlightState = BirdFlightState.Landing;
                    _birdLandingTimer = _birdLandingDuration;
                }
                return;
            }

            // Un item peut être donné à un PNJ déjà endormi. Dans ce cas il doit se réveiller
            // immédiatement pour relancer la recherche du coffre, au lieu de rester bloqué par
            // _hasDepositedTonight jusqu'au matin.
            if (_isSleeping && HomeBuildingId >= 0 && World.HasAnyDepositableItem(this))
            {
                _isSleeping = false;
                _groundResting = false;
                _hasDepositedTonight = false;
                _sleepTimer = 0f;
                _zzzTimer = 0f;
                ReleaseCurrentGoalLock();
                AnimState = "idle";
                Speed = BaseSpeed;
                //  Libérer la chaise réservée pour la nuit (voir GetSleepVisualPosition), sinon
                // elle resterait marquée occupée indéfiniment alors que le PNJ n'y est plus assis.
                if (_restingInChair)
                {
                    World.ReleaseBench(_reservedBench);
                    _reservedBench = null;
                    _restingInChair = false;
                }
            }

            //  Un PNJ ne doit s'endormir que s'il est effectivement arrivé chez lui
            // (ou s'il n'a pas de maison, auquel cas il s'endort sur place faute de mieux).
            // Pour une maison, l'arrivée est l'ensemble des tuiles intérieures : HomePosition
            // n'est qu'une case attribuée au PNJ, pas le centre obligatoire de la maison.
            bool hasHome = HomePosition != Vector2.Zero;
            //  Les tentes restent évaluées par distance, car elles n'ont pas de liste de tuiles
            //  intérieures comparable à celle d'une maison.
            const float HOME_ARRIVED_DISTANCE = 25f;
            //  Le coffre peut très bien se trouver À L'INTÉRIEUR du même rayon de 25
            // unités que HomePosition (petite maison) : sans cette exclusion, le PNJ
            // reste "considéré chez lui" pendant tout son aller-retour vers le coffre,
            // le minuteur de sommeil continue de tourner en parallèle, et peut se
            // terminer AVANT que le trajet ne soit fini. Le dépôt se faisait alors via
            // le filet de sécurité au moment de l'endormissement plutôt que via le
            // vrai passage au coffre. Tant que ce trajet est en cours, le PNJ n'est
            // pas "arrivé" pour les besoins du sommeil, quelle que soit sa position.
            bool isRunningChestErrand = AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw;
            bool isInsideHome = HomeBuildingId == TentHomeBuildingId
                ? Vector2.DistanceSquared(WorldPos, HomePosition) < HOME_ARRIVED_DISTANCE * HOME_ARRIVED_DISTANCE
                : IsInsideAssignedHouse();
            bool isHomeOrHomeless = !isRunningChestErrand
                && (!hasHome || isInsideHome);

            bool canRestInTent = HomeBuildingId != TentHomeBuildingId || _tentRestCooldown <= 0f;
            if (isNight && !IsPlayer && !IsTamed && Behavior != "hostile" && isHomeOrHomeless
                && canRestInTent && _sleepWakeCooldown <= 0f)
            {
                if (!_isSleeping)
                {
                    //  CORRECTIF : Raylib.GetFrameTime() renvoie le delta-temps RÉEL, non affecté
                    // par un éventuel ralenti/pause/multiplicateur de vitesse de jeu, alors que
                    // tout le reste de cette fonction (et de l'IA en général) utilise le
                    // paramètre `dt` reçu en argument. Un `dt` mis à l'échelle (pause, time-scale)
                    // désynchronisait alors ce minuteur de tous les autres (rentrer chez soi,
                    // marche jusqu'au coffre, etc.) : le PNJ pouvait par exemple sembler "figé"
                    // en Idle près de son lit bien plus longtemps que SLEEP_TRANSITION_DELAY ne
                    // le laisse penser (ou au contraire s'endormir plus vite que prévu), le
                    // temps affiché/observé ne correspondant plus au reste de la simulation.
                    _sleepTimer += dt;
                    if (_sleepTimer >= SLEEP_TRANSITION_DELAY)
                    {
                        //  BUGFIX : PrepareHatForNightDeposit() n'était appelée QUE comme effet de
                        // bord du trajet "aller déposer les autres objets au coffre" (juste en
                        // dessous). Un villageois qui n'avait rien d'autre à ranger ce soir-là
                        // (World.HasAnyDepositableItem == false) sautait directement à
                        // l'endormissement sans jamais passer par cette fonction : il s'endormait
                        // chapeau compris. Le retrait du chapeau doit être une étape à part entière
                        // du coucher, indépendante du fait qu'il y ait ou non d'autres objets à
                        // déposer — on l'appelle donc ici, une seule fois, dès qu'on s'apprête
                        // vraiment à basculer en sommeil (elle est de toute façon sans effet si le
                        // chapeau est déjà rangé, voir sa propre garde interne).
                        PrepareHatForNightDeposit();

                        //  Le dépôt doit rester un trajet visible, même si la routine n'a pas
                        //  obtenu la main avant l'expiration du délai de sommeil. Le dépôt direct
                        //  reste uniquement le repli si aucune case d'approche praticable n'est
                        //  disponible, afin de ne pas perdre les objets du PNJ.
                        if (!_hasDepositedTonight && HomeBuildingId >= 0 && World.HasAnyDepositableItem(this)
                            && _chestDepositRetryCooldown <= 0f)
                        {
                            var chestApproachPos = World.GetHouseChestApproachPosition(HomeBuildingId, this);
                            if (chestApproachPos.HasValue)
                            {
                                _activeRoutineTask = RoutineTaskKind.DepositItems;
                                AiState = NpcAiState.GoToChestDeposit;
                                AiTarget = chestApproachPos.Value;
                                _currentPath.Clear();
                                _pathRecalcTimer = 0f;
                                _chestArrivalLogged = false;
                                _chestBlockedLogged = false;
                                _chestDepositFullRetryCount = 0;
                                _chestDepositStuckRetryCount = 0;
                                AnimState = "walk";
                                Speed = BaseSpeed;
                                _sleepTimer = 0f;
                                _chestApproachRetryCount = 0;
                                Console.WriteLine($"[NPC-CHEST] SLEEP_FALLBACK npc={NetId} home={HomeBuildingId} pos={WorldPos} target={AiTarget} inventory={GetChestInventoryLog()}");
                                return;
                            }

                            //  FILET DE SÉCURITÉ : si aucun point d'approche praticable n'est
                            // trouvable après plusieurs tentatives (maison sans case libre,
                            // entièrement meublée, etc.), on ne doit PAS laisser le PNJ figé
                            // indéfiniment à réessayer chaque frame sans jamais dormir — c'est
                            // exactement le bug "le PNJ attend juste sur sa case de sommeil".
                            // Au bout d'un nombre de tentatives raisonnable, on renonce pour ce
                            // soir (il gardera ses objets, rien n'est perdu) et on le laisse
                            // s'endormir normalement ; _hasDepositedTonight reste false pour
                            // qu'il retente dès le lendemain soir.
                            _chestApproachRetryCount++;
                            if (_chestApproachRetryCount >= CHEST_APPROACH_MAX_RETRIES)
                            {
                                _chestApproachRetryCount = 0;
                                _isSleeping = true;
                                _sleepVisualPosition = GetSleepVisualPosition();
                                ReleaseCurrentGoalLock();
                                AnimState = _restingInChair ? "sit" : "sleep";
                                _sleepTimer = 0f;
                                return;
                            }

                            _sleepTimer = 0f;
                            return;
                        }

                        _isSleeping = true;
                        _sleepVisualPosition = GetSleepVisualPosition();
                        ReleaseCurrentGoalLock();
                        AnimState = _restingInChair ? "sit" : "sleep";
                        _sleepTimer = 0f;
                    }
                }
                else
                {
                    if (isTentResident)
                    {
                        _tentSleepTimer += dt;
                        if (_tentSleepTimer >= CAVE_TENT_SLEEP_DURATION)
                        {
                            _tentSleepTimer = 0f;
                            _tentRestCooldown = 45f + (float)(Random.Shared.NextDouble() * 120f);
                            ForceWakeFromTent();
                            RestoreHatAfterNight();
                            Decide();
                            return;
                        }
                    }

                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    Speed = 0f;
                    AnimState = _restingInChair ? "sit" : "sleep";
                }
            }
            else
            {
                if (_isSleeping)
                {
                    _isSleeping = false;
                    _tentSleepTimer = 0f;
                    _groundResting = false;
                    _sleepVisualPosition = Vector2.Zero;
                    _hasDepositedTonight = false;
                    _morningDeparturePending = true;
                    _sleepTimer = 0f;
                    ReleaseCurrentGoalLock();
                    AnimState = "idle";
                    //  Libérer la chaise réservée pour la nuit (voir GetSleepVisualPosition) : sans
                    // cela, elle resterait marquée occupée pour toujours et ne redeviendrait jamais
                    // disponible pour un autre villageois (de jour comme la nuit suivante).
                    if (_restingInChair)
                    {
                        World.ReleaseBench(_reservedBench);
                        _reservedBench = null;
                        _restingInChair = false;
                    }
                    var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
                    if (speciesInfo != null)
                        Speed = speciesInfo.Speed;
                    if (HomeBuildingId == TentHomeBuildingId)
                        _tentRestCooldown = 90f + (float)(Random.Shared.NextDouble() * 150f);
                    RestoreHatAfterNight();
                    Decide();
                }
                else
                {
                    _sleepTimer = 0f;
                }
            }
        }

        private Vector2 GetSleepVisualPosition()
        {
            _restingInChair = false;
            _groundResting = false;

            if (TentPosition != Vector2.Zero && !Program.IsSleepPositionOccupied(TentPosition, this))
            {
                Program.PrepareTentOccupation(TentPosition, this);
                return TentPosition;
            }

            //  Chercher un lit LIBRE en priorité (comportement d'origine).
            Vector2? sleepPosition = World.FindNearestAvailableSleepPosition(HomeBuildingId, WorldPos, this);

            //  Un lit existe dans la maison mais ils sont tous occupés : mieux vaut prendre le
            // lit "occupé" le plus proche (superposition visuelle mineure, déjà le comportement
            // d'origine via FindNearestSleepPosition) plutôt que de passer directement à la
            // recherche de chaise, réservée au cas où la maison n'a carrément AUCUN lit.
            bool houseHasNoBedAtAll = !sleepPosition.HasValue
                && HomeBuildingId >= 0 && HomeBuildingId != TentHomeBuildingId
                && World.FindNearestSleepPosition(HomeBuildingId, WorldPos) is null;

            if (!sleepPosition.HasValue && !houseHasNoBedAtAll)
            {
                sleepPosition = World.FindNearestSleepPosition(HomeBuildingId, WorldPos);
            }

            if (sleepPosition.HasValue)
            {
                Program.PrepareTentOccupation(sleepPosition.Value, this);
                return sleepPosition.Value;
            }

            //  Pas de lit du tout chez lui : plutôt que de "dormir" debout sur place (ce qui, sans
            // animation dédiée, ressemble à une pause figée), on cherche d'abord une chaise/banc libre
            // de la maison. S'il n'y en a pas non plus, le PNJ peut simplement s'asseoir au sol ou
            // se coucher directement sur le sol sans rester planté au milieu de la pièce.
            if (houseHasNoBedAtAll)
            {
                var seat = World.FindNearestFreeHouseSeat(HomeBuildingId, WorldPos, 800f);
                if (seat.HasValue)
                {
                    _restingInChair = true;
                    _reservedBench = seat.Value.key;
                    return seat.Value.seatPos;
                }
            }

            _groundResting = true;
            return WorldPos;
        }
    }

    //  Gère la propagation du signal d'alerte ("effet d'écho") entre entités hostiles : quand
    // une entité programme l'émission de son signal (voir Entity.TriggerAlertSignal /
    // ReceiveAlertSignal), cette classe se charge, une fois le délai écoulé, de retrouver les
    // entités hostiles proches de la même espèce (rayon Entity.ALERT_SIGNAL_RANGE_TILES) et de
    // les réveiller à leur tour via ReceiveAlertSignal, qui peut programmer un nouveau relais.
    public static class AlertSignalManager
    {
        public static void Emit(Entity source, Vector2 targetPos, int targetConnectionId)
        {
            float rangePixels = Entity.ALERT_SIGNAL_RANGE_TILES * Program.TileSize;
            float rangeSq = rangePixels * rangePixels;

            foreach (var other in Program.GetEntities())
            {
                if (other == source || !other.IsAlive) continue;
                if ((other.Behavior != "hostile" && other.Behavior != "neutral") || other.IsTamed) continue;
                if (!string.Equals(other.Species, source.Species, StringComparison.OrdinalIgnoreCase)) continue;

                float distSq = Vector2.DistanceSquared(other.WorldPos, source.WorldPos);
                if (distSq > rangeSq) continue;

                other.ReceiveAlertSignal(targetPos, targetConnectionId);
            }
        }
    }

    //  Zone de danger au sol utilisée par le Génie de la Lampe (phase 2 : tourbillons de sable,
    // phase 3 : flaques de lave). Le cycle est toujours Telegraph (visuel d'avertissement,
    // aucun dégât) -> Active (dégâts au joueur s'il reste dedans) -> expirée (retirée de la liste).
    public class GenieHazardZone
    {
        public Vector2 Pos;
        public float Radius;
        public float TelegraphTimer;   // temps restant avant que la zone devienne active
        public float ActiveTimer;      // temps restant pendant lequel la zone est active
        public bool IsSand;            // true = sable (phase 2, ralentit), false = lave (phase 3, brûle)
        public float DamageTickTimer;  // pour infliger les dégâts périodiquement plutôt qu'en continu

        public bool IsTelegraph => TelegraphTimer > 0f;
        public bool IsExpired => TelegraphTimer <= 0f && ActiveTimer <= 0f;
    }
}