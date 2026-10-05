// QuestSystem.cs - Système de quêtes des PNJ
// -----------------------------------------------------------------
// Principe :
//  - Certains villageois ("donneurs") ont de temps en temps une quête
//    en tête : amener un objet à un autre villageois ("cible").
//  - Le donneur affiche assets/gui/quest_start.png au-dessus de sa tête.
//  - Une fois la quête acceptée, le joueur doit livrer l'objet à la cible.
//  - Une fois la livraison faite, le joueur retourne voir le donneur pour
//    valider la quête et recevoir la récompense.
// -----------------------------------------------------------------
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;
using System.IO;

namespace Soulfract
{
    public enum QuestState
    {
        Offered,        // Le PNJ propose la quête, le joueur n'a pas encore répondu
        Accepted,       // Le joueur a accepté, doit livrer l'objet (ou l'animal) à la cible
        Delivered,      // L'objet/animal a été remis à la cible, il faut revenir au donneur
        Completed       // Terminée (état transitoire, la quête est ensuite retirée)
    }

    public enum QuestType
    {
        DeliverItem,    // Quête classique : apporter un objet d'un PNJ à un autre
        TameAndBring    // Nouvelle quête : apprivoiser une espèce précise et l'offrir à un PNJ
    }

    public class Quest
    {
        public Guid Id = Guid.NewGuid();
        public QuestType Type = QuestType.DeliverItem;

        // NetId des PNJ concernés
        public Guid GiverId;
        public Guid TargetId;

        // Ce qu'il faut livrer (QuestType.DeliverItem)
        public int ItemId;
        public string ItemName = "";
        public int ItemQty = 1;

        // Ce qu'il faut apprivoiser et amener (QuestType.TameAndBring)
        // Espèce attendue (ex: "wolf", "horse"...), voir World.GetSpeciesForBiome.
        public string TargetSpecies = "";
        // Une fois qu'un animal a été proposé au PNJ cible et accepté, on garde son NetId
        // pour retrouver précisément quel individu a été offert (utile en multijoueur / debug).
        public Guid OfferedAnimalId = Guid.Empty;

        // Récompense (le jeu n'ayant pas de monnaie, la récompense est toujours un objet)
        public int RewardItemId = 0;
        public string RewardItemName = "";
        public int RewardItemQty = 0;

        // Positions de secours utilisées par le traqueur si le chunk concerné n'est pas chargé.
        public float LastKnownTargetPosX;
        public float LastKnownTargetPosY;
        public bool HasLastKnownTargetPosition = false;

        public float LastKnownGiverPosX;
        public float LastKnownGiverPosY;
        public bool HasLastKnownGiverPosition = false;

        public QuestState State = QuestState.Offered;

        public string GetOfferLine(string giverName, string targetName)
        {
            if (Type == QuestType.TameAndBring)
            {
                // Pas de tiers pour cette quête : l'animal apprivoisé m'est ramené à moi-même
                // (le donneur), donc pas de targetName à mentionner ici.
                return string.Format(Localization.Get("quest.offer_line_tame", "Dis, tu ne saurais pas apprivoiser {0} et me l'amener ?"), Localization.GetLocalizedSpeciesName(TargetSpecies));
            }
            return string.Format(Localization.Get("quest.offer_line", "Dis-moi, pourrais-tu apporter {0}x {1} à {2} ? Je te revaudrai ça."), ItemQty, GetLocalizedItemName(), targetName);
        }

        public string GetReminderLine(string targetName)
        {
            if (Type == QuestType.TameAndBring)
            {
                return string.Format(Localization.Get("quest.reminder_line_tame", "Apprivoiser {0} et me l'amener."), Localization.GetLocalizedSpeciesName(TargetSpecies));
            }
            return string.Format(Localization.Get("quest.reminder_line", "Livrer {0}x {1} à {2}."), ItemQty, GetLocalizedItemName(), targetName);
        }

        // Ligne affichée dans la bulle de confirmation quand le joueur approche du PNJ cible
        // avec un animal apprivoisé correspondant à proximité.
        public string GetGiftOfferLine(string targetName)
        {
            return string.Format(Localization.Get("quest.gift_offer_line", "Veux-tu offrir cet animal à {0} ?"), targetName);
        }

        public string GetRewardLine()
        {
            if (RewardItemQty > 0)
            {
                string rewardName = GetLocalizedRewardItemName();
                if (!string.IsNullOrWhiteSpace(rewardName))
                    return string.Format(Localization.Get("quest.reward_item", "{0}x {1}"), RewardItemQty, rewardName);
            }
            return Localization.Get("quest.reward_small_thanks", "un petit merci");
        }

        public string GetLocalizedItemName()
        {
            if (!string.IsNullOrWhiteSpace(ItemName))
                return Localization.GetLocalizedItemName(ItemName);
            if (ItemId > 0)
                return Localization.GetLocalizedItemName(ItemId);
            return ItemName;
        }

        public string GetLocalizedRewardItemName()
        {
            if (!string.IsNullOrWhiteSpace(RewardItemName))
                return Localization.GetLocalizedItemName(RewardItemName);
            if (RewardItemId > 0)
                return Localization.GetLocalizedItemName(RewardItemId);
            return RewardItemName;
        }
    }

    public static class QuestManager
    {
        private static readonly Dictionary<Guid, Quest> _persistedQuests = new();

        private static (int itemId, string itemName, int qty) GetCoinRewardInfo(int fallbackItemId, string fallbackName)
        {
            int coinItemId = Program.GetItemId("coin");
            if (coinItemId > 0)
                return (coinItemId, "coin", Random.Shared.Next(3, 6));
            return (fallbackItemId, fallbackName, Random.Shared.Next(1, 3));
        }

        // ==================== CONFIGURATION ====================
        // Intervalle entre deux vérifications ("un PNJ a-t-il une idée de quête ?")
        private const float SPAWN_CHECK_INTERVAL = 15f;
        // Probabilité qu'un villageois éligible ait une idée de quête à chaque vérification
        private const float SPAWN_CHANCE = 0.12f;
        // Une fois qu'un PNJ a une idée de quête, probabilité que ce soit une quête
        // d'apprivoisement plutôt qu'une livraison d'objet classique.
        private const float TAME_QUEST_CHANCE = 0.30f;

        //  Optionnel : si vous remplissez ce tableau manuellement (avec de VRAIS IDs de votre
        // GameData.ItemDatabase), il sera utilisé en priorité. Sinon, un pool est généré
        // automatiquement à partir de tous les objets existants dans GameData.ItemDatabase.
        public static (int id, string name)[] EligibleItems = new (int, string)[0];

        private static (int id, string name)[]? _autoItemPool = null;

        private static (int id, string name)[] GetItemPool(Entity npc)
        {
            var professionIds = npc.HasProfession ? npc.GetProfessionItemIds() : null;
            if (professionIds != null && professionIds.Count > 0)
            {
                var professionPool = professionIds
                    .Where(id => id > 0 && GameData.ItemDatabase.TryGetValue(id, out var itemData) && !string.IsNullOrEmpty(itemData.Name))
                    .Select(id => (id, name: GameData.ItemDatabase[id].Name))
                    .Distinct()
                    .ToArray();

                if (professionPool.Length > 0)
                    return professionPool;

                return new (int id, string name)[0];
            }

            if (EligibleItems.Length > 0)
            {
                if (npc.HasProfession)
                {
                    var professionItemIds = npc.GetProfessionItemIds();
                    return EligibleItems
                        .Where(item => professionItemIds.Contains(item.id))
                        .ToArray();
                }

                return EligibleItems;
            }

            if (_autoItemPool == null)
            {
                _autoItemPool = GameData.ItemDatabase
                    .Where(kv => !string.IsNullOrEmpty(kv.Value.Name))
                    .Select(kv => (id: kv.Key, name: kv.Value.Name))
                    .ToArray();
            }
            return _autoItemPool;
        }

        private static readonly string[] FirstNamesPool = new[]
        {
            "Lucas", "Emma", "Louis", "Camille", "Gabriel", "Jade", "Raphaël",
            "Louna", "Hugo", "Manon", "Arthur", "Chloé", "Jules", "Lina",
            "Adam", "Inès", "Ethan", "Lila", "Noah", "Rose", "Liam",
            "Mia", "Sacha", "Iris", "Naël", "Alice", "Timéo", "Romy",
            "Milo", "Anna", "Gabin", "Lena", "Ibrahim", "Zélie", "Ayden",
            "Julia", "Aaron", "Agathe", "Basile", "Ninon", "Wilson",
            "Robert", "Pablo", "Clara", "Mathis", "Samuel", "Eliott", "Louise", "Théo"
        };

        private static float _spawnTimer = 0f;

        //  OPTIM PERF (villages) : voir Update() plus bas pour le détail du problème
        // corrigé par ce timer de réparation dédié.
        private const float REPAIR_CHECK_INTERVAL = 0.5f;
        private static float _repairTimer = 0f;

        // ==================== PRÉNOMS ====================
        public static void AssignFirstNameIfNeeded(Entity npc)
        {
            if (npc.Species != "human" || npc.IsPlayer) return;
            if (!string.IsNullOrEmpty(npc.FirstName)) return;

            npc.FirstName = FirstNamesPool[Random.Shared.Next(FirstNamesPool.Length)];
        }

        // ==================== GÉNÉRATION ALÉATOIRE DES QUÊTES ====================
        // À appeler une fois par frame (ex: dans la boucle Update principale de Program.cs)
        public static void Update(float dt, List<Entity> entities)
        {
            _spawnTimer += dt;

            //  OPTIM PERF (villages avec beaucoup de PNJ) : ce bloc reconstruisait la liste
            // des villageois (LINQ + ToList, un scan complet de `entities`) et appelait
            // EnsureTargetIsPresent (qui peut lui-même trier TOUTE la liste des entités par
            // distance si la cible d'une quête est introuvable) pour CHAQUE villageois, à
            // CHAQUE frame — donc jusqu'à 60 fois par seconde, même quand rien n'a changé
            // depuis la frame précédente. Le commentaire d'origine visait une réparation
            // "immédiate" après la mort d'une cible, mais une réparation toutes les
            // REPAIR_CHECK_INTERVAL secondes reste totalement imperceptible pour le joueur
            // (l'affichage d'une cible "???" ne dure qu'une fraction de seconde de plus) pour
            // un coût des dizaines de fois moindre.
            _repairTimer -= dt;
            if (_repairTimer <= 0f)
            {
                _repairTimer = REPAIR_CHECK_INTERVAL;
                foreach (var npc in entities)
                {
                    if (!npc.IsAlive || !npc.IsVillager || npc.IsTamed) continue;
                    AssignFirstNameIfNeeded(npc);

                    // Une cible peut avoir été supprimée après la création de la quête
                    // (mort, nettoyage d'un chunk ou ancienne sauvegarde). Réparer la référence
                    // avant de proposer une nouvelle quête ou d'afficher une cible "???".
                    EnsureTargetIsPresent(npc, entities);
                }
            }

            // La création de nouvelles quêtes reste rare : seule cette partie a réellement
            // besoin de la liste matérialisée des villageois.
            if (_spawnTimer < SPAWN_CHECK_INTERVAL) return;
            _spawnTimer = 0f;

            var villagers = entities.Where(e => e.IsAlive && e.IsVillager && !e.IsTamed).ToList();
            if (villagers.Count < 2) return;

            foreach (var npc in villagers)
            {
                if (npc.ActiveQuest != null) continue; // a déjà une idée en tête
                if (Random.Shared.NextDouble() > SPAWN_CHANCE) continue;

                var candidates = villagers.Where(v => v.NetId != npc.NetId).ToList();
                if (candidates.Count == 0) continue;
                var target = candidates[Random.Shared.Next(candidates.Count)];
                AssignFirstNameIfNeeded(target);

                var itemPool = GetItemPool(npc);
                if (itemPool.Length == 0) continue;

                var reward = itemPool[Random.Shared.Next(itemPool.Length)];
                var rewardInfo = GetCoinRewardInfo(reward.id, reward.name);

                bool wantsTameQuest = Random.Shared.NextDouble() < TAME_QUEST_CHANCE;
                string[]? tamableSpecies = wantsTameQuest
                    ? World.GetTamableSpeciesForPosition(npc.WorldPos.X, npc.WorldPos.Y)
                    : null;

                if (wantsTameQuest && tamableSpecies != null && tamableSpecies.Length > 0)
                {
                    var species = tamableSpecies[Random.Shared.Next(tamableSpecies.Length)];

                    // Contrairement à DeliverItem, TameAndBring n'a pas de tiers : l'animal
                    // apprivoisé doit revenir au PNJ qui l'a demandé (le donneur), pas à un
                    // autre PNJ tiré au hasard. GiverId == TargetId ici, volontairement.
                    npc.ActiveQuest = new Quest
                    {
                        Type = QuestType.TameAndBring,
                        GiverId = npc.NetId,
                        TargetId = npc.NetId,
                        TargetSpecies = species,
                        RewardItemId = rewardInfo.itemId,
                        RewardItemName = rewardInfo.itemName,
                        RewardItemQty = rewardInfo.qty
                    };
                }
                else
                {
                    var item = itemPool[Random.Shared.Next(itemPool.Length)];
                    // La récompense est un objet différent de celui demandé (si possible)
                    var rewardPool = itemPool.Where(i => i.id != item.id).ToArray();
                    var deliverReward = rewardPool.Length > 0
                        ? rewardPool[Random.Shared.Next(rewardPool.Length)]
                        : item;

                    npc.ActiveQuest = new Quest
                    {
                        Type = QuestType.DeliverItem,
                        GiverId = npc.NetId,
                        TargetId = target.NetId,
                        ItemId = item.id,
                        ItemName = item.name,
                        ItemQty = Random.Shared.Next(1, 4),
                        RewardItemId = rewardInfo.itemId,
                        RewardItemName = rewardInfo.itemName,
                        RewardItemQty = rewardInfo.qty
                    };
                }
                npc.ActiveQuest.LastKnownTargetPosX = target.WorldPos.X;
                npc.ActiveQuest.LastKnownTargetPosY = target.WorldPos.Y;
                npc.ActiveQuest.HasLastKnownTargetPosition = true;

                npc.ActiveQuest.LastKnownGiverPosX = npc.WorldPos.X;
                npc.ActiveQuest.LastKnownGiverPosY = npc.WorldPos.Y;
                npc.ActiveQuest.HasLastKnownGiverPosition = true;
                RegisterQuest(npc.ActiveQuest);
            }
        }

        public static bool EnsureTargetIsPresent(Entity giver, List<Entity> entities)
        {
            Quest? quest = giver.ActiveQuest;
            if (quest == null)
                quest = _persistedQuests.Values.FirstOrDefault(q => q.GiverId == giver.NetId);
            if (quest == null || quest.State == QuestState.Completed)
                return true;

            if (quest.Type == QuestType.TameAndBring)
            {
                quest.TargetId = giver.NetId;
                quest.LastKnownTargetPosX = giver.WorldPos.X;
                quest.LastKnownTargetPosY = giver.WorldPos.Y;
                quest.HasLastKnownTargetPosition = true;
                giver.ActiveQuest = quest;
                RegisterQuest(quest);
                return true;
            }

            var target = entities.FirstOrDefault(e =>
                e.IsAlive && e.IsVillager && !e.IsTamed && e.NetId == quest.TargetId && e.NetId != giver.NetId);
            if (target == null)
            {
                target = entities
                    .Where(e => e.IsAlive && e.IsVillager && !e.IsTamed && e.NetId != giver.NetId)
                    .OrderBy(e => Vector2.DistanceSquared(e.WorldPos, giver.WorldPos))
                    .FirstOrDefault();
            }

            if (target == null)
            {
                giver.ActiveQuest = null;
                UnregisterQuest(giver.NetId);
                return false;
            }

            quest.TargetId = target.NetId;
            quest.LastKnownTargetPosX = target.WorldPos.X;
            quest.LastKnownTargetPosY = target.WorldPos.Y;
            quest.HasLastKnownTargetPosition = true;
            giver.ActiveQuest = quest;
            RegisterQuest(quest);
            return true;
        }

        public static void ClearPersistedQuests()
        {
            _persistedQuests.Clear();
        }

        public static void NormalizeQuestItemData(Quest quest)
        {
            if (quest == null) return;

            if (!string.IsNullOrWhiteSpace(quest.ItemName))
            {
                quest.ItemId = Program.GetItemId(quest.ItemName);
            }
            else if (quest.ItemId > 0 && GameData.ItemDatabase.TryGetValue(quest.ItemId, out var itemData))
            {
                quest.ItemName = itemData.Name;
            }

            if (!string.IsNullOrWhiteSpace(quest.RewardItemName))
            {
                quest.RewardItemId = Program.GetItemId(quest.RewardItemName);
            }
            else if (quest.RewardItemId > 0 && GameData.ItemDatabase.TryGetValue(quest.RewardItemId, out var rewardData))
            {
                quest.RewardItemName = rewardData.Name;
            }
        }

        public static void RegisterQuest(Quest quest)
        {
            if (quest == null) return;
            if (quest.GiverId == Guid.Empty) return;
            _persistedQuests[quest.GiverId] = quest;
        }

        public static void UnregisterQuest(Guid giverId)
        {
            _persistedQuests.Remove(giverId);
        }

        public static void RestorePersistedQuests(IEnumerable<Quest> questStates)
        {
            _persistedQuests.Clear();
            foreach (var quest in questStates)
            {
                if (quest == null) continue;
                NormalizeQuestItemData(quest);
                RegisterQuest(quest);
            }
        }

        public static IReadOnlyList<Quest> GetPersistedQuestsSnapshot()
        {
            return _persistedQuests.Values.ToList();
        }

        public static List<(Quest Quest, Entity? GiverEntity, string GiverName, string TargetName)> GetQuestEntriesSnapshot(List<Entity> entities)
        {
            var result = new List<(Quest Quest, Entity? GiverEntity, string GiverName, string TargetName)>();
            foreach (var quest in _persistedQuests.Values)
            {
                if (quest.State == QuestState.Completed || quest.State == QuestState.Offered)
                    continue;

                Entity? giverEntity = entities.FirstOrDefault(e => e.NetId == quest.GiverId);
                Entity? targetEntity = entities.FirstOrDefault(e => e.NetId == quest.TargetId);
                string giverName = giverEntity?.DisplayName ?? giverEntity?.FirstName ?? "Villageois";
                string targetName = targetEntity?.DisplayName ?? targetEntity?.FirstName ?? "???";

                result.Add((quest, giverEntity, giverName, targetName));
            }

            return result;
        }

        public static IEnumerable<(Vector2 WorldPos, bool IsLoaded)> GetTrackedTargetsSnapshot(List<Entity> entities)
        {
            foreach (var quest in _persistedQuests.Values)
            {
                if (quest.State == QuestState.Completed)
                    continue;

                if (quest.State == QuestState.Accepted)
                {
                    var target = entities.FirstOrDefault(e => e.NetId == quest.TargetId);
                    if (target != null && target.IsAlive)
                    {
                        quest.LastKnownTargetPosX = target.WorldPos.X;
                        quest.LastKnownTargetPosY = target.WorldPos.Y;
                        quest.HasLastKnownTargetPosition = true;
                        yield return (target.WorldPos, true);
                    }
                    else if (quest.HasLastKnownTargetPosition)
                    {
                        yield return (new Vector2(quest.LastKnownTargetPosX, quest.LastKnownTargetPosY), false);
                    }
                }
                else if (quest.State == QuestState.Delivered)
                {
                    var giver = entities.FirstOrDefault(e => e.NetId == quest.GiverId);
                    if (giver != null && giver.IsAlive)
                    {
                        quest.LastKnownGiverPosX = giver.WorldPos.X;
                        quest.LastKnownGiverPosY = giver.WorldPos.Y;
                        quest.HasLastKnownGiverPosition = true;
                        yield return (giver.WorldPos, true);
                    }
                    else if (quest.HasLastKnownGiverPosition)
                    {
                        yield return (new Vector2(quest.LastKnownGiverPosX, quest.LastKnownGiverPosY), false);
                    }
                }
            }
        }

        public static Entity? FindEntityByNetId(List<Entity> entities, Guid netId)
            => entities.FirstOrDefault(e => e.NetId == netId);

        // Le joueur accepte la quête proposée par ce PNJ : le donneur lui remet l'objet à livrer.
        public static void AcceptQuest(Entity giver, Action<int, int> giveItem)
        {
            if (giver.ActiveQuest != null && giver.ActiveQuest.State == QuestState.Offered)
            {
                var quest = giver.ActiveQuest;
                if (quest.ItemId > 0 && quest.ItemQty > 0)
                    giveItem(quest.ItemId, quest.ItemQty);

                quest.State = QuestState.Accepted;
                RegisterQuest(quest);
            }
        }

        // Le joueur refuse : le PNJ oubliera cette idée (il pourra en avoir une nouvelle plus tard)
        public static void DeclineQuest(Entity giver)
        {
            if (giver.ActiveQuest != null && giver.ActiveQuest.State == QuestState.Offered)
                giver.ActiveQuest = null;
                UnregisterQuest(giver.NetId);
        }

        // Le donneur d'une quête en attente de validation : y a-t-il une quête livrée pour lui ?
        public static Quest? GetIncomingQuestFor(Entity npc, List<Entity> entities)
        {
            var persistedQuest = _persistedQuests.Values.FirstOrDefault(q =>
                q.GiverId == npc.NetId && q.State == QuestState.Delivered);
            if (persistedQuest != null)
                return persistedQuest;

            var giver = entities.FirstOrDefault(e => e.ActiveQuest != null
                && e.ActiveQuest.State == QuestState.Delivered
                && e.ActiveQuest.GiverId == npc.NetId);
            return giver?.ActiveQuest;
        }

        // La cible d'une quête acceptée : y a-t-il une livraison à faire à ce PNJ ?
        public static Quest? GetQuestToDeliverFor(Entity npc, List<Entity> entities)
        {
            var persistedQuest = _persistedQuests.Values.FirstOrDefault(q =>
                q.TargetId == npc.NetId && q.State == QuestState.Accepted);
            if (persistedQuest != null)
                return persistedQuest;

            var giver = entities.FirstOrDefault(e => e.ActiveQuest != null
                && e.ActiveQuest.State == QuestState.Accepted
                && e.ActiveQuest.TargetId == npc.NetId);
            return giver?.ActiveQuest;
        }

        // Tente de livrer l'objet au PNJ cible. Retourne true si la livraison a été prise en compte.
        public static bool TryDeliverQuest(
            Entity targetNpc,
            List<Entity> entities,
            Func<int, int, bool> hasItem,
            Action<int, int> removeItem,
            out Quest? deliveredQuest)
        {
            deliveredQuest = null;

            var quest = _persistedQuests.Values.FirstOrDefault(q =>
                q.TargetId == targetNpc.NetId && q.State == QuestState.Accepted);
            if (quest == null)
            {
                var giverNpc = entities.FirstOrDefault(e => e.ActiveQuest != null
                    && e.ActiveQuest.State == QuestState.Accepted
                    && e.ActiveQuest.TargetId == targetNpc.NetId);
                quest = giverNpc?.ActiveQuest;
            }

            if (quest == null) return false;

            if (!hasItem(quest.ItemId, quest.ItemQty)) return false;

            removeItem(quest.ItemId, quest.ItemQty);

            var giverEntity = entities.FirstOrDefault(e => e.NetId == quest.GiverId);
            if (giverEntity != null && giverEntity.IsAlive)
            {
                quest.LastKnownGiverPosX = giverEntity.WorldPos.X;
                quest.LastKnownGiverPosY = giverEntity.WorldPos.Y;
                quest.HasLastKnownGiverPosition = true;
            }

            quest.State = QuestState.Delivered;
            deliveredQuest = quest;
            return true;
        }

        // Distance (en pixels monde) à laquelle un animal apprivoisé doit se trouver du PNJ
        // cible pour pouvoir lui être offert.
        private const float GIFT_ANIMAL_RANGE = 220f;

        // Pour une quête TameAndBring acceptée dont targetNpc est la cible : cherche, parmi les
        // animaux apprivoisés du joueur à proximité, un individu de la bonne espèce. Ne modifie
        // rien : sert uniquement à savoir si on peut proposer la bulle de confirmation "offrir ?".
        public static Entity? FindEligibleTamedAnimalNearby(Entity targetNpc, List<Entity> entities, string ownerName)
        {
            var quest = _persistedQuests.Values.FirstOrDefault(q =>
                q.Type == QuestType.TameAndBring && q.TargetId == targetNpc.NetId && q.State == QuestState.Accepted);
            if (quest == null)
            {
                var giverNpc = entities.FirstOrDefault(e => e.ActiveQuest != null
                    && e.ActiveQuest.Type == QuestType.TameAndBring
                    && e.ActiveQuest.State == QuestState.Accepted
                    && e.ActiveQuest.TargetId == targetNpc.NetId);
                quest = giverNpc?.ActiveQuest;
            }
            if (quest == null) return null;

            return entities.FirstOrDefault(e =>
                e.IsAlive && e.IsTamed
                && string.Equals(e.OwnerName, ownerName, StringComparison.Ordinal)
                && string.Equals(e.Species, quest.TargetSpecies, StringComparison.OrdinalIgnoreCase)
                && Vector2.DistanceSquared(e.WorldPos, targetNpc.WorldPos) <= (GIFT_ANIMAL_RANGE) * (GIFT_ANIMAL_RANGE));
        }

        // Le joueur confirme le don : l'animal change de propriétaire et devient celui du PNJ,
        // exactement comme si ce dernier l'avait dressé lui-même. La quête passe "Delivered".
        public static bool TryGiftTamedAnimal(Entity targetNpc, Entity animal, List<Entity> entities, out Quest? deliveredQuest)
        {
            deliveredQuest = null;

            var quest = _persistedQuests.Values.FirstOrDefault(q =>
                q.Type == QuestType.TameAndBring && q.TargetId == targetNpc.NetId && q.State == QuestState.Accepted);
            if (quest == null)
            {
                var giverNpc = entities.FirstOrDefault(e => e.ActiveQuest != null
                    && e.ActiveQuest.Type == QuestType.TameAndBring
                    && e.ActiveQuest.State == QuestState.Accepted
                    && e.ActiveQuest.TargetId == targetNpc.NetId);
                quest = giverNpc?.ActiveQuest;
            }
            if (quest == null) return false;
            if (!string.Equals(animal.Species, quest.TargetSpecies, StringComparison.OrdinalIgnoreCase)) return false;

            // L'animal appartient désormais au PNJ cible, au même titre que s'il l'avait apprivoisé
            // lui-même : on réutilise Entity.AddPet, déjà prévu pour ça (IsTamed, OwnerName,
            // OwnerNpcId, TamedBehavior.Follow, Behavior="tamed", HomePosition). L'animal reste une
            // entité indépendante dans la liste globale (comme un apprivoisé du joueur) : c'est
            // OwnerNpcId qui le rattache à son PNJ, pour le suivi (Program.UpdateNpcOwnedPetTargets)
            // et la sauvegarde (EntitySaveData.OwnerNpcId), sans duplication.
            // Il faut d'abord le retirer de la liste de suivi du joueur (FollowOrder) pour qu'il
            // ne reste pas coincé avec un ordre de suivi obsolète une fois son propriétaire changé.
            animal.FollowOrder = -1;
            targetNpc.AddPet(animal);

            quest.OfferedAnimalId = animal.NetId;

            var giverEntity = entities.FirstOrDefault(e => e.NetId == quest.GiverId);
            if (giverEntity != null && giverEntity.IsAlive)
            {
                quest.LastKnownGiverPosX = giverEntity.WorldPos.X;
                quest.LastKnownGiverPosY = giverEntity.WorldPos.Y;
                quest.HasLastKnownGiverPosition = true;
            }

            quest.State = QuestState.Delivered;
            deliveredQuest = quest;
            return true;
        }

        // Tente de valider la quête auprès du donneur pour lui faire remettre la récompense.
        public static bool TryCompleteQuest(
            Entity giverNpc,
            List<Entity> entities,
            Action<int, int> giveItem,
            out Quest? completedQuest)
        {
            completedQuest = null;

            var quest = _persistedQuests.Values.FirstOrDefault(q =>
                q.GiverId == giverNpc.NetId && q.State == QuestState.Delivered);
            if (quest == null && giverNpc.ActiveQuest != null && giverNpc.ActiveQuest.State == QuestState.Delivered)
                quest = giverNpc.ActiveQuest;

            if (quest == null)
                return false;

            int rewardCoinId = Program.GetItemId("coin");
            if (rewardCoinId > 0 && quest.RewardItemQty > 0)
            {
                giveItem(rewardCoinId, quest.RewardItemQty);
            }
            else if (quest.RewardItemId > 0 && quest.RewardItemQty > 0)
            {
                giveItem(quest.RewardItemId, quest.RewardItemQty);
            }

            giverNpc.Friendship = Math.Clamp(giverNpc.Friendship + 0.2f, 0f, 1f);
            quest.State = QuestState.Completed;
            completedQuest = quest;

            if (giverNpc.ActiveQuest == quest)
                giverNpc.ActiveQuest = null; // le donneur pourra avoir une nouvelle idée plus tard

            UnregisterQuest(giverNpc.NetId);
            return true;
        }
    }

    // ==================== RECRUTEMENT DE GUILDE ====================
    // Principe (miroir de QuestManager) :
    //  - Un villageois dont l'amitié avec le joueur est au maximum (Friendship >= 1f) se
    //    propose pour rejoindre la guilde du joueur (si le joueur en a fondé une). Il affiche
    //    alors assets/gui/guild_joinable.png au-dessus de sa tête.
    //    au-dessus de sa tête.
    //  - L'icône "guild_joinable" au-dessus de sa tête est dessinée par World.cs, au même
    //    endroit et de la même façon que les icônes de commerce et de quête (côte à côte).
    //  - L'acceptation se fait via la bulle de dialogue (touche E / clic gauche), exactement
    //    comme pour discuter d'une quête ou commercer : plus de clic droit dédié.
    //    Une fois accepté, le PNJ rejoint la guilde et se comporte alors exactement comme un
    //    animal apprivoisé (IsTamed + TamedBehavior : Follow/Stay/Roam/Attack, ordres, défense),
    //    via les systèmes déjà existants (TamedAnimalUI / TamedAnimalInteraction / Entity.Update).
    public static class GuildRecruitManager
    {
        private const float SPAWN_CHECK_INTERVAL = 15f;

        private static float _spawnTimer = 0f;

        // À appeler une fois par frame (comme QuestManager.Update).
        public static void Update(float dt, List<Entity> entities)
        {
            _spawnTimer += dt;
            if (_spawnTimer < SPAWN_CHECK_INTERVAL) return;
            _spawnTimer = 0f;

            // Pas de guilde : personne ne peut se proposer.
            if (Program.PlayerGuild == null) return;

            var candidates = entities.Where(e =>
                e.IsAlive && e.IsVillager && !e.IsTamed && !e.IsGuildMember &&
                !e.WantsToJoinGuild && e.Friendship >= 1f).ToList();

            foreach (var npc in candidates)
            {
                npc.WantsToJoinGuild = true;
            }
        }

        // Le PNJ propose activement de rejoindre la guilde, et le joueur en a une.
        public static bool CanAccept(Entity npc)
        {
            return (npc.WantsToJoinGuild || npc.Friendship >= 1f)
                && !npc.IsTamed && !npc.IsGuildMember && Program.PlayerGuild != null;
        }

        // Accepte l'offre : le PNJ rejoint la guilde et devient un compagnon "apprivoisé"
        // (mêmes règles que les animaux tamés : suit, défend, obéit aux ordres).
        public static bool TryAccept(Entity npc, string ownerName)
        {
            if (!CanAccept(npc)) return false;

            npc.WantsToJoinGuild = false;
            npc.IsGuildMember = true;
            npc.IsTamed = true;
            npc.TamedBehavior = TamedAnimalMode.Follow;
            npc.OwnerName = ownerName;
            npc.Behavior = "tamed";

            if (Program.PlayerGuild != null && !Program.PlayerGuild.MemberIds.Contains(npc.NetId))
                Program.PlayerGuild.MemberIds.Add(npc.NetId);

            return true;
        }
    }

    // ==================== INTERFACE DE DIALOGUE DE QUÊTE ====================
    // Petite fenêtre modale : proposition de quête (Accepter / Refuser),
    // ou simple rappel quand on reparle au donneur/à la cible.
    public static class QuestDialogUI
    {
        public static bool IsOpen { get; private set; } = false;

        private static Entity? _currentNpc = null;
        private static string _mode = "offer"; // "choice" (choix discuter/commercer/rien) | "offer" (proposition) | "reminder" (rappel)
        // true quand le mode "offer" est utilisé pour confirmer le don d'un animal apprivoisé
        // (plutôt que pour proposer une nouvelle quête) : change les actions renvoyées par Update().
        private static bool _isGiftConfirm = false;
        private static string _bodyText = "";
        private static string _rewardText = "";

        // Options disponibles quand on est en mode "choice"
        private static bool _choiceHasQuest = false;
        private static bool _choiceHasOfferAction = false;
        private static bool _choiceHasValidationAction = false;
        private static bool _choiceHasDeliverAction = false;
        private static bool _choiceHasGenericQuestAction = false;
        private static bool _choiceHasTrade = false;
        private static bool _choiceHasGuildJoin = false;
        private static bool _choiceHasCards = false;
        private static bool _choiceHasSocial = false;

        //  Effet machine à écrire : le texte s'affiche progressivement au lieu d'apparaître d'un coup.
        //  On impose aussi une durée minimale pour éviter que les lignes courtes apparaissent
        //  d'un seul coup alors que la première page est plus longue et plus lisible en typewriter.
        private const float TYPEWRITER_CHARS_PER_SECOND = 45f;
        private const float TYPEWRITER_MIN_SECONDS = 0.7f;
        private static float _typewriterElapsed = 0f;
        private static bool _ignoreNextMousePress = false;

        private static Rectangle _acceptRect;
        private static Rectangle _declineRect;
        private static Rectangle _closeRect;
        private static Rectangle _discussRect;
        private static Rectangle _offerRect;
        private static Rectangle _validateRect;
        private static Rectangle _deliverRect;
        private static Rectangle _tradeRect;
        private static Rectangle _guildRect;
        private static Rectangle _cardsRect;
        private static Rectangle _socialRect;
        private static Rectangle _nothingRect;
        private static Rectangle _friendshipBarRect;
        private static Shader _friendshipBarShader = new Shader();
        private static bool _friendshipBarShaderLoaded = false;
        private static int _friendshipBarFillPercentLoc;
        private static int _friendshipBarFillColorLoc;
        private static Texture2D _friendshipBarTexture = new Texture2D();
        private static bool _friendshipBarTextureLoaded = false;

        // Icônes de palier d'amitié affichées au-dessus de la barre de social
        private static Texture2D _socialIconMid = new Texture2D();
        private static Texture2D _socialIconOkay = new Texture2D();
        private static Texture2D _socialIconGood = new Texture2D();
        private static bool _socialIconsLoaded = false;

        private static Texture2D _acceptButtonTexture = new Texture2D();
        private static Texture2D _acceptButtonPressedTexture = new Texture2D();
        private static Texture2D _declineButtonTexture = new Texture2D();
        private static Texture2D _declineButtonPressedTexture = new Texture2D();
        private static bool _acceptButtonPressed = false;
        private static bool _declineButtonPressed = false;
        private static bool _questButtonTexturesLoaded = false;

        // Icônes affichées à gauche de chaque ligne de choix dans la bulle de dialogue
        private static Texture2D _iconTrade = new Texture2D();
        private static Texture2D _iconCards = new Texture2D();
        private static Texture2D _iconQuestStart = new Texture2D();
        private static Texture2D _iconQuestFinish = new Texture2D();
        private static Texture2D _iconGuildJoin = new Texture2D();
        private static Texture2D _iconLeave = new Texture2D();
        private static bool _choiceIconsLoaded = false;

        private static readonly Dictionary<ProfessionType, Texture2D> _professionWorkIcons = new();
        private static bool _professionWorkIconsLoaded = false;

        private static readonly Color COLOR_CHOICE_HIGHLIGHT = new Color(255, 255, 255, 40);
        private static readonly Color COLOR_CHOICE_SEPARATOR = new Color(255, 255, 255, 100);

        private static void ResetTypewriter()
        {
            _typewriterElapsed = 0f;
            _ignoreNextMousePress = true;
        }

        private static void EnsureFriendshipBarShaderLoaded()
        {
            if (_friendshipBarShaderLoaded) return;
            if (File.Exists("assets/shaders/bubblebar.fs"))
            {
                _friendshipBarShader = Raylib.LoadShader(null, "assets/shaders/bubblebar.fs");
                _friendshipBarShaderLoaded = _friendshipBarShader.Id != 0;
                if (_friendshipBarShaderLoaded)
                {
                    _friendshipBarFillPercentLoc = Raylib.GetShaderLocation(_friendshipBarShader, "fillPercent");
                    _friendshipBarFillColorLoc = Raylib.GetShaderLocation(_friendshipBarShader, "fillColor");
                }
            }
        }

        private static void EnsureFriendshipBarTextureLoaded()
        {
            if (_friendshipBarTextureLoaded) return;
            _friendshipBarTextureLoaded = true;

            if (File.Exists("assets/gui/friendship_bar.png"))
            {
                _friendshipBarTexture = Raylib.LoadTexture("assets/gui/friendship_bar.png");
                if (_friendshipBarTexture.Id != 0)
                {
                    Raylib.SetTextureFilter(_friendshipBarTexture, TextureFilter.Point);
                }
            }
        }

        private static void EnsureSocialIconsLoaded()
        {
            if (_socialIconsLoaded) return;
            _socialIconsLoaded = true;

            _socialIconMid = LoadQuestButtonTexture("assets/gui/social_mid.png", "");
            _socialIconOkay = LoadQuestButtonTexture("assets/gui/social_okay.png", "");
            _socialIconGood = LoadQuestButtonTexture("assets/gui/social_good.png", "");
        }

        // Retourne l'icône de palier correspondant au niveau d'amitié actuel (0..1)
        private static Texture2D GetSocialTierIcon(float friendship)
        {
            float f = Math.Clamp(friendship, 0f, 1f);
            if (f >= 0.66f) return _socialIconGood;
            if (f >= 0.33f) return _socialIconOkay;
            return _socialIconMid;
        }
        private static void EnsureQuestButtonTexturesLoaded()
        {
            if (_questButtonTexturesLoaded) return;
            _questButtonTexturesLoaded = true;

            _acceptButtonTexture = LoadQuestButtonTexture("assets/gui/button_yes.png", "");
            _acceptButtonPressedTexture = LoadQuestButtonTexture("assets/gui/button_yes_pressed.png", "");
            _declineButtonTexture = LoadQuestButtonTexture("assets/gui/button_no.png", "");
            _declineButtonPressedTexture = LoadQuestButtonTexture("assets/gui/button_no_pressed.png", "");
        }

        private static void EnsureChoiceIconsLoaded()
        {
            if (_choiceIconsLoaded) return;
            _choiceIconsLoaded = true;

            _iconTrade = LoadQuestButtonTexture("assets/gui/icon_trade.png", "");
            _iconCards = LoadQuestButtonTexture("assets/gui/icon_cards.png", "");
            _iconQuestStart = LoadQuestButtonTexture("assets/gui/quest_start.png", "");
            _iconQuestFinish = LoadQuestButtonTexture("assets/gui/quest_finish.png", "");
            _iconGuildJoin = LoadQuestButtonTexture("assets/gui/guild_joinable.png", "");
            _iconLeave = LoadQuestButtonTexture("assets/gui/leave.png", "");
        }

        private static void EnsureProfessionWorkIconsLoaded()
        {
            if (_professionWorkIconsLoaded) return;
            _professionWorkIconsLoaded = true;

            foreach (ProfessionType profession in Enum.GetValues<ProfessionType>())
            {
                if (profession == ProfessionType.None) continue;
                string path = GetProfessionWorkIconPath(profession);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                _professionWorkIcons[profession] = Raylib.LoadTexture(path);
            }
        }

        private static string GetProfessionWorkIconPath(ProfessionType profession)
        {
            return profession switch
            {
                ProfessionType.Farmer => "assets/gui/work_icon_farmer.png",
                ProfessionType.Blacksmith => "assets/gui/work_icon_blacksmith.png",
                ProfessionType.Lumberjack => "assets/gui/work_icon_woodsman.png",
                ProfessionType.Hunter => "assets/gui/work_icon_hunter.png",
                ProfessionType.Alchemist => "assets/gui/work_icon_alchemist.png",
                ProfessionType.Tailor => "assets/gui/work_icon_tailor.png",
                ProfessionType.Miner => "assets/gui/work_icon_miner.png",
                ProfessionType.Cook => "assets/gui/work_icon_cook.png",
                _ => string.Empty,
            };
        }

        private static Texture2D GetProfessionWorkIcon(ProfessionType profession)
        {
            if (profession == ProfessionType.None) return new Texture2D();

            if (_professionWorkIcons.TryGetValue(profession, out var texture))
                return texture;

            string path = GetProfessionWorkIconPath(profession);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new Texture2D();

            texture = Raylib.LoadTexture(path);
            _professionWorkIcons[profession] = texture;
            return texture;
        }

        private static Texture2D LoadQuestButtonTexture(string path, string fallbackPath)
        {
            if (File.Exists(path))
                return Raylib.LoadTexture(path);

            if (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath))
                return Raylib.LoadTexture(fallbackPath);

            return new Texture2D();
        }

        private static bool HasValidTexture(Texture2D texture) => texture.Id != 0;

        // Nombre de caractères actuellement visibles du texte en cours d'affichage
        private static int GetVisibleCharCount(string fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return 0;

            float baseDuration = fullText.Length / TYPEWRITER_CHARS_PER_SECOND;
            float effectiveDuration = Math.Max(baseDuration, TYPEWRITER_MIN_SECONDS);
            float progress = Math.Clamp(_typewriterElapsed / effectiveDuration, 0f, 1f);
            int visible = (int)(progress * fullText.Length);
            return Math.Clamp(visible, 0, fullText.Length);
        }

        private static bool IsTypewriterDone(string fullText) => GetVisibleCharCount(fullText) >= fullText.Length;

        // Affiche une bulle de choix : discuter de la quête / commercer / ne rien faire.
        // C'est l'écran qui s'ouvre en premier lorsqu'on appuie sur E sur un PNJ ayant
        // plusieurs interactions possibles (quête et/ou commerce).
        public static void OpenChoice(Entity npc, bool hasQuest, bool hasTrade)
        {
            _currentNpc = npc;
            _mode = "choice";
            _choiceHasQuest = hasQuest;
            _choiceHasOfferAction = false;
            _choiceHasValidationAction = false;
            _choiceHasDeliverAction = false;
            _choiceHasGenericQuestAction = false;
            _choiceHasTrade = hasTrade;
            _choiceHasGuildJoin = false;
            _choiceHasCards = false;
            _choiceHasSocial = npc.IsVillager && !npc.IsTamed;
            _bodyText = Localization.Get("quest.choice.greeting", "Bonjour ! Que puis-je faire pour toi ?");
            _rewardText = "";
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            ResetTypewriter();
            IsOpen = true;
        }

        public static void OpenChoice(Entity npc, bool hasOfferAction, bool hasValidationAction, bool hasDeliverAction, bool hasTrade, bool hasGuildJoin = false, bool hasCards = false, bool hasSocial = false)
        {
            _currentNpc = npc;
            _mode = "choice";
            _choiceHasQuest = false;
            _choiceHasOfferAction = hasOfferAction;
            _choiceHasValidationAction = hasValidationAction;
            _choiceHasDeliverAction = hasDeliverAction;
            _choiceHasGenericQuestAction = false;
            _choiceHasTrade = hasTrade;
            _choiceHasGuildJoin = hasGuildJoin;
            _choiceHasCards = hasCards;
            _choiceHasSocial = hasSocial;
            _bodyText = Localization.Get("quest.choice.greeting", "Bonjour ! Que puis-je faire pour toi ?");
            _rewardText = "";
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            ResetTypewriter();
            IsOpen = true;
        }

        public static void OpenSocialChat(Entity npc)
        {
            _currentNpc = npc;
            _mode = "chat";
            _bodyText = npc.GetSocialOpeningLine();
            _rewardText = "";
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            ResetTypewriter();
            IsOpen = true;
        }

        public static void OpenSocialFollowup(Entity npc, string response)
        {
            _currentNpc = npc;
            _mode = "chat_followup";
            _bodyText = response;
            _rewardText = "";
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            ResetTypewriter();
            IsOpen = true;
        }

        public static void OpenOffer(Entity giver, Entity? target)
        {
            if (giver.ActiveQuest == null) return;
            _currentNpc = giver;
            _mode = "offer";
            var targetName = target?.DisplayName ?? target?.FirstName ?? Localization.Get("quest.unknown_target", "???");
            _bodyText = giver.ActiveQuest.GetOfferLine(giver.DisplayName ?? Localization.Get("quest.unknown_target", "???"), targetName);
            _rewardText = string.Format(Localization.Get("quest.offer_reward", "Récompense : {0}"), giver.ActiveQuest.GetRewardLine());
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            _isGiftConfirm = false;
            ResetTypewriter();
            IsOpen = true;
        }

        //  Bulle de confirmation "Veux-tu offrir cet animal à X ?" affichée quand le joueur
        // approche du PNJ cible d'une quête TameAndBring avec un animal éligible à proximité.
        // Réutilise exactement la mise en page du mode "offer" (texte + boutons Accepter/Refuser),
        // mais renvoie "gift_accept"/"gift_decline" au lieu de "accept"/"decline" pour ne pas être
        // confondue avec l'acceptation d'une nouvelle quête.
        public static void OpenGiftConfirm(Entity targetNpc, string text)
        {
            _currentNpc = targetNpc;
            _mode = "offer";
            _bodyText = text;
            _rewardText = "";
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            _isGiftConfirm = true;
            ResetTypewriter();
            IsOpen = true;
        }

        public static void OpenReminder(Entity npcSpokenTo, string text)
        {
            _currentNpc = npcSpokenTo;
            _mode = "reminder";
            _bodyText = text;
            _rewardText = "";
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            ResetTypewriter();
            IsOpen = true;
        }

        private static float GetDialogWidth() => 760f;

        private static float GetDialogLeftPanelWidth() => 180f;

        private static float GetDialogRightPanelWidth(float dialogWidth)
        {
            float leftPanelWidth = GetDialogLeftPanelWidth();
            return dialogWidth - (24f + leftPanelWidth + 4f + 24f);
        }

        private static float GetDialogHeightForCurrentMode()
        {
            float bodyHeight = GetWrappedTextHeight(_bodyText, (int)(GetDialogRightPanelWidth(GetDialogWidth()) - 20f), 17);
            float minHeight = _mode == "offer" ? 270f : _mode == "choice" ? 284f : 220f;
            float optionHeight = 34f;

            if (_mode == "chat")
            {
                float optionCount = 5f;
                return Math.Max(minHeight, 96f + bodyHeight + optionCount * optionHeight + 18f);
            }
            if (_mode == "chat_followup")
            {
                float optionCount = 2f;
                return Math.Max(minHeight, 96f + bodyHeight + optionCount * optionHeight + 18f);
            }
            if (_mode == "choice")
            {
                float optionCount = 0f;
                if (_choiceHasOfferAction) optionCount++;
                if (_choiceHasValidationAction) optionCount++;
                if (_choiceHasDeliverAction) optionCount++;
                if (_choiceHasTrade) optionCount++;
                if (_choiceHasGuildJoin) optionCount++;
                if (_choiceHasCards) optionCount++;
                if (_choiceHasSocial) optionCount++;
                if (_choiceHasQuest || _choiceHasGenericQuestAction) optionCount++;
                optionCount++;
                return Math.Max(minHeight, 82f + bodyHeight + optionCount * optionHeight + 12f);
            }

            return minHeight;
        }

        public static void Close()
        {
            IsOpen = false;
            _acceptButtonPressed = false;
            _declineButtonPressed = false;
            //  On NE remet PAS _currentNpc à null ici : Program.cs a besoin de le lire
            // juste après l'appel à Update() (ex: pour appeler QuestManager.AcceptQuest).
            // Il sera de toute façon écrasé au prochain OpenOffer/OpenReminder.
        }

        public static Entity? GetCurrentNpc() => _currentNpc;

        // Retourne "accept", "decline", "close", "discuss_quest", "trade" ou null selon l'action du joueur ce frame-ci
        public static string? Update(Vector2 mousePos)
        {
            if (!IsOpen) return null;

            if (_ignoreNextMousePress)
            {
                _ignoreNextMousePress = false;
                return null;
            }

            EnsureQuestButtonTexturesLoaded();
            EnsureChoiceIconsLoaded();
            EnsureFriendshipBarShaderLoaded();
            EnsureFriendshipBarTextureLoaded();
            EnsureSocialIconsLoaded();
            _typewriterElapsed += Raylib.GetFrameTime();
            bool stillTyping = !IsTypewriterDone(_bodyText);
            bool mousePressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            bool mouseReleased = Raylib.IsMouseButtonReleased(MouseButton.Left);
            bool mouseDown = Raylib.IsMouseButtonDown(MouseButton.Left);

            if (mousePressed && stillTyping)
            {
                float fullDuration = Math.Max(_bodyText.Length / TYPEWRITER_CHARS_PER_SECOND, TYPEWRITER_MIN_SECONDS);
                _typewriterElapsed = fullDuration + 0.01f;
                stillTyping = false;
                return null;
            }

            float w = GetDialogWidth();
            float h = GetDialogHeightForCurrentMode();
            float x = (Raylib.GetScreenWidth() - w) / 2f;
            float y = Raylib.GetScreenHeight() - h - 34f;

            float leftPanelWidth = GetDialogLeftPanelWidth();
            Rectangle leftPanel = new Rectangle(x + 24f, y + 24f, leftPanelWidth, h - 48f);
            Rectangle rightPanel = new Rectangle(x + 24f + leftPanelWidth + 4f, y + 24f, GetDialogRightPanelWidth(w), h - 48f);

            if (_mode == "choice")
            {
                if (!stillTyping)
                {
                    int textSize = 17;
                    float textW = rightPanel.Width - 20f;
                    float bodyHeight = GetWrappedTextHeight(_bodyText, (int)textW, textSize);
                    float listTop = rightPanel.Y + 12f + bodyHeight + 12f;
                    float itemHeight = 34f;
                    float itemX = rightPanel.X + 8f;
                    float itemW = rightPanel.Width - 16f;
                    float cursorY = listTop;

                    Rectangle? offerRect = null, validateRect = null, deliverRect = null, discussRect = null, tradeRect = null, guildRect = null, cardsRect = null, socialRect = null;
                    if (_choiceHasOfferAction) { offerRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasValidationAction) { validateRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasDeliverAction) { deliverRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasTrade) { tradeRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasGuildJoin) { guildRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasCards) { cardsRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasSocial) { socialRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    if (_choiceHasQuest || _choiceHasGenericQuestAction) { discussRect = new Rectangle(itemX, cursorY, itemW, itemHeight); cursorY += itemHeight; }
                    _nothingRect = new Rectangle(itemX, cursorY, itemW, itemHeight);
                    _offerRect = offerRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _validateRect = validateRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _deliverRect = deliverRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _discussRect = discussRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _tradeRect = tradeRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _guildRect = guildRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _cardsRect = cardsRect ?? new Rectangle(-1000, -1000, 0, 0);
                    _socialRect = socialRect ?? new Rectangle(-1000, -1000, 0, 0);
                }

                if (mousePressed)
                {
                    if (_choiceHasOfferAction && Raylib.CheckCollisionPointRec(mousePos, _offerRect)) { Close(); return "accept_offer"; }
                    if (_choiceHasValidationAction && Raylib.CheckCollisionPointRec(mousePos, _validateRect)) { Close(); return "validate_quest"; }
                    if (_choiceHasDeliverAction && Raylib.CheckCollisionPointRec(mousePos, _deliverRect)) { Close(); return "deliver_quest"; }
                    if (_choiceHasTrade && Raylib.CheckCollisionPointRec(mousePos, _tradeRect)) { Close(); return "trade"; }
                    if (_choiceHasGuildJoin && Raylib.CheckCollisionPointRec(mousePos, _guildRect)) { Close(); return "join_guild"; }
                    if (_choiceHasCards && Raylib.CheckCollisionPointRec(mousePos, _cardsRect)) { Close(); return "play_cards"; }
                    if (_choiceHasSocial && Raylib.CheckCollisionPointRec(mousePos, _socialRect)) { Close(); return "social_chat"; }
                    if ((_choiceHasQuest || _choiceHasGenericQuestAction) && Raylib.CheckCollisionPointRec(mousePos, _discussRect)) { return "discuss_quest"; }
                    if (Raylib.CheckCollisionPointRec(mousePos, _nothingRect)) { Close(); return "close"; }
                }
            }
            else if (_mode == "chat")
            {
                int textSize = 17;
                float textW = rightPanel.Width - 20f;
                float bodyHeight = GetWrappedTextHeight(_bodyText, (int)textW, textSize);
                float listTop = rightPanel.Y + 12f + bodyHeight + 12f;
                float itemHeight = 34f;
                float itemX = rightPanel.X + 8f;
                float itemW = rightPanel.Width - 16f;
                var topicRects = new[]
                {
                    new Rectangle(itemX, listTop + 0f * itemHeight, itemW, itemHeight),
                    new Rectangle(itemX, listTop + 1f * itemHeight, itemW, itemHeight),
                    new Rectangle(itemX, listTop + 2f * itemHeight, itemW, itemHeight),
                    new Rectangle(itemX, listTop + 3f * itemHeight, itemW, itemHeight),
                    new Rectangle(itemX, listTop + 4f * itemHeight, itemW, itemHeight)
                };

                if (mousePressed)
                {
                    if (Raylib.CheckCollisionPointRec(mousePos, topicRects[0])) { Close(); return "social_village"; }
                    if (Raylib.CheckCollisionPointRec(mousePos, topicRects[1])) { Close(); return "social_self"; }
                    if (Raylib.CheckCollisionPointRec(mousePos, topicRects[2])) { Close(); return "social_others"; }
                    if (Raylib.CheckCollisionPointRec(mousePos, topicRects[3])) { Close(); return "social_wish"; }
                    if (Raylib.CheckCollisionPointRec(mousePos, topicRects[4])) { Close(); return "close"; }
                }
            }
            else if (_mode == "chat_followup")
            {
                float textW = rightPanel.Width - 20f;
                float bodyHeight = GetWrappedTextHeight(_bodyText, (int)textW, 17);
                float listTop = rightPanel.Y + 12f + bodyHeight + 12f;
                float itemHeight = 34f;
                float itemX = rightPanel.X + 8f;
                float itemW = rightPanel.Width - 16f;
                var followUpRects = new[]
                {
                    new Rectangle(itemX, listTop + 0f * itemHeight, itemW, itemHeight),
                    new Rectangle(itemX, listTop + 1f * itemHeight, itemW, itemHeight)
                };

                if (mousePressed)
                {
                    if (Raylib.CheckCollisionPointRec(mousePos, followUpRects[0])) { Close(); return "social_continue"; }
                    if (Raylib.CheckCollisionPointRec(mousePos, followUpRects[1])) { Close(); return "social_back"; }
                }
            }
            else if (_mode == "offer")
            {
                float buttonWidth = 108f;
                float buttonHeight = 40f;
                float buttonGap = 20f;
                float totalButtonWidth = buttonWidth * 2f + buttonGap;
                float buttonsRowX = rightPanel.X + (rightPanel.Width - totalButtonWidth) / 2f;
                float buttonsRowY = rightPanel.Y + rightPanel.Height - buttonHeight - 24f;
                _acceptRect = new Rectangle(buttonsRowX, buttonsRowY, buttonWidth, buttonHeight);
                _declineRect = new Rectangle(buttonsRowX + buttonWidth + buttonGap, buttonsRowY, buttonWidth, buttonHeight);
                _closeRect = new Rectangle(rightPanel.X + rightPanel.Width - 34, rightPanel.Y + 12, 24, 24);

                bool acceptHovered = Raylib.CheckCollisionPointRec(mousePos, _acceptRect);
                bool declineHovered = Raylib.CheckCollisionPointRec(mousePos, _declineRect);

                if (mousePressed)
                {
                    if (acceptHovered)
                    {
                        _acceptButtonPressed = true;
                        _declineButtonPressed = false;
                    }
                    else if (declineHovered)
                    {
                        _declineButtonPressed = true;
                        _acceptButtonPressed = false;
                    }
                }
                else if (mouseReleased)
                {
                    if (_acceptButtonPressed && acceptHovered) { Close(); return _isGiftConfirm ? "gift_accept" : "accept"; }
                    if (_declineButtonPressed && declineHovered) { Close(); return _isGiftConfirm ? "gift_decline" : "decline"; }
                    _acceptButtonPressed = false;
                    _declineButtonPressed = false;
                }
                else if (!mouseDown)
                {
                    _acceptButtonPressed = false;
                    _declineButtonPressed = false;
                }

                if (Raylib.CheckCollisionPointRec(mousePos, _closeRect) && mousePressed)
                {
                    Close();
                    return "close";
                }
            }
            else
            {
                _closeRect = new Rectangle(rightPanel.X + rightPanel.Width - 112, rightPanel.Y + rightPanel.Height - 50, 96, 36);
                if (mousePressed)
                {
                    if (Raylib.CheckCollisionPointRec(mousePos, _closeRect))
                    {
                        Close();
                        return "close";
                    }
                }
            }
            return null;
        }

        public static void Draw()
        {
            if (!IsOpen || _currentNpc == null) return;

            float w = GetDialogWidth();
            float h = GetDialogHeightForCurrentMode();
            float x = (Raylib.GetScreenWidth() - w) / 2f;
            float y = Raylib.GetScreenHeight() - h - 34f;

            Rectangle outerRect = new Rectangle(x, y, w, h);
            float leftMargin = 24f;
            float panelOverlap = 8f;
            float leftPanelWidth = GetDialogLeftPanelWidth();
            float panelTop = y + 24f;
            float panelBottom = y + h - 24f;
            Rectangle leftPanel = new Rectangle(x + leftMargin, panelBottom - (h - 48f), leftPanelWidth, h - 48f);
            Rectangle rightPanel = new Rectangle(
                x + leftMargin + leftPanelWidth + panelOverlap,
                panelTop,
                GetDialogRightPanelWidth(w) - panelOverlap,
                h - 48f);

            // Panneau droit : utiliser le nine-slice TalkBubble si disponible, sinon fallback rounded
            if (UIManager.TalkBubblePanelTexture != null && UIManager.TalkBubblePanelTexture.IsValid)
            {
                UIManager.TalkBubblePanelTexture.Draw(rightPanel, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(rightPanel, 0.12f, 16, new Color(48, 39, 30, 235));
                Raylib.DrawRectangleRoundedLines(rightPanel, 0.12f, 16, 2f, new Color(220, 188, 115, 220));
            }

            string title = _currentNpc.DisplayName ?? Localization.Get("quest.npc_title", "Villageois");

            // Bulle du portrait : un peu plus grande pour mieux équilibrer le panneau
            float bubbleSize = 148f;

            // Icône de palier d'amitié, au-dessus de la barre
            float socialIconSize = 22f;
            float socialIconGap = 6f;
            float friendshipBarHeight = bubbleSize - socialIconSize - socialIconGap;

            // Largeur de la barre calculée à partir du ratio de la texture, pour ne jamais l'étirer
            // (quitte à ce que la barre soit un peu plus large que l'ancienne largeur fixe de 14px).
            float friendshipBarWidth = 16f; // fallback si la texture n'est pas chargée
            if (_friendshipBarTexture.Id != 0 && _friendshipBarTexture.Height > 0)
            {
                float texAspect = (float)_friendshipBarTexture.Width / _friendshipBarTexture.Height;
                friendshipBarWidth = friendshipBarHeight * texAspect;
            }
            float friendshipColumnWidth = Math.Max(friendshipBarWidth, socialIconSize);
            float friendshipBarGap = 8f;

            Rectangle nameBoxRect = new Rectangle(
                leftPanel.X + friendshipColumnWidth + friendshipBarGap,
                rightPanel.Y + rightPanel.Height - 54f,
                bubbleSize,
                54f);

            Rectangle portraitBubbleRect = new Rectangle(
                nameBoxRect.X,
                nameBoxRect.Y - bubbleSize - 8f,
                bubbleSize,
                bubbleSize);

            if (UIManager.BackpackPanelTexture != null && UIManager.BackpackPanelTexture.IsValid)
            {
                UIManager.BackpackPanelTexture.Draw(portraitBubbleRect, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(portraitBubbleRect, 0.2f, 16, new Color(24, 20, 16, 240));
                Raylib.DrawRectangleRoundedLines(portraitBubbleRect, 0.2f, 16, 2f, new Color(190, 160, 100, 255));
            }

            float portraitPadding = 10f;
            Rectangle portraitRect = new Rectangle(
                portraitBubbleRect.X + portraitPadding,
                portraitBubbleRect.Y + portraitPadding,
                portraitBubbleRect.Width - portraitPadding * 2f,
                portraitBubbleRect.Height - portraitPadding * 2f);
            DrawNpcPortrait(_currentNpc, portraitRect);

            EnsureProfessionWorkIconsLoaded();
            if (_currentNpc.HasProfession)
            {
                Texture2D professionIcon = GetProfessionWorkIcon(_currentNpc.Profession);
                if (professionIcon.Id != 0)
                {
                    float professionIconSize = 20f;
                    Rectangle professionIconRect = new Rectangle(
                        portraitRect.X + portraitRect.Width - professionIconSize - 4f,
                        portraitRect.Y + 4f,
                        professionIconSize,
                        professionIconSize);

                    Raylib.DrawRectangleRounded(professionIconRect, 0.22f, 8, new Color(28, 24, 20, 220));
                    Raylib.DrawRectangleRoundedLines(professionIconRect, 0.22f, 8, 1f, new Color(220, 188, 115, 220));
                    Raylib.DrawTexturePro(
                        professionIcon,
                        new Rectangle(0, 0, professionIcon.Width, professionIcon.Height),
                        professionIconRect,
                        Vector2.Zero,
                        0f,
                        Color.White);
                }
            }

            // Icône de palier d'amitié : centrée au-dessus de la barre
            float friendshipColumnX = leftPanel.X + (friendshipColumnWidth - socialIconSize) / 2f;
            Rectangle socialIconRect = new Rectangle(friendshipColumnX, portraitBubbleRect.Y, socialIconSize, socialIconSize);
            Texture2D socialIcon = GetSocialTierIcon(_currentNpc.Friendship);
            if (socialIcon.Id != 0)
            {
                Raylib.DrawTexturePro(socialIcon, new Rectangle(0, 0, socialIcon.Width, socialIcon.Height),
                    socialIconRect, Vector2.Zero, 0f, Color.White);
            }

            // Jauge d'amitié verticale : à gauche de la bulle du portrait, sous l'icône de palier
            float friendshipBarX = leftPanel.X + (friendshipColumnWidth - friendshipBarWidth) / 2f;
            _friendshipBarRect = new Rectangle(friendshipBarX, portraitBubbleRect.Y + socialIconSize + socialIconGap, friendshipBarWidth, friendshipBarHeight);
            DrawFriendshipBar(_friendshipBarRect, _currentNpc.Friendship);

            // Case du prénom : fixée au niveau du bas de la boîte de dialogue.
            if (UIManager.HealthBarPanelTexture != null && UIManager.HealthBarPanelTexture.IsValid)
            {
                UIManager.HealthBarPanelTexture.Draw(nameBoxRect, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(nameBoxRect, 0.2f, 12, new Color(50, 40, 30, 240));
                Raylib.DrawRectangleRoundedLines(nameBoxRect, 0.2f, 12, 2f, new Color(220, 188, 115, 220));
            }

            int titleTextWidth = FontManager.MeasureText(title, 16);
            float titleX = nameBoxRect.X + (nameBoxRect.Width - titleTextWidth) / 2f;
            float titleY = nameBoxRect.Y + 10f;
            FontManager.DrawText(title, (int)titleX, (int)titleY, 16, new Color(245, 225, 155, 255));

            string professionLabel = _currentNpc.HasProfession ? _currentNpc.GetProfessionDisplayName() : Localization.Get("profession.trader", "Marchand");
            int professionTextWidth = FontManager.MeasureText(professionLabel, 12);
            float professionX = nameBoxRect.X + (nameBoxRect.Width - professionTextWidth) / 2f;
            float professionY = nameBoxRect.Y + 28f;
            FontManager.DrawText(professionLabel, (int)professionX, (int)professionY, 12, new Color(215, 210, 190, 255));

            //  Effet machine à écrire : on ne dessine que les caractères déjà "révélés"
            int visibleChars = GetVisibleCharCount(_bodyText);
            string visibleText = _bodyText.Substring(0, visibleChars);

            float contentInset = 12f;
            Rectangle contentRect = new Rectangle(rightPanel.X + contentInset, rightPanel.Y + contentInset, rightPanel.Width - contentInset * 2f, rightPanel.Height - contentInset * 2f);
            float textX = contentRect.X + 8f;
            float textY = contentRect.Y + 10f;
            float textW = contentRect.Width - 16f;
            DrawWrappedText(visibleText, (int)textX, (int)textY, (int)textW, 17, Color.White);

            bool typingDone = visibleChars >= _bodyText.Length;

            if (_mode == "choice")
            {
                if (typingDone)
                {
                    Vector2 mousePos = Raylib.GetMousePosition();
                    if (_choiceHasOfferAction) DrawChoiceItem(_offerRect, Localization.Get("quest.choice.accept_offer", "Accepter sa quête"), mousePos, _iconQuestStart);
                    if (_choiceHasValidationAction) DrawChoiceItem(_validateRect, Localization.Get("quest.choice.validate", "Valider la mienne"), mousePos, _iconQuestFinish);
                    if (_choiceHasDeliverAction) DrawChoiceItem(_deliverRect, Localization.Get("quest.choice.deliver", "Livrer l'objet"), mousePos, _iconQuestFinish);
                    if (_choiceHasTrade) DrawChoiceItem(_tradeRect, Localization.Get("quest.choice.trade", "Commercer"), mousePos, _iconTrade);
                    if (_choiceHasGuildJoin) DrawChoiceItem(_guildRect, Localization.Get("quest.choice.guild_join", "Rejoindre la guilde"), mousePos, _iconGuildJoin);
                    if (_choiceHasCards) DrawChoiceItem(_cardsRect, "Ouvrir les cartes", mousePos, _iconCards);
                    if (_choiceHasSocial) DrawChoiceItem(_socialRect, "Parler un peu", mousePos, _iconLeave);
                    if (_choiceHasQuest || _choiceHasGenericQuestAction) DrawChoiceItem(_discussRect, Localization.Get("quest.choice.discuss", "Discuter de la quête"), mousePos, _iconQuestStart);
                    DrawChoiceItem(_nothingRect, Localization.Get("quest.choice.nothing", "Rien"), mousePos, _iconLeave);
                }
            }
            else if (_mode == "chat")
            {
                if (typingDone)
                {
                    Vector2 mousePos = Raylib.GetMousePosition();
                    float listTop = contentRect.Y + 12f + GetWrappedTextHeight(_bodyText, (int)textW, 17) + 12f;
                    float itemHeight = 34f;
                    float itemX = contentRect.X + 8f;
                    float itemW = contentRect.Width - 16f;
                    var topicRects = new[]
                    {
                        new Rectangle(itemX, listTop + 0f * itemHeight, itemW, itemHeight),
                        new Rectangle(itemX, listTop + 1f * itemHeight, itemW, itemHeight),
                        new Rectangle(itemX, listTop + 2f * itemHeight, itemW, itemHeight),
                        new Rectangle(itemX, listTop + 3f * itemHeight, itemW, itemHeight),
                        new Rectangle(itemX, listTop + 4f * itemHeight, itemW, itemHeight)
                    };

                    DrawChoiceItem(topicRects[0], "Le village", mousePos, _iconTrade);
                    DrawChoiceItem(topicRects[1], "Parler d'eux", mousePos, _iconQuestStart);
                    DrawChoiceItem(topicRects[2], "Les autres PNJ", mousePos, _iconCards);
                    DrawChoiceItem(topicRects[3], "Leurs envies", mousePos, _iconQuestFinish);
                    DrawChoiceItem(topicRects[4], "On se tait", mousePos, _iconLeave);
                }
            }
            else if (_mode == "chat_followup")
            {
                if (typingDone)
                {
                    Vector2 mousePos = Raylib.GetMousePosition();
                    float listTop = contentRect.Y + 12f + GetWrappedTextHeight(_bodyText, (int)textW, 17) + 12f;
                    float itemHeight = 34f;
                    float itemX = contentRect.X + 8f;
                    float itemW = contentRect.Width - 16f;
                    var followUpRects = new[]
                    {
                        new Rectangle(itemX, listTop + 0f * itemHeight, itemW, itemHeight),
                        new Rectangle(itemX, listTop + 1f * itemHeight, itemW, itemHeight)
                    };

                    DrawChoiceItem(followUpRects[0], "Continuer à parler", mousePos, _iconQuestStart);
                    DrawChoiceItem(followUpRects[1], "Retour à la conversation principale", mousePos, _iconLeave);
                }
            }
            else if (_mode == "offer")
            {
                if (typingDone)
                {
                    FontManager.DrawText(_rewardText, (int)textX, (int)(rightPanel.Y + rightPanel.Height - 74), 14, new Color(170, 215, 145, 255));
                    Vector2 mousePos = Raylib.GetMousePosition();
                    DrawQuestButton(_acceptRect, Localization.Get("quest.button.accept", "Accepter"), new Color(74, 132, 76, 255), _acceptButtonPressed, _acceptButtonTexture, _acceptButtonPressedTexture, mousePos);
                    DrawQuestButton(_declineRect, Localization.Get("quest.button.decline", "Refuser"), new Color(136, 74, 74, 255), _declineButtonPressed, _declineButtonTexture, _declineButtonPressedTexture, mousePos);
                }
                Raylib.DrawRectangleRounded(_closeRect, 0.3f, 8, new Color(90, 86, 78, 255));
                FontManager.DrawText("x", (int)_closeRect.X + 7, (int)_closeRect.Y + 1, 16, new Color(240, 240, 240, 255));
            }
            else
            {
                if (typingDone)
                {
                    Vector2 mousePos = Raylib.GetMousePosition();
                    DrawQuestButton(_closeRect, Localization.Get("quest.button.ok", "D'accord"), new Color(98, 98, 120, 255), false, new Texture2D(), new Texture2D(), mousePos);
                }
            }
        }

        private static void DrawNpcPortrait(Entity? npc, Rectangle rect)
        {
            QuestJournalUI.DrawSharedPortrait(npc, rect);
        }

        private static void DrawFriendshipBar(Rectangle rect, float friendship)
        {
            Raylib.DrawRectangleRoundedLines(rect, 0.18f, 6, 1.5f, new Color(220, 220, 220, 140));

            if (_friendshipBarShaderLoaded && _friendshipBarTexture.Id != 0)
            {
                float fillPercent = Math.Clamp(friendship, 0f, 1f);
                float[] col = { 120f / 255f, 170f / 255f, 255f / 255f };
                Raylib.SetShaderValue(_friendshipBarShader, _friendshipBarFillColorLoc, col, ShaderUniformDataType.Vec3);
                Raylib.SetShaderValue(_friendshipBarShader, _friendshipBarFillPercentLoc, fillPercent, ShaderUniformDataType.Float);

                Raylib.BeginShaderMode(_friendshipBarShader);
                Raylib.DrawTexturePro(
                    _friendshipBarTexture,
                    new Rectangle(0, 0, _friendshipBarTexture.Width, _friendshipBarTexture.Height),
                    rect,
                    Vector2.Zero,
                    0f,
                    Color.White);
                Raylib.EndShaderMode();
            }
            else
            {
                Raylib.DrawRectangleRounded(rect, 0.18f, 6, new Color(27, 27, 27, 255));
                float fillHeight = Math.Clamp(friendship, 0f, 1f) * rect.Height;
                Rectangle fillRect = new Rectangle(rect.X, rect.Y + rect.Height - fillHeight, rect.Width, fillHeight);
                Raylib.DrawRectangleRounded(fillRect, 0.18f, 6, new Color(120, 170, 255, 255));
            }
        }

        private static void DrawQuestButton(Rectangle rect, string label, Color color, bool pressed, Texture2D normalTexture, Texture2D pressedTexture, Vector2 mousePos)
        {
            Texture2D textureToDraw = pressed && HasValidTexture(pressedTexture) ? pressedTexture : normalTexture;
            if (HasValidTexture(textureToDraw))
            {
                Raylib.DrawTexturePro(textureToDraw, new Rectangle(0, 0, textureToDraw.Width, textureToDraw.Height), rect, Vector2.Zero, 0f, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(rect, 0.2f, 6, color);
            }

            bool hovered = Raylib.CheckCollisionPointRec(mousePos, rect);
            if (hovered && !pressed)
            {
                Raylib.DrawRectangleRounded(rect, 0.2f, 6, new Color(255, 255, 255, 40));
            }
        }

        private static void DrawChoiceItem(Rectangle rect, string label, Vector2 mousePos, Texture2D icon = default)
        {
            bool hovered = Raylib.CheckCollisionPointRec(mousePos, rect);
            if (hovered)
            {
                Raylib.DrawRectangleRec(rect, COLOR_CHOICE_HIGHLIGHT);
            }

            float textX = rect.X + 10;
            if (HasValidTexture(icon))
            {
                float iconSize = 22f;
                float iconX = rect.X + 10;
                float iconY = rect.Y + rect.Height / 2f - iconSize / 2f;
                Raylib.DrawTexturePro(icon, new Rectangle(0, 0, icon.Width, icon.Height),
                    new Rectangle(iconX, iconY, iconSize, iconSize), Vector2.Zero, 0f, Color.White);
                textX = iconX + iconSize + 8f;
            }

            FontManager.DrawText(label, (int)textX, (int)(rect.Y + rect.Height / 2f - 7), 16, Color.White);
            Raylib.DrawRectangleRec(new Rectangle(rect.X, rect.Y + rect.Height - 1, rect.Width, 1), COLOR_CHOICE_SEPARATOR);
        }

        private static void DrawWrappedText(string text, int x, int y, int maxWidth, int size, Color color)
        {
            var lines = GetWrappedTextLines(text, maxWidth, size);
            int lineY = y;
            foreach (var line in lines)
            {
                FontManager.DrawText(line, x, lineY, size, color);
                lineY += size + 6;
            }
        }

        private static List<string> GetWrappedTextLines(string text, int maxWidth, int size)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            string line = "";
            foreach (var word in words)
            {
                string test = line.Length == 0 ? word : line + " " + word;
                if (FontManager.MeasureText(test, size) > maxWidth && line.Length > 0)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = test;
                }
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        private static float GetWrappedTextHeight(string text, int maxWidth, int size)
        {
            var lines = GetWrappedTextLines(text, maxWidth, size);
            if (lines.Count == 0) return 0f;
            return lines.Count * size + Math.Max(0, lines.Count - 1) * 6;
        }
    }

    // ==================== JOURNAL DE QUÊTES ====================
    // Affiche la liste complète des quêtes actives et permet de les parcourir en overlay.
    public static class QuestJournalUI
    {
        private const int WINDOW_WIDTH = 720;
        private const int WINDOW_HEIGHT = 500;
        private const int HEADER_HEIGHT = 44;
        private const int ENTRY_HEIGHT = 86;

        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(180, 120);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static int _scrollOffset = 0;

        public static bool IsOpen => _isOpen;

        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        public static void Open()
        {
            if (_isOpen) return;
            _isOpen = true;
            _scrollOffset = 0;
            _isDragging = false;
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
        }

        public static void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }

        public static void Update(List<Entity> entities)
        {
            if (!_isOpen) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 80, sw - 80);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - 80);

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;

            Rectangle titleBar = new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT);
            if (Raylib.CheckCollisionPointRec(mousePos, titleBar) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }

            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePos - _dragOffset;

            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            int listX = x + 18;
            int listY = y + HEADER_HEIGHT + 14;
            int listW = WINDOW_WIDTH - 36;
            int listH = WINDOW_HEIGHT - HEADER_HEIGHT - 28;
            Rectangle listRect = new Rectangle(listX, listY, listW, listH);

            var quests = GetQuestEntries(entities);
            int totalHeight = Math.Max(0, quests.Count * ENTRY_HEIGHT);
            int maxScroll = Math.Max(0, totalHeight - listH);

            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, listRect))
            {
                _scrollOffset -= (int)(wheel * ENTRY_HEIGHT);
                _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
            }
        }

        public static void Draw(List<Entity> entities)
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            int listX = x + 18;
            int listY = y + HEADER_HEIGHT + 14;
            int listW = WINDOW_WIDTH - 36;
            int listH = WINDOW_HEIGHT - HEADER_HEIGHT - 28;

            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(24, 20, 16, 240));
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2f, new Color(210, 180, 110, 255));

            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 12, new Color(34, 28, 20, 255));
            FontManager.DrawText(Localization.Get("quest.log.title", "Journal des quêtes"), x + 18, y + 12, 20, new Color(240, 220, 150, 255));
            FontManager.DrawText(Localization.Get("quest.log.close_hint", "G/Echap pour fermer"), x + WINDOW_WIDTH - 180, y + 16, 12, new Color(180, 180, 180, 255));

            var quests = GetQuestEntries(entities);
            if (quests.Count == 0)
            {
                FontManager.DrawText(Localization.Get("quest.log.none", "Aucune quête en cours pour le moment."), x + 24, y + 90, 16, Color.LightGray);
                return;
            }

            int totalHeight = Math.Max(0, quests.Count * ENTRY_HEIGHT);
            int maxScroll = Math.Max(0, totalHeight - listH);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);

            int firstVisible = _scrollOffset / ENTRY_HEIGHT;
            int lastVisible = Math.Min(quests.Count, firstVisible + (listH / ENTRY_HEIGHT) + 2);

            for (int i = firstVisible; i < lastVisible; i++)
            {
                var entry = quests[i];
                int entryY = listY + i * ENTRY_HEIGHT - _scrollOffset;
                if (entryY + ENTRY_HEIGHT < listY || entryY > listY + listH) continue;

                Rectangle rect = new Rectangle(listX, entryY, listW - 10, ENTRY_HEIGHT - 8);
                Raylib.DrawRectangleRounded(rect, 0.06f, 8, new Color(38, 34, 28, 220));
                Raylib.DrawRectangleRoundedLines(rect, 0.06f, 8, 1.5f, new Color(120, 100, 70, 180));

                Rectangle giverPanel = new Rectangle(rect.X + 12, rect.Y + 10, 54, 54);
                Raylib.DrawRectangleRounded(giverPanel, 0.2f, 8, new Color(70, 55, 38, 240));
                Raylib.DrawRectangleRoundedLines(giverPanel, 0.2f, 8, 1f, new Color(200, 170, 110, 180));
                DrawNpcPreview(entry.GiverEntity, giverPanel);
                FontManager.DrawText(entry.GiverName, (int)giverPanel.X - 2, (int)giverPanel.Y + 64, 10, new Color(220, 220, 220, 255));

                string statusText = entry.Quest.State switch
                {
                    QuestState.Offered => Localization.Get("quest.status.offered", "Proposée"),
                    QuestState.Accepted => Localization.Get("quest.status.accepted", "En cours"),
                    QuestState.Delivered => Localization.Get("quest.status.delivered", "À valider"),
                    _ => Localization.Get("quest.status.in_progress", "En cours")
                };

                FontManager.DrawText($"{entry.GiverName} • {statusText}", (int)rect.X + 84, (int)rect.Y + 10, 16, new Color(240, 210, 130, 255));
                string objectiveText = entry.Quest.Type == QuestType.TameAndBring
                    ? string.Format(Localization.Get("quest.log.objective_tame", "Apprivoiser {0} et l'amener à {1}"), Localization.GetLocalizedSpeciesName(entry.Quest.TargetSpecies), entry.TargetName)
                    : string.Format(Localization.Get("quest.log.objective", "Apporter {0}x {1} à {2}"), entry.Quest.ItemQty, entry.Quest.GetLocalizedItemName(), entry.TargetName);
                FontManager.DrawText(objectiveText, (int)rect.X + 84, (int)rect.Y + 34, 14, Color.White);

                int rewardX = (int)rect.X + (int)rect.Width - 88;
                int rewardY = (int)rect.Y + 10;
                var rewardItem = CreateRewardItem(entry.Quest);
                Rectangle rewardRect = new Rectangle(rewardX, rewardY, 44, 44);
                Raylib.DrawRectangleRounded(rewardRect, 0.15f, 8, new Color(40, 38, 30, 220));
                if (rewardItem != null)
                {
                    ItemRenderer.DrawItemPadded(rewardItem, (int)rewardRect.X + 4, (int)rewardRect.Y + 4, 36, 2);
                }
                FontManager.DrawText($"x{entry.Quest.RewardItemQty}", (int)rewardRect.X + 48, (int)rewardRect.Y + 14, 13, new Color(150, 220, 150, 255));
                FontManager.DrawText(Localization.Get("quest.log.reward", "Récompense"), (int)rewardRect.X + 48, (int)rewardRect.Y + 30, 11, new Color(180, 180, 180, 255));
            }

            if (totalHeight > listH)
            {
                int scrollbarX = x + WINDOW_WIDTH - 20;
                int scrollbarY = listY;
                int scrollbarHeight = Math.Max(30, (int)((float)listH / totalHeight * listH));
                float scrollPercent = totalHeight == 0 ? 0f : (float)_scrollOffset / (totalHeight - listH);
                int scrollbarOffset = (int)(scrollPercent * (listH - scrollbarHeight));
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY + scrollbarOffset, 8, scrollbarHeight), 0.25f, 4, new Color(90, 80, 60, 200));
            }
        }

        private static List<(Quest Quest, Entity? GiverEntity, string GiverName, string TargetName)> GetQuestEntries(List<Entity> entities)
        {
            return QuestManager.GetQuestEntriesSnapshot(entities);
        }

        public static void DrawNpcPreview(Entity? npc, Rectangle rect)
        {
            DrawSharedPortrait(npc, rect);
        }

        public static void DrawSharedPortrait(Entity? npc, Rectangle rect)
        {
            if (npc == null) return;

            var hairBase = (npc.Species == "human" && Program.hairBaseTextures.Count > 0)
                ? Program.hairBaseTextures[Math.Clamp(npc.HairStyle, 0, Program.hairBaseTextures.Count - 1)]
                : new Texture2D();
            var hairOverlay = (npc.Species == "human" && Program.hairOverlayTextures.Count > 0)
                ? Program.hairOverlayTextures[Math.Clamp(npc.HairStyle, 0, Program.hairOverlayTextures.Count - 1)]
                : new Texture2D();
            var eyeBase = (npc.Species == "human" && Program.EyeBaseTextures.Count > 0)
                ? Program.EyeBaseTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeBaseTextures.Count - 1)]
                : new Texture2D();
            var eyeOverlay = (npc.Species == "human" && Program.EyeOverlayTextures.Count > 0)
                ? Program.EyeOverlayTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeOverlayTextures.Count - 1)]
                : new Texture2D();

            Rectangle contentRect = new Rectangle(rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12);
            float referenceBubbleSize = 128f;
            float sizeRatio = Math.Clamp(Math.Min(rect.Width, rect.Height) / referenceBubbleSize, 0.45f, 1.2f);
            float portraitScale = 1.34f * sizeRatio;
            int clipHeight = (int)(contentRect.Height * 1f);
            Vector2 previewPos = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height * 0.98f);
            Color tint = npc.Tint.R != 0 || npc.Tint.G != 0 || npc.Tint.B != 0 || npc.Tint.A != 0 ? npc.Tint : Color.White;
            Color hairColor = npc.HairColor.R != 0 || npc.HairColor.G != 0 || npc.HairColor.B != 0 || npc.HairColor.A != 0 ? npc.HairColor : Color.White;

            Raylib.BeginScissorMode((int)contentRect.X, (int)contentRect.Y, (int)contentRect.Width, clipHeight);
            EntityRenderer.DrawEntity(
                npc.Species,
                "idle",
                0,
                0f,
                1f,
                previewPos,
                tint,
                hairBase,
                hairOverlay,
                hairColor,
                SpeciesData.Skeletons,
                eyeBase,
                eyeOverlay,
                customScale: portraitScale,
                equipment: npc.Equipment,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                heldItemTexture: default,
                headAngle: 0f,
                keepItemHorizontal: false,
                isBow: false,
                underwearTexture: default,
                beardStyle: npc.BeardStyle,
                prevAnim: "",
                prevFrame: 0,
                prevProg: 0f,
                transitionWeight: 0f,
                flashWhite: false);
            Raylib.EndScissorMode();

            Raylib.DrawRectangleRec(new Rectangle(contentRect.X, contentRect.Y + clipHeight, contentRect.Width, contentRect.Height - clipHeight), new Color(24, 20, 16, 240));
        }

        private static Item? CreateRewardItem(Quest quest)
        {
            int itemId = 0;
            if (!string.IsNullOrWhiteSpace(quest.RewardItemName))
            {
                itemId = Program.GetItemId(quest.RewardItemName);
            }
            if (itemId <= 0)
            {
                itemId = quest.RewardItemId;
            }

            if (itemId > 0 && GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
            {
                return new Item(itemData.Name, quest.RewardItemQty, itemData.Color, itemData.Icon);
            }

            if (!string.IsNullOrWhiteSpace(quest.RewardItemName))
            {
                return new Item(quest.RewardItemName, quest.RewardItemQty, Color.White, new Texture2D());
            }

            return null;
        }
    }

    // ==================== TRAQUEUR DE QUÊTE ====================
    // Affiche assets/gui/tracker.png au-dessus du PNJ avec qui le joueur doit interagir
    // pour sa quête en cours. Si ce PNJ est hors-écran, la flèche reste collée au bord
    // de l'écran et pivote pour pointer dans sa direction.
    public static class QuestTrackerUI
    {
        private static Texture2D _trackerTex;
        private static bool _loaded = false;

        private const float EDGE_MARGIN = 48f;      // distance minimale par rapport au bord de l'écran
        private const float ABOVE_HEAD_OFFSET = 130f; // hauteur au-dessus du PNJ quand il est visible
        private const float ICON_SIZE = 34f;

        public static Texture2D GetTrackerTexture()
        {
            EnsureLoaded();
            return _trackerTex;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _trackerTex = Raylib.LoadTexture("assets/gui/tracker.png");
            _loaded = true;
        }

        // Trouve, pour chaque quête acceptée, le PNJ que le joueur doit encore rejoindre (la cible de livraison).
        private static IEnumerable<(Vector2 WorldPos, bool IsLoaded)> GetTrackedTargets(List<Entity> entities)
        {
            return QuestManager.GetTrackedTargetsSnapshot(entities);
        }

        public static void Draw(Camera2D camera, List<Entity> entities)
        {
            EnsureLoaded();
            if (_trackerTex.Id == 0) return;

            int screenW = Raylib.GetScreenWidth();
            int screenH = Raylib.GetScreenHeight();
            Vector2 center = new Vector2(screenW / 2f, screenH / 2f);
            float halfW = screenW / 2f - EDGE_MARGIN;
            float halfH = screenH / 2f - EDGE_MARGIN;

            foreach (var trackedTarget in GetTrackedTargets(entities))
            {
                Vector2 targetScreenPos = Raylib.GetWorldToScreen2D(trackedTarget.WorldPos, camera);

                bool onScreen = targetScreenPos.X >= 0 && targetScreenPos.X <= screenW
                              && targetScreenPos.Y >= 0 && targetScreenPos.Y <= screenH;

                Vector2 drawPos;
                float rotationDeg;

                if (onScreen)
                {
                    // Le PNJ est visible : on laisse l'icône de traque se dessiner avec les
                    // autres indicateurs au-dessus de sa tête au lieu d'afficher une flèche
                    // distincte et indépendante.
                    continue;
                }
                else
                {
                    // Le PNJ est hors-écran : on accroche la flèche au bord de l'écran, dans sa direction
                    Vector2 dir = targetScreenPos - center;
                    if (dir.X == 0 && dir.Y == 0) dir = new Vector2(0, 1);

                    float tx = dir.X != 0 ? halfW / MathF.Abs(dir.X) : float.MaxValue;
                    float ty = dir.Y != 0 ? halfH / MathF.Abs(dir.Y) : float.MaxValue;
                    float t = MathF.Min(tx, ty);

                    drawPos = center + dir * t;

                    // La texture pointe naturellement vers le bas (0,1) au repos.
                    float angleToTarget = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
                    rotationDeg = angleToTarget - 90f;
                }

                Rectangle src = new Rectangle(0, 0, _trackerTex.Width, _trackerTex.Height);
                Rectangle dest = new Rectangle(drawPos.X, drawPos.Y, ICON_SIZE, ICON_SIZE);
                Vector2 origin = new Vector2(ICON_SIZE / 2f, ICON_SIZE / 2f);

                Raylib.DrawTexturePro(_trackerTex, src, dest, origin, rotationDeg, Color.White);
            }
        }
    }
}
