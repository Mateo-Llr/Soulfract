using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SoulfractBootstrapper;

internal static class Program
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/Mateo-Llr/Soulfract/releases/latest";
    private const string LauncherAssetName = "SoulfractLauncher.exe";
    private const string LauncherExecutableName = "SoulfractLauncher.exe";
    private const string GameExecutableName = "Soulfract.exe";
    private const string VersionFileName = ".soulfract-launcher-version";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--replace-launcher", StringComparison.Ordinal))
            return await ReplaceLauncherAsync(args);

        return await RunBootstrapperAsync();
    }

    private static async Task<int> RunBootstrapperAsync()
    {
        string? launcherDirectory = null;
        string? launcherPath = null;

        try
        {
            launcherDirectory = GetLauncherDirectory();
            launcherPath = Path.Combine(launcherDirectory, LauncherExecutableName);
            Directory.CreateDirectory(launcherDirectory);
            if (IsLauncherRunning())
                throw new InvalidOperationException("Soulfract Launcher est déjà ouvert. Fermez-le avant de réessayer.");

            Console.WriteLine("Recherche de la dernière version du lanceur...");
            using JsonDocument release = await GetLatestReleaseAsync();
            string tag = release.RootElement.GetProperty("tag_name").GetString()
                ?? throw new InvalidDataException("La release GitHub n'a pas de numéro de version.");
            LauncherReleaseAsset launcherAsset = FindLauncherAsset(release.RootElement)
                ?? throw new InvalidDataException($"La release {tag} ne contient pas {LauncherAssetName}.");

            string versionPath = Path.Combine(launcherDirectory, VersionFileName);
            string? installedVersion = File.Exists(versionPath) ? await File.ReadAllTextAsync(versionPath) : null;
            bool hasInstalledLauncher = File.Exists(launcherPath);

            if (hasInstalledLauncher && string.Equals(installedVersion?.Trim(), launcherAsset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Le lanceur est déjà à jour ({launcherAsset.Sha256[..12]}).");
                StartLauncher(launcherPath, launcherDirectory);
                return 0;
            }

            string temporaryLauncherPath = Path.Combine(launcherDirectory, $".{LauncherExecutableName}.{Guid.NewGuid():N}.tmp");
            try
            {
                Console.WriteLine($"Téléchargement du lanceur {tag}...");
                await DownloadLauncherAsync(launcherAsset.DownloadUrl, temporaryLauncherPath);
                await VerifyLauncherAsync(temporaryLauncherPath, launcherAsset.Sha256);

                Console.WriteLine("Installation...");
                File.Move(temporaryLauncherPath, launcherPath, overwrite: true);

                string temporaryVersionPath = versionPath + ".tmp";
                await File.WriteAllTextAsync(temporaryVersionPath, launcherAsset.Sha256);
                File.Move(temporaryVersionPath, versionPath, overwrite: true);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryLauncherPath);
            }

            Console.WriteLine($"Lancement du lanceur {tag}...");
            StartLauncher(launcherPath, launcherDirectory);
            return 0;
        }
        catch (Exception exception) when (IsRecoverableFailure(exception))
        {
            Console.Error.WriteLine($"La mise à jour du lanceur a échoué : {exception.Message}");
            if (launcherPath != null && launcherDirectory != null && File.Exists(launcherPath))
            {
                Console.Error.WriteLine("Démarrage de la version du lanceur déjà installée.");
                StartLauncher(launcherPath, launcherDirectory);
                return 0;
            }

            Console.Error.WriteLine("Aucun lanceur n'est installé. Vérifiez votre connexion, puis réessayez.");
            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("Appuyez sur une touche pour quitter.");
                Console.ReadKey(intercept: true);
            }

            return 1;
        }
    }

    private static async Task<int> ReplaceLauncherAsync(string[] args)
    {
        string? launcherPath = null;
        string? stagedLauncherPath = null;

        try
        {
            if (args.Length != 7
                || !int.TryParse(args[5], out int launcherProcessId)
                || launcherProcessId <= 0
                || string.IsNullOrWhiteSpace(args[4])
                || args[4].Length != 64
                || !args[4].All(Uri.IsHexDigit))
                throw new InvalidDataException("Les paramètres de mise à jour du lanceur sont invalides.");

            launcherPath = Path.GetFullPath(args[1]);
            stagedLauncherPath = Path.GetFullPath(args[2]);
            string versionPath = Path.GetFullPath(args[3]);
            string workingDirectory = Path.GetFullPath(args[6]);
            string launcherDirectory = Path.GetDirectoryName(launcherPath)
                ?? throw new InvalidDataException("Le dossier du lanceur est introuvable.");

            if (!string.Equals(Path.GetFileName(launcherPath), LauncherExecutableName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(versionPath), VersionFileName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(stagedLauncherPath), launcherDirectory, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(stagedLauncherPath).StartsWith($".{LauncherExecutableName}.", StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(stagedLauncherPath).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(launcherDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Les chemins de mise à jour du lanceur sont invalides.");

            await WaitForLauncherExitAsync(launcherProcessId);
            await VerifyLauncherAsync(stagedLauncherPath, args[4]);
            File.Move(stagedLauncherPath, launcherPath, overwrite: true);

            string temporaryVersionPath = versionPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryVersionPath, args[4]);
                File.Move(temporaryVersionPath, versionPath, overwrite: true);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryVersionPath);
            }

            StartLauncher(launcherPath, launcherDirectory);
            return 0;
        }
        catch (Exception exception) when (IsRecoverableFailure(exception))
        {
            ShowUpdateError($"La mise à jour du lanceur a échoué : {exception.Message}");
            if (launcherPath != null && File.Exists(launcherPath))
            {
                try
                {
                    StartLauncher(launcherPath, Path.GetDirectoryName(launcherPath)!);
                    return 0;
                }
                catch (Exception startException) when (IsRecoverableFailure(startException))
                {
                    ShowUpdateError($"Impossible de redémarrer le lanceur : {startException.Message}");
                }
            }

            return 1;
        }
        finally
        {
            if (stagedLauncherPath != null)
                TryDeleteTemporaryFile(stagedLauncherPath);
        }
    }

    private static async Task WaitForLauncherExitAsync(int processId)
    {
        if (processId == Environment.ProcessId)
            throw new InvalidDataException("Le lanceur ne peut pas attendre son propre processus.");

        try
        {
            using Process launcher = Process.GetProcessById(processId);
            await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (ArgumentException)
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int ShowMessageBox(IntPtr window, string text, string caption, uint type);

    private static void ShowUpdateError(string message)
    {
        ShowMessageBox(IntPtr.Zero, message, "Soulfract", 0x10);
    }

    private static string GetLauncherDirectory()
    {
        string bootstrapperDirectory = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(bootstrapperDirectory, GameExecutableName)))
            return bootstrapperDirectory;

        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("Le dossier AppData local de l'utilisateur est introuvable.");

        return Path.Combine(localApplicationData, "Soulfract", "Launcher");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoulfractBootstrapper", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<JsonDocument> GetLatestReleaseAsync()
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(ReleaseApiUrl);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static LauncherReleaseAsset? FindLauncherAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets))
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != LauncherAssetName)
                continue;

            string? url = asset.GetProperty("browser_download_url").GetString();
            string? digest = asset.TryGetProperty("digest", out JsonElement digestElement)
                ? digestElement.GetString()
                : null;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUrl)
                && parsedUrl.Scheme == Uri.UriSchemeHttps
                && digest is { Length: 71 }
                && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                && digest[7..].All(Uri.IsHexDigit))
                return new LauncherReleaseAsset(parsedUrl.AbsoluteUri, digest[7..]);
        }

        return null;
    }

    private readonly record struct LauncherReleaseAsset(string DownloadUrl, string Sha256);

    private static async Task DownloadLauncherAsync(string url, string destination)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        byte[] buffer = new byte[81920];
        long downloaded = 0;
        long? total = response.Content.Headers.ContentLength;
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloaded += bytesRead;
            if (total is > 0)
            {
                int percent = (int)Math.Clamp(downloaded * 100 / total.Value, 0, 100);
                Console.Write($"\rTéléchargement du lanceur : {percent}%");
            }
        }

        if (total is > 0)
            Console.WriteLine();
        await output.FlushAsync();

        if (downloaded < 1024 * 1024)
            throw new InvalidDataException("Le fichier téléchargé est trop petit pour être un lanceur valide.");

        output.Position = 0;
        if (output.ReadByte() != 'M' || output.ReadByte() != 'Z')
            throw new InvalidDataException("Le fichier téléchargé n'est pas un exécutable Windows valide.");
    }

    private static async Task VerifyLauncherAsync(string path, string expectedSha256)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] actualHash = await SHA256.HashDataAsync(stream);
        string actualSha256 = Convert.ToHexString(actualHash);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La vérification d'intégrité du lanceur téléchargé a échoué.");
    }

    private static bool IsLauncherRunning()
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LauncherExecutableName)))
        {
            using (process)
            {
                if (!process.HasExited)
                    return true;
            }
        }

        return false;
    }

    private static void StartLauncher(string launcherPath, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
    }

    private static bool IsRecoverableFailure(Exception exception)
    {
        return exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or IOException
            or InvalidDataException
            or CryptographicException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException
            or ArgumentException
            or NotSupportedException
            or System.ComponentModel.Win32Exception
            or System.Security.SecurityException
            or KeyNotFoundException;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Impossible de supprimer le fichier temporaire {path}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Impossible de supprimer le fichier temporaire {path}: {exception.Message}");
        }
    }
}
