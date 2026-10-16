// Program.Npc.cs
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
				int tileX = (int)(entityVisualPos.X / TileSize);
				int tileY = (int)(entityVisualPos.Y / TileSize);
				int height = World.GetHeightAt(tileX, tileY);
				Vector2 visualPos = new(entityVisualPos.X, entityVisualPos.Y - height * TileSize / 4f);

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
				int tileX = (int)(entity.WorldPos.X / TileSize);
				int tileY = (int)(entity.WorldPos.Y / TileSize);
				int height = World.GetHeightAt(tileX, tileY);
				float yOffset = -height * TileSize / 4;
				Vector2 visualPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + yOffset);
				
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
