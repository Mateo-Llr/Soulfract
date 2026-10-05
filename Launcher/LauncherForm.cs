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
    private const string DeltaArchivePrefix = "Soulfract-win-x64-delta-from-";
    private const string DeltaManifestName = "soulfract-delta-manifest.json";
    private const string GameExecutableName = "Soulfract.exe";
    private const string GameVersionFileName = ".soulfract-version";
    private const int CardRadius = 18;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Color BackgroundColor = Color.FromArgb(15, 12, 22);
    private static readonly Color SurfaceColor = Color.FromArgb(27, 22, 35);
    private static readonly Color ElevatedColor = Color.FromArgb(41, 32, 50);
    private static readonly Color AccentColor = Color.FromArgb(220, 174, 104);
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
        MinimumSize = new Size(860, 650);
        ClientSize = new Size(1180, 780);
        BackColor = BackgroundColor;
        ForeColor = PrimaryTextColor;
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(26, 14, 26, 14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 276));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        Image logo = LoadLauncherLogo();
        var logoView = new PictureBox
        {
            Image = logo,
            Location = new Point(0, 3),
            Size = new Size(150, 56),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = BackgroundColor
        };
        var subtitle = CreateLabel("UN MONDE À FAÇONNER", 8.5F, FontStyle.Bold, MutedTextColor);
        subtitle.Location = new Point(158, 21);
        subtitle.AutoSize = true;
        var versionCaption = CreateLabel("VERSION DU JEU", 8.5F, FontStyle.Bold, MutedTextColor);
        versionCaption.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        versionCaption.TextAlign = ContentAlignment.MiddleRight;
        versionCaption.SetBounds(770, 8, 300, 18);
        _versionLabel = CreateLabel("Vérification...", 11, FontStyle.Bold, AccentColor);
        _versionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _versionLabel.TextAlign = ContentAlignment.MiddleRight;
        _versionLabel.SetBounds(770, 27, 300, 24);
        header.Controls.AddRange([logoView, subtitle, versionCaption, _versionLabel]);
        root.Controls.Add(header, 0, 0);

        var hero = new HeroBackdropPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 14)
        };
        BuildHero(hero);
        root.Controls.Add(hero, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 8)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        root.Controls.Add(content, 0, 2);

        var gameCard = CreateCard();
        gameCard.Dock = DockStyle.Fill;
        gameCard.Margin = new Padding(0, 0, 9, 0);
        BuildGameCard(gameCard);
        content.Controls.Add(gameCard, 0, 0);

        var changelogCard = CreateCard();
        changelogCard.Dock = DockStyle.Fill;
        changelogCard.Margin = new Padding(9, 0, 0, 0);
        BuildChangelogCard(changelogCard);
        content.Controls.Add(changelogCard, 1, 0);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        var footerLabel = CreateLabel("Explorez. Construisez. Survivez.", 8.5F, FontStyle.Regular, MutedTextColor);
        footerLabel.Dock = DockStyle.Left;
        footerLabel.TextAlign = ContentAlignment.MiddleLeft;
        var footerLink = CreateLinkLabel("COMMUNAUTÉ  ↗", 8.5F, "https://github.com/Mateo-Llr/Soulfract");
        footerLink.Dock = DockStyle.Right;
        footerLink.TextAlign = ContentAlignment.MiddleRight;
        footer.Controls.AddRange([footerLabel, footerLink]);
        root.Controls.Add(footer, 0, 3);
        FormClosed += (_, _) => logo.Dispose();

        Shown += async (_, _) => await CheckForUpdatesAsync();
    }

    private void BuildHero(Panel hero)
    {
        var eyebrow = CreateLabel("AVENTURE • SURVIE • MULTIJOUEUR", 9, FontStyle.Bold, AccentColor);
        eyebrow.Location = new Point(38, 42);
        eyebrow.AutoSize = true;

        var title = CreateLabel("Votre monde.\nVos règles.", 32, FontStyle.Bold, PrimaryTextColor);
        title.Location = new Point(34, 74);
        title.Size = new Size(520, 102);
        title.TextAlign = ContentAlignment.MiddleLeft;

        var description = CreateLabel(
            "Explorez des terres inconnues, bâtissez votre refuge\net écrivez votre propre histoire.",
            11F,
            FontStyle.Regular,
            Color.FromArgb(222, 213, 224));
        description.Location = new Point(39, 181);
        description.Size = new Size(500, 42);
        description.TextAlign = ContentAlignment.TopLeft;

        var worldLabel = CreateLabel("UN UNIVERS QUI N'ATTEND QUE VOUS", 8.5F, FontStyle.Bold, Color.FromArgb(222, 213, 224));
        worldLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        worldLabel.TextAlign = ContentAlignment.MiddleRight;
        worldLabel.SetBounds(690, 32, 420, 22);

        hero.Controls.AddRange([eyebrow, title, description, worldLabel]);
    }

    private void BuildGameCard(Panel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(20, 18, 18, 16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        card.Controls.Add(layout);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0, 0, 12, 0)
        };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        layout.Controls.Add(details, 0, 0);

        var eyebrow = CreateLabel("PRÊT À JOUER", 8.5F, FontStyle.Bold, AccentColor);
        eyebrow.Dock = DockStyle.Fill;
        eyebrow.TextAlign = ContentAlignment.MiddleLeft;
        details.Controls.Add(eyebrow, 0, 0);

        var title = CreateLabel("Soulfract", 21, FontStyle.Bold, PrimaryTextColor);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        details.Controls.Add(title, 0, 1);

        var description = CreateLabel("Le monde vous attend.", 9.5F, FontStyle.Regular, MutedTextColor);
        description.Dock = DockStyle.Fill;
        description.TextAlign = ContentAlignment.MiddleLeft;
        details.Controls.Add(description, 0, 2);

        _statusLabel = CreateLabel("Connexion à GitHub...", 10, FontStyle.Regular, MutedTextColor);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        details.Controls.Add(_statusLabel, 0, 3);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = true
        };
        details.Controls.Add(_progressBar, 0, 4);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 3, 0, 0),
            Anchor = AnchorStyles.None
        };
        _playButton = CreateButton("▶   JOUER", AccentColor, BackgroundColor, 176, 48);
        _playButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _playButton.Enabled = false;
        _playButton.Click += (_, _) => LaunchInstalledGame();
        buttonPanel.Controls.Add(_playButton);

        _checkUpdatesButton = CreateButton("Vérifier les mises à jour", ElevatedColor, PrimaryTextColor, 176, 34);
        _checkUpdatesButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();
        buttonPanel.Controls.Add(_checkUpdatesButton);

        _folderButton = CreateButton("Ouvrir le dossier", SurfaceColor, MutedTextColor, 176, 28);
        _folderButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        _folderButton.Enabled = false;
        _folderButton.Click += (_, _) => OpenGameFolder();
        buttonPanel.Controls.Add(_folderButton);
        layout.Controls.Add(buttonPanel, 1, 0);
    }

    private void BuildChangelogCard(Panel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22, 16, 22, 16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
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

                DeltaManifest manifest = JsonSerializer.Deserialize<DeltaManifest>(await File.ReadAllTextAsync(manifestPath))
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

    private static Image LoadLauncherLogo()
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
            "SoulfractLauncher.Assets.soulfract-logo.png")
            ?? throw new InvalidOperationException("Le logo intégré du lanceur est introuvable.");
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
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var sky = new LinearGradientBrush(
                bounds,
                Color.FromArgb(78, 44, 93),
                Color.FromArgb(23, 19, 33),
                LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(sky, bounds);
            }

            float scaleX = bounds.Width / 1120F;
            float scaleY = bounds.Height / 260F;
            RectangleF sunGlow = new(760 * scaleX, 10 * scaleY, 300 * scaleX, 300 * scaleY);
            using (var glow = new SolidBrush(Color.FromArgb(24, 240, 182, 126)))
                graphics.FillEllipse(glow, sunGlow);
            using (var sun = new SolidBrush(Color.FromArgb(80, 229, 177, 127)))
                graphics.FillEllipse(sun, 855 * scaleX, 70 * scaleY, 106 * scaleX, 106 * scaleY);

            PointF[] distantRidge =
            [
                new(0, 175 * scaleY),
                new(145 * scaleX, 115 * scaleY),
                new(260 * scaleX, 167 * scaleY),
                new(405 * scaleX, 105 * scaleY),
                new(548 * scaleX, 171 * scaleY),
                new(735 * scaleX, 112 * scaleY),
                new(910 * scaleX, 165 * scaleY),
                new(1060 * scaleX, 103 * scaleY),
                new(bounds.Width, 155 * scaleY),
                new(bounds.Width, bounds.Height),
                new(0, bounds.Height)
            ];
            using (var distantLand = new SolidBrush(Color.FromArgb(76, 53, 85)))
                graphics.FillPolygon(distantLand, distantRidge);

            PointF[] foregroundRidge =
            [
                new(0, 213 * scaleY),
                new(150 * scaleX, 171 * scaleY),
                new(320 * scaleX, 210 * scaleY),
                new(510 * scaleX, 158 * scaleY),
                new(690 * scaleX, 204 * scaleY),
                new(875 * scaleX, 151 * scaleY),
                new(bounds.Width, 200 * scaleY),
                new(bounds.Width, bounds.Height),
                new(0, bounds.Height)
            ];
            using (var foregroundLand = new SolidBrush(Color.FromArgb(186, 29, 28, 42)))
                graphics.FillPolygon(foregroundLand, foregroundRidge);

            PointF[] stars =
            [
                new(660 * scaleX, 57 * scaleY),
                new(725 * scaleX, 111 * scaleY),
                new(1032 * scaleX, 51 * scaleY),
                new(1090 * scaleX, 118 * scaleY),
                new(580 * scaleX, 132 * scaleY),
                new(985 * scaleX, 178 * scaleY)
            ];
            using (var star = new SolidBrush(Color.FromArgb(115, 239, 194, 151)))
            {
                foreach (PointF point in stars)
                    graphics.FillRectangle(star, point.X, point.Y, Math.Max(2, 4 * scaleX), Math.Max(2, 4 * scaleY));
            }

            using (var border = new Pen(Color.FromArgb(92, 213, 174, 132)))
                graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
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
}
