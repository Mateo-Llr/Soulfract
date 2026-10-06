using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoulfractLauncher;

internal sealed class LauncherForm : Form
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/Mateo-Llr/Soulfract/releases/latest";
    private const string ChangelogUrlTemplate = "https://raw.githubusercontent.com/Mateo-Llr/Soulfract/{0}/Data/changelog.md";
    private const string GameArchiveName = "Soulfract-win-x64.zip";
    private const string DeltaArchivePrefix = "Soulfract-win-x64-delta-from-";
    private const string DeltaManifestName = "soulfract-delta-manifest.json";
    private const string GameExecutableName = "Soulfract.exe";
    private const string GameVersionFileName = ".soulfract-version";
    private const string LauncherExecutableName = "SoulfractLauncher.exe";
    private const string BootstrapperAssetName = "SoulfractBootstrapper.exe";
    private const string LauncherVersionFileName = ".soulfract-launcher-version";
    private const int CardRadius = 18;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions DeltaManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Color BackgroundColor = Color.FromArgb(13, 11, 18);
    private static readonly Color SurfaceColor = Color.FromArgb(25, 20, 31);
    private static readonly Color ElevatedColor = Color.FromArgb(38, 30, 46);
    private static readonly Color AccentColor = Color.FromArgb(226, 179, 105);
    private static readonly Color AddedColor = Color.FromArgb(126, 207, 151);
    private static readonly Color FixedColor = Color.FromArgb(119, 174, 232);
    private static readonly Color ChangedColor = Color.FromArgb(232, 190, 112);
    private static readonly Color PerformanceColor = Color.FromArgb(190, 153, 231);
    private static readonly Color RemovedColor = Color.FromArgb(222, 132, 132);
    private static readonly Font ChangelogHeadingFont = new("Segoe UI Semibold", 12F, FontStyle.Bold);
    private static readonly Font ChangelogCategoryFont = new("Segoe UI Semibold", 8.5F, FontStyle.Bold);
    private static readonly Font ChangelogEntryFont = new("Segoe UI", 10.5F, FontStyle.Regular);
    private static readonly Font ChangelogPlainFont = new("Segoe UI", 10F, FontStyle.Regular);
    private static readonly Color PrimaryTextColor = Color.FromArgb(246, 241, 234);
    private static readonly Color MutedTextColor = Color.FromArgb(172, 161, 179);

    private readonly string _installDirectory;
    private readonly Label _versionLabel;
    private Label _statusLabel = new();
    private RichTextBox _changelogBox = new();
    private ProgressBar _progressBar = new();
    private Button _playButton = new();
    private Button _checkUpdatesButton = new();
    private Button _folderButton = new();
    private Panel _changelogPanel = new();
    private bool _changelogVisible = false;
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
        MinimumSize = new Size(900, 660);
        ClientSize = new Size(1120, 740);
        BackColor = BackgroundColor;
        ForeColor = PrimaryTextColor;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(26, 12, 26, 12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 202));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        var versionCaption = CreateLabel("VERSION", 8F, FontStyle.Bold, MutedTextColor);
        versionCaption.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        versionCaption.TextAlign = ContentAlignment.MiddleLeft;
        versionCaption.SetBounds(0, 4, 300, 17);
        _versionLabel = CreateLabel("Vérification...", 10, FontStyle.Bold, AccentColor);
        _versionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        _versionLabel.SetBounds(0, 21, 300, 22);
        
        var changelogButton = CreateButton("📋", ElevatedColor, PrimaryTextColor, 32, 32);
        changelogButton.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        changelogButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        changelogButton.TextAlign = ContentAlignment.MiddleCenter;
        changelogButton.Click += (_, _) => ToggleChangelog();
        
        header.Controls.AddRange([versionCaption, _versionLabel, changelogButton]);
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        var hero = new HeroBackdropPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 12)
        };
        BuildHero(hero);
        root.Controls.Add(hero, 0, 1);
        root.SetColumnSpan(hero, 2);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(content, 0, 2);

        _changelogPanel = CreateCard();
        _changelogPanel.Dock = DockStyle.Fill;
        _changelogPanel.Margin = new Padding(0, 0, 12, 0);
        _changelogPanel.Visible = false;
        BuildChangelogCard(_changelogPanel);
        root.Controls.Add(_changelogPanel, 1, 2);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(0, 0, 0, 2)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _statusLabel = CreateLabel("Connexion...", 9F, FontStyle.Regular, MutedTextColor);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.AutoEllipsis = true;
        actions.Controls.Add(_statusLabel, 0, 0);
        actions.SetColumnSpan(_statusLabel, 3);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = true,
            Margin = new Padding(140, 0, 140, 0)
        };
        actions.Controls.Add(_progressBar, 0, 1);
        actions.SetColumnSpan(_progressBar, 3);

        _checkUpdatesButton = CreateButton("VÉRIFIER", ElevatedColor, PrimaryTextColor, 190, 46);
        _checkUpdatesButton.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _checkUpdatesButton.Anchor = AnchorStyles.None;
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();
        actions.Controls.Add(_checkUpdatesButton, 0, 2);

        _playButton = CreateButton("▶   JOUER", AccentColor, BackgroundColor, 244, 56);
        _playButton.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        _playButton.Enabled = false;
        _playButton.Anchor = AnchorStyles.None;
        _playButton.Click += (_, _) => LaunchInstalledGame();
        actions.Controls.Add(_playButton, 1, 2);

        _folderButton = CreateButton("DOSSIER DU JEU", ElevatedColor, MutedTextColor, 190, 46);
        _folderButton.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        _folderButton.Enabled = false;
        _folderButton.Anchor = AnchorStyles.None;
        _folderButton.Click += (_, _) => OpenGameFolder();
        actions.Controls.Add(_folderButton, 2, 2);
        root.Controls.Add(actions, 0, 3);
        root.SetColumnSpan(actions, 2);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        var footerLink = CreateLinkLabel("GitHub", 8.5F, "https://github.com/Mateo-Llr/Soulfract");
        footerLink.Dock = DockStyle.Right;
        footerLink.TextAlign = ContentAlignment.MiddleRight;
        footer.Controls.Add(footerLink);
        root.Controls.Add(footer, 0, 4);
        root.SetColumnSpan(footer, 2);

        Shown += async (_, _) => await CheckForUpdatesAsync();
    }

    private void ToggleChangelog()
    {
        _changelogVisible = !_changelogVisible;
        _changelogPanel.Visible = _changelogVisible;
    }

    private void BuildHero(Panel hero)
    {
        var banner = new PixelPictureBox
        {
            Image = LoadGameBannerTexture(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TabStop = false
        };
        hero.Controls.Add(banner);

        PixelPictureBox titleTexture = new()
        {
            Image = LoadTitleTexture(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            AutoSize = false,
            Size = new Size(260, 174),
            TabStop = false
        };
        hero.Resize += (_, _) =>
        {
            titleTexture.Location = new Point(
                (hero.Width - titleTexture.Width) / 2,
                (hero.Height - titleTexture.Height) / 2);
        };
        titleTexture.Location = new Point(
            (hero.Width - titleTexture.Width) / 2,
            (hero.Height - titleTexture.Height) / 2);
        hero.Controls.Add(titleTexture);
    }

    private void BuildChangelogCard(Panel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(22, 15, 22, 15)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var title = CreateLabel("Nouveautés", 17, FontStyle.Bold, PrimaryTextColor);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(title, 0, 0);

        _changelogBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SurfaceColor,
            ForeColor = PrimaryTextColor,
            Font = new Font("Segoe UI", 10.5F),
            DetectUrls = false,
            HideSelection = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Text = "Chargement du journal des modifications..."
        };
        layout.Controls.Add(_changelogBox, 0, 1);
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
            if (await UpdateLauncherIfNeededAsync(root))
                return;

            GameArchiveAsset? fullArchive = FindGameArchive(root, GameArchiveName);
            if (fullArchive == null)
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

            GameArchiveAsset? deltaArchive = hasInstalledGame && !string.IsNullOrWhiteSpace(installedVersion)
                ? FindGameArchive(root, $"{DeltaArchivePrefix}{installedVersion}.zip")
                : null;
            bool useDelta = deltaArchive != null;
            GameArchiveAsset selectedArchive = deltaArchive ?? fullArchive.Value;
            UpdateStatus(useDelta
                ? $"Téléchargement des fichiers modifiés pour {_latestVersion}..."
                : hasInstalledGame
                    ? $"Téléchargement complet de la mise à jour {_latestVersion}..."
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
            await DownloadArchiveAsync(selectedArchive, archivePath, progress);

            UpdateStatus("Vérification de l'archive...");
            Directory.CreateDirectory(extractionDirectory);
            ExtractArchiveSafely(archivePath, extractionDirectory);

            if (useDelta)
            {
                string manifestPath = Path.Combine(extractionDirectory, DeltaManifestName);
                if (!File.Exists(manifestPath))
                    throw new InvalidDataException("L'archive différentielle ne contient pas son manifeste.");

                DeltaManifest manifest = JsonSerializer.Deserialize<DeltaManifest>(
                    await File.ReadAllTextAsync(manifestPath),
                    DeltaManifestJsonOptions)
                    ?? throw new InvalidDataException("Le manifeste de mise à jour est invalide.");
                if (!string.Equals(manifest.BaseVersion, installedVersion, StringComparison.Ordinal)
                    || manifest.DeletedFiles == null)
                    throw new InvalidDataException("La mise à jour différentielle ne correspond pas à la version installée.");

                UpdateStatus("Installation des fichiers modifiés...");
                CopyGameFiles(extractionDirectory, _installDirectory, preservePlayerData: true, skipDeltaManifest: true);
                DeleteObsoleteGameFiles(manifest.DeletedFiles);
            }
            else
            {
                if (!File.Exists(Path.Combine(extractionDirectory, GameExecutableName)))
                    throw new InvalidDataException($"L'archive téléchargée ne contient pas {GameExecutableName}.");

                UpdateStatus("Installation complète du jeu...");
                CopyGameFiles(extractionDirectory, _installDirectory, preservePlayerData: hasInstalledGame);
            }

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

    private async Task<bool> UpdateLauncherIfNeededAsync(JsonElement release)
    {
        LauncherUpdateAsset launcherAsset = FindLauncherUpdateAsset(release, LauncherExecutableName)
            ?? throw new InvalidDataException($"La release {_latestVersion} ne contient pas de lanceur vérifiable.");
        LauncherUpdateAsset updaterAsset = FindLauncherUpdateAsset(release, BootstrapperAssetName)
            ?? throw new InvalidDataException($"La release {_latestVersion} ne contient pas le composant de mise à jour du lanceur.");

        string currentLauncherPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Le chemin du lanceur en cours d'exécution est introuvable.");
        if (!string.Equals(
                Path.GetFileName(currentLauncherPath),
                LauncherExecutableName,
                StringComparison.OrdinalIgnoreCase))
            return false;

        string currentHash;
        await using (FileStream currentLauncher = File.OpenRead(currentLauncherPath))
            currentHash = Convert.ToHexString(await SHA256.HashDataAsync(currentLauncher));

        if (string.Equals(currentHash, launcherAsset.Sha256, StringComparison.OrdinalIgnoreCase))
            return false;

        UpdateStatus($"Mise à jour du lanceur vers {_latestVersion}...");
        SetWorkingState();

        string launcherDirectory = Path.GetDirectoryName(currentLauncherPath)
            ?? throw new InvalidOperationException("Le dossier du lanceur est introuvable.");
        string stagedLauncherPath = Path.Combine(
            launcherDirectory,
            $".{LauncherExecutableName}.{Guid.NewGuid():N}.tmp");
        string updaterPath = Path.Combine(
            Path.GetTempPath(),
            $"SoulfractLauncherUpdater-{Guid.NewGuid():N}.exe");
        bool handedOff = false;

        try
        {
            await DownloadVerifiedReleaseAssetAsync(launcherAsset, stagedLauncherPath);
            await DownloadVerifiedReleaseAssetAsync(updaterAsset, updaterPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = launcherDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--replace-launcher");
            startInfo.ArgumentList.Add(currentLauncherPath);
            startInfo.ArgumentList.Add(stagedLauncherPath);
            startInfo.ArgumentList.Add(Path.Combine(launcherDirectory, LauncherVersionFileName));
            startInfo.ArgumentList.Add(launcherAsset.Sha256);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(launcherDirectory);

            if (Process.Start(startInfo) == null)
                throw new InvalidOperationException("Le composant de mise à jour du lanceur n'a pas pu démarrer.");

            handedOff = true;
            Close();
            return true;
        }
        finally
        {
            if (!handedOff)
            {
                TryDeleteFile(stagedLauncherPath);
                TryDeleteFile(updaterPath);
            }
        }
    }

    private static LauncherUpdateAsset? FindLauncherUpdateAsset(JsonElement release, string assetName)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets))
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), assetName, StringComparison.Ordinal))
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
                return new LauncherUpdateAsset(parsedUrl.AbsoluteUri, digest[7..]);
        }

        return null;
    }

    private static async Task DownloadVerifiedReleaseAssetAsync(LauncherUpdateAsset asset, string destination)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await input.CopyToAsync(output);
        await output.FlushAsync();

        if (output.Length < 1024 * 1024)
            throw new InvalidDataException("Le fichier de mise à jour téléchargé est trop petit pour être un exécutable valide.");

        output.Position = 0;
        if (output.ReadByte() != 'M' || output.ReadByte() != 'Z')
            throw new InvalidDataException("Le fichier de mise à jour téléchargé n'est pas un exécutable Windows.");

        output.Position = 0;
        string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(output));
        if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La vérification d'intégrité du composant de mise à jour a échoué.");
    }

    private readonly record struct LauncherUpdateAsset(string DownloadUrl, string Sha256);

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
            .TakeLast(50)
            .Reverse()
            .ToList();
        if (selectedSections.Count == 0)
            return "Aucune entrée récente n'est disponible dans le changelog.";

        var formatted = new System.Text.StringBuilder();
        foreach ((string title, List<string> entries) in selectedSections)
        {
            formatted.AppendLine($"## {title}");
            foreach (string entry in entries)
                formatted.AppendLine(entry);
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
                if (_changelogBox.TextLength > 0)
                    _changelogBox.AppendText(Environment.NewLine);
                _changelogBox.SelectionColor = AccentColor;
                _changelogBox.SelectionFont = ChangelogHeadingFont;
                _changelogBox.AppendText(line[3..] + Environment.NewLine);
            }
            else if (TrySplitChangelogEntry(line, out string category, out string description))
            {
                Color categoryColor = category switch
                {
                    "ADDED" => AddedColor,
                    "FIXED" => FixedColor,
                    "CHANGED" => ChangedColor,
                    "PERF" => PerformanceColor,
                    "REMOVED" => RemovedColor,
                    _ => PrimaryTextColor
                };
                string categoryLabel = category switch
                {
                    "ADDED" => "AJOUT",
                    "FIXED" => "CORRECTION",
                    "CHANGED" => "MODIFIÉ",
                    "PERF" => "PERF",
                    "REMOVED" => "RETIRÉ",
                    _ => category
                };

                _changelogBox.SelectionIndent = 0;
                _changelogBox.SelectionColor = categoryColor;
                _changelogBox.SelectionFont = ChangelogCategoryFont;
                _changelogBox.AppendText(categoryLabel.PadRight(12));
                _changelogBox.SelectionColor = PrimaryTextColor;
                _changelogBox.SelectionFont = ChangelogEntryFont;
                _changelogBox.AppendText(description + Environment.NewLine);
            }
            else
            {
                _changelogBox.SelectionColor = MutedTextColor;
                _changelogBox.SelectionFont = ChangelogPlainFont;
                _changelogBox.AppendText(line + Environment.NewLine);
            }
        }

        _changelogBox.SelectionIndent = 0;
        _changelogBox.Select(0, 0);
    }

    private static bool TrySplitChangelogEntry(string line, out string category, out string description)
    {
        category = "";
        description = "";
        if (line.Length < 4 || line[0] != '[')
            return false;

        int closingBracket = line.IndexOf(']');
        if (closingBracket <= 1 || closingBracket + 1 >= line.Length)
            return false;

        category = line[1..closingBracket].Trim().ToUpperInvariant();
        description = line[(closingBracket + 1)..].Trim();
        return category is "ADDED" or "FIXED" or "CHANGED" or "PERF" or "REMOVED";
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

    private static GameArchiveAsset? FindGameArchive(JsonElement release, string assetName)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets))
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), assetName, StringComparison.Ordinal))
                continue;

            string? url = asset.GetProperty("browser_download_url").GetString();
            string? digest = asset.TryGetProperty("digest", out JsonElement digestElement)
                ? digestElement.GetString()
                : null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUrl)
                || parsedUrl.Scheme != Uri.UriSchemeHttps)
                return null;

            string? sha256 = digest is { Length: 71 }
                && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                && digest[7..].All(Uri.IsHexDigit)
                    ? digest[7..]
                    : null;
            return new GameArchiveAsset(parsedUrl.AbsoluteUri, sha256);
        }

        return null;
    }

    private readonly record struct GameArchiveAsset(string DownloadUrl, string? Sha256);

    private sealed class DeltaManifest
    {
        public string BaseVersion { get; set; } = "";
        public List<string> DeletedFiles { get; set; } = new();
    }

    private static async Task DownloadArchiveAsync(
        GameArchiveAsset archive,
        string destination,
        IProgress<(long Downloaded, long? Total)> progress)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(archive.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
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

        if (archive.Sha256 != null)
        {
            await using FileStream downloadedArchive = File.OpenRead(destination);
            byte[] actualHash = await System.Security.Cryptography.SHA256.HashDataAsync(downloadedArchive);
            string actualSha256 = Convert.ToHexString(actualHash);
            if (!string.Equals(actualSha256, archive.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La vérification d'intégrité de l'archive téléchargée a échoué.");
        }
    }

    private static void CopyGameFiles(
        string sourceDirectory,
        string destinationDirectory,
        bool preservePlayerData,
        bool skipDeltaManifest = false)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            if ((skipDeltaManifest && string.Equals(relativePath, DeltaManifestName, StringComparison.OrdinalIgnoreCase))
                || (preservePlayerData && IsPlayerData(relativePath)))
                continue;

            string destinationPath = GetSafeGamePath(destinationDirectory, relativePath);
            string? parentDirectory = Path.GetDirectoryName(destinationPath);
            if (parentDirectory != null)
                Directory.CreateDirectory(parentDirectory);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private void DeleteObsoleteGameFiles(IEnumerable<string> relativePaths)
    {
        foreach (string relativePath in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || IsPlayerData(relativePath))
                continue;

            string targetPath = GetSafeGamePath(_installDirectory, relativePath);
            if (File.Exists(targetPath))
                File.Delete(targetPath);
        }
    }

    private static string GetSafeGamePath(string rootDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Le manifeste contient un chemin de fichier invalide.");

        string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(root, normalizedPath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le manifeste contient un chemin de fichier en dehors du dossier du jeu.");

        return fullPath;
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
            or CryptographicException
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

    private static Image LoadTitleTexture()
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
            "SoulfractLauncher.Assets.title.png")
            ?? throw new InvalidOperationException("Le logo intégré du lanceur est introuvable.");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Image LoadGameBannerTexture()
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
            "SoulfractLauncher.Assets.game_banner.png")
            ?? throw new InvalidOperationException("La bannière du jeu n'est pas trouvée.");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
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

    private sealed class HeroBackdropPanel : Panel
    {
        public HeroBackdropPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            Graphics graphics = e.Graphics;
            graphics.Clear(BackgroundColor);
        }
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
            using var border = new Pen(Color.FromArgb(72, 218, 177, 124));
            e.Graphics.DrawPath(border, path);
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

    private sealed class PixelPictureBox : PictureBox
    {
        protected override void OnPaint(PaintEventArgs pe)
        {
            if (Image != null)
            {
                pe.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                pe.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            }
            base.OnPaint(pe);
        }
    }
}
