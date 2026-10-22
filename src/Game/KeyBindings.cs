#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {
		public static class KeyBindings
		{
			private static readonly Dictionary<GameAction, List<InputBinding>> _defaults = new()
			{
				{ GameAction.MoveUp, new() { new(InputType.Keyboard, (int)KeyboardKey.W) } },
				{ GameAction.MoveDown, new() { new(InputType.Keyboard, (int)KeyboardKey.S) } },
				{ GameAction.MoveLeft, new() { new(InputType.Keyboard, (int)KeyboardKey.A) } },
				{ GameAction.MoveRight, new() { new(InputType.Keyboard, (int)KeyboardKey.D) } },
				{ GameAction.Sprint, new() { new(InputType.Keyboard, (int)KeyboardKey.LeftShift) } },
				{ GameAction.Interact, new() { new(InputType.Keyboard, (int)KeyboardKey.E) } },
				{ GameAction.Inventory, new() { new(InputType.Keyboard, (int)KeyboardKey.I) } },
				{ GameAction.Character, new() { new(InputType.Keyboard, (int)KeyboardKey.C) } },
				{ GameAction.Drop, new() { new(InputType.Keyboard, (int)KeyboardKey.F) } },
				{ GameAction.RadialMenu, new() { new(InputType.Mouse, (int)MouseButton.Middle) } },
				{ GameAction.ChatToggle, new() { new(InputType.Keyboard, (int)KeyboardKey.T) } },
				{ GameAction.PushToTalk, new() { new(InputType.Keyboard, (int)KeyboardKey.V) } },
				{ GameAction.PauseMenu, new() { new(InputType.Keyboard, (int)KeyboardKey.Escape) } },
				{ GameAction.MagicMenu, new() { new(InputType.Keyboard, (int)KeyboardKey.J) } },
				{ GameAction.GuildMenu, new() { new(InputType.Keyboard, (int)KeyboardKey.G) } },
				{ GameAction.Map, new() { new(InputType.Keyboard, (int)KeyboardKey.M) } },
				{ GameAction.Bestiary, new() { new(InputType.Keyboard, (int)KeyboardKey.Y) } },
				{ GameAction.QuickSave, new() { new(InputType.Keyboard, (int)KeyboardKey.F5) } },
				{ GameAction.ReloadData, new() { new(InputType.Keyboard, (int)KeyboardKey.F4) } },
				{ GameAction.ToggleDebug, new() { new(InputType.Keyboard, (int)KeyboardKey.F3) } },
				{ GameAction.TimeSkip, new() { new(InputType.Keyboard, (int)KeyboardKey.F6) } },
				{ GameAction.SelectStructureCorner1, new() { new(InputType.Keyboard, (int)KeyboardKey.F7) } },
				{ GameAction.SelectStructureCorner2, new() { new(InputType.Keyboard, (int)KeyboardKey.F8) } },
				{ GameAction.Fullscreen, new() { new(InputType.Keyboard, (int)KeyboardKey.F11) } },
				{ GameAction.ItemsMenu, new() { new(InputType.Keyboard, (int)KeyboardKey.K) } },
				{ GameAction.HotbarSlot1, new() { new(InputType.Keyboard, (int)KeyboardKey.One) } },
				{ GameAction.HotbarSlot2, new() { new(InputType.Keyboard, (int)KeyboardKey.Two) } },
				{ GameAction.HotbarSlot3, new() { new(InputType.Keyboard, (int)KeyboardKey.Three) } },
				{ GameAction.HotbarSlot4, new() { new(InputType.Keyboard, (int)KeyboardKey.Four) } },
				{ GameAction.HotbarSlot5, new() { new(InputType.Keyboard, (int)KeyboardKey.Five) } },
				{ GameAction.HotbarSlot6, new() { new(InputType.Keyboard, (int)KeyboardKey.Six) } },
				{ GameAction.HotbarSlot7, new() { new(InputType.Keyboard, (int)KeyboardKey.Seven) } },
				{ GameAction.HotbarSlot8, new() { new(InputType.Keyboard, (int)KeyboardKey.Eight) } },
				{ GameAction.HotbarSlot9, new() { new(InputType.Keyboard, (int)KeyboardKey.Nine) } },
				{ GameAction.HotbarSlot10, new() { new(InputType.Keyboard, (int)KeyboardKey.Zero) } },
				{ GameAction.EmoteWheel, new() { new(InputType.Keyboard, (int)KeyboardKey.B) } },
				{ GameAction.ArmorStandPose, new() { new(InputType.Keyboard, (int)KeyboardKey.R) } },
				{ GameAction.WagonLink, new() { new(InputType.Keyboard, (int)KeyboardKey.R) } },
				{ GameAction.BoatRow, new() { new(InputType.Keyboard, (int)KeyboardKey.G) } },
				{ GameAction.CarMode, new() { new(InputType.Keyboard, (int)KeyboardKey.R) } },
			};

			public static Dictionary<GameAction, List<InputBinding>> Current = CloneBindings(_defaults);

			public static Dictionary<GameAction, List<InputBinding>> CloneCurrent() => CloneBindings(Current);

			public static void Apply(Dictionary<GameAction, List<InputBinding>> bindings)
			{
				Current = CloneBindings(bindings);
				Save();
			}

			public static void Restore(Dictionary<GameAction, List<InputBinding>> bindings) => Current = CloneBindings(bindings);

			public static bool HasChanges(Dictionary<GameAction, List<InputBinding>> original)
			{
				if (original.Count != Current.Count) return true;
				return Current.Any(pair => !original.TryGetValue(pair.Key, out var bindings) || !bindings.SequenceEqual(pair.Value));
			}

			private const string SavePath = "Data/keybindings.json";
			private const float GamepadAxisDeadzone = 0.15f;
			private const float GamepadAxisCaptureThreshold = 0.5f;
			private static readonly Dictionary<int, bool> _gamepadAxisPressedStates = new();
			private static readonly Dictionary<int, bool> _gamepadAxisReleasedStates = new();

			public static readonly GamepadButton[] CapturableGamepadButtons =
			{
				GamepadButton.LeftFaceUp, GamepadButton.LeftFaceDown,
				GamepadButton.LeftFaceLeft, GamepadButton.LeftFaceRight,
				GamepadButton.RightFaceUp, GamepadButton.RightFaceDown,
				GamepadButton.RightFaceLeft, GamepadButton.RightFaceRight,
				GamepadButton.LeftTrigger1, GamepadButton.LeftTrigger2,
				GamepadButton.RightTrigger1, GamepadButton.RightTrigger2,
				GamepadButton.MiddleLeft, GamepadButton.Middle,
				GamepadButton.MiddleRight, GamepadButton.LeftThumb,
				GamepadButton.RightThumb
			};

			public static bool IsDown(GameAction action)
			{
				return Current[action].Any(IsDown);
			}

			public static bool IsPressed(GameAction action)
			{
				return Current[action].Any(IsPressed);
			}

			public static bool IsReleased(GameAction action)
			{
				return Current[action].Any(IsReleased);
			}

			public static bool IsHotbarSlotPressed(int slot)
			{
				if (slot < 0 || slot >= 10)
					throw new ArgumentOutOfRangeException(nameof(slot));

				return IsPressed((GameAction)((int)GameAction.HotbarSlot1 + slot));
			}

			private static bool IsDown(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
					return Raylib.IsKeyDown((KeyboardKey)binding.Code);
				if (binding.Type == InputType.Mouse)
					return Raylib.IsMouseButtonDown((MouseButton)binding.Code);
				if (binding.Type == InputType.GamepadAxis)
					return TryGetPrimaryGamepadIndex(out int axisIndex) && IsGamepadAxisDirectionDown(axisIndex, (GamepadAxisDirection)binding.Code, GamepadAxisDeadzone);
				return TryGetPrimaryGamepadIndex(out int index) && Raylib.IsGamepadButtonDown(index, (GamepadButton)binding.Code);
			}

			private static bool IsPressed(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
					return Raylib.IsKeyPressed((KeyboardKey)binding.Code);
				if (binding.Type == InputType.Mouse)
					return Raylib.IsMouseButtonPressed((MouseButton)binding.Code);
				if (binding.Type == InputType.GamepadAxis)
				{
					bool isDown = TryGetAvailableGamepadIndex(out int axisIndex) && IsGamepadAxisDirectionDown(axisIndex, (GamepadAxisDirection)binding.Code, GamepadAxisDeadzone);
					bool wasDown = _gamepadAxisPressedStates.TryGetValue(binding.Code, out bool previous) && previous;
					_gamepadAxisPressedStates[binding.Code] = isDown;
					return isDown && !wasDown;
				}
				return TryGetPrimaryGamepadIndex(out int index) && Raylib.IsGamepadButtonPressed(index, (GamepadButton)binding.Code);
			}

			private static bool IsReleased(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
					return Raylib.IsKeyReleased((KeyboardKey)binding.Code);
				if (binding.Type == InputType.Mouse)
					return Raylib.IsMouseButtonReleased((MouseButton)binding.Code);
				if (binding.Type == InputType.GamepadAxis)
				{
					bool isDown = TryGetAvailableGamepadIndex(out int axisIndex) && IsGamepadAxisDirectionDown(axisIndex, (GamepadAxisDirection)binding.Code, GamepadAxisDeadzone);
					bool wasDown = _gamepadAxisReleasedStates.TryGetValue(binding.Code, out bool previous) && previous;
					_gamepadAxisReleasedStates[binding.Code] = isDown;
					return wasDown && !isDown;
				}
				return TryGetPrimaryGamepadIndex(out int index) && Raylib.IsGamepadButtonReleased(index, (GamepadButton)binding.Code);
			}

			private static bool IsGamepadAxisDirectionDown(int gamepadIndex, GamepadAxisDirection direction, float threshold)
			{
				float movement = direction switch
				{
					GamepadAxisDirection.LeftXNegative or GamepadAxisDirection.LeftXPositive => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftX),
					GamepadAxisDirection.LeftYNegative or GamepadAxisDirection.LeftYPositive => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftY),
					GamepadAxisDirection.RightXNegative or GamepadAxisDirection.RightXPositive => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightX),
					_ => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightY)
				};
				return direction switch
				{
					GamepadAxisDirection.LeftXNegative or GamepadAxisDirection.LeftYNegative or GamepadAxisDirection.RightXNegative or GamepadAxisDirection.RightYNegative => movement < -threshold,
					_ => movement > threshold
				};
			}

			private static bool TryGetAvailableGamepadIndex(out int gamepadIndex)
			{
				for (int index = 0; index < 4; index++)
				{
					if (Raylib.IsGamepadAvailable(index))
					{
						gamepadIndex = index;
						return true;
					}
				}
				gamepadIndex = -1;
				return false;
			}

			public static bool TryGetGamepadAxisDirection(int gamepadIndex, out GamepadAxisDirection direction)
			{
				direction = default;
				float axisX = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftX);
				float axisY = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftY);
				float rightAxisX = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightX);
				float rightAxisY = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightY);
				float strongestAxis = MathF.Max(MathF.Max(MathF.Abs(axisX), MathF.Abs(axisY)), MathF.Max(MathF.Abs(rightAxisX), MathF.Abs(rightAxisY)));
				if (strongestAxis < GamepadAxisCaptureThreshold)
					return false;
				if (strongestAxis == MathF.Abs(axisX))
				{
					direction = axisX < 0 ? GamepadAxisDirection.LeftXNegative : GamepadAxisDirection.LeftXPositive;
					return true;
				}
				if (strongestAxis == MathF.Abs(axisY))
				{
					direction = axisY < 0 ? GamepadAxisDirection.LeftYNegative : GamepadAxisDirection.LeftYPositive;
					return true;
				}
				if (strongestAxis == MathF.Abs(rightAxisX))
				{
					direction = rightAxisX < 0 ? GamepadAxisDirection.RightXNegative : GamepadAxisDirection.RightXPositive;
					return true;
				}
				direction = rightAxisY < 0 ? GamepadAxisDirection.RightYNegative : GamepadAxisDirection.RightYPositive;
				return true;
			}

			private static Dictionary<GameAction, List<InputBinding>> CloneBindings(Dictionary<GameAction, List<InputBinding>> source) =>
				source.ToDictionary(kv => kv.Key, kv => new List<InputBinding>(kv.Value));

			public static IReadOnlyList<InputBinding> GetBindings(GameAction action) => Current[action];

			public static string DisplayName(GameAction action)
			{
				int hotbarSlot = (int)action - (int)GameAction.HotbarSlot1;
				if (hotbarSlot >= 0 && hotbarSlot < 10)
					return Localization.GetOrDefault($"controls.hotbar_slot_{hotbarSlot + 1}",
						hotbarSlot == 9 ? "Emplacement 0 de la barre d'objets" : $"Emplacement {hotbarSlot + 1} de la barre d'objets");

				return action switch
				{
					GameAction.MoveUp => Localization.GetOrDefault("controls.move_up", "Avancer"),
					GameAction.MoveDown => Localization.GetOrDefault("controls.move_down", "Reculer"),
					GameAction.MoveLeft => Localization.GetOrDefault("controls.move_left", "Aller à gauche"),
					GameAction.MoveRight => Localization.GetOrDefault("controls.move_right", "Aller à droite"),
					GameAction.Sprint => Localization.GetOrDefault("controls.sprint", "Sprinter"),
					GameAction.Interact => Localization.GetOrDefault("controls.interact", "Interagir"),
					GameAction.Inventory => Localization.GetOrDefault("controls.inventory", "Inventaire"),
					GameAction.Character => Localization.GetOrDefault("controls.character", "Fiche de personnage"),
					GameAction.Drop => Localization.GetOrDefault("controls.drop", "Ramasser un meuble"),
					GameAction.RadialMenu => Localization.GetOrDefault("controls.radial_menu", "Menu radial"),
					GameAction.ChatToggle => Localization.GetOrDefault("controls.chat_toggle", "Chat"),
					GameAction.PushToTalk => Localization.GetOrDefault("controls.push_to_talk", "Push to talk"),
					GameAction.PauseMenu => Localization.GetOrDefault("controls.pause_menu", "Pause / Retour"),
					GameAction.MagicMenu => Localization.GetOrDefault("controls.magic_menu", "Menu de magie"),
					GameAction.GuildMenu => Localization.GetOrDefault("controls.guild_menu", "Guilde"),
					GameAction.Map => Localization.GetOrDefault("controls.map", "Carte"),
					GameAction.Bestiary => Localization.GetOrDefault("controls.bestiary", "Bestiaire"),
					GameAction.QuickSave => Localization.GetOrDefault("controls.quick_save", "Sauvegarde rapide"),
					GameAction.ReloadData => Localization.GetOrDefault("controls.reload_data", "Recharger les données du projet"),
					GameAction.ToggleDebug => Localization.GetOrDefault("controls.toggle_debug", "Afficher le debug"),
					GameAction.TimeSkip => Localization.GetOrDefault("controls.time_skip", "Accélérer le temps"),
					GameAction.SelectStructureCorner1 => Localization.GetOrDefault("controls.structure_corner_1", "Structure : premier coin"),
					GameAction.SelectStructureCorner2 => Localization.GetOrDefault("controls.structure_corner_2", "Structure : deuxième coin / enregistrer"),
					GameAction.Fullscreen => Localization.GetOrDefault("controls.fullscreen", "Plein écran"),
					GameAction.ItemsMenu => Localization.GetOrDefault("controls.items_menu", "Menu des objets (mode dieu)"),
					GameAction.EmoteWheel => Localization.GetOrDefault("controls.emote_wheel", "Menu des émotes"),
					GameAction.ArmorStandPose => Localization.GetOrDefault("controls.armor_stand_pose", "Changer la pose du porte-armure"),
					GameAction.WagonLink => Localization.GetOrDefault("controls.wagon_link", "Lier / délier les wagons"),
					GameAction.BoatRow => Localization.GetOrDefault("controls.boat_row", "Ramer"),
					GameAction.CarMode => Localization.GetOrDefault("controls.car_mode", "Mode voiture"),
					_ => action.ToString()
				};
			}

			public static string BindingDisplayName(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
				{
					var key = (KeyboardKey)binding.Code;
					return key switch
					{
						KeyboardKey.LeftShift => "MAJ (gauche)",
						KeyboardKey.RightShift => "MAJ (droite)",
						KeyboardKey.LeftControl => "CTRL (gauche)",
						KeyboardKey.RightControl => "CTRL (droite)",
						KeyboardKey.Space => "ESPACE",
						KeyboardKey.Escape => "ECHAP",
						KeyboardKey.Enter => "ENTRÉE",
						KeyboardKey.Tab => "TAB",
						_ => key.ToString().ToUpperInvariant()
					};
				}
				else if (binding.Type == InputType.Mouse)
				{
					var btn = (MouseButton)binding.Code;
					return btn switch
					{
						MouseButton.Left => "Clic gauche",
						MouseButton.Right => "Clic droit",
						MouseButton.Middle => "Clic molette",
						_ => "Souris"
					};
				}
				else if (binding.Type == InputType.GamepadButton)
				{
					return (GamepadButton)binding.Code switch
					{
						GamepadButton.LeftFaceUp => "Manette haut",
						GamepadButton.LeftFaceDown => "Manette bas",
						GamepadButton.LeftFaceLeft => "Manette gauche",
						GamepadButton.LeftFaceRight => "Manette droite",
						GamepadButton.RightFaceUp => "Triangle",
						GamepadButton.RightFaceRight => "Cercle",
						GamepadButton.RightFaceDown => "Croix",
						GamepadButton.RightFaceLeft => "Carre",
						GamepadButton.LeftTrigger1 => "Gachette gauche",
						GamepadButton.LeftTrigger2 => "Gachette gauche 2",
						GamepadButton.RightTrigger1 => "Gachette droite",
						GamepadButton.RightTrigger2 => "Gachette droite 2",
						GamepadButton.LeftThumb => "Joystick gauche",
						GamepadButton.RightThumb => "Joystick droit",
						GamepadButton.MiddleLeft => "Bouton central gauche",
						GamepadButton.Middle => "Bouton central",
						GamepadButton.MiddleRight => "Bouton central droit",
						_ => "Manette"
					};
				}
				else
				{
					return (GamepadAxisDirection)binding.Code switch
					{
						GamepadAxisDirection.LeftXNegative => "Joystick gauche gauche",
						GamepadAxisDirection.LeftXPositive => "Joystick gauche droite",
						GamepadAxisDirection.LeftYNegative => "Joystick gauche haut",
						GamepadAxisDirection.LeftYPositive => "Joystick gauche bas",
						GamepadAxisDirection.RightXNegative => "Joystick droit gauche",
						GamepadAxisDirection.RightXPositive => "Joystick droit droite",
						GamepadAxisDirection.RightYNegative => "Joystick droit haut",
						GamepadAxisDirection.RightYPositive => "Joystick droit bas",
						_ => "Direction manette"
					};
				}
			}

			public static void Rebind(GameAction action, int index, InputBinding newBinding)
			{
				var bindings = Current[action];
				if (index < 0 || index >= bindings.Count || bindings[index].Equals(newBinding))
					return;
				bindings[index] = newBinding;
			}

			public static int AddBinding(GameAction action)
			{
				Current[action].Add(new InputBinding(InputType.Keyboard, (int)KeyboardKey.Null));
				return Current[action].Count - 1;
			}

			public static void RemoveBinding(GameAction action, int index)
			{
				if (index <= 0 || index >= Current[action].Count) return;
				Current[action].RemoveAt(index);
			}

			public static void ResetToDefaults()
			{
				Current = CloneBindings(_defaults);
			}

			public static void Save()
			{
				try
				{
					var asStrings = Current.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Count == 1
						? (object)kv.Value[0].ToString() : kv.Value.Select(binding => binding.ToString()).ToList());
					string json = JsonSerializer.Serialize(asStrings, new JsonSerializerOptions { WriteIndented = true });
					string? dir = Path.GetDirectoryName(SavePath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					File.WriteAllText(SavePath, json);
				}
				catch (Exception ex)
				{
					Console.WriteLine($" Impossible de sauvegarder les touches assignées : {ex.Message}");
				}
			}

			public static void Load()
			{
				Current = new(_defaults);
				if (!File.Exists(SavePath))
					return;

				try
				{
					string json = File.ReadAllText(SavePath);
					var asStrings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
					if (asStrings == null)
						return;

					foreach (var kv in asStrings)
					{
						if (!Enum.TryParse(kv.Key, out GameAction action))
							continue;
						var values = kv.Value.ValueKind == JsonValueKind.Array
							? kv.Value.EnumerateArray().Select(value => value.GetString()).ToList()
							: new List<string?> { kv.Value.GetString() };
						var bindings = values.Where(value => value != null).Select(ParseBinding).Where(binding => binding.HasValue).Select(binding => binding!.Value).ToList();
						if (bindings.Count > 0) Current[action] = bindings;
					}
					Console.WriteLine(" Touches assignées chargées depuis keybindings.json");
				}
				catch (Exception ex)
				{
					Console.WriteLine($" Impossible de charger les touches assignées ({ex.Message}), valeurs par défaut utilisées.");
					Current = CloneBindings(_defaults);
				}
			}

			private static InputBinding? ParseBinding(string? value)
			{
				if (string.IsNullOrEmpty(value)) return null;
				var parts = value.Split(':');
				if (parts.Length != 2 || !Enum.TryParse(parts[0], out InputType type) || !int.TryParse(parts[1], out int code)) return null;
				return new InputBinding(type, code);
			}
		}

		// DrawOptionsMenu() a été déplacé dans OptionsMenu.cs

    }
}
