// Models.cs - Version avec système de combat amélioré (vitesse de poursuite, wind-up)
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soulfract
{
    public class NullableBoolJsonConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.True)
                return true;

            if (reader.TokenType == JsonTokenType.False)
                return false;

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                if (bool.TryParse(text, out var boolValue))
                    return boolValue;

                if (int.TryParse(text, out var intValue))
                    return intValue != 0;
            }

            throw new JsonException($"Unable to convert JSON token {reader.TokenType} to bool? for NullableBoolJsonConverter.");
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteBooleanValue(value.Value);
            else
                writer.WriteNullValue();
        }
    }

	
    public class InventorySlot
	{
		public Item? Item;
		public int Count;
		
		public InventorySlot(Item? item = null, int count = 0)
		{
			Item = item;
			Count = count;
		}
		
		public bool IsEmpty => Item == null || Count <= 0;
		public void Clear() { Item = null; Count = 0; }
	}

    public enum WorkZoneKind { Field, LumberCamp, MineEntrance, HuntingGround }

	public class VillageCenter
	{
		public (int x, int y) CenterTile;
		public int PlazaRadius = 7;
		public float ExchangeRadius = 180f;
	}

    public class VillageWorkZone
    {
        public int Id;
        public WorkZoneKind Kind;
        public Vector2 AnchorPos;
        public float Radius;
        public int MaxWorkers = 3;
		public int TilledTileCount;
		public int MaxTilledTiles = 48;
        public List<WorkSlot> Slots = new();
    }

    public class WorkSlot
    {
        public Vector2 WorldPos;
        public bool Occupied;
    }
	
	// Interface pour uniformiser les conteneurs (panier, sac à dos)
	public interface IContainerInventory
	{
		ContainerInventoryData Inventory { get; }
	}

	// Classe PortableContainerData modifiée
	public class PortableContainerData : IContainerInventory
	{
		public int ItemId { get; set; }
		public ContainerInventoryData Inventory { get; set; } = new();
		
		public PortableContainerData()
		{
			Inventory = new ContainerInventoryData(9, 3);
		}
		
		public PortableContainerData(int slotCount, int columns = 3)
		{
			Inventory = new ContainerInventoryData(slotCount, columns);
		}
	}

	// Sac à dos - classe modifiée
	public class BackpackData : IContainerInventory
	{
		public ContainerInventoryData Inventory { get; set; } = new();
		
		public BackpackData(int slotCount, int columns = 5)
		{
			Inventory = new ContainerInventoryData(slotCount, columns);
		}
	}

	public class ContainerInventoryData
	{
		public List<InventorySlot> Slots { get; set; } = new();
		public int Columns { get; set; } = 8;
		public int Rows { get; set; } = 3;

		public ContainerInventoryData() { }

		public ContainerInventoryData(int slotCount, int columns = 8)
		{
			Columns = columns;
			int rows = (int)Math.Ceiling((double)slotCount / columns);
			Rows = rows;
			for (int i = 0; i < slotCount; i++)
				Slots.Add(new InventorySlot());
		}
	}
	
	public class ArmorStandData
	{
		//  Le porte-armure utilise désormais le même modèle d'équipement que le joueur
		// (zones du corps + emplacements fonctionnels), ce qui permet d'y glisser tous
		// les types d'équipement (armure, accessoires, armes...) exactement comme sur soi.
		public Equipment Equipment { get; set; } = new Equipment();

		// --- Accesseurs de compatibilité (sauvegarde existante) ---
		public Item? Head { get => Equipment.GetZoneItem(BodyZone.TopOfHead); set => SetZoneItem(BodyZone.TopOfHead, value); }
		public Item? Body { get => Equipment.GetZoneItem(BodyZone.Torso); set => SetZoneItem(BodyZone.Torso, value); }
		public Item? Legs { get => Equipment.GetZoneItem(BodyZone.Legs); set => SetZoneItem(BodyZone.Legs, value); }
		public Item? MainHand { get => Equipment.MainHand; set => Equipment.MainHand = value; }
		public Item? OffHand { get => Equipment.OffHand; set => Equipment.OffHand = value; }

		// Accesseurs de compatibilité pour les zones supplémentaires (identiques à celles du joueur)
		public Item? Face { get => Equipment.GetZoneItem(BodyZone.Face); set => SetZoneItem(BodyZone.Face, value); }
		public Item? Ears { get => Equipment.GetZoneItem(BodyZone.Ears); set => SetZoneItem(BodyZone.Ears, value); }
		public Item? Neck { get => Equipment.GetZoneItem(BodyZone.Neck); set => SetZoneItem(BodyZone.Neck, value); }
		public Item? Waist { get => Equipment.GetZoneItem(BodyZone.Waist); set => SetZoneItem(BodyZone.Waist, value); }
		public Item? Feet { get => Equipment.GetZoneItem(BodyZone.Feet); set => SetZoneItem(BodyZone.Feet, value); }
		public Item? Back { get => Equipment.GetZoneItem(BodyZone.Back); set => SetZoneItem(BodyZone.Back, value); }

		// Assigne directement un item sur une zone sans passer par la logique de conflit/inventaire
		// (utilisé pour le chargement de sauvegarde ou la remise à zéro).
		public void SetZoneItem(BodyZone zone, Item? item)
		{
			var existing = Equipment.GetZoneItem(zone);
			if (existing != null) Equipment.UnequipFromBody(existing);
			if (item != null) Equipment.ZoneItems.Add(item);
		}
		
		// NOUVEAU : Pose actuelle du porte-armure
		public string CurrentPose { get; set; } = "idle";
		
		// Liste des poses disponibles (statique)
		public static readonly List<string> AvailablePoses = new List<string> 
		{ 
			"idle", 
			"walk", 
			"wavedance", 
			"posepoint"
		};
		
		// Obtenir la pose suivante dans la liste
		public void NextPose()
		{
			int currentIndex = AvailablePoses.IndexOf(CurrentPose);
			if (currentIndex == -1) currentIndex = 0;
			int nextIndex = (currentIndex + 1) % AvailablePoses.Count;
			CurrentPose = AvailablePoses[nextIndex];
		}
		
		public bool IsEmpty => Equipment.ZoneItems.Count == 0 && MainHand == null && OffHand == null;
		
		public void Clear()
		{
			Equipment.ClearAllEquipment();
			MainHand = null;
			OffHand = null;
		}
	}
	
	public class SteamEngineData
	{
		public const float MaxWater = 10f;
		public const float MaxCoal = 5f;
		public const float WaterConsumptionPerSecond = 1f / 90f;
		public const float CoalConsumptionPerSecond = 1f / 60f;

		public float Water { get; set; } = 0f;          // litres
		public float Coal { get; set; } = 0f;           // unités (charbon)
		public float SteamPressure { get; set; } = 0f;  // 0-100
		public float ProductionRate { get; set; } = 0f; // kW produits
		public float LastUpdateTime { get; set; }
		public bool IsOn { get; set; } = false;
	}

	// Données d'un collecteur de pluie
	public class RainCollectorData
	{
		public const float MaxCapacity = 5f;
		public const float DefaultFillRate = 0.01f; // litres par seconde en pluie

		public float CurrentWater { get; set; } = 0f; // litres
		public float Capacity { get; set; } = MaxCapacity; // litres max
		public float FillRate { get; set; } = DefaultFillRate;
		public float LastUpdateTime { get; set; }
	}

	// Données d’une dynamo
	public class DynamoData
	{
		public float SteamInput { get; set; } = 0f;     // reçu de la machine adjacente
		public float ElectricityOutput { get; set; } = 0f; // kW produits
		public List<EnergyConnection> Connections { get; set; } = new();
		public float LastUpdateTime { get; set; }
	}

	// Données d’un chargeur/batterie
	public class BatteryData
	{
		public float StoredEnergy { get; set; } = 0f;   // Wh
		public float MaxCapacity { get; set; } = 10000f;
		public List<EnergyConnection> Connections { get; set; } = new();
	}

	// Une connexion électrique entre deux tuiles
	public class EnergyConnection
	{
		public int X1, Y1;   // source (dynamo ou batterie)
		public int X2, Y2;   // destination (autre batterie, lampe, etc.)
		public Guid Id { get; set; } = Guid.NewGuid();
	}

	// Un point de branchement de câble sur une tuile électrique (décrit dans worldobjects.json).
	// "input" = la tuile peut RECEVOIR un câble (elle consomme/stocke de l'énergie).
	// "output" = la tuile peut ÉMETTRE un câble (elle fournit de l'énergie).
	public class EnergyPort
	{
		public string Type { get; set; } = "input"; // "input" ou "output"
		public int Dx { get; set; } = 0;  // décalage par rapport à l'origine de la tuile (en cases)
		public int Dy { get; set; } = 0;
		public string? Label { get; set; } // ex: "Entrée câble", "Sortie électrique"
	}
    
	// Données d'un chargeur de batterie (socle sur lequel on pose/retire une batterie
	// portable avec E ; le socle possède une ENTRÉE de câble qui l'alimente).
	public class BatteryChargerData
	{
		public BatteryData? InsertedBattery { get; set; }
		public bool IsOn { get; set; } = true;
		public bool HasPower { get; set; } = false; // reflète l'état du câble d'entrée (pour l'affichage)
		public float LastUpdateTime { get; set; }
	}
	
	// Données d'une ampoule électrique
	public class AmpouleData
	{
		public bool IsOn { get; set; } = false;
		public float LastUpdateTime { get; set; }
		public bool WasLitLastFrame { get; set; } = false;
	}

	// Type de porte logique
	public enum GateType
	{
		AND,   // ET : sortie active seulement si TOUTES les entrées connectées sont actives (2 entrées mini)
		OR,    // OU : sortie active si AU MOINS UNE entrée est active
		NAND,  // NON-ET : inverse de AND
		NOR,   // NON-OU : inverse de OR
		XOR,   // OU-EXCLUSIF : sortie active si un nombre IMPAIR d'entrées sont actives
		NOT,   // NON : inverseur à une seule entrée (sortie = inverse de l'entrée)
		SWITCH, // Interrupteur : source manuelle (ON/OFF), utile pour simuler une condition ("si")
		PRESSURE_PLATE, // Plaque de pression : PAS une source d'énergie, un simple RELAIS MÉCANIQUE.
		               // Elle possède une ENTRÉE (câblée à une batterie/alimentation) et une SORTIE
		               // (vers ce qu'elle alimente) ; elle ne fait que fermer/ouvrir le contact entre
		               // les deux. Le courant ne passe que pendant qu'un poids (joueur, animal...)
		               // presse physiquement le mécanisme ET qu'un courant arrive réellement en
		               // entrée ; il se coupe instantanément dès qu'on la quitte (pas de bascule
		               // persistante comme l'interrupteur, et pas de courant sans source en amont).
		DELAY          // Bloc de délai : RELAIS lui aussi (entrée + sortie, pas de courant sans
		               // entrée câblée), mais qui retransmet l'état de son entrée après un temps
		               // d'attente configurable (voir LogicGateData.DelaySeconds, réglable avec
		               // Maj+E). Contrairement à la plaque, il n'a pas besoin d'un poids : il
		               // retarde simplement tout changement d'état (front montant ET descendant)
		               // d'une durée fixe, sans jamais perdre une transition trop courte.
	}

	// Données d'une porte logique / interrupteur.
	// Comme le câblage du jeu ne relie que des tuiles (pas de ports individuels), une porte
	// logique évalue simplement le nombre de fils entrants connectés à sa tuile et combien
	// d'entre eux sont actuellement alimentés (par une dynamo, une batterie ou une autre porte).
	public class LogicGateData
	{
		public GateType Type { get; set; } = GateType.AND;
		public bool OutputState { get; set; } = false;   // état de sortie actuel (alimente Connections)
		public int ConnectedInputs { get; set; } = 0;    // nombre de fils entrants détectés (debug/UI)
		public int ActiveInputs { get; set; } = 0;        // nombre de fils entrants actuellement alimentés (debug/UI)
		public bool ManualState { get; set; } = false;    // état ON/OFF pour un interrupteur (GateType.SWITCH)
		public bool PendingPress { get; set; } = false;   // PRESSURE_PLATE uniquement : mis à true dès
		                                                   // qu'un poids est détecté sur la tuile, à
		                                                   // n'importe quelle frame depuis le dernier
		                                                   // tic électrique (0.5s) ; consommé (remis à
		                                                   // false) à chaque tic. Évite de rater un
		                                                   // passage plus rapide que l'intervalle de
		                                                   // simulation électrique, sans avoir à faire
		                                                   // tourner toute la grille à 60 FPS.
		public float DelaySeconds { get; set; } = 1f;     // DELAY uniquement : durée d'attente avant
		                                                   // de retransmettre l'entrée en sortie.
		                                                   // Réglable en jeu avec Maj+E (World.CycleDelayBlock).
		public bool DelayLastRawInput { get; set; } = false; // DELAY uniquement : dernier état brut connu
		                                                       // de l'entrée (avant application du délai),
		                                                       // pour détecter les fronts montants/descendants.
		public List<(float ReadyAt, bool State)> DelayQueue { get; set; } = new(); // DELAY uniquement :
		                                                   // file FIFO des transitions en attente
		                                                   // (horodatage d'application, nouvel état),
		                                                   // pour ne jamais perdre un front, même bref.
		public List<EnergyConnection> Connections { get; set; } = new(); // sorties, comme une dynamo/batterie
		public float LastUpdateTime { get; set; }
	}
	
    public class DraggedItem
    {
        public Item? Item;
        public int Count;
        public bool IsDragging;
        public Vector2 Position;
        public int SourceInventorySlot = -1;
        public EquipmentSlot? SourceEquipmentSlot = null;
        public int SourceEquipmentIndex = -1;
        public BodyZone? SourceBodyZone = null;
        
        public void StartDrag(Item item, int count, Vector2 pos, int sourceInventorySlot = -1, EquipmentSlot? sourceEquipmentSlot = null, int sourceEquipmentIndex = -1, BodyZone? sourceBodyZone = null)
        {
            Item = item;
            Count = Math.Max(1, count);
            IsDragging = true;
            Position = pos;
            SourceInventorySlot = sourceInventorySlot;
            SourceEquipmentSlot = sourceEquipmentSlot;
            SourceEquipmentIndex = sourceEquipmentIndex;
            SourceBodyZone = sourceBodyZone;
        }
        
        public void EndDrag()
        {
            Item = null;
            Count = 0;
            IsDragging = false;
            SourceInventorySlot = -1;
            SourceEquipmentSlot = null;
            SourceEquipmentIndex = -1;
            SourceBodyZone = null;
        }
    }
	
	// Item tenu en main (pour l'affichage)
	public class HeldItem
	{
		public Item? Item;
		public float BobOffset;
		public float BobSpeed = 8f;
		
		public HeldItem()
		{
			BobOffset = 0f;
		}
		
		public void Update(float dt)
		{
			BobOffset += dt * BobSpeed;
			if (BobOffset > MathF.PI * 2) BobOffset -= MathF.PI * 2;
		}
		
		public float GetBobHeight()
		{
			return MathF.Sin(BobOffset) * 4f;
		}
		
		public float GetBobRotation()
		{
			return MathF.Sin(BobOffset * 1.5f) * 5f;
		}
	}
    
    // État du panneau d'artisanat
    public enum CraftPanelState
    {
        Collapsed,   // Replié (seulement l'icône dépasse)
        Expanded     // Déplié (affichage complet)
    }
	
public enum RenderItemKind
{
	None,
	External,
	GroundItem,
	WorldEntity,
	Player,
	Car,
	Wire,
	Projectile,
	WorldObject,
	Icon,
}

public struct RenderItem
{
	public float Y;
	public int SortLayer;
	public RenderItemKind Kind;
	public Action? DrawAction;
	public object? Tag;

	//  Départage stable des égalités de Y (tri par index d'insertion). Sans cela, quand deux
	// "gros sprites" (ex : deux arbres voisins sur la même ligne de tuiles) ont exactement le
	// même Y de tri, List<T>.Sort() n'est PAS un tri stable : l'ordre relatif de ces deux éléments
	// à Y égal peut changer d'une frame à l'autre selon ce qui se passe ailleurs dans la liste
	// (entités qui bougent, ajout/suppression d'éléments...). Résultat visuel : les deux sprites
	// se repassent devant/derrière en clignotant. En cassant systématiquement les égalités par
	// l'ordre d'insertion (assigné juste avant le tri, voir World_Core), on obtient un ordre total
	// déterministe et donc un rendu stable d'une frame à l'autre.
	public int SortIndex;

	public void Draw()
	{
		if (DrawAction != null)
		{
			DrawAction.Invoke();
			return;
		}

		if (Tag is null)
			return;

		switch (Kind)
		{
			case RenderItemKind.GroundItem:
				if (Tag is GroundItemRenderTag groundTag)
					World.DrawGroundItemTag(groundTag);
				break;
			case RenderItemKind.WorldEntity:
				if (Tag is WorldEntityRenderTag entityTag)
					World.DrawWorldEntityTag(entityTag);
				break;
			case RenderItemKind.Player:
				if (Tag is PlayerRenderTag playerTag)
					World.DrawPlayerRenderTag(playerTag);
				break;
			case RenderItemKind.Car:
				if (Tag is CarRenderTag carTag)
					World.DrawCarRenderTag(carTag);
				break;
			case RenderItemKind.Wire:
				if (Tag is WireRenderTag wireTag)
					World.DrawWireRenderTag(wireTag);
				break;
			case RenderItemKind.Projectile:
				if (Tag is ProjectileRenderTag projectileTag)
					World.DrawProjectileRenderTag(projectileTag);
				break;
			case RenderItemKind.WorldObject:
				if (Tag is WorldObjectRenderTag objectTag)
					World.DrawWorldObjectRenderTag(objectTag);
				break;
			case RenderItemKind.Icon:
				if (Tag is IconRenderTag iconTag)
					World.DrawIconRenderTag(iconTag);
				break;
		}
	}
}

public readonly struct GroundItemRenderTag
{
	public readonly GroundItem GroundItem;
	public readonly ItemData ItemData;
	public readonly Color ItemColor;
	public readonly bool IsLootbag;
	public readonly float ShadowWidth;
	public readonly float AlphaBottom;
	public readonly float Angle;
	public readonly Vector2 DrawPos;
	public readonly float ItemSize;
	public readonly string? Metadata;
	public readonly Color? CustomColor;
	public GroundItemRenderTag(GroundItem groundItem, ItemData itemData, Color itemColor, bool isLootbag, float shadowWidth, float alphaBottom, float angle, Vector2 drawPos, float itemSize, string? metadata, Color? customColor)
	{
		GroundItem = groundItem;
		ItemData = itemData;
		ItemColor = itemColor;
		IsLootbag = isLootbag;
		ShadowWidth = shadowWidth;
		AlphaBottom = alphaBottom;
		Angle = angle;
		DrawPos = drawPos;
		ItemSize = itemSize;
		Metadata = metadata;
		CustomColor = customColor;
	}
}

public readonly struct WorldEntityRenderTag
{
	public readonly Entity Entity;
	public readonly float YOffset;
	public readonly Vector2 TraderIconPos;
	public readonly bool IsTraderIcon;
	public readonly bool ShowStart;
	public readonly bool ShowFinish;
	public readonly bool ShowGuildIcon;
	public readonly bool ShowAlertIcon;
	public readonly bool IsInterestedInFood;
	public readonly bool IsQuestionMark;
	public readonly bool IsWorm;
	public readonly Vector2 IconWorldPos;
	public readonly float VisualOffsetY;
	public readonly float EntityYOffset;
	public readonly float? CurrentHPRatio;
	public readonly float? TopY;
	public readonly float? WarningY;
	public WorldEntityRenderTag(Entity entity, float yOffset, Vector2 traderIconPos, bool isTraderIcon, bool showStart, bool showFinish, bool showGuildIcon, bool showAlertIcon, bool isInterestedInFood, bool isQuestionMark, bool isWorm, Vector2 iconWorldPos, float visualOffsetY, float entityYOffset, float? currentHPRatio, float? topY, float? warningY)
	{
		Entity = entity; YOffset = yOffset; TraderIconPos = traderIconPos; IsTraderIcon = isTraderIcon; ShowStart = showStart; ShowFinish = showFinish; ShowGuildIcon = showGuildIcon; ShowAlertIcon = showAlertIcon; IsInterestedInFood = isInterestedInFood; IsQuestionMark = isQuestionMark; IsWorm = isWorm; IconWorldPos = iconWorldPos; VisualOffsetY = visualOffsetY; EntityYOffset = entityYOffset; CurrentHPRatio = currentHPRatio; TopY = topY; WarningY = warningY;
	}
}

public readonly struct PlayerRenderTag
{
	public readonly Entity? MountedAnimal;
	public readonly Vector2 ActualDrawPos;
	public readonly Vector2 PlayerPos;
	public readonly string SpeciesToDraw;
	public readonly Texture2D MorphHairBase;
	public readonly Texture2D MorphHairOverlay;
	public readonly Color MorphHairColor;
	public readonly Texture2D MorphEyes;
	public readonly Texture2D MorphMouth;
	public readonly Equipment? MorphEquipment;
	public readonly Color MorphTint;
	public readonly Texture2D UnderwearTex;
	public readonly Texture2D HeldItemTex;
	public readonly Item? HeldItem;
	public readonly int PlayerFrame;
	public readonly int PlayerAnim;
	public readonly float PlayerProg;
	public readonly float Facing;
	public readonly bool IsCarryingCreature;
	public readonly bool IsCarrying;
	public readonly bool IsFurniture;
	public readonly bool IsBow;
	public readonly bool IsPlayerInWater;
	public readonly string? CarriedSpeciesKey;
	public readonly Entity? CarriedCreatureRef;
	public readonly bool IsSitting;
	public PlayerRenderTag(Entity? mountedAnimal, Vector2 actualDrawPos, Vector2 playerPos, string speciesToDraw, Texture2D morphHairBase, Texture2D morphHairOverlay, Color morphHairColor, Texture2D morphEyes, Texture2D morphMouth, Equipment? morphEquipment, Color morphTint, Texture2D underwearTex, Texture2D heldItemTex, Item? heldItem, int playerFrame, int playerAnim, float playerProg, float facing, bool isCarryingCreature, bool isCarrying, bool isFurniture, bool isBow, bool isPlayerInWater, string? carriedSpeciesKey, Entity? carriedCreatureRef, bool isSitting)
	{
		MountedAnimal = mountedAnimal; ActualDrawPos = actualDrawPos; PlayerPos = playerPos; SpeciesToDraw = speciesToDraw; MorphHairBase = morphHairBase; MorphHairOverlay = morphHairOverlay; MorphHairColor = morphHairColor; MorphEyes = morphEyes; MorphMouth = morphMouth; MorphEquipment = morphEquipment; MorphTint = morphTint; UnderwearTex = underwearTex; HeldItemTex = heldItemTex; HeldItem = heldItem; PlayerFrame = playerFrame; PlayerAnim = playerAnim; PlayerProg = playerProg; Facing = facing; IsCarryingCreature = isCarryingCreature; IsCarrying = isCarrying; IsFurniture = isFurniture; IsBow = isBow; IsPlayerInWater = isPlayerInWater; CarriedSpeciesKey = carriedSpeciesKey; CarriedCreatureRef = carriedCreatureRef; IsSitting = isSitting;
	}
}

public readonly struct CarRenderTag
{
	public readonly Car Car;
	public CarRenderTag(Car car) { Car = car; }
}

public readonly struct WireRenderTag
{
	public readonly Vector2 P1;
	public readonly Vector2 P2;
	public readonly Vector2 Mid;
	public readonly bool Powered;
	public readonly float Pulse;
	public WireRenderTag(Vector2 p1, Vector2 p2, Vector2 mid, bool powered, float pulse)
	{
		P1 = p1; P2 = p2; Mid = mid; Powered = powered; Pulse = pulse;
	}
}

public readonly struct ProjectileRenderTag
{
	public readonly SpellProjectile Projectile;
	public readonly Vector2 Position;
	public readonly float Size;
	public readonly float Rotation;
	public readonly Texture2D VisualTexture;
	public readonly Color Color;
	public readonly bool HasTexture;
	public ProjectileRenderTag(SpellProjectile projectile, Vector2 position, float size, float rotation, Texture2D visualTexture, Color color, bool hasTexture)
	{
		Projectile = projectile; Position = position; Size = size; Rotation = rotation; VisualTexture = visualTexture; Color = color; HasTexture = hasTexture;
	}
}

public readonly struct WorldObjectRenderTag
{
	public readonly int X;
	public readonly int Y;
	public readonly float DrawX;
	public readonly float DrawY;
	public readonly int DrawW;
	public readonly int DrawH;
	public readonly int ObjectId;
	public readonly int OverlayId;
	public readonly Texture2D Texture;
	public readonly Texture2D MissingTexture;
	public readonly bool IsTile;
	public readonly int BasedTile;
	public readonly float SortY;
	public WorldObjectRenderTag(int x, int y, float drawX, float drawY, int drawW, int drawH, int objectId, int overlayId, Texture2D texture, Texture2D missingTexture, bool isTile, int basedTile, float sortY)
	{
		X = x; Y = y; DrawX = drawX; DrawY = drawY; DrawW = drawW; DrawH = drawH; ObjectId = objectId; OverlayId = overlayId; Texture = texture; MissingTexture = missingTexture; IsTile = isTile; BasedTile = basedTile; SortY = sortY;
	}
}

public readonly struct IconRenderTag
{
	public readonly Vector2 BasePos;
	public readonly List<Texture2D> Textures;
	public readonly float IconSize;
	public readonly float YOffsetIcon;
	public readonly float Step;
	public readonly int Count;
	public readonly Texture2D AlertTexture;
	public readonly float Pulse;
	public readonly bool IsAlertIcon;
	public readonly bool IsQuestionIcon;
	public readonly float YOffset;
	public IconRenderTag(Vector2 basePos, List<Texture2D> textures, float iconSize, float yOffsetIcon, float step, int count, Texture2D alertTexture, float pulse, bool isAlertIcon, bool isQuestionIcon, float yOffset)
	{
		BasePos = basePos; Textures = textures; IconSize = iconSize; YOffsetIcon = yOffsetIcon; Step = step; Count = count; AlertTexture = alertTexture; Pulse = pulse; IsAlertIcon = isAlertIcon; IsQuestionIcon = isQuestionIcon; YOffset = yOffset;
	}
}
	
	public class Guild
	{
		public string Name { get; set; } = "Guilde";
		public Color[,] Logo { get; set; } // 32×32 pixels
		// NetId des PNJ ayant rejoint la guilde (recrutés via amitié maximale).
		public List<Guid> MemberIds { get; set; } = new();

		public Guild()
		{
			Logo = new Color[32, 32];
			// Logo par défaut : damier noir/blanc
			for (int x = 0; x < 32; x++)
				for (int y = 0; y < 32; y++)
					Logo[x, y] = ((x + y) % 2 == 0) ? Color.Black : Color.White;
		}
	}
	
	public static class GuildStorage
	{
		public const string EMBLEMS_DIR = "GuildEmblems";
		
		/// <summary>
		/// Sauvegarde un blason dans un fichier .png
		/// </summary>
		public static void SaveEmblem(string name, Color[,] logo)
		{
			if (!Directory.Exists(EMBLEMS_DIR))
				Directory.CreateDirectory(EMBLEMS_DIR);
			
			int size = logo.GetLength(0);
			Image img = Raylib.GenImageColor(size, size, Color.Blank);
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
					Raylib.ImageDrawPixel(ref img, x, y, logo[x, y]);
			
			string path = Path.Combine(EMBLEMS_DIR, $"{name}.png");
			Raylib.ExportImage(img, path);
			Raylib.UnloadImage(img);
			Console.WriteLine($" Blason sauvegardé : {path}");
		}
		
		/// <summary>
		/// Charge un blason depuis un fichier .png
		/// </summary>
		public static Color[,]? LoadEmblem(string name)
		{
			string path = Path.Combine(EMBLEMS_DIR, $"{name}.png");
			if (!File.Exists(path)) return null;
			
			Image img = Raylib.LoadImage(path);
			int size = img.Width;
			Color[,] logo = new Color[size, size];
			
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
					logo[x, y] = Raylib.GetImageColor(img, x, y);
			
			Raylib.UnloadImage(img);
			return logo;
		}
		
		/// <summary>
		/// Liste tous les blasons disponibles dans le dossier
		/// </summary>
		public static List<string> GetAvailableEmblems()
		{
			if (!Directory.Exists(EMBLEMS_DIR))
				return new List<string>();
			
			return Directory.GetFiles(EMBLEMS_DIR, "*.png")
				.Select(f => Path.GetFileNameWithoutExtension(f))
				.OrderBy(n => n)
				.ToList();
		}
	}
	
	public class FishingBobber
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public const float Gravity = 600f;
		public float WaterDrag = 0.9f;
		public bool IsInWater = false;
		public bool IsAlive = true;
		public bool IsBiting = false;
		public float BiteTimer = 0f;
		public float BiteDelay = 2f; // délai avant morsure possible
		public float BiteDuration = 0.8f; // temps pour ferrer
		public float BiteProgress = 0f;
		public bool Caught = false;
		public float BobOffset = 0f;
		public float BobSpeed = 3f;
		public int WaterTileX, WaterTileY;
		public float LifeTime = 0f;
		public const float MaxLifeTime = 15f;
		
		public FishingBobber(Vector2 startPos, Vector2 velocity)
		{
			Position = startPos;
			Velocity = velocity;
		}
		
		public void Update(float dt)
		{
			if (!IsAlive) return;
			LifeTime += dt;
			if (LifeTime > MaxLifeTime)
			{
				IsAlive = false;
				return;
			}
			
			if (!IsInWater)
			{
				Velocity.Y += Gravity * dt;
				Position += Velocity * dt;
				
				// Vérifier si on touche l'eau avec les coordonnées monde
				int groundId = World.GetGroundTileIdAt(Position.X, Position.Y);
				if (groundId == 10 || groundId == 11)
				{
					IsInWater = true;
					int tileX = (int)(Position.X / Program.TileSize);
					int tileY = (int)(Position.Y / Program.TileSize);
					WaterTileX = tileX;
					WaterTileY = tileY;
					Velocity = Vector2.Zero;
					int height = World.GetHeightAt(tileX, tileY);
					float yOffset = -height * Program.TileSize / 4;
					Position = new Vector2(Position.X, tileY * Program.TileSize + yOffset + Program.TileSize * 0.5f);
				}
				else if (Position.Y > 10000)
				{
					IsAlive = false;
				}
			}
			else
			{
				BobOffset = MathF.Sin(LifeTime * BobSpeed) * 3f;
				
				if (!IsBiting && !Caught)
				{
					BiteTimer += dt;
					if (BiteTimer >= BiteDelay)
					{
						if (Random.Shared.NextDouble() < dt * 0.5f)
						{
							IsBiting = true;
							BiteProgress = 0f;
						}
					}
				}
				else if (IsBiting && !Caught)
				{
					BiteProgress += dt;
					if (BiteProgress > BiteDuration)
					{
						IsBiting = false;
						BiteTimer = 0f;
					}
					BobOffset -= MathF.Sin(BiteProgress * 10f) * 5f;
				}
			}
		}
	}

    public class Item
	{
		private const string BASE_COLOR_METADATA_KEY = "color";
		private const string CUSTOM_COLOR_METADATA_PREFIX = "color.";
		public string Name;
		public int Count;
		public List<Color?> CustomColors = new();  // Liste au lieu d'une seule couleur
		public Texture2D Icon;
		public PortableContainerData? Container { get; set; }
		public BackpackData? Backpack { get; set; }
		public string Metadata = "";
		[JsonIgnore]
		public float CampfireCookSeconds;
		[JsonIgnore]
		public float CampfireBurnSeconds;
		// Compatibilite temporaire pour les anciennes donnees chargees en memoire.
		public Dictionary<string, string> Meta { get; set; } = new(StringComparer.OrdinalIgnoreCase);

		public bool HasMeta(string key) => !string.IsNullOrEmpty(GetMetadataValue(key));

		public string GetMeta(string key, string defaultValue = "") =>
			GetMetadataValue(key, defaultValue);

		public string GetMetadataValue(string key, string defaultValue = "")
		{
			foreach (var part in (Metadata ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				int separatorIndex = part.IndexOf('=');
				if (separatorIndex <= 0) continue;
				if (string.Equals(part.Substring(0, separatorIndex), key, StringComparison.OrdinalIgnoreCase))
					return part.Substring(separatorIndex + 1);
			}
			return Meta.TryGetValue(key, out var legacyValue) ? legacyValue : defaultValue;
		}

		public void SetMetadataValue(string key, string value)
		{
			var values = new List<string>();
			foreach (var part in (Metadata ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				int separatorIndex = part.IndexOf('=');
				if (separatorIndex > 0)
				{
					string existingKey = part.Substring(0, separatorIndex);
					if (!string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
						values.Add(part);
				}
				else
					values.Add(part);
			}
			values.Add($"{key}={value}");
			Metadata = string.Join(";", values);
		}

		/// <summary>Définit une clé de métadonnée ; une valeur nulle/vide supprime la clé (évite d'accumuler des entrées inutiles à sérialiser).</summary>
		public void SetMeta(string key, string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				var parts = (Metadata ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
					.Where(part =>
					{
						int separatorIndex = part.IndexOf('=');
						return separatorIndex <= 0 || !string.Equals(part.Substring(0, separatorIndex), key, StringComparison.OrdinalIgnoreCase);
					});
				Metadata = string.Join(";", parts);
				return;
			}

			SetMetadataValue(key, value);
			Meta[key] = value;
		}

		public void RemoveMeta(string key) => SetMeta(key, null);

		public string SerializeMetadataForSave()
		{
			return Metadata ?? "";
		}

		private Color GetColorMetadata(string key, Color fallback)
		{
			string encoded = GetMetadataValue(key);
			if (string.IsNullOrWhiteSpace(encoded)) return fallback;
			var parts = encoded.Split(',');
			return parts.Length == 4 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g)
				&& byte.TryParse(parts[2], out var b) && byte.TryParse(parts[3], out var a)
				? new Color(r, g, b, a)
				: fallback;
		}

		private void SetColorMetadata(string key, Color color)
		{
			SetMetadataValue(key, $"{color.R},{color.G},{color.B},{color.A}");
		}

		/// <summary>Synchronise les couleurs de compatibilite dans les metadonnees persistantes.</summary>
		public void SyncColorsToMetadata()
		{
			for (int index = 0; index < CustomColors.Count; index++)
			{
				string key = $"{CUSTOM_COLOR_METADATA_PREFIX}{index + 1}";
				if (CustomColors[index].HasValue && !IsWhite(CustomColors[index]!.Value))
					SetColorMetadata(key, CustomColors[index]!.Value);
				else
					RemoveMeta(key);
			}
		}

		public Color GetLayerColor(int layer)
		{
			return GetColorMetadata(layer == 0 ? BASE_COLOR_METADATA_KEY : $"{CUSTOM_COLOR_METADATA_PREFIX}{layer}", Color.White);
		}

		public void SetLayerColor(int layer, Color color)
		{
			SetColorMetadata(layer == 0 ? BASE_COLOR_METADATA_KEY : $"{CUSTOM_COLOR_METADATA_PREFIX}{layer}", color);
		}

		private static bool IsWhite(Color color) => color.R == 255 && color.G == 255 && color.B == 255 && color.A == 255;

		/// <summary>Recharge les couleurs depuis Metadata, avec repli sur les anciennes donnees.</summary>
		public void LoadColorsFromMetadata()
		{
			var colors = new List<Color?>();
			for (int index = 1; HasMeta($"{CUSTOM_COLOR_METADATA_PREFIX}{index}"); index++)
				colors.Add(GetColorMetadata($"{CUSTOM_COLOR_METADATA_PREFIX}{index}", Color.White));
			if (colors.Count > 0)
				CustomColors = colors;
		}

		public void RestoreMetadataFromSave(string? serializedMetadata)
		{
			if (string.IsNullOrEmpty(serializedMetadata))
			{
				Metadata = "";
				return;
			}

			// Compatibilite avec l'ancien format META:key=value.
			Metadata = serializedMetadata.Replace("META:", "", StringComparison.OrdinalIgnoreCase);
			LoadColorsFromMetadata();
		}

		//  SYSTÈME DE RUNES : emplacements sertis sur cette pièce d'armure (null = slot vide).
		//  La taille de la liste est synchronisée avec ItemData.RuneSlots via EnsureRuneSlots().
		public List<RuneData?> SocketedRunes = new();

		/// <summary>S'assure que la liste de slots a la bonne taille (appelé à l'équipement / au chargement).</summary>
		public void EnsureRuneSlots(int slotCount)
		{
			if (slotCount < 0) slotCount = 0;
			while (SocketedRunes.Count < slotCount) SocketedRunes.Add(null);
			while (SocketedRunes.Count > slotCount) SocketedRunes.RemoveAt(SocketedRunes.Count - 1);
		}

		/// <summary>Index du premier emplacement de rune libre, ou -1 si aucun.</summary>
		public int FirstFreeRuneSlot() => SocketedRunes.FindIndex(r => r == null);

		/// <summary>
		/// Multiplicateur de puissance apporté par les runes Solaire/Givrée serties sur CET item.
		/// Contrairement aux autres runes (bonus additifs cumulés sur tout l'équipement), ces deux
		/// runes boostent tous les effets de la pièce qui les porte (armure ET autres runes serties
		/// dessus). Cumulatif si plusieurs runes de puissance sont serties sur le même item.
		/// </summary>
		public float GetPowerMultiplier()
		{
			float multiplier = 1f;
			foreach (var rune in SocketedRunes)
			{
				if (rune != null && (rune.Type == RuneType.Solar || rune.Type == RuneType.Frost))
					multiplier *= 1f + (rune.Value / 100f);
			}
			return multiplier;
		}

		private const string RUNES_METADATA_PREFIX = "RUNES:";

		/// <summary>
		/// Encode les runes serties dans Item.Metadata (armures uniquement ; le champ n'est utilisé par
		/// rien d'autre pour ce type d'item). Appelé après chaque sertissage pour que la sauvegarde,
		/// qui persiste déjà Metadata, embarque automatiquement les runes.
		/// </summary>
		public void SyncRunesToMetadata()
		{
			if (SocketedRunes.Count == 0 || SocketedRunes.All(r => r == null))
			{
				RemoveMeta("runes");
				return;
			}
			var parts = SocketedRunes.Select(r => r == null ? "_" : $"{r.Type}:{r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
			SetMetadataValue("runes", string.Join(",", parts));
		}

		/// <summary>
		/// Reconstruit SocketedRunes à partir de Item.Metadata (appelé au chargement d'une sauvegarde).
		/// Sans effet si Metadata ne contient pas de runes encodées.
		/// </summary>
		public void LoadRunesFromMetadata()
		{
			string encoded = GetMetadataValue("runes");
			if (string.IsNullOrEmpty(encoded) && Metadata.StartsWith(RUNES_METADATA_PREFIX, StringComparison.OrdinalIgnoreCase))
				encoded = Metadata.Substring(RUNES_METADATA_PREFIX.Length);
			if (string.IsNullOrEmpty(encoded)) return;

			var slots = encoded.Contains(',') ? encoded.Split(',') : encoded.Split(';');
			SocketedRunes = slots.Select(s =>
			{
				if (s == "_" || string.IsNullOrEmpty(s)) return (RuneData?)null;
				return RuneData.FromMetadata(s.Replace(':', '|'));
			}).ToList();
		}

		public Item(string name, int count, Color baseColor, Texture2D icon, Color? customColor = null)
		{
			Name = name;
			Count = count;
			Icon = icon;
			if (customColor.HasValue)
			{
				CustomColors = new List<Color?> { customColor.Value };
			}
			else if (GameData.TryGetItemByName(name, out var itemData) && itemData.IsDyeable)
			{
				int dyeSlots = Math.Max(itemData.DyeLayers - 1, 1);
				CustomColors = Enumerable.Repeat<Color?>(Color.White, dyeSlots).ToList();
				for (int layer = 0; layer < dyeSlots; layer++)
					SetLayerColor(layer, Color.White);
			}
			if (customColor.HasValue)
				SetLayerColor(0, customColor.Value);
		}
		
		// Propriété de compatibilité pour l'ancien code
		public Color? CustomColor 
		{ 
			get => CustomColors.Count > 0 ? CustomColors[0] : null;
			set
			{
				if (CustomColors.Count == 0)
					CustomColors.Add(value);
				else
					CustomColors[0] = value;
			}
		}

		public Color BaseColor => GetLayerColor(0);
		public Color DisplayColor => GetLayerColor(0);
	}

	// ─── SYSTÈME DE RUNES ────────────────────────────────────────────────────────
	//  Solar et Frost sont des runes de PUISSANCE : contrairement aux autres, elles
	// n'ajoutent pas un bonus propre mais multiplient tous les effets de la pièce
	// d'équipement sur laquelle elles sont serties (voir Item.GetPowerMultiplier()).
	public enum RuneType { Resistance, MaxHealth, Speed, Solar, Frost }

	/// <summary>
	/// Pouvoir porté par une pierre runique (item Metadata) ou serti dans une armure
	/// (Item.SocketedRunes).
	/// </summary>
	public class RuneData
	{
		public RuneType Type { get; set; } = RuneType.Resistance;
		public float Value { get; set; } = 1f;

		/// <summary>Encode la rune pour la stocker dans Item.Metadata (pierre non sertie).</summary>
		public string ToMetadata() => $"{Type}|{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

		public static RuneData FromMetadata(string meta)
		{
			var parts = (meta ?? "").Split('|');
			var type = parts.Length > 0 && Enum.TryParse<RuneType>(parts[0], out var t) ? t : RuneType.Resistance;
			var value = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1f;
			return new RuneData { Type = type, Value = value };
		}

		public string DisplayName => Type switch
		{
			RuneType.Resistance => $"+{Value:0.#} Résistance",
			RuneType.MaxHealth   => $"+{Value:0.#} PV max",
			RuneType.Speed       => $"+{Value:0.#}% Vitesse",
			RuneType.Solar       => $"+{Value:0.#}% Puissance (Solaire)",
			RuneType.Frost       => $"+{Value:0.#}% Puissance (Givrée)",
			_ => ""
		};

		public Color DisplayColor => Type switch
		{
			RuneType.Resistance => new Color(160, 160, 220, 255),
			RuneType.MaxHealth   => new Color(220, 100, 100, 255),
			RuneType.Speed       => new Color(120, 220, 160, 255),
			RuneType.Solar       => new Color(255, 170, 40, 255),   // orange doré
			RuneType.Frost       => new Color(140, 220, 255, 255),  // bleu glacé
			_ => Color.White
		};
	}

	// ─── BANNIÈRES DE VILLAGE ────────────────────────────────────────────────────
	public class BannerHolderData
	{
		public bool HasBanner { get; set; } = true;
		public string PatternName { get; set; } = "";
		public string ShapeName  { get; set; } = "";
		public byte BgR { get; set; } = 180;
		public byte BgG { get; set; } = 20;
		public byte BgB { get; set; } = 20;
		public byte PatR { get; set; } = 240;
		public byte PatG { get; set; } = 200;
		public byte PatB { get; set; } = 50;

		public Color BackgroundColor => new Color(BgR, BgG, BgB, (byte)255);
		public Color PatternColor    => new Color(PatR, PatG, PatB, (byte)255);

		/// <summary>Encode le style pour le stocker dans Item.Metadata</summary>
		public string ToMetadata() => $"{PatternName}|{ShapeName}";

		public static BannerHolderData FromMetadata(string meta, List<Color?> colors)
		{
			var parts = meta.Split('|');
			var d = new BannerHolderData
			{
				HasBanner   = true,
				PatternName = parts.Length > 0 ? parts[0] : "",
				ShapeName   = parts.Length > 1 ? parts[1] : "",
			};
			if (colors.Count > 0 && colors[0].HasValue)
			{
				d.BgR = colors[0]!.Value.R;
				d.BgG = colors[0]!.Value.G;
				d.BgB = colors[0]!.Value.B;
			}
			if (colors.Count > 1 && colors[1].HasValue)
			{
				d.PatR = colors[1]!.Value.R;
				d.PatG = colors[1]!.Value.G;
				d.PatB = colors[1]!.Value.B;
			}
			return d;
		}
	}
	
    public enum ItemType { None, Tool, Weapon, Armor, Food, Resource, Placeable, Throwable, WallCovering, Backpack, Instrument, Rune }
    public enum ToolType { None, Axe, Pickaxe, Shovel, Hoe, Scythe, Hammer, Scissors, Fishing, Net, WateringCan }
    public enum WeaponCategory { None, Melee, Ranged }

    public class Equipment
	{
		private static readonly Dictionary<string, Texture2D> EquipmentTextureCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, Dictionary<string, Texture2D>> EquipmentTextureSetCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> MissingEquipmentTexturePaths = new(StringComparer.OrdinalIgnoreCase);

		//  Copie CPU (Image) des textures d'équipement, utilisée UNIQUEMENT pour le test de
		// collision précis au pixel (alpha) sur l'aperçu du personnage dans l'inventaire.
		// Séparée du cache GPU (Texture2D) ci-dessus : ce n'est pas nécessaire pour le rendu
		// en jeu normal, seulement pour déterminer quel item est réellement sous la souris
		// quand plusieurs silhouettes (chapeau, lunettes, boucles d'oreilles...) se chevauchent.
		private static readonly Dictionary<string, Image> EquipmentImageCache = new(StringComparer.OrdinalIgnoreCase);

		public const int BELT_SLOTS = 10;

		//  Charge (ou récupère depuis le cache GPU partagé) la texture d'équipement située à
		// "path", sans dépendre d'une instance d'Equipment ni d'un item réellement porté.
		// Utilisé pour prévisualiser/tester au survol un item PAS ENCORE équipé (drag & drop),
		// avec exactement le même cache que celui utilisé pour l'équipement déjà porté afin de
		// ne jamais charger deux fois la même texture.
		public static Texture2D GetOrLoadEquipTextureByPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return new Texture2D();

			if (EquipmentTextureCache.TryGetValue(path, out var cachedTexture))
				return cachedTexture;

			if (!File.Exists(path))
			{
				MissingEquipmentTexturePaths.Add(path);
				return new Texture2D();
			}

			var texture = Raylib.LoadTexture(path);
			EquipmentTextureCache[path] = texture;
			return texture;
		}

		//  Texture d'un item pour une pièce ("part"/bone) donnée, en respectant EXACTEMENT la
		// même convention de nommage que LoadEquipmentPieceTextures ({TextureName}_{part}[_{layer}].png).
		// Marche pour n'importe quel item, équipé ou non (aperçu fantôme, hit-test au survol).
		public static Texture2D GetItemPartTexture(ItemData itemData, string part, int layer = 1)
		{
			if (string.IsNullOrEmpty(itemData.TextureName))
				return new Texture2D();
			if (layer == 1)
			{
				Texture2D layeredTexture = GetOrLoadEquipTextureByPath($"assets/equipements/{itemData.TextureName}_{part}_1.png");
				if (layeredTexture.Id != 0)
					return layeredTexture;

				return GetOrLoadEquipTextureByPath($"assets/equipements/{itemData.TextureName}_{part}.png");
			}

			return GetOrLoadEquipTextureByPath($"assets/equipements/{itemData.TextureName}_{part}_{layer}.png");
		}

		//  Renvoie true si le pixel local (px, py), exprimé dans l'espace de la texture SOURCE
		// (avant mise à l'échelle, origine en haut à gauche), est opaque. Charge et met en
		// cache une copie CPU de l'image à la demande. C'est ce qui permet à deux silhouettes
		// qui se chevauchent en boîte englobante (ex: chapeau large + lunettes) de rester
		// distinctement cliquables : seul le pixel réellement dessiné à cet endroit compte.
		public static bool IsPixelOpaque(string texturePath, int px, int py, byte alphaThreshold = 10)
		{
			if (string.IsNullOrEmpty(texturePath))
				return false;

			if (!EquipmentImageCache.TryGetValue(texturePath, out var img))
			{
				if (!File.Exists(texturePath))
				{
					EquipmentImageCache[texturePath] = default;
					return false;
				}
				img = Raylib.LoadImage(texturePath);
				EquipmentImageCache[texturePath] = img;
			}

			if (img.Width <= 0 || img.Height <= 0) return false;
			if (px < 0 || py < 0 || px >= img.Width || py >= img.Height) return false;

			Color c = Raylib.GetImageColor(img, px, py);
			return c.A > alphaThreshold;
		}

		//  Système unifié : chaque item porté sur le corps couvre une ou plusieurs BodyZone.
		// Deux items ne peuvent jamais couvrir la même zone en même temps.
		public List<Item> ZoneItems { get; set; } = new();

		// Emplacements fonctionnels (pas de couverture corporelle)
		public Item? MainHand;
		public Item? OffHand;
		public PortableContainerData? OffHandContainer { get; set; }
		public PortableContainerData? MainHandContainer { get; set; }
		public BackpackData? BackpackContainer { get; set; }

		// --- Accesseurs de compatibilité, dérivés du système de zones ---
		// "Head" = item qui couvre le haut de la tête (casque/chapeau), "Body" = torse, "Legs" = jambes,
		// "Backpack" = item qui couvre le dos ET qui a des slots de stockage.
		public Item? Head => GetZoneItem(BodyZone.TopOfHead);
		public Item? Body => GetZoneItem(BodyZone.Torso);
		public Item? Legs => GetZoneItem(BodyZone.Legs);
		public Item? Backpack
		{
			get
			{
				var backItem = GetZoneItem(BodyZone.Back);
				if (backItem == null) return null;
				int id = GetItemId(backItem.Name);
				if (GameData.ItemDatabase.TryGetValue(id, out var data) && (data.Type == ItemType.Backpack || data.BackpackSlots > 0))
					return backItem;
				return null;
			}
		}

		public Item? Glasses => GetZoneItem(BodyZone.Face);
		public Item? Necklace => GetZoneItem(BodyZone.Neck);
		public Item? Waist => GetZoneItem(BodyZone.Waist);
		public Item? Socks => GetZoneItem(BodyZone.Feet);

		// Nouveaux champs pour les textures d'équipement chargées
		public Dictionary<string, Texture2D> EquipmentTextures = new();
		public int TotalSlots => BELT_SLOTS + (Backpack != null ? GetBackpackSlotCount(Backpack.Name) : 0);

		//  L'armure de base de chaque pièce est multipliée par son éventuel(le) rune Solaire/Givrée
		// AVANT d'être sommée, puisque ces runes boostent les effets de LEUR PROPRE pièce.
		public int TotalArmor => (int)Math.Round(ZoneItems.Sum(i =>
				(GameData.ItemDatabase.TryGetValue(GetItemId(i.Name), out var d) ? d.ArmorValue : 0) * i.GetPowerMultiplier()))
			+ (int)Math.Round(GetTotalRuneBonus(RuneType.Resistance));
		public int TotalAttack => 1 + (MainHand != null ? (int)Math.Round(GameData.GetAttack(MainHand.Name) * MainHand.GetPowerMultiplier()) : 0);
		public ToolType HeldTool => MainHand != null ? GameData.GetToolType(MainHand.Name) : ToolType.None;

		/// <summary>
		/// Somme des bonus apportés par toutes les runes serties sur l'équipement, pour un type de
		/// pouvoir donné. Chaque bonus est d'abord multiplié par le GetPowerMultiplier() de SA propre
		/// pièce, donc une rune Solaire/Givrée sertie à côté d'une rune Résistance/PV/Vitesse sur la
		/// même pièce boostera aussi cette dernière.
		/// </summary>
		public float GetTotalRuneBonus(RuneType type)
		{
			float total = 0f;
			foreach (var it in ZoneItems)
			{
				if (it.SocketedRunes.Count == 0) continue;
				float itemMultiplier = it.GetPowerMultiplier();
				foreach (var rune in it.SocketedRunes)
					if (rune != null && rune.Type == type) total += rune.Value * itemMultiplier;
			}
			return total;
		}

		/// <summary>Bonus de PV max apporté par les runes serties sur l'armure équipée (cumulatif).</summary>
		public int TotalRuneMaxHealthBonus => (int)Math.Round(GetTotalRuneBonus(RuneType.MaxHealth));

		/// <summary>Multiplicateur de vitesse apporté par les runes serties sur l'armure équipée (cumulatif, en %).</summary>
		public float TotalRuneSpeedMultiplier => 1f + (GetTotalRuneBonus(RuneType.Speed) / 100f);

		public int GetDamageAfterArmor(int rawDamage)
		{
			if (rawDamage <= 0) return 0;

			int armorPoints = TotalArmor;
			if (armorPoints <= 0) return rawDamage;

			// Réduction progressive : les premiers points d'armure réduisent peu, puis
			// l'effet se renforce sans jamais rendre les dégâts nuls sur un coup.
			float reductionRatio = Math.Min(0.75f, armorPoints / (float)(armorPoints + 50));
			int reducedDamage = (int)Math.Round(rawDamage * (1f - reductionRatio));
			return Math.Max(1, reducedDamage);
		}

		/// <summary>Retourne l'item (s'il y en a un) qui couvre la zone donnée.</summary>
		public Item? GetZoneItem(BodyZone zone)
		{
			foreach (var it in ZoneItems)
			{
				int id = GetItemId(it.Name);
				if (GameData.ItemDatabase.TryGetValue(id, out var data) && data.CoveredZones.Contains(zone))
					return it;
			}
			return null;
		}

		/// <summary>Zones actuellement couvertes par au moins un item équipé.</summary>
		public HashSet<BodyZone> GetCoveredZones()
		{
			var zones = new HashSet<BodyZone>();
			foreach (var it in ZoneItems)
			{
				int id = GetItemId(it.Name);
				if (GameData.ItemDatabase.TryGetValue(id, out var data))
					foreach (var z in data.CoveredZones) zones.Add(z);
			}
			return zones;
		}

		/// <summary>
		/// Équipe un item couvrant une ou plusieurs zones du corps. Si une zone visée est déjà
		/// occupée, l'item en conflit est automatiquement déséquipé et renvoyé (le paramètre out
		/// unequippedItems liste les items retirés, à replacer dans l'inventaire de l'appelant).
		/// Retourne false uniquement si l'item ne couvre aucune zone.
		/// </summary>
		public bool EquipOnBody(Item item, out List<Item> unequippedItems)
		{
			unequippedItems = new List<Item>();
			if (item == null) return false;

			int itemId = GetItemId(item.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return false;
			if (itemData.CoveredZones.Count == 0) return false;

			// Déséquiper tout item qui couvre déjà une des zones visées
			foreach (var zone in itemData.CoveredZones)
			{
				var conflicting = GetZoneItem(zone);
				if (conflicting != null && !unequippedItems.Contains(conflicting))
				{
					UnequipFromBody(conflicting);
					unequippedItems.Add(conflicting);
				}
			}

			// Assurer que les variantes de craft (coat : sleeves/opening) sont correctement
			// initialisées ou respectées avant d'ajouter l'item à l'équipement.
			SaveSystem.RestoreCoatVariantMeta(item, itemData);

			ZoneItems.Add(item);

			if (itemData.CoveredZones.Contains(BodyZone.Back))
				EnsureBackpackContainer(item);

			return true;
		}

		/// <summary>Déséquipe un item porté sur le corps (identifié par référence).</summary>
		public Item? UnequipFromBody(Item item)
		{
			if (item == null || !ZoneItems.Remove(item)) return null;

			if (item == Backpack || (BackpackContainer != null && item.Backpack == BackpackContainer))
			{
				BackpackContainer = null;   // On détache seulement le conteneur actif de l’équipement
			}

			return item;
		}

		/// <summary>Déséquipe l'item qui couvre une zone donnée (s'il y en a un).</summary>
		public Item? UnequipZone(BodyZone zone)
		{
			var item = GetZoneItem(zone);
			return item != null ? UnequipFromBody(item) : null;
		}

		// --- Emplacements fonctionnels (arme, sac à dos vide, monture...) ---
		public Dictionary<(EquipmentSlot slot, int index), Item> EquippedItems { get; set; } = new();

		public bool EquipItem(Item item, EquipmentSlot targetSlot, int index = 0)
		{
			if (item == null) return false;

			int itemId = GetItemId(item.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return false;

			// Vérifier que l'item peut être équipé dans ce slot fonctionnel
			if (!itemData.HasEquipSlot || itemData.EquipSlot != targetSlot) return false;

			// Vérifier la limite de quantité
			int currentCount = GetEquipCount(targetSlot);
			if (currentCount >= itemData.EquipMax) return false;

			// Vérifier si le slot est libre
			if (EquippedItems.ContainsKey((targetSlot, index))) return false;

			EquippedItems[(targetSlot, index)] = item;
			switch (targetSlot)
			{
				case EquipmentSlot.MainHand:
					MainHand = item;
					EnsureMainHandContainer(item);
					break;
				case EquipmentSlot.OffHand:
					OffHand = item;
					EnsureOffHandContainer(item);
					break;
				case EquipmentSlot.Backpack:
					//  NOUVEAU : créer le conteneur si nécessaire
					EnsureBackpackContainer(item);
					break;
			}

			// Restaurer les métadonnées de variante pour les items de type coat
			int id = GetItemId(item.Name);
			if (GameData.ItemDatabase.TryGetValue(id, out var idata))
			{
				SaveSystem.RestoreCoatVariantMeta(item, idata);
			}

			return true;
		}

		public Item? UnEquipItem(EquipmentSlot slot, int index = 0)
		{
			var key = (slot, index);
			if (EquippedItems.TryGetValue(key, out Item? item))
			{
				EquippedItems.Remove(key);
				switch (slot)
				{
					case EquipmentSlot.MainHand:
						if (MainHand == item) MainHand = null;
						MainHandContainer = null;
						break;
					case EquipmentSlot.OffHand:
						if (OffHand == item) OffHand = null;
						//  Le contenu du sac reste attaché à l'objet lui-même (comme pour un sac à dos) :
						// on ne le vide pas, on détache seulement le conteneur actif de l'équipement.
						OffHandContainer = null;
						break;
				}
				return item;
			}
			return null;
		}

		/// <summary>
		/// Crée (si nécessaire) l'inventaire de stockage associé à un conteneur portable
		/// équipé dans la main secondaire (ex : sac en osier), ou réutilise celui déjà
		/// attaché à l'objet (si on ré-équipe un sac qui contenait déjà des objets).
		/// Ne fait rien pour les items qui ne sont pas des conteneurs portables.
		/// </summary>
		public void EnsureOffHandContainer(Item? offHandItem = null)
		{
			var targetItem = offHandItem ?? OffHand;
			if (targetItem == null) return;
			if (!Program.IsPortableContainer(targetItem)) return;

			if (targetItem.Container == null)
				targetItem.Container = CreatePortableContainer(targetItem);

			OffHandContainer = targetItem.Container;
		}

		public void EnsureMainHandContainer(Item? mainHandItem = null)
		{
			var targetItem = mainHandItem ?? MainHand;
			if (targetItem == null) return;
			if (!Program.IsPortableContainer(targetItem)) return;

			if (targetItem.Container == null)
				targetItem.Container = CreatePortableContainer(targetItem);

			MainHandContainer = targetItem.Container;
		}

		private PortableContainerData CreatePortableContainer(Item item)
		{
			int itemId = GetItemId(item.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
				return new PortableContainerData(9, 3);

			int slotCount = itemData.BackpackSlots > 0 ? itemData.BackpackSlots : 9;
			int rows = itemData.BackpackRows > 0 ? itemData.BackpackRows : 3;
			int columns = Math.Min(5, Math.Max(2, (int)Math.Ceiling(slotCount / (double)rows)));
			return new PortableContainerData(slotCount, columns);
		}

		public void EnsureBackpackContainer(Item? backpackItem = null)
		{
			var targetItem = backpackItem ?? Backpack;
			if (targetItem == null) return;

			// Si le conteneur est déjà attaché à l’item, on l’utilise
			if (targetItem.Backpack != null)
			{
				BackpackContainer = targetItem.Backpack;
				return;
			}

			// Sinon, on en crée un neuf
			int itemId = GetItemId(targetItem.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return;
			if (itemData.Type != ItemType.Backpack && itemData.BackpackSlots <= 0) return;

			int slotCount = itemData.BackpackSlots > 0 ? itemData.BackpackSlots : 8;
			int columns = itemData.BackpackRows > 0
				? Math.Min(5, Math.Max(2, (int)Math.Ceiling(slotCount / (double)itemData.BackpackRows)))
				: 5;

			BackpackContainer = new BackpackData(slotCount, columns);
			targetItem.Backpack = BackpackContainer;
		}

		public Item? GetItemInSlot(EquipmentSlot slot, int index = 0)
		{
			EquippedItems.TryGetValue((slot, index), out Item? item);
			return item;
		}

		public int GetEquipCount(EquipmentSlot slot)
		{
			return EquippedItems.Keys.Count(k => k.slot == slot);
		}

		public void ClearAllEquipment()
		{
			EquippedItems.Clear();
			ZoneItems.Clear();
		}

		// Zones dessinées avec leur propre préfixe de texture ("head_", "ears_"...).
		private static readonly Dictionary<BodyZone, string> ZoneTexturePrefix = new()
		{
			{ BodyZone.TopOfHead, "head" },
			{ BodyZone.Ears, "ears" },
			{ BodyZone.Face, "glasses" },
			{ BodyZone.Neck, "necklace" },
			{ BodyZone.Waist, "belt" },
			{ BodyZone.Torso, "body" },
			{ BodyZone.Back, "backpack" },
			{ BodyZone.Legs, "legs" },
			{ BodyZone.Feet, "socks" },
		};

		public void LoadEquipmentTextures()
		{
			string cacheKey = BuildEquipmentTextureCacheKey();
			if (EquipmentTextureSetCache.TryGetValue(cacheKey, out var cachedTextures))
			{
				EquipmentTextures = new Dictionary<string, Texture2D>(cachedTextures, StringComparer.OrdinalIgnoreCase);
				return;
			}

			EquipmentTextures.Clear();

			foreach (var item in ZoneItems)
			{
				int itemId = GetItemId(item.Name);
				if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData) || itemData.CoveredZones.Count == 0)
					continue;

				//  Charger pour CHAQUE zone couverte par l'item
				foreach (var zone in itemData.CoveredZones)
				{
					string prefix = ZoneTexturePrefix.TryGetValue(zone, out var p) ? p : zone.ToString().ToLowerInvariant();
					LoadEquipmentPieceTextures(item, prefix, this);
				}
			}

			EquipmentTextureSetCache[cacheKey] = new Dictionary<string, Texture2D>(EquipmentTextures, StringComparer.OrdinalIgnoreCase);
		}
		
		// Mémoïsation de la clé de cache par instance : la reconstruire (OrderBy + string.Join,
		// potentiellement répété par item pour Meta) coûtait cher et se faisait à CHAQUE frame
		// pour CHAQUE PNJ équipé dans DrawEntity, même quand l'équipement ne change jamais
		// (PNJ immobile). On ne recalcule que si ZoneItems a réellement changé, détecté par une
		// comparaison par référence (rapide, sans LINQ ni allocation) plutôt que par égalité de
		// contenu complet.
		private List<Item>? _cacheKeySnapshot;
		private string? _cachedTextureCacheKey;

		private bool ZoneItemsUnchangedSince(List<Item>? snapshot)
		{
			if (snapshot == null || snapshot.Count != ZoneItems.Count) return false;
			for (int i = 0; i < snapshot.Count; i++)
				if (!ReferenceEquals(snapshot[i], ZoneItems[i])) return false;
			return true;
		}

		private string BuildEquipmentTextureCacheKey()
		{
			var parts = new List<string>();
			foreach (var item in ZoneItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
				AppendEquipmentCachePart(parts, item);
			return string.Join("|", parts);
		}

		public string GetEquipmentTextureCacheKey()
		{
			if (_cachedTextureCacheKey != null && ZoneItemsUnchangedSince(_cacheKeySnapshot))
				return _cachedTextureCacheKey;

			_cachedTextureCacheKey = BuildEquipmentTextureCacheKey();
			_cacheKeySnapshot = new List<Item>(ZoneItems);
			return _cachedTextureCacheKey;
		}

		private void AppendEquipmentCachePart(List<string> parts, Item? item)
		{
			if (item == null) return;

			int itemId = GetItemId(item.Name);
			int layerCount = 1;
			if (GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
				layerCount = Math.Max(itemData.DyeLayers > 0 ? itemData.DyeLayers : 1, (item.CustomColors?.Count ?? 0) + 1);

			string metaSignature = string.IsNullOrEmpty(item.Metadata)
				? "metadata:none"
				: "metadata:" + item.Metadata;

			parts.Add($"{item.Name}:{layerCount}:{item.CustomColors?.Count ?? 0}:{metaSignature}");
		}

		private void LoadEquipmentPieceTextures(Item? item, string bodyPart, Equipment equipment)
		{
			if (item == null) return;
			
			int itemId = GetItemId(item.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return;
			
			string baseName = itemData.TextureName;
			if (string.IsNullOrEmpty(baseName)) return;
			
			int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;
			int customColorLayers = (item.CustomColors?.Count ?? 0) + 1;
			int layers = Math.Max(definedLayers, customColorLayers);
			
			// Les pièces teignables utilisent souvent un fichier de couche recolorable "_1.png"
			// même quand elles ne déclarent qu'une seule couche structurelle.
			bool isCoatItem = string.Equals(item.Name, "coat", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(itemData.TextureName, "coat", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(itemData.Key, "coat", StringComparison.OrdinalIgnoreCase);
			
			string[] targetParts = bodyPart switch
			{
				"head" => new[] { "head" },
				"ears" => new[] { "head" },
				"body" => new[] { "body", "larmtop", "larmbottom", "rarmtop", "rarmbottom" },
				// Une pièce de jambes peut aussi fournir une couche complémentaire sur le corps
				// (ex: jean_body), sans pour autant couvrir la zone Torso.
				"legs" => new[] { "llegtop", "llegbottom", "rlegtop", "rlegbottom", "body" },
				"backpack" => new[] { "body" },
				"earring" => new[] { "head" },
				"glasses" => new[] { "head" },
				"necklace" => new[] { "body" },
				"belt" => new[] { "body" },
				"ring" => new[] { "rarmbottom" },
				"bracelet" => new[] { "rarmtop" },
				"socks" => new[] { "rlegbottom", "llegbottom" },
				"underwear" => new[] { "body" },
				_ => Array.Empty<string>()
			};
			
			bool isMainEquipment = bodyPart == "head" || bodyPart == "body" || bodyPart == "legs" || bodyPart == "backpack";
			
			foreach (string part in targetParts)
			{
				for (int layer = 1; layer <= layers; layer++)
				{
					// Construire la clé simple et la clé préfixée
					string simpleKey;
					string prefixedKey;
					if (bodyPart == "backpack")
					{
                        simpleKey = layer == 1 ? "backpack" : $"backpack_{layer}";
                    }
                    else
                    {
                        simpleKey = layer == 1 ? part : $"{part}_{layer}";
                    }
                    prefixedKey = layer == 1 ? $"{baseName}_{part}" : $"{baseName}_{part}_{layer}";
                    Texture2D tex = new Texture2D();
					bool found = false;
					
					// Déterminer les chemins à essayer
					List<string> pathsToTry = new List<string>();

                    string? variantSuffix = null;
                    {
						string sleeveVal = item.GetMetadataValue("sleeves");
						string styleVal = item.GetMetadataValue("opening");
						if ((part == "larmtop" || part == "rarmtop" || part == "larmbottom" || part == "rarmbottom") && !string.IsNullOrEmpty(sleeveVal))
						{
							if (string.Equals(sleeveVal, "long", StringComparison.OrdinalIgnoreCase))
								variantSuffix = "long";
							else if (string.Equals(sleeveVal, "none", StringComparison.OrdinalIgnoreCase))
								variantSuffix = "none";
							else
								variantSuffix = "short";
						}
						else if (part == "body" && !string.IsNullOrEmpty(styleVal))
						{
							if (string.Equals(styleVal, "open", StringComparison.OrdinalIgnoreCase))
								variantSuffix = "open";
							else if (string.Equals(styleVal, "lace_up", StringComparison.OrdinalIgnoreCase))
								variantSuffix = "lace_up";
							else
								variantSuffix = "closed";
						}
					}
					if (variantSuffix == null && isCoatItem)
                    {
                        if (part == "body")
                            variantSuffix = "closed";
                        else if (part == "larmtop" || part == "rarmtop" || part == "larmbottom" || part == "rarmbottom")
                            variantSuffix = "short";
                    }

					// If the user explicitly chose no sleeves for this part, skip loading
					// any textures for the sleeve parts to avoid probing for "_none" files
					// and to ensure the part is simply not drawn.
					bool isSleevePart = part == "larmtop" || part == "rarmtop" || part == "larmbottom" || part == "rarmbottom";
					if (isSleevePart && string.Equals(variantSuffix, "none", StringComparison.OrdinalIgnoreCase))
					{
						// Do not load any texture for this sleeve part.
						continue;
					}

					string baseTextureKey = $"base:{baseName}_{part}";
					if (!string.IsNullOrEmpty(variantSuffix))
						baseTextureKey += $"_{variantSuffix}";

					List<string> basePathsToTry = new();
					if (!string.IsNullOrEmpty(variantSuffix))
						basePathsToTry.Add($"assets/equipements/{baseName}_{part}_{variantSuffix}.png");
					basePathsToTry.Add($"assets/equipements/{baseName}_{part}.png");
					foreach (string basePath in basePathsToTry)
					{
						Texture2D baseTexture = LoadEquipmentTexture(basePath);
						if (baseTexture.Id != 0)
						{
							equipment.EquipmentTextures[baseTextureKey] = baseTexture;
							break;
						}
					}

					if (layer == 1)
                    {
						pathsToTry.Add($"assets/equipements/{baseName}_{part}_1.png");
                    }
                    else if (layer <= definedLayers)
                    {
                        // Pour les couches structurelles déclarées (dyeLayers), on essaie les fichiers courts _N.
                        pathsToTry.Add($"assets/equipements/{baseName}_{part}_{layer}.png");
                    }
                    else
                    {
                        // Pour les couches virtuelles ajoutées par une teinte personnalisée
						// (ex: teinte n°1 sur un item simple), on réutilise la couche _1.
						pathsToTry.Add($"assets/equipements/{baseName}_{part}_1.png");
                    }

                    if (!string.IsNullOrEmpty(variantSuffix))
                    {
                        if (layer == 1)
                        {
                            pathsToTry.Insert(0, $"assets/equipements/{baseName}_{part}_1_{variantSuffix}.png");
                        }
                        else if (layer <= definedLayers)
                        {
                            pathsToTry.Insert(0, $"assets/equipements/{baseName}_{part}_1_{variantSuffix}_{layer}.png");
                            pathsToTry.Insert(1, $"assets/equipements/{baseName}_{part}_{variantSuffix}_{layer}.png");
                        }
                        else
                        {
                            // C'est une couche virtuelle ajoutée par une couleur custom sur un item à dyeLayers=1
                            pathsToTry.Insert(0, $"assets/equipements/{baseName}_{part}_1_{variantSuffix}.png");
                        }
                    }

                    

					// Debug : afficher les chemins essayés pour les coats afin de diagnostiquer
					if (string.Equals(baseName, "coat", StringComparison.OrdinalIgnoreCase))
					{
						try
						{
							string metaStr = string.IsNullOrEmpty(item.Metadata) ? "(no metadata)" : item.Metadata;
							// Console.WriteLine($"[DEBUG] LoadEquipmentPieceTextures coat: item={item.Name} meta={metaStr} variantSuffix={variantSuffix} paths=[{string.Join("|", pathsToTry)}]");
						}
						catch { }
					}

                    // Essayer chaque chemin jusqu'à trouver une texture valide
                    foreach (string path in pathsToTry)
                    {
                        tex = LoadEquipmentTexture(path);
                        if (tex.Id != 0)
                        {
                            found = true;
                            break;
                        }
                    }

                    // Si on a trouvé, on stocke
                    if (found)
                    {
                        if (!string.IsNullOrEmpty(variantSuffix))
                        {
                            string variantKey = layer == 1 ? $"{part}_{variantSuffix}" : $"{part}_{variantSuffix}_{layer}";
                            equipment.EquipmentTextures[variantKey] = tex;
                        }

						if (isMainEquipment && !(bodyPart == "legs" && part == "body"))
                            equipment.EquipmentTextures[simpleKey] = tex;
                        equipment.EquipmentTextures[prefixedKey] = tex;
                    }
                }
            }

			if (isCoatItem)
			{
				string bodyStyle = item.GetMetadataValue("opening", "closed");
				string sleeveStyle = item.GetMetadataValue("sleeves", "long");

				LoadCoatVariantOverlay(equipment, baseName, "body", bodyStyle);
				if (!string.Equals(sleeveStyle, "none", StringComparison.OrdinalIgnoreCase))
					LoadCoatVariantOverlay(equipment, baseName, "sleeves", sleeveStyle);
			}
        }

		private void LoadCoatVariantOverlay(Equipment equipment, string baseName, string category, string variant)
		{
			if (string.IsNullOrWhiteSpace(variant)) return;

			string key = $"variant_{category}_{variant}";
			if (equipment.EquipmentTextures.ContainsKey(key)) return;

			string[] paths =
			{
				$"assets/equipements/{baseName}_{category}_{variant}.png",
				$"assets/equipements/{baseName}_{category}_1_{variant}.png",
				$"assets/equipements/{baseName}_{category}_{variant}_1.png",
				$"assets/equipements/{category}_{variant}.png",
				$"assets/equipements/{category}_1_{variant}.png",
				$"assets/equipements/{category}_{variant}_1.png",
				$"assets/items/{baseName}_{category}_{variant}.png",
				$"assets/items/{baseName}_{category}_1_{variant}.png",
				$"assets/items/{baseName}_{category}_{variant}_1.png"
			};

			foreach (string path in paths)
			{
				Texture2D texture = LoadEquipmentTexture(path);
				if (texture.Id != 0)
				{
					equipment.EquipmentTextures[key] = texture;
					return;
				}
			}

			equipment.EquipmentTextures[key] = default;
		}
		private Texture2D LoadEquipmentTexture(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return new Texture2D();

			if (EquipmentTextureCache.TryGetValue(path, out var cachedTexture))
				return cachedTexture;

			if (!File.Exists(path))
			{
				if (MissingEquipmentTexturePaths.Add(path))
				return new Texture2D();
			}

			var texture = Raylib.LoadTexture(path);
			EquipmentTextureCache[path] = texture;
			return texture;
		}
		
		// Dans Models.cs, classe Equipment
		private int GetItemId(string name)
		{
			foreach (var kv in GameData.ItemDatabase)
				if (kv.Value.Name == name) return kv.Key;
			return 0;
		}
		
		private static int GetBackpackSlotCount(string backpackName)
		{
			foreach (var kv in GameData.ItemDatabase)
				if (kv.Value.Name == backpackName && kv.Value.Type == ItemType.Backpack)
					return kv.Value.BackpackSlots;
			return 0;
		}
	}

    public class CraftRecipe
	{
		//  Identifiant STABLE et UNIQUE de la recette (le champ "id" du recipes.json,
		// ex: 100, 101, 103...). À NE JAMAIS confondre avec ResultId, qui est l'id de
		// l'ITEM produit (ex: "glassbottle_small") — plusieurs recettes de potions
		// différentes partagent le même ResultId puisqu'elles produisent toutes une
		// simple bouteille, mais chacune a un Id de recette unique.
		public int Id;
		public string ResultName;
		public int ResultId;
		public int ResultCount;
		public List<(int id, int qty)> Ingredients;
		public string RequiresStation;
		public string Category;
		public Color? ResultColor;
		public string ResultMetadata = "";
		
		//  NOUVEAUX CHAMPS POUR LE RITUEL
		public bool IsRitual { get; set; } = false;
		public int BloodEssenceCost { get; set; } = 0;
		public bool IsHidden { get; set; } = false;
		public string UnlockCondition { get; set; } = "";

		// Constructeur existant (6 paramètres)
		public CraftRecipe(string name, int resultId, int resultCount,
						   List<(int, int)> ingredients, string station = "", string category = "")
		{
			ResultName = name; ResultId = resultId; ResultCount = resultCount;
			Ingredients = ingredients; RequiresStation = station; Category = category;
			ResultColor = null;
			IsRitual = false;
			BloodEssenceCost = 0;
			IsHidden = false;
			UnlockCondition = "";
			Id = 0;
		}

		// Nouveau constructeur avec tous les paramètres
		public CraftRecipe(string name, int resultId, int resultCount,
						   List<(int, int)> ingredients, string station, string category, 
						   Color? resultColor, bool isRitual, int bloodEssenceCost, 
						   bool isHidden, string unlockCondition)
		{
			ResultName = name;
			ResultId = resultId;
			ResultCount = resultCount;
			Ingredients = ingredients;
			RequiresStation = station;
			Category = category;
			ResultColor = resultColor;
			IsRitual = isRitual;
			BloodEssenceCost = bloodEssenceCost;
			IsHidden = isHidden;
			UnlockCondition = unlockCondition;
			Id = 0;
		}
	}
	
	// Modèle pour stocker les données d'un poisson dans un aquarium
	public class AquariumFishState
	{
		public Vector2 LocalPosition;      // Position relative à la zone de nage (0-1)
		public Vector2 TargetPosition;     // Position cible pour le déplacement
		public float MoveSpeed;            // Vitesse de déplacement (0-1 par seconde)
		public bool IsMoving;              // Est-ce que le poisson se déplace actuellement?
		public float IdleTimer;            // Temps depuis le début de l'immobilité
		public float BobPhase;             // Phase d'oscillation verticale (VISUELLE SEULEMENT)
		public float BobSpeed;             // Vitesse d'oscillation
		public int SlotIndex;              // Slot correspondant dans le conteneur
		public float MoveStartTime;        // Temps de début du mouvement
		public float MoveDuration;         // Durée du mouvement actuel
		public float BaseY;                // Position Y de base (ne change pas)
	}

	public class AquariumData
	{
		// Dictionnaire associant l'index du slot à l'état du poisson
		public Dictionary<int, AquariumFishState> FishStates = new();
		// Zone de nage en pixels (dans le repère monde)
		public Rectangle SwimArea;      // Position et taille de la zone où les poissons nagent
		public float LastUpdateTime;    // Pour lisser les mises à jour
	}
	
	public class Notification
	{
		public string Message;
		public float Timer, MaxTimer;
		public Color TextColor;
		public int StackCount = 1;
		public Texture2D ItemIcon;  // NOUVEAU : icône de l'item
		public Entity? PreviewEntity;
		public bool UseWideLayout;

		public Notification(string message, Color color, float duration = 3.0f, Entity? previewEntity = null, bool useWideLayout = false)
		{ 
			Message = message; 
			TextColor = color; 
			Timer = duration; 
			MaxTimer = duration;
			StackCount = 1;
			ItemIcon = new Texture2D(); // Texture vide par défaut
			PreviewEntity = previewEntity;
			UseWideLayout = useWideLayout || previewEntity != null;
		}
		
		// Constructeur pour les items avec compteur et icône
		public Notification(string itemName, int quantity, Color color, Texture2D icon, float duration = 3.0f)
		{ 
			Message = itemName; 
			TextColor = color; 
			Timer = duration; 
			MaxTimer = duration;
			StackCount = quantity;
			ItemIcon = icon;
			PreviewEntity = null;
			UseWideLayout = false;
		}
		
		public string GetDisplayMessage()
		{
			if (StackCount > 1)
				return $"+{StackCount} {Message}";
			return $"+{Message}";
		}
	}

    public class Tile
    {
        public string Name;
        public Texture2D Texture;
        public bool IsWalkable;
        public bool IsDiggable;
        public int ItemDrop;

        public Tile(string name, Texture2D tex, bool walkable = true, bool diggable = false, int drop = 0)
        { Name = name; Texture = tex; IsWalkable = walkable; IsDiggable = diggable; ItemDrop = drop; }
    }

    public class PlacedObject
    {
        public string Key;
        public int ItemId;
        public Vector2 WorldPos;
        public PlacedObject(string key, int id, Vector2 pos) { Key = key; ItemId = id; WorldPos = pos; }
    }

    public class WorldObjectHP
    {
        public int Current;
        public int Max;
        public float DamageFlash;
        public WorldObjectHP(int hp) { Current = hp; Max = hp; DamageFlash = 0f; }
    }
	
	// Models.cs - classe GroundItem modifiée
	public class GroundItem
	{
		// Identifiant stable utilisé pour référencer cet item précis sur le réseau
		// (le ramassage à distance se fait par cet id plutôt que par index de liste).
		public Guid NetId = Guid.NewGuid();
		public int ItemId;
		public int Count;
		public Vector2 Position;
		public Vector2 GroundVelocity;
		public float VerticalVelocity;
		public float Height;
		// Les objets au sol ne doivent pas expirer automatiquement : ils restent présents
		// jusqu'à ce qu'ils soient ramassés ou qu'un autre système les retire explicitement.
		public float Lifetime = float.PositiveInfinity;
		public float AttractSpeed = 250f;
		// Distance à partir de laquelle l'item est automatiquement attiré vers le joueur.
		// Les items jetés (voir ThrowDraggedItem) atterrissent volontairement un peu plus
		// loin que cette distance pour ne pas revenir instantanément vers le joueur.
		public const float DefaultPickupRadius = 100f;
		public float PickupRadius = DefaultPickupRadius;
		public float PickupCooldown = 0.25f;
		public float TimeSinceDrop = 0f;

		public float Gravity = 800f;
		public float BounceDamping = 0.5f;
		public float GroundFriction = 0.9f;
		public float AttractDelay = 0f;
		public Color? CustomColor = null;
		//  NOUVEAU : Support des couleurs multiples
		public List<Color?> CustomColors { get; set; } = new();
		public string? LootbagPayload { get; set; } = null;
        public PortableContainerData? Container { get; set; } = null;
        public BackpackData? Backpack { get; set; } = null;
		//  Préserve les données d'instance de l'Item d'origine (liquide dans un seau/une
		// bouteille, charges d'un arrosoir, runes serties encodées, etc.) : sans ça, tout
		// item redevenait "neuf" dès qu'il touchait le sol (voir Item.Metadata / Item.Meta).
		public string Metadata = "";
		public Dictionary<string, string>? Meta = null;

		public bool IsOnGround => Height <= 0f;

		public GroundItem(int itemId, int count, Vector2 pos, float groundY, string? lootbagPayload = null)
		{
			ItemId = itemId;
			Count = count;
			Position = pos;
			TimeSinceDrop = 0f;
			Height = 0f;
			CustomColors = new List<Color?>();
			LootbagPayload = lootbagPayload;

			Random rand = new Random();
			float angle = (float)(rand.NextDouble() * Math.PI * 2);
			float speed = rand.Next(80, 180);
			GroundVelocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

			VerticalVelocity = rand.Next(250, 400);
			Height = 0f;
		}

		public GroundItem(int itemId, int count, Vector2 pos, float groundY, List<Color?>? customColors, string? lootbagPayload = null, string? metadata = null, Dictionary<string, string>? meta = null, PortableContainerData? container = null, BackpackData? backpack = null)
			: this(itemId, count, pos, groundY, lootbagPayload)
		{
			Metadata = metadata ?? "";
			Meta = (meta != null && meta.Count > 0) ? new Dictionary<string, string>(meta, StringComparer.OrdinalIgnoreCase) : null;
			Container = container;
			Backpack = backpack;
			if (customColors != null && customColors.Count > 0)
			{
				CustomColors = new List<Color?>(customColors);
				CustomColor = customColors.Count > 0 ? customColors[0] : null;
			}
		}

		/// <summary>
		/// Construit un item "jeté" : il apparaît à la position du joueur (pos) et reçoit
		/// une vélocité dirigée (au lieu d'un "pop" aléatoire) afin qu'il ait visuellement
		/// l'air d'avoir été lancé, et retombe naturellement autour de throwDistance grâce
		/// à la gravité/rebond déjà simulés dans Update, plutôt que d'être téléporté.
		/// </summary>
		public GroundItem(int itemId, int count, Vector2 pos, float groundY, Vector2 throwDirection, float throwDistance, List<Color?>? customColors, string? lootbagPayload = null, string? metadata = null, Dictionary<string, string>? meta = null, PortableContainerData? container = null, BackpackData? backpack = null)
			: this(itemId, count, pos, groundY, lootbagPayload)
		{
			Metadata = metadata ?? "";
			Meta = (meta != null && meta.Count > 0) ? new Dictionary<string, string>(meta, StringComparer.OrdinalIgnoreCase) : null;
			Container = container;
			Backpack = backpack;
			if (throwDirection.LengthSquared() < 0.0001f)
				throwDirection = new Vector2(1, 0);
			else
				throwDirection = Vector2.Normalize(throwDirection);

			// Durée en l'air avant le premier contact au sol (avant rebond éventuel).
			const float airTime = 0.35f;
			VerticalVelocity = airTime * Gravity / 2f;
			float horizontalSpeed = throwDistance / airTime;
			GroundVelocity = throwDirection * horizontalSpeed;
			Height = 0f;

			//  Un item jeté ne doit pas pouvoir être ramassé (ni ré-attiré) immédiatement :
			// sans ce délai, il serait instantanément récupéré dès son "atterrissage" (ou même
			// avant, via l'attraction automatique) puisqu'il part de la position du joueur.
			// On bloque donc tout ramassage/attraction pendant la durée du vol (+ une petite
			// marge), le temps qu'il soit effectivement hors de portée (voir PickupRadius).
			PickupCooldown = airTime + 0.3f;

			if (customColors != null && customColors.Count > 0)
			{
				CustomColors = new List<Color?>(customColors);
				CustomColor = customColors.Count > 0 ? customColors[0] : null;
			}
		}

		public void Update(float dt, Vector2 playerPos, bool canAttract)
		{
			TimeSinceDrop += dt;
			Lifetime -= dt;

			// Mise à jour de la hauteur (saut)
			if (!IsOnGround)
			{
				VerticalVelocity -= Gravity * dt;
				Height += VerticalVelocity * dt;
				if (Height <= 0f)
				{
					Height = 0f;
					VerticalVelocity = 0f;
					// Rebond si la vélocité verticale est encore significative
					if (Math.Abs(VerticalVelocity) > 50f)
					{
						VerticalVelocity = -VerticalVelocity * BounceDamping;
					}
				}
			}

			// Mouvement horizontal
			Position += GroundVelocity * dt;

			// Frottement au sol
			if (IsOnGround)
			{
				GroundVelocity *= GroundFriction;
				if (GroundVelocity.LengthSquared() < 1f)
					GroundVelocity = Vector2.Zero;
			}

			// Attraction automatique vers le joueur dès que l'item est à portée, à condition
			// qu'il puisse effectivement être rangé quelque part (stack existant ou slot
			// vide, ceinture/sac à dos/panier compris — voir Program.HasSpaceForItem), et
			// qu'il ne soit pas encore en période de cooldown (ex : item tout juste jeté,
			// qui doit d'abord avoir le temps de s'éloigner du joueur).
			if (canAttract && IsOnGround && TimeSinceDrop > PickupCooldown)
			{
				Vector2 dir = playerPos - Position;
				float dist = dir.Length();
				if (dist < PickupRadius)
				{
					if (dist > 0.01f) dir /= dist;
					// Le frottement au sol est applique avant cette section et limitait
					// fortement l'acceleration de l'attraction. Suivre directement le joueur
					// rend le mouvement rapide et evite de le depasser au dernier instant.
					GroundVelocity = dir * MathF.Min(AttractSpeed, dist / MathF.Max(dt, 0.001f));
				}
			}
		}

		public bool IsAlive => Lifetime > 0;

		public bool CanPickup(Vector2 playerPos)
		{
			return TimeSinceDrop > PickupCooldown && Vector2.Distance(Position, playerPos) < 12f;
		}
	}

    // ─── CLASSES POUR LES OBJETS DU MONDE (worldobjects.json) ────────────────────
	public class WorldTileData
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public string Texture { get; set; } = "";
		public int Variation { get; set; } = 0;
		[JsonConverter(typeof(NullableBoolJsonConverter))]
		public bool? Animation { get; set; }
		public int Hardness { get; set; } = 2;
		public string Material { get; set; } = "";
		public string ConnectionType { get; set; } = "";
		public bool Walkable { get; set; } = true;
		public bool OnFloor { get; set; } = false;
		public bool Diggable { get; set; } = true;
		public bool Collectable { get; set; } = false;
		public List<WorldDrop> Drops { get; set; } = new();
		public string MiningTool { get; set; } = "";
		public bool Connectable { get; set; } = false;
		public int ContainerColumns { get; set; } = 8;
		public bool IsFurniture { get; set; } = false;
		public bool furniture { get; set; } = false;
		public bool IsSittable { get; set; } = false;
		public bool IsSleepable { get; set; } = false;
		public bool IsDrinkable { get; set; } = false;
		public bool IsSail { get; set; } = false;
		public bool WallMountable { get; set; } = false;
		
		public bool IsCrop { get; set; }
		public float GrowthTime { get; set; }
		public int GrowthStages { get; set; }
		public string HarvestItemId { get; set; } = "";
		public int HarvestMinQty { get; set; }
		public int HarvestMaxQty { get; set; }
		public bool RegrowsAfterHarvest { get; set; } = false;
		public string HarvestedTexture { get; set; } = "";

		//  Arbres plantés (plantType "classic" dans items.json, ex : le gland) : contrairement
		// aux cultures (IsCrop), ces tuiles poussent n'importe où (comme un objet plaçable),
		// n'ont pas besoin d'être arrosées, et ne se "récoltent" pas par simple clic — une fois
		// GrowthStages atteints, l'objet planté se transforme en AdultTileId (l'arbre adulte
		// existant, ex : Tree id=4), qui se comporte alors comme n'importe quel arbre normal
		// (abattage à la hache, drops classiques).
		public bool IsSapling { get; set; } = false;
		public int AdultTileId { get; set; } = 0;

		//  Vrai pour toute tuile possédant des stades de croissance visuels (culture OU jeune
		// arbre), utilisé pour le chargement/l'affichage des textures "_grow1.._growN".
		[JsonIgnore]
		public bool HasGrowthStages => (IsCrop || IsSapling) && GrowthStages > 0;
		
		//  Propriétés de lumière (UNE SEULE FOIS)
		public bool IsLightSource { get; set; } = false;
		public int LightRadius { get; set; } = 120;
		public int[]? LightColor { get; set; } = new int[] { 255, 220, 150 };
		
		public WorldSize? Size { get; set; }
		public WorldOffset? DrawOffset { get; set; }
		
		public WorldSize? CollisionSize { get; set; }
		public WorldOffset? CollisionOffset { get; set; }
		
		//  Vrai pour les tuiles de flore (arbres, buissons, fleurs, hautes herbes...) dont le(s)
		// sprite(s) doivent osciller avec le vent. La rotation s'applique autour du centre de
		// la hitbox (collision) de la tuile — voir World.GetWindSwayAngle.
		public bool SwaysInWind { get; set; } = false;

		public bool? IsStation { get; set; }
		public string? StationType { get; set; }
		public bool? IsContainer { get; set; }
		public bool? IsArmorStand { get; set; }
		public int? ContainerSlots { get; set; }
		public int? DamageOnTouch { get; set; }

		//  Entrées/sorties de câble électrique de cette tuile (voir EnergyPort). Vide = pas de branchement.
		public List<EnergyPort> EnergyPorts { get; set; } = new();
	}

    public class WorldDrop
    {
		// "id" in JSON can be either a number or a string (key or localized name).
		// Keep the raw element for flexible parsing, then resolve to `Id` after
		// the whole file is deserialized (GameData is already loaded before
		// worldobjects.json is parsed).
		[JsonPropertyName("id")]
		public System.Text.Json.JsonElement IdElement { get; set; }

		[JsonIgnore]
		public int Id { get; set; }

		public int MinQty { get; set; } = 1;
		public int MaxQty { get; set; } = 1;
		public double Chance { get; set; } = 1.0;
    }

    //  TABLE DE LOOT BASÉE SUR DICTIONNAIRE (mobs, cf. SpeciesInfo.Drops / SpeciesJson.drops).
    // Clé du dictionnaire parent = ID de l'item (sous forme de string, contrainte JSON) qui peut
    // être obtenu. Chaque entrée porte sa propre chance de drop, une quantité min/max, ET des
    // métadonnées optionnelles qui seront copiées telles quelles dans l'Item/GroundItem généré
    // au moment précis du drop (cf. SpeciesData.RollDrops) — utile pour un loot qui doit naître
    // avec un état particulier (ex: {"quality":"rare"}, {"durability":"20/100"}, {"enchant":"fire:1"}).
    public class LootDrop
    {
        // Probabilité (0-1) que CETTE entrée soit effectivement tirée. 1.0 = toujours.
        [JsonPropertyName("chance")]
        public double Chance { get; set; } = 1.0;
        [JsonPropertyName("min")]
        public int MinQty { get; set; } = 1;
        [JsonPropertyName("max")]
        public int MaxQty { get; set; } = 1;
        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; } = null;
    }

    public class WorldSize
    {
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
    }

    public class WorldOffset
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class WorldTilesRoot
    {
        public List<WorldTileData> Tiles { get; set; } = new();
    }
	
	public class HouseData
	{
		public int BuildingId;
		public int DoorX, DoorY;
		public Vector2 DoorWorldPos;
		public Vector2 InteriorPosition;
		public bool IsOccupied;
		public bool HasVillagerSpawned;
		public Guid? InfirmaryPatientNetId;
		//  Coffre commun de la maison (partagé par tous les PNJ qui y habitent), utilisé pour
		// le dépôt/retrait automatique de leur inventaire (voir World.Houses.cs). Résolu une
		// seule fois par un scan des tuiles de Bounds puis mis en cache ici ; reste null tant
		// qu'aucun coffre n'a été trouvé (on retente au prochain appel dans ce cas, au cas où
		// le joueur en poserait un plus tard).
		public Vector2? StorageChestPos;
		public HashSet<(int x, int y)> RoofTiles = new(); // Toutes les tuiles du toit
		public Rectangle Bounds; // Rectangle englobant la maison
	}

    // ─── REGISTRY DES OBJETS DU MONDE ───────────────────────────────────────────
	// ─── DONJONS ─────────────────────────────────────────────────────────
	public enum DungeonPortalKind { Entrance, ReturnExit }

	public class DungeonPortalInfo
	{
		public DungeonPortalKind Kind;
		public int InstanceId;
	}

	public class DungeonInstance
	{
		public int Id;
		public int OriginX;                    // coin haut-gauche de la zone réservée (plan souterrain)
		public int OriginY;
		public int Width;
		public int Height;
		public (int x, int y) EntryPos;         // position de l'entrée, côté grotte
		public (int x, int y) RestSpawnTile;    // apparition dans la salle de repos
		public (int x, int y) ExitDoorTile;     // mur (verrouillé) devenant le portail de retour une fois le boss vaincu
		public bool Generated;
		public bool BossDefeated;
		public int Seed;
	}

	// ─── PORTAILS DE TÉLÉPORTATION ──────────────────────────────────────
	// Contrairement aux anciens pylônes, un portail découvert une fois reste
	// accessible depuis n'importe où, même si son chunk n'est plus chargé :
	// sa position est enregistrée dans un registre persistant indépendant
	// des chunks (voir World._knownPortals / SaveSystem.SavePortals).
	public class PortalInfo
	{
		public int TileX;
		public int TileY;
		public bool IsCave;              // true = grotte, false = surface
		public string Name = "";         // nom personnalisé, sinon nom par défaut généré
		public double DiscoveredAt;      // temps de jeu (GetGameTime) lors de la découverte
		public bool IsFavorite;          // épinglé en haut de la liste dans l'UI

		public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name;

		public string DefaultName => (IsCave ? "Portail (grotte) " : "Portail ") + $"({TileX}, {TileY})";
	}

	public static class WorldTileRegistry
	{
		public static Dictionary<int, WorldTileData> Tiles = new();
		public static Dictionary<string, int> TileNameToId = new();
		public static Dictionary<int, List<Texture2D>> TileVariations = new();
	public static Dictionary<int, List<Texture2D>> TileNormalVariations = new();
	public static Dictionary<int, List<Texture2D>> TileAnimations = new();
	public static Dictionary<int, List<Texture2D>> TileAnimationNormalFrames = new();
	public static Dictionary<int, List<Texture2D>> CropStageTextures = new();
	public static Dictionary<string, Texture2D> TileTexturesByName = new(StringComparer.OrdinalIgnoreCase);
	public static Dictionary<int, List<Texture2D>> CropStageNormalTextures = new();

	//  Jeunes arbres plantés (IsSapling) à deux couches, comme l'arbre adulte : un tronc
	// (non teinté) et un feuillage (teinté par arbre via GetTreeLeafTintForObjectId), avec
	// une paire de textures par stade de croissance. Convention de nommage :
	// "{texture}_trunk_grow{stage}.png" et "{texture}_leaves_grow{stage}.png". Si ces fichiers
	// sont absents pour une tuile donnée, le rendu retombe sur le sprite simple habituel
	// (voir LoadTextures / DrawTreeTwoLayer).
	public static Dictionary<int, List<Texture2D>> SaplingTrunkStageTextures = new();
	public static Dictionary<int, List<Texture2D>> SaplingLeavesStageTextures = new();

	public static Dictionary<int, float> AnimationSpeeds = new();
	public static Texture2D DefaultNormalTexture = new();
	public static Dictionary<int, DecorationInfo> TileDecorations = new();
	public static Dictionary<int, List<Texture2D>> DecorationTextures = new();
		
		//  NOUVEAU : Textures de connexion (left, right, up, down)
		public static Dictionary<int, Dictionary<string, Texture2D>> ConnectionTextures = new();
		
		public class DecorationInfo
		{
			public int TileId;
			public string Folder = "";
			public int VariationCount;
			public float SpawnChance;
		}
		
		public static void LoadFromFile()
		{
			string filePath = "Data/worldobjects.json";
			if (!File.Exists(filePath))
			{
				Console.WriteLine(" worldobjects.json manquant, création d'un fichier par défaut");
				CreateDefaultFile();
				return;
			}
			
			string json = File.ReadAllText(filePath);
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var root = JsonSerializer.Deserialize<WorldTilesRoot>(json, options);
			
			if (root?.Tiles != null)
			{
				foreach (var tile in root.Tiles)
				{
					// Résoudre les IDs des drops : le JSON peut contenir soit un nombre,
					// soit une chaîne (clé d'item ou nom localisé). GameData est déjà
					// chargé à ce stade, on peut donc convertir en ID numérique.
					if (tile.Drops != null)
					{
						foreach (var drop in tile.Drops)
						{
							int resolved = 0;
							var el = drop.IdElement;
							if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out var n))
								resolved = n;
							else if (el.ValueKind == System.Text.Json.JsonValueKind.String)
							{
								var s = el.GetString() ?? string.Empty;
								if (int.TryParse(s, out n))
									resolved = n;
								else if (GameData.TryGetItemByName(s, out var itemByName))
									resolved = itemByName.ID;
								else if (GameData.TryGetItemByKey(s, out var itemByKey))
									resolved = itemByKey.ID;
							}
							// If still unresolved, try a case-insensitive search by localized name
							if (resolved == 0)
							{
								var match = GameData.ItemDatabase.Values.FirstOrDefault(i => string.Equals(i.Name, el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : null, StringComparison.OrdinalIgnoreCase));
								if (match.ID != 0) resolved = match.ID;
							}
							drop.Id = resolved;
						}
					}

					//  Lecture de la propriété "furniture" du JSON
					// La désérialisation remplit déjà tile.furniture
					tile.IsFurniture = tile.furniture;  // recopie vers la propriété d'utilisation
					
					//  Initialisation des valeurs par défaut pour la lumière
					if (tile.LightColor == null || tile.LightColor.Length < 3)
						tile.LightColor = new int[] { 255, 220, 150 };
					if (tile.LightRadius <= 0)
						tile.LightRadius = 120;
					
					Tiles[tile.Id] = tile;
					TileNameToId[tile.Name.ToLower()] = tile.Id;
				}
				Console.WriteLine($" {Tiles.Count} objets du monde chargés");
				// ... affichage des 10 premiers ...
			}
			else
			{
				Console.WriteLine(" Erreur: Impossible de charger worldobjects.json");
				CreateDefaultFile();
			}
		}

		public static void CreateDefaultFile()
		{
			var root = new WorldTilesRoot
			{
				Tiles = new List<WorldTileData>
				{
					new WorldTileData { Id = 1, Name = "Grass", Texture = "grass", Walkable = true, Diggable = true }
				}
			};
			string json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText("Data/worldobjects.json", json);
			Console.WriteLine("ℹ Fichier worldobjects.json créé par défaut.");
		}

		public static void LoadTextures(Dictionary<int, Texture2D> textureCache, Texture2D missingTex)
		{
			// Aucun affichage individuel des textures - seules les manquantes seront affichées au final
			DefaultNormalTexture = CreateDefaultNormalMap();
			foreach (string extraName in new[] { "oak_tree_bush", "oak_tree_trunk", "spruce_tree_bush", "spruce_tree_trunk" })
			{
				string extraPath = $"assets/tiles/{extraName}.png";
				Texture2D extraTex = LoadTextureFromFile(extraPath, missingTex);
				TileTexturesByName[extraName] = extraTex;
			}
			
			foreach (var tile in Tiles.Values)
			{
				//  TRAITER LES CULTURES ET LES JEUNES ARBRES EN PREMIER 
				if (tile.HasGrowthStages)
				{
					var stages = new List<Texture2D>();
				var normalStages = new List<Texture2D>();
				int loadedStages = 0;
				for (int stage = 1; stage <= tile.GrowthStages; stage++)
				{
					string texPath = $"assets/tiles/{tile.Texture}_grow{stage}.png";
					string normalPath = texPath.Substring(0, texPath.Length - 4) + "_n.png";
					Texture2D tex = LoadTextureFromFile(texPath, missingTex);
					Texture2D normalTex = LoadTextureFromFile(normalPath, missingTex);
					stages.Add(tex);
					normalStages.Add(normalTex);
					if (tex.Id != 0 && tex.Id != missingTex.Id)
						loadedStages++;
				}
				CropStageTextures[tile.Id] = stages;
				CropStageNormalTextures[tile.Id] = normalStages;
					if (!string.IsNullOrWhiteSpace(tile.HarvestedTexture))
					{
						string harvestedPath = $"assets/tiles/{tile.HarvestedTexture}.png";
						TileTexturesByName[tile.HarvestedTexture] = LoadTextureFromFile(harvestedPath, missingTex);
					}

					//  Jeune arbre (IsSapling) : tenter de charger, en plus du sprite simple
					// ci-dessus (utilisé en repli), une paire tronc/feuillage par stade, sur le
					// même modèle que l'arbre adulte (oak_tree_trunk / oak_tree_bush).
					if (tile.IsSapling)
					{
						var trunkStages = new List<Texture2D>();
						var leavesStages = new List<Texture2D>();
						int loadedTwoLayerStages = 0;
						for (int stage = 1; stage <= tile.GrowthStages; stage++)
						{
							string trunkPath = $"assets/tiles/{tile.Texture}_trunk_grow{stage}.png";
							string leavesPath = $"assets/tiles/{tile.Texture}_leaves_grow{stage}.png";
							Texture2D trunkTex = LoadTextureFromFile(trunkPath, missingTex);
							Texture2D leavesTex = LoadTextureFromFile(leavesPath, missingTex);
							trunkStages.Add(trunkTex);
							leavesStages.Add(leavesTex);
							if (trunkTex.Id != 0 && trunkTex.Id != missingTex.Id &&
								leavesTex.Id != 0 && leavesTex.Id != missingTex.Id)
								loadedTwoLayerStages++;
						}
						//  On ne garde les listes que si TOUS les stades ont bien leur paire
						// tronc/feuillage ; sinon on retombe entièrement sur le sprite simple
						// (CropStageTextures) pour éviter un rendu à moitié en deux couches.
						if (loadedTwoLayerStages == tile.GrowthStages)
						{
							SaplingTrunkStageTextures[tile.Id] = trunkStages;
							SaplingLeavesStageTextures[tile.Id] = leavesStages;
							// Aucun affichage
						}
					}

					// Aucun affichage pour les cultures
					if (loadedStages == 0)
					{
						// Enregistrer comme manquante si nécessaire
					}
					
					// Texture par défaut (premier stade ou missing)
					if (stages.Count > 0 && stages[0].Id != 0)
						textureCache[tile.Id] = stages[0];
					else
						textureCache[tile.Id] = missingTex;
					
					//  PASSER À LA TUILE SUIVANTE - NE PAS ALLER PLUS LOIN 
					continue;
				}
				
				//  TRAITER LES ANIMATIONS 
				if (tile.Animation == true)
				{
					LoadAnimatedTextures(tile, textureCache, missingTex);
					continue;
				}
				
				//  TRAITER LES TUILES NORMALES (VARIATIONS) 
				var tempVariations = new List<(string path, Texture2D tex)>();
				int variationCount = tile.Variation > 0 ? tile.Variation : 1;
				int loadedCount = 0;
				
				var tempNormalVariations = new List<Texture2D>();
				for (int i = 1; i <= variationCount; i++)
				{
					string texName = (variationCount == 1) ? tile.Texture : $"{tile.Texture}{i}";
					string texPath = $"assets/tiles/{texName}.png";
					string normalPath = texPath.Substring(0, texPath.Length - 4) + "_n.png";
					Texture2D tex = LoadTextureFromFile(texPath, missingTex);
					Texture2D normalTex = LoadTextureFromFile(normalPath, missingTex);
					
					if (tex.Id != 0 && tex.Id != missingTex.Id)
					{
						tempVariations.Add((texPath, tex));
						loadedCount++;
						if (!textureCache.ContainsKey(tile.Id))
							textureCache[tile.Id] = tex;
					}
					else if (variationCount == 1 && tex.Id == 0)
					{
						tempVariations.Add((texPath, missingTex));
						textureCache[tile.Id] = missingTex;
					}

					if (normalTex.Id != 0 && normalTex.Id != missingTex.Id)
						tempNormalVariations.Add(normalTex);
					else
						tempNormalVariations.Add(missingTex);
				}
				
				tempVariations = tempVariations.OrderBy(v => v.path).ToList();
				var variations = tempVariations.Select(v => v.tex).ToList();
				var normalVariations = tempNormalVariations.ToList();
				
				if (variations.Count > 0)
					TileVariations[tile.Id] = variations;
				if (normalVariations.Count > 0)
					TileNormalVariations[tile.Id] = normalVariations;
				
				if (variations.Count > 0)
					TileTexturesByName[tile.Texture] = variations[0];
				if (loadedCount > 0)
				{
					// Tuile chargée
				}
				else
				{
					// Tuile manquante - sera affichée dans le résumé final
				}
			}
	}
	
	//  NOUVEAU : Charger les textures de connexion
	public static void LoadConnectionTextures(Texture2D missingTex)
	{
		// Aucun affichage des connexions - seules les manquantes seront affichées au final
			foreach (var tile in Tiles.Values)
			{
				if (!tile.Connectable) continue;
				
				var connDict = new Dictionary<string, Texture2D>();
				string baseName = tile.Texture;
				int loaded = 0;
				
				string[] directions = { "left", "right", "up", "down" };
				
				foreach (string dir in directions)
				{
					string texPath = $"assets/tiles/{baseName}_{dir}.png";
					Texture2D tex = LoadTextureFromFile(texPath, new Texture2D());
					
					if (tex.Id != 0)
					{
						connDict[dir] = tex;
						loaded++;
					}
					else
					{
						connDict[dir] = new Texture2D();
					}
				}
				
				// Toujours stocker le dictionnaire, même si aucune texture n'est chargée
				ConnectionTextures[tile.Id] = connDict;
				
				// Pas d'affichage pour les textures de connexion - elles seront affichees dans le resume final si manquantes
			}
		}
		
		public static void LoadDecorations(Texture2D missingTex)
		{
			// Aucun affichage des décorations - seules les manquantes seront affichées au final
			var decorations = new List<DecorationInfo>
			{
				new DecorationInfo { TileId = 1, Folder = "grass_part", VariationCount = 10, SpawnChance = 0.35f },
				new DecorationInfo { TileId = 2, Folder = "grass_part", VariationCount = 10, SpawnChance = 0.15f },
				new DecorationInfo { TileId = 62, Folder = "beach_part", VariationCount = 5, SpawnChance = 0.15f },
				new DecorationInfo { TileId = 43, Folder = "snow_part", VariationCount = 4, SpawnChance = 0.30f },
				new DecorationInfo { TileId = 46, Folder = "savanna_part", VariationCount = 6, SpawnChance = 0.30f },
				new DecorationInfo { TileId = 44, Folder = "forest_part", VariationCount = 1, SpawnChance = 0.20f },
				new DecorationInfo { TileId = 47, Folder = "taiga_part", VariationCount = 8, SpawnChance = 0.10f },
				new DecorationInfo { TileId = 100, Folder = "stone_part", VariationCount = 4, SpawnChance = 0.30f },
			};
			
			foreach (var deco in decorations)
			{
				var textures = new List<Texture2D>();
				int loadedCount = 0;
				
				for (int i = 1; i <= deco.VariationCount; i++)
				{
					string texPath = $"assets/tiles/{deco.Folder}{i}.png";
					Texture2D tex = LoadTextureFromFile(texPath, missingTex);
					
					if (tex.Id != 0 && tex.Id != missingTex.Id)
					{
						textures.Add(tex);
						loadedCount++;
					}
					else
					{
						textures.Add(missingTex);
					}
				}
				
				if (loadedCount > 0)
				{
					DecorationTextures[deco.TileId] = textures;
					TileDecorations[deco.TileId] = deco;
				}
				else
				{
					// Décorations manquantes - seront affichées au final
				}
			}
		}
		
		//  NOUVEAU : Vérifier si une tuile est connectable
		public static bool IsConnectable(int tileId)
		{
			return Tiles.TryGetValue(tileId, out var tile) && tile.Connectable;
		}
		
		//  NOUVEAU : Obtenir la texture de connexion pour une direction spécifique
		public static Texture2D GetConnectionTexture(int tileId, string direction)
		{
			if (ConnectionTextures.TryGetValue(tileId, out var dict))
			{
				if (dict.TryGetValue(direction, out var tex) && tex.Id != 0)
					return tex;
			}
			return new Texture2D();
		}
		
		public static List<Texture2D>? GetVariations(int tileId) => TileVariations.GetValueOrDefault(tileId);
		public static List<Texture2D>? GetAnimationFrames(int tileId) => TileAnimations.GetValueOrDefault(tileId);
		public static Texture2D GetTextureByName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return new Texture2D();
			if (TileTexturesByName.TryGetValue(name, out var tex) && tex.Id != 0)
				return tex;
			return new Texture2D();
		}
		
		private static void LoadAnimatedTextures(WorldTileData tile, Dictionary<int, Texture2D> textureCache, Texture2D missingTex)
		{
			var frames = new List<Texture2D>();
		var normalFrames = new List<Texture2D>();
		int frameCount = tile.Variation > 0 ? tile.Variation : 4;
		int loadedCount = 0;
		
		for (int i = 1; i <= frameCount; i++)
		{
			string texName = $"{tile.Texture}-{i}";
			string texPath = $"assets/tiles/{texName}.png";
			string normalPath = texPath.Substring(0, texPath.Length - 4) + "_n.png";
			Texture2D tex = LoadTextureFromFile(texPath, missingTex);
			Texture2D normalTex = LoadTextureFromFile(normalPath, missingTex);
			if (tex.Id != 0 && tex.Id != missingTex.Id)
			{
				frames.Add(tex);
				loadedCount++;
				if (!textureCache.ContainsKey(tile.Id))
					textureCache[tile.Id] = tex;
			}
			else
			{
				frames.Add(missingTex);
			}
			if (normalTex.Id != 0 && normalTex.Id != missingTex.Id)
				normalFrames.Add(normalTex);
			else
				normalFrames.Add(DefaultNormalTexture);
		}
		
		if (frames.Count > 0)
		{
			TileAnimations[tile.Id] = frames;
			TileAnimationNormalFrames[tile.Id] = normalFrames;
			AnimationSpeeds[tile.Id] = 0.15f;
		}
		
		// Pas d'affichage pour les animations individuelles - elles seront affichees dans le resume final si manquantes
	}
	
	private static Texture2D LoadTextureFromFile(string path, Texture2D missingTex)
	{
		if (File.Exists(path))
		{
			var tex = Raylib.LoadTexture(path);
			if (tex.Id != 0)
			{
				return tex;
			}
		}
		return missingTex;
	}

	private static Texture2D CreateDefaultNormalMap()
	{
		Image img = Raylib.GenImageColor(2, 2, new Color(128, 128, 255, 255));
		Texture2D tex = Raylib.LoadTextureFromImage(img);
		Raylib.UnloadImage(img);
		return tex;
	}

	public static List<Texture2D>? GetNormalVariations(int tileId) => TileNormalVariations.GetValueOrDefault(tileId);
	public static Texture2D GetFirstNormalTexture(int tileId)
	{
		if (TileNormalVariations.TryGetValue(tileId, out var normals) && normals.Count > 0)
			return normals[0];
		return DefaultNormalTexture;
	}
	public static Texture2D GetNormalTexture(int tileId, float time = 0f)
	{
		if (TileAnimations.ContainsKey(tileId))
			return GetAnimatedNormalTexture(tileId, time);
		return GetFirstNormalTexture(tileId);
	}
	public static Texture2D GetCropStageNormalTexture(int tileId, int stageIndex)
	{
		if (CropStageNormalTextures.TryGetValue(tileId, out var normals) && stageIndex >= 0 && stageIndex < normals.Count)
			return normals[stageIndex];
		return DefaultNormalTexture;
	}
	public static Texture2D GetAnimatedNormalTexture(int tileId, float time)
	{
		if (TileAnimationNormalFrames.TryGetValue(tileId, out var normals) && normals.Count > 0)
		{
			float speed = AnimationSpeeds.GetValueOrDefault(tileId, 0.15f);
			int frameIndex = (int)(time / speed) % normals.Count;
			return normals[frameIndex];
		}
		return DefaultNormalTexture;
	}

	public static Texture2D GetFirstTexture(int tileId)
	{
		if (TileVariations.TryGetValue(tileId, out var variations) && variations.Count > 0)
			return variations[0];
		return new Texture2D();
	}

	public static Texture2D GetAnimatedTexture(int tileId, float time)
	{
		if (TileAnimations.TryGetValue(tileId, out var frames) && frames.Count > 0)
		{
			float speed = AnimationSpeeds.GetValueOrDefault(tileId, 0.15f);
			int frameIndex = (int)(time / speed) % frames.Count;
			return frames[frameIndex];
		}
		return new Texture2D();
	}

	public static bool IsAnimated(int tileId)
	{
		return TileAnimations.ContainsKey(tileId);
	}

	public static WorldTileData? GetTile(int id) => Tiles.GetValueOrDefault(id);
	public static WorldTileData? GetTileByName(string name)
	{
		if (TileNameToId.TryGetValue(name.ToLower(), out int id))
			return GetTile(id);
		return null;
	}

	public static List<(int id, int qty)> GetDrops(int tileId)
	{
		var drops = new List<(int id, int qty)>();
		if (Tiles.TryGetValue(tileId, out var tile))
		{
			foreach (var drop in tile.Drops)
			{
				if (Random.Shared.NextDouble() <= drop.Chance)
				{
					int qty = Random.Shared.Next(drop.MinQty, drop.MaxQty + 1);
					if (qty > 0) drops.Add((drop.Id, qty));
				}
			}
		}
		return drops;
	}
}

    // ─── CLASSE POUR LA COMPATIBILITÉ AVEC L'ANCIEN Renderer ─────────────────────
    public static class Renderer
    {
		private static bool _mainDebugExpanded = true;
		private static bool _heldItemDebugExpanded;
		private static bool _tileDebugExpanded;
		private static Rectangle _mainDebugHeader;
		private static Rectangle _heldItemDebugHeader;
		private static Rectangle _tileDebugHeader;

		public static bool HandleDebugMenuInput()
		{
			if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return false;
			Vector2 mouse = Raylib.GetMousePosition();

			if (Raylib.CheckCollisionPointRec(mouse, _mainDebugHeader))
			{
				_mainDebugExpanded = !_mainDebugExpanded;
				return true;
			}
			if (Raylib.CheckCollisionPointRec(mouse, _heldItemDebugHeader))
			{
				_heldItemDebugExpanded = !_heldItemDebugExpanded;
				return true;
			}
			if (Raylib.CheckCollisionPointRec(mouse, _tileDebugHeader))
			{
				_tileDebugExpanded = !_tileDebugExpanded;
				return true;
			}
			return false;
		}

        public static void DrawPlayerHUD(int hp, int maxHp, Equipment equip)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            int barW = 200, barH = 18;
            int bx = 14, by = sh - 90;
            Raylib.DrawRectangle(bx - 2, by - 2, barW + 4, barH + 4, new Color(20, 20, 20, 200));
            Raylib.DrawRectangle(bx, by, barW, barH, new Color(80, 20, 20, 255));
            int fill = (int)(barW * (float)hp / maxHp);
            Raylib.DrawRectangle(bx, by, fill, barH, new Color(200, 40, 40, 255));
            Raylib.DrawRectangleLines(bx, by, barW, barH, new Color(120, 120, 120, 200));
            Raylib.DrawText($" {hp}/{maxHp}", bx + 6, by + 2, 14, Color.White);
            int ax = 14, ay = sh - 68;
            Raylib.DrawText($" ATK: {equip.TotalAttack}   DEF: {equip.TotalArmor}", ax, ay, 14, new Color(220, 220, 180, 255));
            if (equip.MainHand != null)
                Raylib.DrawText($" {equip.MainHand.Name}", ax, ay + 18, 13, Color.Gold);
        }

        public static void DrawHotbar(List<Item> inventory, int selectedSlot)
        {
            const int slots = 8;
            const int slotSize = 50;
            const int padding = 4;
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            int totalW = slots * (slotSize + padding) - padding;
            int startX = (sw - totalW) / 2;
            int barY = sh - slotSize - 12;
            Raylib.DrawRectangle(startX - 6, barY - 6, totalW + 12, slotSize + 12, new Color(20, 20, 20, 180));
            Raylib.DrawRectangleLines(startX - 6, barY - 6, totalW + 12, slotSize + 12, new Color(80, 80, 80, 200));
            for (int i = 0; i < slots; i++)
            {
                int sx = startX + i * (slotSize + padding);
                bool sel = i == selectedSlot;
                Raylib.DrawRectangle(sx, barY, slotSize, slotSize,
                    sel ? new Color(80, 80, 50, 220) : new Color(40, 40, 40, 200));
                Raylib.DrawRectangleLines(sx, barY, slotSize, slotSize,
                    sel ? Color.Gold : new Color(70, 70, 70, 200));
                if (i < inventory.Count)
                {
                    var item = inventory[i];
					ItemRenderer.DrawItem(item,
						new Rectangle(sx + UIManager.ScaleInt(7), barY + UIManager.ScaleInt(7), UIManager.ScaleInt(36), UIManager.ScaleInt(36)));
                    Raylib.DrawText(item.Count.ToString(), sx + slotSize - 14, barY + slotSize - 16, 12, Color.White);
                }
                Raylib.DrawText((i + 1 <= 9 ? (i + 1).ToString() : "0"), sx + 3, barY + 3, 11, new Color(160, 160, 160, 200));
            }
        }

        public static void DrawInventoryAndCraft(
            List<Item> inventory, Equipment equip,
            int craftTab, ref int craftSelected,
            bool nearWorkbench, bool nearFurnace,
            string activeStation)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 140));
            int panelW = 720, panelH = 460;
            int px = (sw - panelW) / 2;
            int py = (sh - panelH) / 2;
            Raylib.DrawRectangle(px, py, panelW, panelH, new Color(28, 28, 35, 255));
            Raylib.DrawRectangleLines(px, py, panelW, panelH, new Color(90, 90, 110, 255));
            int colLeft = px + 10;
            Raylib.DrawText("INVENTAIRE", colLeft, py + 10, 16, Color.Gold);
            for (int i = 0; i < inventory.Count; i++)
            {
                int iy = py + 35 + i * 42;
                if (iy + 42 > py + panelH - 30) break;
                var itm = inventory[i];
                Raylib.DrawRectangle(colLeft, iy, 220, 38, new Color(40, 40, 50, 255));
                Raylib.DrawRectangleLines(colLeft, iy, 220, 38, new Color(60, 60, 70, 255));
				ItemRenderer.DrawItem(itm,
					new Rectangle(colLeft + UIManager.ScaleInt(5), iy + UIManager.ScaleInt(3), UIManager.ScaleInt(32), UIManager.ScaleInt(32)));
                Raylib.DrawText($"{itm.Name}", colLeft + 45, iy + 5, 16, Color.White);
                Raylib.DrawText($"x{itm.Count}", colLeft + 45, iy + 22, 13, new Color(180, 180, 180, 255));
            }
            if (inventory.Count == 0)
                Raylib.DrawText("Inventaire vide", colLeft + 10, py + 50, 16, Color.Gray);
            int eqX = colLeft + 230;
            Raylib.DrawText("ÉQUIPEMENT", eqX, py + 10, 16, Color.Gold);
            DrawEquipSlot(eqX, py + 35, "Tête", equip.Head);
            DrawEquipSlot(eqX, py + 80, "Corps", equip.Body);
            DrawEquipSlot(eqX, py + 125, "Jambes", equip.Legs);
            DrawEquipSlot(eqX, py + 185, "Main", equip.MainHand);
            DrawEquipSlot(eqX, py + 230, "Bouclier", equip.OffHand);
            Raylib.DrawLine(px + 390, py + 5, px + 390, py + panelH - 5, new Color(70, 70, 90, 255));
            int colRight = px + 400;
            string stationLabel = activeStation switch
            {
                "etabli" => "ÉTABLI",
                "fourneau" => "FOURNEAU",
                _ => "ARTISANAT (basique)"
            };
            Raylib.DrawText(stationLabel, colRight, py + 10, 16, Color.Gold);
            var recipes = GameData.Recipes.Where(r =>
                r.RequiresStation == activeStation ||
                (activeStation == "etabli" && r.RequiresStation == "") ||
                (activeStation == "" && r.RequiresStation == "")
            ).ToList();
            for (int i = 0; i < recipes.Count; i++)
            {
                int ry = py + 35 + i * 52;
                if (ry + 52 > py + panelH - 30) break;
                var rec = recipes[i];
                bool can = CanCraft(inventory, rec);
                bool hov = i == craftSelected;
                Color bg = hov ? new Color(55, 55, 30, 255) : new Color(38, 38, 48, 255);
                Color bdr = can ? (hov ? Color.Gold : new Color(100, 160, 60, 255)) : new Color(70, 50, 50, 255);
                Raylib.DrawRectangle(colRight, ry, 300, 48, bg);
                Raylib.DrawRectangleLines(colRight, ry, 300, 48, bdr);
                if (GameData.ItemDatabase.TryGetValue(rec.ResultId, out var res))
                {
                    Color nc = can ? Color.White : new Color(130, 130, 130, 255);
					var resultItem = new Item(res.Name, 1, res.Color, res.Icon);
					ItemRenderer.DrawItem(resultItem,
						new Rectangle(colRight + UIManager.ScaleInt(5), ry + UIManager.ScaleInt(8), UIManager.ScaleInt(30), UIManager.ScaleInt(30)), nc);
                    Raylib.DrawText($"{rec.ResultName} x{rec.ResultCount}", colRight + 42, ry + 6, 16, nc);
                    string ingStr = string.Join("  ", rec.Ingredients.Select(ing =>
                    {
                        if (GameData.ItemDatabase.TryGetValue(ing.id, out var d))
                        {
                            var it = inventory.Find(x => x.Name == d.Name);
                            int have = it?.Count ?? 0;
                            return $"{d.Name}:{have}/{ing.qty}";
                        }
                        return "";
                    }));
                    Raylib.DrawText(ingStr, colRight + 42, ry + 27, 12,
                        can ? new Color(160, 220, 120, 255) : new Color(200, 100, 100, 255));
                }
                if (hov && can)
                    Raylib.DrawText("[ENTRÉE]", colRight + 235, ry + 8, 13, Color.Yellow);
            }
            Raylib.DrawText("[↑↓] Naviguer  [Entrée] Fabriquer  [I] Fermer",
                px + 10, py + panelH - 22, 13, new Color(140, 140, 160, 255));
        }

        static void DrawEquipSlot(int x, int y, string label, Item? item)
        {
            Raylib.DrawRectangle(x, y, 150, 36, new Color(40, 40, 55, 255));
            Raylib.DrawRectangleLines(x, y, 150, 36, new Color(70, 70, 90, 255));
            Raylib.DrawText(label + ":", x + 4, y + 4, 12, new Color(160, 160, 180, 255));
            if (item != null)
            {
				ItemRenderer.DrawItem(item, new Rectangle(x + UIManager.ScaleInt(55), y + UIManager.ScaleInt(4), UIManager.ScaleInt(28), UIManager.ScaleInt(28)));
                Raylib.DrawText(item.Name, x + 88, y + 10, 12, Color.White);
            }
            else
                Raylib.DrawText("—", x + 55, y + 10, 14, Color.DarkGray);
        }

        public static void UpdateAndDrawNotifications(List<Notification> notifs, float dt)
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			
			for (int i = notifs.Count - 1; i >= 0; i--)
			{
				var n = notifs[i];
				n.Timer -= dt;
				if (n.Timer <= 0) 
				{ 
					notifs.RemoveAt(i); 
					continue; 
				}
				
				float alpha = n.Timer > 1f ? 1f : n.Timer;
				int ab = (int)(alpha * 255);
				bool useWideLayout = n.UseWideLayout || n.PreviewEntity != null;
				
				// Dimensions de la bulle
				int notifWidth = useWideLayout ? 360 : 280;
				int notifHeight = useWideLayout ? 92 : 52;
				int x = sw - notifWidth - 15;
				int y = sh - 90 - (i * (notifHeight + 8));
				
				// Position de l'icône / prévisualisation
				int iconSize = useWideLayout ? 58 : 36;
				int iconX = x + 10;
				int iconY = y + (notifHeight - iconSize) / 2;
				
				// Calcul du texte à afficher
				string displayText = n.GetDisplayMessage();
				int textX = x + (useWideLayout ? 84 : iconSize + 16);
				int textY = y + (useWideLayout ? 14 : notifHeight / 2 - 8);
				int textSize = useWideLayout ? 14 : 16;
				int textMaxWidth = useWideLayout ? notifWidth - 110 : notifWidth - 70;
				
				// Couleur de fond basée sur la rareté
				Color rarityColor = n.TextColor;
				
				// Assombrir la couleur pour le fond (plus subtil)
				Color bgColor = new Color(
					(byte)(rarityColor.R * 0.15f),
					(byte)(rarityColor.G * 0.15f),
					(byte)(rarityColor.B * 0.15f),
					(int)(alpha * 200)
				);
				
				// Bordure colorée selon la rareté
				Color borderColor = new Color(
					rarityColor.R,
					rarityColor.G,
					rarityColor.B,
					(int)(alpha * 180)
				);
				
				// Dessiner la bulle arrondie avec bordure
				Raylib.DrawRectangleRounded(new Rectangle(x, y, notifWidth, notifHeight), 0.3f, 8, bgColor);
				
				// Bordure fine (2 pixels)
				Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, notifWidth, notifHeight), 0.3f, 8, 2, borderColor);
				
				if (useWideLayout && n.PreviewEntity != null)
				{
					Rectangle previewRect = new Rectangle(iconX, iconY, iconSize, iconSize);
					Raylib.DrawRectangleRounded(previewRect, 0.2f, 8, new Color(60, 46, 30, 220));
					Raylib.DrawRectangleRoundedLines(previewRect, 0.2f, 8, 1.2f, new Color(210, 180, 110, 180));
					QuestJournalUI.DrawSharedPortrait(n.PreviewEntity, previewRect);
				}
				else if (n.ItemIcon.Id != 0)
				{
					Rectangle srcRect = new Rectangle(0, 0, n.ItemIcon.Width, n.ItemIcon.Height);
					Rectangle destRect = new Rectangle(iconX, iconY, iconSize, iconSize);
					Raylib.DrawTexturePro(n.ItemIcon, srcRect, destRect, Vector2.Zero, 0, new Color(255, 255, 255, ab));
				}
				else
				{
					// Fallback : cercle coloré selon la rareté
					Raylib.DrawCircle(iconX + iconSize/2, iconY + iconSize/2, iconSize/2, rarityColor);
				}
				
				DrawNotificationText(displayText, textX, textY, textMaxWidth, textSize, new Color(255, 255, 255, ab), ab);
			}
		}

		private static void DrawNotificationText(string text, int x, int y, int maxWidth, int size, Color color, int alpha)
		{
			var lines = WrapNotificationText(text, maxWidth, size);
			for (int i = 0; i < lines.Count; i++)
			{
				int lineY = y + i * (size + 4);
				Color shadow = new Color(0, 0, 0, alpha);
				FontManager.DrawText(lines[i], x - 1, lineY - 1, size, shadow);
				FontManager.DrawText(lines[i], x + 1, lineY - 1, size, shadow);
				FontManager.DrawText(lines[i], x - 1, lineY + 1, size, shadow);
				FontManager.DrawText(lines[i], x + 1, lineY + 1, size, shadow);
				FontManager.DrawText(lines[i], x, lineY, size, color);
			}
		}

		private static List<string> WrapNotificationText(string text, int maxWidth, int size)
		{
			var lines = new List<string>();
			if (string.IsNullOrWhiteSpace(text))
			{
				lines.Add("");
				return lines;
			}

			var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string current = "";
			foreach (var word in words)
			{
				string candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
				if (FontManager.MeasureText(candidate, size) <= maxWidth || string.IsNullOrEmpty(current))
				{
					current = candidate;
				}
				else
				{
					lines.Add(current);
					current = word;
				}
			}
			if (!string.IsNullOrEmpty(current)) lines.Add(current);
			return lines;
		}


		public static void DrawDebugMenu(Vector2 playerPos, float zoom, int npcCount, int animalCount, float dt, Camera2D camera, Item? heldItem = null)
        {
            int fps = Raylib.GetFPS();
            float ms = dt * 1000f;
            Color fpsColor = fps >= 55 ? Color.Green : fps >= 30 ? Color.Yellow : Color.Red;
			int x = 10;
            //  Panneau agrandi (310 -> 420 -> 460) pour loger les nouvelles lignes PERF
            // (Chunks (stream) et Autre update) ajoutées sous "Grille tuiles".
			const int panelWidth = 340;
			const int expandedPanelHeight = 460;
			const int collapsedPanelHeight = 28;
			int panelHeight = _mainDebugExpanded ? expandedPanelHeight : collapsedPanelHeight;
			_mainDebugHeader = new Rectangle(x - 4, 6, panelWidth, collapsedPanelHeight);
			DrawDebugPanelFrame(x, 10, panelWidth, panelHeight, _mainDebugHeader, _mainDebugExpanded, "[F3] DEBUG");

			int panelsTop = 10 + panelHeight + 10;
			DrawHeldItemDebugPanel(heldItem, x, panelsTop, panelWidth, _heldItemDebugExpanded);
			int tileTop = panelsTop + (_heldItemDebugExpanded ? 308 : 36);
			DrawPlayerTileDebugPanel(x, tileTop, panelWidth, _tileDebugExpanded);
			if (!_mainDebugExpanded) return;

			int y = 38, dy = 18;
            DebugLine(ref y, dy, $"FPS     : {fps}", fpsColor);
            DebugLine(ref y, dy, $"Frame ms: {ms:F2} ms", Color.LightGray);
            DebugLine(ref y, dy, "", Color.Blank);
            DebugLine(ref y, dy, $"Pos     : ({playerPos.X:F0}, {playerPos.Y:F0})", Color.White);
			int tileX = (int)Math.Floor(playerPos.X / Program.TileSize);
			int tileY = (int)Math.Floor(playerPos.Y / Program.TileSize);
			Biome currentBiome = World.GetBiomeAt(tileX, tileY);
			DebugLine(ref y, dy, $"Tuile   : ({tileX}, {tileY})", Color.White);
			DebugLine(ref y, dy, $"Biome   : {WorldGeneration.GetBiomeDisplayName(currentBiome)}", Color.White);
            DebugLine(ref y, dy, $"Zoom    : {zoom:F2}x", Color.White);
            DebugLine(ref y, dy, "", Color.Blank);
            DebugLine(ref y, dy, $"NPCs    : {npcCount}", Color.White);
            DebugLine(ref y, dy, $"Animaux : {animalCount}", Color.White);
            DebugLine(ref y, dy, "", Color.Blank);

			//  PERF : temps passé dans l'IA (Entity.Update) vs le rendu du monde (World.DrawWorld),
			// pour voir d'un coup d'oeil laquelle des deux domine quand le FPS chute. Valeurs
			// lissées (EMA) pour rester lisibles ; "instantané" (dernière frame) entre parenthèses.
			float updateMs = PerfStats.GetSmoothedMs(PerfStats.Section.EntityUpdate);
			float drawMs = PerfStats.GetSmoothedMs(PerfStats.Section.WorldDraw);
			float tileGridMs = PerfStats.GetSmoothedMs(PerfStats.Section.TileGridFill);
			float updateMsInstant = PerfStats.GetLastFrameMs(PerfStats.Section.EntityUpdate);
			float drawMsInstant = PerfStats.GetLastFrameMs(PerfStats.Section.WorldDraw);
			float tileGridMsInstant = PerfStats.GetLastFrameMs(PerfStats.Section.TileGridFill);
			Color PerfColor(float v) => v >= 8f ? Color.Red : v >= 4f ? Color.Yellow : Color.LightGray;
			DebugLine(ref y, dy, "── PERF ────────────────────", Color.Gold);
			DebugLine(ref y, dy, $"IA (Update)  : {updateMs:F2} ms ({updateMsInstant:F2})", PerfColor(updateMs));
			DebugLine(ref y, dy, $"Rendu (Draw) : {drawMs:F2} ms ({drawMsInstant:F2})", PerfColor(drawMs));
			// Sous-poste du rendu : collecte des données par tuile (chunks) avant tout dessin GPU.
			// Si ce chiffre représente une grosse part de "Rendu (Draw)" en dézoom, le goulot
			// est bien côté lookups de chunk et pas côté DrawTexturePro.
			DebugLine(ref y, dy, $"  Grille tuiles: {tileGridMs:F2} ms ({tileGridMsInstant:F2})", PerfColor(tileGridMs));

			// Chargement/génération des chunks proches du joueur — suspect n°1 pour les pics de
			// frame en dézoom ou en déplacement rapide (World.UpdateActiveChunks).
			float chunkMs = PerfStats.GetSmoothedMs(PerfStats.Section.ChunkStreaming);
			float chunkMsInstant = PerfStats.GetLastFrameMs(PerfStats.Section.ChunkStreaming);
			DebugLine(ref y, dy, $"Chunks (stream): {chunkMs:F2} ms ({chunkMsInstant:F2})", PerfColor(chunkMs));

			//  Tout ce qui reste dans UpdateGameplay et qui n'est pas encore mesuré séparément
			// (crops, boats, campfires, UI, quêtes, particules...). Calculé par soustraction de
			// GameplayUpdateTotal - EntityUpdate - ChunkStreaming. Un chiffre élevé ici indique où
			// découper la prochaine section de mesure, pas un problème en soi.
			float otherUpdateMs = PerfStats.GetOtherUpdateMs(smoothed: true);
			float otherUpdateMsInstant = PerfStats.GetOtherUpdateMs(smoothed: false);
			DebugLine(ref y, dy, $"Autre update : {otherUpdateMs:F2} ms ({otherUpdateMsInstant:F2})", PerfColor(otherUpdateMs));
			DebugLine(ref y, dy, $"Entités tot. : {PerfStats.EntitiesTotal}", Color.White);
			DebugLine(ref y, dy, $"  IA complète: {PerfStats.EntitiesFullAiUpdated}", Color.White);
			DebugLine(ref y, dy, $"  Dessinées  : {PerfStats.EntitiesDrawnThisFrame}", Color.White);
            DebugLine(ref y, dy, "", Color.Blank);

        }

		private static void DrawHeldItemDebugPanel(Item? item, int x, int top, int width, bool expanded)
		{
			const int headerHeight = 28;
			const int expandedHeight = 300;
			int height = expanded ? expandedHeight : headerHeight;
			_heldItemDebugHeader = new Rectangle(x - 4, top - 4, width, headerHeight);
			DrawDebugPanelFrame(x, top, width, height, _heldItemDebugHeader, expanded, "ITEM EN MAIN");
			if (!expanded) return;

			int y = top + headerHeight + 4;
			int dy = 16;
			if (item == null)
			{
				DebugLineAt(ref y, x, dy, "Aucun item", Color.LightGray);
				return;
			}

			int itemId = Program.GetItemId(item.Name);
			bool hasData = GameData.ItemDatabase.TryGetValue(itemId, out var itemData);
			string metadata = item.Metadata ?? "";
			var metadataEntries = ParseMetadataEntries(metadata);

			DebugLineAt(ref y, x, dy, $"Nom        : {item.Name}", Color.White);
			DebugLineAt(ref y, x, dy, $"ID         : {itemId} | Count: {item.Count}", Color.White);
			Color metadataColor = new Color(130, 230, 140, 255);
			DebugLineAt(ref y, x, dy, $"Metadata   : {metadataEntries.Count} clé(s)", metadataColor);
			if (metadataEntries.Count == 0)
			{
				DebugLineAt(ref y, x, dy, "  <vide>", metadataColor);
			}
			else
			{
				for (int i = 0; i < Math.Min(metadataEntries.Count, 5); i++)
				{
					var (key, value) = metadataEntries[i];
					string label = string.IsNullOrEmpty(key)
						? $"  {TruncateForDebug(value, 80)}"
						: $"  {key}={TruncateForDebug(value, 80)}";
					DebugLineAt(ref y, x, dy, label, metadataColor);
				}
				if (metadataEntries.Count > 5)
					DebugLineAt(ref y, x, dy, $"  ... (+{metadataEntries.Count - 5} autres)", metadataColor);
			}

			if (hasData)
			{
				string basePath = $"assets/items/{itemData.TextureName}.png";
				string overlayPath = $"assets/items/{itemData.TextureName}_1.png";
				DebugLineAt(ref y, x, dy, $"Key        : {itemData.Key}", Color.White);
				DebugLineAt(ref y, x, dy, $"Type       : {itemData.Type} | Stack max: {itemData.StackSize}", Color.White);
				DebugLineAt(ref y, x, dy, $"Texture    : {itemData.TextureName}", Color.White);
				Color spriteOkColor = new Color(130, 230, 140, 255);
				DebugLineAt(ref y, x, dy, $"Base       : {basePath} ({GetDebugTextureInfo(basePath)})", File.Exists(basePath) ? spriteOkColor : Color.Red);
				DebugLineAt(ref y, x, dy, $"Overlay    : {overlayPath} ({GetDebugTextureInfo(overlayPath)})", File.Exists(overlayPath) ? spriteOkColor : Color.Red);
			}
			else
			{
				DebugLineAt(ref y, x, dy, "Définition : introuvable", Color.Red);
			}

			int previewSize = UIManager.ScaleInt(72);
			ItemRenderer.DrawItem(item, new Rectangle(x + width - previewSize - 18, top + headerHeight + 8, previewSize, previewSize));
		}

		private static void DrawPlayerTileDebugPanel(int x, int top, int width, bool expanded)
		{
			const int headerHeight = 28;
			const int expandedHeight = 300;
			int height = expanded ? expandedHeight : headerHeight;
			_tileDebugHeader = new Rectangle(x - 4, top - 4, width, headerHeight);
			DrawDebugPanelFrame(x, top, width, height, _tileDebugHeader, expanded, "TUILE SOUS LE JOUEUR");
			if (!expanded) return;

			int tileX = (int)Math.Floor(Program.GetPlayerPosition().X / Program.TileSize);
			int tileY = (int)Math.Floor(Program.GetPlayerPosition().Y / Program.TileSize);
			int objectId = World.GetObjectIdAt(tileX, tileY);
			int groundId = World.GetGroundTileIdAt(tileX, tileY);
			int overlayId = World.GetOverlayAt(tileX, tileY);
			var objectTile = WorldTileRegistry.GetTile(objectId);
			var groundTile = WorldTileRegistry.GetTile(groundId);
			var overlayTile = WorldTileRegistry.GetTile(overlayId);
			var metadata = World.GetChunkAt(tileX, tileY)?.GetTileMetaBag(tileX, tileY);
			int y = top + headerHeight + 4;
			const int dy = 16;
			DebugLineAt(ref y, x, dy, $"Position : ({tileX}, {tileY}) | hauteur {World.GetHeightAt(tileX, tileY)}", Color.White);
			DebugLineAt(ref y, x, dy, $"Objet    : {(objectTile == null ? "aucun" : objectTile.Name)} (ID {objectId})", Color.White);
			DebugLineAt(ref y, x, dy, $"Sol      : {(groundTile == null ? "inconnu" : groundTile.Name)} (ID {groundId})", Color.White);
			DebugLineAt(ref y, x, dy, $"Overlay  : {(overlayTile == null ? "aucun" : overlayTile.Name)} (ID {overlayId})", Color.White);
			DebugLineAt(ref y, x, dy, $"Biome    : {WorldGeneration.GetBiomeDisplayName(World.GetBiomeAt(tileX, tileY))}", Color.White);
			if (objectTile != null)
			{
				DebugLineAt(ref y, x, dy, $"Matière  : {objectTile.Material} | solidité {objectTile.Hardness} | outil {objectTile.MiningTool}", Color.LightGray);
				DebugLineAt(ref y, x, dy, $"Praticable: {objectTile.Walkable} | cassable: {objectTile.Diggable}", Color.LightGray);
				string size = objectTile.Size == null ? "1x1" : $"{objectTile.Size.Width}x{objectTile.Size.Height}";
				string collision = objectTile.CollisionSize == null ? "défaut" : $"{objectTile.CollisionSize.Width}x{objectTile.CollisionSize.Height}";
				DebugLineAt(ref y, x, dy, $"Taille   : {size} | collision {collision}", Color.LightGray);
			}
			var entries = metadata?.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToList();
			DebugLineAt(ref y, x, dy, $"Métadonnées: {entries?.Count ?? 0}", new Color(130, 230, 140, 255));
			if (entries != null)
			{
				foreach (var entry in entries.Take(6))
					DebugLineAt(ref y, x, dy, $"  {entry.Key}={TruncateForDebug(entry.Value, 48)}", new Color(130, 230, 140, 255));
				if (entries.Count > 6)
					DebugLineAt(ref y, x, dy, $"  ... (+{entries.Count - 6} autres)", new Color(130, 230, 140, 255));
			}
		}

		private static void DrawDebugPanelFrame(int x, int top, int width, int height, Rectangle header, bool expanded, string title)
		{
			Vector2 mouse = Raylib.GetMousePosition();
			bool hovered = Raylib.CheckCollisionPointRec(mouse, header);
			Raylib.DrawRectangle(x - 4, top - 4, width, height, new Color(0, 0, 0, 185));
			Raylib.DrawRectangleLines(x - 4, top - 4, width, height, new Color(80, 80, 80, 220));
			Raylib.DrawRectangle((int)header.X, (int)header.Y, (int)header.Width, (int)header.Height,
				hovered ? new Color(60, 75, 95, 230) : new Color(35, 42, 55, 220));
			DebugLineAt(ref top, x, 18, $"{(expanded ? "[-]" : "[+]" )} {title}", Color.Gold);
		}

		private static List<(string Key, string Value)> ParseMetadataEntries(string rawMetadata)
		{
			if (string.IsNullOrWhiteSpace(rawMetadata))
				return new List<(string Key, string Value)>();

			var entries = new List<(string Key, string Value)>();
			foreach (var part in rawMetadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				int separatorIndex = part.IndexOf('=');
				if (separatorIndex <= 0)
				{
					entries.Add((string.Empty, part));
					continue;
				}

				string key = part.Substring(0, separatorIndex).Trim();
				string value = part.Substring(separatorIndex + 1).Trim();
				entries.Add((key, value));
			}
			return entries;
		}

		private static string TruncateForDebug(string text, int maxLength)
		{
			if (string.IsNullOrEmpty(text)) return "";
			return text.Length <= maxLength ? text : text.Substring(0, Math.Max(0, maxLength - 3)) + "...";
		}

		private static void DebugLineAt(ref int y, int x, int dy, string text, Color color)
		{
			Raylib.DrawText(text, x, y, 13, color);
			y += dy;
		}

		private static string GetDebugTextureInfo(string path)
		{
			if (!File.Exists(path)) return "absent";
			Image image = Raylib.LoadImage(path);
			string info = image.Width > 0 && image.Height > 0 ? $"{image.Width}x{image.Height}" : "invalide";
			Raylib.UnloadImage(image);
			return info;
		}

        static void DebugLine(ref int y, int dy, string text, Color c)
        {
            Raylib.DrawText(text, 10, y, 15, c);
            y += dy;
        }

        public static bool CanCraft(List<Item> inv, CraftRecipe rec)
        {
            foreach (var (id, qty) in rec.Ingredients)
            {
                if (!GameData.ItemDatabase.TryGetValue(id, out var d)) return false;
                var it = inv.Find(x => x.Name == d.Name);
                if (it == null || it.Count < qty) return false;
            }
            return true;
        }

        public static Vector2 RotateVec(Vector2 v, float angle)
        {
            float r = angle * MathF.PI / 180f;
            return new Vector2(v.X * MathF.Cos(r) - v.Y * MathF.Sin(r),
                               v.X * MathF.Sin(r) + v.Y * MathF.Cos(r));
        }
    }
	
	// Classe pour les dégâts flottants
	public class FloatingDamage
	{
		public Vector2 Position;
		public int Damage;  // Ce champ peut être positif (soin) ou négatif (dégât)
		public float Timer;
		public float MaxTimer;
		public float FloatOffset;
		public Color Color;
		public Color? OutlineColor;  //  Contour coloré (null = pas de contour)
		
		public FloatingDamage(Vector2 pos, int damage, Color color)
		{
			Position = pos;
			Damage = damage;  // damage peut être positif pour les soins
			MaxTimer = 1.2f;
			Timer = MaxTimer;
			FloatOffset = 0f;
			Color = color;
		}
		
		public void Update(float dt)
		{
			Timer -= dt;
			//  Éviter les valeurs négatives ou trop grandes
			float remaining = Math.Max(0, MaxTimer - Timer);
			FloatOffset = remaining * -40f;
			
			//  Limiter le décalage
			if (FloatOffset < -80f) FloatOffset = -80f;
		}
		
		public bool IsAlive => Timer > 0f;
	}
	
	public class Particle
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public Color Color;
		public float Size;
		public float Lifetime;
		public float MaxLifetime;
		public float GravityY = 0f;
		public float Drag = 0.98f;
		public float? GroundY = null;
		public Vector2? TargetPosition = null;
		public bool UseTargetLanding = false;
		private bool _isAlive = true;   // Champ interne pour contrôler la vie

		public Particle(Vector2 pos, Vector2 vel, Color color, float size, float lifetime)
		{
			Position = pos;
			Velocity = vel;
			Color = color;
			Size = size;
			Lifetime = lifetime;
			MaxLifetime = lifetime;
			_isAlive = true;
		}

		public virtual void Update(float dt)
		{
			if (GravityY != 0f)
				Velocity += new Vector2(0f, GravityY * dt);

			Position += Velocity * dt;
			Velocity *= Drag;
			Lifetime -= dt;

			if (UseTargetLanding && TargetPosition.HasValue)
			{
				if (Vector2.Distance(Position, TargetPosition.Value) <= 2f)
					_isAlive = false;
			}
			else if (GroundY.HasValue && Position.Y >= GroundY.Value)
				_isAlive = false;
			if (Lifetime <= 0) _isAlive = false;
		}

		public bool IsAlive => _isAlive && Lifetime > 0;

		public float GetAlpha()
		{
			return Lifetime / MaxLifetime;
		}

		// Méthode pour tuer la particule prématurément
		public void Kill()
		{
			_isAlive = false;
		}
	}
	
	public class MagicExplosionEffect : Particle
	{
		private readonly float _frameDuration;
		private readonly float _scale;
		private readonly List<Texture2D> _frames;
		private float _frameTimer;
		private int _frameIndex;

		public MagicExplosionEffect(Vector2 pos, Color color, float scale = 1f)
			: base(pos, Vector2.Zero, color, 36f, 0.4f)
		{
			_frames = Program.MagicExplosionTextures;
			_frameDuration = 0.07f;
			_frameTimer = 0f;
			_frameIndex = 0;
			_scale = Math.Max(1.2f, scale);
		}

		public override void Update(float dt)
		{
			_frameTimer += dt;
			while (_frames.Count > 0 && _frameIndex < _frames.Count - 1 && _frameTimer >= _frameDuration)
			{
				_frameTimer -= _frameDuration;
				_frameIndex++;
			}
			base.Update(dt);
		}

		public void Draw()
		{
			if (_frames.Count == 0 || _frameIndex >= _frames.Count)
				return;

			Texture2D frame = _frames[_frameIndex];
			if (frame.Id == 0 || frame.Width <= 0 || frame.Height <= 0)
				return;

			Color tint = new Color(Color.R, Color.G, Color.B, (byte)255);
			float targetSize = MathF.Max(20f, 42f * _scale);
			float scale = targetSize / MathF.Max(1f, frame.Width);
			Vector2 origin = new Vector2(frame.Width / 2f, frame.Height / 2f);
			Rectangle src = new Rectangle(0, 0, frame.Width, frame.Height);
			Rectangle dest = new Rectangle(Position.X, Position.Y, frame.Width * scale, frame.Height * scale);
			Raylib.DrawTexturePro(frame, src, dest, origin, 0f, tint);
		}
	}

	public class SlashEffect : Particle
	{
		private readonly List<Texture2D> _frames;
		private int _frameIndex;
		private float _frameTimer;
		private float _frameDuration;
		private readonly float _rotationDeg;
		private readonly float _scale;
		private readonly float _lifeTime;

		public SlashEffect(Vector2 pos, Vector2 direction)
			: base(pos, Vector2.Zero, Color.White, 36f, 0.35f)
		{
			_frames = Program.SlashTextures ?? new List<Texture2D>();
			_frameIndex = 0;
			_frameTimer = 0f;
			_lifeTime = 0.35f;
			_frameDuration = _lifeTime / Math.Max(1, _frames.Count);
			_rotationDeg = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
			_scale = 1.0f; // échelle modérée
		}

		public override void Update(float dt)
		{
			if (_frames.Count > 0)
			{
				_frameTimer += dt;
				while (_frameTimer >= _frameDuration && _frameDuration > 0f)
				{
					_frameTimer -= _frameDuration;
					_frameIndex = Math.Min(_frameIndex + 1, _frames.Count - 1);
				}
			}
			base.Update(dt);
		}

		public void Draw()
		{
			if (_frames == null || _frames.Count == 0) return;
			if (_frameIndex < 0 || _frameIndex >= _frames.Count) return;
			Texture2D frame = _frames[_frameIndex];
			if (frame.Id == 0) return;

			// Opacité constante (pas de fondu)
			Color tint = new Color((byte)Color.R, (byte)Color.G, (byte)Color.B, (byte)255);
			float targetSize = 64f * _scale; // taille raisonnable
			float scale = targetSize / MathF.Max(1f, frame.Width);
			Vector2 origin = new Vector2(frame.Width / 2f, frame.Height / 2f);
			Rectangle src = new Rectangle(0, 0, frame.Width, frame.Height);
			Rectangle dest = new Rectangle(Position.X, Position.Y, frame.Width * scale, frame.Height * scale);
			Raylib.DrawTexturePro(frame, src, dest, origin, _rotationDeg, tint);
		}
	}

	//  NUAGE DE SPORES EMPOISONNÉES (attaque du bolet, voir Program.SpawnPoisonCloudBurst
	// et Entity.MeleeCombatBehavior). Utilise les 5 textures assets/extras/cloudpop_1..5 :
	// les 2 premières servent à l'apparition (pop), la 3e reste figée tant que le nuage est
	// "présent" (c'est durant cette phase qu'il empoisonne), et les 2 dernières à la
	// disparition. Un petit délai aléatoire avant l'apparition permet à une salve de nuages
	// de "poper" en quelques instants les uns après les autres plutôt que tous d'un coup.
	public class PoisonCloudParticle : Particle
	{
		private readonly List<Texture2D> _frames;
		private readonly float _spawnDuration;
		private readonly float _despawnDuration;
		private readonly float _radius;
		private readonly Entity? _source;
		private readonly int _targetConnectionId;
		private float _startDelay;
		private float _poisonTickTimer = 0f;
		private const float POISON_APPLY_INTERVAL = 0.4f;
		private const float POISON_APPLY_DURATION = 1.2f; // > interval pour rester continu tant qu'on reste dans le nuage
		private readonly Vector2 _startPos; // Position de départ (centre du bolet)
		private readonly Vector2 _finalPos; // Position finale du nuage

		public PoisonCloudParticle(Vector2 pos, float lifetime, float radius, float startDelay, Entity? source, int targetConnectionId, Vector2? startPos = null)
			: base(pos, Vector2.Zero, Color.White, radius * 2f, lifetime)
		{
			_frames = Program.PoisonCloudTextures ?? new List<Texture2D>();
			_spawnDuration = Math.Min(0.35f, lifetime * 0.2f);
			_despawnDuration = Math.Min(0.35f, lifetime * 0.2f);
			_radius = radius;
			_startDelay = Math.Max(0f, startDelay);
			_source = source;
			_targetConnectionId = targetConnectionId;
			_finalPos = pos; // Position finale
			_startPos = startPos ?? pos; // Position de départ (par défaut = position finale)
		}

		// Vrai uniquement pendant la phase "présente / figée" (frame du milieu) : c'est la
		// seule phase où le nuage empoisonne, comme demandé (spawn/despawn = purement visuel).
		private bool IsActivePhase => _startDelay <= 0f && Lifetime > _despawnDuration && (MaxLifetime - Lifetime) > _spawnDuration;

		public override void Update(float dt)
		{
			if (_startDelay > 0f)
			{
				_startDelay -= dt;
				return; // pas encore apparu : ni animation, ni durée de vie qui s'écoule
			}

			base.Update(dt);
			if (!IsAlive) return;

			// Interpoler la position entre _startPos et _finalPos durant la phase spawn
			float timeSinceSpawn = MaxLifetime - Lifetime;
			if (timeSinceSpawn < _spawnDuration)
			{
				float progress = timeSinceSpawn / _spawnDuration;
				Position = Vector2.Lerp(_startPos, _finalPos, progress);
			}
			else
			{
				// Après spawn, rester à la position finale
				Position = _finalPos;
			}

			if (IsActivePhase)
			{
				_poisonTickTimer -= dt;
				if (_poisonTickTimer <= 0f)
				{
					_poisonTickTimer = POISON_APPLY_INTERVAL;
					ApplyPoisonToNearbyTargets();
				}
			}
		}

		private void ApplyPoisonToNearbyTargets()
		{
			//  Comme pour le reste du combat de mêlée, seul le host simule les entités et
			// applique les dégâts/poison ; un client ne fait qu'afficher le nuage.
			if (NetworkManager.IsClient) return;

			// Joueur (local ou distant, via le même mécanisme que les coups classiques).
			Vector2 playerPos = Program.GetPlayerPosition();
			if (Vector2.Distance(Position, playerPos) <= _radius)
			{
				Program.DamagePlayerTarget(0, _targetConnectionId, _source?.GetDisplayName() ?? "un nuage de spores", Program.DeathCause.Poison, POISON_APPLY_DURATION);
			}

			// Autres créatures présentes dans le nuage (hors la source elle-même).
			foreach (var entity in Program.GetEntities())
			{
				if (entity == null || !entity.IsAlive || entity.IsPlayer) continue;
				if (ReferenceEquals(entity, _source)) continue;
				if (Vector2.Distance(Position, entity.WorldPos) <= _radius)
					entity.ApplyPoison(POISON_APPLY_DURATION);
			}
		}

		public void Draw()
		{
			if (_startDelay > 0f || _frames.Count == 0) return;

			int frameIndex;
			float timeSinceSpawn = MaxLifetime - Lifetime;
			if (timeSinceSpawn < _spawnDuration)
			{
				// Apparition : textures 1 puis 2 (index 0 puis 1)
				frameIndex = timeSinceSpawn < _spawnDuration * 0.5f ? 0 : 1;
			}
			else if (Lifetime < _despawnDuration)
			{
				// Disparition : textures 4 puis 5 (index 3 puis 4)
				frameIndex = Lifetime > _despawnDuration * 0.5f ? 3 : 4;
			}
			else
			{
				// Présent, figé : texture du milieu (index 2)
				frameIndex = 2;
			}
			frameIndex = Math.Clamp(frameIndex, 0, _frames.Count - 1);

			Texture2D frame = _frames[frameIndex];
			if (frame.Id == 0 || frame.Width <= 0 || frame.Height <= 0) return;

			float targetSize = _radius * 2f;
			float scale = targetSize / MathF.Max(1f, frame.Width);
			Vector2 origin = new Vector2(frame.Width / 2f, frame.Height / 2f);
			Rectangle src = new Rectangle(0, 0, frame.Width, frame.Height);
			Rectangle dest = new Rectangle(Position.X, Position.Y, frame.Width * scale, frame.Height * scale);
			Raylib.DrawTexturePro(frame, src, dest, origin, 0f, Color.White);
		}
	}

	// Petite particule en forme de trait vertical (montée de niveau global) : un fin
	// rectangle blanc qui s'élève doucement puis s'estompe.
	public class LevelUpStreakParticle : Particle
	{
		private float _width;
		private float _height;

		public LevelUpStreakParticle(Vector2 pos, Vector2 vel, Color color, float width, float height, float lifetime)
			: base(pos, vel, color, width, lifetime)
		{
			_width = width;
			_height = height;
		}

		public void Draw()
		{
			byte alpha = (byte)(GetAlpha() * 255);
			Color drawColor = new Color(Color.R, Color.G, Color.B, alpha);
			Raylib.DrawRectangle((int)(Position.X - _width / 2f), (int)(Position.Y - _height / 2f),
				(int)MathF.Max(1f, _width), (int)MathF.Max(1f, _height), drawColor);
		}
	}
	
	// Classe spécifique pour les particules Zzz (avec animation de texte)
	public class ZzzParticle : Particle
	{
		private string _zzzText;
		private float _rotationSpeed;
		private float _rotation;
		
		public ZzzParticle(Vector2 pos, Vector2 vel, Color color, float size, float lifetime) 
			: base(pos, vel, color, size, lifetime)
		{
			// Choisir aléatoirement "Z", "zZ", "Zz" ou "ZZZ"
			string[] zzzVariants = { "Z", "z", "Zz", "zZ", "ZZ", "ZZZ", "zZz" };
			_zzzText = zzzVariants[Random.Shared.Next(zzzVariants.Length)];
			_rotationSpeed = (float)(Random.Shared.NextDouble() - 0.5) * 90f;
			_rotation = 0f;
		}
		
		public new void Update(float dt)
		{
			base.Update(dt);
			_rotation += _rotationSpeed * dt;
		}
		
		public void Draw()
		{
			float alpha = GetAlpha();
			byte alphaByte = (byte)(alpha * 255);
			Color drawColor = new Color(Color.R, Color.G, Color.B, alphaByte);
			
			float bobOffset = MathF.Sin((float)Raylib.GetTime() * 8f) * 2f;
			
			int fontSize = (int)(Size * 1.2f);
			int textWidth = Raylib.MeasureText(_zzzText, fontSize);
			
			Raylib.DrawText(_zzzText, 
				(int)(Position.X - textWidth / 2), 
				(int)(Position.Y - fontSize / 2 + bobOffset), 
				fontSize, drawColor);
		}
	}

	// Petite feuille d'arbre : tombe doucement en se balançant de gauche à droite,
	// tourne sur elle-même, puis se pose au sol et s'immobilise avant de s'estomper.
	public class LeafParticle : Particle
	{
		private float _rotation;
		private float _rotationSpeed;
		private float _swayPhase;
		private readonly float _swaySpeed;
		private readonly float _swayAmplitude;
		private readonly float _baseX;
		private readonly float _groundY;
		private readonly float _maxFallSpeed;
		private bool _landed = false;
		private float _restTimer;
		private const float RestDuration = 3f;

		public LeafParticle(Vector2 pos, float groundY, float lifetime, Color? tint = null)
			: base(pos, new Vector2(0f, 12f), tint ?? Color.White, 9f, lifetime)
		{
			_groundY = groundY;
			_baseX = pos.X;
			_rotation = (float)(Random.Shared.NextDouble() * 360.0);
			_rotationSpeed = (float)((Random.Shared.NextDouble() - 0.5) * 160f);
			_swayPhase = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
			_swaySpeed = 1.4f + (float)Random.Shared.NextDouble() * 1.6f;
			_swayAmplitude = 8f + (float)Random.Shared.NextDouble() * 16f;
			_maxFallSpeed = 36f + (float)Random.Shared.NextDouble() * 18f;
			GravityY = 24f;
			_restTimer = RestDuration;
		}

		public override void Update(float dt)
		{
			if (!_landed)
			{
				Velocity.Y += GravityY * dt;
				if (Velocity.Y > _maxFallSpeed) Velocity.Y = _maxFallSpeed;

				Position.Y += Velocity.Y * dt;
				_swayPhase += _swaySpeed * dt;
				Position.X = _baseX + MathF.Sin(_swayPhase) * _swayAmplitude;
				_rotation += _rotationSpeed * dt;

				if (Position.Y >= _groundY)
				{
					Position.Y = _groundY;
					_landed = true;
					Velocity = Vector2.Zero;
					Lifetime = RestDuration;
				}
			}
			else
			{
				//  Une fois posée, la feuille cesse de bouger puis s'estompe doucement
				_rotationSpeed *= 0.9f;
				_rotation += _rotationSpeed * dt;
				_restTimer -= dt;
				Lifetime -= dt;
				if (_restTimer <= 0f) Kill();
			}
		}

		public new bool IsAlive => base.IsAlive && (!_landed || _restTimer > 0f);

		public float Rotation => _rotation;

		// Alpha spécifique : pleine opacité tant qu'elle tombe ou vient de se poser,
		// puis fondu progressif pendant le repos au sol.
		public float GetLeafAlpha()
		{
			if (!_landed) return 1f;
			return Math.Clamp(_restTimer / RestDuration, 0f, 1f);
		}

		public void Draw(Texture2D texture)
		{
			if (texture.Id == 0 || texture.Width <= 0 || texture.Height <= 0) return;

			float alpha = GetLeafAlpha();
			byte alphaByte = (byte)(alpha * 255);
			Color tint = Color;
			Color drawColor = new Color(tint.R, tint.G, tint.B, alphaByte);

			float targetSize = Size;
			float scale = targetSize / MathF.Max(1f, texture.Width);
			Vector2 origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
			Rectangle src = new Rectangle(0, 0, texture.Width, texture.Height);
			Rectangle dest = new Rectangle(Position.X, Position.Y, texture.Width * scale, texture.Height * scale);
			Raylib.DrawTexturePro(texture, src, dest, origin, _rotation, drawColor);
		}
	}

	
	public class ArrowProjectile
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float Damage;
		public float Lifetime;
		public bool IsAlive;
		public bool IsStuck;
		public int StuckTileX;
		public int StuckTileY;
		public float StuckOffsetX;
		public float StuckOffsetY;
		public float Angle;
		public bool IsBullet = false;
		//  Vrai pour une flèche tirée par un monstre (ex : gobelin archer) sur le joueur.
		// Change la cible de la vérification de collision : une flèche "hostile" ne touche
		// que le joueur (jamais les autres entités), l'inverse d'une flèche du joueur.
		public bool IsHostile = false;
		public Entity? TargetEntity;
		public Entity? SourceEntity;
		public int TargetConnectionId = -1;

		public ArrowProjectile(Vector2 pos, Vector2 dir, float speed, float damage)
		{
			Position = pos;
			Velocity = dir * speed;
			Damage = damage;
			Lifetime = 10f;
			IsAlive = true;
			IsStuck = false;
			StuckTileX = StuckTileY = 0;
			StuckOffsetX = StuckOffsetY = 0;
			Angle = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
		}
		
		public void Draw()
		{
			if (!IsAlive) return;
			
			Vector2 drawPos;
			float angle = Angle;
			
			if (IsStuck)
			{
				int height = World.GetHeightAt(StuckTileX, StuckTileY);
				float yOffset = -height * Program.TileSize / 4;
				float tileDrawX = StuckTileX * Program.TileSize;
				float tileDrawY = StuckTileY * Program.TileSize + yOffset;
				drawPos = new Vector2(tileDrawX + StuckOffsetX, tileDrawY + StuckOffsetY);
			}
			else
			{
				drawPos = Position;
			}
			
			if (IsBullet)
			{
				Raylib.DrawCircle((int)drawPos.X, (int)drawPos.Y, 4f, new Color(40, 40, 40, 255));
				Raylib.DrawCircle((int)drawPos.X, (int)drawPos.Y, 2f, Color.White);
				return;
			}
			
			if (Program._arrowTexture.Id != 0)
			{
				float texW = Program._arrowTexture.Width;
				float texH = Program._arrowTexture.Height;
				
				//  AUGMENTER LA TAILLE (de 0.8 à 1.4 ou plus)
				float scale = 1.4f;  // Au lieu de 0.8f
				float drawW = texW * scale;
				float drawH = texH * scale;
				
				Rectangle srcRect = new Rectangle(0, 0, texW, texH);
				Rectangle destRect = new Rectangle(drawPos.X - drawW/2, drawPos.Y - drawH/2, drawW, drawH);
				Vector2 origin = new Vector2(drawW/2, drawH/2);
				Raylib.DrawTexturePro(Program._arrowTexture, srcRect, destRect, origin, angle, Color.White);
			}
			else
			{
				// Fallback : dessiner un projectile plus gros
				Raylib.DrawRectangle((int)drawPos.X - 6, (int)drawPos.Y - 2, 12, 4, Color.Gold);
				Raylib.DrawCircle((int)drawPos.X, (int)drawPos.Y, 4, Color.White);
			}
		}

		public void Update(float dt)
		{
			if (!IsAlive) return;
			
			if (IsStuck)
			{
				Lifetime -= dt;
				if (Lifetime <= 0) IsAlive = false;
				return;
			}

			// Sauvegarder l'ancienne position
			Vector2 oldPos = Position;
			
			// Calculer la nouvelle position
			Vector2 newPos = Position + Velocity * dt;
			
			//  PRIORITÉ 1 : Vérifier d'abord les collisions (entités pour une flèche du joueur,
			// joueur uniquement pour une flèche hostile tirée par un monstre)
			bool hitSomething = IsHostile ? CheckPlayerHitAlongPath(oldPos, newPos) : CheckEntityHitAlongPath(oldPos, newPos);
			if (hitSomething)
			{
				if (!IsBullet) SoundEffects.PlayArrowImpact();
				IsAlive = false;  // La flèche disparaît immédiatement
				return;
			}
			
			//  PRIORITÉ 2 : Vérifier les collisions avec les tuiles
			bool hit = CheckCollisionAlongPath(oldPos, newPos, out Vector2 hitPos, out int hitTileX, out int hitTileY);
			
			if (hit)
			{
				if (!IsBullet) SoundEffects.PlayArrowImpact();
				// La flèche s'enfonce dans la tuile
				StickToTile(hitTileX, hitTileY, hitPos);
			}
			else
			{
				// Pas de collision, déplacer normalement
				Position = newPos;
			}
			
			Lifetime -= dt;
			if (Lifetime <= 0) IsAlive = false;
		}
		
		/// <summary>
		/// Vérifie si la flèche touche une entité le long du chemin
		/// </summary>
		private bool CheckEntityHitAlongPath(Vector2 from, Vector2 to)
		{
			int ts = Program.TileSize;
			float dist = Vector2.Distance(from, to);
			if (dist < 0.01f) return false;
			
			Vector2 dir = (to - from) / dist;
			float step = ts * 0.15f;  // Pas plus petit pour une meilleure précision
			int steps = (int)(dist / step) + 2;
			
			for (int i = 1; i <= steps; i++)
			{
				float t = Math.Min(1f, i * step / dist);
				Vector2 checkPos = from + dir * (dist * t);
				
				foreach (var entity in Program.GetEntities())
				{
					if (!entity.IsAlive) continue;
					if (entity.Species == "human") continue;
					if (entity.IsTamed) continue;
					if (entity.IsPlayer) continue;
					
					Rectangle entityHitbox = World.GetEntityDamageHitbox(entity, ts);
					if (Raylib.CheckCollisionPointRec(checkPos, entityHitbox))
					{
						// Infliger les dégâts
						int damageAmount = (int)Damage;
						entity.CurrentHP -= damageAmount;

						Vector2 knockbackDir = entity.WorldPos - checkPos;
						if (knockbackDir.Length() > 0.01f)
							knockbackDir = Vector2.Normalize(knockbackDir);
						else
							knockbackDir = new Vector2(1, 0);

						entity.OnHit(checkPos);
						entity.ApplyKnockback(knockbackDir, 80f);

						// Afficher les dégâts flottants
						Vector2 damagePos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y - 20);
						Program.AddFloatingDamage(damagePos, damageAmount, damageAmount >= 10, isEnemy: true);

						// Ajouter une notification si l'entité meurt
						if (!entity.IsAlive)
						{
							foreach (var (id, qty, meta) in GameData.GetAnimalDrops(entity.Species))
							{
								Program.GiveItemToPlayer(id, qty, entity.WorldPos, null, meta);
							}
						}

						return true;  // Flèche a touché une entité
					}
				}

				if (NetworkManager.IsClient)
				{
					foreach (var remoteEntity in NetworkManager.GetRemoteEntities())
					{
						if (remoteEntity.HP <= 0 || string.Equals(remoteEntity.Species, "human", StringComparison.OrdinalIgnoreCase)
							|| (remoteEntity.IsTamed && !remoteEntity.IsHostilePet) || remoteEntity.IsCarried)
							continue;

						Rectangle hitbox = World.GetEntityDamageHitbox(
							new Vector2(remoteEntity.PosX, remoteEntity.PosY), remoteEntity.Species, ts, remoteEntity.Scale);
						if (!Raylib.CheckCollisionPointRec(checkPos, hitbox) || !Guid.TryParse(remoteEntity.NetId, out Guid netId))
							continue;

						NetworkManager.RequestEntityHit(netId, (int)Damage);
						return true;
					}
				}

				if (NetworkManager.IsOnline && Program.LocalPvpEnabled)
				{
					foreach (Program.LocalPlayer remotePlayer in NetworkManager.GetRemotePlayersAsLocalPlayers())
					{
						if (!remotePlayer.Connected || !remotePlayer.PvpEnabled)
							continue;

						Rectangle hitbox = World.GetEntityDamageHitbox(
							remotePlayer.Position, remotePlayer.SpeciesOverride ?? "human", ts);
						if (!Raylib.CheckCollisionPointRec(checkPos, hitbox))
							continue;

						int targetConnectionId = remotePlayer.Id - 1000;
						if (NetworkManager.IsHost)
							Program.DamagePlayerTarget((int)Damage, targetConnectionId);
						else
							NetworkManager.SendPvpHitRequest(targetConnectionId, (int)Damage);
						return true;
					}
				}
			}
			
			return false;
		}
		
		/// <summary>
		/// Vérifie si une flèche hostile (tirée par un monstre) touche le joueur le long du chemin.
		/// </summary>
		private bool CheckPlayerHitAlongPath(Vector2 from, Vector2 to)
		{
			float dist = Vector2.Distance(from, to);
			if (dist < 0.01f) return false;
			if (TargetEntity != null && !TargetEntity.IsAlive) return false;
			Program.LocalPlayer? remoteTarget = TargetConnectionId >= 0
				? NetworkManager.GetRemotePlayersAsLocalPlayers().FirstOrDefault(player =>
					player.Connected && player.Id == 1000 + TargetConnectionId)
				: null;
			if (TargetConnectionId >= 0 && remoteTarget == null) return false;

			Vector2 dir = (to - from) / dist;
			float step = Program.TileSize * 0.15f;
			int steps = (int)(dist / step) + 2;

			Vector2 playerPos = Program.GetPlayerPosition();
			const float hitRadius = 22f;
			Rectangle remoteHitbox = remoteTarget != null
				? World.GetEntityDamageHitbox(remoteTarget.Position, remoteTarget.SpeciesOverride ?? "human", Program.TileSize)
				: default;
			Rectangle targetHitbox = TargetEntity != null
				? World.GetEntityDamageHitbox(TargetEntity, Program.TileSize)
				: default;

			for (int i = 1; i <= steps; i++)
			{
				float t = Math.Min(1f, i * step / dist);
				Vector2 checkPos = from + dir * (dist * t);

				bool targetHit = TargetEntity != null
					? Raylib.CheckCollisionPointRec(checkPos, targetHitbox)
					: remoteTarget != null
						? Raylib.CheckCollisionPointRec(checkPos, remoteHitbox)
						: Vector2.DistanceSquared(checkPos, playerPos) <= hitRadius * hitRadius;
				if (targetHit)
				{
					int damageAmount = (int)Damage;
					if (TargetEntity != null)
					{
						Entity target = TargetEntity;
						damageAmount = target.Equipment?.GetDamageAfterArmor(damageAmount) ?? damageAmount;
						target.CurrentHP = Math.Max(0, target.CurrentHP - damageAmount);
						target.OnHit(Position, SourceEntity, attackerIsPlayer: SourceEntity == null);
						Vector2 knockbackDirection = target.WorldPos - Position;
						if (knockbackDirection.LengthSquared() > 0.01f)
							target.ApplyKnockback(Vector2.Normalize(knockbackDirection), 35f);
						Program.AddFloatingDamage(new Vector2(target.WorldPos.X, target.WorldPos.Y - 20f), damageAmount, damageAmount >= 10,
							isEnemy: SourceEntity == null);
						if (!target.IsAlive)
						{
							foreach (var (id, quantity, metadata) in GameData.GetAnimalDrops(target.Species))
								Program.GiveItemToPlayer(id, quantity, target.WorldPos, null, metadata);
						}
					}
					else
					{
						if (remoteTarget != null)
							Program.DamagePlayerTarget(damageAmount, TargetConnectionId,
								SourceEntity?.GetDisplayName() ?? "une flèche",
								Program.InferDeathCauseFromSpecies(SourceEntity?.Species ?? ""));
						else
							Program.DamagePlayer(damageAmount);
					}
					return true;
				}
			}

			return false;
		}

		private bool CheckCollisionAlongPath(Vector2 from, Vector2 to, out Vector2 hitPos, out int hitTileX, out int hitTileY)
		{
			hitPos = to;
			hitTileX = 0;
			hitTileY = 0;
			
			int ts = Program.TileSize;
			float dist = Vector2.Distance(from, to);
			if (dist < 0.01f) return false;
			
			Vector2 dir = (to - from) / dist;
			float step = ts * 0.25f;
			int steps = (int)(dist / step) + 2;
			
			for (int i = 1; i <= steps; i++)
			{
				float t = Math.Min(1f, i * step / dist);
				Vector2 checkPos = from + dir * (dist * t);
				
				int tx = (int)(checkPos.X / ts);
				int ty = (int)(checkPos.Y / ts);
				
				int objectId = World.GetObjectIdAt(tx, ty);
				
				// Collision avec un mur (non walkable) ou un objet placé
				if (objectId != 0)
				{
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData != null && !tileData.Walkable)
					{
						hitPos = checkPos;
						hitTileX = tx;
						hitTileY = ty;
						return true;
					}
				}
				
				// Vérifier aussi le sol (les flèches ne traversent pas le sol)
				int groundId = World.GetGroundTileIdAt(tx, ty);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile != null && !groundTile.Walkable)
				{
					hitPos = checkPos;
					hitTileX = tx;
					hitTileY = ty;
					return true;
				}
			}
			
			return false;
		}

		public void StickToTile(int tileX, int tileY, Vector2 hitPos)
		{
			IsStuck = true;
			StuckTileX = tileX;
			StuckTileY = tileY;
			
			int ts = Program.TileSize;
			int height = World.GetHeightAt(tileX, tileY);
			float yOffset = -height * ts / 4;
			float tileDrawY = tileY * ts + yOffset;
			
			// Calculer l'offset relatif à la tuile
			StuckOffsetX = hitPos.X - (tileX * ts);
			StuckOffsetY = hitPos.Y - tileDrawY;
			
			Velocity = Vector2.Zero;
			Lifetime = 10f; // La flèche reste plantée 10 secondes
		}
	}
	
	public class Boat
	{
		public Vector2 Position;          // centre de la tuile (0,0)
		public Vector2 PreviousPosition;  // position à la frame précédente (pour le déplacement)
		public float Angle;               // orientation (non utilisée pour l'inclinaison des tuiles)
		public Vector2 Velocity;
		public float RowingCooldown;
		public Guid Id;
		public int GroundTileId = 500;    // ID du sol du bateau

		public float SailAngle { get; set; } = 0f;
		public bool IsSailAdjusted { get; set; } = false;

		// Grille des tuiles du bateau
		public Dictionary<(int x, int y), BoatTile> Tiles = new();

		// Objets posés, conteneurs et portes‑armures (stockés par coordonnées relatives)
		public Dictionary<(int x, int y), int> PlacedObjects { get; set; } = new();
		public Dictionary<(int x, int y), ContainerInventoryData> Containers { get; set; } = new();
		public Dictionary<(int x, int y), ArmorStandData> ArmorStands { get; set; } = new();

		// Méthodes d'accès aux objets posés
		public int GetObjectIdAt(int relX, int relY)
			=> PlacedObjects.GetValueOrDefault((relX, relY), 0);

		public void SetObjectAt(int relX, int relY, int objectId)
		{
			if (objectId == 0)
				PlacedObjects.Remove((relX, relY));
			else
				PlacedObjects[(relX, relY)] = objectId;
		}
		
		public Vector2 CalculateTotalThrust()
		{
			Vector2 totalForce = Vector2.Zero;

			// 1. Force des voiles (continue) — basé sur le flag IsSail du registre
			// (et non plus sur un ID codé en dur, qui divergeait de GetSailCount)
			//
			// Réglage de la force : UpdateBoats applique un frottement exponentiel
			// équivalent à un taux de décroissance k ≈ 1.21 / seconde (voir
			// World.UpdateBoats, waterDragPerSecond). À l'équilibre, la vitesse
			// maximale atteinte est v = force / k. L'ancienne valeur (25) donnait
			// donc seulement ~20 px/s (0,5 tuile/s) avec une seule voile : le bateau
			// semblait ne quasiment pas avancer. 110 vise ~90 px/s (~2,3 tuiles/s)
			// avec une seule voile bien orientée, un rythme plus proche d'un vrai
			// petit voilier ; chaque voile supplémentaire s'ajoute ensuite normalement.
			const float sailForcePerSail = 110f;
			foreach (var (relX, relY) in Tiles.Keys)
			{
				int objId = GetObjectIdAt(relX, relY);
				if (objId == 0) continue;
				var tileData = WorldTileRegistry.GetTile(objId);
				if (tileData != null && tileData.IsSail)
				{
					float rad = SailAngle * MathF.PI / 180f;
					Vector2 sailForce = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
					totalForce += sailForce * sailForcePerSail;
				}
			}
			
			// 2. Appliquer le frottement de l'eau (toujours)
			// (sera appliqué dans UpdateBoats)
			
			return totalForce;
		}

		public int GetSailCount()
		{
			int count = 0;
			foreach (var (relX, relY) in Tiles.Keys)
			{
				int objId = GetObjectIdAt(relX, relY);
				if (objId == 0) continue;
				var tileData = WorldTileRegistry.GetTile(objId);
				if (tileData != null && tileData.IsSail)
					count++;
			}
			return count;
		}

		// Vérifie si un gouvernail est présent sur le bateau. Calculé à la demande
		// plutôt que stocké dans un champ, pour ne jamais désynchroniser de l'état réel
		// des tuiles (contrairement à l'ancien champ HasWheel, jamais mis à jour nulle part).
		public const int WheelObjectId = 109;
		public bool ComputeHasWheel()
		{
			foreach (var (relX, relY) in Tiles.Keys)
				if (GetObjectIdAt(relX, relY) == WheelObjectId)
					return true;
			return false;
		}

		// Constructeur : une tuile de départ
		public Boat(Vector2 startWorldPos)
		{
			Position = startWorldPos;
			Id = Guid.NewGuid();
			Tiles[(0, 0)] = new BoatTile { GroundId = GroundTileId };
		}

		// Ajoute une tuile en coordonnées relatives (vérification faite en amont)
		public bool AddTile(int relX, int relY)
		{
			if (Tiles.ContainsKey((relX, relY)))
				return false;
			Tiles[(relX, relY)] = new BoatTile { GroundId = GroundTileId };
			return true;
		}

		// Récupère la position monde du centre d’une tuile relative
		public Vector2 GetTileWorldPos(int relX, int relY)
		{
			return Position + new Vector2(relX * Program.TileSize, relY * Program.TileSize);
		}

		public bool ContainsPosition(Vector2 worldPos, int tileSize)
		{
			foreach (var (relX, relY) in Tiles.Keys)
			{
				Vector2 tileCenter = GetTileWorldPos(relX, relY);
				
				// Calculer la position visuelle réelle de la tuile (avec hauteur)
				int worldTileX = (int)(tileCenter.X / tileSize);
				int worldTileY = (int)(tileCenter.Y / tileSize);
				int height = World.GetHeightAt(worldTileX, worldTileY);
				float yOffset = -height * tileSize / 4;
				
				// Position visuelle du centre de la tuile
				float visualCenterX = tileCenter.X;
				float visualCenterY = tileCenter.Y + yOffset;
				
				// Récupérer les dimensions réelles de l'objet placé sur cette tuile (si présent)
				int objId = GetObjectIdAt(relX, relY);
				int w = tileSize, h = tileSize;
				int offX = 0, offY = 0;
				
				if (objId != 0)
				{
					var tileData = WorldTileRegistry.GetTile(objId);
					if (tileData?.Size != null)
					{
						w = tileSize * tileData.Size.Width;
						h = tileSize * tileData.Size.Height;
						if (tileData.DrawOffset != null)
						{
							offX = tileData.DrawOffset.X;
							offY = tileData.DrawOffset.Y;
						}
						// Ajuster la position Y pour que la hitbox soit au bon endroit
						visualCenterY = visualCenterY - h + tileSize;
					}
				}
				
				// Rectangle de la tuile (position en haut à gauche)
				float tileLeft = visualCenterX - w / 2f + offX;
				float tileTop = visualCenterY - h / 2f + offY;
				
				Rectangle tileRect = new Rectangle(tileLeft, tileTop, w, h);
				
				if (Raylib.CheckCollisionPointRec(worldPos, tileRect))
					return true;
			}
			return false;
		}

		// Retourne les coordonnées relatives de la tuile contenant worldPos, ou null
		public (int x, int y)? WorldToRelative(Vector2 worldPos, int tileSize)
		{
			Vector2 local = worldPos - Position;
			int rx = (int)Math.Round(local.X / tileSize);
			int ry = (int)Math.Round(local.Y / tileSize);
			if (Tiles.ContainsKey((rx, ry)))
				return (rx, ry);
			return null;
		}

		// Propriété de compatibilité (utilisée par l'ancien code) – à terme à supprimer
		public List<Vector2> TileOffsets 
		{ 
			get
			{
				var list = new List<Vector2>();
				foreach (var (x, y) in Tiles.Keys)
					list.Add(new Vector2(x * Program.TileSize, y * Program.TileSize));
				return list;
			} 
		}
	}

	public class BoatTile
	{
		public int GroundId = 500;
		// pourra contenir d’autres données (objets, etc.)
	}
}