// Program.Network.cs
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {
		//  MULTIJOUEUR : accès pour les vérifications de collision faites depuis Network.cs
        // (host) lors de la dépose d'une créature portée par un client.
        public static HashSet<string> GetDestroyedObjectsForNetwork() => destroyedObjects;

		// 0 = Héberger, 1 = Rejoindre, 2 = Retour
		static bool _isHostSetup = false;

		static string _mpHostIpInput = "127.0.0.1";

		// client en attente du message "Welcome"
		static bool _isHostingFromPause = false;

		private static DiscordRpcClient? _discordClient = null;

		static int _pendingHostPort = 7777;

		static bool _pendingHostMode = false;

		// ════════════════════════════════════════════════════════════════
		//   CALLBACKS RÉSEAU — appelés par NetworkManager (depuis la boucle principale)
		// ════════════════════════════════════════════════════════════════

		public static void OnNetworkPlayerJoined(int id, string name)
		{
			AddNotification(new Notification($" {name} a rejoint la partie.", new Color(150, 220, 255, 255), 3f));
		}

		public static void OnNetworkPlayerLeft(int id, string name)
		{
			AddNotification(new Notification($" {name} a quitté la partie.", new Color(255, 180, 140, 255), 3f));
			_remoteWasDead.Remove(1000 + id);
		}

		public static void OnNetworkEntityDeath(string netIdStr)
		{
			if (!Guid.TryParse(netIdStr, out var guid)) return;
			var e = entities.FirstOrDefault(x => x.NetId == guid);
			if (e != null) e.SetHealthSilently(0, e.MaxHP);
		}

		public static void ApplyNetworkGameTime(float gameTime)
		{
			if (Math.Abs(_gameTime - gameTime) > 2f)
				_gameTime = gameTime;
		}

		public static string GetCurrentWeatherType() => Weather.Current switch
		{
			WeatherType.Rain => "rain",
			WeatherType.Storm => "storm",
			_ => "clear"
		};

		public static void ApplyNetworkWeather(string weatherType)
		{
			string normalized = string.IsNullOrWhiteSpace(weatherType) ? "clear" : weatherType.Trim().ToLowerInvariant();
			if (normalized is not "rain" and not "storm") normalized = "clear";
			if (GetCurrentWeatherType() != normalized)
				Weather.SetWeather(normalized);
		}

		private static void CheckFirewallBeforeHosting(int port)
		{
			try
			{
				if (!OperatingSystem.IsWindows()) return;
				bool exists = FirewallHelper.RuleExists(port);
				if (exists)
				{
					Console.WriteLine($" Règle pare-feu SoulfractHostVoice déjà présente.");
					return;
				}

				Console.WriteLine($" La règle complète TCP/UDP n'existe pas pour le port {port}.");
				Console.WriteLine("Tentative d'ajout automatique...");
				if (!FirewallHelper.GameRuleExists(port))
					FirewallHelper.ShowFirewallDialog(port);
				bool added = FirewallHelper.AddRule(port);
				if (added)
				{
					AddNotification(new Notification(
						$" Règle pare-feu ajoutée pour le port {port} !",
						new Color(100, 255, 100, 255), 3f));
				}
				else
				{
					AddNotification(new Notification(
						$" Impossible d'ajouter la règle pare-feu. Les autres joueurs pourraient ne pas pouvoir se connecter.",
						new Color(255, 200, 100, 255), 5f));
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Erreur pare-feu : {ex.Message}");
			}
		}

		private const string AutoHostArgPrefix = "--autohost-port=";

		public static bool TryConsumeAutoHostArg(string[] args, out int port)
		{
			port = 0;
			var arg = args.FirstOrDefault(a => a.StartsWith(AutoHostArgPrefix));
			if (arg == null) return false;
			return int.TryParse(arg.Substring(AutoHostArgPrefix.Length), out port);
		}

		private static bool StartHostingGame(int port)
		{
			CheckFirewallBeforeHosting(port);

			if (!NetworkManager.StartHost(port))
			{
				AddNotification(new Notification(
					$" Impossible d'héberger : {NetworkManager.LastError}",
					new Color(255, 120, 120, 255), 5f));
				return false;
			}

			return true;
		}

		public static void ApplyNetworkQuestActionResult(QuestActionResultMsg result)
		{
			if (!result.Success) return;
			if (result.Action == "GiftAnimal")
				activeNotifications.Add(new Notification(" Animal offert avec succès !", new Color(220, 200, 100, 255), 3f));
		}

		private static string _clientCarriedSpecies = "";
		public static bool IsClientCarryingCreature => _clientCarriedNetId != null;
    }

    public class CharacterProfile
    {
        public string Guid { get; set; } = "";
        public string Label { get; set; } = "Personnage";
        public string LastUsedUtc { get; set; } = "";
    }

    public static class LocalCharacters
    {
		private const string LegacyFilePath = "Data/player_characters.json";

        private static string GetFilePath(string hostKey, string worldName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string folderName = new string(worldName.Select(c =>
                invalidChars.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "world";
            if (folderName.Length > 48) folderName = folderName[..48];

            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"{hostKey}\n{worldName}"));
            string suffix = Convert.ToHexString(key)[..16].ToLowerInvariant();
            return Path.Combine("Data", "Characters", $"{folderName}_{suffix}", "characters.json");
        }

        private static List<CharacterProfile> Load(string hostKey, string worldName)
        {
            try
            {
                string path = GetFilePath(hostKey, worldName);
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<List<CharacterProfile>>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Impossible de charger les personnages de \"{worldName}\" : {ex.Message}");
            }
            return new List<CharacterProfile>();
        }

		private static List<CharacterProfile> LoadLegacy(string hostKey)
		{
			try
			{
				if (!File.Exists(LegacyFilePath)) return new List<CharacterProfile>();
				var profilesByHost = JsonSerializer.Deserialize<Dictionary<string, List<CharacterProfile>>>(File.ReadAllText(LegacyFilePath));
				return profilesByHost != null && profilesByHost.TryGetValue(hostKey, out var profiles)
					? profiles
					: new List<CharacterProfile>();
			}
			catch (Exception ex)
			{
				Console.WriteLine($" Impossible de charger les anciens personnages locaux : {ex.Message}");
				return new List<CharacterProfile>();
			}
		}

		public static List<string> GetLegacyGuids(string hostKey) =>
			LoadLegacy(hostKey).Select(profile => profile.Guid).Where(guid => !string.IsNullOrWhiteSpace(guid)).ToList();

		public static void ImportLegacyForWorld(string hostKey, string worldName, List<string> existingGuids)
		{
			var acceptedGuids = new HashSet<string>(existingGuids, StringComparer.OrdinalIgnoreCase);
			if (acceptedGuids.Count == 0) return;

			var profiles = Load(hostKey, worldName);
			var knownGuids = new HashSet<string>(profiles.Select(profile => profile.Guid), StringComparer.OrdinalIgnoreCase);
			foreach (var legacyProfile in LoadLegacy(hostKey))
			{
				if (acceptedGuids.Contains(legacyProfile.Guid) && knownGuids.Add(legacyProfile.Guid))
					profiles.Add(legacyProfile);
			}
			Save(hostKey, worldName, profiles);
		}

        private static void Save(string hostKey, string worldName, List<CharacterProfile> profiles)
        {
            try
            {
                string path = GetFilePath(hostKey, worldName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(profiles));
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Impossible d'enregistrer les personnages de \"{worldName}\" : {ex.Message}");
            }
        }

        public static List<CharacterProfile> GetForWorld(string hostKey, string worldName)
        {
            var result = new List<CharacterProfile>
            {
                new CharacterProfile { Guid = Program.DefaultLocalPlayerGuidReadOnly, Label = "Personnage principal" }
            };
            result.AddRange(Load(hostKey, worldName));
            return result;
        }

        public static CharacterProfile CreateNew(string hostKey, string worldName, string label)
        {
            var profile = new CharacterProfile
            {
                Guid = System.Guid.NewGuid().ToString(),
                Label = label,
                LastUsedUtc = DateTime.UtcNow.ToString("o")
            };
            var profiles = Load(hostKey, worldName);
            profiles.Add(profile);
            Save(hostKey, worldName, profiles);
            return profile;
        }

        public static void MarkUsed(string hostKey, string worldName, string guid)
        {
            if (guid == Program.DefaultLocalPlayerGuidReadOnly) return;
            var profiles = Load(hostKey, worldName);
            var profile = profiles.FirstOrDefault(p => p.Guid == guid);
            if (profile == null) return;
            profile.LastUsedUtc = DateTime.UtcNow.ToString("o");
            Save(hostKey, worldName, profiles);
        }
    }
}