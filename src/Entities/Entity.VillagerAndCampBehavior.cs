#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Decision helpers and story camp behavior
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
            ResetPathfinding();
            _pathRecalcTimer = PATH_RECALC_INTERVAL;
            _workGoalActive = false; // les routines de travail le repositionnent après SetGoal
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
            // balade : le système GoHome (dans Update) le ramènera dans l'intérieur de sa maison
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

            // Le Veilleur ne suit pas la routine de village : il vit autour de son camp.
            if (IsStoryGuide)
            {
                DecideStoryCampIdle();
                return;
            }

            if (Profession == ProfessionType.Farmer && IsInsideAssignedHouse()
                && !_farmerSupplyPreparedToday)
            {
                _farmerSupplyPreparedToday = true;
                PrepareFarmerSeedsFromInventory();
                if (GetFarmerSeedCount() < FARMER_SEED_STOCK_TARGET && HomeBuildingId >= 0)
                {
                    var chestApproachPos = World.GetHouseChestApproachPosition(HomeBuildingId, this);
                    if (chestApproachPos.HasValue)
                    {
                        _farmerSupplyWithdrawPending = true;
                        SetGoal(NpcAiState.GoToChestWithdraw, chestApproachPos.Value, 45f);
                        return;
                    }
                }
            }

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
                // Une cible de travail périmée ne doit pas "rappeler" le fermier au champ pendant sa pause.
                _farmerWorkTarget = null;
                _farmerApproachPos = null;
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

                // Trajet de travail tout juste abandonné (cible/poste inaccessible) : on fait autre
                // chose un moment plutôt que de réessayer la même cible en boucle.
                if (_workGoalBackoffTimer > 0f)
                    return false;

                if (WorkplacePosition == null || WorkZoneId < 0
                    || !World.WorkZones.TryGetValue(WorkZoneId, out var assignedField)
                    || assignedField.Kind != WorkZoneKind.Field)
                {
                    // AssignWorkplace() peut tenter de créer un champ (scan de terrain coûteux) :
                    // ne pas le relancer à chaque Decide() quand ça échoue.
                    if (_workplaceRetryCooldown > 0f)
                        return false;
                    _workplaceRetryCooldown = 30f;
                    AssignWorkplace();
                }

                if (!WorkplacePosition.HasValue)
                    return false;

                //  Enchaîne directement sur la prochaine tâche du champ (avant : retour systématique
                // au poste entre deux actions, puis nouvelle recherche).
                if (TryFarmerWorkCycle())
                {
                    AiTimer = _farmerActionCooldown;
                    return true;
                }
                if (_farmerWorkTarget.HasValue)
                    return true; // TryFarmerWorkCycle a lancé le déplacement vers la case

                //  Rien à faire dans le champ : rejoint son poste (case praticable la plus proche) puis attend.
                if (!_workApproachPos.HasValue)
                {
                    var destroyedFarm = Program.GetDestroyedObjects();
                    _workApproachPos = Pathfinder.IsWorldPositionWalkable(WorkplacePosition.Value, destroyedFarm, Species)
                        ? WorkplacePosition.Value
                        : Pathfinder.FindNearestWalkablePosition(WorkplacePosition.Value, destroyedFarm, Species, 4)
                          ?? WorkplacePosition.Value;
                }

                if (Vector2.DistanceSquared(WorldPos, _workApproachPos.Value) < 90f * 90f)
                {
                    SetGoal(NpcAiState.Idle, WorldPos, RandomRange(4f, 8f), "idle");
                    return true;
                }

                SetGoal(NpcAiState.WalkToTarget, _workApproachPos.Value, 45f);
                _workGoalActive = true;
                return true;
            }

            //  Métiers à zone de travail (bûcheron, mineur, chasseur) : allaient auparavant JAMAIS
            // à leur poste (seul le fermier était géré). Ils rejoignent maintenant leur emplacement,
            // y restent un moment, puis font une pause avant de revenir.
            if (Profession != ProfessionType.None && Profession != ProfessionType.Guard
                && World.ProfessionZoneKind.ContainsKey(Profession))
            {
                if (IsOnWorkBreak())
                    return false;

                if (_workGoalBackoffTimer > 0f)
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
                    _workGoalActive = true;
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

        private float FarmerActionRangeSq
        {
            get { float r = MathF.Max(70f, Program.TileSize * 2f); return r * r; }
        }

        private bool IsFarmTileBlacklisted(int x, int y)
            => _unreachableFarmTiles.TryGetValue((x, y), out var until) && until > _farmClock;

        private void BlacklistFarmTile(Vector2 worldPos, float seconds)
        {
            int tx = (int)MathF.Floor(worldPos.X / Program.TileSize);
            int ty = (int)MathF.Floor(worldPos.Y / Program.TileSize);
            if (_unreachableFarmTiles.Count > 32)
            {
                var expired = _unreachableFarmTiles.Where(kv => kv.Value <= _farmClock).Select(kv => kv.Key).ToList();
                foreach (var k in expired) _unreachableFarmTiles.Remove(k);
            }
            _unreachableFarmTiles[(tx, ty)] = _farmClock + seconds;
        }

        //  Une case portant une culture/un objet peut ne pas être praticable pour l'A* (le but serait
        // alors inatteignable par construction) : on vise la case praticable la plus proche.
        private bool TryResolveFarmerApproach(Vector2 tileCenter, out Vector2 approach)
        {
            approach = tileCenter;
            var destroyed = Program.GetDestroyedObjects();
            if (Pathfinder.IsWorldPositionWalkable(tileCenter, destroyed, Species))
                return true;

            var near = Pathfinder.FindNearestWalkablePosition(tileCenter, destroyed, Species, 2);
            if (!near.HasValue)
                return false;

            float maxOffset = Program.TileSize * 2f;
            if (Vector2.DistanceSquared(near.Value, tileCenter) > maxOffset * maxOffset)
                return false;

            approach = near.Value;
            return true;
        }

        private void BeginFarmerWalk()
        {
            Vector2 dest = _farmerApproachPos ?? _farmerWorkTarget ?? WorldPos;
            SetGoal(NpcAiState.WalkToTarget, dest, 45f);
            _workGoalActive = true;
        }

        //  Appelée à l'arrivée d'un fermier en service (à la place des deux blocs dupliqués de
        // MoveTowardTarget) : agit si possible, sinon enchaîne sur la tâche suivante ou se repose.
        private void HandleFarmerArrival()
        {
            if (TryFarmerWorkCycle())
            {
                AiState = NpcAiState.Idle;
                AnimState = "idle";
                AiTimer = _farmerActionCooldown;
                return;
            }

            if (AiState == NpcAiState.WalkToTarget && _farmerWorkTarget.HasValue)
                return; // un nouveau déplacement vers la prochaine case vient d'être lancé

            AiState = NpcAiState.Idle;
            AnimState = "idle";
            AiTimer = RandomRange(1f, 3f);
        }

        private bool TryFarmerWorkCycle()
        {
            if (Profession != ProfessionType.Farmer)
                return false;

            if (_farmerActionCooldown > 0f)
            {
                AiState = NpcAiState.Idle;
                AnimState = "idle";
                AiTimer = _farmerActionCooldown;
                return true;
            }

            //  Pendant une pause, le fermier ne retourne pas travailler (avant, n'importe quelle
            // arrivée de balade le remettait au travail via cette méthode).
            if (_workBreakCooldown > 0f)
            {
                _farmerWorkTarget = null;
                _farmerApproachPos = null;
                return false;
            }

            if (WorkZoneId < 0 || !World.WorkZones.TryGetValue(WorkZoneId, out var zone) || zone.Kind != WorkZoneKind.Field)
                return false;

            if (_farmerWorkTarget.HasValue)
            {
                Vector2 t = _farmerWorkTarget.Value;
                bool inRange = Vector2.DistanceSquared(WorldPos, t) < FarmerActionRangeSq
                    || (_farmerApproachPos.HasValue && Vector2.DistanceSquared(WorldPos, _farmerApproachPos.Value) < 70f * 70f);

                if (inRange)
                {
                    if (TryHarvestReadyCropInZone(zone, t)
                        || TryWaterDryingCropInZone(zone, t)
                        || TryTillFieldTile(zone, t)
                        || TryPlantSeedInFieldZone(zone, t))
                    {
                        _farmerWorkTarget = null;
                        _farmerApproachPos = null;
                        _farmerActionCooldown = FarmerActionCooldownSeconds;
                        AiTimer = 0f;
                        return true;
                    }

                    //  Arrivé mais plus rien à faire sur cette case (état changé entre-temps) : sans
                    // liste noire, TryFindFarmerActionTarget la renvoyait à l'identique, en boucle.
                    BlacklistFarmTile(t, 120f);
                    _farmerWorkTarget = null;
                    _farmerApproachPos = null;
                }
                else
                {
                    // En route (ou trajet interrompu par un autre état) : relancer le déplacement.
                    BeginFarmerWalk();
                    return false;
                }
            }

            if (_farmerNoWorkCooldown > 0f)
                return false;

            if (!Inventory.Slots.Any(slot =>
                !slot.IsEmpty && slot.Item != null &&
                GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var data) &&
                data.IsSeed && data.CropId != 0)
                && TryCraftSeedsForFarmer())
            {
                _farmerActionCooldown = FarmerActionCooldownSeconds;
                AiTimer = 0f;
                return true;
            }

            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!TryFindFarmerActionTarget(zone, out var targetPos))
                    break;

                if (TryResolveFarmerApproach(targetPos, out var approach))
                {
                    _farmerWorkTarget = targetPos;
                    _farmerApproachPos = approach;
                    BeginFarmerWalk();
                    return false;
                }

                BlacklistFarmTile(targetPos, 180f); // aucune case praticable à proximité : on l'ignore
            }

#if DEBUG_NPC_AI
            Console.WriteLine($"[Farming] PNJ {NetId} sur sa zone {WorkZoneId} : rien à faire (pas de récolte prête, rien à arroser, pas de case libre ou plus de graines).");
#endif
            _farmerNoWorkCooldown = RandomRange(8f, 15f);
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
            var searchTiles = GetFieldTileSearchOrder(zone, minTileX, maxTileX, minTileY, maxTileY);
            searchTiles.RemoveAll(t => IsFarmTileBlacklisted(t.x, t.y));

            foreach (var (x, y) in searchTiles)
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

            foreach (var (x, y) in searchTiles)
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

            var seedSlot = Inventory.Slots.FirstOrDefault(slot =>
                !slot.IsEmpty && slot.Item != null &&
                GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var data) &&
                data.IsSeed && data.CropId != 0);

            if (seedSlot == null || seedSlot.Item == null)
                return TryFindUntilledFieldTile(zone, minTileX, maxTileX, minTileY, maxTileY, out targetPos);

            int itemId = GameData.GetItemId(seedSlot.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var seedData) || seedData.CropId == 0)
                return false;

            foreach (var (x, y) in searchTiles)
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

            return TryFindUntilledFieldTile(zone, minTileX, maxTileX, minTileY, maxTileY, out targetPos);
        }

        private static List<(int x, int y)> GetFieldTileSearchOrder(
            VillageWorkZone zone,
            int minTileX,
            int maxTileX,
            int minTileY,
            int maxTileY)
        {
            int centerX = (int)MathF.Floor(zone.AnchorPos.X / Program.TileSize);
            int centerY = (int)MathF.Floor(zone.AnchorPos.Y / Program.TileSize);
            return (from x in Enumerable.Range(minTileX, maxTileX - minTileX + 1)
                    from y in Enumerable.Range(minTileY, maxTileY - minTileY + 1)
                    orderby (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY)
                    select (x, y)).ToList();
        }

        private bool TryFindUntilledFieldTile(VillageWorkZone zone, int minTileX, int maxTileX, int minTileY, int maxTileY, out Vector2 targetPos)
        {
            targetPos = Vector2.Zero;
            if (zone.TilledTileCount >= zone.MaxTilledTiles)
                return false;

            foreach (var (x, y) in GetFieldTileSearchOrder(zone, minTileX, maxTileX, minTileY, maxTileY))
            {
                if (IsFarmTileBlacklisted(x, y))
                    continue;
                int groundId = World.GetGroundTileIdAt(x, y);
                var groundTile = WorldTileRegistry.GetTile(groundId);
                if (groundTile == null || !groundTile.Diggable)
                    continue;
                if (groundId == 27 || groundId == 29 || World.GetObjectIdAt(x, y) != 0 || World.GetCropDataAt(x, y) != null)
                    continue;

                targetPos = new Vector2((x + 0.5f) * Program.TileSize, (y + 0.5f) * Program.TileSize);
                return true;
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
                    if (!GameData.ItemDatabase.TryGetValue(harvestItemId, out var harvestData))
                    {
                        Console.WriteLine($"[Farming] Impossible de récolter {tileData.Name} : item de récolte introuvable ({tileData.HarvestItemId}).");
                        return false;
                    }

                    if (!World.ContainerHasSpaceForItem(Inventory, harvestData.Name))
                        return false;

                    var harvestItem = new Item(harvestData.Name, qty, harvestData.Color, harvestData.Icon);
                    int stored = World.StackItemIntoContainer(Inventory, harvestItem, qty);
                    if (stored != qty)
                    {
                        Console.WriteLine($"[Farming] Récolte de {tileData.Name} annulée : inventaire du fermier {NetId} insuffisant ({stored}/{qty}).");
                        return false;
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

        private bool TryCraftSeedsForFarmer()
        {
            foreach (var recipe in GameData.Recipes)
            {
                if (recipe.IsRitual || recipe.IsHidden || !string.IsNullOrWhiteSpace(recipe.RequiresStation) || recipe.ResultCount <= 0)
                    continue;
                if (!GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var seedData)
                    || !seedData.IsSeed
                    || seedData.CropId == 0
                    || !World.ContainerHasSpaceForItem(Inventory, seedData.Name))
                    continue;

                var requiredIngredients = recipe.Ingredients
                    .GroupBy(ingredient => ingredient.id)
                    .Select(group => (itemId: group.Key, quantity: group.Sum(ingredient => ingredient.qty)))
                    .ToList();

                if (requiredIngredients.Count == 0 || requiredIngredients.Any(ingredient =>
                    ingredient.quantity <= 0
                    || !GameData.ItemDatabase.TryGetValue(ingredient.itemId, out var ingredientData)
                    || Inventory.Slots.Sum(slot =>
                        slot.IsEmpty || slot.Item == null || slot.Count <= 0
                            || GameData.GetItemId(slot.Item.Name) != ingredient.itemId
                            ? 0
                            : slot.Count) < ingredient.quantity))
                    continue;

                foreach (var ingredient in requiredIngredients)
                {
                    int remaining = ingredient.quantity;
                    foreach (var slot in Inventory.Slots)
                    {
                        if (remaining <= 0)
                            break;
                        if (slot.IsEmpty || slot.Item == null || GameData.GetItemId(slot.Item.Name) != ingredient.itemId)
                            continue;

                        int consumed = Math.Min(slot.Count, remaining);
                        slot.Count -= consumed;
                        remaining -= consumed;
                        if (slot.Count <= 0)
                            slot.Clear();
                    }
                }

                var craftedSeeds = new Item(seedData.Name, recipe.ResultCount, seedData.Color, seedData.Icon);
                int stored = World.StackItemIntoContainer(Inventory, craftedSeeds, recipe.ResultCount);
                if (stored != recipe.ResultCount)
                    throw new InvalidOperationException($"Fermier {NetId} : impossible de ranger les graines produites par la recette {recipe.Id}.");

                return true;
            }

            return false;
        }

        private int GetFarmerSeedCount()
        {
            return Inventory.Slots.Sum(slot =>
                !slot.IsEmpty && slot.Item != null
                && GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var data)
                && data.IsSeed && data.CropId != 0
                    ? slot.Count
                    : 0);
        }

        private void PrepareFarmerSeedsFromInventory()
        {
            while (GetFarmerSeedCount() < FARMER_SEED_STOCK_TARGET && TryCraftSeedsForFarmer())
            {
            }
        }

        private void PrepareFarmerSeedsFromHouseChest()
        {
            if (HomeBuildingId < 0)
                return;

            int seedCount = GetFarmerSeedCount();
            foreach (var seedData in GameData.ItemDatabase.Values
                .Where(data => data.IsSeed && data.CropId != 0)
                .OrderBy(data => data.ID))
            {
                if (seedCount >= FARMER_SEED_STOCK_TARGET)
                    break;

                int itemId = seedData.ID;
                int available = World.GetHouseChestItemCount(HomeBuildingId, itemId);
                int quantity = Math.Min(FARMER_SEED_STOCK_TARGET - seedCount, available);
                if (quantity <= 0)
                    continue;

                if (World.TryWithdrawHouseChestItems(HomeBuildingId, this,
                    new Dictionary<int, int> { [itemId] = quantity }))
                    seedCount += quantity;
            }

            PrepareFarmerSeedsFromInventory();
            while (GetFarmerSeedCount() < FARMER_SEED_STOCK_TARGET
                && TryWithdrawIngredientsForFarmerSeedRecipe()
                && TryCraftSeedsForFarmer())
            {
            }
        }

        private bool TryWithdrawIngredientsForFarmerSeedRecipe()
        {
            if (HomeBuildingId < 0)
                return false;

            foreach (var recipe in GameData.Recipes)
            {
                if (recipe.IsRitual || recipe.IsHidden || !string.IsNullOrWhiteSpace(recipe.RequiresStation)
                    || recipe.ResultCount <= 0
                    || !GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var seedData)
                    || !seedData.IsSeed || seedData.CropId == 0
                    || !World.ContainerHasSpaceForItem(Inventory, seedData.Name))
                    continue;

                var missingIngredients = new Dictionary<int, int>();
                bool validRecipe = true;
                foreach (var group in recipe.Ingredients.GroupBy(ingredient => ingredient.id))
                {
                    int required = group.Sum(ingredient => ingredient.qty);
                    int carried = Inventory.Slots.Sum(slot =>
                        !slot.IsEmpty && slot.Item != null && slot.Count > 0
                        && GameData.GetItemId(slot.Item.Name) == group.Key
                            ? slot.Count
                            : 0);
                    int missing = required - carried;
                    if (required <= 0 || !GameData.ItemDatabase.ContainsKey(group.Key))
                    {
                        validRecipe = false;
                        break;
                    }
                    if (missing > 0)
                        missingIngredients[group.Key] = missing;
                }

                if (validRecipe && missingIngredients.Count > 0
                    && World.TryWithdrawHouseChestItems(HomeBuildingId, this, missingIngredients))
                    return true;
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

            foreach (var (x, y) in GetFieldTileSearchOrder(zone, minTileX, maxTileX, minTileY, maxTileY))
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

        // ==================== CAMPEMENT DU VEILLEUR ====================
        // Le Veilleur (voir World.TryBuildStoryCamp) se promène autour du feu, va fouiller ses
        // caisses et passe la nuit dans sa tente (HomeBuildingId = TentHomeBuildingId, déjà géré
        // par le système de sommeil des tentes). Rien de tout cela n'est sauvegardé : les objets
        // du camp sont simplement redécouverts autour de HomePosition.
        private Vector2 _campFirePos = Vector2.Zero;
        private readonly List<(int x, int y)> _campChests = new();
        private float _campScanTimer = 0f;
        private (int x, int y)? _campChestTile = null;
        private Vector2 _campChestApproach = Vector2.Zero;
        private float _campChestTimeout = 0f;
        private float _campChestWorkTimer = 0f;

        private void RefreshStoryCampObjects()
        {
            if (_campScanTimer > 0f) return;
            _campScanTimer = 30f;

            int ts = Program.TileSize;
            Vector2 anchor = HomePosition != Vector2.Zero ? HomePosition : WorldPos;
            int ax = (int)MathF.Floor(anchor.X / ts);
            int ay = (int)MathF.Floor(anchor.Y / ts);

            _campChests.Clear();
            _campFirePos = Vector2.Zero;
            int bestFireDistSq = int.MaxValue;

            for (int dx = -8; dx <= 8; dx++)
            {
                for (int dy = -8; dy <= 8; dy++)
                {
                    int objectId = World.GetObjectIdAt(ax + dx, ay + dy);
                    if (objectId == 32 || objectId == 18) // feu de camp
                    {
                        int d = dx * dx + dy * dy;
                        if (d < bestFireDistSq)
                        {
                            bestFireDistSq = d;
                            _campFirePos = new Vector2((ax + dx + 0.5f) * ts, (ay + dy + 0.5f) * ts);
                        }
                    }
                    else if (objectId == 107 || objectId == 108 || objectId == 115) // tonneau, caisse, boîte
                    {
                        _campChests.Add((ax + dx, ay + dy));
                    }
                }
            }
        }

        // Point praticable au hasard autour du feu (ou de la tente si le feu a disparu).
        private Vector2 PickStoryCampTarget(float minTiles, float maxTiles)
        {
            int ts = Program.TileSize;
            Vector2 center = _campFirePos != Vector2.Zero ? _campFirePos : (HomePosition != Vector2.Zero ? HomePosition : WorldPos);
            HashSet<string> destroyed = Program.GetDestroyedObjects();

            for (int attempt = 0; attempt < 14; attempt++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
                float dist = (minTiles + (float)Random.Shared.NextDouble() * (maxTiles - minTiles)) * ts;
                Vector2 candidate = center + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                int tx = (int)MathF.Floor(candidate.X / ts);
                int ty = (int)MathF.Floor(candidate.Y / ts);
                var ground = WorldTileRegistry.GetTile(World.GetGroundTileIdAt(tx, ty));
                if (ground == null || !ground.Walkable || World.GetObjectIdAt(tx, ty) != 0)
                    continue;
                if (!Pathfinder.IsWorldPositionWalkable(candidate, destroyed, Species))
                    continue;
                if (HasCaveRouteTo(candidate))
                    return candidate;
            }
            return WorldPos;
        }

        private bool TryStartStoryCampChestErrand()
        {
            if (_campChests.Count == 0) return false;

            int ts = Program.TileSize;
            HashSet<string> destroyed = Program.GetDestroyedObjects();
            var offsets = new[] { (0, 1), (-1, 0), (1, 0), (0, -1) }; // on se place de préférence devant la caisse

            foreach (var chest in _campChests.OrderBy(_ => Random.Shared.Next()))
            {
                foreach (var (ox, oy) in offsets)
                {
                    int tx = chest.x + ox;
                    int ty = chest.y + oy;
                    if (World.GetObjectIdAt(tx, ty) != 0) continue;
                    var ground = WorldTileRegistry.GetTile(World.GetGroundTileIdAt(tx, ty));
                    if (ground == null || !ground.Walkable) continue;

                    Vector2 approach = new((tx + 0.5f) * ts, (ty + 0.5f) * ts);
                    if (!Pathfinder.IsWorldPositionWalkable(approach, destroyed, Species)) continue;
                    if (!HasCaveRouteTo(approach)) continue;

                    _campChestTile = chest;
                    _campChestApproach = approach;
                    _campChestTimeout = 18f;
                    _campChestWorkTimer = 0f;
                    SetGoal(NpcAiState.WalkToTarget, approach, 16f);
                    return true;
                }
            }
            return false;
        }

        private void CancelStoryCampChestErrand()
        {
            _campChestTile = null;
            _campChestWorkTimer = 0f;
        }

        // Appelée à chaque frame : gère l'arrivée devant la caisse et la courte "fouille".
        private void UpdateStoryCampChest(float dt)
        {
            _campScanTimer -= dt;
            if (!_campChestTile.HasValue) return;

            if (_isSleeping || IsTalking || IsInPlayerDialogue || Program.GetDarknessAlpha() > 0.6f)
            {
                CancelStoryCampChestErrand();
                return;
            }

            var chest = _campChestTile.Value;

            if (_campChestWorkTimer > 0f)
            {
                _campChestWorkTimer -= dt;
                Speed = 0f;
                AnimState = "idle";
                if (_campChestWorkTimer <= 0f)
                {
                    CancelStoryCampChestErrand();
                    ReleaseCurrentGoalLock();
                    Speed = BaseSpeed;
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    AiTimer = RandomRange(1.5f, 3.5f);
                }
                return;
            }

            _campChestTimeout -= dt;
            if (Vector2.DistanceSquared(WorldPos, _campChestApproach) < 28f * 28f)
            {
                _campChestWorkTimer = RandomRange(2.5f, 4f);
                SetGoal(NpcAiState.Idle, WorldPos, _campChestWorkTimer + 0.3f, "idle");
                Speed = 0f;
                float dirX = (chest.x + 0.5f) * Program.TileSize - WorldPos.X;
                if (MathF.Abs(dirX) > 4f)
                    Facing = MathF.Sign(dirX); // se tourne vers la caisse
                return;
            }

            if (_campChestTimeout <= 0f)
                CancelStoryCampChestErrand();
        }

        //  Décision de fond du Veilleur pendant la journée.
        private void DecideStoryCampIdle()
        {
            RefreshStoryCampObjects();

            var choice = WeightedChoice(
                (0, _campChests.Count > 0 ? 0.30f : 0f),  // fouiller une caisse
                (1, 0.35f),                                // se promener dans le camp
                (2, 0.15f),                                // se poser près du feu
                (3, 0.20f));                               // rester là à ne rien faire

            switch (choice)
            {
                case 0:
                    if (TryStartStoryCampChestErrand()) return;
                    goto case 1;
                case 1:
                    SetGoal(NpcAiState.WalkToTarget, PickStoryCampTarget(1.5f, 5.5f), RandomRange(4f, 9f));
                    return;
                case 2:
                    SetGoal(NpcAiState.WalkToTarget, PickStoryCampTarget(1.3f, 2.2f), RandomRange(3f, 6f));
                    return;
                default:
                    SetGoal(NpcAiState.Idle, WorldPos, RandomRange(3f, 9f), "idle");
                    return;
            }
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
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            _currentPath.Clear();
            Speed = 0f;

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
                partner.AiState = NpcAiState.Idle;
                partner.AiTarget = partner.WorldPos;
                partner._currentPath.Clear();
                partner.Speed = 0f;
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
                Vector2 visualPos = World.GetEntityGroundPosition(WorldPos);
                Vector2 visualTarget = World.GetEntityGroundPosition(AiTarget);
                Raylib.DrawLineEx(visualPos, visualTarget, 2, Color.Red);
                Raylib.DrawCircle((int)visualTarget.X, (int)visualTarget.Y, 5, Color.Red);
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
                Raylib.DrawLineEx(
                    World.GetEntityGroundPosition(WorldPos),
                    World.GetEntityGroundPosition(_currentPath[0]),
                    3, Color.Green);
                for (int i = 0; i < _currentPath.Count - 1; i++)
                {
                    Raylib.DrawLineEx(
                        World.GetEntityGroundPosition(_currentPath[i]),
                        World.GetEntityGroundPosition(_currentPath[i + 1]),
                        3, Color.Green);
                }

                Vector2 debugEnd = useLogicalTargetForDebug && AiTarget != Vector2.Zero
                    ? AiTarget
                    : _currentPath[_currentPath.Count - 1];
                Vector2 visualDebugEnd = World.GetEntityGroundPosition(debugEnd);
                Raylib.DrawCircle((int)visualDebugEnd.X, (int)visualDebugEnd.Y, 5, Color.Yellow);
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
            Vector2 headPos = new Vector2(WorldPos.X, WorldPos.Y - ts * 0.9f);

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

    }
}
