// Network.cs - Système de multijoueur en ligne (hébergement / connexion à une partie)
//
// Architecture : "host authoritative" (le joueur qui héberge fait tourner la simulation
// complète, exactement comme en solo). Les clients distants :
//   - envoient leur état (position, anim...) et leurs demandes d'action au host,
//   - reçoivent en retour l'état des autres joueurs, les chunks du monde, et la
//     confirmation des actions (construire/casser/récolter/...) pour rester synchronisés.
//
// Transport : TCP brut (sockets .NET standard, aucune dépendance externe), avec des
// messages encodés en JSON et préfixés par leur taille (4 octets) pour le découpage.
// Compression GZip automatique pour les paquets de plus de 4 Ko.
//
// INTERPOLATION : pour un rendu fluide, les clients stockent les derniers états reçus
// avec leur timestamp et interpolent linéairement entre deux échantillons.
#nullable enable
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO.Compression;
using Raylib_cs;

namespace Soulfract
{
    // ───────────────────────── STRUCTURES DE MESSAGES ─────────────────────────

    // Représente l'état complet d'un joueur (position, apparence, anim...).
    // Utilisé à la fois pour le "Hello" initial, les mises à jour périodiques,
    // et la liste des joueurs connectés.
    public class PlayerTransform
    {
        public int Id { get; set; } = -1;
        public string Name { get; set; } = "Joueur";
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float Facing { get; set; } = 1f;
        public string AnimState { get; set; } = "idle";
        public int AnimFrame { get; set; }
        public float AnimProg { get; set; }
        public float AttackSwingProgress { get; set; }
        public int HotbarSlot { get; set; }
        public string Species { get; set; } = "human";
        public ColorSave Skin { get; set; } = new ColorSave { R = 255, G = 235, B = 200, A = 255 };
        public ColorSave MorphTint { get; set; } = new ColorSave { R = 255, G = 255, B = 255, A = 255 };
        public Dictionary<string, int> MorphFeatureVariants { get; set; } = new();
        public Dictionary<string, ColorSave> MorphFeatureColors { get; set; } = new();
        public ColorSave Hair { get; set; } = new ColorSave { R = 60, G = 40, B = 30, A = 255 };
        public int HairStyle { get; set; } = 0;
        public int BeardStyle { get; set; } = 0;
        public int EyeStyle { get; set; }
        public ColorSave EyeColor { get; set; } = new ColorSave { R = 80, G = 120, B = 180, A = 255 };

        //  MULTIJOUEUR : identifiant stable (généré une fois, stocké sur la machine du
        // joueur) permettant au host de le reconnaître s'il rejoint un monde déjà visité.
        public string PlayerGuid { get; set; } = "";

        //  Rotation de tête (visée à la souris) et position monde du curseur du joueur -
        // pour que chaque joueur distant ait sa propre orientation de tête/bras et son
        // propre regard (pupilles/sourcils) au lieu de copier ceux du joueur qui regarde.
        public float HeadAngle { get; set; } = 0f;
        public float LookX { get; set; } = 0f;
        public float LookY { get; set; } = 0f;

        //  SYNCHRONISATION ÉTENDUE
        public int HP { get; set; } = 20;
        public int MaxHP { get; set; } = 20;
        // Item en main (null = rien) : nom de la texture dans ItemDatabase
        public int HeldItemId { get; set; } = 0;
        // Équipement visible (ids ItemDatabase, 0 = vide) - conservés pour compatibilité
        public int EqHead { get; set; } = 0;
        public int EqBody { get; set; } = 0;
        public int EqLegs { get; set; } = 0;

        //  NOUVEAU : équipement complet (toutes les zones + couleurs)
        public EquipData Equip { get; set; } = new EquipData();
        public bool Underground { get; set; }

        //  PVP : si vrai, ce joueur ne doit pas être localisable par les autres
        // (pseudo + barre de vie masqués au-dessus de sa tête).
        public bool PvpEnabled { get; set; } = false;
    }

    // Snapshot périodique de toutes les entités simulées par le host
    public class EntityDto
    {
        public string NetId { get; set; } = "";
        public string Species { get; set; } = "";
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float Facing { get; set; } = 1f;
        public string AnimState { get; set; } = "idle";
        public int AnimFrame { get; set; }
        public float AnimProg { get; set; }
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public float Scale { get; set; } = 1f;
        public float DamageFlash { get; set; } = 0f;
        public bool IsTamed { get; set; }
        public string OwnerName { get; set; } = "";
        public InventorySlotSave? SlimeStorageSlot { get; set; }
        public float SlimeIncubationSeconds { get; set; }
        public bool IsSpiritAnimal { get; set; }
        public int SpiritTotemTileX { get; set; } = int.MinValue;
        public int SpiritTotemTileY { get; set; } = int.MinValue;
        //  "Les amis de mes ennemis sont mes ennemis" : reflète Entity.IsHostilePet - vrai si ce
        // tamed appartient à un PNJ hostile (ex : loup de compagnie d'un gobelin). Permet aux
        // clients distants de savoir qu'il reste une cible valide malgré IsTamed (voir
        // GetRemoteEntitiesInAttackCone dans Program.Combat.cs), contrairement à un tamed "ami".
        public bool IsHostilePet { get; set; }
        //  MULTIJOUEUR : vraie si un client est en train de porter cette créature - permet aux
        // autres clients de savoir qu'elle n'est pas une cible valide (attaque/portage).
        public bool IsCarried { get; set; }
        public bool IsBlinking { get; set; }
        public string CustomName { get; set; } = "";
        public ColorSave Tint { get; set; } = new();
        public ColorSave HairColor { get; set; } = new();
        public int HairStyle { get; set; }
        public int BeardStyle { get; set; }
        //  NOUVEAU : équipement complet de l'entité
        public EquipData Equip { get; set; } = new EquipData();

        //  MULTIJOUEUR : données de quête/commerce du PNJ, absentes jusqu'ici du snapshot,
        // ce qui empêchait les clients (autres que l'hôte) de voir les quêtes proposées et
        // le commerce d'un villageois - ils ne recevaient que sa position/anim/équipement.
        public string FirstName { get; set; } = "";
        public bool IsTrader { get; set; }
        public List<int> TraderItems { get; set; } = new();
        public int Profession { get; set; }
        public float Friendship { get; set; }
        public bool IsGuildMember { get; set; }
        public bool WantsToJoinGuild { get; set; }

        //  Quête active proposée/en cours PAR ce PNJ (il est le donneur), aplatie pour le
        // réseau (Quest n'est pas directement sérialisable proprement dans un DTO imbriqué
        // pratique). QuestState = -1 signifie "aucune quête active".
        public string QuestId { get; set; } = "";
        public string QuestTargetNetId { get; set; } = "";
        public int QuestItemId { get; set; }
        public string QuestItemName { get; set; } = "";
        public int QuestItemQty { get; set; }
        public int QuestRewardItemId { get; set; }
        public string QuestRewardItemName { get; set; } = "";
        public int QuestRewardItemQty { get; set; }
        public int QuestState { get; set; } = -1;
    }

    // Dégâts infligés à une entité par un joueur client
    public class EntityHitMsg
    {
        public string NetId { get; set; } = "";
        public int Damage { get; set; }
    }

    // Dégâts infligés par une entité (IA) à un joueur client spécifique - envoyé par le host
    // au client concerné, qui applique alors les dégâts sur son propre HP local.
    public class PlayerDamageMsg
    {
        public int Amount { get; set; }
        public float PoisonDuration { get; set; }
    }

    //  MULTIJOUEUR : portage de créatures par un client. Le client demande, le host (seul
    // autoritaire sur "entities") valide et fait suivre la créature à la position du client
    // tant qu'elle est portée ; elle continue d'apparaître dans les snapshots EntitySnapshotMsg
    // normaux (avec une position qui suit le porteur), donc tous les autres joueurs la voient.
    public class PickupCreatureRequestMsg
    {
        public string NetId { get; set; } = "";
    }

    public class DropCreatureRequestMsg
    {
        public string NetId { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
    }

    //  MULTIJOUEUR : apprivoisement d'animal par un client. Le host (seul autoritaire sur
    // "entities") valide la distance, la nourriture donnée et l'état de l'animal, puis
    // applique le tamage et répond au client pour qu'il consomme l'item et affiche le résultat.
    public class TameAnimalRequestMsg
    {
        public string NetId { get; set; } = "";
        public int ItemId { get; set; }
    }

    public class TameAnimalResultMsg
    {
        public string NetId { get; set; } = "";
        public bool Success { get; set; }
        public string Species { get; set; } = "";
        public int ItemId { get; set; }
    }

    public class SlimeStorageRequestMsg
    {
        public string NetId { get; set; } = "";
        public InventorySlotSave Slot { get; set; } = new();
    }

    public class EntitySnapshotMsg
    {
        public List<EntityDto> Entities { get; set; } = new();
    }

    public class EntityDeathMsg
    {
        public string NetId { get; set; } = "";
    }

    // Synchronisation du temps de jeu (cycle jour/nuit)
    public class GameTimeMsg
    {
        public float GameTime { get; set; }
    }

    //  MÉTÉO : synchronisation de la pluie/etc. décidée par le host vers tous les clients,
    // pour que tout le monde voie exactement le même temps qu'il fasse (pluie visible pour
    // tout le monde en même temps, pas seulement pour l'hôte).
    public class GameWeatherMsg
    {
        public string WeatherType { get; set; } = "clear";
    }

    // Mise à jour d'un conteneur (coffre) après modification
    public class ContainerSyncMsg
    {
        public int TileX { get; set; }
        public int TileY { get; set; }
        public bool Underground { get; set; }
        public ContainerInventorySave Data { get; set; } = new();
    }

    // Requête d'ouverture de conteneur par un client (pour recevoir son contenu actuel)
    public class ContainerRequestMsg
    {
        public int TileX { get; set; }
        public int TileY { get; set; }
        public bool Underground { get; set; }
    }

    //  MULTIJOUEUR : un client demande à l'hôte (seul autoritaire sur Program.entities et
    // donc sur l'état des quêtes) d'appliquer une action de quête/guilde. NpcNetId est le
    // PNJ sur lequel le joueur a cliqué (donneur pour Accept/Decline/Validate, cible pour
    // Deliver). Pour "Deliver", le client a déjà vérifié/retiré l'objet de son propre
    // inventaire localement (l'inventaire est géré côté client) ; l'hôte se contente de
    // faire passer la quête à l'état "Delivered".
    public class QuestActionRequestMsg
    {
        public string NpcNetId { get; set; } = "";
        public string Action { get; set; } = "";

        //  MULTIJOUEUR : NetId additionnel optionnel, utilisé par "GiftAnimal" pour transporter
        // l'identifiant de l'animal apprivoisé offert (le client connaît l'Entity locale de
        // l'animal, mais seul l'hôte peut légitimement muter Program.entities).
        public string? ExtraNetId { get; set; } = null;
    }

    //  MULTIJOUEUR : retour de l'hôte au client d'origine après traitement d'une action de
    // quête asynchrone (pour l'instant seulement "GiftAnimal", dont le résultat n'était pas
    // visible immédiatement côté client avant la prochaine resynchronisation périodique).
    public class QuestActionResultMsg
    {
        public bool Success { get; set; }
        public string Action { get; set; } = "";
        public string NpcNetId { get; set; } = "";
    }

    public class TraderPurchaseRequestMsg
    {
        public string NpcNetId { get; set; } = "";
        public int ItemId { get; set; }
    }

    public class PouilleuxInviteRequestMsg
    {
        public int TargetConnectionId { get; set; }
    }

    public class PouilleuxInvitationMsg
    {
        public string InvitationId { get; set; } = "";
        public int InviterConnectionId { get; set; }
        public string InviterName { get; set; } = "Joueur";
    }

    public class PouilleuxInviteResultMsg
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class PouilleuxInviteResponseMsg
    {
        public string InvitationId { get; set; } = "";
        public bool Accepted { get; set; }
    }

    public class PouilleuxStartMsg
    {
        public string SessionId { get; set; } = "";
        public int HostConnectionId { get; set; }
        public int OtherConnectionId { get; set; }
        public string OtherPlayerName { get; set; } = "Joueur";
    }

    public class PouilleuxParticipantStateMsg
    {
        public int ConnectionId { get; set; }
        public string Name { get; set; } = "Joueur";
        public List<int> Cards { get; set; } = new();
        public bool IsSpectator { get; set; }
    }

    public class PouilleuxStateMsg
    {
        public string SessionId { get; set; } = "";
        public int CurrentOwnerConnectionId { get; set; }
        public int Phase { get; set; }
        public string Status { get; set; } = "";
        public string Result { get; set; } = "";
        public List<int> DealingDeck { get; set; } = new();
        public int TransferSourceConnectionId { get; set; } = int.MinValue;
        public int TransferTargetConnectionId { get; set; } = int.MinValue;
        public int TransferCardIndex { get; set; } = -1;
        public int TransferCard { get; set; }
        public int TransferTargetIndex { get; set; } = -1;
        public int AnimationRank { get; set; } = -1;
        //  Nécessaires pour rejouer l'animation de mélange d'un PNJ (phase NpcShuffling)
        // chez un client au lieu de la masquer — voir CartesGameUI.ApplyNetworkState.
        public int ShuffleParticipantConnectionId { get; set; } = int.MinValue;
        public int ShuffleFirstIndex { get; set; } = -1;
        public int ShuffleSecondIndex { get; set; } = -1;
        public float ShuffleDuration { get; set; } = 0.6f;
        public List<PouilleuxParticipantStateMsg> Participants { get; set; } = new();
    }

    public class PouilleuxDrawRequestMsg
    {
        public string SessionId { get; set; } = "";
        public int SourceParticipantIndex { get; set; }
        public int CardIndex { get; set; }
    }

    public class PouilleuxStartRequestMsg
    {
        public string SessionId { get; set; } = "";
    }

    public class WelcomeMsg
    {
        public int YourId { get; set; }
        public int VoicePort { get; set; }
        public string VoiceToken { get; set; } = "";
        public int WorldSeed { get; set; }
        public string WorldName { get; set; } = "";
        public float GameTime { get; set; }
        //  MÉTÉO : envoyée dès l'arrivée pour que le nouveau client voie tout de suite le
        // même temps que les autres, sans attendre la prochaine diffusion périodique.
        public string WeatherType { get; set; } = "clear";
        public List<PlayerTransform> Roster { get; set; } = new();
        //  MULTIJOUEUR : personnage sauvegardé du joueur qui rejoint (position, stats,
        // inventaire, équipement, apparence), s'il a déjà visité ce monde. Null la
        // première fois : le client applique alors son comportement de bienvenue habituel.
        public GuestPlayerSaveData? Restore { get; set; } = null;
    }

    public class PlayerLeftMsg
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class WorldInfoRequestMsg
    {
        public List<string> CandidateGuids { get; set; } = new();
    }

    public class WorldInfoMsg
    {
        public string WorldName { get; set; } = "";
        public List<string> ExistingCharacterGuids { get; set; } = new();
    }

    public class RosterStateMsg
    {
        public List<PlayerTransform> Players { get; set; } = new();
    }

    public class ChatRelayMsg
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class ChunkRequestMsg
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public bool Underground { get; set; }
    }

    public class ChunkDataMsg
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public bool Underground { get; set; }
        public ChunkSaveData Data { get; set; } = new();
    }

    //  MULTIJOUEUR : paquet unique envoyé à un client qui vient de rejoindre, juste après
    // "Welcome". Regroupe tout ce qu'il faut pour afficher un monde complet dès la première
    // frame (au lieu d'attendre les diffusions périodiques et les requêtes de chunk une par
    // une) : les chunks autour de son point d'apparition, les entités, les objets au sol et
    // l'état du réseau électrique. Le client reste sur l'écran de chargement tant qu'il n'a
    // pas reçu ce message (voir HandleWorldSync / Program.OnNetworkWorldSyncComplete).
    public class WorldSyncMsg
    {
        public List<ChunkDataMsg> Chunks { get; set; } = new();
        public EntitySnapshotMsg Entities { get; set; } = new();
        public GroundItemsSnapshotMsg GroundItems { get; set; } = new();
        public EnergySnapshotMsg Energy { get; set; } = new();
        public bool IsFinalPart { get; set; }
    }

    // Message générique de mutation du monde (construire, casser, planter...).
    // "Action" identifie l'opération, X/Y la tuile concernée, Value un paramètre
    // (id d'objet, id de tuile de sol, id de culture selon le cas).
    public class WorldActionMsg
    {
        public string Action { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Value { get; set; }
        public bool Underground { get; set; }
    }

    //  Message générique de mutation d'une métadonnée de tuile (voir ChunkData.TileMeta /
    // World.SetTileMeta). Même schéma que WorldActionMsg, mais porte une clé/valeur texte
    // plutôt qu'un seul int, pour couvrir en un seul type de message toute donnée occasionnelle
    // de tuile (ruche, minerai, câblage, etc.) sans avoir à ajouter un message par fonctionnalité.
    // Value = "" (ou absent) supprime la clé.
    public class TileMetaActionMsg
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Underground { get; set; }
    }

    public class GroundItemDto
    {
        public string NetId { get; set; } = "";
        public int ItemId { get; set; }
        public int Count { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public List<ColorSave?> Colors { get; set; } = new();
        public string Metadata { get; set; } = "";
        public Dictionary<string, string>? Meta { get; set; }
        //  MULTIJOUEUR : contenu complet d'un sac de butin (mort d'un joueur), au format
        // "itemId:count|itemId:count|...". Vide pour un item au sol normal.
        public string LootbagPayload { get; set; } = "";
    }

    public class GroundItemsSnapshotMsg
    {
        public List<GroundItemDto> Items { get; set; } = new();
    }

    //  MULTIJOUEUR : action sur la grille électrique (câblage, carburant, marche/arrêt).
    // Même schéma que WorldActionMsg : le client délègue, le host applique avec son
    // autorité puis redistribue.
    public class EnergyActionMsg
    {
        public string Action { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public float Water { get; set; }
        public float Coal { get; set; }
    }

    public class SteamEngineDto { public int X; public int Y; public bool IsOn; public float Water; public float Coal; public float SteamPressure; }
    public class DynamoDto { public int X; public int Y; public float ElectricityOutput; }
    public class BatteryDto { public int X; public int Y; public float StoredEnergy; public float MaxCapacity; }
    public class AmpouleDto { public int X; public int Y; public bool IsOn; }
    public class EnergyConnectionDto { public int X1; public int Y1; public int X2; public int Y2; }

    // Snapshot complet de la grille électrique, diffusé périodiquement par le host (comme
    // pour les items au sol / entités) puisque son état évolue en continu (pression,
    // charge des batteries...) et pas seulement lors d'actions ponctuelles du joueur.
    public class EnergySnapshotMsg
    {
        public List<SteamEngineDto> Engines { get; set; } = new();
        public List<DynamoDto> Dynamos { get; set; } = new();
        public List<BatteryDto> Batteries { get; set; } = new();
        public List<AmpouleDto> Ampoules { get; set; } = new();
        public List<EnergyConnectionDto> Connections { get; set; } = new();
    }

    public class PickupRequestMsg
    {
        public string NetId { get; set; } = "";
    }

    //  MULTIJOUEUR : demande d'un client pour faire apparaître un item au sol.
    // Le host est seul propriétaire de Program.GroundItems ; un client ne doit donc
    // jamais y ajouter d'item localement (ça créerait un item "fantôme" invisible et
    // impossible à ramasser pour tout le monde sauf lui). À la place, il envoie cette
    // requête, et c'est le host qui crée réellement l'item puis le diffuse à tous.
    public class ItemDropRequestMsg
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public List<ColorSave?> Colors { get; set; } = new();
        //  Contenu d'instance de l'item jeté (seau rempli, gourde chargée, etc.) — voir
        // Item.Metadata / Item.Meta. Sans ça, un item jeté par un client redevenait "neuf".
        public string Metadata { get; set; } = "";
        public Dictionary<string, string>? Meta { get; set; }
        public PortableContainerSaveData? PortableContainer { get; set; }
        public BackpackSaveData? Backpack { get; set; }
        public float? PickupCooldown { get; set; }
    }

    public class ItemGrantMsg
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public List<ColorSave?> Colors { get; set; } = new();
        //  MULTIJOUEUR : si l'item ramassé était un sac de butin, son contenu complet est
        // renvoyé ici pour que le client le restaure intégralement dans son inventaire.
        public string LootbagPayload { get; set; } = "";
        //  Contenu d'instance de l'item ramassé (voir ItemDropRequestMsg.Metadata/Meta).
        public string Metadata { get; set; } = "";
        public Dictionary<string, string>? Meta { get; set; }
        public PortableContainerSaveData? PortableContainer { get; set; }
        public BackpackSaveData? Backpack { get; set; }
    }

    //  MULTIJOUEUR : demande d'un client mort pour faire apparaître son sac de butin.
    // Comme pour ItemDropRequestMsg, seul le host possède Program.GroundItems ; le client
    // envoie donc le contenu de ses sacs (ceinture + sac à dos + panier) et c'est le host
    // qui crée réellement le sac au sol puis le diffuse à tous.
    public class LootbagDropRequestMsg
    {
        public string Payload { get; set; } = "";
        public float PosX { get; set; }
        public float PosY { get; set; }
    }

    //  MULTIJOUEUR : envoyé périodiquement par un client au host (et à la déconnexion
    // propre) pour que celui-ci conserve son personnage entre deux connexions.
    public class PlayerSaveStateMsg
    {
        public string PlayerGuid { get; set; } = "";
        public GuestPlayerSaveData Data { get; set; } = new();
    }

    // Enveloppe générique : "T" identifie le type, "J" contient le JSON du contenu réel.
    internal class NetEnvelope
    {
        public string T { get; set; } = "";
        public string J { get; set; } = "";
    }

    internal class IncomingMessage
    {
        public int ConnectionId;
        public NetEnvelope Envelope = new();
    }

    // Représente une connexion active côté host (un client connecté) ou côté client (le host).
    internal class NetConnection
    {
        public int Id;
        public TcpClient Client = null!;
        public NetworkStream Stream = null!;
        public Thread? ReaderThread;
        public bool Disconnected = false;
        public DateTime LastActivity = DateTime.UtcNow; // pour heartbeat
        public int ErrorCount = 0; // nombre d'erreurs consécutives
        public string VoiceToken = "";

        // État connu pour ce correspondant (utilisé côté host pour suivre chaque client).
        public PlayerTransform? LastTransform;
    }

    //  INTERPOLATION : structure générique pour stocker deux états consécutifs avec timestamp
    internal class InterpolatedState<T> where T : class, new()
    {
        public T Previous { get; set; } = new T();
        public T Current { get; set; } = new T();
        public double ReceiveTime { get; set; } = 0; // temps local (Raylib.GetTime()) de réception
    }

    // ════════════════════════════════════════════════════════════════
    //  STRUCTURES POUR L'ÉQUIPEMENT SYNCHRONISÉ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Données d'équipement complètes pour un joueur ou une entité.
    /// Utilisé dans PlayerTransform et EntityDto.
    /// </summary>
    public class EquipData
    {
        // Zones du corps (BodyZone)
        public int Head { get; set; }
        public int Body { get; set; }
        public int Legs { get; set; }
        public int Back { get; set; }
        public int Face { get; set; }
        public int Neck { get; set; }
        public int Waist { get; set; }
        public int Ears { get; set; }
        public int Feet { get; set; }

        // Slots fonctionnels
        public int MainHand { get; set; }
        public int OffHand { get; set; }
        public int Backpack { get; set; }

        // Couleurs personnalisées pour chaque zone (listes de ColorSave)
        public List<ColorSave?> HeadColors { get; set; } = new();
        public List<ColorSave?> BodyColors { get; set; } = new();
        public List<ColorSave?> LegsColors { get; set; } = new();
        public List<ColorSave?> BackColors { get; set; } = new();
        public List<ColorSave?> FaceColors { get; set; } = new();
        public List<ColorSave?> NeckColors { get; set; } = new();
        public List<ColorSave?> WaistColors { get; set; } = new();
        public List<ColorSave?> EarsColors { get; set; } = new();
        public List<ColorSave?> FeetColors { get; set; } = new();
        public List<ColorSave?> MainHandColors { get; set; } = new();
        public List<ColorSave?> OffHandColors { get; set; } = new();
        public List<ColorSave?> BackpackColors { get; set; } = new();

        // Méthode utilitaire pour savoir si l'équipement est vide
        public bool IsEmpty =>
            Head == 0 && Body == 0 && Legs == 0 && Back == 0 && Face == 0 && Neck == 0 && Waist == 0 && Ears == 0 && Feet == 0 &&
            MainHand == 0 && OffHand == 0 && Backpack == 0;
    }

    // ──────────────────── FIN DES STRUCTURES ────────────────────

    public static class NetworkManager
    {
        private const int DEFAULT_PORT = 7777;
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        // ---- Sécurité et robustesse ----
        private const int COMPRESSION_THRESHOLD = 4096; // octets : on compresse au-delà
        private const int MAX_MESSAGE_SIZE = 10 * 1024 * 1024; // 10 Mo (réduit pour éviter les abus)
        private const int MAX_INCOMING_QUEUE = 10000; // nombre max de messages en attente
        private const int MAX_INCOMING_MESSAGES_PER_FRAME = 4;
        private const int NETWORK_TIMEOUT_MS = 5000; // timeout lecture/écriture (5s)
        private const int HEARTBEAT_INTERVAL_MS = 5000; // intervalle entre les heartbeats
        private const int MAX_ERRORS_BEFORE_DISCONNECT = 10; // seuil d'erreurs

        // ---- État général ----
        private static bool _isHost = false;
        private static bool _isClient = false;
        public static bool IsHost => _isHost;
        public static bool IsClient => _isClient;
        public static bool AmIHost { get; private set; } = false;
        public static bool IsOnline => _isHost || _isClient;
        public static int LocalConnectionId => _isHost ? 0 : _myId;

        public static string LastError { get; private set; } = "";
        public static string ConnectionStatus { get; private set; } = "";
        public static bool IsConnecting { get; private set; } = false;

        // ---- Accès depuis Internet (UPnP + IP publique) ----
        public static string PublicIp { get; private set; } = "";
        public static string UpnpStatus { get; private set; } = "";
        public static bool UpnpInProgress { get; private set; } = false;
        public static bool UpnpMappingActive { get; private set; } = false;
        private static int _upnpMappedPort = 0;

        // ---- Côté host ----
        private static TcpListener? _listener;
        private static Thread? _acceptThread;
        private static readonly ConcurrentDictionary<int, NetConnection> _clients = new();
        private static readonly ConcurrentDictionary<string, (int InviterId, int TargetId)> _pendingPouilleuxInvitations = new();
        private static int _nextClientId = 1;
        private static int _hostPort = DEFAULT_PORT;
        private static DateTime _lastHeartbeatCheck = DateTime.UtcNow;

        // ---- Côté client ----
        private static NetConnection? _hostConnection;
        private static int _myId = 0;
        private static DateTime _lastHeartbeatSend = DateTime.UtcNow;

        // ---- Réception ----
        private static readonly ConcurrentQueue<IncomingMessage> _incoming = new();
        private static readonly ConcurrentQueue<int> _disconnections = new();

        // ---- INTERPOLATION : états des joueurs distants (utilisé par TOUS, hôte comme client) ----
        private static readonly ConcurrentDictionary<int, InterpolatedState<PlayerTransform>> _remotePlayerStates = new();
        private static readonly ConcurrentDictionary<string, InterpolatedState<EntityDto>> _remoteEntityStates = new();

        // ---- Chunks en attente (côté client) ----
        private static readonly HashSet<(int, int, bool)> _pendingChunkRequests = new();

        // ---- Items au sol (côté client : copie reçue du host) ----
        private static List<GroundItemDto> _remoteGroundItems = new();
        private static bool _groundItemsDirty = false;

        // ---- Timers d'envoi périodique ----
        private static double _lastStateSend = 0;
        private static double _lastPouilleuxStateBroadcast = 0;
        private static double _lastRosterBroadcast = 0;
        private static double _lastGroundItemsBroadcast = 0;
        private static double _lastEntityBroadcast = 0;
        private static double _lastTimeBroadcast = 0;
        private static double _lastWeatherBroadcast = 0;
        private const double STATE_SEND_INTERVAL = 1.0 / 15.0;  // 15 fps
        //  MULTIJOUEUR : intervalle d'envoi de la sauvegarde du personnage invité au host
        // (beaucoup plus espacé que la position : pas besoin de la fréquence temps réel).
        private const double GUEST_SAVE_INTERVAL = 8.0;
        private static double _lastGuestSaveSend = 0;
        private const double GROUND_ITEMS_INTERVAL = 0.5;
        private const double ENTITY_BROADCAST_INTERVAL = 1.0 / 10.0; // 10 fps
        private const double TIME_BROADCAST_INTERVAL = 5.0;
        //  MÉTÉO : rediffusée assez souvent pour que les transitions (début/fin de pluie
        // décidées par le host) restent quasi immédiates pour les clients.
        private const double WEATHER_BROADCAST_INTERVAL = 3.0;
        private const double ENERGY_BROADCAST_INTERVAL = 0.5;
        private static double _lastEnergyBroadcast = 0;
        // Les snapshots sont reconstruits régulièrement, mais leur contenu reste souvent
        // identique. Conserver l'enveloppe JSON évite les écritures TCP et la compression
        // quand rien n'a changé.
        private static string? _lastGroundItemsPayload;
        private static string? _lastEntityPayload;
        private static string? _lastEnergyPayload;
        private static string? _lastLocalStatePayload;
        private static string? _lastRosterPayload;

        //  MULTIJOUEUR : synchronisation initiale alignée sur la distance de rendu du monde.
        private const int INITIAL_SYNC_CHUNK_RADIUS = 3;

        //  Filet de sécurité : si un client ne reçoit pas "WorldSync" dans ce délai après
        // "Welcome" (host resté sur une version antérieure, message perdu...), on le laisse
        // quand même jouer plutôt que de le bloquer indéfiniment sur l'écran de chargement ;
        // il retombera alors sur le chargement de chunk à la demande, comme avant ce correctif.
        private const double WORLD_SYNC_TIMEOUT = 6.0;
        private static double _welcomeReceivedAt = -1;
        private static bool _worldSyncApplied = false;

        // ---- Entités reçues (côté client) ----
        // Utilisé pour l'API publique (GetRemoteEntities) qui renvoie la liste interpolée
        private static List<EntityDto> _remoteEntitiesInterpolated = new();
        private static bool _remoteEntitiesDirty = false;

        public static int LocalId => _myId;
        public static int PlayerCount => 1 + _remotePlayerStates.Count;

        // ════════════════════════════════════════════════════════════════
        //  DÉMARRAGE / ARRÊT
        // ════════════════════════════════════════════════════════════════

        // Ajout de la méthode KickPlayer
        public static void KickPlayer(int connectionId)
        {
            if (!_isHost) return;

            // Envoyer un message "Kick" au client
            SendTo(connectionId, "Kick", new { });

            // Fermer la connexion et nettoyer
            if (_clients.TryRemove(connectionId, out var conn))
            {
                VoiceChat.RemovePeer(connectionId);
                try { conn.Client.Close(); } catch { }
                string name = conn.LastTransform?.Name ?? $"Joueur {connectionId}";
                Broadcast("Left", new PlayerLeftMsg { Id = connectionId, Name = name });
                _remotePlayerStates.TryRemove(connectionId, out _);
                Program.OnNetworkPlayerLeft(connectionId, name);
            }
        }

        private static async Task SetupInternetAccessAsync(int port)
        {
            try
            {
                // Timeout global de 8 secondes pour l'ensemble de la découverte UPnP
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var mapping = await UpnpPortMapper.TryAddPortMappingAsync(port, cts.Token).ConfigureAwait(false);

                if (mapping.Success)
                {
                    UpnpMappingActive = true;
                    _upnpMappedPort = port;
                    UpnpStatus = mapping.UdpSuccess
                        ? "Port du jeu et de la voix ouvert automatiquement (UPnP)."
                        : "Port du jeu ouvert (UPnP), mais l'UDP vocal doit être autorisé sur le même port.";
                }
                else
                {
                    UpnpStatus = "Redirection de port automatique impossible (routeur sans UPnP ?). " +
                                "Redirigez manuellement le port si vos amis ne sont pas sur le même réseau.";
                }

                // Récupération de l'IP publique (timeout 5 secondes)
                string ip = mapping.ExternalIp;
                if (string.IsNullOrEmpty(ip))
                {
                    try
                    {
                        using var httpCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        ip = await UpnpPortMapper.FetchPublicIpFallbackAsync(httpCts.Token).ConfigureAwait(false) ?? "";
                    }
                    catch (OperationCanceledException)
                    {
                        ip = "";
                    }
                }

                PublicIp = ip ?? "";
                if (string.IsNullOrEmpty(PublicIp))
                {
                    UpnpStatus += " (IP publique non détectée, consultez un site comme whatismyip.com)";
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout global de la découverte UPnP
                UpnpStatus = "Découverte UPnP trop lente (délai dépassé). " +
                            "Redirigez manuellement le port si nécessaire.";

                // On essaie quand même de récupérer l'IP publique via le fallback (timeout 5s)
                try
                {
                    using var httpCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    string ip = await UpnpPortMapper.FetchPublicIpFallbackAsync(httpCts.Token).ConfigureAwait(false) ?? "";
                    PublicIp = ip;
                    if (!string.IsNullOrEmpty(PublicIp))
                        UpnpStatus += " (IP publique trouvée)";
                    else
                        UpnpStatus += " (IP publique non détectée)";
                }
                catch
                {
                    // Ignoré
                }
            }
            catch (Exception ex)
            {
                UpnpStatus = $"Redirection de port automatique impossible : {ex.Message}";
            }
            finally
            {
                UpnpInProgress = false; // Toujours sortir de l'état "en cours"
            }
        }

        // Modifier la méthode StartHost pour inclure la vérification
        public static bool StartHost(int port)
        {
            try
            {
                Disconnect();
                _hostPort = port <= 0 ? DEFAULT_PORT : port;
                _listener = new TcpListener(IPAddress.Any, _hostPort);
                _listener.Start();
                _isHost = true;
                _isClient = false;
                _myId = 0;
                AmIHost = true;
                ConnectionStatus = $"Partie hébergée sur le port {_hostPort}";
                VoiceChat.StartHost(_hostPort, id => _clients.TryGetValue(id, out var peer) ? peer.VoiceToken : null);
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
                _acceptThread.Start();

                PublicIp = "";
                UpnpStatus = "Recherche de votre routeur (UPnP)...";
                UpnpInProgress = true;
                UpnpMappingActive = false;
                int portToOpen = _hostPort;
                Task.Run(() => SetupInternetAccessAsync(portToOpen));

                return true;
            }
            catch (Exception ex)
            {
                try { _listener?.Stop(); } catch { }
                _listener = null;
                LastError = $"Impossible d'héberger : {ex.Message}";
                _isHost = false;
                return false;
            }
        }

        public static void JoinHostAsync(string ip, int port, string playerName)
        {
            Disconnect();
            ConnectionStatus = $"Connexion à {ip}:{(port <= 0 ? DEFAULT_PORT : port)}...";
            LastError = "";
            IsConnecting = true;
            var thread = new Thread(() => DoJoin(ip, port <= 0 ? DEFAULT_PORT : port, playerName));
            thread.IsBackground = true;
            thread.Start();
        }

        public static Task<WorldInfoMsg?> QueryHostWorldInfoAsync(string ip, int port, List<string> candidateGuids)
        {
            return Task.Run(() =>
            {
                try
                {
                    using var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = NETWORK_TIMEOUT_MS, SendTimeout = NETWORK_TIMEOUT_MS };
                    if (!tcp.ConnectAsync(ip, port <= 0 ? DEFAULT_PORT : port).Wait(TimeSpan.FromMilliseconds(NETWORK_TIMEOUT_MS)))
                        return null;

                    using var stream = tcp.GetStream();
                    WriteFramed(stream, Encelope("WorldInfoRequest", new WorldInfoRequestMsg { CandidateGuids = candidateGuids }));
                    string? response = ReadFramed(stream);
                    if (response == null) return null;

                    var envelope = JsonSerializer.Deserialize<NetEnvelope>(response, JsonOpts);
                    if (envelope?.T != "WorldInfo") return null;
                    var worldInfo = Deserialize<WorldInfoMsg>(envelope.J);
                    return string.IsNullOrWhiteSpace(worldInfo.WorldName) ? null : worldInfo;
                }
                catch
                {
                    return null;
                }
            });
        }

        private static void DoJoin(string ip, int port, string playerName)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = new TcpClient();
                tcp.NoDelay = true;
                tcp.SendTimeout = NETWORK_TIMEOUT_MS;
                tcp.ReceiveTimeout = 0;

                var connectTask = tcp.ConnectAsync(ip, port);
                if (!connectTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    LastError = "Connexion échouée : aucune réponse de l'hôte après 10s. " +
                                "Vérifiez l'IP/le port, et que le port est bien ouvert/redirigé chez l'hôte (pare-feu, box).";
                    ConnectionStatus = "";
                    _isClient = false;
                    try { tcp.Close(); } catch { }
                    return;
                }

                var conn = new NetConnection { Id = -1, Client = tcp, Stream = tcp.GetStream(), LastActivity = DateTime.UtcNow };

                var hello = BuildLocalTransform(playerName);
                WriteFramed(conn.Stream, Encelope("Hello", hello));

                conn.ReaderThread = new Thread(() => ReadLoop(conn)) { IsBackground = true };
                conn.ReaderThread.Start();

                _hostConnection = conn;
                _isClient = true;
                _isHost = false;
                ConnectionStatus = "Connecté ! En attente du monde...";
            }
            catch (AggregateException aex) when (aex.InnerException is SocketException sockEx)
            {
                LastError = $"Connexion échouée : {DescribeSocketError(sockEx)}";
                ConnectionStatus = "";
                _isClient = false;
                try { tcp?.Close(); } catch { }
            }
            catch (SocketException sockEx)
            {
                LastError = $"Connexion échouée : {DescribeSocketError(sockEx)}";
                ConnectionStatus = "";
                _isClient = false;
                try { tcp?.Close(); } catch { }
            }
            catch (Exception ex)
            {
                LastError = $"Connexion échouée : {ex.Message}";
                ConnectionStatus = "";
                _isClient = false;
                try { tcp?.Close(); } catch { }
            }
            finally
            {
                IsConnecting = false;
            }
        }

        private static string DescribeSocketError(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    return "connexion refusée par l'adresse indiquée. Le port n'est probablement " +
                           "pas ouvert chez l'hôte (partie non hébergée, mauvais port, ou redirection non active).";
                case SocketError.TimedOut:
                    return "aucune réponse (délai dépassé). Le port semble bloqué ou non redirigé " +
                           "jusqu'à l'hôte (pare-feu, box sans UPnP, ou double NAT/CGNAT côté hôte).";
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                    return "hôte injoignable. Vérifiez l'adresse IP saisie.";
                case SocketError.HostNotFound:
                    return "adresse introuvable. Vérifiez l'IP ou le nom saisi.";
                case SocketError.AccessDenied:
                    return "accès refusé par le système (pare-feu local ou antivirus).";
                default:
                    return ex.Message;
            }
        }

        public static void Disconnect()
        {
            //  MULTIJOUEUR : dernière sauvegarde du personnage avant de couper la connexion,
            // en best-effort (l'envoi périodique couvre déjà la plupart des cas, ceci réduit
            // juste la fenêtre de perte si le joueur quitte juste après une action).
            if (_isClient && _hostConnection != null)
            {
                try
                {
                    SendToHost("PlayerSave", new PlayerSaveStateMsg
                    {
                        PlayerGuid = Program.LocalPlayerGuid,
                        Data = Program.BuildGuestSaveData()
                    });
                }
                catch { /* best-effort : on ne bloque pas la déconnexion si ça échoue */ }
            }

            VoiceChat.Stop();

            _welcomeReceivedAt = -1;
            _worldSyncApplied = false;

            try { _listener?.Stop(); } catch { }
            _listener = null;

            if (UpnpMappingActive && _upnpMappedPort > 0)
            {
                int portToClose = _upnpMappedPort;
                Task.Run(() => UpnpPortMapper.TryRemovePortMappingAsync(portToClose));
            }
            UpnpMappingActive = false;
            UpnpInProgress = false;
            UpnpStatus = "";
            PublicIp = "";
            _upnpMappedPort = 0;

            foreach (var kv in _clients)
            {
                try { kv.Value.Client.Close(); } catch { }
            }
            _clients.Clear();

            if (_hostConnection != null)
            {
                try { _hostConnection.Client.Close(); } catch { }
                _hostConnection = null;
            }

            _isHost = false;
            _isClient = false;
            IsConnecting = false;
            _myId = 0;
            _remotePlayerStates.Clear();
            _remoteEntityStates.Clear();
            _pendingChunkRequests.Clear();
            _remoteGroundItems.Clear();
            _lastGroundItemsPayload = null;
            _lastEntityPayload = null;
            _lastEnergyPayload = null;
            _lastLocalStatePayload = null;
            _lastRosterPayload = null;
            while (_incoming.TryDequeue(out _)) { }
            while (_disconnections.TryDequeue(out _)) { }
            ConnectionStatus = "";
        }

        public static void SendTeleport(int connectionId, Vector2 pos)
        {
            if (!_isHost) return;
            SendTo(connectionId, "Teleport", new TeleportMsg { X = pos.X, Y = pos.Y });
        }

        //  PVP : envoi (côté client) d'une demande de dégâts sur un autre joueur touché
        // par une attaque au corps à corps. Le host revalide le consentement mutuel avant
        // d'appliquer quoi que ce soit (voir Dispatch, case "PvpHitReq").
        public static void SendPvpHitRequest(int targetConnectionId, int damage)
        {
            if (!_isClient || damage <= 0) return;
            SendToHost("PvpHitReq", new PvpHitReqMsg { TargetId = targetConnectionId, Damage = damage });
        }

        //  PVP : résout l'état d'activation du PVP d'un joueur connu du host, à partir de
        // son id de connexion (0 = l'hôte lui-même).
        private static bool GetPvpEnabledFor(int connectionId)
        {
            if (connectionId == 0) return Program.LocalPvpEnabled;
            if (_clients.TryGetValue(connectionId, out var conn) && conn.LastTransform != null)
                return conn.LastTransform.PvpEnabled;
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  RÉSEAU BAS NIVEAU (cadrage des messages, threads de lecture)
        // ════════════════════════════════════════════════════════════════

        private static void AcceptLoop()
        {
            try
            {
                while (_listener != null)
                {
                    TcpClient tcp = _listener.AcceptTcpClient();
                    tcp.NoDelay = true;
                    tcp.SendTimeout = NETWORK_TIMEOUT_MS;
                    tcp.ReceiveTimeout = 0;
                    int id = Interlocked.Increment(ref _nextClientId) - 1;
                    var conn = new NetConnection { Id = id, Client = tcp, Stream = tcp.GetStream(), LastActivity = DateTime.UtcNow };
                    _clients[id] = conn;
                    conn.ReaderThread = new Thread(() => ReadLoop(conn)) { IsBackground = true };
                    conn.ReaderThread.Start();
                }
            }
            catch { /* listener arrêté */ }
        }

        private static void ReadLoop(NetConnection conn)
        {
            bool receivedAnything = false;
            try
            {
                while (true)
                {
                    string? json = ReadFramed(conn.Stream);
                    if (json == null)
                    {
                        if (_isClient && !receivedAnything)
                            LastError = "Connexion coupée juste après l'établissement, avant toute donnée. " +
                                        "C'est le signe typique d'un pare-feu ou d'une box (chez l'hôte) qui referme " +
                                        "la connexion, ou d'un double NAT/CGNAT qui empêche la redirection de port de fonctionner réellement.";
                        break;
                    }
                    receivedAnything = true;
                    conn.LastActivity = DateTime.UtcNow;
                    conn.ErrorCount = 0; // réinitialise le compteur d'erreurs

                    // Désérialisation protégée
                    NetEnvelope? env = null;
                    try
                    {
                        env = JsonSerializer.Deserialize<NetEnvelope>(json, JsonOpts);
                    }
                    catch (JsonException)
                    {
                        // Message JSON invalide : on ignore
                        continue;
                    }
                    if (env == null) continue;

                    // Limiter la taille de la file d'attente pour éviter l'explosion mémoire
                    if (_incoming.Count >= MAX_INCOMING_QUEUE)
                    {
                        // On supprime les messages les plus anciens
                        while (_incoming.TryDequeue(out _)) { }
                    }
                    _incoming.Enqueue(new IncomingMessage { ConnectionId = conn.Id, Envelope = env });
                }
            }
            catch (SocketException sockEx)
            {
                if (_isClient) LastError = $"Connexion coupée : {DescribeSocketError(sockEx)}";
            }
            catch (ObjectDisposedException)
            {
                // Socket fermé proprement
            }
            catch (IOException ioEx)
            {
                if (_isClient) LastError = $"Erreur d'E/S réseau : {ioEx.Message}";
            }
            catch (Exception ex)
            {
                if (_isClient) LastError = $"Connexion coupée : {ex.Message}";
            }
            finally
            {
                conn.Disconnected = true;
                _disconnections.Enqueue(conn.Id);
            }
        }

        /// <summary>
        /// Écrit un message encadré par sa longueur, avec compression automatique si la taille dépasse le seuil.
        /// Format : [4 octets : longueur totale (payload + 1 octet de flag)] [1 octet : flag (0=non compressé, 1=GZip)] [payload]
        /// </summary>
        private static byte[] BuildFramedPacket(string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte flag = 0;

            // Compression si nécessaire
            if (payload.Length > COMPRESSION_THRESHOLD)
            {
                try
                {
                    using var memoryStream = new MemoryStream();
                    using (var gzip = new GZipStream(memoryStream, CompressionLevel.Fastest, true))
                    {
                        gzip.Write(payload, 0, payload.Length);
                    }
                    payload = memoryStream.ToArray();
                    flag = 1;
                }
                catch
                {
                    // En cas d'erreur, on garde le payload non compressé
                    flag = 0;
                    payload = Encoding.UTF8.GetBytes(json);
                }
            }

            // Taille totale = 1 (flag) + payload.Length
            int totalLen = 1 + payload.Length;
            if (totalLen > MAX_MESSAGE_SIZE)
            {
                // Trop gros, on le réduit en échouant (ou on pourrait tronquer, mais mieux vaut éviter)
                throw new InvalidOperationException($"Message trop gros ({totalLen} octets)");
            }

            byte[] packet = new byte[4 + totalLen];
            BitConverter.GetBytes(totalLen).CopyTo(packet, 0);
            packet[4] = flag;
            Buffer.BlockCopy(payload, 0, packet, 5, payload.Length);
            return packet;
        }

        private static void WriteFramed(NetworkStream stream, string json)
        {
            WriteFramed(stream, BuildFramedPacket(json));
        }

        private static void WriteFramed(NetworkStream stream, byte[] packet)
        {
            stream.Write(packet, 0, packet.Length);
            stream.Flush();
        }

        /// <summary>
        /// Lit un message encadré, le décompresse si nécessaire, et retourne la chaîne JSON.
        /// </summary>
        private static string? ReadFramed(NetworkStream stream)
        {
            byte[] header = new byte[4];
            if (!ReadExact(stream, header, 4)) return null;
            int totalLen = BitConverter.ToInt32(header, 0);
            if (totalLen <= 0 || totalLen > MAX_MESSAGE_SIZE) return null;

            // Lire le flag + payload
            byte[] buffer = new byte[totalLen];
            if (!ReadExact(stream, buffer, totalLen)) return null;

            byte flag = buffer[0];
            byte[] payload = new byte[totalLen - 1];
            Array.Copy(buffer, 1, payload, 0, totalLen - 1);

            try
            {
                if (flag == 1) // compressé
                {
                    using var inputStream = new MemoryStream(payload);
                    using var gzip = new GZipStream(inputStream, CompressionMode.Decompress);
                    using var outputStream = new MemoryStream();
                    gzip.CopyTo(outputStream);
                    byte[] decompressed = outputStream.ToArray();
                    return Encoding.UTF8.GetString(decompressed);
                }
                else
                {
                    return Encoding.UTF8.GetString(payload);
                }
            }
            catch (Exception)
            {
                // Si la décompression échoue, on retourne null (message corrompu)
                return null;
            }
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n;
                try
                {
                    n = stream.Read(buffer, total, count - total);
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }
                if (n <= 0) return false;
                total += n;
            }
            return true;
        }

        private static string Encelope(string type, object payload)
        {
            var env = new NetEnvelope { T = type, J = JsonSerializer.Serialize(payload, payload.GetType(), JsonOpts) };
            return JsonSerializer.Serialize(env, JsonOpts);
        }

        private static void SendTo(int connectionId, string type, object payload)
        {
            if (_clients.TryGetValue(connectionId, out var conn) && !conn.Disconnected)
            {
                try
                {
                    WriteFramed(conn.Stream, Encelope(type, payload));
                    conn.LastActivity = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" Erreur d'envoi réseau ({type}, client {connectionId}) : {ex.Message}");
                    conn.Disconnected = true;
                    _disconnections.Enqueue(connectionId);
                }
            }
        }

        private static void Broadcast(string type, object payload, int exceptConnectionId = -1)
        {
            BroadcastRaw(Encelope(type, payload), exceptConnectionId);
        }

        private static void BroadcastRaw(string raw, int exceptConnectionId = -1)
        {
            byte[] packet = BuildFramedPacket(raw);
            foreach (var kv in _clients)
            {
                if (kv.Key == exceptConnectionId || kv.Value.Disconnected) continue;
                try
                {
                    WriteFramed(kv.Value.Stream, packet);
                    kv.Value.LastActivity = DateTime.UtcNow;
                }
                catch (Exception)
                {
                    kv.Value.Disconnected = true;
                    _disconnections.Enqueue(kv.Key);
                }
            }
        }

        private static void BroadcastIfChanged(string type, object payload, ref string? previousPayload)
        {
            string raw = Encelope(type, payload);
            if (raw == previousPayload) return;
            previousPayload = raw;
            BroadcastRaw(raw);
        }

        private static void SendToHost(string type, object payload)
        {
            SendToHostRaw(Encelope(type, payload));
        }

        private static void SendToHostRaw(string raw)
        {
            if (_hostConnection == null || _hostConnection.Disconnected) return;
            try
            {
                WriteFramed(_hostConnection.Stream, raw);
                _hostConnection.LastActivity = DateTime.UtcNow;
            }
            catch (Exception)
            {
                _hostConnection.Disconnected = true;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  HEARTBEAT et nettoyage des connexions mortes
        // ════════════════════════════════════════════════════════════════

        private static void CheckHeartbeats()
        {
            if (!IsOnline) return;

            var now = DateTime.UtcNow;

            // Côté serveur : vérifier les clients
            if (_isHost)
            {
                foreach (var kv in _clients)
                {
                    var conn = kv.Value;
                    if (conn.Disconnected) continue;
                    // Si la dernière activité est trop ancienne, on considère la connexion morte
                    if ((now - conn.LastActivity).TotalMilliseconds > HEARTBEAT_INTERVAL_MS * 3)
                    {
                        conn.Disconnected = true;
                        _disconnections.Enqueue(conn.Id);
                        try { conn.Client.Close(); } catch { }
                    }
                }
            }

            // Côté client : envoyer un ping périodique
            if (_isClient && _hostConnection != null && !_hostConnection.Disconnected)
            {
                if ((now - _lastHeartbeatSend).TotalMilliseconds > HEARTBEAT_INTERVAL_MS)
                {
                    _lastHeartbeatSend = now;
                    try
                    {
                        // Envoyer un message "Ping" (type spécial)
                        SendToHost("Ping", new { });
                    }
                    catch
                    {
                        // Si l'envoi échoue, on marque la connexion comme morte
                        _hostConnection.Disconnected = true;
                    }
                }
            }

            _lastHeartbeatCheck = now;
        }

        // ════════════════════════════════════════════════════════════════
        //  BOUCLE PRINCIPALE (appelée une fois par frame depuis Program.Main)
        // ════════════════════════════════════════════════════════════════

        public static void Update()
        {
            if (!IsOnline) return;

            // Vérifier les heartbeats toutes les 2 secondes environ
            if ((DateTime.UtcNow - _lastHeartbeatCheck).TotalMilliseconds > 2000)
            {
                CheckHeartbeats();
            }

            //  Filet de sécurité : "WorldSync" absent trop longtemps après "Welcome" (host trop
            // ancien, message perdu...) -> on laisse quand même le client jouer, il retombera
            // sur le chargement de chunk à la demande comme avant ce correctif.
            if (_isClient && !_worldSyncApplied && _welcomeReceivedAt > 0
                && Raylib.GetTime() - _welcomeReceivedAt > WORLD_SYNC_TIMEOUT)
            {
                _worldSyncApplied = true;
                Program.OnNetworkWorldSyncComplete();
            }

            // 1. Traiter les déconnexions
            while (_disconnections.TryDequeue(out int discId))
            {
                if (_isHost)
                {
                    if (_clients.TryRemove(discId, out var conn))
                    {
                        VoiceChat.RemovePeer(discId);
                        _remotePlayerStates.TryRemove(discId, out _);
                        //  MULTIJOUEUR : si ce client portait une créature, on la relâche sur
                        // place plutôt que de la laisser suivre indéfiniment sa dernière position.
                        var carried = Program.entities.FirstOrDefault(x => x.CarriedByConnectionId == discId);
                        if (carried != null) carried.CarriedByConnectionId = -1;
                        if (conn.LastTransform != null)
                        {
                            Broadcast("Left", new PlayerLeftMsg { Id = discId, Name = conn.LastTransform.Name });
                            Program.OnNetworkPlayerLeft(discId, conn.LastTransform.Name);
                        }
                    }
                }
                else if (_isClient)
                {
                    if (string.IsNullOrEmpty(LastError)) LastError = "Connexion au host perdue.";
                    Program.OnNetworkHostLost();
                    Disconnect();
                    return;
                }
            }

            // 2. Traiter les messages reçus
            for (int processed = 0;
                processed < MAX_INCOMING_MESSAGES_PER_FRAME && _incoming.TryDequeue(out var msg);
                processed++)
            {
                try { Dispatch(msg); }
                catch (Exception ex)
                {
                    // On log mais on continue pour ne pas planter la boucle
                    LastError = $"Erreur réseau ({msg.Envelope.T}) : {ex.Message}";
                    if (_isClient) ConnectionStatus = LastError;
                }
            }

            if (!IsOnline) return;

            double now = Raylib.GetTime();

            // 3. Envoi périodique de l'état local
            if (now - _lastStateSend >= STATE_SEND_INTERVAL)
            {
                _lastStateSend = now;
                var me = BuildLocalTransform(Program.LocalPlayerName);
                me.Id = _myId;

                if (_isClient)
                {
                    string raw = Encelope("State", me);
                    if (raw != _lastLocalStatePayload)
                    {
                        _lastLocalStatePayload = raw;
                        SendToHostRaw(raw);
                    }
                }
                else if (_isHost)
                {
                    var roster = new List<PlayerTransform> { me };
                    foreach (var kv in _clients)
                    {
                        if (kv.Value.LastTransform != null)
                        {
                            var t = kv.Value.LastTransform;
                            t.Id = kv.Key;
                            roster.Add(t);
                        }
                    }
                    BroadcastIfChanged("Roster", new RosterStateMsg { Players = roster }, ref _lastRosterPayload);
                }
            }

            //  MULTIJOUEUR : sauvegarde périodique du personnage invité auprès du host,
            // pour qu'il retrouve sa position/stats/inventaire/équipement en cas de
            // reconnexion (même après un redémarrage du host, la donnée étant écrite sur
            // le disque du host — voir Program.UpsertNetworkPlayerRecord).
            if (_isClient && now - _lastGuestSaveSend >= GUEST_SAVE_INTERVAL)
            {
                _lastGuestSaveSend = now;
                SendToHost("PlayerSave", new PlayerSaveStateMsg
                {
                    PlayerGuid = Program.LocalPlayerGuid,
                    Data = Program.BuildGuestSaveData()
                });
            }

            if (_isHost && CartesGameUI.IsNetworkSession && now - _lastPouilleuxStateBroadcast >= 0.08)
            {
                _lastPouilleuxStateBroadcast = now;
                Broadcast("PouilleuxState", CartesGameUI.BuildNetworkState());
            }

            // 4. Snapshot périodique des objets au sol (host uniquement)
            if (_isHost && now - _lastGroundItemsBroadcast >= GROUND_ITEMS_INTERVAL)
            {
                _lastGroundItemsBroadcast = now;
                if (!_clients.IsEmpty)
                    BroadcastGroundItemsSnapshot();
            }

            // 5. Snapshot périodique des entités (host uniquement)
            if (_isHost && now - _lastEntityBroadcast >= ENTITY_BROADCAST_INTERVAL)
            {
                _lastEntityBroadcast = now;
                if (!_clients.IsEmpty)
                    BroadcastEntitySnapshot();
            }

            // 6. Sync du temps de jeu
            if (_isHost && now - _lastTimeBroadcast >= TIME_BROADCAST_INTERVAL)
            {
                _lastTimeBroadcast = now;
                Broadcast("Time", new GameTimeMsg { GameTime = Program.GetGameTime() });
            }

            // 6bis. Sync de la météo (l'hôte est seul autoritaire : le temps qu'il décide
            // est celle que tout le monde voit).
            if (_isHost && now - _lastWeatherBroadcast >= WEATHER_BROADCAST_INTERVAL)
            {
                _lastWeatherBroadcast = now;
                Broadcast("Weather", new GameWeatherMsg { WeatherType = Program.GetCurrentWeatherType() });
            }

            // 7. Sync de la grille électrique
            if (_isHost && now - _lastEnergyBroadcast >= ENERGY_BROADCAST_INTERVAL)
            {
                _lastEnergyBroadcast = now;
                if (!_clients.IsEmpty)
                    BroadcastEnergySnapshot();
            }

            // 8. INTERPOLATION : mise à jour des listes interpolées pour les getters publics
            UpdateInterpolatedStates(now);
        }

        // ════════════════════════════════════════════════════════════════
        //  INTERPOLATION
        // ════════════════════════════════════════════════════════════════

        // Interpolation linéaire entre deux états. L'intervalle est l'intervalle d'envoi (STATE_SEND_INTERVAL)
        // pour les joueurs, et ENTITY_BROADCAST_INTERVAL pour les entités.
        private static T InterpolateState<T>(InterpolatedState<T> state, double interval) where T : class, new()
        {
            double elapsed = Raylib.GetTime() - state.ReceiveTime;
            float t = (float)Math.Clamp(elapsed / interval, 0.0, 1.0);
            var result = new T();
            if (typeof(T) == typeof(PlayerTransform))
            {
                var prev = state.Previous as PlayerTransform;
                var curr = state.Current as PlayerTransform;
                var res = result as PlayerTransform;
                // Copier les champs non numériques depuis Current
                res.Id = curr.Id;
                res.Name = curr.Name;
                res.Facing = curr.Facing;
                res.AnimState = curr.AnimState;
                res.AnimFrame = curr.AnimFrame;
                res.AnimProg = curr.AnimProg;
                res.AttackSwingProgress = curr.AttackSwingProgress;
                res.HotbarSlot = curr.HotbarSlot;
                res.Species = curr.Species;
                res.Skin = curr.Skin;
                res.MorphTint = curr.MorphTint;
                res.MorphFeatureVariants = curr.MorphFeatureVariants ?? new();
                res.MorphFeatureColors = curr.MorphFeatureColors ?? new();
                res.Hair = curr.Hair;
                res.HairStyle = curr.HairStyle;
                res.BeardStyle = curr.BeardStyle;
                res.EyeStyle = curr.EyeStyle;
                res.EyeColor = curr.EyeColor ?? new ColorSave { R = 80, G = 120, B = 180, A = 255 };
                res.HP = curr.HP;
                res.MaxHP = curr.MaxHP;
                res.HeldItemId = curr.HeldItemId;
                res.EqHead = curr.EqHead;
                res.EqBody = curr.EqBody;
                res.EqLegs = curr.EqLegs;
                res.Equip = curr.Equip;
                res.Underground = curr.Underground; // ← AJOUTER CETTE LIGNE
                res.PvpEnabled = curr.PvpEnabled;
                // Interpoler les positions
                res.PosX = MathHelper.Lerp(prev.PosX, curr.PosX, t);
                res.PosY = MathHelper.Lerp(prev.PosY, curr.PosY, t);
                res.HeadAngle = MathHelper.Lerp(prev.HeadAngle, curr.HeadAngle, t);
                res.LookX = MathHelper.Lerp(prev.LookX, curr.LookX, t);
                res.LookY = MathHelper.Lerp(prev.LookY, curr.LookY, t);
                return (T)(object)res;
            }
            else if (typeof(T) == typeof(EntityDto))
            {
                var prev = state.Previous as EntityDto;
                var curr = state.Current as EntityDto;
                var res = result as EntityDto;
                // Copier les champs non numériques
                res.NetId = curr.NetId;
                res.Species = curr.Species;
                res.Facing = curr.Facing;
                res.AnimState = curr.AnimState;
                res.AnimFrame = curr.AnimFrame;
                res.AnimProg = curr.AnimProg;
                res.HP = curr.HP;
                res.MaxHP = curr.MaxHP;
                res.Scale = curr.Scale;
                res.DamageFlash = curr.DamageFlash;
                res.IsTamed = curr.IsTamed;
                res.OwnerName = curr.OwnerName;
                res.SlimeStorageSlot = curr.SlimeStorageSlot;
                res.SlimeIncubationSeconds = curr.SlimeIncubationSeconds;
                res.IsSpiritAnimal = curr.IsSpiritAnimal;
                res.SpiritTotemTileX = curr.SpiritTotemTileX;
                res.SpiritTotemTileY = curr.SpiritTotemTileY;
                res.IsHostilePet = curr.IsHostilePet;
                res.IsBlinking = curr.IsBlinking;
                res.CustomName = curr.CustomName;
                res.Tint = curr.Tint;
                res.HairColor = curr.HairColor;
                res.HairStyle = curr.HairStyle;
                res.BeardStyle = curr.BeardStyle;
                res.Equip = curr.Equip;
                //  CORRECTIF : ces champs (quête/commerce/métier) étaient absents d'ici, donc
                // remplacés à chaque frame par les valeurs par défaut d'un EntityDto tout neuf
                // (IsTrader=false, TraderItems vide, QuestState=-1...) au lieu de la vraie
                // donnée reçue de l'hôte. Un client voyait donc TOUJOURS "rien à proposer",
                // même quand l'hôte affichait une quête ou un commerce disponible.
                res.IsCarried = curr.IsCarried;
                res.FirstName = curr.FirstName;
                res.IsTrader = curr.IsTrader;
                res.TraderItems = curr.TraderItems;
                res.Profession = curr.Profession;
                res.Friendship = curr.Friendship;
                res.IsGuildMember = curr.IsGuildMember;
                res.WantsToJoinGuild = curr.WantsToJoinGuild;
                res.QuestId = curr.QuestId;
                res.QuestTargetNetId = curr.QuestTargetNetId;
                res.QuestItemId = curr.QuestItemId;
                res.QuestItemName = curr.QuestItemName;
                res.QuestItemQty = curr.QuestItemQty;
                res.QuestRewardItemId = curr.QuestRewardItemId;
                res.QuestRewardItemName = curr.QuestRewardItemName;
                res.QuestRewardItemQty = curr.QuestRewardItemQty;
                res.QuestState = curr.QuestState;
                // Interpoler les positions
                res.PosX = MathHelper.Lerp(prev.PosX, curr.PosX, t);
                res.PosY = MathHelper.Lerp(prev.PosY, curr.PosY, t);
                return (T)(object)res;
            }
            else
            {
                // Fallback : retourner Current
                return state.Current;
            }
        }

        private static void UpdateInterpolatedStates(double now)
        {
            // Pour les joueurs distants
            var playerList = new List<PlayerTransform>();
            foreach (var kv in _remotePlayerStates)
            {
                var state = kv.Value;
                // Si on n'a reçu qu'un seul état, on le répète
                if (state.ReceiveTime == 0) continue;
                var interpolated = InterpolateState(state, STATE_SEND_INTERVAL);
                playerList.Add(interpolated);
            }
            // Mise à jour de la liste interpolée pour les joueurs (utilisée par GetRemotePlayersAsLocalPlayers)
            // On pourrait stocker cette liste dans un champ, mais on la recrée à chaque appel.

            // Pour les entités
            var entityList = new List<EntityDto>();
            foreach (var kv in _remoteEntityStates)
            {
                var state = kv.Value;
                if (state.ReceiveTime == 0) continue;
                var interpolated = InterpolateState(state, ENTITY_BROADCAST_INTERVAL);
                entityList.Add(interpolated);
            }
            _remoteEntitiesInterpolated = entityList;
            _remoteEntitiesDirty = true; // pour signaler changement
        }

        // ════════════════════════════════════════════════════════════════
        //  MÉTHODES DE CONSTRUCTION DE L'ÉTAT LOCAL
        // ════════════════════════════════════════════════════════════════

        private static PlayerTransform BuildLocalTransform(string name)
        {
            var lp = Program.GetLocalPlayers().FirstOrDefault(p => p.Id == 0);
            var t = new PlayerTransform
            {
                Id = _myId,
                Name = string.IsNullOrWhiteSpace(name) ? "Joueur" : name,
                Species = Program.PlayerMorphSpecies,
                MorphTint = new ColorSave { R = Program.PlayerMorphTint.R, G = Program.PlayerMorphTint.G, B = Program.PlayerMorphTint.B, A = Program.PlayerMorphTint.A },
                MorphFeatureVariants = new Dictionary<string, int>(Program.PlayerMorphFeatureVariants),
                MorphFeatureColors = Program.PlayerMorphFeatureColors.ToDictionary(
                    kv => kv.Key,
                    kv => new ColorSave { R = kv.Value.R, G = kv.Value.G, B = kv.Value.B, A = kv.Value.A }),
                HairStyle = Program.PlayerHairStyle,
                BeardStyle = Program.PlayerBeardStyle,
                EyeStyle = Program.EyeStyle,
                EyeColor = new ColorSave { R = Program.EyeColor.R, G = Program.EyeColor.G, B = Program.EyeColor.B, A = Program.EyeColor.A },
                PlayerGuid = Program.LocalPlayerGuid,
                HeadAngle = Program.GetHeadAngle(),
                LookX = Program.GetCursorWorldPos().X,
                LookY = Program.GetCursorWorldPos().Y,
                Skin = new ColorSave { R = Program.SkinColor.R, G = Program.SkinColor.G, B = Program.SkinColor.B, A = Program.SkinColor.A },
                Hair = new ColorSave { R = Program.HairColor.R, G = Program.HairColor.G, B = Program.HairColor.B, A = Program.HairColor.A },
                HP = Program.playerHP,
                MaxHP = Program.playerMaxHP,
                Underground = World.IsUnderground,
                PvpEnabled = Program.LocalPvpEnabled
            };

            var slots = InventoryRenderer.InventorySlots;
            if (slots != null && Program.hotbarSlot < slots.Count && !slots[Program.hotbarSlot].IsEmpty)
                t.HeldItemId = Program.GetItemId(slots[Program.hotbarSlot].Item!.Name);

            // Anciens champs pour compatibilité
            if (Program.equipment.Head != null) t.EqHead = Program.GetItemId(Program.equipment.Head.Name);
            if (Program.equipment.Body != null) t.EqBody = Program.GetItemId(Program.equipment.Body.Name);
            if (Program.equipment.Legs != null) t.EqLegs = Program.GetItemId(Program.equipment.Legs.Name);

            //  NOUVEAU : remplir EquipData complet
            t.Equip = BuildEquipDataFromEquipment(Program.equipment);

            if (lp != null)
            {
                t.PosX = lp.Position.X;
                t.PosY = lp.Position.Y;
                t.Facing = lp.Facing;
                t.AnimState = lp.AnimState;
                t.AnimFrame = lp.AnimFrame;
                t.AnimProg = lp.AnimProg;
                t.AttackSwingProgress = Program.GetAttackSwingProgress();
                t.HotbarSlot = lp.HotbarSlot;
            }
            else
            {
                var pos = Program.GetPlayerPosition();
                t.PosX = pos.X;
                t.PosY = pos.Y;
            }
            return t;
        }

        /// <summary>
        /// Construit un EquipData à partir d'un objet Equipment.
        /// </summary>
        private static EquipData BuildEquipDataFromEquipment(Equipment eq)
        {
            var data = new EquipData();

            // Zones du corps (propriétés existantes)
            data.Head = GetItemId(eq.Head?.Name);
            data.Body = GetItemId(eq.Body?.Name);
            data.Legs = GetItemId(eq.Legs?.Name);
            // Zones supplémentaires via GetZoneItem
            data.Back = GetItemId(eq.GetZoneItem(BodyZone.Back)?.Name);
            data.Face = GetItemId(eq.GetZoneItem(BodyZone.Face)?.Name);
            data.Neck = GetItemId(eq.GetZoneItem(BodyZone.Neck)?.Name);
            data.Waist = GetItemId(eq.GetZoneItem(BodyZone.Waist)?.Name);
            data.Ears = GetItemId(eq.GetZoneItem(BodyZone.Ears)?.Name);
            data.Feet = GetItemId(eq.GetZoneItem(BodyZone.Feet)?.Name);

            // Slots fonctionnels
            data.MainHand = GetItemId(eq.MainHand?.Name);
            data.OffHand = GetItemId(eq.OffHand?.Name);
            data.Backpack = GetItemId(eq.Backpack?.Name);

            // Couleurs personnalisées
            data.HeadColors = ColorsToSave(eq.Head?.CustomColors);
            data.BodyColors = ColorsToSave(eq.Body?.CustomColors);
            data.LegsColors = ColorsToSave(eq.Legs?.CustomColors);
            data.BackColors = ColorsToSave(eq.GetZoneItem(BodyZone.Back)?.CustomColors);
            data.FaceColors = ColorsToSave(eq.GetZoneItem(BodyZone.Face)?.CustomColors);
            data.NeckColors = ColorsToSave(eq.GetZoneItem(BodyZone.Neck)?.CustomColors);
            data.WaistColors = ColorsToSave(eq.GetZoneItem(BodyZone.Waist)?.CustomColors);
            data.EarsColors = ColorsToSave(eq.GetZoneItem(BodyZone.Ears)?.CustomColors);
            data.FeetColors = ColorsToSave(eq.GetZoneItem(BodyZone.Feet)?.CustomColors);
            data.MainHandColors = ColorsToSave(eq.MainHand?.CustomColors);
            data.OffHandColors = ColorsToSave(eq.OffHand?.CustomColors);
            data.BackpackColors = ColorsToSave(eq.Backpack?.CustomColors);

            return data;
        }

        private static List<ColorSave?> ColorsToSave(List<Color?>? colors)
        {
            if (colors == null) return new List<ColorSave?>();
            return colors.Select(c => c.HasValue ? new ColorSave { R = c.Value.R, G = c.Value.G, B = c.Value.B, A = c.Value.A } : (ColorSave?)null).ToList();
        }

        private static int GetItemId(string? name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            return Program.GetItemId(name);
        }

        // ════════════════════════════════════════════════════════════════
        //  CONVERSION EQUIPDATA -> EQUIPMENT (pour le rendu client)
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Construit un objet Equipment à partir d'un EquipData reçu du réseau.
        /// </summary>
        public static Equipment BuildEquipmentFromData(EquipData data)
        {
            var eq = new Equipment();

            if (data == null) return eq;

            // Fonction utilitaire pour créer un Item à partir d'un ID et d'une liste de couleurs
            Item? CreateItem(int id, List<ColorSave?>? colorSaves)
            {
                if (id == 0) return null;
                if (!GameData.ItemDatabase.TryGetValue(id, out var itemData)) return null;
                var item = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
                if (colorSaves != null && colorSaves.Count > 0)
                {
                    var colors = colorSaves.Select(c => c != null ? (Color?)new Color(c.R, c.G, c.B, c.A) : null).ToList();
                    item.CustomColors = colors;
                }
                if (itemData.RuneSlots > 0)
                {
                    item.EnsureRuneSlots(itemData.RuneSlots);
                    item.LoadRunesFromMetadata();
                }
                return item;
            }

            // Zones du corps (via EquipOnBody)
            var head = CreateItem(data.Head, data.HeadColors);
            var body = CreateItem(data.Body, data.BodyColors);
            var legs = CreateItem(data.Legs, data.LegsColors);
            var back = CreateItem(data.Back, data.BackColors);
            var face = CreateItem(data.Face, data.FaceColors);
            var neck = CreateItem(data.Neck, data.NeckColors);
            var waist = CreateItem(data.Waist, data.WaistColors);
            var ears = CreateItem(data.Ears, data.EarsColors);
            var feet = CreateItem(data.Feet, data.FeetColors);

            if (head != null) eq.EquipOnBody(head, out _);
            if (body != null) eq.EquipOnBody(body, out _);
            if (legs != null) eq.EquipOnBody(legs, out _);
            if (back != null) eq.EquipOnBody(back, out _);
            if (face != null) eq.EquipOnBody(face, out _);
            if (neck != null) eq.EquipOnBody(neck, out _);
            if (waist != null) eq.EquipOnBody(waist, out _);
            if (ears != null) eq.EquipOnBody(ears, out _);
            if (feet != null) eq.EquipOnBody(feet, out _);

            // Slots fonctionnels
            eq.MainHand = CreateItem(data.MainHand, data.MainHandColors);
            eq.OffHand = CreateItem(data.OffHand, data.OffHandColors);
            // Pour le sac à dos, on utilise EquipItem avec le slot Backpack
            var backpack = CreateItem(data.Backpack, data.BackpackColors);
            if (backpack != null) eq.EquipItem(backpack, EquipmentSlot.Backpack);

            eq.LoadEquipmentTextures();
            return eq;
        }

        // ════════════════════════════════════════════════════════════════
        //  DISPATCH DES MESSAGES REÇUS
        // ════════════════════════════════════════════════════════════════

        private static void Dispatch(IncomingMessage msg)
        {
            switch (msg.Envelope.T)
            {
                // Gestion du Ping (pour le heartbeat côté client)
                case "Ping":
                    // Répondre avec un Pong
                    if (_isHost)
                    {
                        SendTo(msg.ConnectionId, "Pong", new { });
                    }
                    else if (_isClient)
                    {
                        // Les clients ne traitent pas les Ping, ils répondent via le host
                    }
                    break;

                case "Pong":
                    // Rien à faire, le heartbeat est reçu
                    if (_hostConnection != null)
                    {
                        _hostConnection.LastActivity = DateTime.UtcNow;
                    }
                    break;

                case "Hello":
                    if (_isHost) HandleHello(msg.ConnectionId, Deserialize<PlayerTransform>(msg.Envelope.J));
                    break;
                case "WorldInfoRequest":
                    if (_isHost)
                    {
                        var request = Deserialize<WorldInfoRequestMsg>(msg.Envelope.J);
                        var existingGuids = request.CandidateGuids
                            .Where(guid => !string.IsNullOrWhiteSpace(guid) && Program.TryGetNetworkPlayerRecord(guid) != null)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        SendTo(msg.ConnectionId, "WorldInfo", new WorldInfoMsg
                        {
                            WorldName = World.CurrentSaveName,
                            ExistingCharacterGuids = existingGuids
                        });
                    }
                    break;
                case "Welcome":
                    if (_isClient) HandleWelcome(Deserialize<WelcomeMsg>(msg.Envelope.J));
                    break;
                case "Joined":
                    if (_isClient) HandleJoined(Deserialize<PlayerTransform>(msg.Envelope.J));
                    break;
                case "Left":
                    if (_isClient)
                    {
                        var left = Deserialize<PlayerLeftMsg>(msg.Envelope.J);
                        _remotePlayerStates.TryRemove(left.Id, out _);
                        Program.OnNetworkPlayerLeft(left.Id, left.Name);
                    }
                    break;
                case "Teleport":
                    if (_isClient)
                    {
                        var tp = Deserialize<TeleportMsg>(msg.Envelope.J);
                        Program.SetPlayerPosition(new Vector2(tp.X, tp.Y));
                    }
                    break;
                case "Kick":
                    if (_isClient)
                    {
                        Program.AddNotification(new Notification("Vous avez été expulsé par l'hôte.", new Color(255, 100, 100, 255), 3f));
                        Disconnect();
                    }
                    break;
                case "PouilleuxInviteReq":
                    if (_isHost)
                        HandlePouilleuxInviteRequest(msg.ConnectionId, Deserialize<PouilleuxInviteRequestMsg>(msg.Envelope.J));
                    break;
                case "PouilleuxInvite":
                    if (_isClient || _isHost)
                    {
                        var invitation = Deserialize<PouilleuxInvitationMsg>(msg.Envelope.J);
                        Program.ReceivePouilleuxInvitation(invitation);
                    }
                    break;
                case "PouilleuxInviteResult":
                    if (_isClient)
                    {
                        var result = Deserialize<PouilleuxInviteResultMsg>(msg.Envelope.J);
                        Program.AddNotification(new Notification(result.Message, result.Success
                            ? new Color(120, 230, 150, 255)
                            : new Color(230, 130, 130, 255), 3f));
                    }
                    break;
                case "PouilleuxInviteResponse":
                    if (_isHost)
                        HandlePouilleuxInviteResponse(msg.ConnectionId, Deserialize<PouilleuxInviteResponseMsg>(msg.Envelope.J));
                    break;
                case "PouilleuxStart":
                    if (_isClient)
                        Program.ReceivePouilleuxStart(Deserialize<PouilleuxStartMsg>(msg.Envelope.J));
                    break;
                case "PouilleuxState":
                    if (_isClient)
                        CartesGameUI.ApplyNetworkState(Deserialize<PouilleuxStateMsg>(msg.Envelope.J));
                    break;
                case "PouilleuxDrawReq":
                    if (_isHost)
                        HandlePouilleuxDrawRequest(msg.ConnectionId, Deserialize<PouilleuxDrawRequestMsg>(msg.Envelope.J));
                    break;
                case "PouilleuxStartReq":
                    if (_isHost)
                        CartesGameUI.HandleNetworkStartRequest(msg.ConnectionId, Deserialize<PouilleuxStartRequestMsg>(msg.Envelope.J));
                    break;
                case "PvpHitReq":
                    if (_isHost)
                    {
                        var pvpReq = Deserialize<PvpHitReqMsg>(msg.Envelope.J);
                        int attackerConnId = msg.ConnectionId;
                        //  Le host revalide que les DEUX joueurs ont bien le PVP activé avant
                        // d'appliquer les dégâts, pour ne pas se fier uniquement au client.
                        if (pvpReq.Damage > 0 && GetPvpEnabledFor(attackerConnId) && GetPvpEnabledFor(pvpReq.TargetId))
                        {
                            Program.DamagePlayerTarget(pvpReq.Damage, pvpReq.TargetId == 0 ? -1 : pvpReq.TargetId);
                        }
                    }
                    break;
                case "State":
                    if (_isHost)
                    {
                        var t = Deserialize<PlayerTransform>(msg.Envelope.J);
                        t.Id = msg.ConnectionId;
                        if (_clients.TryGetValue(msg.ConnectionId, out var c)) c.LastTransform = t;

                        // === AJOUT : mise à jour de l'état interpolé pour l'hôte ===
                        var state = _remotePlayerStates.GetOrAdd(msg.ConnectionId, _ => new InterpolatedState<PlayerTransform>());
                        state.Previous = state.Current ?? t;
                        state.Current = t;
                        state.ReceiveTime = Raylib.GetTime();
                    }
                    break;
                case "Roster":
                    if (_isClient)
                    {
                        var roster = Deserialize<RosterStateMsg>(msg.Envelope.J);
                        foreach (var p in roster.Players)
                        {
                            if (p.Id == _myId) continue;
                            var state = _remotePlayerStates.GetOrAdd(p.Id, _ => new InterpolatedState<PlayerTransform>());
                            // Mise à jour de l'état interpolé
                            state.Previous = state.Current ?? p;
                            state.Current = p;
                            state.ReceiveTime = Raylib.GetTime();
                        }
                        // Supprimer les joueurs qui ne sont plus dans le roster
                        var currentIds = roster.Players.Select(p => p.Id).ToHashSet();
                        foreach (var kv in _remotePlayerStates.ToList())
                        {
                            if (!currentIds.Contains(kv.Key))
                                _remotePlayerStates.TryRemove(kv.Key, out _);
                        }
                    }
                    break;
                case "Chat":
                    {
                        var chat = Deserialize<ChatRelayMsg>(msg.Envelope.J);
                        if (_isHost)
                        {
                            ChatSystem.AddNetworkMessage(chat.Name, chat.Text);
                            Broadcast("Chat", chat, exceptConnectionId: msg.ConnectionId);
                        }
                        else
                        {
                            ChatSystem.AddNetworkMessage(chat.Name, chat.Text);
                        }
                    }
                    break;
                case "ChunkReq":
                    if (_isHost) HandleChunkRequest(msg.ConnectionId, Deserialize<ChunkRequestMsg>(msg.Envelope.J));
                    break;
                case "ChunkData":
                    if (_isClient) HandleChunkData(Deserialize<ChunkDataMsg>(msg.Envelope.J));
                    break;
                case "WorldAction":
                    HandleWorldAction(msg.ConnectionId, Deserialize<WorldActionMsg>(msg.Envelope.J));
                    break;
                case "TileMetaAction":
                    HandleTileMetaAction(msg.ConnectionId, Deserialize<TileMetaActionMsg>(msg.Envelope.J));
                    break;
                case "Items":
                    if (_isClient) ApplyGroundItemsSnapshot(Deserialize<GroundItemsSnapshotMsg>(msg.Envelope.J));
                    break;
                case "WorldSync":
                    if (_isClient) HandleWorldSync(Deserialize<WorldSyncMsg>(msg.Envelope.J));
                    break;
                case "Pickup":
                    if (_isHost) HandlePickupRequest(msg.ConnectionId, Deserialize<PickupRequestMsg>(msg.Envelope.J));
                    break;
                case "ItemDropReq":
                    if (_isHost) HandleItemDropRequest(Deserialize<ItemDropRequestMsg>(msg.Envelope.J));
                    break;
                case "LootbagDropReq":
                    if (_isHost) HandleLootbagDropRequest(Deserialize<LootbagDropRequestMsg>(msg.Envelope.J));
                    break;
                case "EnergyAction":
                    if (_isHost) HandleEnergyAction(Deserialize<EnergyActionMsg>(msg.Envelope.J));
                    break;
                case "EnergySnapshot":
                    if (_isClient) ApplyEnergySnapshot(Deserialize<EnergySnapshotMsg>(msg.Envelope.J));
                    break;
                case "Grant":
                    if (_isClient)
                    {
                        var grant = Deserialize<ItemGrantMsg>(msg.Envelope.J);
                        Program.ApplyNetworkItemGrant(grant);
                    }
                    break;

                // ---- Synchronisation des entités ----
                case "Entities":
                    if (_isClient) ApplyEntitySnapshot(Deserialize<EntitySnapshotMsg>(msg.Envelope.J));
                    break;
                case "EntityHit":
                    if (_isHost) HandleEntityHit(msg.ConnectionId, Deserialize<EntityHitMsg>(msg.Envelope.J));
                    break;
                case "PickupCreatureReq":
                    if (_isHost) HandlePickupCreatureRequest(msg.ConnectionId, Deserialize<PickupCreatureRequestMsg>(msg.Envelope.J));
                    break;
                case "DropCreatureReq":
                    if (_isHost) HandleDropCreatureRequest(msg.ConnectionId, Deserialize<DropCreatureRequestMsg>(msg.Envelope.J));
                    break;
                case "TameAnimalReq":
                    if (_isHost) HandleTameAnimalRequest(msg.ConnectionId, Deserialize<TameAnimalRequestMsg>(msg.Envelope.J));
                    break;
                case "SlimeStorageReq":
                    if (_isHost) HandleSlimeStorageRequest(msg.ConnectionId, Deserialize<SlimeStorageRequestMsg>(msg.Envelope.J));
                    break;
                case "TameResult":
                    if (_isClient) Program.ApplyNetworkTameResult(Deserialize<TameAnimalResultMsg>(msg.Envelope.J));
                    break;
                case "EntityDeath":
                    if (_isClient)
                    {
                        var death = Deserialize<EntityDeathMsg>(msg.Envelope.J);
                        _remoteEntityStates.TryRemove(death.NetId, out _);
                        Program.OnNetworkEntityDeath(death.NetId);
                    }
                    break;
                case "PlayerDamage":
                    if (_isClient)
                    {
                        var dmg = Deserialize<PlayerDamageMsg>(msg.Envelope.J);
                        Program.DamagePlayer(dmg.Amount);
                        if (dmg.PoisonDuration > 0f)
                            Program.ApplyPlayerPoison(dmg.PoisonDuration);
                    }
                    break;
                case "Time":
                    if (_isClient)
                    {
                        var tm = Deserialize<GameTimeMsg>(msg.Envelope.J);
                        Program.ApplyNetworkGameTime(tm.GameTime);
                    }
                    break;
                case "Weather":
                    if (_isClient)
                    {
                        var wt = Deserialize<GameWeatherMsg>(msg.Envelope.J);
                        Program.ApplyNetworkWeather(wt.WeatherType);
                    }
                    break;

                // ---- Conteneurs ----
                case "QuestActionReq":
                    if (_isHost) HandleQuestActionRequest(msg.ConnectionId, Deserialize<QuestActionRequestMsg>(msg.Envelope.J));
                    break;
                case "QuestActionResult":
                    if (_isClient) Program.ApplyNetworkQuestActionResult(Deserialize<QuestActionResultMsg>(msg.Envelope.J));
                    break;
                case "TraderPurchaseReq":
                    if (_isHost) HandleTraderPurchaseRequest(msg.ConnectionId, Deserialize<TraderPurchaseRequestMsg>(msg.Envelope.J));
                    break;
                case "ContainerReq":
                    if (_isHost) HandleContainerRequest(msg.ConnectionId, Deserialize<ContainerRequestMsg>(msg.Envelope.J));
                    break;
                case "ContainerSync":
                    // Si c'est un client, on applique la mise à jour (reçue du host)
                    if (_isClient)
                    {
                        Program.ApplyNetworkContainerSync(Deserialize<ContainerSyncMsg>(msg.Envelope.J));
                    }
                    else if (_isHost)
                    {
                        // Le host applique localement et diffuse à tous les autres clients
                        var cs = Deserialize<ContainerSyncMsg>(msg.Envelope.J);
                        Program.ApplyNetworkContainerSync(cs);
                        Broadcast("ContainerSync", cs, exceptConnectionId: msg.ConnectionId);
                    }
                    break;

                // ---- Personnage invité (persistance entre connexions) ----
                case "PlayerSave":
                    if (_isHost)
                    {
                        var psm = Deserialize<PlayerSaveStateMsg>(msg.Envelope.J);
                        Program.UpsertNetworkPlayerRecord(psm.PlayerGuid, psm.Data);
                    }
                    break;
            }
        }

        private static T Deserialize<T>(string json) where T : new()
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? new T();
            }
            catch (JsonException)
            {
                return new T();
            }
        }

        // ---- Handshake côté host ----
        private static void HandleHello(int connectionId, PlayerTransform hello)
        {
            hello.Id = connectionId;
            string voiceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            //  MULTIJOUEUR : ce joueur a-t-il déjà un personnage sauvegardé sur ce monde ?
            var restore = Program.TryGetNetworkPlayerRecord(hello.PlayerGuid);
            if (restore != null)
            {
                // On corrige tout de suite la position/l'apparence connues du host, pour que
                // le roster envoyé aux autres clients (et à ce client) soit cohérent dès la
                // première frame, avant même que ce client renvoie son propre état confirmé.
                hello.PosX = restore.PosX;
                hello.PosY = restore.PosY;
                hello.Facing = restore.Facing;
                hello.HP = restore.HP;
                hello.MaxHP = restore.MaxHP;
                hello.Species = restore.Species;
                hello.Skin = restore.Skin;
                hello.Hair = restore.Hair;
                hello.HairStyle = restore.HairStyle;
                hello.BeardStyle = restore.BeardStyle;
                hello.Equip = NetworkManagerEquipDataFromSave(restore.Equipment);
            }

            if (_clients.TryGetValue(connectionId, out var conn))
            {
                conn.LastTransform = hello;
                conn.VoiceToken = voiceToken;
            }

            // === AJOUT : initialiser l'état interpolé pour ce nouveau client ===
            var state = _remotePlayerStates.GetOrAdd(connectionId, _ => new InterpolatedState<PlayerTransform>());
            state.Current = hello;
            state.Previous = hello;
            state.ReceiveTime = Raylib.GetTime();

            var roster = new List<PlayerTransform> { BuildLocalTransform(Program.LocalPlayerName) };
            foreach (var kv in _clients)
            {
                if (kv.Key == connectionId) continue;
                if (kv.Value.LastTransform != null)
                    roster.Add(kv.Value.LastTransform);
            }

            SendTo(connectionId, "Welcome", new WelcomeMsg
            {
                YourId = connectionId,
                VoicePort = _hostPort,
                VoiceToken = voiceToken,
                WorldSeed = Program.WorldSeed,
                WorldName = World.CurrentSaveName,
                GameTime = Program.GetGameTime(),
                WeatherType = Program.GetCurrentWeatherType(),
                Roster = roster,
                Restore = restore
            });

            SendInitialWorldSync(connectionId, restore, roster[0]);

            Broadcast("Joined", hello, exceptConnectionId: connectionId);
            Program.OnNetworkPlayerJoined(connectionId, hello.Name);
        }

        //  MULTIJOUEUR : construit le paquet de synchronisation initiale envoyé juste après
        // "Welcome" à un client qui rejoint. Reprend le même point d'apparition que celui déjà
        // utilisé côté client en secours (Program.OnNetworkWelcomeReceived) : le personnage
        // restauré si ce joueur est déjà connu de ce monde, sinon un point proche de l'hôte.
        private static void SendInitialWorldSync(int connectionId, GuestPlayerSaveData? restore, PlayerTransform hostTransform)
        {
            float spawnX = restore?.PosX ?? (hostTransform.PosX + 60);
            float spawnY = restore?.PosY ?? (hostTransform.PosY + 60);
            bool underground = restore?.Underground ?? false;

            int ts = Program.TileSize;
            int spawnChunkX = (int)Math.Floor(spawnX / (World.CHUNK_SIZE * ts));
            int spawnChunkY = (int)Math.Floor(spawnY / (World.CHUNK_SIZE * ts));
            float now = (float)Raylib.GetTime();

            for (int x = spawnChunkX - INITIAL_SYNC_CHUNK_RADIUS; x <= spawnChunkX + INITIAL_SYNC_CHUNK_RADIUS; x++)
            {
                for (int y = spawnChunkY - INITIAL_SYNC_CHUNK_RADIUS; y <= spawnChunkY + INITIAL_SYNC_CHUNK_RADIUS; y++)
                {
                    World.EnsureChunkLoadedForNetwork(x, y, underground, now, Program.entities);
                    var data = SaveSystem.BuildChunkSaveDataForNetwork(x, y, underground);
                    if (data != null)
                    {
                        SendTo(connectionId, "WorldSync", new WorldSyncMsg
                        {
                            Chunks = { new ChunkDataMsg { ChunkX = x, ChunkY = y, Underground = underground, Data = data } }
                        });
                    }
                }
            }

            SendTo(connectionId, "WorldSync", new WorldSyncMsg
            {
                Entities = BuildEntitySnapshot(),
                GroundItems = BuildGroundItemsSnapshot(),
                Energy = BuildEnergySnapshot(),
                IsFinalPart = true
            });
        }

        //  Convertit un EquipmentSave (format des sauvegardes) en EquipData (format réseau),
        // pour que le roster initial affiche déjà le bon équipement à un joueur qui revient.
        private static EquipData NetworkManagerEquipDataFromSave(EquipmentSave save)
        {
            var eq = new Equipment();
            SaveSystem.RestoreEquipmentFromSave(eq, save);
            return BuildEquipDataFromEquipment(eq);
        }

        // ---- Handshake côté client ----
        private static void HandleWelcome(WelcomeMsg welcome)
        {
            _myId = welcome.YourId;
            AmIHost = (_myId == 0);
            if (welcome.VoicePort > 0 && !string.IsNullOrEmpty(welcome.VoiceToken)
                && _hostConnection?.Client.Client.RemoteEndPoint is IPEndPoint hostEndpoint)
            {
                IPAddress hostAddress = hostEndpoint.Address.IsIPv4MappedToIPv6
                    ? hostEndpoint.Address.MapToIPv4()
                    : hostEndpoint.Address;
                VoiceChat.StartClient(hostAddress, welcome.VoicePort, welcome.YourId, welcome.VoiceToken);
            }
            _remotePlayerStates.Clear();
            foreach (var p in welcome.Roster)
            {
                if (p.Id == _myId) continue;
                var state = new InterpolatedState<PlayerTransform> { Current = p, Previous = p, ReceiveTime = Raylib.GetTime() };
                _remotePlayerStates[p.Id] = state;
            }
            ConnectionStatus = "Monde reçu, chargement...";
            _welcomeReceivedAt = Raylib.GetTime();
            _worldSyncApplied = false;
            Program.OnNetworkWelcomeReceived(welcome);
        }

        //  MULTIJOUEUR : reçu une seule fois côté client, juste après "Welcome". Applique
        // d'un coup tous les chunks/entités/objets/énergie initiaux, puis seulement ensuite
        // fait passer le client en jeu (voir Program.OnNetworkWorldSyncComplete) — plutôt que
        // de le laisser jouer immédiatement dans un monde encore vide pendant qu'il attend les
        // premières diffusions périodiques et ses requêtes de chunk une par une.
        private static void HandleWorldSync(WorldSyncMsg sync)
        {
            foreach (var chunk in sync.Chunks)
                World.ReceiveChunkFromNetwork(chunk.ChunkX, chunk.ChunkY, chunk.Data, chunk.Underground);

            if (!sync.IsFinalPart) return;

            ApplyEntitySnapshot(sync.Entities);
            ApplyGroundItemsSnapshot(sync.GroundItems);
            ApplyEnergySnapshot(sync.Energy);

            _worldSyncApplied = true;
            ConnectionStatus = "Monde reçu, chargement...";
            Program.OnNetworkWorldSyncComplete();
        }

        private static void HandleJoined(PlayerTransform p)
        {
            if (p.Id == _myId) return;
            var state = new InterpolatedState<PlayerTransform> { Current = p, Previous = p, ReceiveTime = Raylib.GetTime() };
            _remotePlayerStates[p.Id] = state;
            Program.OnNetworkPlayerJoined(p.Id, p.Name);
        }

        // ---- Chunks ----
        private static void HandleChunkRequest(int connectionId, ChunkRequestMsg req)
        {
            World.EnsureChunkLoadedForNetwork(req.ChunkX, req.ChunkY, req.Underground, (float)Raylib.GetTime(), Program.entities);
            var data = SaveSystem.BuildChunkSaveDataForNetwork(req.ChunkX, req.ChunkY, req.Underground);
            if (data != null)
            {
                SendTo(connectionId, "ChunkData", new ChunkDataMsg
                {
                    ChunkX = req.ChunkX,
                    ChunkY = req.ChunkY,
                    Underground = req.Underground,
                    Data = data
                });
            }
        }

        private static void HandleChunkData(ChunkDataMsg msg)
        {
            _pendingChunkRequests.Remove((msg.ChunkX, msg.ChunkY, msg.Underground));
            World.ReceiveChunkFromNetwork(msg.ChunkX, msg.ChunkY, msg.Data, msg.Underground);
        }

        public static void RequestChunk(int chunkX, int chunkY, bool underground)
        {
            if (!_isClient) return;
            var key = (chunkX, chunkY, underground);
            lock (_pendingChunkRequests)
            {
                if (_pendingChunkRequests.Contains(key)) return;
                _pendingChunkRequests.Add(key);
            }
            SendToHost("ChunkReq", new ChunkRequestMsg { ChunkX = chunkX, ChunkY = chunkY, Underground = underground });
        }

        // ---- Actions sur le monde ----
        public static void RequestWorldAction(string action, int x, int y, int value, bool underground)
        {
            SendToHost("WorldAction", new WorldActionMsg { Action = action, X = x, Y = y, Value = value, Underground = underground });
        }

        public static void BroadcastWorldAction(string action, int x, int y, int value, bool underground)
        {
            if (!_isHost) return;
            Broadcast("WorldAction", new WorldActionMsg { Action = action, X = x, Y = y, Value = value, Underground = underground });
        }

        private static void HandleWorldAction(int connectionId, WorldActionMsg action)
        {
            if (_isHost)
            {
                World.ApplyWorldActionFromNetwork(action);
                Broadcast("WorldAction", action);
            }
            else if (_isClient)
            {
                World.ApplyWorldActionFromNetwork(action);
            }
        }

        // ---- Métadonnées de tuile (générique) ----
        public static void RequestTileMetaAction(int x, int y, string key, string value, bool underground)
        {
            SendToHost("TileMetaAction", new TileMetaActionMsg { X = x, Y = y, Key = key, Value = value, Underground = underground });
        }

        public static void BroadcastTileMetaAction(int x, int y, string key, string value, bool underground)
        {
            if (!_isHost) return;
            Broadcast("TileMetaAction", new TileMetaActionMsg { X = x, Y = y, Key = key, Value = value, Underground = underground });
        }

        private static void HandleTileMetaAction(int connectionId, TileMetaActionMsg action)
        {
            if (_isHost)
            {
                World.ApplyTileMetaActionFromNetwork(action);
                Broadcast("TileMetaAction", action);
            }
            else if (_isClient)
            {
                World.ApplyTileMetaActionFromNetwork(action);
            }
        }

        // ---- Grille électrique ----
        public static void RequestEnergyAction(string action, int x, int y, int x2 = 0, int y2 = 0, float water = 0, float coal = 0)
        {
            if (!_isClient) return;
            SendToHost("EnergyAction", new EnergyActionMsg { Action = action, X = x, Y = y, X2 = x2, Y2 = y2, Water = water, Coal = coal });
        }

        private static void HandleEnergyAction(EnergyActionMsg msg)
        {
            if (!_isHost) return;
            switch (msg.Action)
            {
                case "ConnectWire":
                    World.ConnectEnergyWire(msg.X, msg.Y, msg.X2, msg.Y2);
                    break;
                case "ToggleEngine":
                    World.ToggleSteamEngine(msg.X, msg.Y);
                    break;
                case "AddFuel":
                    World.AddSteamEngineFuel(msg.X, msg.Y, msg.Water, msg.Coal);
                    break;
            }
            BroadcastEnergySnapshot();
        }

        private static EnergySnapshotMsg BuildEnergySnapshot()
        {
            var msg = new EnergySnapshotMsg();
            foreach (var kv in World.SteamEngines)
                msg.Engines.Add(new SteamEngineDto { X = kv.Key.x, Y = kv.Key.y, IsOn = kv.Value.IsOn, Water = kv.Value.Water, Coal = kv.Value.Coal, SteamPressure = kv.Value.SteamPressure });
            foreach (var kv in World.Dynamos)
                msg.Dynamos.Add(new DynamoDto { X = kv.Key.x, Y = kv.Key.y, ElectricityOutput = kv.Value.ElectricityOutput });
            foreach (var kv in World.Batteries)
                msg.Batteries.Add(new BatteryDto { X = kv.Key.x, Y = kv.Key.y, StoredEnergy = kv.Value.StoredEnergy, MaxCapacity = kv.Value.MaxCapacity });
            foreach (var kv in World.Ampoules)
                msg.Ampoules.Add(new AmpouleDto { X = kv.Key.x, Y = kv.Key.y, IsOn = kv.Value.IsOn });
            foreach (var c in World.EnergyConnections)
                msg.Connections.Add(new EnergyConnectionDto { X1 = c.X1, Y1 = c.Y1, X2 = c.X2, Y2 = c.Y2 });
            return msg;
        }

        public static void BroadcastEnergySnapshot()
        {
            var raw = Encelope("EnergySnapshot", BuildEnergySnapshot());
            if (raw == _lastEnergyPayload) return;
            _lastEnergyPayload = raw;
            BroadcastRaw(raw);
        }

        private static void ApplyEnergySnapshot(EnergySnapshotMsg msg)
        {
            World.SteamEngines.Clear();
            foreach (var e in msg.Engines)
                World.SteamEngines[(e.X, e.Y)] = new SteamEngineData { IsOn = e.IsOn, Water = e.Water, Coal = e.Coal, SteamPressure = e.SteamPressure };

            World.Dynamos.Clear();
            foreach (var d in msg.Dynamos)
                World.Dynamos[(d.X, d.Y)] = new DynamoData { ElectricityOutput = d.ElectricityOutput };

            World.Batteries.Clear();
            foreach (var b in msg.Batteries)
                World.Batteries[(b.X, b.Y)] = new BatteryData { StoredEnergy = b.StoredEnergy, MaxCapacity = b.MaxCapacity };

            World.Ampoules.Clear();
            foreach (var a in msg.Ampoules)
                World.Ampoules[(a.X, a.Y)] = new AmpouleData { IsOn = a.IsOn };

            World.EnergyConnections.Clear();
            foreach (var c in msg.Connections)
                World.EnergyConnections.Add(new EnergyConnection { X1 = c.X1, Y1 = c.Y1, X2 = c.X2, Y2 = c.Y2 });
        }

        // ---- Entités ----
        private static void ApplyEntitySnapshot(EntitySnapshotMsg msgEntities)
        {
            // Mettre à jour les états interpolés
            var receivedIds = new HashSet<string>();
            foreach (var dto in msgEntities.Entities)
            {
                receivedIds.Add(dto.NetId);
                var state = _remoteEntityStates.GetOrAdd(dto.NetId, _ => new InterpolatedState<EntityDto>());
                state.Previous = state.Current ?? dto;
                state.Current = dto;
                state.ReceiveTime = Raylib.GetTime();
            }
            // Supprimer les entités qui ne sont plus dans le snapshot
            foreach (var kv in _remoteEntityStates.ToList())
            {
                if (!receivedIds.Contains(kv.Key))
                    _remoteEntityStates.TryRemove(kv.Key, out _);
            }
            // La liste interpolée sera mise à jour dans Update()
        }

        private static EntitySnapshotMsg BuildEntitySnapshot()
        {
            var dtos = new List<EntityDto>();
            foreach (var e in Program.entities)
            {
                if (e.IsPlayer || !e.IsAlive) continue;
                var dto = new EntityDto
                {
                    NetId = e.NetId.ToString(),
                    Species = e.Species,
                    PosX = e.WorldPos.X,
                    PosY = e.WorldPos.Y,
                    Facing = e.Facing,
                    AnimState = e.AnimState,
                    AnimFrame = e.CurrentFrame,
                    AnimProg = e.AnimProgress,
                    HP = e.CurrentHP,
                    MaxHP = e.MaxHP,
                    Scale = e.Scale,
                    IsTamed = e.IsTamed,
                    OwnerName = e.OwnerName ?? "",
                    IsSpiritAnimal = e.IsSpiritAnimal,
                    SpiritTotemTileX = e.SpiritTotemTileX,
                    SpiritTotemTileY = e.SpiritTotemTileY,
                    IsHostilePet = e.IsHostilePet,
                    IsCarried = e.CarriedByConnectionId != -1,
                    CustomName = e.CustomName ?? "",
                    Tint = new ColorSave { R = e.Tint.R, G = e.Tint.G, B = e.Tint.B, A = e.Tint.A },
                    HairColor = new ColorSave { R = e.HairColor.R, G = e.HairColor.G, B = e.HairColor.B, A = e.HairColor.A },
                    HairStyle = e.HairStyle,
                    BeardStyle = e.BeardStyle,
                    DamageFlash = e.DamageFlash,
                    FirstName = e.FirstName ?? "",
                    IsTrader = e.IsTrader,
                    TraderItems = e.TraderItems,
                    Profession = (int)e.Profession,
                    Friendship = e.Friendship,
                    IsGuildMember = e.IsGuildMember,
                    WantsToJoinGuild = e.WantsToJoinGuild
                };

                if (e.IsSlime)
                {
                    dto.SlimeStorageSlot = SaveSystem.CreateInventorySlotSave(e.GetSlimeStorageSlot());
                    dto.SlimeIncubationSeconds = e.SlimeIncubationSeconds;
                }

                //  Quête active proposée par ce PNJ (s'il en a une) : aplatie dans le DTO.
                if (e.ActiveQuest != null)
                {
                    var q = e.ActiveQuest;
                    dto.QuestId = q.Id.ToString();
                    dto.QuestTargetNetId = q.TargetId.ToString();
                    dto.QuestItemId = q.ItemId;
                    dto.QuestItemName = q.ItemName;
                    dto.QuestItemQty = q.ItemQty;
                    dto.QuestRewardItemId = q.RewardItemId;
                    dto.QuestRewardItemName = q.RewardItemName;
                    dto.QuestRewardItemQty = q.RewardItemQty;
                    dto.QuestState = (int)q.State;
                }

                //  Équipement de l'entité
                if (e.Equipment != null)
                {
                    dto.Equip = BuildEquipDataFromEquipment(e.Equipment);
                }

                dtos.Add(dto);
            }
            return new EntitySnapshotMsg { Entities = dtos };
        }

        private static void BroadcastEntitySnapshot()
        {
            var raw = Encelope("Entities", BuildEntitySnapshot());
            if (raw == _lastEntityPayload) return;
            _lastEntityPayload = raw;
            BroadcastRaw(raw);
        }

        public static void BroadcastEntityDeath(Guid netId)
        {
            if (!_isHost) return;
            Broadcast("EntityDeath", new EntityDeathMsg { NetId = netId.ToString() });
        }

        public static void RequestEntityHit(Guid netId, int damage)
        {
            if (!_isClient) return;
            SendToHost("EntityHit", new EntityHitMsg { NetId = netId.ToString(), Damage = damage });
        }

        public static void RequestPouilleuxInvitation(int targetConnectionId)
        {
            if (_isClient)
            {
                SendToHost("PouilleuxInviteReq", new PouilleuxInviteRequestMsg { TargetConnectionId = targetConnectionId });
                return;
            }

            if (_isHost)
                HandlePouilleuxInviteRequest(0, new PouilleuxInviteRequestMsg { TargetConnectionId = targetConnectionId });
        }

        public static void RespondToPouilleuxInvitation(string invitationId, bool accepted)
        {
            var response = new PouilleuxInviteResponseMsg
            {
                InvitationId = invitationId,
                Accepted = accepted
            };
            if (_isClient)
                SendToHost("PouilleuxInviteResponse", response);
            else if (_isHost)
                HandlePouilleuxInviteResponse(0, response);
        }

        public static void RequestPouilleuxDraw(string sessionId, int sourceParticipantIndex, int cardIndex)
        {
            if (!_isClient) return;
            SendToHost("PouilleuxDrawReq", new PouilleuxDrawRequestMsg
            {
                SessionId = sessionId,
                SourceParticipantIndex = sourceParticipantIndex,
                CardIndex = cardIndex
            });
        }

        public static void RequestPouilleuxStart(string sessionId)
        {
            if (!_isClient) return;
            SendToHost("PouilleuxStartReq", new PouilleuxStartRequestMsg { SessionId = sessionId });
        }

        private static void HandlePouilleuxDrawRequest(int connectionId, PouilleuxDrawRequestMsg request)
        {
            CartesGameUI.HandleNetworkDrawRequest(connectionId, request);
        }

        private static void HandlePouilleuxInviteRequest(int inviterConnectionId, PouilleuxInviteRequestMsg request)
        {
            if (!_isHost || request.TargetConnectionId == inviterConnectionId) return;
            void Reject(string message)
            {
                if (inviterConnectionId != 0)
                    SendTo(inviterConnectionId, "PouilleuxInviteResult", new PouilleuxInviteResultMsg { Success = false, Message = message });
                else
                    Program.AddNotification(new Notification(message, new Color(230, 130, 130, 255), 3f));
            }
            bool targetIsHost = request.TargetConnectionId == 0;
            NetConnection? targetConnection = null;
            if (!targetIsHost && (!_clients.TryGetValue(request.TargetConnectionId, out targetConnection)
                || targetConnection.Disconnected || targetConnection.LastTransform == null))
            {
                Reject("Invitation impossible : joueur introuvable.");
                return;
            }

            PlayerTransform? inviterTransform = inviterConnectionId == 0
                ? null
                : (_clients.TryGetValue(inviterConnectionId, out NetConnection? inviterConnection) ? inviterConnection.LastTransform : null);
            Vector2 inviterPosition = inviterConnectionId == 0
                ? Program.GetPlayerPosition()
                : inviterTransform == null ? Vector2.Zero : new Vector2(inviterTransform.PosX, inviterTransform.PosY);
            Vector2 targetPosition = targetIsHost
                ? Program.GetPlayerPosition()
                : new Vector2(targetConnection!.LastTransform!.PosX, targetConnection.LastTransform.PosY);
            if (inviterConnectionId != 0 && inviterTransform == null)
            {
                Reject("Invitation impossible : position de l'inviteur inconnue.");
                return;
            }
            bool targetUnderground = targetIsHost ? World.IsUnderground : targetConnection!.LastTransform!.Underground;
            if (inviterConnectionId != 0 && inviterTransform!.Underground != targetUnderground)
            {
                Reject("Invitation impossible : vous n'êtes pas dans la même zone.");
                return;
            }
            if (Vector2.DistanceSquared(inviterPosition, targetPosition) > 320f * 320f)
            {
                Reject("Invitation impossible : le joueur est trop éloigné.");
                return;
            }

            string invitationId = Guid.NewGuid().ToString("N");
            _pendingPouilleuxInvitations[invitationId] = (inviterConnectionId, request.TargetConnectionId);
            string inviterName = inviterConnectionId == 0
                ? Program.LocalPlayerName
                : inviterTransform!.Name;
            var invitation = new PouilleuxInvitationMsg
            {
                InvitationId = invitationId,
                InviterConnectionId = inviterConnectionId,
                InviterName = string.IsNullOrWhiteSpace(inviterName) ? "Joueur" : inviterName
            };
            if (targetIsHost)
                Program.ReceivePouilleuxInvitation(invitation);
            else
                SendTo(request.TargetConnectionId, "PouilleuxInvite", invitation);
            if (inviterConnectionId != 0)
                SendTo(inviterConnectionId, "PouilleuxInviteResult", new PouilleuxInviteResultMsg { Success = true, Message = "Invitation envoyée." });
        }

        private static void HandlePouilleuxInviteResponse(int targetConnectionId, PouilleuxInviteResponseMsg response)
        {
            if (!_isHost || !_pendingPouilleuxInvitations.TryRemove(response.InvitationId, out var invitation)) return;
            if (invitation.TargetId != targetConnectionId || !response.Accepted) return;

            string otherName = targetConnectionId == 0
                ? Program.LocalPlayerName
                : (_clients.TryGetValue(targetConnectionId, out NetConnection? target) ? target.LastTransform?.Name : null) ?? "Joueur";
            var inviterStart = new PouilleuxStartMsg
            {
                SessionId = Guid.NewGuid().ToString("N"),
                HostConnectionId = invitation.InviterId,
                OtherConnectionId = targetConnectionId,
                OtherPlayerName = otherName
            };
            var targetStart = new PouilleuxStartMsg
            {
                SessionId = inviterStart.SessionId,
                HostConnectionId = invitation.InviterId,
                OtherConnectionId = invitation.InviterId,
                OtherPlayerName = invitation.InviterId == 0
                    ? Program.LocalPlayerName
                    : (_clients.TryGetValue(invitation.InviterId, out NetConnection? inviter) ? inviter.LastTransform?.Name : null) ?? "Joueur"
            };

            if (invitation.InviterId == 0)
                Program.ReceivePouilleuxStart(inviterStart);
            else
                SendTo(invitation.InviterId, "PouilleuxStart", inviterStart);

            if (targetConnectionId == 0)
                Program.ReceivePouilleuxStart(targetStart);
            else
                SendTo(targetConnectionId, "PouilleuxStart", targetStart);
        }

        //  MULTIJOUEUR : permet à la simulation d'entités du host de considérer les joueurs
        // clients distants comme cibles potentielles (agro, fuite, combat), pas seulement le
        // joueur local du host. Renvoie (connectionId, position) pour chaque client connecté
        // ayant déjà envoyé au moins une position.
        public static List<(int ConnectionId, Vector2 Pos)> GetConnectedPlayerPositions()
        {
            var list = new List<(int, Vector2)>();
            if (!_isHost) return list;
            foreach (var kv in _clients)
            {
                var t = kv.Value.LastTransform;
                if (t != null) list.Add((kv.Key, new Vector2(t.PosX, t.PosY)));
            }
            return list;
        }

        public static int GetConnectedPlayerHeldItemId(int connectionId)
        {
            return _isHost && _clients.TryGetValue(connectionId, out var connection)
                ? connection.LastTransform?.HeldItemId ?? 0
                : 0;
        }
        private static void HandleTraderPurchaseRequest(int connectionId, TraderPurchaseRequestMsg request)
        {
            if (!_clients.TryGetValue(connectionId, out var connection) || connection.LastTransform == null)
                return;
            if (!Guid.TryParse(request.NpcNetId, out Guid netId))
                return;

            var trader = Program.entities.FirstOrDefault(entity => entity.NetId == netId);
            if (trader == null || (!trader.IsTrader && !trader.HasProfession) || !trader.TraderItems.Contains(request.ItemId))
                return;
            if (!GameData.ItemDatabase.TryGetValue(request.ItemId, out var itemData))
                return;

            Vector2 playerPosition = new(connection.LastTransform.PosX, connection.LastTransform.PosY);
            float interactionRange = Math.Max(Program.TileSize * 5f, 160f);
            if (Vector2.DistanceSquared(playerPosition, trader.WorldPos) > interactionRange * interactionRange)
                return;

            int tradeCost = itemData.Value > 0 ? itemData.Value : 1;
            trader.Friendship = Math.Clamp(trader.Friendship + Math.Clamp(tradeCost * 0.002f, 0f, 1f), 0f, 1f);
        }

        public static void RequestTraderPurchase(Guid traderNetId, int itemId)
        {
            if (!_isClient) return;
            SendToHost("TraderPurchaseReq", new TraderPurchaseRequestMsg
            {
                NpcNetId = traderNetId.ToString(),
                ItemId = itemId
            });
        }

        //  Envoie des dégâts (infligés par une entité du host) à un client précis, qui les
        // applique sur son propre HP local (le host reste autoritaire : c'est lui qui décide
        // du montant, mais chaque client gère l'affichage/la mort de son propre personnage).
        public static void SendPlayerDamage(int connectionId, int amount, float poisonDuration = 0f)
        {
            if (!_isHost || amount <= 0) return;
            SendTo(connectionId, "PlayerDamage", new PlayerDamageMsg { Amount = amount, PoisonDuration = poisonDuration });
        }

        //  MULTIJOUEUR : portage de créatures - demandes envoyées par un client au host.
        public static void RequestPickupCreature(Guid netId)
        {
            if (!_isClient) return;
            SendToHost("PickupCreatureReq", new PickupCreatureRequestMsg { NetId = netId.ToString() });
        }

        public static void RequestDropCreature(Guid netId, Vector2 pos)
        {
            if (!_isClient) return;
            SendToHost("DropCreatureReq", new DropCreatureRequestMsg { NetId = netId.ToString(), X = pos.X, Y = pos.Y });
        }

        //  MULTIJOUEUR : demande d'apprivoisement envoyée par un client au host. L'item n'est
        // pas retiré de l'inventaire local avant confirmation : le host peut refuser (animal
        // trop loin, déjà apprivoisé, entité disparue), auquel cas le joueur garde son item.
        public static void RequestTameAnimal(Guid netId, int itemId)
        {
            if (!_isClient) return;
            SendToHost("TameAnimalReq", new TameAnimalRequestMsg { NetId = netId.ToString(), ItemId = itemId });
        }

        public static void RequestSlimeStorageUpdate(Guid netId, InventorySlot slot)
        {
            if (!_isClient) return;
            SendToHost("SlimeStorageReq", new SlimeStorageRequestMsg
            {
                NetId = netId.ToString(),
                Slot = SaveSystem.CreateInventorySlotSave(slot)
            });
        }

        private static bool TryGetConnectionPos(int connectionId, out Vector2 pos)
        {
            pos = Vector2.Zero;
            if (_clients.TryGetValue(connectionId, out var conn) && conn.LastTransform != null)
            {
                pos = new Vector2(conn.LastTransform.PosX, conn.LastTransform.PosY);
                return true;
            }
            return false;
        }

        public static bool TryGetConnectedPlayerPosition(int connectionId, out Vector2 pos) =>
            TryGetConnectionPos(connectionId, out pos);

        private static void HandlePickupCreatureRequest(int connectionId, PickupCreatureRequestMsg msg)
        {
            if (!Guid.TryParse(msg.NetId, out var guid)) return;
            var e = Program.entities.FirstOrDefault(x => x.NetId == guid);
            if (e == null || !e.IsAlive) return;
            if (e.CarriedByConnectionId != -1) return; // déjà portée par quelqu'un
            if (!SpeciesData.IsPortable(e.Species)) return;
            if (!TryGetConnectionPos(connectionId, out var playerPos)) return;
            if (Vector2.DistanceSquared(playerPos, e.WorldPos) > (Program.TileSize * 2.5f) * (Program.TileSize * 2.5f)) return;

            e.CarriedByConnectionId = connectionId;
            e.AnimState = "idle";
        }

        private static void HandleDropCreatureRequest(int connectionId, DropCreatureRequestMsg msg)
        {
            if (!Guid.TryParse(msg.NetId, out var guid)) return;
            var e = Program.entities.FirstOrDefault(x => x.NetId == guid);
            if (e == null || e.CarriedByConnectionId != connectionId) return;

            var targetPos = new Vector2(msg.X, msg.Y);
            if (World.IsCollidingEntity(targetPos, Program.GetDestroyedObjectsForNetwork(), e.Species)) return;

            e.WorldPos = targetPos;
            e.AnimState = "idle";
            e.CarriedByConnectionId = -1;
        }

        private static void HandleTameAnimalRequest(int connectionId, TameAnimalRequestMsg msg)
        {
            if (!Guid.TryParse(msg.NetId, out var guid)) return;
            var e = Program.entities.FirstOrDefault(x => x.NetId == guid);

            void Fail()
            {
                SendTo(connectionId, "TameResult", new TameAnimalResultMsg { NetId = msg.NetId, Success = false, ItemId = msg.ItemId });
            }

            if (e == null || !e.IsAlive) { Fail(); return; }
            if (e.IsTamed) { Fail(); return; }
            if (e.IsSpiritAnimal) { Fail(); return; }
            if (e.CarriedByConnectionId != -1) { Fail(); return; } // portée par quelqu'un
            if (!TryGetConnectionPos(connectionId, out var playerPos)) { Fail(); return; }
            if (Vector2.DistanceSquared(playerPos, e.WorldPos) > (Program.TileSize * 2.5f) * (Program.TileSize * 2.5f)) { Fail(); return; }
            if (msg.ItemId != Program.GetPreferredFoodId(e.Species)) { Fail(); return; }

            e.IsTamed = true;
            e.Behavior = "tamed";
            e.OwnerName = (_clients.TryGetValue(connectionId, out var conn) && conn.LastTransform != null)
                ? conn.LastTransform.Name
                : $"Joueur {connectionId}";

            SendTo(connectionId, "TameResult", new TameAnimalResultMsg { NetId = msg.NetId, Success = true, Species = e.Species, ItemId = msg.ItemId });
        }

        private static void HandleSlimeStorageRequest(int connectionId, SlimeStorageRequestMsg msg)
        {
            if (!Guid.TryParse(msg.NetId, out var guid)) return;
            var slime = Program.entities.FirstOrDefault(entity => entity.NetId == guid);
            if (slime == null || !slime.IsAlive || !slime.IsSlime || !slime.IsTamed || slime.IsHostilePet) return;
            if (!_clients.TryGetValue(connectionId, out var client) || client.LastTransform == null) return;
            if (!string.Equals(slime.OwnerName, client.LastTransform.Name, StringComparison.OrdinalIgnoreCase)) return;
            if (!TryGetConnectionPos(connectionId, out var playerPos)) return;
            if (Vector2.DistanceSquared(playerPos, slime.WorldPos) > (Program.TileSize * 2.5f) * (Program.TileSize * 2.5f)) return;

            InventorySlot storedSlot = slime.GetSlimeStorageSlot();
            if (msg.Slot == null || msg.Slot.IsEmpty || string.IsNullOrWhiteSpace(msg.Slot.ItemName))
            {
                storedSlot.Clear();
                slime.SlimeIncubationSeconds = 0f;
                return;
            }

            int itemId = GameData.GetItemId(msg.Slot.ItemName);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)
                || msg.Slot.Count <= 0 || msg.Slot.Count > itemData.StackSize)
                return;

            SaveSystem.RestoreInventorySlotFromSave(msg.Slot, storedSlot);
            slime.SlimeIncubationSeconds = 0f;
        }

        private static void HandleEntityHit(int connectionId, EntityHitMsg msg)
        {
            if (!Guid.TryParse(msg.NetId, out var guid)) return;
            var e = Program.entities.FirstOrDefault(x => x.NetId == guid);
            if (e == null || !e.IsAlive) return;
            if (e.CarriedByConnectionId != -1) return; // on ne frappe pas une créature en train d'être portée

            Vector2 attackerPos = Vector2.Zero;
            if (_clients.TryGetValue(connectionId, out var conn) && conn.LastTransform != null)
            {
                attackerPos = new Vector2(conn.LastTransform.PosX, conn.LastTransform.PosY);
            }

            e.CurrentHP = Math.Max(0, e.CurrentHP - msg.Damage);
            e.OnHit(attackerPos, attackerConnectionId: connectionId); // la peur/riposte suit le client qui a frappé

            //  Écarte l'animal de l'attaquant (le client qui a envoyé le coup), pour reproduire
            // le recul qu'obtient le host quand il frappe lui-même une entité.
            Vector2 knockbackDir = new Vector2(1, 0);
            if (conn.LastTransform != null)
            {
                Vector2 attackerPos2 = new Vector2(conn.LastTransform.PosX, conn.LastTransform.PosY);
                Vector2 dir = e.WorldPos - attackerPos2;
                if (dir.Length() > 0.01f) knockbackDir = Vector2.Normalize(dir);
            }
            e.ApplyKnockback(knockbackDir, 6f);

            if (!e.IsAlive)
            {
                //  Butin : posé au sol (et non donné directement à un inventaire) puisque c'est
                // un client distant qui a porté le coup fatal - n'importe quel joueur pourra le
                // ramasser, exactement comme un item lâché normalement.
                foreach (var (id, qty, _) in GameData.GetAnimalDrops(e.Species))
                {
                    Program.SpawnAuthoritativeGroundItem(id, qty, e.WorldPos);
                }
                Broadcast("EntityDeath", new EntityDeathMsg { NetId = msg.NetId });
            }
        }

        // ---- Quêtes des PNJ ----
        //  Traite côté hôte (seul autoritaire sur Program.entities) une action de quête
        // demandée par un client. Réutilise directement QuestManager, exactement comme le
        // ferait le joueur hôte lui-même ; le nouvel état (ActiveQuest modifié/effacé) sera
        // renvoyé à tout le monde, y compris au client demandeur, via le prochain
        // EntitySnapshotMsg (BroadcastEntitySnapshot), pas besoin de message de retour dédié.
        private static void HandleQuestActionRequest(int connectionId, QuestActionRequestMsg req)
        {
            if (!Guid.TryParse(req.NpcNetId, out var npcNetId)) return;
            var npc = Program.entities.FirstOrDefault(e => e.NetId == npcNetId);
            if (npc == null) return;
            if (!_clients.TryGetValue(connectionId, out var connection) || connection.LastTransform == null) return;
            Vector2 requesterPosition = new(connection.LastTransform.PosX, connection.LastTransform.PosY);
            float interactionRange = Math.Max(Program.TileSize * 5f, 160f);
            if (Vector2.DistanceSquared(requesterPosition, npc.WorldPos) > interactionRange * interactionRange) return;

            switch (req.Action)
            {
                case "Accept":
                    QuestManager.AcceptQuest(npc, (itemId, qty) => SendItemGrant(connectionId, itemId, qty));
                    break;

                case "Decline":
                    QuestManager.DeclineQuest(npc);
                    break;

                case "Deliver":
                    //  Le client a déjà vérifié/retiré l'objet de son propre inventaire avant
                    // d'envoyer la requête (l'inventaire est géré localement, pas par l'hôte) :
                    // on se contente donc ici de faire passer la quête à "Delivered".
                    QuestManager.TryDeliverQuest(
                        npc,
                        Program.entities,
                        hasItem: (_, __) => true,
                        removeItem: (_, __) => { },
                        out _);
                    break;

                case "Validate":
                    QuestManager.TryCompleteQuest(
                        npc,
                        Program.entities,
                        giveItem: (itemId, qty) => SendItemGrant(connectionId, itemId, qty),
                        out _);
                    break;

                case "JoinGuild":
                    GuildRecruitManager.TryAccept(npc, World.CurrentSaveName);
                    break;

                case "GiftAnimal":
                    //  MULTIJOUEUR : un client a confirmé le don d'un animal apprivoisé à ce PNJ
                    // pour une quête TameAndBring. req.ExtraNetId porte le NetId de l'animal ;
                    // seul l'hôte peut retrouver la véritable Entity dans Program.entities et
                    // appliquer QuestManager.TryGiftTamedAnimal (qui mute AddPet/OwnerNpcId).
                    if (Guid.TryParse(req.ExtraNetId, out var animalNetId))
                    {
                        var animal = Program.entities.FirstOrDefault(e => e.NetId == animalNetId);
                        if (animal != null)
                        {
                            bool gifted = QuestManager.TryGiftTamedAnimal(npc, animal, Program.entities, out var deliveredQuest);
                            if (gifted && deliveredQuest != null)
                            {
                                //  Notifier le client donneur du succès (l'hôte a déjà tout appliqué
                                // localement ; les Entity/quêtes seront de toute façon rediffusées
                                // par les snapshots réseau périodiques, mais on prévient tout de
                                // suite le joueur d'origine pour qu'il ait un retour immédiat).
                                SendTo(connectionId, "QuestActionResult", new QuestActionResultMsg
                                {
                                    Success = true,
                                    Action = "GiftAnimal",
                                    NpcNetId = req.NpcNetId
                                });
                            }
                        }
                    }
                    break;
            }
        }

        //  Appelé côté client pour demander à l'hôte d'appliquer une action de quête/guilde.
        // Ne fait rien si on est l'hôte (ou en solo) : dans ce cas, Program.cs continue
        // d'appeler QuestManager directement, comme avant, car il possède déjà les vraies
        // Entity de Program.entities.
        public static void RequestQuestAction(Guid npcNetId, string action, Guid? extraNetId = null)
        {
            if (!_isClient) return;
            SendToHost("QuestActionReq", new QuestActionRequestMsg
            {
                NpcNetId = npcNetId.ToString(),
                Action = action,
                ExtraNetId = extraNetId?.ToString()
            });
        }

        public static void SendItemGrant(int connectionId, int itemId, int count)
        {
            if (!_isHost || count <= 0 || !GameData.ItemDatabase.ContainsKey(itemId))
                return;

            SendTo(connectionId, "Grant", new ItemGrantMsg { ItemId = itemId, Count = count });
        }

        // ---- Conteneurs ----
        private static void HandleContainerRequest(int connectionId, ContainerRequestMsg req)
        {
            var data = Program.GetContainerDataForNetwork(req.TileX, req.TileY, req.Underground);
            if (data != null) SendTo(connectionId, "ContainerSync", new ContainerSyncMsg
            {
                TileX = req.TileX,
                TileY = req.TileY,
                Underground = req.Underground,
                Data = data
            });
        }

        public static void RequestContainerData(int tileX, int tileY, bool underground)
        {
            if (!_isClient) return;
            SendToHost("ContainerReq", new ContainerRequestMsg { TileX = tileX, TileY = tileY, Underground = underground });
        }

        /// <summary>
        /// Envoie une mise à jour de conteneur au host (si client) ou la diffuse (si host).
        /// À appeler après chaque modification d'un conteneur (coffre, étagère, etc.).
        /// </summary>
        public static void SyncContainer(int tileX, int tileY, bool underground, ContainerInventorySave data)
        {
            var msg = new ContainerSyncMsg { TileX = tileX, TileY = tileY, Underground = underground, Data = data };
            if (_isClient) SendToHost("ContainerSync", msg);
            else if (_isHost) Broadcast("ContainerSync", msg);
        }

        // ---- Objets au sol ----
        private static void ApplyGroundItemsSnapshot(GroundItemsSnapshotMsg snapshot)
        {
            _remoteGroundItems = snapshot.Items;
            _groundItemsDirty = true;
        }

        private static GroundItemsSnapshotMsg BuildGroundItemsSnapshot()
        {
            var items = Program.GroundItems.Select(gi => new GroundItemDto
            {
                NetId = gi.NetId.ToString(),
                ItemId = gi.ItemId,
                Count = gi.Count,
                PosX = gi.Position.X,
                PosY = gi.Position.Y,
                Colors = (gi.CustomColors ?? new List<Color?>()).Select(c => c.HasValue
                    ? new ColorSave { R = c.Value.R, G = c.Value.G, B = c.Value.B, A = c.Value.A }
                    : (ColorSave?)null).ToList(),
                Metadata = gi.Metadata ?? "",
                Meta = gi.Meta,
                LootbagPayload = gi.LootbagPayload ?? ""
            }).ToList();
            return new GroundItemsSnapshotMsg { Items = items };
        }

        public static void BroadcastGroundItemsSnapshot()
        {
            var raw = Encelope("Items", BuildGroundItemsSnapshot());
            if (raw == _lastGroundItemsPayload) return;
            _lastGroundItemsPayload = raw;
            BroadcastRaw(raw);
        }

        public static void RequestPickup(string netId)
        {
            SendToHost("Pickup", new PickupRequestMsg { NetId = netId });
        }

        public static void RequestItemDrop(int itemId, int count, float posX, float posY, List<ColorSave?>? colors = null, string? metadata = null, Dictionary<string, string>? meta = null, PortableContainerSaveData? portableContainer = null, BackpackSaveData? backpack = null, float? pickupCooldown = null)
        {
            if (!_isClient) return;
            SendToHost("ItemDropReq", new ItemDropRequestMsg
            {
                ItemId = itemId,
                Count = count,
                PosX = posX,
                PosY = posY,
                Colors = colors ?? new List<ColorSave?>(),
                Metadata = metadata ?? "",
                Meta = (meta != null && meta.Count > 0) ? new Dictionary<string, string>(meta) : null,
                PortableContainer = portableContainer,
                Backpack = backpack,
                PickupCooldown = pickupCooldown
            });
        }

        private static void HandleItemDropRequest(ItemDropRequestMsg req)
        {
            if (!_isHost) return;
            List<Color?>? customColors = null;
            if (req.Colors != null && req.Colors.Count > 0)
            {
                customColors = req.Colors.Select(c => c != null
                    ? (Color?)new Color(c.R, c.G, c.B, c.A)
                    : (Color?)null).ToList();
            }
            var portableContainer = Program.RestorePortableContainer(req.PortableContainer);
            var backpack = Program.RestoreBackpack(req.Backpack);
            Program.SpawnAuthoritativeGroundItem(req.ItemId, req.Count, new Vector2(req.PosX, req.PosY), customColors, null, 0f, req.Metadata, req.Meta, portableContainer, backpack, req.PickupCooldown);
            BroadcastGroundItemsSnapshot();
        }

        //  MULTIJOUEUR : un client vient de mourir et nous envoie le contenu de ses sacs
        // (ceinture + sac à dos + panier) pour qu'on fasse apparaître son sac de butin,
        // exactement comme DropPlayerLootbag le fait localement pour le host.
        public static void RequestLootbagDrop(string payload, Vector2 pos)
        {
            if (!_isClient) return;
            if (string.IsNullOrWhiteSpace(payload)) return;
            SendToHost("LootbagDropReq", new LootbagDropRequestMsg
            {
                Payload = payload,
                PosX = pos.X,
                PosY = pos.Y
            });
        }

        private static void HandleLootbagDropRequest(LootbagDropRequestMsg req)
        {
            if (!_isHost) return;
            if (string.IsNullOrWhiteSpace(req.Payload)) return;
            Program.SpawnAuthoritativePlayerLootbag(req.Payload, new Vector2(req.PosX, req.PosY));
            BroadcastGroundItemsSnapshot();
        }

        private static void HandlePickupRequest(int connectionId, PickupRequestMsg req)
        {
            if (!Guid.TryParse(req.NetId, out var guid)) return;
            var item = Program.GroundItems.FirstOrDefault(g => g.NetId == guid);
            if (item == null) return;

            Program.GroundItems.Remove(item);
            BroadcastGroundItemsSnapshot();

            var colors = item.CustomColors != null ? new List<ColorSave?>(item.CustomColors.Select(c => c.HasValue
                ? new ColorSave { R = c.Value.R, G = c.Value.G, B = c.Value.B, A = c.Value.A }
                : (ColorSave?)null)) : new List<ColorSave?>();

            SendTo(connectionId, "Grant", new ItemGrantMsg
            {
                ItemId = item.ItemId,
                Count = item.Count,
                Colors = colors,
                LootbagPayload = item.LootbagPayload ?? "",
                Metadata = item.Metadata ?? "",
                Meta = (item.Meta != null && item.Meta.Count > 0) ? new Dictionary<string, string>(item.Meta) : null,
                PortableContainer = item.Container != null ? Program.CreatePortableContainerSaveData(item.Container) : null,
                Backpack = item.Backpack != null ? Program.CreateBackpackSaveData(item.Backpack) : null
            });
        }

        // ---- Chat ----
        public static void SendChat(string playerName, string text)
        {
            if (!IsOnline) return;
            var msg = new ChatRelayMsg { Name = playerName, Text = text };
            if (_isClient) SendToHost("Chat", msg);
            else if (_isHost) Broadcast("Chat", msg);
        }

        // ════════════════════════════════════════════════════════════════
        //  API PUBLIQUE POUR LE RENDU (avec interpolation)
        // ════════════════════════════════════════════════════════════════

        public static List<Program.LocalPlayer> GetRemotePlayersAsLocalPlayers()
        {
            var result = new List<Program.LocalPlayer>();
            foreach (var kv in _remotePlayerStates)
            {
                var state = kv.Value;
                if (state.ReceiveTime == 0) continue;
                var p = InterpolateState(state, STATE_SEND_INTERVAL);
                var local = new Program.LocalPlayer
                {
                    Id = 1000 + kv.Key,
                    Connected = true,
                    Position = new Vector2(p.PosX, p.PosY),
                    Facing = p.Facing,
                    AnimState = p.AnimState,
                    AnimFrame = p.AnimFrame,
                    AnimProg = p.AnimProg,
                    AttackSwingProgress = p.AttackSwingProgress,
                    HotbarSlot = p.HotbarSlot,
                    DeviceName = "network",
                    DeviceIndex = -1,
                    IsNetworkPlayer = true,
                    DisplayName = p.Name,
                    SpeciesOverride = p.Species,
                    MorphTintOverride = new Color(p.MorphTint.R, p.MorphTint.G, p.MorphTint.B, p.MorphTint.A),
                    MorphFeatureVariantsOverride = new Dictionary<string, int>(p.MorphFeatureVariants ?? new()),
                    MorphFeatureColorsOverride = (p.MorphFeatureColors ?? new()).ToDictionary(
                        kv => kv.Key,
                        kv => new Color(kv.Value.R, kv.Value.G, kv.Value.B, kv.Value.A)),
                    SkinColorOverride = new Color(p.Skin.R, p.Skin.G, p.Skin.B, p.Skin.A),
                    HairColorOverride = new Color(p.Hair.R, p.Hair.G, p.Hair.B, p.Hair.A),
                    HairStyleOverride = p.HairStyle,
                    BeardStyleOverride = p.BeardStyle,
                    EyeStyleOverride = p.EyeStyle,
                    EyeColorOverride = new Color(p.EyeColor.R, p.EyeColor.G, p.EyeColor.B, p.EyeColor.A),
                    HP = p.HP,
                    MaxHP = p.MaxHP,
                    HeldItemId = p.HeldItemId,
                    EqHead = p.EqHead,
                    EqBody = p.EqBody,
                    EqLegs = p.EqLegs,
                    Equip = p.Equip,
                    HeadAngle = p.HeadAngle,
                    LookWorldPos = new Vector2(p.LookX, p.LookY),
                    Underground = p.Underground,
                    PvpEnabled = p.PvpEnabled
                };

                result.Add(local);
            }
            return result;
        }

        public static IReadOnlyList<EntityDto> GetRemoteEntities()
        {
            return _remoteEntitiesInterpolated;
        }

        //  MULTIJOUEUR : reconstruit, côté client uniquement, des Entity "fantômes" à partir
        // des derniers EntityDto reçus de l'hôte, pour les villageois (PNJ). Program.entities
        // est vide chez un client (l'hôte est seul à simuler les PNJ) : sans ceci, tout le
        // système de quêtes/commerce (QuestManager, FindQuestNpcUnderMouse, etc., qui itèrent
        // sur une List<Entity>) ne voit jamais aucun PNJ. Ces objets sont reconstruits à
        // chaque appel à partir du dernier snapshot : ils ne doivent JAMAIS être mutés pour
        // faire progresser une quête - toute action doit passer par RequestQuestAction, qui
        // demande à l'hôte (seul autoritaire) d'appliquer le changement, propagé ensuite à
        // tout le monde via le prochain EntitySnapshotMsg.
        public static List<Entity> GetVillagerProxies()
        {
            var result = new List<Entity>();
            foreach (var dto in _remoteEntitiesInterpolated)
            {
                if (dto.Species != "human") continue;
                if (!Guid.TryParse(dto.NetId, out var netId)) continue;

                var proxy = new Entity(new Vector2(dto.PosX, dto.PosY), dto.Species)
                {
                    NetId = netId,
                    IsTamed = dto.IsTamed,
                    IsHostilePet = dto.IsHostilePet,
                    Scale = dto.Scale,
                    CustomName = string.IsNullOrEmpty(dto.CustomName) ? null : dto.CustomName,
                    FirstName = string.IsNullOrEmpty(dto.FirstName) ? null : dto.FirstName,
                    IsTrader = dto.IsTrader,
                    TraderItems = dto.TraderItems ?? new List<int>(),
                    Profession = (ProfessionType)dto.Profession,
                    Friendship = dto.Friendship,
                    IsGuildMember = dto.IsGuildMember,
                    WantsToJoinGuild = dto.WantsToJoinGuild,
                    //  IMPORTANT : le constructeur d'Entity tire ces champs au hasard pour un
                    // "human" (apparence aléatoire par défaut) - il faut les écraser avec
                    // l'apparence RÉELLE du PNJ envoyée par l'hôte, sinon la bulle de dialogue
                    // affiche un portrait différent (et changeant) à chaque PNJ côté client.
                    Tint = new Color(dto.Tint.R, dto.Tint.G, dto.Tint.B, dto.Tint.A),
                    HairColor = new Color(dto.HairColor.R, dto.HairColor.G, dto.HairColor.B, dto.HairColor.A),
                    HairStyle = dto.HairStyle,
                    BeardStyle = dto.BeardStyle,
                    Facing = dto.Facing,
                    AnimState = dto.AnimState
                };
                proxy.SetHealthSilently(dto.HP, dto.MaxHP);

                if (dto.QuestState >= 0 && Guid.TryParse(dto.QuestTargetNetId, out var targetId))
                {
                    proxy.ActiveQuest = new Quest
                    {
                        Id = Guid.TryParse(dto.QuestId, out var qid) ? qid : Guid.NewGuid(),
                        GiverId = netId,
                        TargetId = targetId,
                        ItemId = dto.QuestItemId,
                        ItemName = dto.QuestItemName,
                        ItemQty = dto.QuestItemQty,
                        RewardItemId = dto.QuestRewardItemId,
                        RewardItemName = dto.QuestRewardItemName,
                        RewardItemQty = dto.QuestRewardItemQty,
                        State = (QuestState)dto.QuestState
                    };
                    QuestManager.NormalizeQuestItemData(proxy.ActiveQuest);
                }

                result.Add(proxy);
            }
            return result;
        }

        public static bool ConsumeEntitiesDirty()
        {
            bool d = _remoteEntitiesDirty;
            _remoteEntitiesDirty = false;
            return d;
        }

        public static IReadOnlyList<GroundItemDto> GetRemoteGroundItems() => _remoteGroundItems;
        public static bool ConsumeGroundItemsDirty()
        {
            bool d = _groundItemsDirty;
            _groundItemsDirty = false;
            return d;
        }

        public static List<(int id, string name, bool pvp)> GetConnectedPlayersInfo()
        {
            var list = new List<(int id, string name, bool pvp)> { (LocalId, Program.LocalPlayerName + " (vous)", Program.LocalPvpEnabled) };
            foreach (var kv in _remotePlayerStates)
                list.Add((kv.Key, kv.Value.Current.Name, kv.Value.Current.PvpEnabled));
            return list;
        }
    }

    public class TeleportMsg
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    //  PVP : demande d'un client au host pour infliger des dégâts à un autre joueur
    // (le host reste seul autoritaire : il revalide que les deux joueurs ont bien le PVP activé).
    public class PvpHitReqMsg
    {
        public int TargetId { get; set; }
        public int Damage { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    //   ACCÈS DEPUIS INTERNET : redirection de port automatique (UPnP)
    // ════════════════════════════════════════════════════════════════
    // (inchangé)
    internal static class UpnpPortMapper
    {
        public struct MappingResult
        {
            public bool Success;
            public bool UdpSuccess;
            public string ExternalIp;
        }

        private const string SSDP_ADDR = "239.255.255.250";
        private const int SSDP_PORT = 1900;
        private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(4);

        public static async Task<MappingResult> TryAddPortMappingAsync(int port, CancellationToken ct = default)
        {
            var result = new MappingResult { Success = false, ExternalIp = "" };
            try
            {
                var gateway = await DiscoverGatewayAsync(ct).ConfigureAwait(false);
                if (gateway == null) return result;

                string localIp = GetLocalIpAddress();
                if (string.IsNullOrEmpty(localIp)) return result;

                bool mapped = await SendAddPortMappingAsync(gateway.Value, port, localIp, "TCP", ct).ConfigureAwait(false);
                if (!mapped) return result;

                result.Success = true;
                result.UdpSuccess = await SendAddPortMappingAsync(gateway.Value, port, localIp, "UDP", ct).ConfigureAwait(false);
                result.ExternalIp = await SendGetExternalIpAsync(gateway.Value, ct).ConfigureAwait(false) ?? "";
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return result;
            }
        }
        public static async Task<string?> FetchPublicIpFallbackAsync(CancellationToken ct = default)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string ip = await http.GetStringAsync("https://api.ipify.org", ct).ConfigureAwait(false);
                return System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task TryRemovePortMappingAsync(int port)
        {
            try
            {
                var gateway = await DiscoverGatewayAsync().ConfigureAwait(false);
                if (gateway == null) return;
                await SendDeletePortMappingAsync(gateway.Value, port, "TCP").ConfigureAwait(false);
                await SendDeletePortMappingAsync(gateway.Value, port, "UDP").ConfigureAwait(false);
            }
            catch { }
        }
        private struct GatewayInfo
        {
            public string ControlUrl;
            public string ServiceType;
        }

        private static async Task<GatewayInfo?> DiscoverGatewayAsync(CancellationToken ct = default)
        {
            string? location = await DiscoverLocationAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(location)) return null;

            using var http = new HttpClient { Timeout = HttpTimeout };
            string xml;
            try { xml = await http.GetStringAsync(location, ct).ConfigureAwait(false); }
            catch { return null; }

            var (controlPath, serviceType) = ParseControlUrl(xml);
            if (string.IsNullOrEmpty(controlPath)) return null;

            Uri baseUri = new Uri(location!);
            Uri controlUri = new Uri(baseUri, controlPath);
            return new GatewayInfo { ControlUrl = controlUri.ToString(), ServiceType = serviceType };
        }
        private static async Task<string?> DiscoverLocationAsync(CancellationToken ct = default)
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); } catch { }

            string request =
                "M-SEARCH * HTTP/1.1\r\n" +
                $"HOST: {SSDP_ADDR}:{SSDP_PORT}\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 2\r\n" +
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
            byte[] payload = Encoding.ASCII.GetBytes(request);
            var target = new IPEndPoint(IPAddress.Parse(SSDP_ADDR), SSDP_PORT);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DiscoveryTimeout);

            try
            {
                await udp.SendAsync(payload, payload.Length, target).ConfigureAwait(false);
                await Task.Delay(150, cts.Token).ConfigureAwait(false);
                await udp.SendAsync(payload, payload.Length, target).ConfigureAwait(false);

                while (!cts.IsCancellationRequested)
                {
                    var receiveTask = udp.ReceiveAsync(cts.Token).AsTask();
                    var completed = await Task.WhenAny(receiveTask, Task.Delay(DiscoveryTimeout, cts.Token)).ConfigureAwait(false);
                    if (completed != receiveTask) break;

                    string response = Encoding.ASCII.GetString(receiveTask.Result.Buffer);
                    string? loc = ExtractHeader(response, "LOCATION");
                    if (!string.IsNullOrEmpty(loc)) return loc;
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout
            }
            catch { }
            return null;
        }
        private static string? ExtractHeader(string httpMessage, string header)
        {
            foreach (var rawLine in httpMessage.Split('\n'))
            {
                string line = rawLine.Trim('\r', '\n', ' ');
                int sep = line.IndexOf(':');
                if (sep <= 0) continue;
                string name = line.Substring(0, sep).Trim();
                if (string.Equals(name, header, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(sep + 1).Trim();
            }
            return null;
        }

        private static (string? controlUrl, string serviceType) ParseControlUrl(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                XNamespace ns = doc.Root?.GetDefaultNamespace() ?? "";
                foreach (var service in doc.Descendants(ns + "service"))
                {
                    string type = service.Element(ns + "serviceType")?.Value ?? "";
                    if (type.Contains("WANIPConnection") || type.Contains("WANPPPConnection"))
                    {
                        string? control = service.Element(ns + "controlURL")?.Value;
                        if (!string.IsNullOrEmpty(control)) return (control, type);
                    }
                }
            }
            catch { }
            return (null, "");
        }

        private static async Task<bool> SendAddPortMappingAsync(GatewayInfo gateway, int port, string localIp, string protocol, CancellationToken ct = default)
        {
            string body =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:AddPortMapping xmlns:u=\"{gateway.ServiceType}\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{port}</NewExternalPort>" +
                $"<NewProtocol>{protocol}</NewProtocol>" +
                $"<NewInternalPort>{port}</NewInternalPort>" +
                $"<NewInternalClient>{localIp}</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled>" +
                "<NewPortMappingDescription>RPG Engine (hébergement partie)</NewPortMappingDescription>" +
                "<NewLeaseDuration>0</NewLeaseDuration>" +
                "</u:AddPortMapping>" +
                "</s:Body></s:Envelope>";

            string? response = await SendSoapAsync(gateway, "AddPortMapping", body, ct).ConfigureAwait(false);
            return response != null;
        }

        private static async Task SendDeletePortMappingAsync(GatewayInfo gateway, int port, string protocol)
        {
            string body =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:DeletePortMapping xmlns:u=\"{gateway.ServiceType}\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{port}</NewExternalPort>" +
                $"<NewProtocol>{protocol}</NewProtocol>" +
                "</u:DeletePortMapping>" +
                "</s:Body></s:Envelope>";
            await SendSoapAsync(gateway, "DeletePortMapping", body).ConfigureAwait(false);
        }

        private static async Task<string?> SendGetExternalIpAsync(GatewayInfo gateway, CancellationToken ct = default)
        {
            string body =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:GetExternalIPAddress xmlns:u=\"{gateway.ServiceType}\"></u:GetExternalIPAddress>" +
                "</s:Body></s:Envelope>";

            string? response = await SendSoapAsync(gateway, "GetExternalIPAddress", body, ct).ConfigureAwait(false);
            if (response == null) return null;
            try
            {
                var doc = XDocument.Parse(response);
                var ipEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress");
                string? ip = ipEl?.Value;
                return System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
            }
            catch { return null; }
        }
        private static async Task<string?> SendSoapAsync(GatewayInfo gateway, string action, string body, CancellationToken ct = default)
        {
            try
            {
                using var http = new HttpClient { Timeout = HttpTimeout };
                var content = new StringContent(body, Encoding.UTF8, "text/xml");
                content.Headers.Remove("Content-Type");
                content.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");

                var request = new HttpRequestMessage(HttpMethod.Post, gateway.ControlUrl) { Content = content };
                request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{gateway.ServiceType}#{action}\"");

                var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? text : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
        private static string GetLocalIpAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint) return endPoint.Address.ToString();
            }
            catch { }
            return "";
        }
    }
}