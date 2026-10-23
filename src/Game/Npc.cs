// Npc.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using static Soulfract.WorldGeneration;

namespace Soulfract
{
    public static partial class Program
    {

		private static Entity? _lastPlayerDialogNpc = null;

		// Détecte le PNJ actuellement en dialogue de quête (QuestDialogUI) ou en commerce (TraderUI) avec
		// le joueur, le fait se tourner vers lui (Facing) et le marque comme "en train de parler" pour que
		// UpdateGaze (Entity.cs) le fasse regarder le joueur plutôt que sa cible habituelle.
		private static void UpdateNpcPlayerDialogGaze(Vector2 playerPos)
		{
			Entity? currentDialogNpc = null;
			if (QuestDialogUI.IsOpen)
				currentDialogNpc = QuestDialogUI.GetCurrentNpc();
			else if (TraderUI.IsOpen)
				currentDialogNpc = TraderUI.GetCurrentTrader();

			// Le PNJ qui était en dialogue la frame précédente mais ne l'est plus : on le libère
			// (il peut de nouveau bouger et reprendre son IA normale).
			if (_lastPlayerDialogNpc != null && _lastPlayerDialogNpc != currentDialogNpc)
			{
				_lastPlayerDialogNpc.IsTalking = false;
				_lastPlayerDialogNpc.IsInPlayerDialogue = false;
				_lastPlayerDialogNpc.ResumeMovementAfterDialogue();
			}

			if (currentDialogNpc != null)
			{
				currentDialogNpc.IsTalking = true;
				currentDialogNpc.IsInPlayerDialogue = true; //  fige son déplacement tant que le dialogue est ouvert
				currentDialogNpc.CurrentTalkPartner = null; // null = interlocuteur == joueur (voir Entity.UpdateGaze)

				// Se tourner physiquement vers le joueur (gauche/droite)
				float dirX = playerPos.X - currentDialogNpc.WorldPos.X;
				if (Math.Abs(dirX) > 1f)
					currentDialogNpc.Facing = Math.Sign(dirX);
			}

			_lastPlayerDialogNpc = currentDialogNpc;
		}

		//  SYSTÈME DE QUÊTES : trouve un PNJ villageois sous la souris qui a une interaction de quête à proposer
		//  MULTIJOUEUR : source des PNJ pour tout le système de quêtes/commerce. Sur l'hôte
		// (ou en solo), on utilise directement Program.entities (les vraies Entity simulées).
		// Sur un client, Program.entities est vide (seul l'hôte simule les PNJ) : on utilise
		// à la place des PNJ "fantômes" reconstruits à partir des derniers snapshots réseau
		// reçus (voir NetworkManager.GetVillagerProxies). Ne JAMAIS muter ces PNJ fantômes
		// pour faire progresser une quête : toute action doit passer par
		// NetworkManager.RequestQuestAction, qui la fait valider par l'hôte.
		private static List<Entity> GetQuestNpcs()
		{
			return NetworkManager.IsClient ? NetworkManager.GetVillagerProxies() : entities;
		}

		private static List<Entity> GetMusicBipeds()
		{
			return NetworkManager.IsClient ? NetworkManager.GetMusicBipedProxies() : entities;
		}

		public static bool IsMusicBandMember(Entity entity)
		{
			if (!entity.IsAlive || SpeciesData.GetSpeciesInfo(entity.Species)?.Type != "biped")
				return false;

			if (entity.IsGuildMember)
				return true;

			return entity.IsTamed && !entity.IsHostilePet && entity.OwnerNpcId == null;
		}

		private static Entity? FindMusicBipedUnderMouse(Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			float bestDistSq = 50f * 50f;
			Entity? closest = null;

			foreach (var entity in GetMusicBipeds())
			{
				if (!IsMusicBandMember(entity))
					continue;

				Vector2 entityVisualPos = entity.VisualWorldPos;
				Vector2 visualPos = entityVisualPos;
				float distSq = Vector2.DistanceSquared(mouseWorld, visualPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = entity;
				}
			}

			return closest;
		}

		private static bool TryInteractWithMusicBiped(Camera2D camera)
		{
			if (PlayerCurrentCar != null)
				return false;

			Item? offeredItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
			if (offeredItem == null)
				return false;
			int instrumentId = GetItemId(offeredItem.Name);
			if (!GameData.ItemDatabase.TryGetValue(instrumentId, out var itemData) || !itemData.IsInstrument)
				return false;

			Entity? target = FindMusicBipedUnderMouse(camera);
			if (target == null)
				return false;

			float interactionRange = Math.Max(TileSize * 5f, 160f);
			if (Vector2.DistanceSquared(GetPlayerPosition(), target.WorldPos) > interactionRange * interactionRange)
			{
				AddNotification(new Notification("Rapprochez-vous du bipède pour lui donner un instrument.", Color.Orange, 2f));
				return true;
			}

			NetworkManager.RequestInstrumentExchange(target.NetId, instrumentId, offeredItem);
			return true;
		}

		public static bool TryExchangeInstrument(Entity target, Item instrument, out string message)
		{
			message = "L'échange d'instrument a échoué.";
			int instrumentId = GetItemId(instrument.Name);
			if (!IsMusicBandMember(target) || target.Equipment == null ||
				!GameData.ItemDatabase.TryGetValue(instrumentId, out var instrumentData) ||
				!instrumentData.IsInstrument || !instrumentData.HasEquipSlot ||
				instrumentData.EquipSlot != EquipmentSlot.MainHand)
				return false;

			Item? previousItem = target.Equipment.MainHand;
			if (previousItem != null && GetItemId(previousItem.Name) == instrumentId)
			{
				message = $"{target.DisplayName} possède déjà cet instrument.";
				return false;
			}

			InventorySlot? previousItemSlot = previousItem == null
				? null
				: target.Inventory.Slots.FirstOrDefault(slot => !slot.IsEmpty && ReferenceEquals(slot.Item, previousItem));
			List<InventorySlot> freeSlots = target.Inventory.Slots.Where(slot => slot.IsEmpty).ToList();
			InventorySlot? previousItemDestination = previousItem != null && previousItemSlot == null
				? freeSlots.FirstOrDefault()
				: null;
			InventorySlot? instrumentDestination = freeSlots.FirstOrDefault(slot => slot != previousItemDestination);
			if (instrumentDestination == null || (previousItem != null && previousItemDestination == null && previousItemSlot == null))
			{
				message = $"L'inventaire de {target.DisplayName} n'a pas assez de place pour cet instrument.";
				return false;
			}

			if (previousItemDestination != null)
			{
				previousItemDestination.Item = previousItem;
				previousItemDestination.Count = 1;
			}

			instrument.Count = 1;
			instrumentDestination.Item = instrument;
			instrumentDestination.Count = 1;

			foreach (var key in target.Equipment.EquippedItems.Keys
				.Where(key => key.slot == EquipmentSlot.MainHand).ToList())
				target.Equipment.EquippedItems.Remove(key);

			target.Equipment.MainHand = null;
			target.Equipment.MainHandContainer = null;
			if (!target.Equipment.EquipItem(instrument, EquipmentSlot.MainHand))
			{
				instrumentDestination.Clear();
				if (previousItemDestination != null)
					previousItemDestination.Clear();
				if (previousItem != null)
				{
					target.Equipment.MainHand = previousItem;
					target.Equipment.EquippedItems[(EquipmentSlot.MainHand, 0)] = previousItem;
				}
				message = "Cet instrument ne peut pas être équipé dans la main droite.";
				return false;
			}

			target.Equipment.LoadEquipmentTextures();
			message = previousItem != null
				? $"Instrument donné à {target.DisplayName} et rangé dans son inventaire. Son objet précédent y a aussi été rangé."
				: $"{target.DisplayName} a rejoint votre groupe musical.";
			return true;
		}

		public static Item? CreateInstrumentFromGrant(ItemGrantMsg grant)
		{
			if (!GameData.ItemDatabase.TryGetValue(grant.ItemId, out var itemData) || !itemData.IsInstrument)
				return null;

			var item = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
			if (grant.Colors != null && grant.Colors.Count > 0)
				item.CustomColors = grant.Colors.Select(color => color != null
					? (Color?)new Color(color.R, color.G, color.B, color.A)
					: null).ToList();
			item.RestoreMetadataFromSave(grant.Metadata ?? "");
			if (grant.Meta != null)
				foreach (var pair in grant.Meta)
					item.Meta[pair.Key] = pair.Value;
			item.Container = grant.PortableContainer != null
				? RestorePortableContainer(grant.PortableContainer)
				: null;
			item.Backpack = grant.Backpack != null
				? RestoreBackpack(grant.Backpack)
				: null;
			return item;
		}

		public static List<MusicPerformer> GetMusicBandPerformers(int additionalPlayerId, int additionalInstrumentId)
		{
			var performers = new List<MusicPerformer>();
			var includedPlayers = new HashSet<int>();

			void AddPlayerInstrument(int playerId, int itemId)
			{
				if (itemId <= 0 || !includedPlayers.Add(playerId) ||
					!GameData.ItemDatabase.TryGetValue(itemId, out var itemData) || !itemData.IsInstrument)
					return;

				performers.Add(new MusicPerformer
				{
					Program = MusicPlayer.GetInstrumentProgram(itemData.Name),
					PlayerId = playerId,
					IsPercussion = MusicPlayer.IsPercussionInstrument(itemData.Name)
				});
			}

			AddPlayerInstrument(NetworkManager.LocalConnectionId, GetMusicInstrumentId(equipment));
			AddPlayerInstrument(additionalPlayerId, additionalInstrumentId);

			foreach (var entity in GetMusicBipeds())
			{
				if (!IsMusicBandMember(entity) || entity.Equipment == null)
					continue;

				int instrumentId = GetMusicInstrumentId(entity.Equipment);
				if (instrumentId > 0 && GameData.ItemDatabase.TryGetValue(instrumentId, out var itemData))
					performers.Add(new MusicPerformer
					{
						Program = MusicPlayer.GetInstrumentProgram(itemData.Name),
						EntityNetId = entity.NetId.ToString(),
						IsPercussion = MusicPlayer.IsPercussionInstrument(itemData.Name)
					});
			}

			return performers.Take(15).ToList();
		}

		private static int GetMusicInstrumentId(Equipment? entityEquipment)
		{
			if (entityEquipment == null)
				return 0;

			foreach (var item in new[]
			{
				entityEquipment.MainHand,
				entityEquipment.GetItemInSlot(EquipmentSlot.Instrument),
				entityEquipment.OffHand
			})
			{
				if (item == null)
					continue;
				int itemId = GetItemId(item.Name);
				if (GameData.ItemDatabase.TryGetValue(itemId, out var itemData) && itemData.IsInstrument)
					return itemId;
			}

			return 0;
		}

		public static void EmitMusicNoteParticle(MusicPerformer performer, int pitch, int velocity, float duration)
		{
			Vector2 origin;
			if (performer.PlayerId >= 0)
			{
				if (!NetworkManager.TryGetMusicPlayerPosition(performer.PlayerId, out origin))
					return;
				origin.Y -= 24f;
			}
			else if (Guid.TryParse(performer.EntityNetId, out var entityNetId))
			{
				if (!NetworkManager.TryGetMusicEntityPosition(entityNetId, out origin))
					return;
				origin.Y -= 28f;
			}
			else
			{
				return;
			}

			float sideOffset = (float)(Random.Shared.NextDouble() * 12f - 6f);
			float riseSpeed = 24f + Math.Clamp(pitch - 36, 0, 48) * 1.2f;
			float horizontalSpeed = sideOffset * 1.8f + (float)(Random.Shared.NextDouble() * 8f - 4f);
			float durationFactor = Math.Clamp(duration, 0.1f, 2.5f);
			float size = 7f + Math.Clamp(durationFactor * 2f, 0f, 5f) + velocity / 100f;
			float lifetime = Math.Clamp(0.55f + durationFactor * 0.38f, 0.6f, 1.6f);

			GetParticleList().Add(new MusicNoteParticle(
				origin + new Vector2(sideOffset, 0),
				new Vector2(horizontalSpeed, -riseSpeed),
				pitch,
				size,
				lifetime));
		}

		public static void ApplyInstrumentExchangeResult(int offeredInstrumentId)
		{
			if (GameData.ItemDatabase.TryGetValue(offeredInstrumentId, out var offeredItem))
				RemoveItemFromInventory(offeredItem.Name, 1);
		}

		//  SYSTÈME DE QUÊTES (TameAndBring) : animal proposé au don, en attente de la réponse
		// du joueur dans la bulle de confirmation ouverte par TryDeliverQuestWithNpc.
		private static Entity? _pendingGiftAnimal = null;

		private static Entity? FindQuestNpcUnderMouse(Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			float bestDistSq = 50f * 50f;
			Entity? closest = null;

			foreach (var entity in GetQuestNpcs())
			{
				if (!entity.IsAlive) continue;
				if (!entity.IsVillager) continue;
				//  Un membre de guilde est marqué IsTamed (comportement de compagnon), mais il doit
				// rester interactible pour les quêtes/commerce comme n'importe quel autre villageois.
				if (entity.IsTamed && !entity.IsGuildMember) continue;

				Vector2 entityVisualPos = entity.VisualWorldPos;
				Vector2 visualPos = entityVisualPos;

				float distSq = Vector2.DistanceSquared(mouseWorld, visualPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = entity;
				}
			}
			return closest;
		}

		//  Indique si ce PNJ propose du commerce (a un métier, ou est marqué marchand).
		private static bool HasTradeAvailable(Entity npc)
		{
			// Un membre de guilde (IsTamed) garde son commerce, contrairement à un animal apprivoisé classique.
			return (npc.IsTrader || npc.HasProfession) && (!npc.IsTamed || npc.IsGuildMember);
		}

		//  Point d'entrée unifié : ouvre la bulle de choix (Discuter de la quête / Commercer / Rien)
		// si le PNJ a plusieurs interactions possibles, ou ouvre directement la seule interaction
		// disponible s'il n'y en a qu'une (ex : un simple marchand sans quête).
		private static bool TryOpenVillagerDialog(Entity npc)
		{
			//  Le Veilleur du campement de départ a sa propre conversation en plusieurs pages
			// (voir Program_StoryGuide.cs) ; les autres PNJ passent par la bulle de choix habituelle.
			if (TryStartStoryGuideDialog(npc))
				return true;

			return OpenVillagerChoiceDialog(npc);
		}

		private static bool OpenVillagerChoiceDialog(Entity npc)
		{
			npc.WakeFromInteraction();

			bool hasOfferAction = npc.ActiveQuest != null && npc.ActiveQuest.State == QuestState.Offered;
			var deliveryTarget = QuestManager.GetQuestToDeliverFor(npc, GetQuestNpcs());
			var incoming = QuestManager.GetIncomingQuestFor(npc, GetQuestNpcs());
			bool hasValidationAction = incoming != null;
			bool hasDeliverAction = deliveryTarget != null;
			bool hasTrade = HasTradeAvailable(npc);
			bool hasGuildJoin = GuildRecruitManager.CanAccept(npc);
			bool hasCardDeck = GetItemCountInInventory(GameData.GetItemId("card_deck")) > 0;
			bool hasCards = hasCardDeck && npc.IsVillager && (!npc.IsTamed || npc.IsGuildMember);
			bool hasQuestInteraction = hasOfferAction || hasDeliverAction || hasValidationAction;

			//  On propose TOUJOURS toutes les interactions disponibles en même temps (offre de sa
			// propre quête, validation, livraison d'un objet, commerce, rejoindre la guilde) plutôt
			// que de n'en choisir qu'une seule automatiquement : un PNJ qui a sa propre quête ET qui
			// attend la livraison d'un objet pour une autre quête doit laisser le joueur décider.
			bool hasConversation = npc.IsVillager && !npc.IsTamed;
			if (hasQuestInteraction || hasTrade || hasGuildJoin || hasCards || hasConversation)
			{
				// On propose toujours la bulle de choix, même si "Commercer" est la seule option :
				// le joueur doit pouvoir voir la page de conversation avant d'entrer dans le commerce.
				QuestDialogUI.OpenChoice(npc, hasOfferAction: hasOfferAction, hasValidationAction: hasValidationAction, hasDeliverAction: hasDeliverAction, hasTrade: hasTrade, hasGuildJoin: hasGuildJoin, hasCards: hasCards, hasSocial: hasConversation);
			}
			else
			{
				QuestDialogUI.OpenReminder(npc, "Bonjour ! Je n'ai rien à te proposer pour l'instant.");
			}
			return true;
		}

		//  SYSTÈME DE QUÊTES : traite l'interaction avec un PNJ de quête donné (souris OU touche E)
		private static bool HandleQuestNpcInteraction(Entity npc)
		{
			QuestManager.EnsureTargetIsPresent(npc, GetQuestNpcs());

			// Cas 1 : ce PNJ propose une quête -> ouvrir la fenêtre d'offre
			if (npc.ActiveQuest != null && npc.ActiveQuest.State == QuestState.Offered)
			{
				var target = QuestManager.FindEntityByNetId(GetQuestNpcs(), npc.ActiveQuest.TargetId);
                QuestDialogUI.OpenOffer(npc, target);
                return true;
			}

			if (TryDeliverQuestWithNpc(npc)) return true;

			// Cas 3 : ce PNJ est le donneur d'une quête livrée -> validation finale
			var incomingQuest = QuestManager.GetIncomingQuestFor(npc, GetQuestNpcs());
			if (incomingQuest != null)
			{
				if (TryValidateQuestWithNpc(npc)) return true;
			}

			return false;
		}

		//  SYSTÈME DE QUÊTES (TameAndBring) : cherche, parmi les animaux apprivoisés du joueur
		// à proximité du PNJ cible, un individu de l'espèce demandée. S'il y en a un, ouvre la
		// bulle "Veux-tu l'offrir ?" au lieu de finaliser directement (contrairement à la livraison
		// d'objet, le don est une action volontaire et irréversible pour un compagnon du joueur).
		private static bool TryDeliverTameQuestWithNpc(Entity npc, Quest tameQuest)
		{
			var eligibleAnimal = QuestManager.FindEligibleTamedAnimalNearby(npc, entities, _currentSaveName);
			if (eligibleAnimal == null)
			{
				QuestDialogUI.OpenReminder(npc, tameQuest.GetReminderLine(npc.DisplayName ?? "ce PNJ"));
				return true;
			}

			_pendingGiftAnimal = eligibleAnimal;
			QuestDialogUI.OpenGiftConfirm(npc, tameQuest.GetGiftOfferLine(npc.DisplayName ?? "ce PNJ"));
			return true;
		}

		//  SYSTÈME DE QUÊTES : gère le clic SOURIS du joueur sur un PNJ concerné par une quête.
		// Passe par le point d'entrée unifié (bulle de choix) plutôt que d'ouvrir la quête directement,
		// pour rester cohérent avec l'interaction à la touche E en proximité.
		private static bool TryInteractWithQuestNpc(Camera2D camera)
		{
			Entity? npc = FindQuestNpcUnderMouse(camera);
			if (npc == null) return false;
			if (CartesGameUI.IsOpen)
			{
				return CartesGameUI.TryInviteNpc(npc);
			}
			return TryOpenVillagerDialog(npc);
		}

		private static Entity? FindTraderUnderMouse(Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			float bestDistSq = 50f * 50f;
			Entity? closest = null;
			
			foreach (var entity in GetQuestNpcs())
			{
				if (!entity.IsAlive) continue;
				if (entity.Species != "human") continue;
				if (!entity.IsTrader && !entity.HasProfession) continue;
				if (entity.IsTamed && !entity.IsGuildMember) continue;

				// Position visuelle de l'entité
				Vector2 visualPos = entity.WorldPos;
				
				float distSq = Vector2.DistanceSquared(mouseWorld, visualPos);
				
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = entity;
				}
			}
			
			return closest;
		}


    }
}
