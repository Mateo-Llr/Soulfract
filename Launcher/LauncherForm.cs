using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SoulfractLauncher;

internal sealed class LauncherForm : Form
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/Mateo-Llr/Soulfract/releases/latest";
    private const string GameArchiveName = "Soulfract-win-x64.zip";
    private const string GameExecutableName = "Soulfract.exe";
    private const string VersionFileName = ".soulfract-version";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly string _installDirectory;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _launchButton;
    private readonly Button _retryButton;
    private readonly Button _closeButton;
    private bool _isWorking;

    public LauncherForm()
    {
        string launcherDirectory = AppContext.BaseDirectory;
        _installDirectory = File.Exists(Path.Combine(launcherDirectory, GameExecutableName))
            ? launcherDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soulfract");

        Text = "Soulfract";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 150);
        Font = new Font("Segoe UI", 9F);

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(20, 20),
            Size = new Size(400, 40),
            Text = "Préparation du lanceur..."
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 65),
            Size = new Size(400, 18),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25
        };
        _launchButton = new Button
        {
            Location = new Point(20, 105),
            Size = new Size(130, 30),
            Text = "Lancer le jeu",
            Enabled = false
        };
        _retryButton = new Button
        {
            Location = new Point(290, 105),
            Size = new Size(70, 30),
            Text = "Réessayer",
            Enabled = false
        };
        _closeButton = new Button
        {
            Location = new Point(370, 105),
            Size = new Size(50, 30),
            Text = "Quitter"
        };

        _launchButton.Click += (_, _) => LaunchInstalledGame();
        _retryButton.Click += async (_, _) => await CheckAndLaunchAsync();
        _closeButton.Click += (_, _) => Close();
        Controls.AddRange([_statusLabel, _progressBar, _launchButton, _retryButton, _closeButton]);
        Shown += async (_, _) => await CheckAndLaunchAsync();
    }

    private async Task CheckAndLaunchAsync()
    {
        if (_isWorking)
            return;

        _isWorking = true;
        SetWorkingState();
        string archivePath = Path.Combine(Path.GetTempPath(), $"Soulfract-{Guid.NewGuid():N}.zip");
        string extractionDirectory = Path.Combine(Path.GetTempPath(), $"Soulfract-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(_installDirectory);
            UpdateStatus("Recherche de la dernière version...");

            using JsonDocument release = await GetLatestReleaseAsync();
            JsonElement root = release.RootElement;
            string tag = root.GetProperty("tag_name").GetString()
                ?? throw new InvalidDataException("La release GitHub n'a pas de numéro de version.");
            string? downloadUrl = FindGameArchiveUrl(root);
            if (downloadUrl == null)
                throw new InvalidDataException($"La release {tag} ne contient pas {GameArchiveName}.");

            string versionPath = Path.Combine(_installDirectory, VersionFileName);
            string? installedVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : null;
            string gamePath = Path.Combine(_installDirectory, GameExecutableName);
            bool hasInstalledGame = File.Exists(gamePath);

            if (hasInstalledGame && string.Equals(installedVersion, tag, StringComparison.Ordinal))
            {
                UpdateStatus($"Soulfract est déjà à jour ({tag}). Démarrage...");
                LaunchInstalledGame();
                return;
            }

            if (hasInstalledGame && IsGameRunning())
            {
                UpdateStatus("Fermez Soulfract avant d'installer la mise à jour.");
                SetIdleState(canLaunch: true);
                return;
            }

            UpdateStatus($"Téléchargement de Soulfract {tag}...");
            var progress = new Progress<(long Downloaded, long? Total)>(value =>
            {
                if (value.Total is > 0)
                {
                    _progressBar.Style = ProgressBarStyle.Continuous;
                    _progressBar.Value = (int)Math.Clamp(value.Downloaded * 100 / value.Total.Value, 0, 100);
                    UpdateStatus($"Téléchargement de Soulfract {tag}... {value.Downloaded / 1_048_576} / {value.Total.Value / 1_048_576} Mo");
                }
            });
            await DownloadArchiveAsync(downloadUrl, archivePath, progress);

            UpdateStatus("Vérification des fichiers...");
            Directory.CreateDirectory(extractionDirectory);
            ExtractArchiveSafely(archivePath, extractionDirectory);
            if (!File.Exists(Path.Combine(extractionDirectory, GameExecutableName)))
                throw new InvalidDataException($"L'archive téléchargée ne contient pas {GameExecutableName}.");

            UpdateStatus("Installation de la mise à jour...");
            CopyGameFiles(extractionDirectory, _installDirectory, preservePlayerData: hasInstalledGame);
            string temporaryVersionPath = versionPath + ".tmp";
            await File.WriteAllTextAsync(temporaryVersionPath, tag);
            File.Move(temporaryVersionPath, versionPath, overwrite: true);

            UpdateStatus($"Soulfract {tag} est prêt. Démarrage...");
            LaunchInstalledGame();
        }
        catch (Exception exception) when (IsRecoverableLauncherFailure(exception))
        {
            UpdateStatus($"La mise à jour a échoué : {exception.Message}");
            SetIdleState(File.Exists(Path.Combine(_installDirectory, GameExecutableName)));
        }
        finally
        {
            _isWorking = false;
            TryDeleteFile(archivePath);
            TryDeleteDirectory(extractionDirectory);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoulfractLauncher", "1.0"));
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

    private static string? FindGameArchiveUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets))
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == GameArchiveName)
            {
                string? url = asset.GetProperty("browser_download_url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUrl)
                    && parsedUrl.Scheme == Uri.UriSchemeHttps)
                    return parsedUrl.AbsoluteUri;
            }
        }

        return null;
    }

    private static async Task DownloadArchiveAsync(
        string url,
        string destination,
        IProgress<(long Downloaded, long? Total)> progress)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloaded += bytesRead;
            progress.Report((downloaded, response.Content.Headers.ContentLength));
        }
    }

    private static void ExtractArchiveSafely(string archivePath, string destinationDirectory)
    {
        string destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L'archive contient un chemin de fichier invalide.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            string? directory = Path.GetDirectoryName(destinationPath);
            if (directory != null)
                Directory.CreateDirectory(directory);
            using Stream input = entry.Open();
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void CopyGameFiles(string sourceDirectory, string destinationDirectory, bool preservePlayerData)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            if (preservePlayerData && IsPlayerData(relativePath))
                continue;

            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            string? parentDirectory = Path.GetDirectoryName(destinationPath);
            if (parentDirectory != null)
                Directory.CreateDirectory(parentDirectory);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static bool IsPlayerData(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        return path.Equals("player_id.txt", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Saves", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("GuildEmblems", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("GuildEmblems/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/settings.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/keybindings.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/servers.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/player_characters.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/character_animations.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/worldobjects.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Data/structures.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGameRunning()
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExecutableName)))
        {
            using (process)
            {
                if (!process.HasExited)
                    return true;
            }
        }

        return false;
    }

    private static bool IsRecoverableLauncherFailure(Exception exception)
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

    private void LaunchInstalledGame()
    {
        string gamePath = Path.Combine(_installDirectory, GameExecutableName);
        if (!File.Exists(gamePath))
        {
            UpdateStatus("Aucune version du jeu n'est installée pour le moment.");
            SetIdleState(canLaunch: false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = gamePath,
                WorkingDirectory = _installDirectory,
                UseShellExecute = true
            });
            Close();
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            UpdateStatus($"Impossible de démarrer le jeu : {exception.Message}");
            SetIdleState(canLaunch: true);
        }
    }

    private void SetWorkingState()
    {
        _launchButton.Enabled = false;
        _retryButton.Enabled = false;
        _closeButton.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
    }

    private void SetIdleState(bool canLaunch)
    {
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Value = 0;
        _launchButton.Enabled = canLaunch;
        _retryButton.Enabled = true;
        _closeButton.Enabled = true;
    }

    private void UpdateStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private static void TryDeleteFile(string path)
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Impossible de supprimer le dossier temporaire {path}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Impossible de supprimer le dossier temporaire {path}: {exception.Message}");
        }
    }
}
