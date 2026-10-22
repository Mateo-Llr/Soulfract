// FirewallHelper.cs - Version complète avec votre commande

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Soulfract
{
    public static class FirewallHelper
    {
        private const string RULE_NAME = "SoulfractHostVoice";
        private const string LEGACY_GAME_RULE_NAME = "RPGEngineHost";
        
        /// <summary>
        /// Vérifie si la règle de pare-feu existe déjà
        /// </summary>
        public static bool RuleExists(int port)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            return GameRuleExists(port) && VoiceRuleExists(port);
        }

        public static bool GameRuleExists(int port) =>
            RuleMatches(RULE_NAME, port, "Any")
            || RuleMatches($"SoulfractHostTcp_{port}", port, "TCP")
            || RuleMatches(LEGACY_GAME_RULE_NAME, port, "TCP");

        public static bool VoiceRuleExists(int port) =>
            RuleMatches(RULE_NAME, port, "Any")
            || RuleMatches($"SoulfractVoiceUdp_{port}", port, "UDP");

        private static bool RuleMatches(string name, int port, string protocol)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"{name}\" verbose",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(3000) || !output.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return false;

                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string? localPortLine = lines.FirstOrDefault(line =>
                    line.Contains("LocalPort", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Port local", StringComparison.OrdinalIgnoreCase));
                string? protocolLine = lines.FirstOrDefault(line =>
                    line.Contains("Protocol", StringComparison.OrdinalIgnoreCase));
                if (localPortLine == null || protocolLine == null)
                    return false;

                string[] portTokens = localPortLine.Split(new[] { ' ', ':', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                bool portMatches = portTokens.Contains(port.ToString(), StringComparer.OrdinalIgnoreCase);
                bool protocolMatches = protocolLine.Contains(protocol, StringComparison.OrdinalIgnoreCase)
                    || protocolLine.Contains("Any", StringComparison.OrdinalIgnoreCase)
                    || protocolLine.Contains("Tout", StringComparison.OrdinalIgnoreCase);
                return portMatches && protocolMatches;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firewall] Erreur vérification de {name} : {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Ajoute la règle de pare-feu (comme votre commande)
        /// </summary>
        public static bool AddRule(int port)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            if (RuleExists(port))
                return true;

            var commands = new List<string>();
            if (!GameRuleExists(port))
                commands.Add($"advfirewall firewall add rule name=\"SoulfractHostTcp_{port}\" dir=in action=allow protocol=TCP localport={port}");
            if (!VoiceRuleExists(port))
                commands.Add($"advfirewall firewall add rule name=\"SoulfractVoiceUdp_{port}\" dir=in action=allow protocol=UDP localport={port}");

            string tempOut = Path.Combine(Path.GetTempPath(), $"rpgfw_{Guid.NewGuid():N}.log");
            try
            {
                Console.WriteLine($"[Firewall] Tentative d'ajout de la règle pour le port {port}...");

                if (IsAdministrator())
                {
                    foreach (string command in commands)
                    {
                        var psiDirect = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = command,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using var procDirect = Process.Start(psiDirect);
                        if (procDirect == null)
                            return false;

                        string output = procDirect.StandardOutput.ReadToEnd();
                        string error = procDirect.StandardError.ReadToEnd();
                        if (!procDirect.WaitForExit(5000))
                            return false;

                        Console.WriteLine($"[Firewall] Sortie : {output}");
                        if (!string.IsNullOrEmpty(error))
                            Console.WriteLine($"[Firewall] Erreur : {error}");
                        if (procDirect.ExitCode != 0)
                            return false;
                    }

                    return RuleExists(port);
                }

                string netshArgs = string.Join(" & ", commands.Select(command => $"netsh {command}"));
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {netshArgs} > \"{tempOut}\" 2>&1",
                    UseShellExecute = true,   // requis pour Verb = runas
                    Verb = "runas",           // déclenche la fenêtre UAC
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    if (!proc.WaitForExit(15000))
                        return RuleExists(port);

                    bool success = proc.ExitCode == 0;

                    string output = "";
                    try
                    {
                        if (File.Exists(tempOut))
                            output = File.ReadAllText(tempOut);
                    }
                    catch { /* lecture du log best-effort */ }

                    if (!string.IsNullOrEmpty(output))
                        Console.WriteLine($"[Firewall] Sortie : {output}");
                    Console.WriteLine($"[Firewall] Ajout de la règle : {(success ? "SUCCÈS" : "ÉCHEC")} (code {proc.ExitCode})");

                    return success && RuleExists(port);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Levée quand l'utilisateur clique sur "Non" dans la popup UAC
                Console.WriteLine("[Firewall] L'utilisateur a refusé l'élévation des privilèges (UAC).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firewall] Erreur lors de l'ajout : {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            }
            return false;
        }
        /// <summary>
        /// Supprime la règle de pare-feu
        /// </summary>
        public static bool RemoveRule()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;
                
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall delete rule name=\"{RULE_NAME}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };
                
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return true;
            }
            catch { return false; }
        }
        
        /// <summary>
        /// Relance l'exécutable actuel avec élévation UAC (runas), en lui passant
        /// des arguments supplémentaires (ex: pour redémarrer directement en mode hôte).
        /// Retourne true si le nouveau processus a bien été lancé (le processus actuel
        /// doit alors se fermer immédiatement après l'appel).
        /// </summary>
        public static bool RelaunchAsAdministrator(string extraArgs)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Environment.ProcessPath
                    ?? string.Empty;

                if (string.IsNullOrEmpty(exePath))
                {
                    Console.WriteLine("[Firewall] Impossible de déterminer le chemin de l'exécutable pour la relance.");
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = extraArgs,
                    UseShellExecute = true, // requis pour Verb = runas
                    Verb = "runas",
                    WorkingDirectory = Environment.CurrentDirectory
                };

                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // L'utilisateur a cliqué sur "Non" dans la popup UAC
                Console.WriteLine("[Firewall] L'utilisateur a refusé l'élévation des privilèges (UAC) pour l'hébergement.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firewall] Erreur lors de la relance en admin : {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Vérifie si le programme a les droits administrateur
        /// </summary>
        public static bool IsAdministrator()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;
                
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Affiche une fenêtre d'information sur le pare-feu
        /// (pour les joueurs, en français)
        /// </summary>
        public static void ShowFirewallDialog(int port)
        {
            Console.Clear();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║              CONFIGURATION DU PARE-FEU POUR LE PORT {port}              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Pour permettre aux autres joueurs de se connecter à votre partie,");
            Console.WriteLine("les protocoles TCP (jeu) et UDP (voix) doivent être autorisés dans le pare-feu Windows.");
            Console.WriteLine();
            
            if (IsAdministrator())
            {
                Console.WriteLine(" Vous êtes en mode administrateur.");
                Console.WriteLine(" La règle sera ajoutée automatiquement.");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("  Vous n'êtes PAS en mode administrateur.");
                Console.WriteLine(" Une fenêtre de confirmation va s'ouvrir pour les droits.");
                Console.WriteLine();
            }
            
            Console.WriteLine("Commande utilisée :");
            Console.WriteLine($"   netsh advfirewall firewall add rule name=\"SoulfractHostTcp_{port}\" dir=in action=allow protocol=TCP localport={port}");
            Console.WriteLine($"   netsh advfirewall firewall add rule name=\"SoulfractVoiceUdp_{port}\" dir=in action=allow protocol=UDP localport={port}");
            Console.WriteLine();
            Console.WriteLine("Appuyez sur une touche pour continuer...");
            Console.ReadKey(true);
        }
    }
}