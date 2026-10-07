// Program.Npc.cs
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {

		// seuil pour lancer
		
		public static Texture2D _questionIcon;

		//  Avance la séquence d'écran de mort. Appelée depuis la boucle de mise à jour tant
        // que IsPlayerRagdolled est vrai. `skipRequested` fait sauter directement à l'état
        // final (crâne posé, texte complet, bouton prêt) si le joueur clique pendant l'anim.
        static void UpdateDeathScreen(float dt, bool skipRequested)
        {
            if (skipRequested && _deathScreenPhase != DeathScreenPhase.ButtonReady)
            {
                _deathScreenPhase = DeathScreenPhase.ButtonReady;
                _deathMessageCharsShown = _deathMessage.Length;
                return;
            }

            switch (_deathScreenPhase)
            {
                case DeathScreenPhase.Vignette:
                    _deathScreenPhaseTimer += dt;
                    if (_deathScreenPhaseTimer >= VIGNETTE_DURATION)
                    {
                        _deathScreenPhase = DeathScreenPhase.SkullPop;
                        _deathScreenPhaseTimer = 0f;
                    }
                    break;
                case DeathScreenPhase.SkullPop:
                    _deathScreenPhaseTimer += dt;
                    if (_deathScreenPhaseTimer >= SKULL_POP_DURATION)
                    {
                        _deathScreenPhase = DeathScreenPhase.TypingMessage;
                        _deathScreenPhaseTimer = 0f;
                    }
                    break;
                case DeathScreenPhase.TypingMessage:
                    _deathMessageCharTimer += dt;
                    while (_deathMessageCharTimer >= CHAR_REVEAL_INTERVAL && _deathMessageCharsShown < _deathMessage.Length)
                    {
                        _deathMessageCharTimer -= CHAR_REVEAL_INTERVAL;
                        _deathMessageCharsShown++;
                    }
                    if (_deathMessageCharsShown >= _deathMessage.Length)
                    {
                        _deathScreenPhase = DeathScreenPhase.WaitingForButton;
                        _deathScreenPhaseTimer = 0f;
                    }
                    break;
                case DeathScreenPhase.WaitingForButton:
                    _deathScreenPhaseTimer += dt;
                    if (_deathScreenPhaseTimer >= DELAY_BEFORE_BUTTON)
                        _deathScreenPhase = DeathScreenPhase.ButtonReady;
                    break;
                case DeathScreenPhase.ButtonReady:
                    break;
            }
        }

		//  Bouche animée : quand ce PNJ (humain) est en train de parler (IsTalking), alterne
		// rapidement entre Humanmouth (fermée) et Humanmouth_open (ouverte) pour donner
		// l'impression qu'il articule, à la place de l'ancienne bulle de dialogue. Le déphasage
		// par NetId évite que tous les PNJ qui parlent en même temps articulent en synchronisation
		// parfaite.
		private const double MOUTH_FLAP_SPEED = 9.0;

		public static void RequestImmediateCameraCenter() => _cameraNeedsImmediateCenter = true;

		//  Repose (côté client) la créature actuellement portée à la position indiquée, si le
		// sol le permet localement (les chunks/objets sont synchronisés côté client, donc cette
		// vérification préalable est fiable) ; le host revalide de toute façon avant d'accepter.
		public static bool TryDropCreatureClient(Vector2 targetPos)
		{
			if (_clientCarriedNetId == null) return false;
			if (!Guid.TryParse(_clientCarriedNetId, out var netId)) return false;
			if (World.IsCollidingEntity(targetPos, destroyedObjects, _clientCarriedSpecies))
			{
				activeNotifications.Add(new Notification("Impossible de reposer la créature ici !", new Color(255, 100, 100, 255), 1.5f));
				return false;
			}
			NetworkManager.RequestDropCreature(netId, targetPos);
			_clientCarriedNetId = null;
			_clientCarriedSpecies = "";
			activeNotifications.Add(new Notification("Créature reposée", new Color(0, 255, 200, 255), 1.5f));
			return true;
		}

		private const float TAMED_ANIMAL_INTERACTION_RANGE = 120f;

		private static Entity? FindNearbyTamedAnimal()
		{
			if (NetworkManager.IsClient)
			{
				EntityDto? closestSlime = null;
				float closestSlimeDistanceSq = TAMED_ANIMAL_INTERACTION_RANGE * TAMED_ANIMAL_INTERACTION_RANGE;
				foreach (var dto in NetworkManager.GetRemoteEntities())
				{
					if (!dto.IsTamed || dto.IsHostilePet || dto.IsCarried
						|| !string.Equals(dto.Species, "slime", StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(dto.OwnerName, LocalPlayerName, StringComparison.OrdinalIgnoreCase)) continue;
					float distanceSq = Vector2.DistanceSquared(_playerPos, new Vector2(dto.PosX, dto.PosY));
					if (distanceSq >= closestSlimeDistanceSq) continue;
					closestSlimeDistanceSq = distanceSq;
					closestSlime = dto;
				}
				return closestSlime != null ? CreateSlimeProxyFromSnapshot(closestSlime) : null;
			}

			float bestDistSq = TAMED_ANIMAL_INTERACTION_RANGE * TAMED_ANIMAL_INTERACTION_RANGE;
			Entity? closest = null;
			
			foreach (var entity in entities)
			{
				if (!entity.IsAlive || !entity.IsTamed) continue;
				//  Un animal offert à un PNJ (quête TameAndBring) reste IsTamed=true mais son
				// OwnerName n'est plus celui du joueur : il ne doit pas apparaître ici.
				if (!string.Equals(entity.OwnerName, _currentSaveName, StringComparison.Ordinal)) continue;
				
				float distSq = Vector2.DistanceSquared(_playerPos, entity.WorldPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = entity;
				}
			}
			return closest;
		}

		private static Entity? _lastPlayerDialogNpc = null;
		private static string? _pendingPouilleuxInvitationId;
		private static string _pendingPouilleuxInviterName = "Joueur";
		private static float _pendingPouilleuxInvitationTime;
		private static Rectangle _acceptPouilleuxInvitationRect;
		private static Rectangle _declinePouilleuxInvitationRect;

		public static void ReceivePouilleuxInvitation(PouilleuxInvitationMsg invitation)
		{
			_pendingPouilleuxInvitationId = invitation.InvitationId;
			_pendingPouilleuxInviterName = string.IsNullOrWhiteSpace(invitation.InviterName) ? "Joueur" : invitation.InviterName;
			_pendingPouilleuxInvitationTime = 12f;
			AddNotification(new Notification(
				$"{_pendingPouilleuxInviterName} vous propose d'ouvrir les cartes. Y accepter / N refuser",
				new Color(120, 200, 255, 255),
				12f));
		}

		public static bool HasPendingPouilleuxInvitation => _pendingPouilleuxInvitationId != null;

		public static void UpdatePouilleuxInvitation(float dt)
		{
			if (_pendingPouilleuxInvitationId == null) return;
			_pendingPouilleuxInvitationTime -= dt;
			if (_pendingPouilleuxInvitationTime <= 0f)
			{
				RespondToPouilleuxInvitation(false);
				return;
			}

			if (Raylib.IsKeyPressed(KeyboardKey.Y))
				RespondToPouilleuxInvitation(true);
			else if (Raylib.IsKeyPressed(KeyboardKey.N) || Raylib.IsKeyPressed(KeyboardKey.Escape))
				RespondToPouilleuxInvitation(false);
			else if (Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				Vector2 mouse = Raylib.GetMousePosition();
				if (Raylib.CheckCollisionPointRec(mouse, _acceptPouilleuxInvitationRect))
					RespondToPouilleuxInvitation(true);
				else if (Raylib.CheckCollisionPointRec(mouse, _declinePouilleuxInvitationRect))
					RespondToPouilleuxInvitation(false);
			}
		}

		public static void DrawPouilleuxInvitation()
		{
			if (_pendingPouilleuxInvitationId == null) return;
			int sw = Raylib.GetScreenWidth();
			const int width = 390;
			const int height = 142;
			int x = sw - width - 24;
			int y = 122;
			Rectangle panel = new(x, y, width, height);
			Raylib.DrawRectangleRounded(panel, 0.08f, 8, new Color(24, 30, 42, 245));
			Raylib.DrawRectangleRoundedLines(panel, 0.08f, 8, 2, new Color(100, 190, 255, 240));
			FontManager.DrawText("Invitation Cartes", x + 18, y + 14, 21, new Color(130, 210, 255, 255));
			FontManager.DrawText($"{_pendingPouilleuxInviterName} veut jouer avec vous.", x + 18, y + 46, 16, Color.White);
			FontManager.DrawText($"Expire dans {(int)MathF.Ceiling(_pendingPouilleuxInvitationTime)} s", x + 18, y + 70, 13, new Color(180, 190, 205, 255));

			_acceptPouilleuxInvitationRect = new Rectangle(x + 18, y + 101, 150, 30);
			_declinePouilleuxInvitationRect = new Rectangle(x + 184, y + 101, 150, 30);
			bool acceptHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _acceptPouilleuxInvitationRect);
			bool declineHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _declinePouilleuxInvitationRect);
			UIManager.DrawButton(_acceptPouilleuxInvitationRect, new Color(50, 125, 88, 255), acceptHover);
			UIManager.DrawButton(_declinePouilleuxInvitationRect, new Color(125, 65, 65, 255), declineHover);
			FontManager.DrawText("Accepter (Y)", x + 43, y + 108, 14, Color.White);
			FontManager.DrawText("Refuser (N)", x + 211, y + 108, 14, Color.White);
		}

		public static void RespondToPouilleuxInvitation(bool accepted)
		{
			if (_pendingPouilleuxInvitationId == null) return;
			string invitationId = _pendingPouilleuxInvitationId;
			_pendingPouilleuxInvitationId = null;
			NetworkManager.RespondToPouilleuxInvitation(invitationId, accepted);
			AddNotification(new Notification(
				accepted ? "Invitation Cartes acceptée." : "Invitation Cartes refusée.",
				accepted ? new Color(120, 230, 150, 255) : new Color(220, 150, 150, 255),
				2.5f));
		}

		public static void ReceivePouilleuxStart(PouilleuxStartMsg start)
		{
			Entity? remoteParticipant = CreatePouilleuxRemoteParticipant(start.OtherConnectionId, start.OtherPlayerName);
			if (remoteParticipant == null)
			{
				AddNotification(new Notification("Impossible de rejoindre la partie de cartes.", new Color(230, 130, 130, 255), 3f));
				return;
			}

			SetPlayerPosition(remoteParticipant.WorldPos + new Vector2(140f, 0f));
			CartesGameUI.OpenNetwork(remoteParticipant, start.SessionId, start.OtherConnectionId, start.HostConnectionId);
		}

		private static Entity? CreatePouilleuxRemoteParticipant(int connectionId, string displayName)
		{
			Program.LocalPlayer? player = connectionId == NetworkManager.LocalConnectionId
				? null
				: NetworkManager.GetRemotePlayersAsLocalPlayers().FirstOrDefault(candidate => candidate.Id == 1000 + connectionId);
			Vector2 position;
			string species;
			Color tint;
			Color hairColor;
			int hairStyle;
			int beardStyle;

			if (connectionId == 0)
			{
				position = GetPlayerPosition();
				species = "human";
				tint = SkinColor;
				hairColor = HairColor;
				hairStyle = PlayerHairStyle;
				beardStyle = PlayerBeardStyle;
			}
			else if (player != null)
			{
				position = player.Position;
				species = player.SpeciesOverride ?? "human";
				tint = player.MorphTintOverride ?? Color.White;
				hairColor = player.HairColorOverride ?? Color.White;
				hairStyle = player.HairStyleOverride ?? 0;
				beardStyle = player.BeardStyleOverride ?? 0;
			}
			else
			{
				return null;
			}

			Entity proxy = new(position, species);
			proxy.FirstName = string.IsNullOrWhiteSpace(displayName) ? "Joueur" : displayName;
			proxy.Tint = tint;
			proxy.HairColor = hairColor;
			proxy.HairStyle = hairStyle;
			proxy.BeardStyle = beardStyle;
			proxy.Facing = 1f;
			return proxy;
		}

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

		private static readonly Dictionary<Guid, List<Entity>> _npcPetGroupBuffer = new();

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

		private static int? FindRemotePlayerConnectionUnderMouse(Camera2D camera)
		{
			if (!NetworkManager.IsOnline) return null;
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			float bestDistSq = 55f * 55f;
			int? closest = null;
			foreach (Program.LocalPlayer player in NetworkManager.GetRemotePlayersAsLocalPlayers())
			{
				if (!player.Connected || !player.IsNetworkPlayer) continue;
				float distSq = Vector2.DistanceSquared(mouseWorld, player.Position);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = player.Id - 1000;
				}
			}
			return closest;
		}

		public static bool TryInviteRemotePlayerUnderMouse(Camera2D camera)
		{
			if (!CartesGameUI.IsOpen) return false;
			int? connectionId = FindRemotePlayerConnectionUnderMouse(camera);
			if (!connectionId.HasValue) return false;
			NetworkManager.RequestPouilleuxInvitation(connectionId.Value);
			AddNotification(new Notification("Invitation Cartes envoyée.", new Color(120, 200, 255, 255), 2.5f));
			return true;
		}

		//  Indique si ce PNJ a une interaction de quête disponible en ce moment (offre, livraison ou validation),
		// indépendamment de son métier. Utilisé partout pour décider si le PNJ est "cliquable" pour une quête.
		private static bool HasQuestInteractionAvailable(Entity npc)
		{
			bool hasOffer = npc.ActiveQuest != null && npc.ActiveQuest.State == QuestState.Offered;
			var deliveryTarget = QuestManager.GetQuestToDeliverFor(npc, GetQuestNpcs());
			var incoming = QuestManager.GetIncomingQuestFor(npc, GetQuestNpcs());
			return hasOffer || deliveryTarget != null || incoming != null;
		}

		//  Indique si ce PNJ propose du commerce (a un métier, ou est marqué marchand).
		private static bool HasTradeAvailable(Entity npc)
		{
			// Un membre de guilde (IsTamed) garde son commerce, contrairement à un animal apprivoisé classique.
			return (npc.IsTrader || npc.HasProfession) && (!npc.IsTamed || npc.IsGuildMember);
		}

		private static void StartPouilleux(Entity npc)
		{
			// Le lancement de la table demande une vraie invitation par clic sur le village.
			// On n’impose plus le voisin le plus proche : la table ouvre seulement sur l’initiateur
			// et laisse les participants du monde se signaler par le clic de sélection dans le monde.
			CartesGameUI.Open(npc, Array.Empty<Entity>());
		}

		//  Point d'entrée unifié : ouvre la bulle de choix (Discuter de la quête / Commercer / Rien)
		// si le PNJ a plusieurs interactions possibles, ou ouvre directement la seule interaction
		// disponible s'il n'y en a qu'une (ex : un simple marchand sans quête).
		private static bool TryOpenVillagerDialog(Entity npc)
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

		/// <summary>
		/// Attire vers le PNJ les objets au sol proches qu'il peut effectivement ranger.
		/// Utilise la même portée et la même vitesse que l'attraction du joueur.
		/// </summary>
		public static void TryNpcAttractNearby(Entity npc, float radius)
		{
			float radiusSq = radius * radius;
			foreach (var gi in GroundItems)
			{
				if (!gi.IsAlive || !gi.IsOnGround) continue;
				if (gi.TimeSinceDrop <= gi.PickupCooldown) continue;
				if (Vector2.DistanceSquared(npc.WorldPos, gi.Position) > radiusSq) continue;
				if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) continue;
				if (!World.ContainerHasSpaceForItem(npc.Inventory, itemData.Name)) continue;

				Vector2 direction = npc.WorldPos - gi.Position;
				float distance = direction.Length();
				if (distance > 0.01f)
					direction /= distance;
				gi.GroundVelocity = direction * MathF.Min(gi.AttractSpeed, distance / 0.25f);
			}
		}

		/// <summary>
		/// Ramassage passif : ramasse dans l'inventaire du PNJ tout objet au sol à moins de
		/// "radius" pixels qu'il peut ranger (slot vide ou pile compatible). Contrairement au
		/// trajet actif (GoToGroundItem, piloté par le système de priorités), cette fonction est
		/// appelée sans condition à chaque frame (throttlée côté appelant) : elle sert de filet
		/// de sécurité garanti pour que les objets proches finissent toujours par disparaître du
		/// sol, même si la logique de décision plus haut ne s'est pas déclenchée.
		/// </summary>
		public static void TryNpcPassivePickupNearby(Entity npc, float radius)
		{
			float radiusSq = radius * radius;
			for (int i = GroundItems.Count - 1; i >= 0; i--)
			{
				var gi = GroundItems[i];
				if (!gi.IsAlive) continue;
				if (gi.TimeSinceDrop <= gi.PickupCooldown) continue;
				if (Vector2.DistanceSquared(npc.WorldPos, gi.Position) > radiusSq) continue;
				if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) continue;
				if (!CanNpcPickUpItem(npc, itemData)) continue;

				var item = new Item(itemData.Name, gi.Count, itemData.Color, itemData.Icon);
				if (gi.CustomColors != null && gi.CustomColors.Count > 0)
					item.CustomColors = new List<Color?>(gi.CustomColors);
				item.RestoreMetadataFromSave(gi.Metadata ?? "");
				if (gi.Meta != null && gi.Meta.Count > 0)
					foreach (var kv in gi.Meta)
						item.Meta[kv.Key] = kv.Value;

				if (TryEquipNpcPickup(npc, item, itemData))
				{
					Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count=1 mode=passive equipped=true");
					npc.NotifyItemPickedUp();
					if (gi.Count <= 1)
						GroundItems.RemoveAt(i);
					else
						gi.Count--;
					continue;
				}

				int deposited = World.StackItemIntoContainer(npc.Inventory, item, gi.Count);
				if (deposited >= gi.Count)
				{
					Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count={deposited} mode=passive");
					npc.NotifyItemPickedUp();
					GroundItems.RemoveAt(i);
				}
				else if (deposited > 0)
				{
					gi.Count -= deposited;
				}
			}
		}

		/// <summary>
		/// Cherche l'objet au sol le plus proche que ce PNJ peut effectivement ranger (slot vide
		/// ou pile existante du même item dans son inventaire), dans la limite de maxDistance.
		/// Ignore les objets tout juste jetés (encore en cooldown) pour ne pas se jeter dessus
		/// avant même qu'ils aient fini de rebondir.
		/// </summary>
		public static GroundItem? FindNearestGroundItemForNpc(Vector2 worldPos, float maxDistance, Entity npc)
		{
			GroundItem? closest = null;
			float bestDistSq = maxDistance * maxDistance;

			foreach (var gi in GroundItems)
			{
				if (!gi.IsAlive) continue;
				if (gi.TimeSinceDrop <= gi.PickupCooldown) continue;
				if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) continue;
				if (!CanNpcPickUpItem(npc, itemData)) continue;

				float distSq = Vector2.DistanceSquared(worldPos, gi.Position);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = gi;
				}
			}
			return closest;
		}

		/// <summary>
		/// Ramasse (si toujours présent) l'objet au sol identifié par targetNetId dans
		/// l'inventaire du PNJ. Ne fait rien s'il a déjà été ramassé entre-temps par quelqu'un
		/// d'autre (le joueur, un autre PNJ) — le PNJ revient simplement bredouille.
		/// </summary>
		public static void TryNpcPickupGroundItem(Entity npc, Guid targetNetId)
		{
			if (targetNetId == Guid.Empty) return;
			var gi = GroundItems.FirstOrDefault(g => g.NetId == targetNetId && g.IsAlive);
			if (gi == null) return;
			if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) return;
			if (!CanNpcPickUpItem(npc, itemData)) return;

			var item = new Item(itemData.Name, gi.Count, itemData.Color, itemData.Icon);
			if (gi.CustomColors != null && gi.CustomColors.Count > 0)
				item.CustomColors = new List<Color?>(gi.CustomColors);
			item.RestoreMetadataFromSave(gi.Metadata ?? "");
			if (gi.Meta != null && gi.Meta.Count > 0)
				foreach (var kv in gi.Meta)
					item.Meta[kv.Key] = kv.Value;

			if (TryEquipNpcPickup(npc, item, itemData))
			{
				Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count=1 mode=active equipped=true");
				npc.NotifyItemPickedUp();
				if (gi.Count <= 1)
					GroundItems.Remove(gi);
				else
					gi.Count--;
				return;
			}

			int deposited = World.StackItemIntoContainer(npc.Inventory, item, gi.Count);
			if (deposited >= gi.Count)
			{
				Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count={deposited} mode=active");
				npc.NotifyItemPickedUp();
				GroundItems.Remove(gi);
			}
			else if (deposited > 0)
			{
				gi.Count -= deposited; // inventaire presque plein : le PNJ prend ce qu'il peut, le reste reste au sol
			}
		}

		private static bool CanNpcPickUpItem(Entity npc, ItemData itemData)
		{
			return World.ContainerHasSpaceForItem(npc.Inventory, itemData.Name)
				|| HasFreeNpcEquipmentSlot(npc, itemData);
		}

		private static bool HasFreeNpcEquipmentSlot(Entity npc, ItemData itemData)
		{
			if (itemData.CoveredZones.Count > 0)
				return itemData.CoveredZones.All(zone => npc.Equipment?.GetZoneItem(zone) == null);

			if (!itemData.HasEquipSlot || npc.Equipment == null) return false;
			return npc.Equipment.GetEquipCount(itemData.EquipSlot) < itemData.EquipMax
				&& Enumerable.Range(0, itemData.EquipMax)
					.Any(index => npc.Equipment.GetItemInSlot(itemData.EquipSlot, index) == null);
		}

		private static bool TryEquipNpcPickup(Entity npc, Item item, ItemData itemData)
		{
			if (!HasFreeNpcEquipmentSlot(npc, itemData)) return false;
			npc.Equipment ??= new Equipment();

			if (itemData.CoveredZones.Count > 0)
			{
				bool equipped = npc.Equipment.EquipOnBody(item, out _);
				if (equipped) npc.Equipment.LoadEquipmentTextures();
				return equipped;
			}

			for (int index = 0; index < itemData.EquipMax; index++)
			{
				if (npc.Equipment.GetItemInSlot(itemData.EquipSlot, index) == null)
				{
					bool equipped = npc.Equipment.EquipItem(item, itemData.EquipSlot, index);
					if (equipped) npc.Equipment.LoadEquipmentTextures();
					return equipped;
				}
			}

			return false;
		}

		/// <summary>
		/// Fait "acheter" une ration de nourriture par un PNJ affamé auprès d'un marchand.
		/// Génération infinie côté serveur pour l'instant : pas de stock ni d'or décomptés
		/// chez le marchand. Si le marchand a un item de type Food dans son propre inventaire
		/// (vitrine), c'est celui-ci qui est utilisé comme modèle ; sinon on retombe sur le
		/// premier item Food connu de la base de données.
		/// </summary>
		public static void TryBuyFoodFromMerchant(Entity buyer, Guid merchantNetId)
		{
			var merchant = entities.FirstOrDefault(e => e.NetId == merchantNetId && e.IsAlive);
			if (merchant == null || !(merchant.IsTrader || merchant.HasProfession)) return;

			var foodSlot = merchant.Inventory.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item != null
				&& GameData.ItemDatabase.TryGetValue(GameData.GetItemId(s.Item.Name), out var d) && d.Type == ItemType.Food);

			Item? foodTemplate = foodSlot?.Item;
			if (foodTemplate == null)
			{
				var fallback = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Type == ItemType.Food);
				if (fallback.ID == 0) return; // aucune nourriture connue dans le jeu : rien à faire
				foodTemplate = new Item(fallback.Name, 1, fallback.Color, fallback.Icon);
			}

			buyer.Hunger = 0f;
			World.StackItemIntoContainer(buyer.Inventory, foodTemplate, 1);
		}

		/// <summary>
		/// Demande la fermeture du menu pause : joue l'animation de glissement vers le haut,
		/// puis applique le changement d'état (et l'action éventuelle) une fois l'animation terminée.
		/// </summary>
		private static void RequestClosePauseMenu(GameState target, Action extraAction = null)
		{
			if (_pauseMenuClosing) return; // déjà en cours de fermeture
			_pauseMenuClosing = true;
			_pausePendingState = target;
			_pausePendingAction = extraAction;
		}
    }
}
