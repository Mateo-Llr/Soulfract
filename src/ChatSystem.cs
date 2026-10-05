// ChatSystem.cs - Interface de chat moderne avec historique défilable, positionnée en bas à gauche
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class ChatSystem
    {
        private static bool _isOpen = false;
        private static float _panelOpacity = 0f;
        private const float PANEL_FADE_SPEED = 5f;
        private static string _inputText = "";
        private static List<ChatMessage> _messages = new List<ChatMessage>();
        private static float _messageLifetime = 10f; // seulement pour l'affichage flottant, mais l'historique reste
        private static int _maxMessages = 200; // limite de l'historique

        // Paramètres de l'interface
        private const int PANEL_WIDTH = 420;
        private const int PANEL_HEIGHT = 320;
        private const int TITLE_HEIGHT = 30;
        private const int INPUT_HEIGHT = 32;
        private const int PADDING = 10;
        private const int LINE_HEIGHT = 20;
        private const int FONT_SIZE = 15;
        private const int SCROLLBAR_WIDTH = 6;
        private const int MESSAGE_GAP = 5;

        // Défilement
        private static int _scrollOffset = 0;
        private static int _maxScroll = 0;
        private static bool _isScrolledToBottom = true;

        // Couleurs
        private static readonly Color COLOR_BG = new Color(12, 16, 24, 238);
        private static readonly Color COLOR_TITLE_BG = new Color(20, 27, 39, 245);
        private static readonly Color COLOR_TITLE_TEXT = new Color(236, 218, 164, 255);
        private static readonly Color COLOR_INPUT_BG = new Color(22, 29, 42, 255);
        private static readonly Color COLOR_INPUT_TEXT = new Color(240, 243, 248, 255);
        private static readonly Color COLOR_SCROLLBAR_TRACK = new Color(255, 255, 255, 18);
        private static readonly Color COLOR_SCROLLBAR_THUMB = new Color(236, 200, 112, 180);
        private static readonly Color COLOR_MESSAGE_META = new Color(150, 165, 185, 180);
        private static readonly Color COLOR_MESSAGE_DEFAULT = new Color(255, 255, 255, 255);
        private static readonly Color COLOR_MESSAGE_COMMAND = new Color(255, 200, 100, 255);
        private static readonly Color COLOR_MESSAGE_ERROR = new Color(255, 100, 100, 255);
        private static readonly Color COLOR_MESSAGE_SUCCESS = new Color(100, 255, 100, 255);
        private static readonly Color COLOR_MESSAGE_NETWORK = new Color(150, 210, 255, 255);
        private static readonly List<ChatDisplayLine> _displayLines = new();
        private static int _displayLineWidth = -1;
        private static int _displayMessageCount = -1;

        private readonly struct ChatDisplayLine
        {
            public readonly string Text;
            public readonly Color Color;
            public readonly bool StartsMessage;

            public ChatDisplayLine(string text, Color color, bool startsMessage)
            {
                Text = text;
                Color = color;
                StartsMessage = startsMessage;
            }
        }

        private static bool _ignoreNextChar = false;

		public static bool IsOpen => _isOpen;

		public static void Toggle()
		{
			_isOpen = !_isOpen;
            if (_isOpen)
                _panelOpacity = 1f;
			if (!_isOpen)
			{
				_inputText = "";
				_ignoreNextChar = false;
                TextInput.Reset("chat");
			}
			else
			{
                TextInput.Reset();
                TextInput.Focus("chat", 0);
				_ignoreNextChar = true;  // Ignorer le caractère qui a ouvert le chat
			}
		}

		public static void Update()
		{
            if (!_isOpen)
            {
                _panelOpacity = Math.Max(0f, _panelOpacity - Raylib.GetFrameTime() * PANEL_FADE_SPEED);
                return;
            }

            _panelOpacity = 1f;

            Rectangle inputRect = GetInputRect();
            if (_ignoreNextChar)
            {
                while (Raylib.GetCharPressed() != 0) { }
                _ignoreNextChar = false;
            }
            TextInput.Update(ref _inputText, "chat", inputRect, 100);

            if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                SubmitCommand();
                _isOpen = false;
                _inputText = "";
                _ignoreNextChar = false;
                TextInput.Reset("chat");
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                _isOpen = false;
                _inputText = "";
                _ignoreNextChar = false;
                TextInput.Reset("chat");
                return;
            }

			// Gestion de la molette pour le défilement
			Vector2 mousePos = Raylib.GetMousePosition();
			Rectangle panelRect = GetPanelRect();
			if (Raylib.CheckCollisionPointRec(mousePos, panelRect))
			{
				float wheel = Raylib.GetMouseWheelMove();
				if (wheel != 0)
				{
					int step = (int)(wheel * LINE_HEIGHT * 2);
					_scrollOffset -= step;
					ClampScroll();
					_isScrolledToBottom = (_scrollOffset >= _maxScroll - 10);
				}
			}
		}

        private static Rectangle GetInputRect()
        {
            Rectangle panel = GetPanelRect();
            return new Rectangle(panel.X + PADDING, panel.Y + panel.Height - INPUT_HEIGHT - PADDING, panel.Width - PADDING * 2, INPUT_HEIGHT);
        }

        private static void SubmitCommand()
        {
            if (string.IsNullOrWhiteSpace(_inputText)) return;
            string command = _inputText.Trim();
            if (command.StartsWith("/"))
            {
                ExecuteCommand(command);
            }
            else
            {
                AddMessage(new ChatMessage(command, COLOR_MESSAGE_DEFAULT, false));
                //  MULTIJOUEUR : seuls les messages de chat (pas les commandes /xxx,
                // qui restent strictement locales) sont envoyés aux autres joueurs.
                if (NetworkManager.IsOnline)
                {
                    NetworkManager.SendChat(Program.LocalPlayerName, command);
                }
            }
            _isScrolledToBottom = true;
            _scrollOffset = _maxScroll;
        }

        private static void ExecuteCommand(string command)
        {
            string[] parts = command.Split(' ');
            string cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "/give":
                    GiveItem(parts);
                    break;
                case "/help":
                    ShowHelp();
                    break;
                case "/clear":
                    ClearInventory();
                    break;
                case "/heal":
                    HealPlayer();
                    break;
                case "/tp":
                    Teleport(parts);
                    break;
                case "/time":
                    if (parts.Length >= 2 && parts[1].Equals("set", StringComparison.OrdinalIgnoreCase))
                        SetTime(parts);
                    else
                        ShowTime();
                    break;
                case "/weather":
                    SetWeather(parts);
                    break;
                case "/seed":
                    ShowSeed();
                    break;
                case "/pos":
                    ShowPosition();
                    break;
                case "/spawn":
                    if (!RequireHost()) break;
                    SpawnEntity(parts);
                    break;
                case "/findhome":
                    FindHome();
                    break;
                case "/listhouses":
                    ListHouses();
                    break;
                case "/spawnhouse":
                    if (!RequireHost()) break;
                    SpawnHouse();
                    break;
                case "/noclip":
                    ToggleNoClip();
                    break;
				case "/destroy":
					if (!RequireHost()) break;
					if (parts.Length > 1)
					{
						string filterSpecies = parts[1].ToLower();
						DestroyAllEntities(filterSpecies);
					}
					else
					{
						DestroyAllEntities();
					}
					break;
                case "/godmode":
                    Program.ToggleGodMode();
                    break;
                case "/items":
                    if (Program.IsGodMode)
                        ItemMenuUI.Open();
                    else
                        AddMessage(new ChatMessage(" Commande disponible uniquement en mode Godmode.", COLOR_MESSAGE_ERROR, true));
                    break;
                case "/refreshhouses":
                    World.RefreshAllHouses();
                    AddMessage(new ChatMessage($" {World.Houses.Count} maisons détectées", COLOR_MESSAGE_SUCCESS, true));
                    break;
                case "/rebuildroofs":
                    if (!RequireHost()) break;
                    World.RebuildAllRoofs();
                    AddMessage(new ChatMessage($" Toits régénérés pour {World.Houses.Count} maisons", COLOR_MESSAGE_SUCCESS, true));
                    break;
                case "/morph":
                    if (parts.Length < 2)
                    {
                        AddMessage(new ChatMessage("Usage: /morph <espèce>", COLOR_MESSAGE_ERROR, true));
                        return;
                    }
                    string species = parts[1].ToLower();
                    if (SpeciesData.Species.ContainsKey(species))
                    {
                        Program.SetPlayerMorphSpecies(species);
                        AddMessage(new ChatMessage($" Morphé en {species} !", COLOR_MESSAGE_SUCCESS, true));
                    }
                    else
                    {
                        AddMessage(new ChatMessage($" Espèce '{species}' inconnue.", COLOR_MESSAGE_ERROR, true));
                    }
                    break;
                case "/check":
                    Vector2 playerPos = Program.GetPlayerPosition();
                    int tx = (int)(playerPos.X / Program.TileSize);
                    int ty = (int)(playerPos.Y / Program.TileSize);
                    int objId = World.GetObjectIdAt(tx, ty);
                    AddMessage(new ChatMessage($"Tile ({tx},{ty}) contient l'objet ID {objId}", COLOR_MESSAGE_COMMAND, true));
                    break;
                case "/play":
                    if (parts.Length < 2)
                    {
                        AddMessage(new ChatMessage("Usage: /play <nom_musique>", COLOR_MESSAGE_ERROR, true));
                        return;
                    }
                    string musicName = parts[1];
                    string musicPath = $"assets/musics/{musicName}.json";
                    if (MusicPlayer.LoadMusic(musicPath))
                    {
                        MusicPlayer.Play();
                        AddMessage(new ChatMessage($" Lecture : {musicName}", COLOR_MESSAGE_SUCCESS, true));
                    }
                    else
                    {
                        AddMessage(new ChatMessage($" Impossible de charger {musicName}", COLOR_MESSAGE_ERROR, true));
                    }
                    break;
                case "/stopmusic":
                    MusicPlayer.Stop();
                    AddMessage(new ChatMessage("⏹ Musique arrêtée", COLOR_MESSAGE_COMMAND, true));
                    break;
                case "/pausemusic":
                    MusicPlayer.Pause();
                    AddMessage(new ChatMessage("⏸ Musique en pause", COLOR_MESSAGE_COMMAND, true));
                    break;
                case "/resumemusic":
                    MusicPlayer.Resume();
                    AddMessage(new ChatMessage("▶ Musique reprise", COLOR_MESSAGE_COMMAND, true));
                    break;
                default:
                    AddMessage(new ChatMessage($"Commande inconnue: {cmd}. Tapez /help pour la liste.", COLOR_MESSAGE_ERROR, true));
                    break;
            }
        }

        // ----------------------------------------------------------------------
        // Commandes
        // ----------------------------------------------------------------------

        private static void GiveItem(string[] parts)
        {
            if (parts.Length < 2)
            {
                AddMessage(new ChatMessage("Usage: /give <ID|nom> [quantité] ou /give <nom>[recipe=X] [quantité]", COLOR_MESSAGE_ERROR, true));
                return;
            }

            string itemPart = parts[1];
            int itemId = 0;
            int recipeId = -1;

            if (!TryParseItemToken(itemPart, out itemId, out recipeId))
            {
                AddMessage(new ChatMessage($"Objet introuvable: {itemPart}", COLOR_MESSAGE_ERROR, true));
                return;
            }

            int quantity = 1;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out quantity))
            {
                AddMessage(new ChatMessage($"Quantité invalide: {parts[2]}", COLOR_MESSAGE_ERROR, true));
                return;
            }
            if (quantity <= 0)
            {
                AddMessage(new ChatMessage("La quantité doit être supérieure à 0", COLOR_MESSAGE_ERROR, true));
                return;
            }

            string itemKey = GameData.GetItemKey(itemId);
            if (string.Equals(itemKey, "recipe_scroll", StringComparison.OrdinalIgnoreCase) && recipeId != -1)
            {
                GiveRecipeItemById(recipeId, quantity);
                return;
            }

            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
            {
                AddMessage(new ChatMessage($"Item '{itemPart}' introuvable", COLOR_MESSAGE_ERROR, true));
                return;
            }

            int remaining = quantity;
            int maxStack = itemData.StackSize;
            var incomingItem = new Item(itemData.Name, quantity, itemData.Color, itemData.Icon);

            foreach (var slot in InventoryRenderer.InventorySlots)
            {
                if (remaining <= 0) break;
                if (!slot.IsEmpty && slot.Item != null && Program.AreItemsStackable(slot.Item, incomingItem))
                {
                    int space = maxStack - slot.Count;
                    if (space > 0)
                    {
                        int add = Math.Min(space, remaining);
                        slot.Count += add;
                        remaining -= add;
                    }
                }
            }

            while (remaining > 0)
            {
                bool added = false;
                foreach (var slot in InventoryRenderer.InventorySlots)
                {
                    if (slot.IsEmpty)
                    {
                        slot.Item = new Item(itemData.Name, 0, itemData.Color, itemData.Icon);
                        slot.Count = Math.Min(remaining, maxStack);
                        remaining -= slot.Count;
                        added = true;
                        break;
                    }
                }
                if (!added) break;
            }

            if (remaining > 0)
            {
                AddMessage(new ChatMessage($"Inventaire plein! {remaining} {itemData.Name} non ajoutés.", COLOR_MESSAGE_ERROR, true));
            }
            else
            {
                AddMessage(new ChatMessage($"+{quantity} {itemData.Name} (ID: {itemId})", COLOR_MESSAGE_SUCCESS, true));
                Program.AddItemNotification(itemData.Name, quantity, itemData.Color);
            }
        }
		
		//  MULTIJOUEUR : certaines commandes mutent directement des listes qui ne sont
		// autoritaires que côté host (Program.entities est toujours vide côté client, et les
		// chunks/toits d'un client ne sont jamais renvoyés au host). Les exécuter depuis un
		// client ne ferait donc rien d'utile (ou pire, désynchroniserait silencieusement sa
		// propre vue du monde). On les réserve donc au host (et au mode solo).
		private static bool RequireHost()
		{
			if (NetworkManager.IsClient)
			{
				AddMessage(new ChatMessage(" Cette commande n'est disponible que pour l'hôte de la partie.", COLOR_MESSAGE_ERROR, true));
				return false;
			}
			return true;
		}

		private static void DestroyAllEntities(string targetSpecies = "")
		{
			var entities = Program.GetEntities();
			int count = 0;
			
			for (int i = entities.Count - 1; i >= 0; i--)
			{
				var entity = entities[i];
				
				if (entity.IsPlayer) continue;
				
				// Si un filtre d'espèce est spécifié, ne supprimer que cette espèce
				if (!string.IsNullOrEmpty(targetSpecies) && !entity.Species.Equals(targetSpecies, StringComparison.OrdinalIgnoreCase))
					continue;
				
				entities.RemoveAt(i);
				count++;
			}
			
			// Nettoyer les chunks de surface
			var chunks = World.GetAllSurfaceChunks();
			foreach (var (chunkX, chunkY, chunkData) in chunks)
			{
				for (int i = chunkData.Entities.Count - 1; i >= 0; i--)
				{
					var entity = chunkData.Entities[i];
					if (entity.IsPlayer) continue;
					if (!string.IsNullOrEmpty(targetSpecies) && !entity.Species.Equals(targetSpecies, StringComparison.OrdinalIgnoreCase))
						continue;
					chunkData.Entities.RemoveAt(i);
				}
			}
			
			// Nettoyer les chunks de grottes
			var caveChunks = World.GetAllCaveChunks();
			foreach (var (chunkX, chunkY, chunkData) in caveChunks)
			{
				for (int i = chunkData.Entities.Count - 1; i >= 0; i--)
				{
					var entity = chunkData.Entities[i];
					if (entity.IsPlayer) continue;
					if (!string.IsNullOrEmpty(targetSpecies) && !entity.Species.Equals(targetSpecies, StringComparison.OrdinalIgnoreCase))
						continue;
					chunkData.Entities.RemoveAt(i);
				}
			}
			
			string entityType = string.IsNullOrEmpty(targetSpecies) ? "entités" : targetSpecies;
			AddMessage(new ChatMessage($" {count} {entityType} détruites !", COLOR_MESSAGE_SUCCESS, true));
			Program.AddNotification(new Notification($" {count} {entityType} supprimées", new Color(255, 100, 100, 255), 3f));
		}

        private static bool TryParseItemToken(string itemPart, out int itemId, out int recipeId)
        {
            itemId = 0;
            recipeId = -1;

            if (string.IsNullOrWhiteSpace(itemPart))
                return false;

            string token = itemPart.Trim();
            string identifier = token;

            if (token.Contains('[') && token.Contains(']'))
            {
                int bracketStart = token.IndexOf('[');
                int bracketEnd = token.IndexOf(']');
                identifier = token.Substring(0, bracketStart).Trim();

                string paramsPart = token.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                foreach (var pair in paramsPart.Split(','))
                {
                    string[] keyValue = pair.Split('=', 2);
                    if (keyValue.Length != 2)
                        continue;

                    string key = keyValue[0].Trim().ToLower();
                    string value = keyValue[1].Trim();
                    if (key == "recipe")
                    {
                        if (int.TryParse(value, out int parsedRecipeId))
                        {
                            recipeId = parsedRecipeId;
                        }
                        else if (TryResolveRecipeReference(value, out int referencedRecipeId))
                        {
                            recipeId = referencedRecipeId;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            if (int.TryParse(identifier, out int parsedId) && GameData.ItemDatabase.ContainsKey(parsedId))
            {
                itemId = parsedId;
                return true;
            }

            if (GameData.TryGetItemByName(identifier, out var itemByName))
            {
                itemId = itemByName.ID;
                return true;
            }

            if (GameData.TryGetItemByKey(identifier, out var itemByKey))
            {
                itemId = itemByKey.ID;
                return true;
            }

            return false;
        }

        private static bool TryResolveRecipeReference(string value, out int recipeId)
        {
            recipeId = -1;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (int.TryParse(value, out int parsedRecipeId))
            {
                recipeId = parsedRecipeId;
                return true;
            }

            var recipe = GameData.Recipes.FirstOrDefault(r =>
                string.Equals(r.ResultName, value, StringComparison.OrdinalIgnoreCase) ||
                (GameData.ItemDatabase.TryGetValue(r.ResultId, out var resultData) &&
                 (string.Equals(resultData.Name, value, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(resultData.Key, value, StringComparison.OrdinalIgnoreCase))));

            if (recipe != null)
            {
                recipeId = recipe.Id;
                return true;
            }

            return false;
        }

        private static void GiveRecipeItemById(int recipeId, int quantity = 1)
        {
            if (quantity > 64) quantity = 64;
            if (quantity < 1) quantity = 1;

            var allRecipes = GameData.Recipes;
            var targetRecipe = allRecipes.FirstOrDefault(r => r.Id == recipeId);

            if (targetRecipe == null)
            {
                AddMessage(new ChatMessage($" Recette ID {recipeId} introuvable", COLOR_MESSAGE_ERROR, true));
                var cauldronRecipes = allRecipes.Where(r => r.RequiresStation == "cauldron").Take(5);
                var availableIds = cauldronRecipes.Select(r => $"{r.ResultId}({r.ResultName})");
                AddMessage(new ChatMessage($" Recettes chaudron: {string.Join(", ", availableIds)}", COLOR_MESSAGE_COMMAND, true));
                return;
            }

            if (GameData.ItemDatabase.TryGetValue(212, out var itemData))
            {
                int remaining = quantity;
                int maxStack = itemData.StackSize;

                foreach (var slot in InventoryRenderer.InventorySlots)
                {
                    if (remaining <= 0) break;
                    if (slot.IsEmpty)
                    {
                        var newItem = new Item(itemData.Name, 0, itemData.Color, itemData.Icon);
                        newItem.SetMeta("recipeId", targetRecipe.Id.ToString());
                        slot.Item = newItem;
                        slot.Count = Math.Min(remaining, maxStack);
                        remaining -= slot.Count;
                        break;
                    }
                }

                string stationType = GetStationDisplayName(targetRecipe.RequiresStation);
                AddMessage(new ChatMessage($" +{quantity} Recette: {targetRecipe.ResultName} ({stationType})", COLOR_MESSAGE_SUCCESS, true));
                Program.AddItemNotification($"Recette: {targetRecipe.ResultName}", quantity, new Color(210, 180, 100, 255));
            }
            else
            {
                AddMessage(new ChatMessage($" Item Recette (ID 212) introuvable", COLOR_MESSAGE_ERROR, true));
            }
        }

        private static string GetStationDisplayName(string station)
        {
            return station switch
            {
                "cauldron" => " Chaudron",
                "workbench" => " Établi",
                "anvil" => " Enclume",
                "furnace" => " Fourneau",
                "" => " Basique",
                _ => station
            };
        }

        private static void ShowHelp()
        {
            AddMessage(new ChatMessage("--- COMMANDES DISPONIBLES ---", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/give <ID> [qty] - Ajoute un item", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/give 212[recipe=100] 1 - Donne une recette spécifique", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/clear - Vide l'inventaire", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/heal - Soigne le joueur", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/tp <x> <y> - Téléportation", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/time - Affiche le temps de jeu", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/time set <day|noon|night|midnight|valeur> - Change l'heure (valeur en secondes)", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/weather <rain|storm|clear> - Change la météo", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/seed - Affiche la seed du monde", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/pos - Affiche la position", COLOR_MESSAGE_COMMAND, true));
            AddMessage(new ChatMessage("/help - Affiche cette aide", COLOR_MESSAGE_COMMAND, true));
        }

        private static void ClearInventory()
        {
            foreach (var slot in InventoryRenderer.InventorySlots)
            {
                slot.Clear();
            }
            AddMessage(new ChatMessage("Inventaire vidé !", COLOR_MESSAGE_SUCCESS, true));
        }

        private static void HealPlayer()
        {
            Program.playerHP = Program.playerMaxHP;
            AddMessage(new ChatMessage("Joueur soigné !", COLOR_MESSAGE_SUCCESS, true));
            Program.AddNotification(new Notification(" Soigné !", new Color(100, 255, 100, 255), 2f));
        }

        private static void SetWeather(string[] parts)
        {
            if (!RequireHost()) return;

            if (parts.Length < 2)
            {
                AddMessage(new ChatMessage("Usage: /weather <rain|storm|clear>", COLOR_MESSAGE_ERROR, true));
                return;
            }

            string weatherArg = parts[1];
            if (Weather.SetWeather(weatherArg))
            {
                string message = Weather.Current switch
                {
                    WeatherType.Rain => " Il commence à pleuvoir.",
                    WeatherType.Storm => " L'orage éclate : vent violent, pluie battante et éclairs !",
                    _ => " Le ciel se dégage."
                };
                string notification = Weather.Current switch
                {
                    WeatherType.Rain => " Pluie",
                    WeatherType.Storm => " Tempête",
                    _ => " Temps clair"
                };
                Color notificationColor = Weather.Current == WeatherType.Storm
                    ? new Color(175, 190, 225, 255)
                    : Weather.Current == WeatherType.Rain
                        ? new Color(150, 180, 220, 255)
                        : new Color(255, 220, 100, 255);
                AddMessage(new ChatMessage(message, COLOR_MESSAGE_SUCCESS, true));
                Program.AddNotification(new Notification(notification, notificationColor, 2f));
            }
            else
            {
                AddMessage(new ChatMessage("Usage: /weather <rain|storm|clear>", COLOR_MESSAGE_ERROR, true));
            }
        }

        private static void Teleport(string[] parts)
        {
            if (parts.Length < 3)
            {
                AddMessage(new ChatMessage("Usage: /tp <x> <y>", COLOR_MESSAGE_ERROR, true));
                return;
            }
            if (!float.TryParse(parts[1], out float x) || !float.TryParse(parts[2], out float y))
            {
                AddMessage(new ChatMessage("Coordonnées invalides", COLOR_MESSAGE_ERROR, true));
                return;
            }
            Program.SetPlayerPosition(new Vector2(x, y));
            AddMessage(new ChatMessage($"Téléporté vers ({x:F0}, {y:F0})", COLOR_MESSAGE_SUCCESS, true));
        }

        private static void ShowTime()
        {
            AddMessage(new ChatMessage($"Temps de jeu: {Program.GetPlayTime():F1} secondes", COLOR_MESSAGE_COMMAND, true));
        }

        private static void SetTime(string[] parts)
        {
            if (!RequireHost()) return;
            if (parts.Length < 3)
            {
                AddMessage(new ChatMessage("Usage: /time set <day|noon|night|midnight|valeur>", COLOR_MESSAGE_ERROR, true));
                return;
            }

            string value = parts[2].ToLowerInvariant();
            float gameTime = value switch
            {
                "day" => 150f,
                "noon" => 300f,
                "night" => 450f,
                "midnight" => 0f,
                _ => float.NaN
            };

            if (float.IsNaN(gameTime) && !float.TryParse(parts[2], out gameTime))
            {
                AddMessage(new ChatMessage($"Heure invalide: {parts[2]}", COLOR_MESSAGE_ERROR, true));
                return;
            }

            Program.SetGameTime(gameTime);
            AddMessage(new ChatMessage($"Heure réglée sur {parts[2]}.", COLOR_MESSAGE_SUCCESS, true));
        }

        private static void ShowSeed()
        {
            AddMessage(new ChatMessage($"Seed du monde: {Program.WorldSeed}", COLOR_MESSAGE_COMMAND, true));
        }

        private static void ShowPosition()
        {
            var pos = Program.GetPlayerPosition();
            AddMessage(new ChatMessage($"Position: ({pos.X:F0}, {pos.Y:F0})", COLOR_MESSAGE_COMMAND, true));
        }

        private static void SpawnEntity(string[] parts)
        {
            if (parts.Length < 2)
            {
                AddMessage(new ChatMessage("Usage: /spawn <nom_entité> [quantité]", COLOR_MESSAGE_ERROR, true));
                return;
            }

            string speciesName = parts[1].ToLower();
            int quantity = 1;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out quantity))
            {
                AddMessage(new ChatMessage($"Quantité invalide: {parts[2]}", COLOR_MESSAGE_ERROR, true));
                return;
            }
            if (quantity <= 0)
            {
                AddMessage(new ChatMessage($"La quantité doit être supérieure à 0", COLOR_MESSAGE_ERROR, true));
                return;
            }
            if (quantity > 50)
            {
                AddMessage(new ChatMessage($"Quantité max: 50 (vous avez demandé {quantity})", COLOR_MESSAGE_ERROR, true));
                return;
            }

            var speciesInfo = SpeciesData.GetSpeciesInfo(speciesName);
            if (speciesInfo == null)
            {
                AddMessage(new ChatMessage($"Espèce '{speciesName}' inconnue.", COLOR_MESSAGE_ERROR, true));
                return;
            }

            Vector2 playerPos = Program.GetPlayerPosition();
            int successCount = 0;
            bool firstEntityIsTrader = false;
            bool hasAssignedHome = false;
            Vector2 homePositionFound = Vector2.Zero;

            for (int q = 0; q < quantity; q++)
            {
                float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 30f;
                float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 30f;
                Vector2 spawnPos = new Vector2(playerPos.X + offsetX, playerPos.Y + offsetY);

                int entityTileX = (int)(spawnPos.X / Program.TileSize);
                int entityTileY = (int)(spawnPos.Y / Program.TileSize);
                int groundIdAtPos = World.GetGroundTileIdAt(entityTileX, entityTileY);
                if (groundIdAtPos == 10)
                {
                    AddMessage(new ChatMessage($"Position de spawn ({entityTileX},{entityTileY}) dans l'eau — spawn ignoré.", COLOR_MESSAGE_ERROR, true));
                    continue;
                }

                Entity newEntity = new Entity(spawnPos, speciesName, false);

                if (speciesName == "human" && !newEntity.IsTamed)
                {
                    Random rand = new Random();
                    try
                    {
                        if (rand.NextDouble() < 0.1)
                        {
                            newEntity.IsTrader = true;
                            newEntity.InitTraderItems();
                            if (q == 0) firstEntityIsTrader = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($" Erreur lors de l'initialisation du commerçant : {ex}");
                        newEntity.IsTrader = false;
                    }
                }

                newEntity.IsTamed = false;
                newEntity.CustomName = null;

                if (speciesName == "human" && !newEntity.IsTamed)
                {
                    if (!hasAssignedHome)
                    {
                        if (World.Houses.Count == 0)
                            World.RefreshAllHouses();

                        homePositionFound = World.FindNearestFreeHouse(playerPos);
                        hasAssignedHome = true;
                    }

                    if (homePositionFound != Vector2.Zero)
                    {
                        newEntity.HomePosition = homePositionFound;
                        if (q == 0)
                        {
                            int homeTileX = (int)(homePositionFound.X / Program.TileSize);
                            int homeTileY = (int)(homePositionFound.Y / Program.TileSize);
                            AddMessage(new ChatMessage($" Maison assignée à ({homeTileX}, {homeTileY})", COLOR_MESSAGE_COMMAND, true));
                        }
                    }
                    else if (q == 0)
                    {
                        AddMessage(new ChatMessage($" Aucune maison libre trouvée.", COLOR_MESSAGE_ERROR, true));
                        newEntity.HomePosition = Vector2.Zero;
                    }
                }
                else
                {
                    newEntity.HomePosition = Vector2.Zero;
                }

                Program.GetEntities().Add(newEntity);

                var chunk = World.GetChunkAt(entityTileX, entityTileY);
                if (chunk != null)
                {
                    chunk.Entities.Add(newEntity);
                }

                successCount++;
            }

            if (successCount > 0)
            {
                string traderMsg = firstEntityIsTrader ? " (dont 1 commerçant)" : "";
                AddMessage(new ChatMessage($" {successCount} {speciesInfo.Name}(s) spawné(s) à vos pieds !{traderMsg}", COLOR_MESSAGE_SUCCESS, true));
            }
        }

        private static void FindHome()
        {
            Vector2 playerPos = Program.GetPlayerPosition();
            Vector2 nearestHome = World.FindNearestFreeHouse(playerPos, 2400f);
            if (nearestHome != Vector2.Zero)
            {
                int tileX = (int)(nearestHome.X / Program.TileSize);
                int tileY = (int)(nearestHome.Y / Program.TileSize);
                AddMessage(new ChatMessage($" Maison libre la plus proche : ({tileX}, {tileY})", COLOR_MESSAGE_COMMAND, true));
            }
            else
            {
                AddMessage(new ChatMessage($" Aucune maison libre trouvée.", COLOR_MESSAGE_ERROR, true));
            }
        }

        private static void ListHouses()
        {
            if (World.Houses.Count == 0)
            {
                return;
            }
            foreach (var house in World.Houses.Values)
            {
                string status = house.IsOccupied ? " occupée" : " libre";
            }
        }

        private static void SpawnHouse()
        {
            Vector2 playerPos = Program.GetPlayerPosition();
            int tileX = (int)(playerPos.X / Program.TileSize);
            int tileY = (int)(playerPos.Y / Program.TileSize);

            var structures = StructureManager.GetStructures();
            if (structures.Count == 0)
            {
                return;
            }

            var house = structures[0];
            int width = house.MaxX - house.MinX + 1;
            int height = house.MaxY - house.MinY + 1;
            int posX = tileX - width / 2;
            int posY = tileY - height / 2;

            var chunk = World.GetChunkAt(tileX, tileY);
            if (chunk == null)
            {
                AddMessage(new ChatMessage(" Chunk non chargé", COLOR_MESSAGE_ERROR, true));
                return;
            }

            Random rand = new Random();
            if (StructureManager.TryPlaceStructure(house, posX, posY, chunk, rand, (float)Program.GetGameTime(), out var doorPos, out var interiorPos))
            {
                int buildingId = World.RegisterHouse(doorPos.x, doorPos.y, interiorPos);
            }
        }

        private static void ToggleNoClip()
        {
            Program.ToggleNoClip();
            string status = Program.NoClipEnabled ? "activé" : "désactivé";
            AddMessage(new ChatMessage($" NoClip {status} !", COLOR_MESSAGE_SUCCESS, true));
        }

        // ----------------------------------------------------------------------
        // Gestion des messages et du rendu
        // ----------------------------------------------------------------------

        private static void AddMessage(ChatMessage message)
        {
            _messages.Add(message);
            message.StartTime = (float)Raylib.GetTime();

            while (_messages.Count > _maxMessages)
            {
                _messages.RemoveAt(0);
            }

            if (_isScrolledToBottom)
            {
                _scrollOffset = _maxScroll;
            }
            RecalculateMaxScroll();
        }

        //  MULTIJOUEUR : affiche un message de chat reçu d'un autre joueur (appelé par
        // NetworkManager). Préfixe toujours avec le pseudo, contrairement aux messages
        // locaux qui restent bruts.
        public static void AddNetworkMessage(string playerName, string text)
        {
            AddMessage(new ChatMessage($"{playerName}: {text}", COLOR_MESSAGE_NETWORK, false));
            _isScrolledToBottom = true;
            _scrollOffset = _maxScroll;
        }

        private static void RecalculateMaxScroll()
        {
            int visibleHeight = PANEL_HEIGHT - TITLE_HEIGHT - INPUT_HEIGHT - PADDING * 2;
            RebuildDisplayLines(visibleHeight);
            int totalHeight = Math.Max(0, _displayLines.Count * LINE_HEIGHT + MESSAGE_GAP);
            _maxScroll = Math.Max(0, totalHeight - visibleHeight);
            if (_isScrolledToBottom)
            {
                _scrollOffset = _maxScroll;
            }
            else
            {
                ClampScroll();
            }
        }

        private static void RebuildDisplayLines(int availableHeight)
        {
            int width = PANEL_WIDTH - PADDING * 2 - SCROLLBAR_WIDTH - 12;
            if (_displayLineWidth == width && _displayMessageCount == _messages.Count)
                return;

            _displayLines.Clear();
            _displayLineWidth = width;
            _displayMessageCount = _messages.Count;
            foreach (var message in _messages)
            {
                var lines = WrapMessage(message.Text, width);
                for (int index = 0; index < lines.Count; index++)
                    _displayLines.Add(new ChatDisplayLine(lines[index], message.Color, index == 0));
                _displayLines.Add(new ChatDisplayLine(string.Empty, COLOR_MESSAGE_META, false));
            }
        }

        private static List<string> WrapMessage(string text, int maxWidth)
        {
            var result = new List<string>();
            string[] paragraphs = text.Replace("\r", string.Empty).Split('\n');
            foreach (string paragraph in paragraphs)
            {
                if (paragraph.Length == 0)
                {
                    result.Add(string.Empty);
                    continue;
                }

                string current = string.Empty;
                foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (FontManager.MeasureText(candidate, FONT_SIZE) <= maxWidth)
                    {
                        current = candidate;
                        continue;
                    }

                    if (current.Length > 0)
                        result.Add(current);

                    current = word;
                    while (FontManager.MeasureText(current, FONT_SIZE) > maxWidth && current.Length > 1)
                    {
                        int split = current.Length;
                        while (split > 1 && FontManager.MeasureText(current[..split], FONT_SIZE) > maxWidth)
                            split--;
                        result.Add(current[..split]);
                        current = current[split..];
                    }
                }
                if (current.Length > 0)
                    result.Add(current);
            }
            return result.Count == 0 ? new List<string> { string.Empty } : result;
        }

        private static void ClampScroll()
        {
            _scrollOffset = Math.Clamp(_scrollOffset, 0, _maxScroll);
        }

        private static Rectangle GetPanelRect()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            Rectangle hotbar = HudRenderer.GetClassicHotbarBounds();
            int x = 20;
            int bottomMargin = 12;
            int y = Math.Min(sh - PANEL_HEIGHT - 20, (int)hotbar.Y - PANEL_HEIGHT - bottomMargin);
            return new Rectangle(x, y, PANEL_WIDTH, PANEL_HEIGHT);
        }

        public static void Draw()
        {
            float currentTime = (float)Raylib.GetTime();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            // On ne supprime pas les messages de l'historique, mais on les garde tous
            // (la limite _maxMessages est gérée dans AddMessage)
            // On ne les supprime pas ici pour l'historique

            if (_panelOpacity > 0f)
            {
                DrawChatPanel(currentTime, _panelOpacity);
            }

            string hint = "T : Chat";
            int hintWidth = FontManager.MeasureText(hint, 12);
            FontManager.DrawText(hint, sw - hintWidth - 10, sh - 25, 12, new Color(100, 100, 100, 120));
        }

        private static void DrawChatPanel(float currentTime, float opacity)
        {
            Rectangle panelRect = GetPanelRect();
            int x = (int)panelRect.X;
            int y = (int)panelRect.Y;
            int w = (int)panelRect.Width;
            int h = (int)panelRect.Height;

            // Surface flottante sans contour dur : l'ombre donne la separation avec le jeu.
            Raylib.DrawRectangleRounded(new Rectangle(x + 5, y + 7, w, h), 0.08f, 12, WithOpacity(new Color(0, 0, 0, 105), opacity));
            Raylib.DrawRectangleRounded(panelRect, 0.15f, 8, WithOpacity(COLOR_BG, opacity));

            // Barre de titre
            Rectangle titleRect = new Rectangle(x, y, w, TITLE_HEIGHT);
            Raylib.DrawRectangleRounded(titleRect, 0.15f, 8, WithOpacity(COLOR_TITLE_BG, opacity));
            FontManager.DrawText("CHAT", x + PADDING, y + 7, 13, WithOpacity(COLOR_TITLE_TEXT, opacity));
            FontManager.DrawText("MESSAGES", x + PADDING + 54, y + 8, 10, WithOpacity(COLOR_MESSAGE_META, opacity));

            // Bouton fermer
            int closeSize = 20;
            Rectangle closeBtn = new Rectangle(x + w - closeSize - 6, y + 5, closeSize, closeSize);
            bool hoverClose = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), closeBtn);
            Raylib.DrawCircle((int)(closeBtn.X + closeSize / 2f), (int)(closeBtn.Y + closeSize / 2f), 4,
                WithOpacity(hoverClose ? new Color(255, 115, 105, 255) : new Color(180, 105, 105, 210), opacity));
            if (_isOpen && hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isOpen = false;
                _inputText = "";
                return;
            }

            // Zone des messages
            int messagesY = y + TITLE_HEIGHT + PADDING;
            int messagesHeight = h - TITLE_HEIGHT - INPUT_HEIGHT - PADDING * 2;
            Rectangle messagesRect = new Rectangle(x + PADDING, messagesY, w - PADDING * 2 - SCROLLBAR_WIDTH - 4, messagesHeight);

            Raylib.BeginScissorMode((int)messagesRect.X, (int)messagesRect.Y, (int)messagesRect.Width, (int)messagesRect.Height);

            RebuildDisplayLines(messagesHeight);
            int startIndex = Math.Max(0, _scrollOffset / LINE_HEIGHT);
            int endIndex = Math.Min(_displayLines.Count, startIndex + (messagesHeight / LINE_HEIGHT) + 2);

            for (int i = startIndex; i < endIndex; i++)
            {
                int lineY = messagesY + i * LINE_HEIGHT - _scrollOffset;
                if (lineY + LINE_HEIGHT < messagesY || lineY > messagesY + messagesHeight) continue;
                var line = _displayLines[i];
                if (line.Text.Length > 0)
                    FontManager.DrawText(line.Text, (int)messagesRect.X + 4, lineY + 2, FONT_SIZE, WithOpacity(line.Color, opacity));
            }

            Raylib.EndScissorMode();

            // Barre de défilement
            if (_maxScroll > 0)
            {
                int scrollbarX = x + w - SCROLLBAR_WIDTH - PADDING;
                int scrollbarY = messagesY;
                int scrollbarHeight = messagesHeight;
                float thumbHeight = Math.Max(20, (float)messagesHeight / Math.Max(1, _displayLines.Count * LINE_HEIGHT) * messagesHeight);
                float thumbY = scrollbarY + (float)_scrollOffset / _maxScroll * (scrollbarHeight - thumbHeight);

                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, scrollbarHeight), 0.3f, 6, WithOpacity(COLOR_SCROLLBAR_TRACK, opacity));
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, thumbY, SCROLLBAR_WIDTH, thumbHeight), 0.3f, 6, WithOpacity(COLOR_SCROLLBAR_THUMB, opacity));
            }

            // Barre de saisie
            int inputY = y + h - INPUT_HEIGHT - PADDING;
            Rectangle inputRect = new Rectangle(x + PADDING, inputY, w - PADDING * 2, INPUT_HEIGHT);
            Raylib.DrawRectangleRounded(inputRect, 0.15f, 8, WithOpacity(COLOR_INPUT_BG, opacity));

            TextInput.DrawSingleLine(_inputText, "chat", inputRect, FONT_SIZE, WithOpacity(COLOR_INPUT_TEXT, opacity),
                WithOpacity(new Color(90, 110, 150, 220), opacity), WithOpacity(COLOR_TITLE_TEXT, opacity), 6, prefix: "> ");

            string hint = "ENTRÉE : envoyer | ESC : fermer";
            int hintWidth = FontManager.MeasureText(hint, 10);
            FontManager.DrawText(hint, x + w - hintWidth - PADDING, inputY - 14, 10, WithOpacity(new Color(150, 150, 150, 180), opacity));
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            return new Color(color.R, color.G, color.B, (byte)Math.Clamp(color.A * opacity, 0f, 255f));
        }

        private static void DrawFloatingMessages(float currentTime)
        {
            int messageY = (int)HudRenderer.GetClassicHotbarBounds().Y - 12;
            Vector2 mousePos = Raylib.GetMousePosition();
            const int maxVisibleLines = 5;
            int renderedLines = 0;

            // Zone de survol : englobe la zone où s'affichent les messages flottants.
            // Si la souris est dessus, on "gèle" le compte à rebours de disparition
            // pour laisser le temps de lire/répondre.
            Rectangle hoverZone = new Rectangle(8, messageY - maxVisibleLines * LINE_HEIGHT - 6, 420, maxVisibleLines * LINE_HEIGHT + 16);
            bool isHovering = Raylib.CheckCollisionPointRec(mousePos, hoverZone);
            float dt = Raylib.GetFrameTime();

            for (int i = _messages.Count - 1; i >= 0 && renderedLines < maxVisibleLines; i--)
            {
                var msg = _messages[i];
                float age = currentTime - msg.StartTime;
                if (age < 0f || age > _messageLifetime)
                    continue;

                // Reset du timer : tant que la souris survole le tchat, on repousse
                // l'heure de départ du message pour empêcher le fondu de progresser.
                if (isHovering)
                {
                    msg.StartTime += dt;
                    age = currentTime - msg.StartTime;
                }

                // Petit fondu d'entrée pour éviter un "pop" trop brutal
                float fadeIn = Math.Clamp(age / 0.15f, 0f, 1f);
                // Fondu de sortie sur la dernière seconde de vie du message
                float fadeOut = Math.Clamp(1f - (age / (_messageLifetime - 1f)), 0f, 1f);
                float alpha = Math.Min(fadeIn, fadeOut);

                Color color = new Color(msg.Color.R, msg.Color.G, msg.Color.B, (byte)(alpha * 255));
                var lines = WrapMessage(msg.Text, 400);
                for (int lineIndex = lines.Count - 1; lineIndex >= 0 && renderedLines < maxVisibleLines; lineIndex--)
                {
                    string line = lines[lineIndex];
                    int lineWidth = FontManager.MeasureText(line, FONT_SIZE);
                    Raylib.DrawRectangleRounded(new Rectangle(7, messageY - 2, lineWidth + 14, LINE_HEIGHT + 4),
                        0.25f, 6, new Color(12, 16, 24, (int)Math.Clamp(alpha * 145f, 0f, 145f)));
                    FontManager.DrawText(line, 14, messageY, FONT_SIZE, color);
                    messageY -= LINE_HEIGHT;
                    renderedLines++;
                }
                messageY -= 3;
            }
        }

        public static void AddNotificationMessage(string message, Color color)
        {
            AddMessage(new ChatMessage(message, color, true));
        }
    }

    public class ChatMessage
    {
        public string Text;
        public Color Color;
        public float StartTime;
        public bool IsCommand;

        public ChatMessage(string text, Color color, bool isCommand = false)
        {
            Text = text;
            Color = color;
            IsCommand = isCommand;
            StartTime = 0;
        }
    }
}