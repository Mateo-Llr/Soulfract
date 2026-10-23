using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    private const int ChangelogPanelRadius = 16;
    private const int ChangelogDaysPerPage = 5;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Color BackgroundColor = Color.FromArgb(24, 26, 29);
    private static readonly Color ElevatedColor = Color.FromArgb(39, 42, 47);
    private static readonly Color SurfaceColor = Color.FromArgb(31, 34, 39);
    private static readonly Color BorderColor = Color.FromArgb(59, 63, 70);
    private static readonly Color AccentColor = Color.FromArgb(47, 220, 133);
    private static readonly Color AddedColor = Color.FromArgb(126, 207, 151);
    private static readonly Color FixedColor = Color.FromArgb(119, 174, 232);
    private static readonly Color ChangedColor = Color.FromArgb(232, 190, 112);
    private static readonly Color PerformanceColor = Color.FromArgb(190, 153, 231);
    private static readonly Color RemovedColor = Color.FromArgb(222, 132, 132);
    private static readonly Font ChangelogHeadingFont = new("Segoe UI Black", 24F, FontStyle.Bold);
    private static readonly Font ChangelogMonthFont = new("Segoe UI Semibold", 11F, FontStyle.Bold);
    private static readonly Font ChangelogCategoryFont = new("Segoe UI Semibold", 8F, FontStyle.Bold);
    private static readonly Font ChangelogEntryFont = new("Segoe UI", 10F, FontStyle.Regular);
    private static readonly Font ChangelogPlainFont = new("Segoe UI", 9F, FontStyle.Regular);
    private static readonly Color PrimaryTextColor = Color.FromArgb(239, 242, 244);
    private static readonly Color MutedTextColor = Color.FromArgb(160, 167, 176);
    private static readonly string[] SupportedLanguageCodes = ["cn", "de", "en", "es", "fr", "it", "jp", "pl", "pt", "ru", "se"];

    private readonly string _installDirectory;
    private readonly Label _versionLabel;
    private Label _statusLabel = new();
    private ChangelogScrollPanel _changelogContent = new();
    private string _changelogText = "";
    private List<ChangelogDayGroup> _changelogDays = [];
    private int _visibleChangelogDayCount;
    private Button _changelogLoadMoreButton = new();
    private Label _changelogTitle = new();
    private string? _lastRenderedChangelogMonth;
    private ProgressBar _progressBar = new();
    private Button _playButton = new();
    private Button _checkUpdatesButton = new();
    private Label _updateAvailableLabel = new();
    private Button _folderButton = new();
    private GlassSidebarPanel _changelogPanel = new();
    private Panel _navigationPanel = new();
    private TableLayoutPanel _rootLayout = new();
    private Panel _pageHost = new();
    private HeroBackdropPanel _heroPanel = new();
    private Panel _settingsPage = new();
    private Label _settingsTitle = new();
    private Label _languageLabel = new();
    private ComboBox _languageSelector = new();
    private Button _homeNavigationButton = new();
    private Button _changelogNavigationButton = new();
    private Button _settingsNavigationButton = new();
    private DiscordIconButton _discordButton = null!;
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _resizeRefreshTimer = new() { Interval = 120 };
    private bool _isLayingOutChangelog;
    private bool _isVerticalNavigation;
    private bool _isWorking;
    private bool _gameUpdateAvailable;
    private string? _latestVersion;
    private string _languageCode = "en";
    private string _statusMessage = "";

    private enum LauncherPage
    {
        Home,
        Changelog,
        Settings
    }

    private sealed record LanguageOption(string Code, string NativeName);
    private sealed record ChangelogCategoryGroup(string Category, List<string> Entries);
    private sealed record ChangelogDayGroup(string Title, string Month, List<ChangelogCategoryGroup> Categories);

    public LauncherForm()
    {
        string launcherDirectory = AppContext.BaseDirectory;
        _installDirectory = File.Exists(Path.Combine(launcherDirectory, GameExecutableName))
            ? launcherDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soulfract");

        _languageCode = LoadPreferredLanguage();
        Text = "Soulfract";
        Icon = LoadLauncherIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(760, 540);
        ClientSize = new Size(1240, 780);
        BackColor = BackgroundColor;
        ForeColor = PrimaryTextColor;
        Font = new Font("Segoe UI", 10F, FontStyle.Bold);

        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14)
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(_rootLayout);

        _navigationPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor
        };
        _discordButton = new DiscordIconButton(LoadDiscordIconTexture())
        {
            AccessibleName = "Discord",
            AccessibleDescription = "Rejoindre le serveur Discord de Soulfract",
            Size = new Size(40, 40),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        BuildNavigation();
        _rootLayout.Controls.Add(_navigationPanel, 0, 0);

        _pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            Margin = new Padding(12, 0, 0, 0)
        };
        _rootLayout.Controls.Add(_pageHost, 1, 0);

        _heroPanel = new HeroBackdropPanel { Dock = DockStyle.Fill };
        BuildHero(_heroPanel);
        _changelogPanel = new GlassSidebarPanel
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Visible = false,
            Margin = new Padding(0)
        };
        BuildChangelogCard(_changelogPanel);
        _pageHost.Controls.Add(_changelogPanel);

        BuildSettingsPage();
        _pageHost.Controls.Add(_settingsPage);
        _pageHost.Controls.Add(_heroPanel);

        _versionLabel = CreateLabel("", 9F, FontStyle.Bold, AccentColor);
        _versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        _versionLabel.SetBounds(18, 0, 174, 22);
        _navigationPanel.Controls.Add(_versionLabel);

        _statusLabel = CreateLabel("", 9.5F, FontStyle.Bold, PrimaryTextColor);
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.AutoEllipsis = true;
        _heroPanel.Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = true
        };
        _heroPanel.Controls.Add(_progressBar);

        _playButton = CreateButton("", AccentColor, BackgroundColor, 250, 58);
        _playButton.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        _playButton.Enabled = false;
        _playButton.Click += async (_, _) => await CheckForUpdatesAsync(installUpdate: true);
        _heroPanel.Controls.Add(_playButton);

        _checkUpdatesButton = CreateButton("", Color.FromArgb(113, 78, 192), PrimaryTextColor, 174, 52);
        _checkUpdatesButton.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(installUpdate: _gameUpdateAvailable);
        _navigationPanel.Controls.Add(_checkUpdatesButton);

        _updateAvailableLabel = CreateLabel("", 8.5F, FontStyle.Bold, ChangedColor);
        _updateAvailableLabel.Visible = false;
        _updateAvailableLabel.TextAlign = ContentAlignment.MiddleCenter;
        _navigationPanel.Controls.Add(_updateAvailableLabel);

        _isVerticalNavigation = true;
        LayoutNavigation();
        _resizeRefreshTimer.Tick += (_, _) =>
        {
            _resizeRefreshTimer.Stop();
            _heroPanel.SetResizing(false);
            LayoutNavigation();
            LayoutChangelogEntries();
            LayoutChangelogPage();
            _heroPanel.Invalidate();
        };
        Resize += (_, _) =>
        {
            _heroPanel.SetResizing(true);
            _resizeRefreshTimer.Stop();
            _resizeRefreshTimer.Start();
        };
        _pageHost.Resize += (_, _) => ScheduleResizeRefresh();
        _heroPanel.Resize += (_, _) => LayoutHomeControls();
        FormClosed += (_, _) =>
        {
            _resizeRefreshTimer.Dispose();
            _toolTip.Dispose();
        };
        ApplyLocalizedInterface();
        ShowPage(LauncherPage.Home);

        Shown += async (_, _) => await CheckForUpdatesAsync();
    }

    private void BuildNavigation()
    {
        _homeNavigationButton = CreateNavigationButton();
        _changelogNavigationButton = CreateNavigationButton();
        _settingsNavigationButton = CreateNavigationButton();
        _discordButton.Click += (_, _) => OpenDiscordInvite();
        _toolTip.SetToolTip(_discordButton, "Discord");
        _homeNavigationButton.Click += (_, _) => ShowPage(LauncherPage.Home);
        _changelogNavigationButton.Click += (_, _) => ShowPage(LauncherPage.Changelog);
        _settingsNavigationButton.Click += (_, _) => ShowPage(LauncherPage.Settings);
        _navigationPanel.Controls.AddRange([
            _homeNavigationButton,
            _changelogNavigationButton,
            _settingsNavigationButton
        ]);
        _navigationPanel.Controls.Add(_discordButton);

        var versionCaption = CreateLabel("VERSION", 8F, FontStyle.Bold, MutedTextColor);
        versionCaption.Name = "versionCaption";
        versionCaption.SetBounds(18, 0, 174, 18);
        _navigationPanel.Controls.Add(versionCaption);
    }

    private static Button CreateNavigationButton()
    {
        var button = CreateButton("", SurfaceColor, MutedTextColor, 168, 46);
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(14, 0, 8, 0);
        return button;
    }

    private void LayoutNavigation()
    {
        if (_navigationPanel.IsDisposed || _rootLayout.IsDisposed)
            return;

        bool vertical = ClientSize.Width >= 1040 && ClientSize.Width > ClientSize.Height * 1.28;
        if (_isVerticalNavigation != vertical || _rootLayout.ColumnCount == 0)
        {
            _isVerticalNavigation = vertical;
            _rootLayout.SuspendLayout();
            _rootLayout.Controls.Remove(_navigationPanel);
            _rootLayout.Controls.Remove(_pageHost);
            _rootLayout.ColumnStyles.Clear();
            _rootLayout.RowStyles.Clear();
            if (vertical)
            {
                _rootLayout.ColumnCount = 2;
                _rootLayout.RowCount = 1;
                _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
                _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                _rootLayout.Controls.Add(_navigationPanel, 0, 0);
                _rootLayout.Controls.Add(_pageHost, 1, 0);
                _pageHost.Margin = new Padding(12, 0, 0, 0);
            }
            else
            {
                _rootLayout.ColumnCount = 1;
                _rootLayout.RowCount = 2;
                _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
                _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                _rootLayout.Controls.Add(_navigationPanel, 0, 0);
                _rootLayout.Controls.Add(_pageHost, 0, 1);
                _pageHost.Margin = new Padding(0, 10, 0, 0);
            }
            _rootLayout.ResumeLayout(true);
        }

        int navWidth = _navigationPanel.ClientSize.Width;
        int navHeight = _navigationPanel.ClientSize.Height;
        Control? versionCaption = _navigationPanel.Controls["versionCaption"];
        if (vertical)
        {
            int buttonY = 96;
            int buttonWidth = Math.Max(120, navWidth - 32);
            foreach (Button button in new[] { _homeNavigationButton, _changelogNavigationButton, _settingsNavigationButton })
            {
                button.SetBounds(16, buttonY, buttonWidth, 46);
                buttonY += 56;
            }
            _discordButton.SetBounds(16, buttonY + 2, 40, 40);

            int updateWidth = Math.Max(120, navWidth - 32);
            _checkUpdatesButton.SetBounds(16, navHeight - 82, updateWidth, 50);
            _updateAvailableLabel.SetBounds(16, navHeight - 112, updateWidth, 24);
            if (versionCaption != null)
            {
                versionCaption.Visible = true;
                versionCaption.SetBounds(18, navHeight - 42, navWidth - 36, 16);
            }
            _versionLabel.Visible = true;
            _versionLabel.SetBounds(18, navHeight - 25, navWidth - 36, 20);
        }
        else
        {
            int buttonWidth = Math.Clamp((navWidth - 430) / 3, 112, 166);
            int buttonX = 168;
            foreach (Button button in new[] { _homeNavigationButton, _changelogNavigationButton, _settingsNavigationButton })
            {
                button.SetBounds(buttonX, 15, buttonWidth, 46);
                buttonX += buttonWidth + 8;
            }
            _discordButton.SetBounds(buttonX - 2, 18, 40, 40);

            int updateWidth = Math.Min(188, Math.Max(145, navWidth / 5));
            _checkUpdatesButton.SetBounds(navWidth - updateWidth - 14, 15, updateWidth, 46);
            _updateAvailableLabel.SetBounds(navWidth - updateWidth - 14, 0, updateWidth, 14);
            if (versionCaption != null)
            {
                versionCaption.Visible = true;
                versionCaption.SetBounds(18, 34, 140, 12);
            }
            _versionLabel.Visible = true;
            _versionLabel.SetBounds(18, 46, 140, 20);
        }

        LayoutHomeControls();
    }

    private void LayoutHomeControls()
    {
        if (_heroPanel.IsDisposed || _playButton.IsDisposed)
            return;

        int width = _heroPanel.ClientSize.Width;
        int height = _heroPanel.ClientSize.Height;
        if (width <= 0 || height <= 0)
            return;

        int playWidth = Math.Min(280, Math.Max(210, width / 3));
        _playButton.SetBounds((width - playWidth) / 2, height - 108, playWidth, 58);
        _statusLabel.SetBounds(Math.Max(28, width / 8), height - 44, width - Math.Max(56, width / 4), 20);
        _progressBar.SetBounds(Math.Max(36, width / 5), height - 18, width - Math.Max(72, width * 2 / 5), 6);
    }

    private void ScheduleResizeRefresh()
    {
        if (IsDisposed)
            return;

        _heroPanel.SetResizing(true);
        _resizeRefreshTimer.Stop();
        _resizeRefreshTimer.Start();
    }

    private void ShowPage(LauncherPage page)
    {
        _heroPanel.Visible = page == LauncherPage.Home;
        _changelogPanel.Visible = page == LauncherPage.Changelog;
        _settingsPage.Visible = page == LauncherPage.Settings;
        _heroPanel.BringToFront();
        if (page == LauncherPage.Changelog)
        {
            _changelogPanel.BringToFront();
            LayoutChangelogPage();
        }
        else if (page == LauncherPage.Settings)
            _settingsPage.BringToFront();

        SetNavigationSelection(_homeNavigationButton, page == LauncherPage.Home);
        SetNavigationSelection(_changelogNavigationButton, page == LauncherPage.Changelog);
        SetNavigationSelection(_settingsNavigationButton, page == LauncherPage.Settings);
    }

    private static void SetNavigationSelection(Button button, bool selected)
    {
        button.BackColor = selected ? ElevatedColor : SurfaceColor;
        button.ForeColor = selected ? PrimaryTextColor : MutedTextColor;
        if (button is RoundedButton roundedButton)
            roundedButton.SetBaseColor(selected ? ElevatedColor : SurfaceColor);
    }

    private void BuildSettingsPage()
    {
        _settingsPage = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            Visible = false,
            Padding = new Padding(28)
        };

        var card = new GlassSidebarPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30)
        };
        _settingsPage.Controls.Add(card);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        card.Controls.Add(layout);

        _settingsTitle = CreateLabel("", 23F, FontStyle.Bold, PrimaryTextColor);
        _settingsTitle.Dock = DockStyle.Fill;
        _languageLabel = CreateLabel("", 10F, FontStyle.Bold, MutedTextColor);
        _languageLabel.Dock = DockStyle.Fill;
        _languageSelector = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = ElevatedColor,
            ForeColor = PrimaryTextColor,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 30
        };
        _languageSelector.DrawItem += DrawLanguageOption;
        _languageSelector.SelectedIndexChanged += OnLanguageSelectionChanged;
        _folderButton = CreateButton("", SurfaceColor, PrimaryTextColor, 224, 44);
        _folderButton.Enabled = File.Exists(Path.Combine(_installDirectory, GameExecutableName));
        _folderButton.Click += (_, _) => OpenGameFolder();
        layout.Controls.Add(_settingsTitle, 0, 0);
        layout.Controls.Add(_languageLabel, 0, 1);
        layout.Controls.Add(_languageSelector, 0, 2);
        layout.Controls.Add(CreateLabel("", 8F, FontStyle.Regular, MutedTextColor), 0, 3);
        layout.Controls.Add(_folderButton, 0, 4);
        LinkLabel githubLink = CreateLinkLabel("GitHub", 9F, "https://github.com/Mateo-Llr/Soulfract");
        githubLink.Dock = DockStyle.Fill;
        githubLink.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(githubLink, 0, 5);

        foreach (string code in SupportedLanguageCodes)
        {
            _languageSelector.Items.Add(new LanguageOption(code, GetNativeLanguageName(code)));
        }
    }

    private void DrawLanguageOption(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0 && e.Index < _languageSelector.Items.Count
            && _languageSelector.Items[e.Index] is LanguageOption option
            && (e.Font ?? SystemFonts.DefaultFont) is Font font)
        {
            using var brush = new SolidBrush(e.ForeColor);
            e.Graphics.DrawString(option.NativeName, font, brush, e.Bounds.Left + 8, e.Bounds.Top + 6);
        }
        e.DrawFocusRectangle();
    }

    private void SaveSelectedLanguage()
    {
        if (_languageSelector.SelectedItem is not LanguageOption option || option.Code == _languageCode)
            return;

        string previousLanguage = _languageCode;
        _languageCode = option.Code;
        try
        {
            SavePreferredLanguage(_languageCode);
            ApplyLocalizedInterface();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _languageCode = previousLanguage;
            int previousIndex = _languageSelector.Items
                .Cast<LanguageOption>()
                .ToList()
                .FindIndex(language => language.Code == previousLanguage);
            if (previousIndex >= 0)
                _languageSelector.SelectedIndex = previousIndex;
            MessageBox.Show(
                $"{GetText("languageSaveError")}: {exception.Message}",
                "Soulfract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string LoadPreferredLanguage()
    {
        string settingsPath = Path.Combine(_installDirectory, "Data", "settings.json");
        if (!File.Exists(settingsPath))
            return GetSystemLanguageCode();

        try
        {
            JsonObject? settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
            string? language = settings?
                .FirstOrDefault(property => string.Equals(property.Key, "Language", StringComparison.OrdinalIgnoreCase))
                .Value?.GetValue<string>();
            return SupportedLanguageCodes.Contains(language, StringComparer.OrdinalIgnoreCase)
                ? language!
                : GetSystemLanguageCode();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            MessageBox.Show(
                $"{GetText("settingsReadError")}: {exception.Message}",
                "Soulfract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return GetSystemLanguageCode();
        }
    }

    private void SavePreferredLanguage(string languageCode)
    {
        string settingsPath = Path.Combine(_installDirectory, "Data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        JsonObject settings;
        if (File.Exists(settingsPath))
            settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                ?? throw new JsonException("Le fichier des paramètres doit contenir un objet JSON.");
        else
            settings = new JsonObject();

        string? languageProperty = settings
            .Select(property => property.Key)
            .FirstOrDefault(key => string.Equals(key, "Language", StringComparison.OrdinalIgnoreCase));
        settings[languageProperty ?? "Language"] = languageCode;
        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetSystemLanguageCode()
    {
        string code = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        code = code switch
        {
            "sv" => "se",
            "ja" => "jp",
            "zh" => "cn",
            _ => code
        };
        return SupportedLanguageCodes.Contains(code, StringComparer.OrdinalIgnoreCase) ? code : "en";
    }

    private static string GetNativeLanguageName(string code) => code switch
    {
        "cn" => "中文",
        "de" => "Deutsch",
        "en" => "English",
        "es" => "Español",
        "fr" => "Français",
        "it" => "Italiano",
        "jp" => "日本語",
        "pl" => "Polski",
        "pt" => "Português",
        "ru" => "Русский",
        "se" => "Svenska",
        _ => code
    };

    private void ApplyLocalizedInterface()
    {
        Text = $"Soulfract — {GetText("launcher")}";
        if (_navigationPanel.Controls["versionCaption"] is Label versionCaption)
            versionCaption.Text = GetText("version");
        _homeNavigationButton.Text = $"⌂   {GetText("home")}";
        _changelogNavigationButton.Text = $"▤   {GetText("changelog")}";
        _settingsNavigationButton.Text = $"⚙   {GetText("settings")}";
        _checkUpdatesButton.Text = _gameUpdateAvailable ? GetText("update") : GetText("checkUpdates");
        _playButton.Text = $"▶   {GetText("play")}";
        _folderButton.Text = GetText("openFolder");
        _settingsTitle.Text = GetText("settings");
        _languageLabel.Text = GetText("language");
        _updateAvailableLabel.Text = GetText("updateAvailable");
        _changelogLoadMoreButton.Text = GetText("showMore");
        _statusLabel.Text = TranslateStatus(_statusMessage);

        int selectedIndex = _languageSelector.Items
            .Cast<LanguageOption>()
            .ToList()
            .FindIndex(language => language.Code == _languageCode);
        if (selectedIndex >= 0)
        {
            _languageSelector.SelectedIndexChanged -= OnLanguageSelectionChanged;
            _languageSelector.SelectedIndex = selectedIndex;
            _languageSelector.SelectedIndexChanged += OnLanguageSelectionChanged;
        }

    }

    private void OnLanguageSelectionChanged(object? sender, EventArgs e) => SaveSelectedLanguage();

    private string GetText(string key)
    {
        if (key == "version")
        {
            return _languageCode switch
            {
                "fr" => "VERSION",
                "es" => "VERSIÓN",
                "de" => "VERSION",
                "it" => "VERSIONE",
                "pt" => "VERSÃO",
                "pl" => "WERSJA",
                "ru" => "ВЕРСИЯ",
                "se" => "VERSION",
                "jp" => "バージョン",
                "cn" => "版本",
                _ => "VERSION"
            };
        }
        if (key == "showMore")
        {
            return _languageCode switch
            {
                "fr" => "VOIR PLUS",
                "es" => "VER MÁS",
                "de" => "MEHR ANZEIGEN",
                "it" => "MOSTRA ALTRO",
                "pt" => "VER MAIS",
                "pl" => "POKAŻ WIĘCEJ",
                "ru" => "ПОКАЗАТЬ ЕЩЁ",
                "se" => "VISA MER",
                "jp" => "もっと見る",
                "cn" => "查看更多",
                _ => "SHOW MORE"
            };
        }

        string[] values = _languageCode switch
        {
            "fr" => ["Lanceur", "Accueil", "Changelog", "Paramètres", "Vérifier les mises à jour", "Mettre à jour", "Jouer", "Dossier du jeu", "Langue", "Mise à jour disponible", "Impossible d'enregistrer la langue", "Impossible de lire les paramètres"],
            "es" => ["Lanzador", "Inicio", "Novedades", "Ajustes", "Buscar actualizaciones", "Actualizar", "Jugar", "Carpeta del juego", "Idioma", "Actualización disponible", "No se pudo guardar el idioma", "No se pudieron leer los ajustes"],
            "de" => ["Launcher", "Startseite", "Änderungen", "Einstellungen", "Nach Updates suchen", "Aktualisieren", "Spielen", "Spielordner", "Sprache", "Update verfügbar", "Sprache konnte nicht gespeichert werden", "Einstellungen konnten nicht gelesen werden"],
            "it" => ["Launcher", "Home", "Novità", "Impostazioni", "Cerca aggiornamenti", "Aggiorna", "Gioca", "Cartella del gioco", "Lingua", "Aggiornamento disponibile", "Impossibile salvare la lingua", "Impossibile leggere le impostazioni"],
            "pt" => ["Launcher", "Início", "Novidades", "Configurações", "Verificar atualizações", "Atualizar", "Jogar", "Pasta do jogo", "Idioma", "Atualização disponível", "Não foi possível guardar o idioma", "Não foi possível ler as configurações"],
            "pl" => ["Launcher", "Start", "Aktualności", "Ustawienia", "Sprawdź aktualizacje", "Aktualizuj", "Graj", "Folder gry", "Język", "Aktualizacja dostępna", "Nie można zapisać języka", "Nie można odczytać ustawień"],
            "ru" => ["Лаунчер", "Главная", "Изменения", "Настройки", "Проверить обновления", "Обновить", "Играть", "Папка игры", "Язык", "Доступно обновление", "Не удалось сохранить язык", "Не удалось прочитать настройки"],
            "se" => ["Startprogram", "Hem", "Ändringar", "Inställningar", "Sök efter uppdateringar", "Uppdatera", "Spela", "Spelmapp", "Språk", "Uppdatering tillgänglig", "Kunde inte spara språk", "Kunde inte läsa inställningar"],
            "jp" => ["ランチャー", "ホーム", "更新履歴", "設定", "更新を確認", "更新", "プレイ", "ゲームフォルダー", "言語", "更新があります", "言語を保存できません", "設定を読み込めません"],
            "cn" => ["启动器", "首页", "更新日志", "设置", "检查更新", "更新", "开始游戏", "游戏文件夹", "语言", "有可用更新", "无法保存语言", "无法读取设置"],
            _ => ["Launcher", "Home", "Changelog", "Settings", "Check for updates", "Update", "Play", "Game folder", "Language", "Update available", "Unable to save language", "Unable to read settings"]
        };

        int index = key switch
        {
            "launcher" => 0,
            "home" => 1,
            "changelog" => 2,
            "settings" => 3,
            "checkUpdates" => 4,
            "update" => 5,
            "play" => 6,
            "openFolder" => 7,
            "language" => 8,
            "updateAvailable" => 9,
            "languageSaveError" => 10,
            "settingsReadError" => 11,
            _ => -1
        };
        return index >= 0 ? values[index] : key;
    }

    private void BuildHero(HeroBackdropPanel hero)
    {
        hero.SetImages(LoadGameBannerTexture(), LoadTitleTexture());
    }

    private void BuildChangelogCard(GlassSidebarPanel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20, 14, 20, 18)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        var cautionSign = new PixelArtImageControl(LoadCautionSignTexture())
        {
            Location = new Point(0, -4),
            Size = new Size(40, 40),
            AccessibleName = "Attention"
        };
        _changelogTitle = CreateLabel(GetText("changelog"), 24F, FontStyle.Bold, PrimaryTextColor);
        _changelogTitle.Font = ChangelogHeadingFont;
        _changelogTitle.TextAlign = ContentAlignment.MiddleLeft;
        _changelogTitle.Location = new Point(4, 0);
        header.Controls.Add(_changelogTitle);
        header.Controls.Add(cautionSign);
        header.Resize += (_, _) =>
        {
            _changelogTitle.SetBounds(4, 0, Math.Max(100, header.ClientSize.Width - 56), header.ClientSize.Height);
            cautionSign.Location = new Point(header.ClientSize.Width - cautionSign.Width - 4, -4);
        };
        layout.Controls.Add(header, 0, 0);

        _changelogContent = new ChangelogScrollPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(2, 8, 12, 12),
            Margin = Padding.Empty
        };
        _changelogContent.Resize += (_, _) => ScheduleResizeRefresh();
        _changelogContent.ScrollPositionChanged += (_, _) => UpdateChangelogMonthTitle();
        layout.Controls.Add(_changelogContent, 0, 1);
        _changelogLoadMoreButton = CreateButton("", ElevatedColor, PrimaryTextColor, 176, 42);
        _changelogLoadMoreButton.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        _changelogLoadMoreButton.Margin = new Padding(4, 8, 4, 12);
        _changelogLoadMoreButton.Click += (_, _) => ShowMoreChangelogDays();
        SetChangelogText("Chargement du journal des modifications...");
    }

    private async Task CheckForUpdatesAsync(bool installUpdate = false)
    {
        if (_isWorking)
            return;

        _isWorking = true;
        SetWorkingState();
        string? archivePath = null;
        string? extractionDirectory = null;

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

            if (installUpdate && await UpdateLauncherIfNeededAsync(root))
                return;

            _versionLabel.Text = _latestVersion;
            await LoadChangelogAsync(_latestVersion, root);

            string versionPath = Path.Combine(_installDirectory, GameVersionFileName);
            string? installedVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : null;
            string gamePath = Path.Combine(_installDirectory, GameExecutableName);
            bool hasInstalledGame = File.Exists(gamePath);

            bool isGameUpToDate = hasInstalledGame
                && string.Equals(installedVersion, _latestVersion, StringComparison.Ordinal);
            _gameUpdateAvailable = !isGameUpToDate;
            _checkUpdatesButton.Text = _gameUpdateAvailable ? GetText("update") : GetText("checkUpdates");
            _updateAvailableLabel.Visible = _gameUpdateAvailable;

            if (!installUpdate)
            {
                UpdateStatus(isGameUpToDate
                    ? $"Version {_latestVersion} installée et à jour."
                    : $"Mise à jour disponible : Soulfract {_latestVersion}.");
                _folderButton.Enabled = hasInstalledGame;
                SetIdleState(canPlay: true);
                return;
            }

            if (isGameUpToDate)
            {
                _folderButton.Enabled = true;
                SetIdleState(canPlay: true);
                LaunchInstalledGame();
                return;
            }

            if (hasInstalledGame && IsGameRunning())
            {
                UpdateStatus("Fermez Soulfract pour installer la mise à jour.");
                _folderButton.Enabled = true;
                SetIdleState(canPlay: true);
                return;
            }

            archivePath = Path.Combine(Path.GetTempPath(), $"Soulfract-{Guid.NewGuid():N}.zip");
            extractionDirectory = Path.Combine(Path.GetTempPath(), $"Soulfract-{Guid.NewGuid():N}");
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
            IProgress<(long Downloaded, long? Total)> progress = CreateDownloadProgress("Téléchargement");
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

            UpdateStatus($"Soulfract {_latestVersion} est prêt. Démarrage du jeu...");
            _folderButton.Enabled = true;
            SetIdleState(canPlay: true);
            _gameUpdateAvailable = false;
            _checkUpdatesButton.Text = GetText("checkUpdates");
            _updateAvailableLabel.Visible = false;
            LaunchInstalledGame();
        }
        catch (Exception exception) when (IsRecoverableLauncherFailure(exception))
        {
            UpdateStatus($"Mise à jour impossible : {exception.Message}");
            if (_latestVersion == null)
                _versionLabel.Text = GetStatusText("versionUnavailable");
            bool hasInstalledGame = File.Exists(Path.Combine(_installDirectory, GameExecutableName));
            _folderButton.Enabled = hasInstalledGame;
            SetIdleState(canPlay: _folderButton.Enabled);
            if (_changelogText.StartsWith("Chargement", StringComparison.Ordinal))
                SetChangelogText("Impossible de charger le journal des modifications.\n\nRéessayez lorsque la connexion sera disponible.");
        }
        finally
        {
            _isWorking = false;
            if (archivePath != null)
                TryDeleteFile(archivePath);
            if (extractionDirectory != null)
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
            List<ChangelogDayGroup> changelogDays = FormatRecentChangelog(markdown);
            if (changelogDays.Count > 0)
                SetChangelogDays(changelogDays);
            else
                SetChangelogText("Aucune entrée récente n'est disponible dans le changelog.");
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
            await DownloadVerifiedReleaseAssetAsync(
                launcherAsset,
                stagedLauncherPath,
                CreateDownloadProgress($"Mise à jour du lanceur vers {_latestVersion}"));
            await DownloadVerifiedReleaseAssetAsync(
                updaterAsset,
                updaterPath,
                CreateDownloadProgress($"Composant de mise à jour du lanceur {_latestVersion}"));

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

    private static async Task DownloadVerifiedReleaseAssetAsync(
        LauncherUpdateAsset asset,
        string destination,
        IProgress<(long Downloaded, long? Total)> progress)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
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
            progress.Report((downloaded, total));
        }
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

    private static List<ChangelogDayGroup> FormatRecentChangelog(string markdown)
    {
        bool inChangelog = false;
        string currentMonth = "";
        var sections = new List<(string Title, string Month, Dictionary<string, List<string>> Categories, List<string> CategoryOrder)>();
        (string Title, string Month, Dictionary<string, List<string>> Categories, List<string> CategoryOrder)? currentSection = null;

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
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentMonth = line[3..].Trim();
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (currentSection.HasValue)
                    sections.Add(currentSection.Value);
                string title = line[4..].Trim();
                currentSection = (
                    title,
                    currentMonth,
                    new Dictionary<string, List<string>>(StringComparer.Ordinal),
                    []);
                continue;
            }

            if (currentSection.HasValue && line.StartsWith("- ", StringComparison.Ordinal))
            {
                string entry = StripInlineMarkdown(line[2..]);
                string category = TrySplitChangelogEntry(entry, out string parsedCategory, out _)
                    ? parsedCategory
                    : "INFO";
                string description = category == "INFO"
                    ? entry
                    : TrySplitChangelogEntry(entry, out _, out string parsedDescription)
                        ? parsedDescription
                        : entry;
                if (!currentSection.Value.Categories.TryGetValue(category, out List<string>? entries))
                {
                    entries = [];
                    currentSection.Value.Categories.Add(category, entries);
                    currentSection.Value.CategoryOrder.Add(category);
                }
                entries.Add(description);
            }
        }

        if (currentSection.HasValue)
            sections.Add(currentSection.Value);

        var selectedSections = sections
            .Where(section => section.Categories.Count > 0)
            .Reverse()
            .ToList();
        return selectedSections
            .Select(section => new ChangelogDayGroup(
                section.Title,
                section.Month,
                section.CategoryOrder
                    .Select(category => new ChangelogCategoryGroup(category, section.Categories[category]))
                    .ToList()))
            .ToList();
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
        _changelogText = text;
        string currentTitle = "Notes de version";
        string currentMonth = "Notes de version";
        var days = new List<ChangelogDayGroup>();
        var categories = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var categoryOrder = new List<string>();

        void FlushDay()
        {
            if (categories.Count == 0)
                return;

            days.Add(new ChangelogDayGroup(
                currentTitle,
                currentMonth,
                categoryOrder.Select(category => new ChangelogCategoryGroup(category, categories[category])).ToList()));
            categories = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            categoryOrder = [];
        }

        foreach (string rawLine in text.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushDay();
                currentTitle = line[3..];
                currentMonth = currentTitle;
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushDay();
                currentTitle = line[4..];
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
                line = line[2..].Trim();
            string category = TrySplitChangelogEntry(line, out string parsedCategory, out _)
                ? parsedCategory
                : "INFO";
            string description = category == "INFO"
                ? StripInlineMarkdown(line)
                : TrySplitChangelogEntry(line, out _, out string parsedDescription)
                    ? StripInlineMarkdown(parsedDescription)
                    : StripInlineMarkdown(line);
            if (!categories.TryGetValue(category, out List<string>? entries))
            {
                entries = [];
                categories.Add(category, entries);
                categoryOrder.Add(category);
            }
            entries.Add(description);
        }
        FlushDay();

        if (days.Count == 0)
            days.Add(new ChangelogDayGroup("Notes de version", "Notes de version", [new ChangelogCategoryGroup("INFO", [text])]));
        SetChangelogDays(days);
    }

    private void SetChangelogDays(List<ChangelogDayGroup> days)
    {
        _changelogDays = days;
        _changelogText = "Changelog chargé";
        _visibleChangelogDayCount = 0;
        _lastRenderedChangelogMonth = null;
        _changelogTitle.Text = days.Count > 0 ? days[0].Month : GetText("changelog");
        _changelogContent.SuspendLayout();
        try
        {
            ClearChangelogControls();
            AddMoreChangelogDays();
        }
        finally
        {
            _changelogContent.ResumeLayout(true);
            LayoutChangelogEntries();
        }
    }

    private void ClearChangelogControls()
    {
        while (_changelogContent.Controls.Count > 0)
        {
            Control control = _changelogContent.Controls[0];
            _changelogContent.Controls.RemoveAt(0);
            if (!ReferenceEquals(control, _changelogLoadMoreButton))
                control.Dispose();
        }
    }

    private void ShowMoreChangelogDays()
    {
        if (_visibleChangelogDayCount >= _changelogDays.Count)
            return;

        _changelogContent.SuspendLayout();
        try
        {
            _changelogContent.Controls.Remove(_changelogLoadMoreButton);
            AddMoreChangelogDays();
        }
        finally
        {
            _changelogContent.ResumeLayout(true);
            LayoutChangelogEntries();
        }
    }

    private void AddMoreChangelogDays()
    {
        int lastDayIndex = Math.Min(_visibleChangelogDayCount + ChangelogDaysPerPage, _changelogDays.Count);
        for (int dayIndex = _visibleChangelogDayCount; dayIndex < lastDayIndex; dayIndex++)
        {
            ChangelogDayGroup day = _changelogDays[dayIndex];
            if (!string.Equals(_lastRenderedChangelogMonth, day.Month, StringComparison.Ordinal))
            {
                AddChangelogMonthSeparator(day.Month);
                _lastRenderedChangelogMonth = day.Month;
            }
            AddChangelogSection(day.Title);
            foreach (ChangelogCategoryGroup category in day.Categories)
                AddChangelogEntry(category.Category, category.Entries);
        }
        UpdateChangelogMonthTitle();
        _visibleChangelogDayCount = lastDayIndex;

        if (_visibleChangelogDayCount < _changelogDays.Count)
        {
            _changelogLoadMoreButton.Text = GetText("showMore");
            _changelogLoadMoreButton.Width = Math.Max(180, _changelogContent.ClientSize.Width - 32);
            _changelogContent.Controls.Add(_changelogLoadMoreButton);
        }
    }

    private void AddChangelogSection(string heading)
    {
        var label = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = AccentColor,
            BackColor = Color.Transparent,
            Text = heading,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(4, 12, 4, 4),
            Height = 34
        };
        _changelogContent.Controls.Add(label);
    }

    private void AddChangelogMonthSeparator(string month)
    {
        var separator = new ChangelogMonthSeparator(month)
        {
            Height = 40,
            Margin = new Padding(4, 18, 4, 6)
        };
        _changelogContent.Controls.Add(separator);
    }

    private void AddChangelogEntry(string category, IReadOnlyList<string> descriptions)
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
            "INFO" => "INFO",
            "FIXED" => "CORRECTION",
            "CHANGED" => "MODIFIÉ",
            "PERF" => "PERFORMANCE",
            "REMOVED" => "RETIRÉ",
            _ => category
        };

        _changelogContent.Controls.Add(new ChangelogEntryCard(
            $"{categoryLabel}  ·  {descriptions.Count}",
            descriptions,
            categoryColor));
    }

    private void LayoutChangelogEntries()
    {
        if (_changelogContent.IsDisposed || _changelogContent.ClientSize.Width <= 0)
            return;

        int width = Math.Max(240, _changelogContent.ClientSize.Width - _changelogContent.Padding.Horizontal - 8);
        foreach (Control entry in _changelogContent.Controls)
        {
            if (entry.Width == width)
                continue;

            entry.Width = width;
            if (entry is ChangelogEntryCard card)
                card.RefreshContentLayout();
        }
        LayoutChangelogPage();
    }

    private void LayoutChangelogPage()
    {
        if (_isLayingOutChangelog || _pageHost.IsDisposed || _changelogPanel.IsDisposed)
            return;

        int availableWidth = _pageHost.ClientSize.Width;
        int availableHeight = _pageHost.ClientSize.Height;
        if (availableWidth <= 0 || availableHeight <= 0)
            return;

        _isLayingOutChangelog = true;
        try
        {
            int contentHeight = _changelogContent.Padding.Vertical;
            foreach (Control entry in _changelogContent.Controls)
                contentHeight += entry.Height + entry.Margin.Vertical;

            int desiredHeight = 14 + 70 + 18 + contentHeight;
            int panelHeight = Math.Min(availableHeight, Math.Max(150, desiredHeight));
            _changelogPanel.SetBounds(0, 0, availableWidth, panelHeight);
        }
        finally
        {
            _isLayingOutChangelog = false;
        }
    }

    private void UpdateChangelogMonthTitle()
    {
        if (_changelogTitle.IsDisposed || _changelogContent.IsDisposed)
            return;

        ChangelogMonthSeparator? currentMonth = null;
        ChangelogMonthSeparator? firstMonth = null;
        int viewportTop = _changelogContent.ClientRectangle.Top + _changelogContent.Padding.Top;
        foreach (Control control in _changelogContent.Controls)
        {
            if (control is not ChangelogMonthSeparator separator)
                continue;

            firstMonth ??= separator;
            int separatorTop = _changelogContent.PointToClient(separator.PointToScreen(Point.Empty)).Y;
            if (separatorTop <= viewportTop)
                currentMonth = separator;
        }

        string? month = (currentMonth ?? firstMonth)?.Month;
        if (month != null)
            _changelogTitle.Text = month;
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
        [JsonPropertyName("baseVersion")]
        public string BaseVersion { get; set; } = "";

        [JsonPropertyName("deletedFiles")]
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
            || path.Equals("Data/character_animations.json", StringComparison.OrdinalIgnoreCase);
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

    private void OpenDiscordInvite()
    {
        const string inviteUrl = "https://discord.gg/gEJtyxseAC";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = inviteUrl, UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            MessageBox.Show(
                $"Impossible d'ouvrir l'invitation Discord : {exception.Message}",
                "Soulfract",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

    private IProgress<(long Downloaded, long? Total)> CreateDownloadProgress(string operation)
    {
        return new Progress<(long Downloaded, long? Total)>(value =>
        {
            if (value.Total is > 0)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = (int)Math.Clamp(value.Downloaded * 100 / value.Total.Value, 0, 100);
                UpdateStatus($"{operation}... {value.Downloaded / 1_048_576} / {value.Total.Value / 1_048_576} Mo");
            }
            else
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
                UpdateStatus($"{operation}... {value.Downloaded / 1_048_576} Mo téléchargés");
            }
        });
    }

    private void SetIdleState(bool canPlay)
    {
        _progressBar.Visible = false;
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Value = 0;
        _playButton.Enabled = canPlay;
        _checkUpdatesButton.Enabled = true;
        _checkUpdatesButton.Text = _gameUpdateAvailable ? GetText("update") : GetText("checkUpdates");
    }

    private void UpdateStatus(string text)
    {
        _statusMessage = text;
        _statusLabel.Text = TranslateStatus(text);
    }

    private string TranslateStatus(string text)
    {
        if (_languageCode == "fr")
            return text;

        if (text == "Recherche de la dernière version...")
            return GetStatusText("checking");
        if (text == "Fermez Soulfract pour installer la mise à jour.")
            return GetStatusText("closeGame");
        if (text == "Vérification de l'archive...")
            return GetStatusText("verifyArchive");
        if (text == "Installation des fichiers modifiés...")
            return GetStatusText("installDelta");
        if (text == "Installation complète du jeu...")
            return GetStatusText("installGame");
        if (text == "Le jeu n'est pas encore installé. Vérifiez les mises à jour.")
            return GetStatusText("noGame");
        if (text == "Version indisponible")
            return GetStatusText("versionUnavailable");

        const string upToDateSuffix = " installée et à jour.";
        if (text.StartsWith("Version ", StringComparison.Ordinal)
            && text.EndsWith(upToDateSuffix, StringComparison.Ordinal))
        {
            string version = text["Version ".Length..^upToDateSuffix.Length];
            return string.Format(GetStatusText("upToDate"), version);
        }

        const string updateAvailablePrefix = "Mise à jour disponible : Soulfract ";
        if (text.StartsWith(updateAvailablePrefix, StringComparison.Ordinal) && text.EndsWith('.'))
        {
            string version = text[updateAvailablePrefix.Length..^1];
            return string.Format(GetStatusText("updateAvailable"), version);
        }

        const string readyPrefix = "Soulfract ";
        const string readySuffix = " est prêt. Démarrage du jeu...";
        if (text.StartsWith(readyPrefix, StringComparison.Ordinal)
            && text.EndsWith(readySuffix, StringComparison.Ordinal))
        {
            string version = text[readyPrefix.Length..^readySuffix.Length];
            return string.Format(GetStatusText("ready"), version);
        }

        const string updateErrorPrefix = "Mise à jour impossible : ";
        if (text.StartsWith(updateErrorPrefix, StringComparison.Ordinal))
            return $"{GetStatusText("updateError")}: {text[updateErrorPrefix.Length..]}";

        const string launchErrorPrefix = "Impossible de démarrer le jeu : ";
        if (text.StartsWith(launchErrorPrefix, StringComparison.Ordinal))
            return $"{GetStatusText("launchError")}: {text[launchErrorPrefix.Length..]}";

        const string folderErrorPrefix = "Impossible d'ouvrir le dossier : ";
        if (text.StartsWith(folderErrorPrefix, StringComparison.Ordinal))
            return $"{GetStatusText("folderError")}: {text[folderErrorPrefix.Length..]}";

        const string changelogUnavailablePrefix = "Lecture du changelog GitHub indisponible : ";
        if (text.StartsWith(changelogUnavailablePrefix, StringComparison.Ordinal))
            return $"{GetStatusText("changelogUnavailable")}: {text[changelogUnavailablePrefix.Length..]}";

        const string changelogInterruptedPrefix = "Lecture du changelog GitHub interrompue : ";
        if (text.StartsWith(changelogInterruptedPrefix, StringComparison.Ordinal))
            return $"{GetStatusText("changelogInterrupted")}: {text[changelogInterruptedPrefix.Length..]}";

        if (text.StartsWith("Téléchargement des fichiers modifiés pour ", StringComparison.Ordinal))
            return $"{GetStatusText("downloadDelta")}{text["Téléchargement des fichiers modifiés pour ".Length..]}";
        if (text.StartsWith("Téléchargement complet de la mise à jour ", StringComparison.Ordinal))
            return $"{GetStatusText("downloadUpdate")}{text["Téléchargement complet de la mise à jour ".Length..]}";
        if (text.StartsWith("Téléchargement de Soulfract ", StringComparison.Ordinal))
            return $"{GetStatusText("downloadGame")}{text["Téléchargement de Soulfract ".Length..]}";
        if (text.StartsWith("Mise à jour du lanceur vers ", StringComparison.Ordinal))
        {
            return $"{GetStatusText("launcherUpdate")} {text["Mise à jour du lanceur vers ".Length..]}"
                .Replace(" Mo téléchargés", $" {GetStatusText("downloaded")}", StringComparison.Ordinal)
                .Replace(" Mo", " MB", StringComparison.Ordinal);
        }

        if (text.StartsWith("Téléchargement... ", StringComparison.Ordinal))
            text = $"{GetStatusText("downloading")}... {text["Téléchargement... ".Length..]}";
        else if (text.StartsWith("Mise à jour du lanceur vers ", StringComparison.Ordinal))
            text = $"{GetStatusText("launcherUpdate")} {text["Mise à jour du lanceur vers ".Length..]}";
        else if (text.StartsWith("Composant de mise à jour du lanceur ", StringComparison.Ordinal))
            text = $"{GetStatusText("launcherUpdater")} {text["Composant de mise à jour du lanceur ".Length..]}";

        return text
            .Replace(" Mo téléchargés", $" {GetStatusText("downloaded")}", StringComparison.Ordinal)
            .Replace(" Mo", " MB", StringComparison.Ordinal);
    }

    private string GetStatusText(string key)
    {
        string[] values = _languageCode switch
        {
            "fr" => ["Recherche de la dernière version...", "Version {0} installée et à jour.", "Mise à jour disponible : Soulfract {0}.", "Fermez Soulfract pour installer la mise à jour.", "Téléchargement des fichiers modifiés pour ", "Téléchargement complet de la mise à jour ", "Téléchargement de Soulfract ", "Vérification de l'archive...", "Installation des fichiers modifiés...", "Installation complète du jeu...", "Soulfract {0} est prêt. Démarrage du jeu...", "Mise à jour impossible", "Le jeu n'est pas encore installé. Vérifiez les mises à jour.", "Impossible de démarrer le jeu", "Impossible d'ouvrir le dossier", "Téléchargement", "Mo téléchargés", "Mise à jour du lanceur vers", "Composant de mise à jour du lanceur", "Version indisponible", "Lecture du changelog GitHub indisponible", "Lecture du changelog GitHub interrompue"],
            "es" => ["Buscando la última versión...", "La versión {0} está instalada y actualizada.", "Actualización disponible: Soulfract {0}.", "Cierra Soulfract para instalar la actualización.", "Descargando archivos modificados para Soulfract ", "Descargando la actualización completa ", "Descargando Soulfract ", "Verificando el archivo...", "Instalando archivos modificados...", "Instalando el juego completo...", "Soulfract {0} está listo. Iniciando el juego...", "No se pudo actualizar", "El juego aún no está instalado. Busca actualizaciones.", "No se pudo iniciar el juego", "No se pudo abrir la carpeta", "Descargando", "MB descargados", "Actualizando el lanzador a", "Componente de actualización del lanzador", "Versión no disponible", "Changelog de GitHub no disponible", "Lectura del changelog de GitHub interrumpida"],
            "de" => ["Suche nach der neuesten Version...", "Version {0} ist installiert und aktuell.", "Update verfügbar: Soulfract {0}.", "Schließe Soulfract, um das Update zu installieren.", "Lade geänderte Dateien für Soulfract herunter ", "Lade das vollständige Update herunter ", "Lade Soulfract herunter ", "Archiv wird überprüft...", "Geänderte Dateien werden installiert...", "Spiel wird vollständig installiert...", "Soulfract {0} ist bereit. Spiel wird gestartet...", "Update fehlgeschlagen", "Das Spiel ist noch nicht installiert. Suche nach Updates.", "Spiel konnte nicht gestartet werden", "Ordner konnte nicht geöffnet werden", "Download", "MB heruntergeladen", "Launcher wird aktualisiert auf", "Launcher-Updater-Komponente", "Version nicht verfügbar", "GitHub-Changelog nicht verfügbar", "GitHub-Changelog konnte nicht gelesen werden"],
            "it" => ["Ricerca dell'ultima versione...", "La versione {0} è installata e aggiornata.", "Aggiornamento disponibile: Soulfract {0}.", "Chiudi Soulfract per installare l'aggiornamento.", "Download dei file modificati per Soulfract ", "Download dell'aggiornamento completo ", "Download di Soulfract ", "Verifica dell'archivio...", "Installazione dei file modificati...", "Installazione completa del gioco...", "Soulfract {0} è pronto. Avvio del gioco...", "Aggiornamento non riuscito", "Il gioco non è ancora installato. Verifica gli aggiornamenti.", "Impossibile avviare il gioco", "Impossibile aprire la cartella", "Download", "MB scaricati", "Aggiornamento del launcher a", "Componente di aggiornamento del launcher", "Versione non disponibile", "Changelog GitHub non disponibile", "Lettura del changelog GitHub interrotta"],
            "pt" => ["Procurando a versão mais recente...", "A versão {0} está instalada e atualizada.", "Atualização disponível: Soulfract {0}.", "Feche Soulfract para instalar a atualização.", "Transferindo arquivos alterados para Soulfract ", "Transferindo a atualização completa ", "Transferindo Soulfract ", "Verificando o arquivo...", "Instalando arquivos alterados...", "Instalando o jogo completo...", "Soulfract {0} está pronto. Iniciando o jogo...", "Falha ao atualizar", "O jogo ainda não está instalado. Verifique atualizações.", "Não foi possível iniciar o jogo", "Não foi possível abrir a pasta", "Transferindo", "MB transferidos", "Atualizando o launcher para", "Componente de atualização do launcher", "Versão indisponível", "Changelog do GitHub indisponível", "Leitura do changelog do GitHub interrompida"],
            "pl" => ["Sprawdzanie najnowszej wersji...", "Wersja {0} jest zainstalowana i aktualna.", "Dostępna aktualizacja: Soulfract {0}.", "Zamknij Soulfract, aby zainstalować aktualizację.", "Pobieranie zmienionych plików Soulfract ", "Pobieranie pełnej aktualizacji ", "Pobieranie Soulfract ", "Weryfikowanie archiwum...", "Instalowanie zmienionych plików...", "Pełna instalacja gry...", "Soulfract {0} jest gotowy. Uruchamianie gry...", "Aktualizacja nie powiodła się", "Gra nie jest jeszcze zainstalowana. Sprawdź aktualizacje.", "Nie można uruchomić gry", "Nie można otworzyć folderu", "Pobieranie", "MB pobrano", "Aktualizacja launchera do", "Komponent aktualizacji launchera", "Wersja niedostępna", "Changelog GitHub jest niedostępny", "Odczyt changeloga GitHub został przerwany"],
            "ru" => ["Поиск последней версии...", "Версия {0} установлена и обновлена.", "Доступно обновление: Soulfract {0}.", "Закройте Soulfract для установки обновления.", "Загрузка изменённых файлов Soulfract ", "Загрузка полного обновления ", "Загрузка Soulfract ", "Проверка архива...", "Установка изменённых файлов...", "Полная установка игры...", "Soulfract {0} готов. Запуск игры...", "Не удалось обновить", "Игра ещё не установлена. Проверьте обновления.", "Не удалось запустить игру", "Не удалось открыть папку", "Загрузка", "МБ загружено", "Обновление лаунчера до", "Компонент обновления лаунчера", "Версия недоступна", "Changelog GitHub недоступен", "Чтение changelog GitHub прервано"],
            "se" => ["Söker efter den senaste versionen...", "Version {0} är installerad och aktuell.", "Uppdatering tillgänglig: Soulfract {0}.", "Stäng Soulfract för att installera uppdateringen.", "Hämtar ändrade filer för Soulfract ", "Hämtar hela uppdateringen ", "Hämtar Soulfract ", "Verifierar arkivet...", "Installerar ändrade filer...", "Installerar hela spelet...", "Soulfract {0} är redo. Startar spelet...", "Uppdateringen misslyckades", "Spelet är inte installerat än. Sök efter uppdateringar.", "Det gick inte att starta spelet", "Det gick inte att öppna mappen", "Hämtar", "MB hämtade", "Uppdaterar startprogrammet till", "Uppdateringskomponent för startprogrammet", "Versionen är inte tillgänglig", "GitHub-changeloggen är inte tillgänglig", "Läsningen av GitHub-changeloggen avbröts"],
            "jp" => ["最新バージョンを確認中...", "バージョン {0} はインストール済みで最新です。", "更新があります: Soulfract {0}", "アップデートをインストールするには Soulfract を終了してください。", "Soulfract の変更ファイルをダウンロード中 ", "アップデート全体をダウンロード中 ", "Soulfract をダウンロード中 ", "アーカイブを確認中...", "変更ファイルをインストール中...", "ゲーム全体をインストール中...", "Soulfract {0} の準備ができました。ゲームを起動します...", "アップデートに失敗しました", "ゲームはまだインストールされていません。アップデートを確認してください。", "ゲームを起動できません", "フォルダーを開けません", "ダウンロード中", "MB ダウンロード済み", "ランチャーを更新中:", "ランチャー更新コンポーネント", "バージョンを取得できません", "GitHub の変更履歴を取得できません", "GitHub の変更履歴の読み込みが中断されました"],
            "cn" => ["正在检查最新版本...", "版本 {0} 已安装且为最新版本。", "有可用更新：Soulfract {0}", "请关闭 Soulfract 以安装更新。", "正在下载 Soulfract 修改的文件 ", "正在下载完整更新 ", "正在下载 Soulfract ", "正在验证压缩包...", "正在安装修改的文件...", "正在完整安装游戏...", "Soulfract {0} 已就绪。正在启动游戏...", "更新失败", "游戏尚未安装。请检查更新。", "无法启动游戏", "无法打开文件夹", "正在下载", "MB 已下载", "正在更新启动器至", "启动器更新组件", "版本不可用", "无法获取 GitHub 更新日志", "GitHub 更新日志读取已中断"],
            _ => ["Searching for the latest version...", "Version {0} is installed and up to date.", "Update available: Soulfract {0}.", "Close Soulfract to install the update.", "Downloading modified Soulfract files ", "Downloading the full update ", "Downloading Soulfract ", "Verifying archive...", "Installing modified files...", "Installing the full game...", "Soulfract {0} is ready. Starting the game...", "Update failed", "The game is not installed yet. Check for updates.", "Unable to start the game", "Unable to open the folder", "Downloading", "MB downloaded", "Updating launcher to", "Launcher updater component", "Version unavailable", "GitHub changelog unavailable", "GitHub changelog reading was interrupted"]
        };

        int index = key switch
        {
            "checking" => 0,
            "upToDate" => 1,
            "updateAvailable" => 2,
            "closeGame" => 3,
            "downloadDelta" => 4,
            "downloadUpdate" => 5,
            "downloadGame" => 6,
            "verifyArchive" => 7,
            "installDelta" => 8,
            "installGame" => 9,
            "ready" => 10,
            "updateError" => 11,
            "noGame" => 12,
            "launchError" => 13,
            "folderError" => 14,
            "downloading" => 15,
            "downloaded" => 16,
            "launcherUpdate" => 17,
            "launcherUpdater" => 18,
            "versionUnavailable" => 19,
            "changelogUnavailable" => 20,
            "changelogInterrupted" => 21,
            _ => -1
        };
        return index >= 0 ? values[index] : key;
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

    private static Image LoadCautionSignTexture()
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
            "SoulfractLauncher.Assets.Launcher.caution_sign.png")
            ?? throw new InvalidOperationException("La texture d'avertissement du changelog est introuvable.");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Image LoadDiscordIconTexture()
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
            "SoulfractLauncher.Assets.Launcher.discord_icon.png")
            ?? throw new InvalidOperationException("L'icône Discord du lanceur est introuvable.");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Icon LoadLauncherIcon()
    {
        using Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
            "SoulfractLauncher.Assets.Launcher.launcher_icon.ico")
            ?? throw new InvalidOperationException("L'icône du lanceur est introuvable.");
        return new Icon(stream);
    }

    private static Button CreateButton(string text, Color background, Color foreground, int width, int height)
    {
        var button = new RoundedButton(background, foreground)
        {
            Text = text,
            Size = new Size(width, height),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 10),
            TabStop = true
        };
        return button;
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

    private sealed class RoundedButton : Button
    {
        private Color _baseColor;
        private readonly Color _textColor;
        private bool _isHovered;
        private bool _isPressed;

        public RoundedButton(Color background, Color foreground)
        {
            _baseColor = background;
            _textColor = foreground;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = BackgroundColor;
            ForeColor = foreground;
        }

        public void SetBaseColor(Color color)
        {
            _baseColor = color;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Region?.Dispose();
            using GraphicsPath path = CreateRoundedPath(ClientRectangle, ScaleRadius(10));
            Region = new Region(path);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isPressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent?.BackColor ?? BackgroundColor);

            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 1 || bounds.Height <= 1)
                return;

            Color fillColor = !Enabled
                ? ElevatedColor
                : _isPressed
                    ? ControlPaint.Dark(_baseColor, 0.08F)
                    : _isHovered
                        ? ControlPaint.Light(_baseColor, 0.2F)
                        : _baseColor;
            Color borderColor = _baseColor == AccentColor
                ? ControlPaint.Light(_baseColor, _isHovered ? 0.12F : 0.02F)
                : _isHovered
                    ? Color.FromArgb(78, 92, 103, 116)
                    : BorderColor;

            Rectangle shapeBounds = Rectangle.Inflate(bounds, -1, -1);
            using GraphicsPath path = CreateRoundedPath(shapeBounds, ScaleRadius(10));
            using (var fill = new SolidBrush(fillColor))
                graphics.FillPath(fill, path);
            using (var border = new Pen(borderColor, 1F))
                graphics.DrawPath(border, path);

            Color textColor = Enabled ? _textColor : MutedTextColor;
            TextFormatFlags alignment = TextAlign switch
            {
                ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
                ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
                _ => TextFormatFlags.HorizontalCenter
            };
            Rectangle textBounds = Padding == Padding.Empty
                ? bounds
                : Rectangle.FromLTRB(
                    bounds.Left + Padding.Left,
                    bounds.Top,
                    bounds.Right - Padding.Right,
                    bounds.Bottom);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                textColor,
                alignment
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = Rectangle.Inflate(shapeBounds, -4, -4);
                using GraphicsPath focusPath = CreateRoundedPath(focusBounds, ScaleRadius(7));
                using var focusBorder = new Pen(AccentColor, 1F);
                graphics.DrawPath(focusBorder, focusPath);
            }
        }

        private int ScaleRadius(int radius) => Math.Max(1, (int)Math.Round(radius * DeviceDpi / 96F));
    }

    private static LinkLabel CreateLinkLabel(string text, float size, string url)
    {
        var link = new LinkLabel
        {
            Text = text,
            Font = new Font("Segoe UI", size, FontStyle.Bold),
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

    private sealed class PixelArtImageControl : Control
    {
        private Image? _image;

        public PixelArtImageControl(Image image)
        {
            _image = image;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint
                    | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_image, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
                _image = null;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ChangelogScrollPanel : FlowLayoutPanel
    {
        private const int WmVScroll = 0x0115;
        private const int WmMouseWheel = 0x020A;
        private const int WmKeyDown = 0x0100;
        private bool _scrollUpdateQueued;

        public event EventHandler? ScrollPositionChanged;

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            QueueScrollPositionChanged();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is WmVScroll or WmMouseWheel or WmKeyDown)
                QueueScrollPositionChanged();
        }

        private void QueueScrollPositionChanged()
        {
            if (_scrollUpdateQueued || !IsHandleCreated || IsDisposed)
                return;

            _scrollUpdateQueued = true;
            BeginInvoke((Action)(() =>
            {
                _scrollUpdateQueued = false;
                if (!IsDisposed)
                    ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
            }));
        }
    }

    private sealed class ChangelogMonthSeparator : Panel
    {
        public string Month { get; }

        public ChangelogMonthSeparator(string month)
        {
            Month = month;
            BackColor = Color.Transparent;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackgroundColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int textWidth = TextRenderer.MeasureText(Month, ChangelogMonthFont, Size.Empty, TextFormatFlags.SingleLine).Width;
            int textLeft = 4;
            int centerY = ClientSize.Height / 2;
            TextRenderer.DrawText(
                e.Graphics,
                Month,
                ChangelogMonthFont,
                new Point(textLeft, centerY - 10),
                AccentColor,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int lineStart = textLeft + textWidth + 16;
            if (lineStart < ClientSize.Width - 8)
            {
                using var pen = new Pen(BorderColor);
                e.Graphics.DrawLine(pen, lineStart, centerY, ClientSize.Width - 8, centerY);
            }
        }
    }

    private sealed class ChangelogEntryCard : Panel
    {
        private readonly string _description;
        private readonly Color _categoryColor;
        private readonly Label _categoryLabel;
        private readonly Label _descriptionLabel;

        public ChangelogEntryCard(string category, IReadOnlyList<string> entries, Color categoryColor)
        {
            _description = string.Join(
                Environment.NewLine,
                entries.Select(entry => $"• {entry}"));
            _categoryColor = categoryColor;
            BackColor = Color.FromArgb(36, 39, 45);
            Margin = new Padding(2, 0, 2, 12);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);

            _categoryLabel = new Label
            {
                AutoSize = false,
                Font = ChangelogCategoryFont,
                ForeColor = categoryColor,
                BackColor = Color.Transparent,
                Text = category,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _descriptionLabel = new Label
            {
                AutoSize = false,
                Font = ChangelogEntryFont,
                ForeColor = PrimaryTextColor,
                BackColor = Color.Transparent,
                Text = _description,
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(_categoryLabel);
            Controls.Add(_descriptionLabel);
            RefreshContentLayout();
        }

        public void RefreshContentLayout()
        {
            if (ClientSize.Width <= 0)
                return;

            int textWidth = Math.Max(100, ClientSize.Width - 36);
            _categoryLabel.SetBounds(18, 8, textWidth, 18);
            Size descriptionSize = TextRenderer.MeasureText(
                _description,
                _descriptionLabel.Font,
                new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            int descriptionHeight = Math.Max(20, descriptionSize.Height);
            _descriptionLabel.SetBounds(18, 29, textWidth, descriptionHeight);
            int cardHeight = descriptionHeight + 39;
            if (Height != cardHeight)
                Height = cardHeight;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using var categoryAccent = new SolidBrush(_categoryColor);
            e.Graphics.FillRectangle(categoryAccent, 0, 0, 4, ClientSize.Height);
        }
    }

    private sealed class DiscordIconButton : Button
    {
        private Image? _icon;
        private bool _isHovered;
        private bool _isPressed;

        public DiscordIconButton(Image icon)
        {
            _icon = icon;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = BackgroundColor;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _isPressed = e.Button == MouseButtons.Left;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? BackgroundColor);

            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            if (_isHovered)
            {
                Color hoverColor = _isPressed
                    ? Color.FromArgb(90, 87, 105, 212)
                    : Color.FromArgb(56, 87, 105, 212);
                using GraphicsPath hoverPath = CreateRoundedPath(Rectangle.Inflate(bounds, -1, -1), 10);
                using var hoverBrush = new SolidBrush(hoverColor);
                e.Graphics.FillPath(hoverBrush, hoverPath);
            }

            if (_icon == null)
                return;

            int scale = Math.Max(1, Math.Min(bounds.Width / _icon.Width, bounds.Height / _icon.Height));
            int iconWidth = _icon.Width * scale;
            int iconHeight = _icon.Height * scale;
            var iconBounds = new Rectangle(
                (bounds.Width - iconWidth) / 2,
                (bounds.Height - iconHeight) / 2,
                iconWidth,
                iconHeight);
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_icon, iconBounds);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icon?.Dispose();
                _icon = null;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class HeroBackdropPanel : Panel
    {
        private Image? _banner;
        private Image? _title;
        private bool _isResizing;

        public HeroBackdropPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            BackColor = BackgroundColor;
        }

        public void SetImages(Image banner, Image title)
        {
            _banner?.Dispose();
            _title?.Dispose();
            _banner = banner;
            _title = title;
            Invalidate();
        }

        public void SetResizing(bool isResizing)
        {
            _isResizing = isResizing;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackgroundColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0 || _banner == null || _title == null)
                return;

            Graphics graphics = e.Graphics;
            graphics.InterpolationMode = _isResizing
                ? InterpolationMode.Bilinear
                : InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using GraphicsPath clipPath = CreateRoundedPath(bounds, 16);
            GraphicsState graphicsState = graphics.Save();
            graphics.SetClip(clipPath);
            Rectangle sourceBounds = new(0, 0, _banner.Width, _banner.Height);
            float sourceAspect = (float)_banner.Width / _banner.Height;
            float targetAspect = (float)bounds.Width / bounds.Height;
            if (sourceAspect < targetAspect)
            {
                int cropHeight = (int)Math.Round(_banner.Width / targetAspect);
                sourceBounds.Y = (_banner.Height - cropHeight) / 2;
                sourceBounds.Height = cropHeight;
            }
            else if (sourceAspect > targetAspect)
            {
                int cropWidth = (int)Math.Round(_banner.Height * targetAspect);
                sourceBounds.X = (_banner.Width - cropWidth) / 2;
                sourceBounds.Width = cropWidth;
            }
            graphics.DrawImage(
                _banner,
                bounds,
                sourceBounds,
                GraphicsUnit.Pixel);

            int gradientTop = (int)(bounds.Height * 0.44F);
            Rectangle gradientBounds = new(0, gradientTop, bounds.Width, bounds.Height - gradientTop);
            using (var gradient = new LinearGradientBrush(
                gradientBounds,
                Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(242, 0, 0, 0),
                LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(gradient, gradientBounds);
            }

            float maxTitleWidth = Math.Min(bounds.Width * 0.64F, 640F);
            float maxTitleHeight = bounds.Height * 0.58F;
            int pixelScale = Math.Max(1, (int)Math.Floor(Math.Min(
                maxTitleWidth / _title.Width,
                maxTitleHeight / _title.Height)));
            float titleWidth = _title.Width * pixelScale;
            float titleHeight = _title.Height * pixelScale;

            var titleBounds = new Rectangle(
                (bounds.Width - (int)titleWidth) / 2,
                (bounds.Height - (int)titleHeight) / 2,
                (int)titleWidth,
                (int)titleHeight);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(_title, titleBounds);
            graphics.Restore(graphicsState);

            Rectangle borderBounds = Rectangle.Inflate(bounds, -1, -1);
            using GraphicsPath borderPath = CreateRoundedPath(borderBounds, 16);
            using var border = new Pen(BorderColor, 1F);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawPath(border, borderPath);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _banner?.Dispose();
                _title?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class GlassSidebarPanel : Panel
    {
        public GlassSidebarPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true);
            BackColor = SurfaceColor;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = CreateRoundedPath(bounds, ChangelogPanelRadius);
            using var surface = new LinearGradientBrush(
                bounds,
                Color.FromArgb(39, 42, 47),
                Color.FromArgb(29, 32, 36),
                LinearGradientMode.Vertical);
            e.Graphics.FillPath(surface, path);

            using var border = new Pen(BorderColor);
            e.Graphics.DrawPath(border, path);
            using var highlight = new Pen(Color.FromArgb(30, Color.White));
            e.Graphics.DrawLine(highlight, ChangelogPanelRadius, 1, bounds.Width - ChangelogPanelRadius, 1);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            using GraphicsPath path = CreateRoundedPath(ClientRectangle, ChangelogPanelRadius);
            Region = new Region(path);
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

}
