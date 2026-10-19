#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Quests, villagers, social data, inventory and related properties
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

        //  Point de MARCHE réel utilisé pour atteindre _farmerWorkTarget (case de culture). Une case
        // portant une culture / un objet peut être non praticable pour l'A* : on vise alors la
        // case praticable la plus proche, et on exécute l'action depuis là.
        private Vector2? _farmerApproachPos = null;
        //  Cases de champ jugées inaccessibles (A* en échec / action impossible), ignorées un moment.
        // La valeur est l'horloge (_farmClock) à laquelle la case redevient éligible.
        private readonly Dictionary<(int x, int y), float> _unreachableFarmTiles = new();
        private const float FarmerActionCooldownSeconds = 0.65f;
        private float _farmClock = 0f;
        private float _farmerActionCooldown = 0f;
        //  Évite de rescanner tout le champ (tri LINQ) à chaque Decide() quand il n'y a rien à faire.
        private float _farmerNoWorkCooldown = 0f;
        //  Après l'abandon d'un trajet vers le poste/une case de travail, on laisse le PNJ vaquer
        // à autre chose quelques secondes au lieu de re-choisir immédiatement la même cible.
        private float _workGoalBackoffTimer = 0f;
        private bool _workGoalActive = false;
        private bool _assigningWorkplace = false;
        private bool IsFarmerOnShift => Profession == ProfessionType.Farmer && _workBreakCooldown <= 0f && WorkZoneId >= 0;
        public bool IsVillager => Species == "human" && !IsPlayer;

        /// <summary>Nom du PNJ du campement de départ (voir World.TryBuildStoryCamp).</summary>
        public const string StoryGuideName = "Orwen le Veilleur";

        /// <summary>
        /// Le Veilleur du campement de départ : il vit autour de son feu (promenade, caisses, nuit
        /// dans sa tente) au lieu de la routine de village. Identifié par son nom (aucun champ de
        /// sauvegarde supplémentaire n'est nécessaire) ; il redevient un PNJ ordinaire s'il est
        /// apprivoisé / recruté.
        /// </summary>
        public bool IsStoryGuide => Species == "human" && !IsPlayer && !IsTamed
            && (string.Equals(FirstName, StoryGuideName, StringComparison.Ordinal)
                || string.Equals(CustomName, StoryGuideName, StringComparison.Ordinal));
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

            string rumblePath = Path.Combine("assets", "sounds", "worm_rumbling.mp3");
            if (File.Exists(rumblePath))
                _wormRumbleSound = Raylib.LoadSound(rumblePath);

            string roarPath = Path.Combine("assets", "sounds", "roar.mp3");
            if (File.Exists(roarPath))
                _wormRoarSound = Raylib.LoadSound(roarPath);
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

        private void PlayWormRoarSound()
        {
            if (_wormRoarSound == null || !Raylib.IsSoundReady(_wormRoarSound.Value))
                return;

            float distance = Vector2.Distance(WorldPos, Program.GetPlayerPosition());
            float volume = Math.Clamp(1f - distance / 1200f, 0.05f, 1f);
            Raylib.SetSoundVolume(_wormRoarSound.Value, volume);
            Raylib.PlaySound(_wormRoarSound.Value);
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

    }
}
