using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoulfractLauncher;

internal sealed class LauncherForm : Form
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/Mateo-Llr/Soulfract/releases/latest";
    private const string ChangelogUrlTemplate = "https://raw.githubusercontent.com/Mateo-Llr/Soulfract/{0}/Data/changelog.md";
    private const string GameArchiveName = "Soulfract-win-x64.zip";
    private const string GameExecutableName = "Soulfract.exe";
    private const string GameVersionFileName = ".soulfract-version";
    private const int CardRadius = 18;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Color BackgroundColor = Color.FromArgb(11, 15, 22);
    private static readonly Color SurfaceColor = Color.FromArgb(20, 27, 37);
    private static readonly Color ElevatedColor = Color.FromArgb(27, 36, 49);
    private static readonly Color AccentColor = Color.FromArgb(225, 179, 94);
    private static readonly Color PrimaryTextColor = Color.FromArgb(239, 240, 242);
    private static readonly Color MutedTextColor = Color.FromArgb(151, 162, 177);

    private readonly string _installDirectory;
    private readonly Label _versionLabel;
    private Label _statusLabel = new();
    private RichTextBox _changelogBox = new();
    private ProgressBar _progressBar = new();
    private Button _playButton = new();
    private Button _checkUpdatesButton = new();
    private Button _folderButton = new();
    private bool _isWorking;
    private string? _latestVersion;

    public LauncherForm()
    {
        string launcherDirectory = AppContext.BaseDirectory;
        _installDirectory = File.Exists(Path.Combine(launcherDirectory, GameExecutableName))
            ? launcherDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soulfract");

        Text = "Soulfract";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(800, 600);
        ClientSize = new Size(1120, 720);
        BackColor = BackgroundColor;
        ForeColor = PrimaryTextColor;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(30, 16, 30, 16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        var brand = CreateLabel("SOULFRACT", 23, FontStyle.Bold, AccentColor);
        brand.Location = new Point(2, 15);
        brand.AutoSize = true;
        var subtitle = CreateLabel("MONDE • AVENTURE • MULTIJOUEUR", 8.5F, FontStyle.Regular, MutedTextColor);
        subtitle.Location = new Point(4, 48);
        subtitle.AutoSize = true;
        var versionCaption = CreateLabel("VERSION", 8.5F, FontStyle.Bold, MutedTextColor);
        versionCaption.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        versionCaption.TextAlign = ContentAlignment.MiddleRight;
        versionCaption.SetBounds(770, 12, 275, 18);
        _versionLabel = CreateLabel("Vérification...", 11, FontStyle.Bold, PrimaryTextColor);
        _versionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _versionLabel.TextAlign = ContentAlignment.MiddleRight;
        _versionLabel.SetBounds(770, 31, 275, 24);
        header.Controls.AddRange([brand, subtitle, versionCaption, _versionLabel]);
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 12, 0, 12)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        root.Controls.Add(content, 0, 1);

        var gameCard = CreateCard();
        gameCard.Dock = DockStyle.Fill;
        gameCard.Margin = new Padding(0, 0, 12, 0);
        BuildGameCard(gameCard);
        content.Controls.Add(gameCard, 0, 0);

        var changelogCard = CreateCard();
        changelogCard.Dock = DockStyle.Fill;
        changelogCard.Margin = new Padding(12, 0, 0, 0);
        BuildChangelogCard(changelogCard);
        content.Controls.Add(changelogCard, 1, 0);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        var footerLabel = CreateLabel("Soulfract  •  Le monde vous attend.", 9, FontStyle.Regular, MutedTextColor);
        footerLabel.Dock = DockStyle.Left;
        footerLabel.TextAlign = ContentAlignment.MiddleLeft;
        var footerLink = CreateLinkLabel("GitHub", 9, "https://github.com/Mateo-Llr/Soulfract");
        footerLink.Dock = DockStyle.Right;
        footerLink.TextAlign = ContentAlignment.MiddleRight;
        footer.Controls.AddRange([footerLabel, footerLink]);
        root.Controls.Add(footer, 0, 2);

        Shown += async (_, _) => await CheckForUpdatesAsync();
    }

    private void BuildGameCard(Panel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(30, 28, 30, 28)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        card.Controls.Add(layout);

        var eyebrow = CreateLabel("VOTRE PROCHAINE AVENTURE", 9, FontStyle.Bold, AccentColor);
        eyebrow.Dock = DockStyle.Fill;
        eyebrow.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(eyebrow, 0, 0);

        var title = CreateLabel("Explorez.\nConstruisez. Survivez.", 28, FontStyle.Bold, PrimaryTextColor);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(title, 0, 1);

        var description = CreateLabel("Un monde vivant à découvrir seul ou avec vos amis.", 11, FontStyle.Regular, MutedTextColor);
        description.Dock = DockStyle.Fill;
        description.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(description, 0, 2);

        _statusLabel = CreateLabel("Connexion à GitHub...", 10, FontStyle.Regular, MutedTextColor);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.BottomLeft;
        _statusLabel.AutoEllipsis = true;
        layout.Controls.Add(_statusLabel, 0, 3);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 8,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = true
        };
        layout.Controls.Add(_progressBar, 0, 4);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 10, 0, 0)
        };
        _playButton = CreateButton("JOUER", AccentColor, BackgroundColor, 190, 54);
        _playButton.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _playButton.Enabled = false;
        _playButton.Click += (_, _) => LaunchInstalledGame();
        buttonPanel.Controls.Add(_playButton);

        _checkUpdatesButton = CreateButton("Rechercher les mises à jour", ElevatedColor, PrimaryTextColor, 230, 38);
        _checkUpdatesButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();
        buttonPanel.Controls.Add(_checkUpdatesButton);
        layout.Controls.Add(buttonPanel, 0, 5);

        _folderButton = CreateButton("Ouvrir le dossier du jeu", SurfaceColor, MutedTextColor, 215, 32);
        _folderButton.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        _folderButton.Enabled = false;
        _folderButton.Click += (_, _) => OpenGameFolder();
        layout.Controls.Add(_folderButton, 0, 6);
    }

    private void BuildChangelogCard(Panel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(26, 24, 26, 24)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var title = CreateLabel("Quoi de neuf ?", 19, FontStyle.Bold, PrimaryTextColor);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(title, 0, 0);

        var hint = CreateLabel("Les dernières nouveautés de Soulfract", 9.5F, FontStyle.Regular, MutedTextColor);
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(hint, 0, 1);

        _changelogBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SurfaceColor,
            ForeColor = PrimaryTextColor,
            Font = new Font("Segoe UI", 10F),
            DetectUrls = false,
            HideSelection = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Text = "Chargement du journal des modifications..."
        };
        layout.Controls.Add(_changelogBox, 0, 2);
    }

    private async Task CheckForUpdatesAsync()
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
            _latestVersion = root.GetProperty("tag_name").GetString()
                ?? throw new InvalidDataException("La release GitHub n'a pas de numéro de version.");
            string? downloadUrl = FindGameArchiveUrl(root);
            if (downloadUrl == null)
                throw new InvalidDataException($"La release {_latestVersion} ne contient pas {GameArchiveName}.");

            _versionLabel.Text = _latestVersion;
            await LoadChangelogAsync(_latestVersion, root);

            string versionPath = Path.Combine(_installDirectory, GameVersionFileName);
            string? installedVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : null;
            string gamePath = Path.Combine(_installDirectory, GameExecutableName);
            bool hasInstalledGame = File.Exists(gamePath);

            if (hasInstalledGame && string.Equals(installedVersion, _latestVersion, StringComparison.Ordinal))
            {
                UpdateStatus($"Version {_latestVersion} installée et à jour.");
                _folderButton.Enabled = true;
                SetIdleState(canPlay: true);
                return;
            }

            if (hasInstalledGame && IsGameRunning())
            {
                UpdateStatus("Fermez Soulfract pour installer la mise à jour.");
                _folderButton.Enabled = true;
                SetIdleState(canPlay: true);
                return;
            }

            UpdateStatus(hasInstalledGame
                ? $"Téléchargement de la mise à jour {_latestVersion}..."
                : $"Téléchargement de Soulfract {_latestVersion}...");
            var progress = new Progress<(long Downloaded, long? Total)>(value =>
            {
                if (value.Total is > 0)
                {
                    _progressBar.Style = ProgressBarStyle.Continuous;
                    _progressBar.Value = (int)Math.Clamp(value.Downloaded * 100 / value.Total.Value, 0, 100);
                    UpdateStatus($"Téléchargement... {value.Downloaded / 1_048_576} / {value.Total.Value / 1_048_576} Mo");
                }
            });
            await DownloadArchiveAsync(downloadUrl, archivePath, progress);

            UpdateStatus("Vérification de l'archive...");
            Directory.CreateDirectory(extractionDirectory);
            ExtractArchiveSafely(archivePath, extractionDirectory);
            if (!File.Exists(Path.Combine(extractionDirectory, GameExecutableName)))
                throw new InvalidDataException($"L'archive téléchargée ne contient pas {GameExecutableName}.");

            UpdateStatus("Installation de la mise à jour...");
            CopyGameFiles(extractionDirectory, _installDirectory, preservePlayerData: hasInstalledGame);
            string temporaryVersionPath = versionPath + ".tmp";
            await File.WriteAllTextAsync(temporaryVersionPath, _latestVersion);
            File.Move(temporaryVersionPath, versionPath, overwrite: true);

            UpdateStatus($"Soulfract {_latestVersion} est prêt. Cliquez sur Jouer.");
            _folderButton.Enabled = true;
            SetIdleState(canPlay: true);
        }
        catch (Exception exception) when (IsRecoverableLauncherFailure(exception))
        {
            UpdateStatus($"Mise à jour impossible : {exception.Message}");
            if (_latestVersion == null)
                _versionLabel.Text = "Version indisponible";
            _folderButton.Enabled = File.Exists(Path.Combine(_installDirectory, GameExecutableName));
            SetIdleState(canPlay: _folderButton.Enabled);
            if (_changelogBox.Text.StartsWith("Chargement", StringComparison.Ordinal))
                SetChangelogText("Impossible de charger le journal des modifications.\n\nRéessayez lorsque la connexion sera disponible.");
        }
        finally
        {
            _isWorking = false;
            TryDeleteFile(archivePath);
            TryDeleteDirectory(extractionDirectory);
        }
    }

    private async Task LoadChangelogAsync(string version, JsonElement release)
    {
        string? markdown = null;
        string changelogUrl = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            ChangelogUrlTemplate,
            Uri.EscapeDataString(version));

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(changelogUrl);
            if (response.IsSuccessStatusCode)
                markdown = await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException exception)
        {
            UpdateStatus($"Lecture du changelog GitHub indisponible : {exception.Message}");
        }
        catch (TaskCanceledException exception)
        {
            UpdateStatus($"Lecture du changelog GitHub interrompue : {exception.Message}");
        }

        if (!string.IsNullOrWhiteSpace(markdown))
        {
            SetChangelogText(FormatRecentChangelog(markdown));
            return;
        }

        if (release.TryGetProperty("body", out JsonElement body) && !string.IsNullOrWhiteSpace(body.GetString()))
        {
            SetChangelogText(body.GetString()!);
            return;
        }

        SetChangelogText("Aucune note de version n'est disponible pour le moment.");
    }

    private static string FormatRecentChangelog(string markdown)
    {
        bool inChangelog = false;
        var sections = new List<(string Title, List<string> Entries)>();
        (string Title, List<string> Entries)? currentSection = null;

        foreach (string rawLine in markdown.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (!inChangelog)
            {
                if (line.Equals("# Changelog", StringComparison.OrdinalIgnoreCase))
                    inChangelog = true;
                continue;
            }

            if (line.StartsWith("# Potentiels ajouts futurs", StringComparison.OrdinalIgnoreCase))
                break;
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (currentSection.HasValue)
                    sections.Add(currentSection.Value);
                currentSection = (line[4..].Trim(), new List<string>());
                continue;
            }

            if (currentSection.HasValue && line.StartsWith("- ", StringComparison.Ordinal))
                currentSection.Value.Entries.Add(StripInlineMarkdown(line[2..]));
        }

        if (currentSection.HasValue)
            sections.Add(currentSection.Value);

        var selectedSections = sections
            .Where(section => section.Entries.Count > 0)
            .TakeLast(8)
            .Reverse()
            .ToList();
        if (selectedSections.Count == 0)
            return "Aucune entrée récente n'est disponible dans le changelog.";

        var formatted = new System.Text.StringBuilder();
        foreach ((string title, List<string> entries) in selectedSections)
        {
            formatted.AppendLine($"## {title}");
            foreach (string entry in entries)
                formatted.AppendLine($"•  {entry}");
            formatted.AppendLine();
        }

        return formatted.ToString().TrimEnd();
    }

    private static string StripInlineMarkdown(string value)
    {
        string stripped = Regex.Replace(value, @"\*\*(.*?)\*\*|__(.*?)__|`([^`]+)`", match =>
        {
            for (int group = 1; group < match.Groups.Count; group++)
            {
                if (match.Groups[group].Success)
                    return match.Groups[group].Value;
            }

            return match.Value;
        });
        return stripped.Replace("~~", "");
    }

    private void SetChangelogText(string text)
    {
        _changelogBox.Clear();
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                _changelogBox.SelectionColor = AccentColor;
                _changelogBox.SelectionFont = new Font(_changelogBox.Font, FontStyle.Bold);
                _changelogBox.AppendText(line[3..] + Environment.NewLine);
                _changelogBox.SelectionColor = PrimaryTextColor;
                _changelogBox.SelectionFont = _changelogBox.Font;
            }
            else
            {
                _changelogBox.SelectionColor = line.StartsWith("•", StringComparison.Ordinal)
                    ? PrimaryTextColor
                    : MutedTextColor;
                _changelogBox.SelectionFont = _changelogBox.Font;
                _changelogBox.AppendText(line + Environment.NewLine);
            }
        }

        _changelogBox.Select(0, 0);
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
            UpdateStatus("Le jeu n'est pas encore installé. Vérifiez les mises à jour.");
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
        }
    }

    private void OpenGameFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _installDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            UpdateStatus($"Impossible d'ouvrir le dossier : {exception.Message}");
        }
    }

    private void SetWorkingState()
    {
        _playButton.Enabled = false;
        _checkUpdatesButton.Enabled = false;
        _folderButton.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Style = ProgressBarStyle.Marquee;
    }

    private void SetIdleState(bool canPlay)
    {
        _progressBar.Visible = false;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Value = 0;
        _playButton.Enabled = canPlay;
        _checkUpdatesButton.Enabled = true;
    }

    private void UpdateStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private static Label CreateLabel(string text, float size, FontStyle style, Color color)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
    }

    private static Button CreateButton(string text, Color background, Color foreground, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, height),
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0, 0, 0, 10),
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, 0.12F);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, 0.12F);
        return button;
    }

    private static LinkLabel CreateLinkLabel(string text, float size, string url)
    {
        var link = new LinkLabel
        {
            Text = text,
            Font = new Font("Segoe UI", size, FontStyle.Regular),
            LinkColor = MutedTextColor,
            ActiveLinkColor = AccentColor,
            VisitedLinkColor = MutedTextColor,
            BackColor = BackgroundColor,
            AutoSize = false
        };
        link.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception exception) when (
                exception is System.ComponentModel.Win32Exception
                    or InvalidOperationException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                MessageBox.Show(
                    $"Impossible d'ouvrir le lien GitHub : {exception.Message}",
                    "Soulfract",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        return link;
    }

    private static Panel CreateCard()
    {
        var panel = new RoundedCardPanel(CardRadius)
        {
            BackColor = SurfaceColor,
            Padding = new Padding(0)
        };
        return panel;
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

    private sealed class RoundedCardPanel : Panel
    {
        private readonly int _radius;

        public RoundedCardPanel(int radius)
        {
            _radius = radius;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = CreateRoundedPath(ClientRectangle, _radius);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            using GraphicsPath path = CreateRoundedPath(ClientRectangle, _radius);
            Region = new Region(path);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return path;

            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
