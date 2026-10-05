using System.Diagnostics;
using System.Net.Http.Headers;
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

    private static async Task<int> Main()
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
            or UnauthorizedAccessException
            or InvalidOperationException
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
