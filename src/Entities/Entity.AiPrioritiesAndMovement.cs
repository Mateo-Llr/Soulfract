#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Leash, AI priorities, movement momentum and stuck detection
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
            //  Un trajet de TRAVAIL (case de champ ou poste) abandonné : on mémorise la case comme
            // inaccessible et on suspend la routine de métier un moment. Sans ça, Decide() re-choisissait
            // la même cible (la recherche est déterministe : toujours la case la plus proche) toutes les
            // ~1,5 s, ce qui donnait un fermier figé avec sa ligne rouge de debug et aucun chemin.
            if (_workGoalActive && AiState == NpcAiState.WalkToTarget)
            {
                _workGoalBackoffTimer = 25f;
                if (_farmerWorkTarget.HasValue)
                    BlacklistFarmTile(_farmerWorkTarget.Value, 120f);
                _farmerWorkTarget = null;
                _farmerApproachPos = null;
                _workApproachPos = null;
            }
            _workGoalActive = false;

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
        public bool WasKilledByPlayerWeapon;

        //  SIGNAL D'ALERTE ("effet d'écho") : quand une entité hostile repère sa cible pour la
        // première fois (par vue directe, pas par relais), elle affiche brièvement l'icône
        // exclamation.png au-dessus de sa tête et programme l'émission d'un signal qui, après un
        // petit délai aléatoire, va avertir les entités hostiles alentour (rayon
        // ALERT_SIGNAL_RANGE_TILES) même si elles n'ont pas encore vu la cible elles-mêmes.
        // Les entités averties ne retransmettent pas le signal afin d'éviter les échos en chaîne.
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
        private bool _farmerSupplyPreparedToday;
        private bool _farmerSupplyWithdrawPending;
        private const int FARMER_SEED_STOCK_TARGET = 10;
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
            ResetMoveMomentum();
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

        // ==================== MOMENTUM DE DÉPLACEMENT ====================
        //  Avant, l'entité se déplaçait à vitesse constante dans la direction exacte de son
        // prochain nœud (dir * Speed * dt) : démarrage instantané, arrêt instantané et virages
        // parfaitement anguleux. On conserve désormais une vitesse persistante (_moveVelocity)
        // qui rejoint progressivement la vitesse voulue : l'entité accélère au départ, prend un
        // léger élan dans les virages (elle dévie de la ligne droite au lieu de pivoter net) et
        // glisse un court instant en s'arrêtant.
        //   MOVE_ACCEL_TIME : temps pour atteindre la pleine vitesse (et pour se réorienter).
        //   MOVE_BRAKE_TIME : constante de temps du freinage quand plus rien ne la pilote
        //                     (décroissance exponentielle : ~12 px de glissement en course).
        //   AccelerationFactor : réglage par entité (> 1 = plus nerveuse, < 1 = plus lourde).
        private Vector2 _moveVelocity = Vector2.Zero;
        private bool _steeredThisFrame = false;
        private const float MOVE_ACCEL_TIME = 0.30f;
        private const float MOVE_BRAKE_TIME = 0.18f;
        private const float MOVE_MOMENTUM_EPSILON = 1.5f; // px/s : en dessous, on considère l'entité arrêtée
        public float AccelerationFactor = 1f;

        //  Fait tendre la vitesse courante vers la vitesse voulue sans jamais dépasser
        // l'accélération maximale (changement de vitesse VECTORIEL : un demi-tour exige donc
        // plus de temps qu'une simple accélération, d'où l'élan dans les virages).
        private Vector2 SteerVelocity(Vector2 desired, float dt, float responseTime)
        {
            float reference = MathF.Max(desired.Length(), _moveVelocity.Length());
            if (reference <= 0.0001f)
                return Vector2.Zero;

            float maxDelta = reference / MathF.Max(0.01f, responseTime) * MathF.Max(0.05f, AccelerationFactor) * dt;
            Vector2 delta = desired - _moveVelocity;
            float deltaLength = delta.Length();
            if (deltaLength <= maxDelta || deltaLength < 0.0001f)
                return desired;

            return _moveVelocity + delta / deltaLength * maxDelta;
        }

        private void ResetMoveMomentum()
        {
            _moveVelocity = Vector2.Zero;
        }

        //  Appelée une fois par Update, après l'exécution de l'état d'IA : si aucun déplacement
        // piloté n'a eu lieu cette frame (arrêt, Idle, arrivée...), l'entité décélère et
        // parcourt la petite distance restante au lieu de s'arrêter net.
        private void ApplyResidualMomentum(float dt, HashSet<string> destroyed)
        {
            bool steered = _steeredThisFrame;
            _steeredThisFrame = false;
            if (steered || _moveVelocity == Vector2.Zero)
                return;

            //  Pas de glissement quand l'entité est censée être plantée au sol, assise, ou en pleine
            // attaque (frappes « pieds plantés ») : on coupe l'élan net.
            if (_isSleeping || _attackPhase != AttackPhase.None || IsInPlayerDialogue
                || AiState == NpcAiState.Sitting || AiState == NpcAiState.HealingAtInfirmary
                || IsKnockedBack || IsMounted)
            {
                ResetMoveMomentum();
                return;
            }

            _moveVelocity = SteerVelocity(Vector2.Zero, dt, MOVE_BRAKE_TIME);
            if (_moveVelocity.LengthSquared() < MOVE_MOMENTUM_EPSILON * MOVE_MOMENTUM_EPSILON)
            {
                ResetMoveMomentum();
                return;
            }

            Vector2 move = _moveVelocity * dt;
            if (!World.IsCollidingEntity(WorldPos + new Vector2(move.X, 0f), destroyed, Species))
                WorldPos.X += move.X;
            else
                _moveVelocity.X = 0f;

            if (!World.IsCollidingEntity(WorldPos + new Vector2(0f, move.Y), destroyed, Species))
                WorldPos.Y += move.Y;
            else
                _moveVelocity.Y = 0f;
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
            _pathFailStreak = 0;
            _stuckProgressTimer = 0f;
            _stuckProgressCheckPos = WorldPos;
        }

        // Autres timers
        private float _lookAtPlayerTimer = 0f;
        private float _continuousFearTimer = 0f;
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
        private double _nextHurtSoundTime;
        private const double HURT_SOUND_COOLDOWN = 0.75;
        private List<Sound> _attackSounds = new();
        private bool _attackSoundLoaded = false;

    }
}
