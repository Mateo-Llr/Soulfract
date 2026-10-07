// Program.Inventory.cs
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

		private static bool ApplyFoodSpoilTick(Item item, float dt, float spoilLifetime)
		{
			if (item == null) return false;
			if (spoilLifetime <= 0f) return false;
			if (string.Equals(item.GetMeta("spoiled", ""), "1", StringComparison.OrdinalIgnoreCase)) return false;
			// Utiliser la métadonnée "spoil" si présente, sinon initialiser via max = 100
			string spoilMeta = item.GetMeta("spoil", "");
			float cur = -1f, max = -1f;
			if (!string.IsNullOrWhiteSpace(spoilMeta))
			{
				var seps = new char[] {'/','|',':','='};
				var parts = spoilMeta.Split(seps, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 1) float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cur);
				if (parts.Length >= 2) float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out max);
			}
			if (cur < 0f)
			{
				max = 100f;
				cur = max;
			}
			if (max <= 0f) max = 100f;
			// Dégradation linéaire : tout pourrit en spoilLifetime secondes
			float decayPerSec = max / spoilLifetime;
			cur -= decayPerSec * dt;
			if (cur <= 0f)
			{
				item.SetMeta("spoil", $"0/{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
				// Marquer comme pourri
				item.SetMeta("spoiled", "1");
				AddNotification(new Notification($" {item.Name} a pourri.", new Color(180, 120, 80, 255), 1.5f));
				return true;
			}
			else
			{
				item.SetMeta("spoil", $"{cur.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
			}
			return false;
		}

		private static void ConvertSpoiledSlotToMoldyScrap(InventorySlot slot)
		{
			if (slot.IsEmpty || slot.Item == null) return;
			if (!GameData.ItemDatabase.TryGetValue(GetItemId("moldy_scrap"), out var moldyData)) return;

			int count = slot.Count;
			var moldyItem = new Item(moldyData.Name, count, moldyData.Color, moldyData.Icon);
			slot.Clear();
			AddItemToInventory(moldyItem, count, silent: true);
		}

		private static void ConvertSpoiledMainHandToMoldyScrap()
		{
			if (equipment.MainHand == null) return;
			if (!GameData.ItemDatabase.TryGetValue(GetItemId("moldy_scrap"), out var moldyData)) return;

			var moldyItem = new Item(moldyData.Name, equipment.MainHand.Count, moldyData.Color, moldyData.Icon);
			moldyItem.CustomColors = new List<Color?>(equipment.MainHand.CustomColors);
			equipment.MainHand = moldyItem;
		}

		// 0=Général, 1=Contrôles, 2=Interface
		private static Texture2D _uiBarSlotTex;

		private static Texture2D _uiBarSlotOpenTex;

		internal static Texture2D RadialSlotTexture;

		internal static Texture2D RadialSlotOnTexture;

		internal static Texture2D RadialSelectorTexture;

		internal static Texture2D RadialSelectorOnTexture;

		// Avance de temps progressive pour éviter les traitements massifs en une frame
		static float _timeSkipRemaining = 0f;

		private static Sound _itemPickupSound = default;

		private static bool _itemPickupSoundLoaded = false;

		public static List<GroundItem> GroundItems = new();

		public static Equipment equipment = new();

		//  MULTIJOUEUR : cache des équipements visuels (tenue) des joueurs distants,
        // indexé par id de connexion. Ne recharge les textures (coûteux) que lorsque
        // les ids d'équipement reçus du réseau changent réellement.
        static Dictionary<int, (int head, int body, int legs, Equipment eq)> _networkEquipmentCache = new();

		// Même principe pour les entités/PNJ distants, indexés par NetId.
        static Dictionary<string, (string signature, Equipment eq)> _networkEntityEquipmentCache = new();

		// Valeur de base (persistée) - hors bonus de runes

        /// <summary>PV max effectifs = base + bonus cumulés des runes serties sur l'armure équipée.</summary>
        public static int GetEffectivePlayerMaxHP() => playerMaxHP + equipment.TotalRuneMaxHealthBonus;

		//  Chaque modèle est associé à une ou plusieurs causes (pour permettre de piocher
        // au hasard parmi tous les messages pertinents quand plusieurs conviennent, plutôt
        // que d'avoir un mapping 1-pour-1 rigide). "{0}" = victime, "{1}" = agresseur/source.
        private static readonly (DeathCause[] causes, string template)[] _deathMessageTemplates = new (DeathCause[], string)[]
        {
            (new[]{ DeathCause.Fire },                         "{0} a été réduit en cendres par les flammes dévorantes de {1}."),
            (new[]{ DeathCause.Cold, DeathCause.Ice },          "Le froid mordant de {1} a transformé {0} en statue de glace, brisée en mille morceaux."),
            (new[]{ DeathCause.Lightning, DeathCause.Energy },  "{0} s'est fait foudroyer par {1}, son corps arc-bouté dans une dernière convulsion."),
            (new[]{ DeathCause.Shadow, DeathCause.Curse },      "Les ténèbres de {1} ont englouti {0}, qui ne laisse derrière lui qu'un silence absolu."),
            (new[]{ DeathCause.Physical },                      "{0} a été déchiqueté par la violence pure de {1}, sans même un cri."),
            (new[]{ DeathCause.Poison },                        "Le venin de {1} a coulé dans les veines de {0}, qui s'est éteint en tremblant."),
            (new[]{ DeathCause.Crush, DeathCause.Physical },    "{0} a été écrasé par la masse implacable de {1}, réduit en bouillie."),
            (new[]{ DeathCause.Holy, DeathCause.Celestial },    "La lumière sacrée de {1} a consumé {0} comme un papillon de nuit."),
            (new[]{ DeathCause.Ice, DeathCause.Cold },          "{0} a été empalé par les pics de glace de {1}, suspendu un instant avant de tomber."),
            (new[]{ DeathCause.Earth },                         "La terre elle-même, sous l'ordre de {1}, s'est ouverte pour avaler {0}."),
            (new[]{ DeathCause.Water },                         "{0} a été noyé dans les eaux vengeresses de {1}, ses poumons remplis d'ombre."),
            (new[]{ DeathCause.ToxicBreath, DeathCause.Poison },"Le souffle empoisonné de {1} a fait fondre la chair de {0} sur place."),
            (new[]{ DeathCause.Energy, DeathCause.Lightning },  "{0} a été pulvérisé par une décharge de {1}, ne laissant qu'un cratère fumant."),
            (new[]{ DeathCause.Wind },                          "Les lames de vent de {1} ont lacéré {0} jusqu'à ce qu'il ne soit plus qu'un souvenir."),
            (new[]{ DeathCause.Gaze, DeathCause.Curse },        "{0} a été transpercé par le regard mortel de {1}, tombé sans comprendre."),
            (new[]{ DeathCause.Curse },                         "La malédiction de {1} a rongé {0} de l'intérieur, lentement, inexorablement."),
            (new[]{ DeathCause.Celestial, DeathCause.Crush },   "{0} a été broyé sous le poids céleste de {1}, comme une coquille vide."),
            (new[]{ DeathCause.SpectralFire, DeathCause.Shadow},"Les flammes spectrales de {1} ont dévoré l'âme de {0} avant son corps."),
            (new[]{ DeathCause.Disintegration, DeathCause.Energy}, "{0} a été désintégré par l'énergie brute de {1}, atome par atome."),
            (new[]{ DeathCause.Cold, DeathCause.Ice },          "{0} n'a pas survécu à l'étreinte glaciale de {1} ; il repose maintenant en poussière."),
        };

		// ==================== BOSS DE L'OEIL (Socle de gemme-oeil, tile 118) ====================
		private static bool _eyeBossActive = false;

		static List<Item> _inventoryBuffer = new();

		static HeldItem _heldItem = new HeldItem();

		public static void ToggleLocalPvp()
		{
			LocalPvpEnabled = !LocalPvpEnabled;
			AddNotification(new Notification(
				LocalPvpEnabled ? "PVP activé : vous n'êtes plus localisable par les autres." : "PVP désactivé.",
				LocalPvpEnabled ? new Color(255, 100, 100, 255) : new Color(150, 220, 255, 255), 2.5f));
		}

		static int _hoveredMenuItem = -1;

		public static bool TryGetHeldRangedAmmoInfo(Item? heldItem, out int currentAmmo, out int maxAmmo)
		{
			currentAmmo = 0;
			maxAmmo = 0;
			if (heldItem == null) return false;

			int heldId = GetItemId(heldItem.Name);
			if (!GameData.ItemDatabase.TryGetValue(heldId, out var weaponData) || !weaponData.IsRangedWeapon)
				return false;

			maxAmmo = Math.Max(1, weaponData.MaxAmmoPerCharge);
			if (!_rangedWeaponReloadStates.TryGetValue(heldId, out var state))
			{
				state = new RangedWeaponReloadState { CurrentAmmo = maxAmmo, ReloadAmmoId = 0, ReloadAmount = 0 };
				_rangedWeaponReloadStates[heldId] = state;
			}
			currentAmmo = state.CurrentAmmo;
			return true;
		}

		// (Ancien flag global IsAttractingItems retiré : l'attraction est désormais
		// évaluée item par item via Program.HasSpaceForItem, voir UpdateGameplay.)
		
		const float MAX_LEFT_CLICK_CHARGE = 1.5f;

		public class LocalPlayer
		{
			public int Id;
			public bool Connected;
			public Vector2 Position;
			public float Facing = 1f;
			public string AnimState = "idle";
			public int AnimFrame = 0;
			public float AnimProg = 0f;
			public float AttackSwingProgress = 0f;
			public string DeviceName = "keyboard";
			public int DeviceIndex = -1;
			public int HotbarSlot = 0;

			//  MULTIJOUEUR EN LIGNE : champs utilisés uniquement pour représenter
			// un joueur distant (DeviceName == "network"). Ils restent à null pour
			// les joueurs locaux (clavier/manette), qui continuent d'utiliser
			// l'apparence globale du joueur comme avant.
			public bool IsNetworkPlayer = false;
			public string DisplayName = "";
			public string? SpeciesOverride = null;
			public Color? MorphTintOverride = null;
			public Dictionary<string, int>? MorphFeatureVariantsOverride = null;
			public Dictionary<string, Color>? MorphFeatureColorsOverride = null;
			public Color? SkinColorOverride = null;
			public Color? HairColorOverride = null;
			public int? HairStyleOverride = null;
			public int? BeardStyleOverride = null;
			public int? EyeStyleOverride = null;
			public Color? EyeColorOverride = null;
				public bool Underground = false;
			//  Santé et équipement visibles sur l'avatar distant
			public int HP = 20;
			public int MaxHP = 20;
			public int HeldItemId = 0;   // id ItemDatabase, 0 = rien
			public int EqHead = 0;
			public int EqBody = 0;
			public int EqLegs = 0;
			//  Équipement complet (toutes zones + teintes) pour l'affichage des joueurs distants
			public EquipData? Equip = null;
			//  Rotation de tête (visée) et cible du regard (position monde du curseur de CE
			// joueur), propres à chaque joueur distant - voir PlayerTransform.HeadAngle/LookX/LookY.
			public float HeadAngle = 0f;
			public Vector2 LookWorldPos = Vector2.Zero;
			//  PVP : si activé, on ne peut plus localiser ce joueur (pseudo + barre de
			// vie masqués au-dessus de sa tête pour les autres joueurs).
			public bool PvpEnabled = false;
		}

		//  OPTIM : cache des 10 premiers slots de la hotbar. Ce Take(10).ToList() était
		// recopié jusqu'à 7 fois par frame (dessin du hotbar, ghost de placement, etc.) ;
		// on ne le recalcule maintenant qu'une fois par frame.
		private static readonly List<InventorySlot> _hotbarSlotsCache = new();

		private static int _hotbarSlotsCacheFrame = -1;

		internal static List<InventorySlot> GetHotbarSlotsCached()
		{
			if (_hotbarSlotsCacheFrame != _currentFrame)
			{
				_hotbarSlotsCache.Clear();
				int count = 0;
				foreach (var slot in InventoryRenderer.InventorySlots)
				{
					if (count >= 10) break;
					_hotbarSlotsCache.Add(slot);
					count++;
				}
				_hotbarSlotsCacheFrame = _currentFrame;
			}
			return _hotbarSlotsCache;
		}

		public static int hotbarSlot = 0;

		public static void ToggleGodMode()
		{
			_godmode = !_godmode;
			if (_godmode)
			{
				playerHP = GetEffectivePlayerMaxHP();
				playerHunger = playerMaxHunger;
				playerThirst = playerMaxThirst;
				_starvationDamageAccumulator = 0f;
				_hungerWarningPlayed = false;
			}
			AddNotification(new Notification($" Godmode {(_godmode ? "activé" : "désactivé")}", _godmode ? Color.Gold : Color.Gray, 2f));
		}

		private static int GetMineralItemId(int overlayId)
		{
			return overlayId switch
			{
				1001 => GameData.GetItemId("iron_ore"),
				1002 => GameData.GetItemId("copper_ore"),
				1003 => GameData.GetItemId("coal"),
				1004 => GameData.GetItemId("garnet"),
				1005 => GameData.GetItemId("amber"),
				1006 => GameData.GetItemId("topaze"),
				1007 => GameData.GetItemId("onyx"),
				1008 => GameData.GetItemId("opale"),
				1009 => GameData.GetItemId("obsidian"),
				_ => 0
			};
		}

		private static void DrawEmoteWheel()
		{
			Vector2 center = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);
			float radius = 140f;
			int optionCount = _emoteWheelOptions.Length;
			float angleStep = 360f / optionCount;

			Raylib.DrawCircleGradient((int)center.X, (int)center.Y, (int)(radius * 1.35f), new Color(0, 0, 0, 120), new Color(0, 0, 0, 0));
			Raylib.DrawCircle((int)center.X, (int)center.Y, radius + 16f, new Color(25, 25, 35, 210));
			Raylib.DrawCircleLines((int)center.X, (int)center.Y, radius + 16f, new Color(180, 180, 200, 120));

			for (int i = 0; i < optionCount; i++)
			{
				float startAngle = i * angleStep - 90f;
				float midAngle = startAngle + angleStep / 2f;
				float rad = midAngle * MathF.PI / 180f;
				Vector2 slotPos = center + new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * (radius * 0.65f);
				bool hovered = i == _emoteWheelHoverIndex;
				Color bg = hovered ? new Color(255, 255, 255, 35) : new Color(255, 255, 255, 15);
				Color border = hovered ? new Color(255, 240, 180, 200) : new Color(210, 210, 210, 120);

				Raylib.DrawCircle((int)slotPos.X, (int)slotPos.Y, 40, _emoteWheelColors[i]);
				Raylib.DrawCircleLines((int)slotPos.X, (int)slotPos.Y, 40, border);

				string label = _emoteWheelLabels[i];
				int fontSize = 16;
				int labelWidth = FontManager.MeasureText(label, fontSize);
				FontManager.DrawText(label, (int)(slotPos.X - labelWidth / 2f), (int)(slotPos.Y - 10), fontSize, Color.White);
			}

			Raylib.DrawCircle((int)center.X, (int)center.Y, 34, new Color(20, 20, 30, 220));
			Raylib.DrawCircleLines((int)center.X, (int)center.Y, 34, new Color(230, 215, 140, 200));

			string title = Localization.Get("emote.select");
			int titleSize = 18;
			int titleWidth = FontManager.MeasureText(title, titleSize);
			FontManager.DrawText(title, (int)(center.X - titleWidth / 2f), (int)(center.Y - 22), titleSize, Color.White);
			string hint = _emoteWheelHoverIndex >= 0 ? _emoteWheelLabels[_emoteWheelHoverIndex] : "Relâchez B pour choisir";
			int hintSize = 14;
			int hintWidth = FontManager.MeasureText(hint, hintSize);
			FontManager.DrawText(hint, (int)(center.X - hintWidth / 2f), (int)(center.Y + 28), hintSize, new Color(230, 230, 230, 200));
		}

		public static int GetItemId(string name)
        {
            return GameData.GetItemId(name);
        }

		/// <summary>Joue le son de récupération d'item (pop.mp3) lorsqu'un objet est ajouté à l'inventaire.</summary>
		private static void PlayItemPickupSound()
		{
			if (!_itemPickupSoundLoaded)
			{
				_itemPickupSoundLoaded = true;
				_itemPickupSound = LoadUiSound("pop.mp3");
			}
			if (!Raylib.IsSoundReady(_itemPickupSound)) return;
			Raylib.SetSoundVolume(_itemPickupSound, 0.45f);
			Raylib.SetSoundPitch(_itemPickupSound, GetRandomSoundPitch());
			Raylib.PlaySound(_itemPickupSound);
		}

		//  Système unifié de contenant de liquide : gourde/arrosoir (id 230, toujours de
		// l'eau, 6 charges) ET seau (id 63, eau OU lait, 10 charges) sont tous deux traités
		// via ces deux fonctions, pour n'avoir qu'un seul code d'arrosage et qu'un seul code
		// de consommation/boisson, quel que soit l'objet réellement tenu en main.
		public static bool TryGetLiquidContainer(Item? heldItem, out string liquidType, out int charges, out int maxCharges)
		{
			liquidType = "empty";
			charges = 0;
			maxCharges = 0;
			if (heldItem == null) return false;

			if (ItemRenderer.IsPotionMetadata(heldItem.Metadata))
			{
				var potionData = new ItemData { TextureName = "glassbottle_small" };
				if (!ItemRenderer.TryGetBottleContentInfo(heldItem, potionData, out liquidType, out _))
					return false;

				maxCharges = GetWaterCapacityForItem(heldItem);
				if (maxCharges <= 0)
					maxCharges = 1;
				charges = Math.Clamp(ItemRenderer.GetPotionDoses(heldItem), 0, maxCharges);
				return true;
			}

			int id = GetItemId(heldItem.Name);
			if (id == 63) // Seau
			{
				ParseBucketContent(heldItem.Metadata, out liquidType, out charges);
				maxCharges = BUCKET_MAX_CHARGES;
				return true;
			}

			int waterCapacity = GetWaterCapacityForItem(heldItem);
			if (waterCapacity > 0)
			{
				if (GameData.TryGetItemByName(heldItem.Name, out var itemData)
					&& itemData.TextureName.Contains("glassbottle", StringComparison.OrdinalIgnoreCase)
					&& ItemRenderer.IsPotionMetadata(heldItem.Metadata))
					return false;

				maxCharges = waterCapacity;
				charges = GetWaterAmountFromMetadata(heldItem);
				charges = Math.Clamp(charges, 0, maxCharges);
				liquidType = charges > 0 ? "water" : "empty";
				return true;
			}

			return false;
		}

		public static bool IsLiquidContainer(Item? item)
		{
			if (item == null) return false;

			int id = GetItemId(item.Name);
			if (id == 63 || GetWaterCapacityForItem(item) > 0)
				return true;

			return GameData.TryGetItemByName(item.Name, out var itemData)
				&& itemData.TextureName.Contains("glassbottle", StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsEmptyLiquidContainer(Item? item)
		{
			if (!IsLiquidContainer(item)) return false;

			int id = GetItemId(item!.Name);
			if (id == 63)
			{
				if (ItemRenderer.IsPotionMetadata(item.Metadata))
					return false;
				ParseBucketContent(item.Metadata, out _, out int charges);
				return charges <= 0;
			}

			if (GetWaterCapacityForItem(item) > 0)
				return GetWaterAmountFromMetadata(item) <= 0
					&& !ItemRenderer.IsPotionMetadata(item.Metadata);

			return string.IsNullOrWhiteSpace(item.Metadata)
				|| item.Metadata.Equals("empty", StringComparison.OrdinalIgnoreCase);
		}

		public static void SetLiquidContainerCharges(Item heldItem, string liquidType, int newCharges)
		{
			if (ItemRenderer.IsPotionMetadata(heldItem.Metadata))
			{
				if (newCharges <= 0)
					EmptyBottleItem(heldItem);
				else
					ItemRenderer.SetPotionDoses(heldItem, newCharges);
				return;
			}

			int id = GetItemId(heldItem.Name);
			if (id == 63)
			{
				heldItem.Metadata = FormatBucketContent(liquidType, newCharges);
			}
			else
			{
				int capacity = GetWaterCapacityForItem(heldItem);
				int safeAmount = Math.Clamp(newCharges, 0, capacity > 0 ? capacity : 0);
				heldItem.Metadata = safeAmount > 0
					? FormatBucketContent("water", safeAmount)
					: string.Empty;
			}
		}

		public static int GetWaterAmountFromMetadata(Item item)
		{
			if (item == null) return 0;

			if (!string.IsNullOrWhiteSpace(item.Metadata))
			{
				string[] metadataParts = item.Metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				if (metadataParts.Length > 0 && metadataParts[0].Equals("water", StringComparison.OrdinalIgnoreCase))
				{
					string dosePart = metadataParts.FirstOrDefault(part => part.StartsWith("doses=", StringComparison.OrdinalIgnoreCase)) ?? "";
					if (dosePart.Length > 6 && int.TryParse(dosePart[6..], out int parsedDoses))
						return parsedDoses;
				}

				foreach (var part in item.Metadata.Split(';', StringSplitOptions.RemoveEmptyEntries))
				{
					int separatorIndex = part.IndexOf('=');
					if (separatorIndex > 0 && string.Equals(part.Substring(0, separatorIndex).Trim(), "water", StringComparison.OrdinalIgnoreCase))
					{
						if (int.TryParse(part.Substring(separatorIndex + 1).Trim(), out int parsed))
							return parsed;
					}
				}

				if (int.TryParse(item.Metadata.Trim(), out int legacyAmount))
					return legacyAmount;
			}

			return 0;
		}

		public static int GetWaterCapacityForItem(Item item)
		{
			if (item == null) return 0;

			int id = GetItemId(item.Name);
			if (id == 63)
				return BUCKET_MAX_CHARGES;

			if (GameData.TryGetItemByName(item.Name, out var itemData) && itemData.WaterCapacity > 0)
				return itemData.WaterCapacity;

			if (GameData.GetToolType(item.Name) == ToolType.WateringCan)
				return 6;

			return 0;
		}

		public static void EmptyBottleItem(Item heldItem)
		{
			int id = GetItemId(heldItem.Name);
			if (id == 63)
			{
				heldItem.Metadata = FormatBucketContent("empty", 0);
			}
			else
			{
				heldItem.SetMeta("water", null);
				heldItem.Metadata = string.Empty;
			}
		}

		private static void UseSandlandFlute(Item heldInstrument)
		{
			if (heldInstrument == null) return;
			// Jouer le son
			string path = "assets/sounds/flute.wav";
			if (File.Exists(path))
			{
				Sound s = Raylib.LoadSound(path);
				if (Raylib.IsSoundReady(s))
				{
					Raylib.PlaySound(s);
					// Programmer la suppression de l'item après un court délai
					float removeTime = (float)Raylib.GetTime() + 0.6f;
					_pendingFluteRemovals.Add(removeTime);
				}
			}
			// Invoquer Khamsin à une distance max de 20 tuiles (~20*TileSize)
			Vector2 playerPos = GetPlayerPosition();
			int maxTiles = 20;
			float maxDist = maxTiles * TileSize;
			Random rand = new Random();
			for (int attempts = 0; attempts < 64; attempts++)
			{
				float ang = (float)(rand.NextDouble() * Math.PI * 2);
				float dist = (float)(rand.NextDouble() * maxDist);
				Vector2 spawnPos = playerPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist;
				int tileX = (int)(spawnPos.X / TileSize);
				int tileY = (int)(spawnPos.Y / TileSize);
				int groundId = World.GetGroundTileIdAt(tileX, tileY);
				var ground = WorldTileRegistry.GetTile(groundId);
				if (ground == null || !ground.Walkable) continue;
				if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
				// Spawn Khamsin using the existing species definition
				var kh = new Entity(spawnPos, "Khamsin", false);
				kh.IsBoss = true;
				kh.MaxHP = 300;
				kh.CurrentHP = kh.MaxHP;
				kh.Attack = 18;
				kh.Behavior = "hostile";
				kh.Scale = 1.1f;
				GetEntities().Add(kh);
				// Ajouter au chunk
				var chunk = World.GetChunkAt(tileX, tileY);
				if (chunk != null) chunk.Entities.Add(kh);
				AddNotification(new Notification("Khamsin invoqué !", new Color(200, 80, 80, 255), 4f));
				return;
			}
			AddNotification(new Notification("Impossible d'invoquer Khamsin ici.", Color.Red, 2.5f));
		}

		//  Déclenché en frappant le socle avec l'oeil (tile 118) avec le Doigt de Biggie (item 239).
		private static void StartEyeBossFight(int tileX, int tileY)
		{
			if (_eyeBossActive) return;

			_eyeBossActive = true;
			_eyeBossIntroPlaying = true;
			_eyeBossIntroTimer = EYE_BOSS_INTRO_DURATION;
			_eyeBossTile = (tileX, tileY);

			int ts = Program.TileSize;
			// Centre du socle (2x2 tuiles). Le nuage de particules/anneau de boucliers se
			// positionnait une tuile trop bas : le pivot visuel du socle est en fait à tileY*ts,
			// pas tileY*ts+ts.
			_eyeBossCenter = new Vector2(tileX * ts + ts, tileY * ts);

			int totalHp = 0;
			foreach (var wave in _eyeBossWaves)
				foreach (var (species, count) in wave)
					totalHp += GetEyeBossMonsterDamage(species) * count;
			_eyeBossMaxHP = Math.Max(1, totalHp);
			_eyeBossHP = _eyeBossMaxHP;

			_eyeBossWaveIndex = -1;
			_eyeBossWaveEntities.Clear();
			_eyeBossWaitingNextWave = false;

			//  Un bouclier de moins par vague terminée, sauf la toute dernière (l'oeil reste nu).
			_eyeBossShieldTotal = Math.Max(0, _eyeBossWaves.Length - 1);
			_eyeBossShieldsRemaining = _eyeBossShieldTotal;
			_eyeBossShieldRotation = 0f;

			SpawnEyeBossConvergeParticles(_eyeBossCenter);
			ApplyScreenShake(1.5f, 0.3f);
			AddNotification(new Notification(" L'oeil s'éveille...", new Color(190, 100, 230, 255), 2.5f));
		}

		//  Dessine les boucliers restants tournant autour de l'oeil ; remplace l'ancienne barre
		// de vie du boss. Appelé DANS BeginMode2D (coordonnées monde), voir la boucle de rendu.
		private static void DrawEyeBossShields()
		{
			if (!_eyeBossActive || _eyeBossIntroPlaying || _eyeBossShieldsRemaining <= 0) return;

			Texture2D tex = LoadEyeShieldTexture();
			if (tex.Id == 0) return;

			const float size = 40f;
			Vector2 origin = new Vector2(size / 2f, size / 2f);
			for (int i = 0; i < _eyeBossShieldsRemaining; i++)
			{
				Vector2 pos = GetEyeBossShieldPosition(i);
				float angle = (2f * MathF.PI * i / Math.Max(1, _eyeBossShieldTotal)) + _eyeBossShieldRotation;
				float rotationDeg = angle * 180f / MathF.PI + 90f;
				Raylib.DrawTexturePro(tex,
					new Rectangle(0, 0, tex.Width, tex.Height),
					new Rectangle(pos.X, pos.Y, size, size),
					origin,
					rotationDeg,
					Color.White);
			}
		}

		//  À appeler une fois par frame : gère l'intro, l'enchaînement des vagues et la victoire.
		private static void UpdateEyeBossFight(float dt)
		{
			if (!_eyeBossActive) return;

			_eyeBossShieldRotation += dt * EYE_BOSS_SHIELD_ROTATION_SPEED;

			if (_eyeBossIntroPlaying)
			{
				_eyeBossIntroTimer -= dt;
				if (_eyeBossIntroTimer <= 0f)
				{
					_eyeBossIntroPlaying = false;
					SpawnEyeBossExplosionParticles(_eyeBossCenter);
					_eyeBossWaveIndex = 0;
					SpawnEyeBossWave(_eyeBossWaveIndex);
				}
				return;
			}

			//  Ne garder que les monstres de vague encore vivants.
			_eyeBossWaveEntities.RemoveAll(e => !e.IsAlive);

			if (_eyeBossWaveEntities.Count == 0 && !_eyeBossWaitingNextWave)
			{
				if (_eyeBossHP <= 0 || _eyeBossWaveIndex + 1 >= _eyeBossWaves.Length)
				{
					EndEyeBossFight(true);
					return;
				}
				BreakEyeBossShield();
				_eyeBossWaitingNextWave = true;
				_eyeBossNextWaveTimer = EYE_BOSS_WAVE_DELAY;
				AddNotification(new Notification(" La prochaine vague arrive...", new Color(220, 150, 100, 255), 2f));
			}

			if (_eyeBossWaitingNextWave)
			{
				_eyeBossNextWaveTimer -= dt;
				if (_eyeBossNextWaveTimer <= 0f)
				{
					_eyeBossWaitingNextWave = false;
					_eyeBossWaveIndex++;
					SpawnEyeBossWave(_eyeBossWaveIndex);
				}
			}
		}

		//  Termine le combat (actuellement uniquement par victoire : le socle a été vidé de son
		// énergie une fois toutes les vagues repoussées).
		private static void EndEyeBossFight(bool victory)
		{
			_eyeBossActive = false;
			_eyeBossIntroPlaying = false;
			_eyeBossWaitingNextWave = false;
			_eyeBossShieldsRemaining = 0;
			_eyeBossShieldTotal = 0;

			// Les monstres de la vague en cours restent en vie et redeviennent des monstres normaux
			// (ils ne disparaissent pas par magie), on arrête simplement de les suivre.
			_eyeBossWaveEntities.Clear();

			if (victory)
			{
				SpawnEyeBossExplosionParticles(_eyeBossCenter);
				Program.GiveItemToPlayer(242, 1, _eyeBossCenter); // Récompense de victoire du boss de l'oeil
				AddNotification(new Notification(" L'oeil s'est éteint... vous récupérez une Gemme-oeil !", new Color(190, 100, 230, 255), 4f));
				if (_eyeBossTile.x != -1)
					World.AddPlacedObject(_eyeBossTile.x, _eyeBossTile.y, 117); // le socle redevient vide
			}
		}

		static void UpdateMainMenu()
		{
			if (_isSelectingSave || _isMultiplayerMenu || _isInWorldCreation) return;

			if (!_mainMenuActivated)
			{
				if (Raylib.GetKeyPressed() != 0 || Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					RefreshAvailableSaves();
					_mainMenuActivated = true;
					_classicMenuSelectedCardIndex = 0;
					_classicMenuSelectedSaveIndex = -1;
					_worldCarouselIndex = 1;
					_worldCarouselAnimIndex = _worldCarouselIndex;
				}
				return;
			}

			if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				// Retour fluide à l'écran titre : _mainMenuAnim va se relâcher progressivement
				// vers 0 (même transition en douceur que pour l'ouverture), pas de coupure brutale.
				_mainMenuActivated = false;
				PlayButtonClickSound();
				return;
			}

			int cardCount = _availableSaves.Count + 1;
			if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A))
			{
				_classicMenuSelectedCardIndex = (_classicMenuSelectedCardIndex - 1 + cardCount) % cardCount;
				_classicMenuSelectedSaveIndex = _classicMenuSelectedCardIndex - 1;
				_worldCarouselIndex = _classicMenuSelectedCardIndex == 0 ? 1 : _classicMenuSelectedCardIndex + 1;
				PlayButtonClickSound();
			}
			if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D))
			{
				_classicMenuSelectedCardIndex = (_classicMenuSelectedCardIndex + 1) % cardCount;
				_classicMenuSelectedSaveIndex = _classicMenuSelectedCardIndex - 1;
				_worldCarouselIndex = _classicMenuSelectedCardIndex == 0 ? 1 : _classicMenuSelectedCardIndex + 1;
				PlayButtonClickSound();
			}

			if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
			{
				PlayButtonClickSound();
				ConfirmWorldCarouselSelection();
			}
		}

		// Valide l'élément actuellement au centre du carrousel : charge le monde,
		// ou ouvre la saisie de nom si c'est le bouton "Créer un monde".
		static void ConfirmWorldCarouselSelection()
		{
			RefreshAvailableSaves();
			int worldCount = _availableSaves.Count;
			int totalItems = worldCount + 2;
			_worldCarouselIndex = Math.Clamp(_worldCarouselIndex, 0, Math.Max(0, totalItems - 1));
			bool isJoinSlot = _worldCarouselIndex == 0;
			bool isCreateSlot = _worldCarouselIndex == 1;
			if (isJoinSlot)
			{
				PlayButtonClickSound();
				_mpPseudoInput = string.IsNullOrWhiteSpace(LocalPlayerName) ? "Joueur" : LocalPlayerName;
				_mpFocusedField = 0;
				_mpStatusMessage = "";
				_isJoinSetup = true;
				_isMultiplayerMenu = true;
			}
			else if (isCreateSlot)
			{
				_isInWorldCreation = true;
				_isSelectingSave = false;
				_newWorldName = "";
				_newWorldSeed = "";
				_creationError = "";
				// Randomiser le personnage par défaut lors de la création d'un nouveau monde
				RandomizeCharacterCustomization();
				_creationTab = 0;
				SyncSlidersFromColor(_tempHairColor);
				_gameState = GameState.MainMenu; // Rester dans le menu principal, le nouveau menu est un overlay
			}
			else
			{
				var save = _availableSaves[_worldCarouselIndex - 2];
				StartLoadingGame(save.Name);
				_isSelectingSave = false;
			}
		}

		static bool IsAnyTextInputFocused()
		{
			// Multiplayer text fields
			if (_isHostSetup || _isJoinSetup) return true;

			// Per-UI focused inputs
			if (PortalUI.IsTextInputFocused) return true;
			if (BestiaryUI.IsTextInputFocused) return true;
			if (DyeingUI.IsTextInputFocused) return true;
			if (GuildUI.IsTextInputFocused) return true;
			if (ItemMenuUI.IsTextInputFocused) return true;
			if (TamedAnimalUI.IsEditingName) return true;

			return false;
		}

		public static void OnNetworkHostLost()
		{
			string reason = string.IsNullOrEmpty(NetworkManager.LastError) ? "" : $" ({NetworkManager.LastError})";
			AddNotification(new Notification($" Connexion à l'hôte perdue.{reason}", new Color(255, 100, 100, 255), 6f));
			_gameState = GameState.MainMenu;
			_isMultiplayerMenu = false;
			_isHostSetup = false;
			_isJoinSetup = false;
			_isHostingFromPause = false;
			_mpWaitingForWorld = false;
			_mainMenuActivated = false;
			_mainMenuAnim = 0f;
			InventoryRenderer.CloseInventory();
		}

		// Reçu côté client en réponse à une demande de ramassage d'objet au sol acceptée par le host.
		public static void ApplyNetworkItemGrant(ItemGrantMsg grant)
		{
			if (!GameData.ItemDatabase.TryGetValue(grant.ItemId, out var itemData)) return;

			var item = new Item(itemData.Name, grant.Count, itemData.Color, itemData.Icon);
			if (grant.Colors != null && grant.Colors.Count > 0)
			{
				item.CustomColors = grant.Colors
					.Select(c => c != null ? (Color?)new Color(c.R, c.G, c.B, c.A) : null)
					.ToList();
			}
			item.RestoreMetadataFromSave(grant.Metadata ?? "");
			if (grant.Meta != null && grant.Meta.Count > 0)
				foreach (var kv in grant.Meta)
					item.Meta[kv.Key] = kv.Value;
			item.Container = grant.PortableContainer != null ? RestorePortableContainer(grant.PortableContainer) : null;
			item.Backpack = grant.Backpack != null ? RestoreBackpack(grant.Backpack) : null;

			AddItemToInventory(item, item.Count);
			return;
		}

		public static void ApplyNetworkTameResult(TameAnimalResultMsg result)
		{
			if (!result.Success)
			{
				AddNotification(new Notification("Impossible d'apprivoiser cet animal.", new Color(255, 100, 100, 255), 2f));
				return;
			}

			if (GameData.ItemDatabase.TryGetValue(result.ItemId, out var itemData))
				RemoveItemFromInventory(itemData.Name, 1);

			SkillSystem.AddXP(SkillType.Dresseur, 15);
			AchievementManager.Progress(AchievementType.TameAnimal, 1);
			AddNotification(new Notification($" {result.Species} apprivoisé(e) !", new Color(100, 255, 100, 255), 2.5f));
		}

		//  Accès aux données d'un conteneur pour les envoyer à un client
		public static ContainerInventorySave? GetContainerDataForNetwork(int tileX, int tileY, bool underground)
		{
			bool saved = World.IsUnderground;
			World.IsUnderground = underground;
			try
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				if (chunk == null) return null;
				var container = chunk.GetContainerAt(tileX, tileY);
				if (container == null) return null;
				var save = new ContainerInventorySave();
				foreach (var slot in container.Slots)
				{
					save.Slots.Add(new InventorySlotSave
					{
						ItemName = slot.Item?.Name ?? "",
						Count = slot.Count,
						IsEmpty = slot.IsEmpty,
						CustomColors = SaveSystem.SaveCustomColors(slot.Item?.CustomColors)
					});
				}
				return save;
			}
			finally { World.IsUnderground = saved; }
		}

		//  Application d'une synchronisation de conteneur reçue du réseau
		public static void ApplyNetworkContainerSync(ContainerSyncMsg msg)
		{
			bool saved = World.IsUnderground;
			World.IsUnderground = msg.Underground;
			try
			{
				var chunk = World.GetChunkAt(msg.TileX, msg.TileY);
				if (chunk == null) return;
				var container = chunk.GetContainerAt(msg.TileX, msg.TileY);
				if (container == null) return;
				container.Slots.Clear();
				foreach (var slotSave in msg.Data.Slots)
				{
					Item? item = null;
					if (!slotSave.IsEmpty && !string.IsNullOrEmpty(slotSave.ItemName))
					{
						var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == slotSave.ItemName);
						if (itemData.ID != 0)
						{
							item = new Item(itemData.Name, slotSave.Count, itemData.Color, itemData.Icon);
							if (slotSave.CustomColors != null)
								item.CustomColors = slotSave.CustomColors
									.Select(c => c != null ? (Color?)new Color(c.R, c.G, c.B, c.A) : null)
									.ToList();
							item.RestoreMetadataFromSave(slotSave.Metadata ?? "");
							if (itemData.RuneSlots > 0)
							{
								item.EnsureRuneSlots(itemData.RuneSlots);
								item.LoadRunesFromMetadata();
							}
						}
					}
					// Créer un nouveau slot avec l'item et le compte
					var newSlot = new InventorySlot { Item = item, Count = slotSave.Count };
					// Pas besoin d'assigner IsEmpty
					container.Slots.Add(newSlot);
				}
			}
			finally { World.IsUnderground = saved; }
		}

		//  Client : construit l'état actuel du personnage local, à envoyer au host pour
		// qu'il le conserve (voir NetworkManager -> "PlayerSave").
		public static GuestPlayerSaveData BuildGuestSaveData()
		{
			var data = new GuestPlayerSaveData
			{
				PosX = _playerPos.X,
				PosY = _playerPos.Y,
				Facing = _playerFacing,
				Underground = World.IsUnderground,
				HP = playerHP,
				MaxHP = playerMaxHP,
				Stamina = playerStamina,
				MaxStamina = playerMaxStamina,
				Hunger = playerHunger,
				MaxHunger = playerMaxHunger,
				Thirst = playerThirst,
				MaxThirst = playerMaxThirst,
				HairStyle = PlayerHairStyle,
				BeardStyle = PlayerBeardStyle,
				EyeStyle = EyeStyle,
				Species = string.IsNullOrEmpty(PlayerMorphSpecies) ? "human" : PlayerMorphSpecies,
				Skin = new ColorSave { R = SkinColor.R, G = SkinColor.G, B = SkinColor.B, A = SkinColor.A },
				Hair = new ColorSave { R = HairColor.R, G = HairColor.G, B = HairColor.B, A = HairColor.A },
				Eye = new ColorSave { R = EyeColor.R, G = EyeColor.G, B = EyeColor.B, A = EyeColor.A },
				Equipment = SaveSystem.CreateEquipmentSave(equipment)
			};

			foreach (var slot in InventoryRenderer.InventorySlots)
				data.InventorySlots.Add(SaveSystem.CreateInventorySlotSave(slot));

			return data;
		}

		//  Client : applique un personnage sauvegardé reçu du host à la connexion (au lieu
		// du comportement de bienvenue par défaut qui réinitialise tout). Ne touche qu'à
		// l'état du joueur - jamais au monde/aux chunks, gérés séparément par le streaming
		// réseau habituel.
		public static void ApplyGuestSaveData(GuestPlayerSaveData data)
		{
			World.IsUnderground = data.Underground;
			_playerPos = new Vector2(data.PosX, data.PosY);
			_playerFacing = data.Facing;
			_smoothedCameraTarget = GetPlayerCameraTarget(_playerPos);
			_cameraNeedsImmediateCenter = true;

			playerHP = data.HP;
			playerMaxHP = data.MaxHP;
			playerStamina = data.MaxStamina > 0 ? data.Stamina : 100f;
			playerMaxStamina = data.MaxStamina > 0 ? data.MaxStamina : 100f;
			staminaLocked = false;
			playerHunger = data.MaxHunger > 0 ? data.Hunger : 100f;
			playerMaxHunger = data.MaxHunger > 0 ? data.MaxHunger : 100f;
			playerThirst = data.MaxThirst > 0 ? data.Thirst : 100f;
			playerMaxThirst = data.MaxThirst > 0 ? data.MaxThirst : 100f;

			PlayerHairStyle = data.HairStyle;
			PlayerBeardStyle = data.BeardStyle;
			EyeStyle = data.EyeStyle;
			SetPlayerMorphSpecies(string.IsNullOrEmpty(data.Species) ? "human" : data.Species);
			SkinColor = new Color(data.Skin.R, data.Skin.G, data.Skin.B, data.Skin.A);
			HairColor = new Color(data.Hair.R, data.Hair.G, data.Hair.B, data.Hair.A);
			EyeColor = new Color(data.Eye.R, data.Eye.G, data.Eye.B, data.Eye.A);

			equipment = new Equipment();
			SaveSystem.RestoreEquipmentFromSave(equipment, data.Equipment);
			EntityRenderer.ClearEquipmentCache();

			if (data.InventorySlots != null && data.InventorySlots.Count > 0)
				InventoryRenderer.RestoreFromSave(data.InventorySlots);
			else
				InventoryRenderer.Initialize(40);
		}

		public static bool IsPortableContainer(Item item)
		{
			if (item == null) return false;
			int itemId = GetItemId(item.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return false;
			// Comparaison sur la clé stable de l'item (items.json -> "id": "basket_wicker"),
			// et non sur le nom localisé (fragile : dépend de la langue/traduction chargée)
			// ni sur un ID numérique en dur (qui ne correspondait à aucun item réel).
			return (string.Equals(itemData.Key, "basket_wicker", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(itemData.Key, "suitcase", StringComparison.OrdinalIgnoreCase))
				&& itemData.BackpackSlots > 0;
		}

		private static void UpdateMainHandFromHotbar()
		{
			var heldItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
			equipment.MainHand = heldItem;

			//  Si l'item est un conteneur portable, s'assurer que le conteneur est lié
			if (heldItem != null && IsPortableContainer(heldItem))
			{
				equipment.EnsureOffHandContainer();
			}
		}

		// Vérifier si un item est équipable (armure/accessoire couvrant une zone du corps, arme, sac...)
        private static bool IsEquippableItem(Item item)
        {
            int itemId = GetItemId(item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                return false;

            return itemData.CoveredZones.Count > 0
                || itemData.HasEquipSlot
                || itemData.Type == ItemType.Armor
                || itemData.Type == ItemType.Weapon
                || itemData.Type == ItemType.Backpack
                || !string.IsNullOrEmpty(itemData.ArmorSlot);
        }

		public static bool EquipItemFromSlot(InventorySlot slot, string targetSlot)
        {
            if (slot.IsEmpty || slot.Item == null) return false;

            Item newItem = slot.Item;
            string equippedItemName = newItem.Name;
            int itemId = GetItemId(equippedItemName);

            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return false;

            //  Cas 1 : l'item couvre une ou plusieurs zones du corps (armure, accessoire cosmétique...)
            if (itemData.CoveredZones.Count > 0)
            {
                bool success = equipment.EquipOnBody(newItem, out var unequipped);
                if (!success)
                {
                    Program.AddNotification(new Notification($" {itemData.Name} ne peut pas être équipé", Color.Red, 1.5f));
                    return false;
                }

                slot.Clear();

                // Les items délogés par un conflit de zone retournent dans l'inventaire (ou tombent au sol si plein)
                foreach (var displaced in unequipped)
                {
                    Program.GiveItemToPlayer(
                        GetItemId(displaced.Name),
                        1,
                        Program.GetPlayerPosition(),
                        displaced.CustomColors,
						displaced.Meta != null && displaced.Meta.Count > 0 ? new Dictionary<string, string>(displaced.Meta, StringComparer.OrdinalIgnoreCase) : null,
						displaced.Metadata
                    );
                }

                equipment.LoadEquipmentTextures();
                return true;
            }

            //  Cas 2 : emplacement fonctionnel (arme, sac vide, monture...)
            if (!itemData.HasEquipSlot)
            {
                Program.AddNotification(new Notification($" {itemData.Name} ne peut pas être équipé", Color.Red, 1.5f));
                return false;
            }

            EquipmentSlot equipSlot = itemData.EquipSlot;

            if (!GameData.CanEquipItemInSlot(itemData, equipSlot))
            {
                Program.AddNotification(new Notification($" {itemData.Name} ne peut pas être équipé", Color.Red, 1.5f));
                return false;
            }

            int maxCount = itemData.EquipMax;
            int slotIndex = -1;
            int currentCount = equipment.GetEquipCount(equipSlot);

            if (currentCount >= maxCount)
            {
                Program.AddNotification(new Notification($" Trop d'items équipés sur {GameData.GetSlotDisplayName(equipSlot)} (max {maxCount})", new Color(255, 200, 100, 255), 2f));
                return false;
            }

            for (int i = 0; i < maxCount; i++)
            {
                if (equipment.GetItemInSlot(equipSlot, i) == null)
                {
                    slotIndex = i;
                    break;
                }
            }

            if (slotIndex == -1) return false;

            bool equippedOk = equipment.EquipItem(newItem, equipSlot, slotIndex);
            if (!equippedOk)
                return false;

            slot.Clear();
            equipment.LoadEquipmentTextures();
            return true;
        }

		public static void RebuildInventoryFromEquipment()
        {
            InventoryRenderer.RebuildInventory(equipment);
        }

		// Poisson choisi au moment du lancer, donné une fois attrapé

		// Convertit la rareté textuelle d'un item en nombre d'étoiles (1 à 5) pour le mini-jeu de pêche.
		private static int RarityToStars(string? rarity)
		{
			return (rarity ?? "common").ToLowerInvariant() switch
			{
				"common" => 1,
				"uncommon" => 2,
				"rare" => 3,
				"epic" => 4,
				"legendary" => 5,
				_ => 1,
			};
		}

		private static int[] GetFishingItemIds()
		{
			return GameData.ItemDatabase.Values
				.Where(item => item.Key.Equals("fish", StringComparison.OrdinalIgnoreCase)
					|| item.Key.StartsWith("fish_", StringComparison.OrdinalIgnoreCase))
				.Select(item => item.ID)
				.ToArray();
		}

		// Choisit aléatoirement le poisson qui va mordre et renvoie son nombre d'étoiles (rareté).
		private static int PickPendingFishAndGetStars()
		{
			int[] fishIds = GetFishingItemIds();
			if (fishIds.Length == 0)
			{
				_pendingFishId = 0;
				return 1;
			}

			_pendingFishId = fishIds[Random.Shared.Next(fishIds.Length)];
			string rarity = "common";
			if (GameData.ItemDatabase.TryGetValue(_pendingFishId, out var fishData))
				rarity = fishData.Rarity ?? "common";
			return RarityToStars(rarity);
		}

		private static void GiveRandomFish()
		{
			int fishId = _pendingFishId;
			if (fishId == 0)
			{
				int[] fishIds = GetFishingItemIds();
				if (fishIds.Length == 0)
					return;
				fishId = fishIds[Random.Shared.Next(fishIds.Length)];
			}

			Vector2 dropPos = _currentFishingProjectile?.Position ?? Program.GetPlayerPosition();
			Program.GiveItemToPlayer(fishId, 1, dropPos);
			if (GameData.ItemDatabase.TryGetValue(fishId, out var fishItem))
				SkillSystem.AddXP(SkillType.Pecheur, fishItem.Value);
			AchievementManager.Progress(AchievementType.Fishing, 1);
			_pendingFishId = 0;
		}

		private static void BreakRock(Item heldItem, Vector2 playerPos)
		{
			if (attackCooldown > 0) return;

			// Liste des drops possibles (ID, poids)
			var drops = new List<(int id, int weight)>
			{
				(21, 45),   // Pierre (plus fréquente)
				(22, 25),   // Argile
				(27, 20),   // Charbon
				(83, 8),    // Zinc (rare)
				(86, 8),    // Soufre (rare)
				(81, 6),    // Minerai de cuivre (plus rare)
				(25, 6),    // Minerai de fer (plus rare)
				(201, 2),   // Cristal (très rare)
				(89, 1),    // Gemme violette (extrêmement rare)
				(92, 1),    // Gemme jaune (extrêmement rare)
				(91, 1),    // Gemme orange (extrêmement rare)
				(90, 1),    // Gemme bleu foncé (extrêmement rare)
				(202, 1),   // Émeraude (extrêmement rare)
				(203, 1)    // Rubis (extrêmement rare)
			};

			int totalWeight = drops.Sum(d => d.weight);
			int roll = Random.Shared.Next(totalWeight);
			int cumulative = 0;
			int selectedId = 21; // fallback
			foreach (var (id, weight) in drops)
			{
				cumulative += weight;
				if (roll < cumulative)
				{
					selectedId = id;
					break;
				}
			}

			// Donner l'item
			int remaining = AddItemToInventory(selectedId, 1);
			if (remaining == 0 && GameData.ItemDatabase.TryGetValue(selectedId, out var itemData))
			{
				AddItemNotification(itemData.Name, 1, itemData.Color);
				RemoveItemFromInventory(heldItem.Name, 1);      // consomme un amas
				attackCooldown = 0.3f;
				attackAnim = 0.2f;
				isAttacking = true;
				SpawnRockBreakParticles(playerPos);
			}
			else
			{
				AddNotification(new Notification("Inventaire plein !", Color.Red, 1.5f));
			}
		}

		// Applique de la durabilité sur l'item tenu en main (armes/outils).
		private static void ApplyDurabilityToMainHand(int amount)
		{
			if (Program.equipment == null) return;
			var item = Program.equipment.MainHand;
			if (item == null) return;
			// Ne s'applique que si c'est une arme ou un outil
			var type = GameData.GetItemType(item.Name);
			if (type != ItemType.Weapon && type != ItemType.Tool) return;

			string durMeta = item.GetMeta("durability", "");
			float cur = -1f, max = -1f;
			if (!string.IsNullOrWhiteSpace(durMeta))
			{
				// format cur/max ou cur
				var seps = new char[] {'/','|',':','='};
				var parts = durMeta.Split(seps, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 1) float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cur);
				if (parts.Length >= 2) float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out max);
			}
			// Si pas de métadonnée, tenter d'utiliser Metadata contenant cur/max
			if (cur < 0f)
			{
				if (!string.IsNullOrWhiteSpace(item.Metadata) && item.Metadata.Contains("/"))
				{
					var parts = item.Metadata.Split('/');
					if (parts.Length >= 1) float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cur);
					if (parts.Length >= 2) float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out max);
				}
			}

			if (max <= 0f)
			{
				// Si pas de max connu, définir un max par défaut
				max = 100f;
				if (cur < 0f) cur = max;
			}

			cur -= amount;
			if (cur <= 0f)
			{
				// L'arme se casse
				RemoveItemFromInventory(item.Name, 1);
				AddNotification(new Notification($" {item.Name} s'est cassée !", new Color(200, 150, 100, 255), 2f));
				Program.equipment.MainHand = null;
				return;
			}
			// Mettre à jour la métadonnée
			item.SetMeta("durability", $"{cur.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
		}

		private static int GetItemIdFromPlaceableId(int placeableId)
		{
			foreach (var kv in GameData.ItemDatabase)
				if (kv.Value.PlaceableId == placeableId)
					return kv.Key;
			return 0;
		}

		public class CarriedFurniture
		{
			public int ItemId;
			public int PlaceableId;
			public string ItemName;
			public string? PottedFlowerItemId;
			public ContainerInventoryData? ContainerData;
			public ArmorStandData? ArmorStandData;
		}

		private static bool IsInventoryFull()
		{
			foreach (var slot in InventoryRenderer.InventorySlots)
				if (slot.IsEmpty) return false;
			return true;
		}

		/// <summary>
		/// Détermine si un item donné peut encore trouver une place quelque part chez le
		/// joueur : soit en se stackant sur un tas existant (ceinture, sac à dos, panier
		/// équipé), soit dans un slot vide de l'un de ces stockages. Contrairement à
		/// IsInventoryFull (qui ne regarde que la ceinture et ignore les stacks), cette
		/// méthode reflète fidèlement ce que AddItemToInventory est capable d'absorber.
		/// </summary>
		public static bool HasSpaceForItem(int itemId)
		{
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var data))
				return false;

			foreach (var slot in GetAllInventorySlots())
			{
				if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == data.Name && slot.Count < data.StackSize)
					return true;
				if (slot.IsEmpty)
					return true;
			}

			return false;
		}

		private static void SelectItemInHotbar(int itemId)
		{
			var itemData = GameData.ItemDatabase[itemId];
			for (int i = 0; i < 10 && i < InventoryRenderer.InventorySlots.Count; i++)
			{
				var slot = InventoryRenderer.InventorySlots[i];
				if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == itemData.Name)
				{
					Program.hotbarSlot = i;
					break;
				}
			}
		}

		public static void DamagePlayer(int amount, string? sourceName = null, DeathCause cause = DeathCause.Generic)
		{
			if (amount <= 0) return;
			//  Mémorise la source du coup qui vient d'être porté : si ce coup s'avère fatal
			// (playerHP <= 0 plus bas), c'est CE nom/cause qui sera utilisé pour composer le
			// message de l'écran de mort (voir StartDeathScreen).
			if (!string.IsNullOrEmpty(sourceName))
			{
				_lastDamageSourceName = sourceName;
				_lastDamageCause = cause;
			}
			if (IsPlayerRagdolled) return; // déjà mort, en ragdoll : plus de dégâts tant qu'on n'a pas réapparu
			if (IsGodMode)
			{
				playerHP = GetEffectivePlayerMaxHP();
				return;
			}

			int effectiveDamage = equipment.GetDamageAfterArmor(amount);
			playerHP -= effectiveDamage;
			
			//  AJOUTER LES DÉGÂTS FLOTTANTS SUR LE JOUEUR
			Vector2 playerVisualPos = GetPlayerVisualPosition(_playerPos);
			AddFloatingDamage(playerVisualPos, effectiveDamage, effectiveDamage >= 10, isPlayer: true);
			
			//  Vérifier que la position du joueur est valide avant le screen shake
			if (!float.IsNaN(_playerPos.X) && !float.IsNaN(_playerPos.Y) &&
				!float.IsInfinity(_playerPos.X) && !float.IsInfinity(_playerPos.Y))
			{
				ApplyScreenShake(effectiveDamage * 0.8f, 0.25f);
			}
			
			if (playerHP <= 0 && !IsPlayerRagdolled)
			{
				playerHP = 0;
				_playerDeathPosition = _playerPos;
				LastDeathPosition = _playerPos;
				HasDiedAtLeastOnce = true;
				_playerDeathTimer = PLAYER_DEATH_DURATION;
				_playerDeathPendingRespawn = true;
				IsPlayerRagdolled = true;
				_ragdollMinDelay = 0.2f;
				SpawnPlayerDeathEffect(playerVisualPos);
				DropPlayerLootbag(playerVisualPos);
				StartDeathScreen();
				AddNotification(new Notification("Vous êtes mort... mais votre butin reste au sol.", new Color(255, 69, 0, 255), 3.5f));
				if (!AchievementManager.IsUnlocked(AchievementType.FirstDeath))
				{
					AchievementManager.UnlockAchievement(AchievementType.FirstDeath);
					PlayAchievementSound();
				}
			}
		}

		/// <summary>
		/// Met à jour l'endurance (vidage au sprint / remontée progressive) ainsi que
		/// la faim et la soif (diminution lente + dégâts de famine quand elles
		/// atteignent 0).
		/// </summary>
		private static void UpdateSurvivalStats(float dt)
		{
			if (IsPlayerRagdolled) return; // pas de mise à jour pendant le ragdoll de mort

			if (IsGodMode)
			{
				playerStamina = playerMaxStamina;
				staminaLocked = false;
				playerHP = GetEffectivePlayerMaxHP();
				playerHunger = playerMaxHunger;
				playerThirst = playerMaxThirst;
				_starvationDamageAccumulator = 0f;
				_hungerWarningPlayed = false;
				return;
			}
			
			// --- Endurance ---
			bool isSprintingNow = KeyBindings.IsDown(GameAction.Sprint) && playerStamina > 0f && !staminaLocked
				&& (KeyBindings.IsDown(GameAction.MoveUp) || KeyBindings.IsDown(GameAction.MoveLeft) || KeyBindings.IsDown(GameAction.MoveDown) || KeyBindings.IsDown(GameAction.MoveRight));
			
			if (isSprintingNow)
			{
				playerStamina -= STAMINA_DRAIN_PER_SEC * dt;
				_staminaRegenTimer = 0f;
				if (playerStamina <= 0f)
				{
					playerStamina = 0f;
					staminaLocked = true; // il faut attendre le remplissage COMPLET avant de resprinter
				}
			}

			// ---- Pourrissement des aliments (tick global, géré côté host seulement) ----
			if (WorldFoodSpoil && !NetworkManager.IsClient)
			{
				// Dégrader la valeur 'spoil' pour les items de type Food dans l'inventaire du joueur
				float daySeconds = DAY_CYCLE_DURATION;
				// Durée par défaut avant pourrissement complet (en secondes de jeu) : 7 jours
				float spoilLifetime = daySeconds * 7f;
				foreach (var slot in GetAllInventorySlots().ToList())
				{
					if (slot.IsEmpty || slot.Item == null) continue;
					int id = GetItemId(slot.Item.Name);
					if (!GameData.ItemDatabase.TryGetValue(id, out var data) || data.Type != ItemType.Food) continue;
					if (ApplyFoodSpoilTick(slot.Item, dt, spoilLifetime))
						ConvertSpoiledSlotToMoldyScrap(slot);
				}
				// Main hand (si nourriture tenue)
				if (equipment.MainHand != null)
				{
					int id = GetItemId(equipment.MainHand.Name);
					if (GameData.ItemDatabase.TryGetValue(id, out var data) && data.Type == ItemType.Food)
					{
						if (ApplyFoodSpoilTick(equipment.MainHand, dt, spoilLifetime))
							ConvertSpoiledMainHandToMoldyScrap();
					}
				}
			}

			_staminaRegenTimer += dt;
			if (_staminaRegenTimer >= STAMINA_REGEN_DELAY && playerStamina < playerMaxStamina)
			{
				playerStamina = Math.Min(playerMaxStamina, playerStamina + STAMINA_REGEN_PER_SEC * dt);
				if (staminaLocked && playerStamina >= playerMaxStamina)
					staminaLocked = false; // pleine jauge -> on peut de nouveau sprinter
			}
			
			if (IsGodMode)
			{
				playerHP = GetEffectivePlayerMaxHP();
				playerHunger = playerMaxHunger;
				playerThirst = playerMaxThirst;
				_starvationDamageAccumulator = 0f;
				_hungerWarningPlayed = false;
				return;
			}

			// --- Faim / Soif ---
			//  RASSASIEMENT : tant que _satietyTimer > 0, la faim ne baisse plus (le
			// joueur a mangé jusqu'à saturation récemment). Le timer se décrémente ici
			// aussi ; une fois à 0, la faim recommence à baisser normalement.
			if (_satietyTimer > 0f)
			{
				_satietyTimer = Math.Max(0f, _satietyTimer - dt);
				if (_satietyTimer <= 0f)
				{
					AddNotification(new Notification(" Vous recommencez à avoir faim.", new Color(220, 200, 160, 255), 1.8f));
				}
			}
			else
			{
				playerHunger = Math.Max(0f, playerHunger - HUNGER_DRAIN_PER_SEC * dt);
			}
			playerThirst = Math.Max(0f, playerThirst - THIRST_DRAIN_PER_SEC * dt);

			// --- Son d'alerte de faim : joué une seule fois en passant sous le seuil,
			//     et réarmé dès que la faim remonte au-dessus (pour pouvoir se redéclencher plus tard) ---
			if (playerHunger <= HUNGER_WARNING_THRESHOLD)
			{
				if (!_hungerWarningPlayed)
				{
					_hungerWarningPlayed = true;
					PlayHungrySound();
				}
			}
			else
			{
				_hungerWarningPlayed = false;
			}
			
			// --- Conséquence : perte de vie douce si faim OU soif est à 0 ---
			if (playerHunger <= 0f || playerThirst <= 0f)
			{
				_starvationDamageAccumulator += STARVATION_DAMAGE_PER_SEC * dt;
				if (_starvationDamageAccumulator >= 5f)
				{
					int dmg = (int)_starvationDamageAccumulator;
					_starvationDamageAccumulator -= dmg;
					DamagePlayer(dmg, "la faim et la soif", DeathCause.Starvation);
				}
			}
			else
			{
				_starvationDamageAccumulator = 0f;
			}

            if (_speedPotionTimer > 0f)
            {
                _speedPotionTimer = Math.Max(0f, _speedPotionTimer - dt);
                if (_speedPotionTimer <= 0f)
                {
                    AddNotification(new Notification(" L'effet de la potion de vitesse s'est dissipé.", new Color(200, 200, 255, 255), 1.8f));
                }
            }

            if (_strengthPotionTimer > 0f)
            {
                _strengthPotionTimer = Math.Max(0f, _strengthPotionTimer - dt);
                if (_strengthPotionTimer <= 0f)
                    AddNotification(new Notification(" L'effet de la potion de force s'est dissipé.", new Color(200, 200, 255, 255), 1.8f));
            }

            if (_resistancePotionTimer > 0f)
            {
                _resistancePotionTimer = Math.Max(0f, _resistancePotionTimer - dt);
                if (_resistancePotionTimer <= 0f)
                    AddNotification(new Notification(" L'effet de la potion de résistance s'est dissipé.", new Color(200, 200, 255, 255), 1.8f));
            }

            if (_lightPotionTimer > 0f)
            {
                _lightPotionTimer = Math.Max(0f, _lightPotionTimer - dt);
                if (_lightPotionTimer <= 0f)
                    AddNotification(new Notification(" L'effet de la potion de lumière s'est dissipé.", new Color(200, 200, 255, 255), 1.8f));
            }

            if (_heatResistPotionTimer > 0f)
            {
                _heatResistPotionTimer = Math.Max(0f, _heatResistPotionTimer - dt);
                if (_heatResistPotionTimer <= 0f)
                    AddNotification(new Notification(" L'effet de résistance à la chaleur s'est dissipé.", new Color(200, 200, 255, 255), 1.8f));
            }

            if (_coldResistPotionTimer > 0f)
            {
                _coldResistPotionTimer = Math.Max(0f, _coldResistPotionTimer - dt);
                if (_coldResistPotionTimer <= 0f)
                    AddNotification(new Notification(" L'effet de résistance au froid s'est dissipé.", new Color(200, 200, 255, 255), 1.8f));
            }

            if (_invisibilityPotionTimer > 0f)
            {
                _invisibilityPotionTimer = Math.Max(0f, _invisibilityPotionTimer - dt);
                if (_invisibilityPotionTimer <= 0f)
                    AddNotification(new Notification(" Vous redevenez visible.", new Color(200, 200, 255, 255), 1.8f));
            }

            if (_poisonTimer > 0f)
            {
                _poisonTimer = Math.Max(0f, _poisonTimer - dt);
                _poisonTickAccumulator += dt;
                if (_poisonTickAccumulator >= POISON_TICK_INTERVAL)
                {
                    int ticks = (int)(_poisonTickAccumulator / POISON_TICK_INTERVAL);
                    _poisonTickAccumulator -= ticks * POISON_TICK_INTERVAL;
                    for (int i = 0; i < ticks; i++)
                    {
                        DamagePlayer((int)GetPoisonDamagePerTick(), "le poison", DeathCause.Poison);
                    }
                }
                if (_poisonTimer <= 0f)
                {
                    _poisonTimer = 0f;
                    _poisonDuration = 0f;
                    _poisonTickAccumulator = 0f;
                    AddNotification(new Notification(" Le poison s'est dissipé.", new Color(200, 255, 180, 255), 1.8f));
                }
            }
        }

		private static float GetPlayerSpeedMultiplier()
        {
            float potionMultiplier = _speedPotionTimer > 0f ? SPEED_POTION_SPEED_MULTIPLIER : 1f;
            return potionMultiplier * equipment.TotalRuneSpeedMultiplier;
        }

		private static bool HasFullArmorSet(int[] pieceIds)
		{
			foreach (var id in pieceIds)
			{
				bool found = false;
				foreach (var it in equipment.ZoneItems)
				{
					if (GetItemId(it.Name) == id) { found = true; break; }
				}
				if (!found) return false;
			}
			return true;
		}

		public static void ReloadData()
		{
			try
			{
				GameData.LoadFromFiles();
				SpeciesData.LoadFromFile();
				WorldTileRegistry.LoadFromFile();
				StructureManager.LoadStructures();
				EntityRenderer.ClearEquipmentCache(); // ← Déjà présent

				AddNotification(new Notification(Localization.Get("inventory.data_reloaded"), new Color(100, 255, 100, 255), 3f));
				Console.WriteLine("Données rechargées à chaud.");
			}
			catch (Exception ex)
			{
				AddNotification(new Notification($" Erreur lors du rechargement : {ex.Message}", Color.Red, 4f));
				Console.WriteLine($"Erreur ReloadData : {ex}");
			}
		}

		public static void ToggleNoClip()
		{
			_noClipEnabled = !_noClipEnabled;
			AddNotification(new Notification($" NoClip {(_noClipEnabled ? "activé" : "désactivé")} !", 
				_noClipEnabled ? new Color(100, 255, 100, 255) : new Color(255, 100, 100, 255), 2f));
		}

		//  MULTIJOUEUR : construit (ou récupère du cache) l'Equipment visuel d'un joueur
		// distant à partir des ids d'objets synchronisés (EqHead/EqBody/EqLegs). Sans ça,
		// les habits des autres joueurs ne s'affichaient jamais.
		public static Equipment? GetNetworkPlayerEquipment(int connectionId, int eqHead, int eqBody, int eqLegs)
		{
			if (eqHead == 0 && eqBody == 0 && eqLegs == 0)
			{
				_networkEquipmentCache.Remove(connectionId);
				return null;
			}

			if (_networkEquipmentCache.TryGetValue(connectionId, out var cached) &&
				cached.head == eqHead && cached.body == eqBody && cached.legs == eqLegs)
			{
				return cached.eq;
			}

			var eq = new Equipment();
			var headItem = BuildEquipmentItem(eqHead);
			var bodyItem = BuildEquipmentItem(eqBody);
			var legsItem = BuildEquipmentItem(eqLegs);
			if (headItem != null) eq.EquipOnBody(headItem, out _);
			if (bodyItem != null) eq.EquipOnBody(bodyItem, out _);
			if (legsItem != null) eq.EquipOnBody(legsItem, out _);
			eq.LoadEquipmentTextures();

			_networkEquipmentCache[connectionId] = (eqHead, eqBody, eqLegs, eq);
			return eq;
		}

		//  MULTIJOUEUR : version complète (toutes zones + teintes) de l'équipement d'un
		// joueur distant, construite depuis EquipData (voir NetworkManager.BuildEquipmentFromData).
		// C'est ce qui manquait pour voir lunettes/écharpes/bijoux et les objets teintés
		// chez les autres joueurs : l'ancienne version ne gérait que Head/Body/Legs, sans couleur.
		public static Equipment? GetNetworkPlayerEquipmentFull(int connectionId, EquipData? data)
		{
			if (data == null || data.IsEmpty)
			{
				_networkFullEquipmentCache.Remove(connectionId);
				return null;
			}

			string sig = JsonSerializer.Serialize(data);
			if (_networkFullEquipmentCache.TryGetValue(connectionId, out var cached) && cached.signature == sig)
				return cached.eq;

			var eq = NetworkManager.BuildEquipmentFromData(data);
			_networkFullEquipmentCache[connectionId] = (sig, eq);
			return eq;
		}

		//  MULTIJOUEUR : équivalent pour les PNJ/créatures distants (EntityDto.Equip),
		// indexé par NetId puisqu'il n'y a pas de connectionId pour une entité.
		public static Equipment? GetNetworkEntityEquipment(string netId, EquipData? data)
		{
			if (data == null || data.IsEmpty)
			{
				_networkEntityEquipmentCache.Remove(netId);
				return null;
			}

			string sig = JsonSerializer.Serialize(data);
			if (_networkEntityEquipmentCache.TryGetValue(netId, out var cached) && cached.signature == sig)
				return cached.eq;

			var eq = NetworkManager.BuildEquipmentFromData(data);
			_networkEntityEquipmentCache[netId] = (sig, eq);
			return eq;
		}

		private static Item? BuildEquipmentItem(int itemId)
		{
			if (itemId == 0) return null;
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return null;
			return new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
		}

		public static PortableContainerSaveData? CreatePortableContainerSaveData(PortableContainerData? container)
	{
		if (container == null) return null;
		return new PortableContainerSaveData
		{
			Columns = container.Inventory.Columns,
			Rows = container.Inventory.Rows,
			Slots = container.Inventory.Slots.Select(SaveSystem.CreateInventorySlotSave).ToList()
		};
	}

		public static BackpackSaveData? CreateBackpackSaveData(BackpackData? backpack)
	{
		if (backpack == null) return null;
		return new BackpackSaveData
		{
			Columns = backpack.Inventory.Columns,
			Rows = backpack.Inventory.Rows,
			Slots = backpack.Inventory.Slots.Select(SaveSystem.CreateInventorySlotSave).ToList()
		};
	}

		public static PortableContainerData? RestorePortableContainer(PortableContainerSaveData? save)
	{
		if (save == null) return null;
		var container = new PortableContainerData(save.Slots.Count, save.Columns > 0 ? save.Columns : 3);
		for (int i = 0; i < save.Slots.Count && i < container.Inventory.Slots.Count; i++)
		{
			SaveSystem.RestoreInventorySlotFromSave(save.Slots[i], container.Inventory.Slots[i]);
		}
		return container;
	}

	public static BackpackData? RestoreBackpack(BackpackSaveData? save)
	{
		if (save == null) return null;
		var backpack = new BackpackData(save.Slots.Count, save.Columns > 0 ? save.Columns : 5);
		for (int i = 0; i < save.Slots.Count && i < backpack.Inventory.Slots.Count; i++)
		{
			SaveSystem.RestoreInventorySlotFromSave(save.Slots[i], backpack.Inventory.Slots[i]);
		}
		return backpack;
	}

	/// <summary>
	/// Variante utilisée pour les items jetés : worldPos est le point de départ (le
	/// joueur), pas la position finale. throwDirection/throwDistance donnent à l'item
	/// (seau rempli, gourde chargée, etc.) au lieu de le perdre au moment du drop.
	/// </summary>
	public static void DropCustomItemOnGround(Vector2 worldPos, int itemId, int count, List<Color?>? customColors, Vector2? throwDirection, float throwDistance, string? metadata = null, Dictionary<string, string>? meta = null, PortableContainerData? container = null, BackpackData? backpack = null, float? pickupCooldown = null)
	{
		if (count <= 0) return;

		//  MULTIJOUEUR : le host est seul autoritaire sur Program.GroundItems. Si un
		// client ajoutait directement ici, l'item n'existerait QUE chez lui (invisible
		// et non ramassable pour les autres, et "flottant" car jamais mis à jour par
		// un snapshot du host). On délègue donc la création réelle au host.
		if (NetworkManager.IsClient)
		{
			List<ColorSave?>? colorSaves = customColors?.Select(c => c.HasValue
				? (ColorSave?)new ColorSave { R = c.Value.R, G = c.Value.G, B = c.Value.B, A = c.Value.A }
				: (ColorSave?)null).ToList();
			NetworkManager.RequestItemDrop(itemId, count, worldPos.X, worldPos.Y, colorSaves, metadata, meta,
				CreatePortableContainerSaveData(container), CreateBackpackSaveData(backpack), pickupCooldown);
			return;
		}

		SpawnAuthoritativeGroundItem(itemId, count, worldPos, customColors, throwDirection, throwDistance, metadata, meta, container, backpack, pickupCooldown);

			if (NetworkManager.IsHost)
				NetworkManager.BroadcastGroundItemsSnapshot();
		}

		//  MULTIJOUEUR : création réelle d'un item au sol. N'appeler QUE côté host (ou en
		// solo) — jamais côté client, sous peine de créer un item fantôme désynchronisé.
public static void SpawnAuthoritativeGroundItem(int itemId, int count, Vector2 worldPos, List<Color?>? customColors = null, PortableContainerData? container = null, BackpackData? backpack = null)
	{
		SpawnAuthoritativeGroundItem(itemId, count, worldPos, customColors, null, 0f, null, null, container, backpack);
	}

		public static void SpawnAuthoritativeGroundItem(int itemId, int count, Vector2 worldPos, List<Color?>? customColors, Vector2? throwDirection, float throwDistance, string? metadata = null, Dictionary<string, string>? meta = null, PortableContainerData? container = null, BackpackData? backpack = null, float? pickupCooldown = null)
	{
		if (count <= 0) return;
		int tileX = (int)(worldPos.X / TileSize);
		int tileY = (int)(worldPos.Y / TileSize);
		int height = World.GetHeightAt(tileX, tileY);
		float groundY = tileY * TileSize - (height * TileSize / 4) + TileSize;

		GroundItem groundItem = throwDirection.HasValue && throwDistance > 0f
			? new GroundItem(itemId, count, worldPos, groundY, throwDirection.Value, throwDistance, customColors, null, metadata, meta, container, backpack)
			: new GroundItem(itemId, count, worldPos, groundY, customColors, null, metadata, meta, container, backpack);
		if (pickupCooldown.HasValue)
			groundItem.PickupCooldown = Math.Max(0f, pickupCooldown.Value);
			GroundItems.Add(groundItem);
		}

		// Surcharge pour compatibilité legacy
		public static void DropCustomItemOnGround(Vector2 worldPos, int itemId, int count, Color? customColor = null)
		{
			List<Color?>? customColors = null;
			if (customColor.HasValue)
			{
				customColors = new List<Color?> { customColor.Value };
			}
			DropCustomItemOnGroundWithColors(worldPos, itemId, count, customColors);
		}

		public static void DropCustomItemOnGroundWithColors(Vector2 worldPos, int itemId, int count, List<Color?>? customColors = null)
		{
			DropCustomItemOnGround(worldPos, itemId, count, customColors, null, 0f, null, null);
		}

		public static void DropCustomItemOnGroundWithColors(Vector2 worldPos, int itemId, int count, List<Color?>? customColors, string? metadata, Dictionary<string, string>? meta = null)
		{
			DropCustomItemOnGround(worldPos, itemId, count, customColors, null, 0f, metadata, meta);
		}

		public static void ThrowDraggedItem(Item? item, int count)
		{
			if (item == null || count <= 0) return;
			
			Vector2 playerPos = GetPlayerPosition();
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), GetCurrentCamera());
			Vector2 direction = mouseWorld - playerPos;
			if (direction.Length() < 0.01f) direction = new Vector2(1, 0);
			direction = Vector2.Normalize(direction);
			
			//  Les items sont désormais attirés automatiquement dès qu'ils sont à portée
			// (voir GroundItem.PickupRadius). Un item jeté doit donc atterrir un peu plus
			// loin que cette portée pour ne pas revenir instantanément vers le joueur.
			float throwDistance = GroundItem.DefaultPickupRadius + 30f;

			// Si un obstacle bloque la direction visée à cette distance, on réduit la
			// portée du lancer au lieu de téléporter l'item ailleurs.
			int ts = TileSize;
			Vector2 landingSpot = playerPos + direction * throwDistance;
			int tileX = (int)(landingSpot.X / ts);
			int tileY = (int)(landingSpot.Y / ts);
			int groundId = World.GetGroundTileIdAt(tileX, tileY);
			var groundTile = WorldTileRegistry.GetTile(groundId);
			bool isWalkable = (groundTile != null && groundTile.Walkable) && World.GetObjectIdAt(tileX, tileY) == 0;
			if (!isWalkable)
			{
				throwDistance = GroundItem.DefaultPickupRadius * 0.5f;
			}
			
			int itemId = GetItemId(item.Name);
			
			if (itemId != 0 && GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
			{
				//  Utiliser les couleurs multiples de l'item
				List<Color?> colorsToUse = null;
				if (item.CustomColors != null && item.CustomColors.Count > 0)
				{
					colorsToUse = new List<Color?>(item.CustomColors);
				}
				
				//  L'item part de la position du joueur avec une vélocité dirigée : il a
				// l'air d'être lancé (voir GroundItem constructeur "throw") au lieu d'être
				// téléporté instantanément à sa position d'atterrissage.
				DropCustomItemOnGround(playerPos, itemId, count, colorsToUse, direction, throwDistance, item.Metadata, item.Meta, item.Container, item.Backpack);
			}
		}

		static void HandleWallCoveringPlace(Vector2 mouseWorld, Item heldItem, ItemData itemData)
		{
			int ts = Program.TileSize;
			(int tileX, int tileY, float dist, int objectId) = FindTileUnderMouse(mouseWorld, ts);
			
			if (tileX == -1 || tileY == -1) return;
			
			// Vérifier si la tuile ciblée est un mur (ID 8 ou 84)
			if (objectId != 8 && objectId != 84) return;
			
			// Vérifier la distance
			float playerDistSq = Vector2.DistanceSquared(Program.GetPlayerPosition(), new Vector2(tileX * ts + ts/2f, tileY * ts + ts/2f));
			if (playerDistSq > (ts * 3f) * (ts * 3f)) return;
			
			// Récupérer le chunk
			var chunk = World.GetChunkAt(tileX, tileY);
			if (chunk == null) return;
			
			// Appliquer le revêtement
			chunk.SetWallCoveringAt(tileX, tileY, itemData.PlaceableId);
			
			// Retirer l'item de l'inventaire
			RemoveItemFromInventory(heldItem.Name, 1);
			
			Program.AddNotification(new Notification($" Revêtement '{itemData.Name}' appliqué au mur !", new Color(100, 200, 255, 255), 2f));
		}

		//  SYSTÈME DE QUÊTES : tente de livrer l'objet attendu à ce PNJ cible. Factorisé pour être
		// appelé directement quand le joueur choisit explicitement "Livrer l'objet" dans la bulle de
		// choix, sans dépendre de l'ordre de priorité de HandleQuestNpcInteraction.
		//
		//  MULTIJOUEUR : l'inventaire est géré localement par chaque client, donc la vérification
		// et le retrait de l'objet se font toujours ici, en local. Seul le passage de la quête à
		// l'état "Delivered" doit être validé par l'hôte (seul autoritaire sur Program.entities) :
		// sur un client, on envoie donc une requête au lieu d'appeler QuestManager.TryDeliverQuest
		// directement sur le PNJ fantôme (qui serait de toute façon écrasé au prochain snapshot).
		private static bool TryDeliverQuestWithNpc(Entity npc)
		{
			var deliverQuest = QuestManager.GetQuestToDeliverFor(npc, GetQuestNpcs());
			if (deliverQuest == null) return false;

			if (deliverQuest.Type == QuestType.TameAndBring)
				return TryDeliverTameQuestWithNpc(npc, deliverQuest);

			bool hasItem = GetItemCountInInventory(deliverQuest.ItemId) >= deliverQuest.ItemQty;
			if (!hasItem)
			{
				QuestDialogUI.OpenReminder(npc, deliverQuest.GetReminderLine(npc.DisplayName ?? "ce PNJ"));
				return true;
			}

			if (NetworkManager.IsClient)
			{
				RemoveItemFromInventoryById(deliverQuest.ItemId, deliverQuest.ItemQty);
				NetworkManager.RequestQuestAction(npc.NetId, "Deliver");
				AddNotification(new Notification(
					$" Objet remis à {npc.DisplayName ?? "ce PNJ"}.",
					new Color(220, 200, 100, 255), 2.8f));
				return true;
			}

			bool delivered = QuestManager.TryDeliverQuest(
				npc,
				GetQuestNpcs(),
				hasItem: (itemId, qty) => GetItemCountInInventory(itemId) >= qty,
				removeItem: (itemId, qty) => RemoveItemFromInventoryById(itemId, qty),
				out var deliveredQuest
			);

			if (delivered && deliveredQuest != null)
			{
				AddNotification(new Notification(
					$" Objet remis à {npc.DisplayName ?? "ce PNJ"}. Retourne voir {QuestManager.FindEntityByNetId(GetQuestNpcs(), deliveredQuest.GiverId)?.DisplayName ?? "le donneur"} pour récupérer ta récompense.",
					new Color(220, 200, 100, 255), 2.8f));
			}
			else
			{
				QuestDialogUI.OpenReminder(npc, deliverQuest.GetReminderLine(npc.DisplayName ?? "ce PNJ"));
			}
			return true;
		}

		private static (string Objective, string Reward) GetQuestCompletionSplashText(Quest quest)
		{
			var target = QuestManager.FindEntityByNetId(GetQuestNpcs(), quest.TargetId);
			string targetName = target?.DisplayName ?? target?.FirstName ?? Localization.Get("quest.unknown_target", "???");
			string objective = quest.GetReminderLine(targetName);
			string reward = string.Format(
				Localization.Get("quest.offer_reward", "Récompense : {0}"),
				quest.GetRewardLine());
			return (objective, reward);
		}

		//  MULTIJOUEUR : la récompense est ajoutée à l'inventaire du joueur si possible ;
		// si l'inventaire est plein, le reliquat tombe au sol à la position du PNJ. Les clients
		// ne modifient pas directement le sol, ils délèguent via le host.
		private static bool TryValidateQuestWithNpc(Entity npc)
		{
			var incomingQuest = QuestManager.GetIncomingQuestFor(npc, GetQuestNpcs());
			if (incomingQuest == null) return false;

			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestQuestAction(npc.NetId, "Validate");
				var splashText = GetQuestCompletionSplashText(incomingQuest);
				QuestSplashUI.Show(
					Localization.Get("quest.splash.completed", "Quête terminée"),
					splashText.Objective,
					splashText.Reward,
					new Color(140, 255, 170, 255),
					new Color(255, 255, 255, 255));
				return true;
			}

			bool completed = QuestManager.TryCompleteQuest(
				npc,
				GetQuestNpcs(),
				giveItem: (itemId, qty) => GiveQuestItemToPlayer(itemId, qty, npc.WorldPos),
				out var completedQuest
			);

			if (completed && completedQuest != null)
			{
				var splashText = GetQuestCompletionSplashText(completedQuest);
				QuestSplashUI.Show(
					Localization.Get("quest.splash.completed", "Quête terminée"),
					splashText.Objective,
					splashText.Reward,
					new Color(140, 255, 170, 255),
					new Color(255, 255, 255, 255));
			}
			else
			{
				QuestDialogUI.OpenReminder(npc, "Merci pour la livraison. Reviens me voir pour finaliser la quête.");
			}
			return true;
		}

		public static void AddItemNotification(string itemName, int quantity, Color color)
		{
            int itemId = GetItemId(itemName);
            string displayName = itemId > 0
                ? ItemRenderer.GetDisplayName(null, itemId, itemName)
                : itemName;

            Texture2D itemIcon = new Texture2D();
            string rarity = "common";
            
            foreach (var kv in GameData.ItemDatabase)
            {
                if (kv.Value.Name == itemName)
                {
                    itemIcon = kv.Value.Icon;
                    rarity = kv.Value.Rarity ?? "common";
                    break;
                }
            }
            
            Color rarityColor = GameData.GetRarityColor(rarity);
            
            var existing = activeNotifications.FirstOrDefault(n => n.Message == displayName);
            if (existing != null)
            {
                existing.Timer = existing.MaxTimer;
                existing.StackCount += quantity;
                existing.MaxTimer = 3f;
                existing.Timer = 3f;
                existing.TextColor = rarityColor;
            }
            else 
            {
                activeNotifications.Add(new Notification(displayName, quantity, rarityColor, itemIcon, 3f));
            }
        }

		public static void AddItemNotification(Item? item, int quantity, Color color)
        {
            if (item == null) return;

            int itemId = GetItemId(item.Name);
            string displayName = ItemRenderer.GetDisplayName(item, itemId, item.Name);
            Texture2D itemIcon = item.Icon.Id != 0 ? item.Icon : new Texture2D();
            string rarity = "common";
            if (GameData.ItemDatabase.TryGetValue(itemId, out var data))
            {
                rarity = data.Rarity ?? "common";
                if (itemIcon.Id == 0 && data.Icon.Id != 0)
                    itemIcon = data.Icon;
            }

            Color rarityColor = GameData.GetRarityColor(rarity);
            var existing = activeNotifications.FirstOrDefault(n => n.Message == displayName);
            if (existing != null)
            {
                existing.Timer = existing.MaxTimer;
                existing.StackCount += quantity;
                existing.MaxTimer = 3f;
                existing.Timer = 3f;
                existing.TextColor = rarityColor;
            }
            else
            {
                activeNotifications.Add(new Notification(displayName, quantity, rarityColor, itemIcon, 3f));
            }
        }

		static void DropPlayerLootbag(Vector2 worldPos)
		{
			var payload = BuildPlayerLootbagPayload();
			if (string.IsNullOrWhiteSpace(payload)) return;

			if (NetworkManager.IsClient)
			{
				//  MULTIJOUEUR : un client ne possède pas Program.GroundItems (seul le host
				// en est propriétaire), donc il ne peut pas faire apparaître son sac de
				// butin lui-même. On envoie le contenu au host, qui le crée réellement et
				// le diffuse à tout le monde ; on vide ensuite l'inventaire local comme
				// pour le host, pour ne pas dupliquer les objets déjà "au sol".
				NetworkManager.RequestLootbagDrop(payload, worldPos);
				ClearCurrentPlayerInventoryForLootbag();
				return;
			}

			ClearCurrentPlayerInventoryForLootbag();
			SpawnAuthoritativePlayerLootbag(payload, worldPos);
		}

		//  MULTIJOUEUR : point d'entrée unique pour faire réellement apparaître un sac de
		// butin dans Program.GroundItems (le host uniquement doit appeler ceci) — utilisé
		// à la fois pour la mort locale du host et pour la mort d'un client distant
		// (voir NetworkManager.HandleLootbagDropRequest).
		public static void SpawnAuthoritativePlayerLootbag(string payload, Vector2 worldPos)
		{
			if (string.IsNullOrWhiteSpace(payload)) return;
			int itemId = GetItemId("Sac en osier");
			if (itemId == 0) itemId = 211;
			SpawnAuthoritativeGroundItem(itemId, 1, worldPos, null, null, 0f);
			var bag = GroundItems.LastOrDefault();
			if (bag != null)
			{
				bag.LootbagPayload = payload;
				bag.PickupRadius = 80f;
				bag.PickupCooldown = 1.5f;
				bag.Position = new Vector2(worldPos.X, worldPos.Y + 8f);
			}
		}

		static string BuildPlayerLootbagPayload()
		{
			var inventorySlots = new List<string>();
			foreach (var slot in InventoryRenderer.InventorySlots)
			{
				if (slot.IsEmpty || slot.Item == null) continue;
				if (!GameData.ItemDatabase.TryGetValue(GetItemId(slot.Item.Name), out var itemData)) continue;
				inventorySlots.Add($"{itemData.ID}:{slot.Count}");
			}
			if (equipment.BackpackContainer != null)
			{
				foreach (var slot in equipment.BackpackContainer.Inventory.Slots)
				{
					if (slot.IsEmpty || slot.Item == null) continue;
					if (!GameData.ItemDatabase.TryGetValue(GetItemId(slot.Item.Name), out var itemData)) continue;
					inventorySlots.Add($"{itemData.ID}:{slot.Count}");
				}
			}
			if (equipment.OffHandContainer != null && IsPortableContainer(equipment.OffHand))
			{
				foreach (var slot in equipment.OffHandContainer.Inventory.Slots)
				{
					if (slot.IsEmpty || slot.Item == null) continue;
					if (!GameData.ItemDatabase.TryGetValue(GetItemId(slot.Item.Name), out var itemData)) continue;
					inventorySlots.Add($"{itemData.ID}:{slot.Count}");
				}
			}
			if (equipment.MainHandContainer != null && IsPortableContainer(equipment.MainHand))
			{
				foreach (var slot in equipment.MainHandContainer.Inventory.Slots)
				{
					if (slot.IsEmpty || slot.Item == null) continue;
					if (!GameData.ItemDatabase.TryGetValue(GetItemId(slot.Item.Name), out var itemData)) continue;
					inventorySlots.Add($"{itemData.ID}:{slot.Count}");
				}
			}
			return string.Join("|", inventorySlots);
		}

		static void ClearCurrentPlayerInventoryForLootbag()
		{
			foreach (var slot in InventoryRenderer.InventorySlots)
			{
				slot.Clear();
			}
			if (equipment.BackpackContainer != null)
			{
				foreach (var slot in equipment.BackpackContainer.Inventory.Slots)
				{
					slot.Clear();
				}
			}
			if (equipment.OffHandContainer != null && IsPortableContainer(equipment.OffHand))
			{
				foreach (var slot in equipment.OffHandContainer.Inventory.Slots)
				{
					slot.Clear();
				}
			}
			if (equipment.MainHandContainer != null && IsPortableContainer(equipment.MainHand))
			{
				foreach (var slot in equipment.MainHandContainer.Inventory.Slots)
				{
					slot.Clear();
				}
			}
		}

		static void RestoreLootbagPayload(string payload)
		{
			if (string.IsNullOrWhiteSpace(payload)) return;
			foreach (var entry in payload.Split('|', StringSplitOptions.RemoveEmptyEntries))
			{
				var parts = entry.Split(':');
				if (parts.Length != 2) continue;
				if (!int.TryParse(parts[0], out int itemId) || !int.TryParse(parts[1], out int count) || count <= 0) continue;
				if (GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
				{
					var item = new Item(itemData.Name, count, itemData.Color, itemData.Icon);
					AddItemToInventory(item, count);
				}
			}
		}

		// Sélecteur de style de cheveux
		private static void DrawHairStylePicker(int x, int y, int itemSize = 40, int spacing = 8, int cols = 5)
		{
			DrawCreationText("Style de cheveux", x, y, 14, Color.White);
			int startX = x;
			int startY = y + 22;

			for (int i = 0; i < hairBaseTextures.Count; i++)
			{
				int col = i % cols;
				int row = i / cols;
				int lastRow = (hairBaseTextures.Count - 1) / cols;
				int btnX = startX + col * (itemSize + spacing);
				int btnY = startY + row * (itemSize + spacing);
				Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
				bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
				bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
				Color bg = i == _tempHairStyle ? new Color(100, 110, 80, 255) : new Color(50, 53, 60, 255);
				UIManager.DrawItemSlotBackground(rect, hover, pressed, bg,
					row == 0, row == lastRow, col == 0, col == cols - 1 || i == hairBaseTextures.Count - 1);

				Texture2D hairTex = i == 0 ? NoneStyleIcon : hairBaseTextures[i];
				if (hairTex.Id != 0)
				{
					float iconScale = i == 0
						? itemSize * 0.65f / Math.Max(hairTex.Width, hairTex.Height)
						: itemSize / (float)hairTex.Width;
					Vector2 iconPos = i == 0
						? new Vector2(btnX + (itemSize - hairTex.Width * iconScale) / 2f, btnY + (itemSize - hairTex.Height * iconScale) / 2f)
						: new Vector2(btnX, btnY);
					if (i > 0 && i < hairBackTextures.Count && hairBackTextures[i].Id != 0)
						Raylib.DrawTextureEx(hairBackTextures[i], iconPos, 0, iconScale, Color.White);
					Raylib.DrawTextureEx(hairTex, iconPos, 0, iconScale, Color.White);
				}
				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					_tempHairStyle = i;
			}
		}

		// Sélecteur de style de barbe
		private static void DrawBeardStylePicker(int x, int y, int itemSize = 40, int spacing = 8, int cols = 5)
		{
			DrawCreationText("Style de barbe", x, y, 14, Color.White);
			int startX = x;
			int startY = y + 22;

			for (int i = 0; i < beardBaseTextures.Count; i++)
			{
				int col = i % cols;
				int row = i / cols;
				int lastRow = (beardBaseTextures.Count - 1) / cols;
				int btnX = startX + col * (itemSize + spacing);
				int btnY = startY + row * (itemSize + spacing);
				Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
				bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
				bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
				Color bg = i == _tempBeardStyle ? new Color(100, 110, 80, 255) : new Color(50, 53, 60, 255);
				UIManager.DrawItemSlotBackground(rect, hover, pressed, bg,
					row == 0, row == lastRow, col == 0, col == cols - 1 || i == beardBaseTextures.Count - 1);

				Texture2D beardTex = i == 0 ? NoneStyleIcon : beardBaseTextures[i];
				if (beardTex.Id != 0)
				{
					float scale = itemSize * (i == 0 ? 0.65f : 1f) / Math.Max(beardTex.Width, beardTex.Height);
					Vector2 iconPos = i == 0
						? new Vector2(btnX + (itemSize - beardTex.Width * scale) / 2f, btnY + (itemSize - beardTex.Height * scale) / 2f)
						: new Vector2(btnX, btnY);
					Raylib.DrawTextureEx(beardTex, iconPos, 0, scale, Color.White);
				}
				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					_tempBeardStyle = i;
			}
		}

		// Sélecteur de style d'yeux
		private static void DrawEyeStylePicker(int x, int y, int itemSize = 40, int spacing = 8, int cols = 5)
		{
			DrawCreationText("Style d'yeux", x, y, 14, Color.White);
			int startX = x;
			int startY = y + 22;

			for (int i = 0; i < EyeBaseTextures.Count; i++)
			{
				int col = i % cols;
				int row = i / cols;
				int lastRow = (EyeBaseTextures.Count - 1) / cols;
				int btnX = startX + col * (itemSize + spacing);
				int btnY = startY + row * (itemSize + spacing);
				Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
				bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
				bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
				Color bg = i == _tempEyeStyle ? new Color(100, 110, 80, 255) : new Color(50, 53, 60, 255);
				UIManager.DrawItemSlotBackground(rect, hover, pressed, bg,
					row == 0, row == lastRow, col == 0, col == cols - 1 || i == EyeBaseTextures.Count - 1);

				Texture2D eyeTex = EyeBaseTextures[i];
				if (eyeTex.Id != 0)
				{
					float scale = itemSize / (float)eyeTex.Width;
					Raylib.DrawTextureEx(eyeTex, new Vector2(btnX, btnY), 0, scale, Color.White);
				}
				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					_tempEyeStyle = i;
			}
		}

		// Dessine le personnage en aperçu avec une orientation donnée
		private static void DrawCharacterPreviewWithRotation(Vector2 pos, Color skin, int hairStyle, int beardStyle, Color hairColor, int eyeStyle, Color eyeColor, float facing)
		{
			Texture2D hairBase = (hairStyle >= 0 && hairStyle < hairBaseTextures.Count) ? hairBaseTextures[hairStyle] : new Texture2D();
			Texture2D hairOverlay = (hairStyle >= 0 && hairStyle < hairOverlayTextures.Count) ? hairOverlayTextures[hairStyle] : new Texture2D();
			Texture2D eyeBase = (eyeStyle >= 0 && eyeStyle < EyeBaseTextures.Count) ? EyeBaseTextures[eyeStyle] : new Texture2D();
			Texture2D eyeOverlay = (eyeStyle >= 0 && eyeStyle < EyeOverlayTextures.Count) ? EyeOverlayTextures[eyeStyle] : new Texture2D();

			Equipment dummyEquipment = new Equipment();

			// Sauvegarder et remplacer temporairement les listes de textures d'yeux
			var oldEyeBase = EyeBaseTextures;
			var oldEyeOverlay = EyeOverlayTextures;
			var oldEyeStyle = Program.EyeStyle;
			var oldEyeColor = Program.EyeColor;

			Program.EyeBaseTextures = new List<Texture2D> { eyeBase };
			Program.EyeOverlayTextures = new List<Texture2D> { eyeOverlay };
			Program.EyeStyle = 0;
			Program.EyeColor = eyeColor;

			// Calculer les offsets de regard locaux pour que l'aperçu suive la souris
			var gaze = ComputeGazeOffsetsFor(pos, Raylib.GetMousePosition(), facing);

			EntityRenderer.DrawEntity(
				speciesName: "human",
				anim: "idle",
				frame: 0,
				prog: 0f,
				facing: facing,
				pos: pos,
				tint: skin,
				hBase: hairBase,
				hOverlay: hairOverlay,
				hColor: hairColor,
				skeletons: SpeciesData.Skeletons,
				eyes: eyeBase,
				mouth: Program.MouthTexture,
				customScale: 1.3f,
				equipment: dummyEquipment,
				isCarrying: false,
				inWater: false,
				attackSwingProgress: 0f,
				heldItemTexture: default,
				headAngle: 0f,
				keepItemHorizontal: false,
				isBow: false,
				underwearTexture: Program.LeafUnderpantsTexture,
				beardStyle: beardStyle
				,
				pupilLeftOffset: gaze.pupilLeft,
				pupilRightOffset: gaze.pupilRight,
				eyebrowLeftOffset: gaze.browLeft,
				eyebrowRightOffset: gaze.browRight
			);

			// Restaurer les listes et la couleur originales
			Program.EyeBaseTextures = oldEyeBase;
			Program.EyeOverlayTextures = oldEyeOverlay;
			Program.EyeStyle = oldEyeStyle;
			Program.EyeColor = oldEyeColor;
		}

		private static bool TryCatchInsectWithNet(Vector2 playerPos, Camera2D camera)
		{
			if (attackCooldown > 0f) return false;
			if (equipment.MainHand == null) return false;
			
			int heldId = GetItemId(equipment.MainHand.Name);
			if (heldId != 5000) return false; // Vérifier si c'est un filet
			
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			Vector2 attackDir = mouseWorld - playerVisualPos;
			if (attackDir.Length() < 0.01f) attackDir = new Vector2(1, 0);
			attackDir = Vector2.Normalize(attackDir);

			Entity? closestInsect = null;
			float bestDistSq = float.MaxValue;
			foreach (var entity in GetEntitiesInAttackCone(playerPos, attackDir, ATTACK_RADIUS, ATTACK_ANGLE))
			{
				var speciesInfo = SpeciesData.GetSpeciesInfo(entity.Species);
				if (speciesInfo == null || !speciesInfo.CanBeCaught) continue;

				float distSq = Vector2.DistanceSquared(playerPos, entity.WorldPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closestInsect = entity;
				}
			}
			
			if (closestInsect != null)
			{
				// Capturer l'insecte s'il est dans la zone d'attaque du filet
				var insectInfo = SpeciesData.GetSpeciesInfo(closestInsect.Species);
				if (insectInfo != null && insectInfo.Drops.Count > 0)
				{
					string? itemKey = null;
					LootDrop? lootDrop = null;
					foreach (var kv in insectInfo.Drops)
					{
						itemKey = kv.Key;
						lootDrop = kv.Value;
						break;
					}
					if (itemKey != null && lootDrop != null && int.TryParse(itemKey, out int itemId))
					{
						int qty = lootDrop.MinQty;
						int remaining = AddItemToInventory(itemId, qty);
						if (remaining == 0)
						{
							AddItemNotification(GameData.ItemDatabase[itemId].Name, qty, GameData.ItemDatabase[itemId].Color);
						}
					}

					closestInsect.SetHealthSilently(0, closestInsect.MaxHP);
					entities.Remove(closestInsect);
					
					Vector2 catchPos = new Vector2(closestInsect.WorldPos.X, closestInsect.WorldPos.Y);
					SpawnCatchParticles(catchPos);
					
					attackAnim = 0.2f;
					isAttacking = true;
					_attackSwingProgress = 0.01f;
					attackCooldown = 0.25f;
					
					return true;
				}
			}
			
			return false;
		}

		// Petite popup modale "Supprimer le monde '<nom>' ?" affichée quand on clique sur
		// l'icône poubelle d'un monde dans le carrousel. Confirme via SaveSystem.DeleteSave.
		static void DrawWorldDeleteConfirmPopup(int sw, int sh, Vector2 mousePos)
		{
			string worldName = _worldPendingDeleteName!;

			Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)0, (byte)150));

			int panelW = 420, panelH = 170;
			int px = (sw - panelW) / 2;
			int py = (sh - panelH) / 2;
			Raylib.DrawRectangleRounded(new Rectangle(px, py, panelW, panelH), 0.1f, 12, new Color(20, 15, 15, 235));
			Raylib.DrawRectangleRoundedLines(new Rectangle(px, py, panelW, panelH), 0.1f, 12, 2, new Color(150, 90, 90, 220));

			string title = Localization.Get("world.delete.title");
			int titleW = FontManager.MeasureText(title, 20);
			FontManager.DrawText(title, px + panelW / 2 - titleW / 2, py + 22, 20, new Color(240, 210, 210, 255));

			string nameLine = worldName;
			int nameW = FontManager.MeasureText(nameLine, 16);
			FontManager.DrawText(nameLine, px + panelW / 2 - nameW / 2, py + 52, 16, new Color(220, 190, 150, 255));

			string warn = Localization.Get("world.delete.warning");
			int warnW = FontManager.MeasureText(warn, 13);
			FontManager.DrawText(warn, px + panelW / 2 - warnW / 2, py + 76, 13, new Color(190, 150, 150, 200));

			int btnW = 150, btnH = 44, btnY = py + panelH - 58;
			Rectangle cancelBtn = new Rectangle(px + panelW / 2 - btnW - 18, btnY, btnW, btnH);
			bool cancelHover = Raylib.CheckCollisionPointRec(mousePos, cancelBtn);
			UIManager.DrawButton(cancelBtn, new Color(105, 118, 132, 255), cancelHover, true);
			string cancelText = Localization.Get("button.cancel");
			int cancelTextW = FontManager.MeasureText(cancelText, 15);
			FontManager.DrawText(cancelText, (int)(cancelBtn.X + (cancelBtn.Width - cancelTextW) / 2f), (int)(cancelBtn.Y + (cancelBtn.Height - 15) / 2f), 15, Color.White);

			Rectangle deleteBtn = new Rectangle(px + panelW / 2 + 18, btnY, btnW, btnH);
			bool deleteHover = Raylib.CheckCollisionPointRec(mousePos, deleteBtn);
			UIManager.DrawButton(deleteBtn, new Color(180, 72, 62, 255), deleteHover, true);
			string deleteText = Localization.Get("button.delete");
			int deleteTextW = FontManager.MeasureText(deleteText, 15);
			FontManager.DrawText(deleteText, (int)(deleteBtn.X + (deleteBtn.Width - deleteTextW) / 2f), (int)(deleteBtn.Y + (deleteBtn.Height - 15) / 2f), 15, Color.White);

			if (cancelHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				_worldPendingDeleteName = null;
			}
			else if (deleteHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				_worldPendingDeleteName = null;
				SaveSystem.DeleteSave(worldName);
				RefreshAvailableSaves();
			}
			else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				_worldPendingDeleteName = null;
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  TOUCHES RÉASSIGNABLES
		// ─────────────────────────────────────────────────────────────────────────
		// Actions de gameplay dont la touche peut être changée depuis l'onglet
		// "Contrôles" du menu Options. Volontairement limité aux actions clavier
		// simples et sans ambiguïté (déplacement, sprint, interagir, inventaire,
		// personnage, lâcher un objet) : les actions à la souris (clic gauche/droit,
		// molette) et les touches fixes (hotbar 1-0, Entrée pour le chat, Echap)
		// restent telles quelles pour l'instant.
		public enum GameAction
		{
			MoveUp,
			MoveDown,
			MoveLeft,
			MoveRight,
			Sprint,
			Interact,
			Inventory,
			Character,
			Drop,
			RadialMenu,      // Nouveau
			ChatToggle,      // Nouveau
			PushToTalk,
			PauseMenu        // Nouveau
		}

		static void LoadAssets(Action<string, float>? onProgress = null)
		{
			// Header stylisé
			PrintBanner("CHARGEMENT DES ASSETS", ConsoleColor.Yellow);
			
			var missingTextures = new List<string>();
			var stats = new LoadStats();
			const int totalSteps = 8;
			void Step(int index, string label)
			{
				onProgress?.Invoke(label, (float)index / totalSteps);
			}
			
			// 1. TEXTURES DE BASE
			_haloTexture = CreateHaloTexture(128);
			missingTexture = TryLoadWithStatus("assets/missing.png", "missing.png", missingTextures, stats);
			Step(1, "Textures de base...");
			
			// 2. INTERFACE & TUILES
			LoadGuiTextures(missingTextures, stats);
			WorldTileRegistry.LoadTextures(tileTextures, missingTexture);
			World.LoadBannerTextures();
			WorldTileRegistry.LoadDecorations(missingTexture);
			WorldTileRegistry.LoadConnectionTextures(missingTexture);
			LoadBiomeBorderTextures();
			PrintInfo($"Tuiles chargees : {WorldTileRegistry.Tiles.Count}");
			Step(2, "Interface et tuiles...");
			
			// 3. PERSONNAGES
			LoadCharacterTextures(missingTextures, stats);
			Step(3, "Personnages...");
			
			// 4. EQUIPEMENTS
			LoadEquipmentTextures(missingTextures);
			Step(4, "Équipements...");
			
			// 5. JEU & UI
			LoadGameUiAssets(missingTextures);
			
			HudRenderer.LoadTextures();
			Step(5, "Interface de jeu...");
			
			// 6. ITEMS
			LoadItems(missingTextures, stats);
			Step(6, "Objets...");
			
			// 7. CREATURES
			LoadAnimals(missingTextures, stats);
			Step(7, "Créatures...");
			
			// 8. VEHICULES
			LoadCarTextures();
			Console.WriteLine();
			Step(8, "Véhicules...");
			
			// RESUME FINAL
			PrintSummary(missingTextures, stats);
		}

		private static void LoadEquipmentTextures(List<string> missingTextures)
		{
			LeafUnderpantsTexture = TryLoad("assets/equipements/leaf_underpants.png");
		}

		private static void DrawCarPedals(Car car)
		{
			if (PlayerCurrentCar == null) return;
			if (_pedalTexture.Id == 0 || _pedalPushTexture.Id == 0) return;

			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			
			// Taille des pédales (identique à la texture)
			float pedalSize = 160f;
			float spacing = 210f;
			float bottomMargin = 64f;
			
			// Position des pédales (centrées en bas de l'écran)
			float centerX = sw / 2f;
			float centerY = sh - bottomMargin;
			
			// Pédale de frein (à gauche)
			float brakeX = centerX - pedalSize - spacing;
			float brakeY = centerY - pedalSize / 2f;
			
			// Pédale d'accélération (à droite)
			float accelX = centerX + spacing;
			float accelY = centerY - pedalSize / 2f;
			
			// États des touches
			bool isAccelerating = KeyBindings.IsDown(GameAction.MoveUp) || Raylib.IsKeyDown(KeyboardKey.Up);
			bool isBraking = KeyBindings.IsDown(GameAction.MoveDown) || Raylib.IsKeyDown(KeyboardKey.Down);
			
			// Dessin de la pédale d'accélération
			Texture2D accelTex = isAccelerating ? _pedalPushTexture : _pedalTexture;
			DrawPedal(accelTex, accelX, accelY, pedalSize);
			
			// Dessin de la pédale de frein
			Texture2D brakeTex = isBraking ? _pedalPushTexture : _pedalTexture;
			DrawPedal(brakeTex, brakeX, brakeY, pedalSize);
		}

		private static void LoadItems(List<string> missingTextures, LoadStats stats)
		{
			var items = GameData.ItemDatabase.Values.ToList();
			int loaded = items.Count(i => GameData.HasItemTexture(i));
			int missing = items.Count - loaded;
			
			if (missing > 0)
			{
				foreach (var item in items.Where(i => !GameData.HasItemTexture(i)))
				{
					if (!string.IsNullOrEmpty(item.TextureName))
						missingTextures.Add($"assets/items/{item.TextureName}.png");
					else
						missingTextures.Add($"assets/items/unknown_{item.ID}.png");
				}
			}
			
			if (missing != 0)
				PrintWarning($"Objets : {loaded} charges, {missing} manquants");
			
			stats.Success += loaded;
			stats.Warnings += missing;
		}

		static void HandleDig(Vector2 mousePos, Camera2D cam)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mousePos, cam);
			int ts = Program.TileSize;
			(int tileX, int tileY, float dist, int objectId) = FindTileUnderMouse(mouseWorld, ts);
			if (tileX == -1 || tileY == -1) return;
			string key = $"{tileX}_{tileY}";
			int objId = World.GetObjectIdAt(tileX, tileY);
			if (objId != 0)
			{
				int itemId = 0;
				foreach (var kv in GameData.ItemDatabase)
					if (kv.Value.PlaceableId == objId) { itemId = kv.Key; break; }
				if (itemId != 0)
				{
					World.RemovePlacedObject(tileX, tileY);
					if (objId == 8)
					{
						World.UpdateRoofsAt(tileX, tileY);
					}
					Program.GiveItemToPlayer(itemId, 1, new Vector2(tileX * ts + ts/2f, tileY * ts + (World.GetHeightAt(tileX, tileY) * -ts / 4) + ts/2f));
					activeNotifications.Add(new Notification("Objet récupéré", new Color(0, 255, 255, 255), 1.5f));
				}
				return;
			}
			if (equipment.HeldTool == ToolType.Shovel)
			{
				int tileId = World.GetTileIdAt(tileX, tileY);
				var drops = WorldTileRegistry.GetDrops(tileId);
				if (drops.Count > 0)
					foreach (var (id, qty) in drops) { AddItemToInventory(id, qty); if (GameData.ItemDatabase.TryGetValue(id, out var idata)) AddItemNotification(idata.Name, qty, idata.Color); }
			}
		}

		// Version 1 : Ajouter un item par ID (sans couleur personnalisée)
		public static int AddItemToInventory(int id, int count, bool silent = false)
		{
			if (!GameData.ItemDatabase.TryGetValue(id, out var data))
				return count;

			int remaining = count;
			var incomingItem = new Item(data.Name, count, data.Color, data.Icon);
			var allSlots = GetAllInventorySlots().ToList();

			foreach (var slot in allSlots)
			{
				if (remaining <= 0) break;
				if (!slot.IsEmpty && slot.Item != null && AreItemsStackable(slot.Item, incomingItem))
				{
					int add = Math.Min(data.StackSize - slot.Count, remaining);
					if (add > 0)
					{
						slot.Count += add;
						remaining -= add;
					}
				}
			}

			foreach (var slot in allSlots)
			{
				if (remaining <= 0) break;
				if (slot.IsEmpty)
				{
					slot.Item = new Item(data.Name, 0, data.Color, data.Icon, null);
					slot.Count = Math.Min(remaining, data.StackSize);
					remaining -= slot.Count;
				}
			}

			int addedCount = count - remaining;
			if (addedCount > 0)
			{
				MarkItemAsPreviouslyOwned(data.Name);
				if (CraftingUI.IsOpen)
					CraftingUI.RefreshRecipeList();
			}
			if (!silent && addedCount > 0)
			{
				PlayItemPickupSound();
				AddItemNotification(data.Name, addedCount, data.Color);
			}

			return remaining;
		}

		public static void MergeSpoilMeta(Item targetItem, Item sourceItem, int sourceAddedCount)
        {
            if (targetItem == null || sourceItem == null || sourceAddedCount <= 0) return;

            bool hasTarget = TryParseMetaPair(targetItem.GetMeta("spoil", ""), out float targetCur, out float targetMax);
            bool hasSource = TryParseMetaPair(sourceItem.GetMeta("spoil", ""), out float sourceCur, out float sourceMax);

            if (!hasTarget && !hasSource) return;

            if (!hasTarget)
            {
                targetMax = hasSource ? sourceMax : 100f;
                targetCur = targetMax;
            }
            if (!hasSource)
            {
                sourceMax = targetMax > 0f ? targetMax : 100f;
                sourceCur = sourceMax;
            }
            if (targetMax <= 0f) targetMax = 100f;
            if (sourceMax <= 0f) sourceMax = targetMax;

            if (Math.Abs(targetMax - sourceMax) > 0.001f)
            {
                float sourceRatio = sourceCur / sourceMax;
                sourceCur = sourceRatio * targetMax;
            }

            float existingCount = targetItem.Count;
            float totalCount = existingCount + sourceAddedCount;
            if (totalCount <= 0f) return;

            float existingRatio = targetCur / targetMax;
            float sourceRatioFinal = sourceCur / targetMax;
            float combinedRatio = (existingRatio * existingCount + sourceRatioFinal * sourceAddedCount) / totalCount;
            float newCur = MathF.Max(0f, MathF.Min(combinedRatio * targetMax, targetMax));

            targetItem.SetMeta("spoil", FormatMetaPair(newCur, targetMax));
            if (newCur <= 0f)
                targetItem.SetMeta("spoiled", "1");
            else
                targetItem.RemoveMeta("spoiled");
        }

		public static bool AreItemsStackable(Item slotItem, Item incomingItem)
        {
            if (slotItem == null || incomingItem == null) return false;
            if (!string.Equals(slotItem.Name, incomingItem.Name, StringComparison.OrdinalIgnoreCase)) return false;

            var slotColors = slotItem.CustomColors ?? new List<Color?>();
            var incomingColors = incomingItem.CustomColors ?? new List<Color?>();
            if (!AreColorsEqualNormalized(slotColors, incomingColors)) return false;

            if (!AreMetadataEqualExceptSpoil(slotItem.Metadata, incomingItem.Metadata)) return false;
            if (slotItem.Container != null || incomingItem.Container != null) return false;
            if (slotItem.Backpack != null || incomingItem.Backpack != null) return false;

            var slotMeta = slotItem.Meta ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var incomingMeta = incomingItem.Meta ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!AreMetaEqualExceptSpoil(slotMeta, incomingMeta)) return false;

            return true;
        }

        private static bool AreColorsEqualNormalized(List<Color?> a, List<Color?> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] == null && b[i] == null) continue;
                if (a[i] == null || b[i] == null) return false;
                if (!a[i].Value.Equals(b[i].Value)) return false;
            }
            return true;
        }

		// Parcourt TOUS les slots d'inventaire (ceinture + sac à dos + panier)
        public static IEnumerable<InventorySlot> GetAllInventorySlots()
        {
            // 1. Ceinture
            foreach (var slot in InventoryRenderer.InventorySlots)
                yield return slot;

            // 2. Sac à dos équipé
            if (equipment.BackpackContainer != null)
            {
                foreach (var slot in equipment.BackpackContainer.Inventory.Slots)
                    yield return slot;
            }

            // 3. Panier équipé (main gauche)
			if (equipment.OffHandContainer != null && IsPortableContainer(equipment.OffHand))
			{
				foreach (var slot in equipment.OffHandContainer.Inventory.Slots)
					yield return slot;
			}

			// 4. Panier équipé (main droite)
			if (equipment.MainHandContainer != null && IsPortableContainer(equipment.MainHand))
			{
				foreach (var slot in equipment.MainHandContainer.Inventory.Slots)
					yield return slot;
			}
        }

		// Retourne la quantité totale d'un item dans TOUS les conteneurs
        public static int GetTotalItemCount(string itemName)
		{
			int total = 0;
			foreach (var slot in GetAllInventorySlots())
			{
				if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == itemName)
					total += slot.Count;
			}
			return total;
		}

		// Retire des items de TOUS les conteneurs (commence par la ceinture, puis sac, puis panier)
		public static void RemoveItemFromAllInventories(string itemName, int count)
		{
			int remaining = count;
			foreach (var slot in GetAllInventorySlots())
			{
				if (remaining <= 0) break;
				if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == itemName)
				{
					int take = Math.Min(remaining, slot.Count);
					slot.Count -= take;
					remaining -= take;
					if (slot.Count <= 0) slot.Clear();
				}
			}
		}

		public static void GiveItemToPlayer(int id, int count, Vector2 sourceWorldPos, List<Color?>? customColors = null, Dictionary<string, string>? meta = null, string? metadata = null, float? pickupCooldown = null)
        {
			if (count <= 0) return;
			if (!GameData.ItemDatabase.TryGetValue(id, out var itemData)) return;
			DropCustomItemOnGround(sourceWorldPos, id, count, customColors, null, 0f, metadata, meta, pickupCooldown: pickupCooldown);
        }

		private static void GiveQuestItemToPlayer(int id, int count, Vector2 sourceWorldPos)
		{
			if (count <= 0) return;

			int remaining = AddItemToInventory(id, count);
			if (remaining > 0)
				GiveItemToPlayer(id, remaining, sourceWorldPos);
		}

		public static void DropItemOnGround(Vector2 worldPos, int itemId, int count)
        {
            // Réutilise le chemin réseau-aware de DropCustomItemOnGround (host autoritaire,
            // client délègue via une requête) au lieu de dupliquer la logique.
            DropCustomItemOnGroundWithColors(worldPos, itemId, count, (List<Color?>?)null);
        }

		static void RemoveItemFromInventory(string name, int count)
        {
            int rem = count;
			foreach (var slot in GetAllInventorySlots())
            {
                if (!slot.IsEmpty && slot.Item!=null && slot.Item.Name == name)
                {
                    int take = Math.Min(rem, slot.Count);
                    slot.Count -= take;
                    rem -= take;
                    if (slot.Count<=0)
                    {
                        //  Mettre à jour equipment.MainHand si c'était l'item en main
                        if (equipment.MainHand == slot.Item)
                            equipment.MainHand = null;
                        slot.Clear();
                    }
                    if (rem<=0) return;
                }
            }
        }

		public static bool ConsumeItemFromInventory(Item item, int count)
		{
			int remaining = count;
			foreach (var slot in GetAllInventorySlots())
			{
				if (remaining <= 0) break;
				if (slot.IsEmpty || slot.Item == null || slot.Item.Name != item.Name) continue;

				bool sameColor = (slot.Item.CustomColor == null && item.CustomColor == null) ||
					(slot.Item.CustomColor != null && item.CustomColor != null &&
					 slot.Item.CustomColor.Value.Equals(item.CustomColor.Value));
				if (!sameColor) continue;

				int take = Math.Min(remaining, slot.Count);
				slot.Count -= take;
				remaining -= take;
				if (slot.Count <= 0) slot.Clear();
			}

			return remaining == 0;
		}

		public static int GetItemCountInInventoryById(int itemId)
		{
			return GetItemCountInInventory(itemId);
		}

		static int GetItemCountInInventory(int itemId)
		{
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var data)) return 0;
			int total = 0;
				foreach (var slot in GetAllInventorySlots())
				if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == data.Name) total += slot.Count;
			return total;
		}

		public static bool ConsumeItemFromInventoryById(int itemId, int count)
		{
			return RemoveItemFromInventoryById(itemId, count);
		}

		static bool RemoveItemFromInventoryById(int itemId, int count)
		{
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var data)) return false;
			int toRemove = count;
			bool removed = false;
				foreach (var slot in GetAllInventorySlots())
			{
				if (toRemove <= 0) break;
				if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == data.Name)
				{
					int take = Math.Min(toRemove, slot.Count);
					slot.Count -= take;
					toRemove -= take;
					if (slot.Count <= 0)
					{
						//  Mettre à jour equipment.MainHand si c'était l'item en main
						if (equipment.MainHand == slot.Item)
							equipment.MainHand = null;
						slot.Clear();
					}
					removed = true;
				}
			}
			return removed;
		}

		static Item MakeItem(string key, int count)
		{
			if (!GameData.TryGetItemByKey(key, out var itemData))
				throw new InvalidOperationException($"Item de départ introuvable: {key}");

			return new Item(itemData.Name, count, itemData.Color, itemData.Icon);
		}

		public static void AddNotification(Notification n)
	{
		if (n.StackCount > 1 && !string.IsNullOrEmpty(n.Message)) AddItemNotification(n.Message, n.StackCount, n.TextColor);
		else activeNotifications.Add(n);
	}
    }
}