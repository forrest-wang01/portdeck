using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using PortDeck.Localization;
using PortDeck.Models;
using PortDeck.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PortDeck;

public partial class MainWindow : Window
{
    private readonly PortScanner _scanner = new();
    private readonly FavoritesStore _favoritesStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly HistoryStore _historyStore = new();
    private readonly AppSettings _settings;
    private readonly ObservableCollection<PortEntry> _visible = new();
    private readonly ObservableCollection<HistoryEvent> _history = new();
    private readonly ListCollectionView _collectionView;
    private readonly HashSet<int> _favorites;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _toastTimer;

    private List<PortEntry> _all = new();
    private PortEntry? _selected;
    private PortEntry? _pendingKill;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayRefreshItem;
    private Forms.ToolStripMenuItem? _trayExitItem;
    private Drawing.Icon? _trayIcon;
    private bool _initialized;
    private bool _isScanning;
    private bool _suppressLanguage;
    private DateTime? _lastScanAt;
    private long _lastScanDuration;
    private ScanStats? _lastStats;
    private bool _lastScanFailed;
    private bool _hasHistoryBaseline;
    private bool _autoRefresh = true;
    private bool _showSystem = false;
    private bool _sortDescending;
    private int _sortMode;
    private bool _groupByProject = true;
    private bool _closeToTray;
    private bool _autoCheckUpdates;
    private string _theme = "dark";
    private int _uiScale = 100;
    private bool _exitRequested;
    private bool _startMaximized;
    private bool _updateCheckInProgress;
    private string _view = "ports";
    private string _filter = "all";
    private string _search = string.Empty;
    private string _languagePreference = "system";
    private int _intervalSeconds = 5;
    private bool _suppressNav;
    private bool _suppressFilter;

    public static readonly DependencyProperty SearchQueryProperty = DependencyProperty.Register(
        nameof(SearchQuery), typeof(string), typeof(MainWindow), new PropertyMetadata(string.Empty));

    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    public static readonly DependencyProperty StarColumnWidthProperty = DependencyProperty.Register(
        nameof(StarColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(32)));
    public static readonly DependencyProperty PortColumnWidthProperty = DependencyProperty.Register(
        nameof(PortColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(88)));
    public static readonly DependencyProperty ProcessColumnWidthProperty = DependencyProperty.Register(
        nameof(ProcessColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(2.2, GridUnitType.Star)));
    public static readonly DependencyProperty ProjectColumnWidthProperty = DependencyProperty.Register(
        nameof(ProjectColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(1.5, GridUnitType.Star)));
    public static readonly DependencyProperty AddressColumnWidthProperty = DependencyProperty.Register(
        nameof(AddressColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(1.3, GridUnitType.Star)));
    public static readonly DependencyProperty PidColumnWidthProperty = DependencyProperty.Register(
        nameof(PidColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(62)));
    public static readonly DependencyProperty ActionsColumnWidthProperty = DependencyProperty.Register(
        nameof(ActionsColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(72)));

    public GridLength StarColumnWidth { get => (GridLength)GetValue(StarColumnWidthProperty); set => SetValue(StarColumnWidthProperty, value); }
    public GridLength PortColumnWidth { get => (GridLength)GetValue(PortColumnWidthProperty); set => SetValue(PortColumnWidthProperty, value); }
    public GridLength ProcessColumnWidth { get => (GridLength)GetValue(ProcessColumnWidthProperty); set => SetValue(ProcessColumnWidthProperty, value); }
    public GridLength ProjectColumnWidth { get => (GridLength)GetValue(ProjectColumnWidthProperty); set => SetValue(ProjectColumnWidthProperty, value); }
    public GridLength AddressColumnWidth { get => (GridLength)GetValue(AddressColumnWidthProperty); set => SetValue(AddressColumnWidthProperty, value); }
    public GridLength PidColumnWidth { get => (GridLength)GetValue(PidColumnWidthProperty); set => SetValue(PidColumnWidthProperty, value); }
    public GridLength ActionsColumnWidth { get => (GridLength)GetValue(ActionsColumnWidthProperty); set => SetValue(ActionsColumnWidthProperty, value); }

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();
        _favorites = _favoritesStore.Load();
        _autoRefresh = _settings.AutoRefresh;
        _intervalSeconds = NormalizeInterval(_settings.RefreshIntervalSeconds);
        _showSystem = _settings.ShowSystemProcesses;
        GroupExpansionStore.Load(_settings.CollapsedGroups);
        _sortDescending = _settings.SortDescending;
        _sortMode = _settings.SortMode is >= 0 and <= 4 ? _settings.SortMode : 0;
        _groupByProject = _settings.GroupByProject;
        _closeToTray = _settings.CloseToTray;
        _autoCheckUpdates = _settings.AutoCheckUpdates;
        _startMaximized = _settings.WindowMaximized;
        _languagePreference = _settings.Language;
        _theme = string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        _uiScale = _settings.UiScale is 125 ? 125 : 100;
        RootLayout.LayoutTransform = new ScaleTransform(_uiScale / 100.0, _uiScale / 100.0);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_intervalSeconds) };
        _refreshTimer.Tick += RefreshTimer_Tick;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += ToastTimer_Tick;

        _collectionView = new ListCollectionView(_visible);
        PortsList.ItemsSource = _collectionView;

        foreach (var historyEvent in _historyStore.Load()) _history.Add(historyEvent);
        HistoryList.ItemsSource = _history;
        UpdateHistoryEmptyState();

        ApplySettingsToControls();
        RestoreColumnWidths();
        RestoreWindowBounds();
        if (_uiScale == 125 && _settings.WindowWidth is null && !_startMaximized)
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width * 1.25, Math.Max(MinWidth, area.Width - 16));
            Height = Math.Min(Height * 1.25, Math.Max(MinHeight, area.Height - 16));
            Left = Math.Max(area.Left, area.Left + (area.Width - Width) / 2);
            Top = Math.Max(area.Top, area.Top + (area.Height - Height) / 2);
        }
        ApplyGrouping();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3.0";
        VersionText.Text = $"v{version}";
        AboutVersionText.Text = $"PortDeck v{version}";
        DataPathText.Text = _favoritesStore.FilePath;

        _initialized = true;
        Loaded += MainWindow_Loaded;
        CreateTray();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        System.Windows.Application.Current.SessionEnding += OnSessionEnding;
    }

    private static int NormalizeInterval(int seconds) => seconds is 3 or 5 or 10 ? seconds : 5;

    private static string FormatPortCount(int count) =>
        L.Format(count == 1 ? "PortCountOne" : "PortCountFormat", count);

    private void ApplySettingsToControls()
    {
        AutoRefreshToggle.IsChecked = _autoRefresh;
        ShowSystemToggle.IsChecked = _showSystem;
        CloseToTrayToggle.IsChecked = _closeToTray;
        AutoUpdateToggle.IsChecked = _autoCheckUpdates;
        ThemeDark.IsChecked = _theme == "dark";
        ThemeLight.IsChecked = _theme == "light";
        Scale100.IsChecked = _uiScale == 100;
        Scale125.IsChecked = _uiScale == 125;
        Interval3.IsChecked = _intervalSeconds == 3;
        Interval5.IsChecked = _intervalSeconds == 5;
        Interval10.IsChecked = _intervalSeconds == 10;
        UpdateSortLabel();

        _suppressLanguage = true;
        LanguageSystem.IsChecked = _languagePreference is not ("zh-CN" or "en-US");
        LanguageChinese.IsChecked = _languagePreference == "zh-CN";
        LanguageEnglish.IsChecked = _languagePreference == "en-US";
        _suppressLanguage = false;

        UpdateGroupButtonVisual();
        UpdateHideSystemLabel();
    }

    private void UpdateHideSystemLabel()
    {
        if (HideSystemLabel is null) return;
        HideSystemLabel.Text = L.Get(_showSystem ? "HideSystem" : "ShowSystem");
    }

    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth is not double width ||
            _settings.WindowHeight is not double height ||
            width < MinWidth || height < MinHeight)
            return;

        var left = _settings.WindowLeft ?? double.NaN;
        var top = _settings.WindowTop ?? double.NaN;
        if (!double.IsFinite(left) || !double.IsFinite(top)) return;

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        var scale = _uiScale / 100.0;
        width = Math.Min(width * scale, SystemParameters.VirtualScreenWidth);
        height = Math.Min(height * scale, SystemParameters.VirtualScreenHeight);

        var target = new Rect(left, top, width, height);
        var virtualRect = new Rect(virtualLeft, virtualTop, virtualRight - virtualLeft, virtualBottom - virtualTop);
        if (!virtualRect.IntersectsWith(target)) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        Left = Math.Clamp(left, virtualLeft, virtualRight - width);
        Top = Math.Clamp(top, virtualTop, virtualBottom - height);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startMaximized) WindowState = WindowState.Maximized;
        if (_autoRefresh) _refreshTimer.Start();
        await RefreshAsync();

        if (_autoCheckUpdates)
            _ = CheckUpdatesAsync(silent: true);
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!_autoRefresh || !IsVisible || WindowState == WindowState.Minimized) return;
        await RefreshAsync(silent: true);
    }

    private async Task RefreshAsync(bool silent = false)
    {
        if (_isScanning) return;

        _isScanning = true;
        RefreshButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;

        if (!silent)
        {
            StatusText.Text = L.Get("ScanInProgress");
            StatusDot.Fill = (Brush)FindResource("WarnBrush");
        }

        try
        {
            var result = await _scanner.ScanAsync(_favorites);

            if (!result.Ok)
            {
                _lastScanFailed = true;
                StatusText.Text = L.Get("ScanFailed");
                StatusDot.Fill = (Brush)FindResource("DangerBrush");
                LastScanText.Text = result.Error ?? L.Get("UnknownError");
                ShowToast(result.Error ?? L.Get("ScanFailed"), true);
                return;
            }

            var previousHealth = _all
                .Where(entry => entry.HasHealthResult)
                .ToDictionary(
                    entry => (entry.Port, entry.Protocol, entry.Pid),
                    entry => (
                        entry.HealthState,
                        entry.HealthLatencyMs,
                        entry.HealthError,
                        entry.HealthStatusCode,
                        entry.HealthScheme));

            RecordHistory(result.Ports);
            _all = result.Ports;
            foreach (var entry in _all)
            {
                entry.IsFavorite = _favorites.Contains(entry.Port);

                if (!previousHealth.TryGetValue((entry.Port, entry.Protocol, entry.Pid), out var health))
                    continue;

                if (health.HealthState == HealthState.Healthy)
                    entry.SetHealth(
                        HealthState.Healthy,
                        health.HealthLatencyMs,
                        statusCode: health.HealthStatusCode,
                        scheme: health.HealthScheme);
                else if (health.HealthState == HealthState.Unhealthy)
                    entry.SetHealth(
                        HealthState.Unhealthy,
                        null,
                        health.HealthError,
                        health.HealthStatusCode,
                        health.HealthScheme);
                else if (health.HealthState == HealthState.Unsupported)
                    entry.SetHealth(HealthState.Unsupported);
            }

            UpdateStats(result.Stats);
            ApplyFilters();

            _lastScanFailed = false;
            StatusText.Text = L.Get("ScanNormal");
            StatusDot.Fill = (Brush)FindResource("AccentBrush");
            _lastScanAt = result.GeneratedAt;
            _lastScanDuration = result.DurationMs;
            _lastStats = result.Stats;
            LastScanText.Text = L.Format("LastScanFormat", result.GeneratedAt.ToString("HH:mm:ss"), result.DurationMs);
            ScanInfoText.Text = L.Format(
                "ScanInfoFormat",
                result.Stats.Ports,
                result.Stats.Processes,
                result.DurationMs);

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                Log.Info(result.Warning);
                if (!silent) ShowToast(result.Warning, true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Port scan failed.", ex);
            _lastScanFailed = true;
            StatusText.Text = L.Get("ScanFailed");
            StatusDot.Fill = (Brush)FindResource("DangerBrush");
            LastScanText.Text = ex.Message;
            ShowToast(L.Format("ScanFailedFormat", ex.Message), true);
        }
        finally
        {
            _isScanning = false;
            LoadingBar.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
        }
    }

    private void UpdateStats(ScanStats stats)
    {
        _lastStats = stats;
        StatPorts.Text = stats.Ports.ToString();
        StatProcesses.Text = stats.Processes.ToString();
        StatFavorites.Text = _favorites.Count.ToString();
        StatConflictsText.Text = stats.Conflicts.ToString();
        StatConflictsText.Foreground = (Brush)FindResource(stats.Conflicts > 0 ? "DangerBrush" : "TextBrush");
    }

    private void ApplyFilters()
    {
        IEnumerable<PortEntry> query = _all;

        if (_view == "favorites" || _filter == "favorite")
            query = query.Where(entry => entry.IsFavorite);

        query = _filter switch
        {
            "dev" => query.Where(entry => entry.IsDev),
            "conflict" => query.Where(entry => entry.Conflict),
            _ => query
        };

        if (!_showSystem)
            query = query.Where(entry => !entry.IsSystem);

        if (!string.IsNullOrWhiteSpace(_search))
            query = query.Where(entry => SearchMatcher.Matches(entry, _search));

        var filtered = query.ToList();
        if (_groupByProject)
        {
            var grouped = filtered
                .OrderBy(entry => entry.HasProject ? 0 : 1)
                .ThenBy(entry => entry.ProjectGroupKey, StringComparer.OrdinalIgnoreCase);

            grouped = _sortMode switch
            {
                1 => _sortDescending
                    ? grouped.ThenByDescending(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                    : grouped.ThenBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase),
                2 => _sortDescending
                    ? grouped.ThenByDescending(entry => entry.ProjectName, StringComparer.OrdinalIgnoreCase)
                    : grouped.ThenBy(entry => entry.ProjectName, StringComparer.OrdinalIgnoreCase),
                3 => _sortDescending
                    ? grouped.ThenByDescending(entry => entry.AddressSummary, StringComparer.OrdinalIgnoreCase)
                    : grouped.ThenBy(entry => entry.AddressSummary, StringComparer.OrdinalIgnoreCase),
                4 => _sortDescending
                    ? grouped.ThenByDescending(entry => entry.Pid)
                    : grouped.ThenBy(entry => entry.Pid),
                _ => _sortDescending
                    ? grouped.ThenByDescending(entry => entry.Port)
                    : grouped.ThenBy(entry => entry.Port)
            };

            filtered = grouped.ThenBy(entry => entry.Port).ToList();
        }
        else
        {
            filtered = SortEntries(filtered);
        }

        _visible.Clear();
        foreach (var entry in filtered) _visible.Add(entry);

        ResultCountText.Text = FormatPortCount(_visible.Count);
        UpdateSortLabel();

        if (_visible.Count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Visible;
        EmptyAction.Visibility = Visibility.Visible;

        if (_isScanning && _all.Count == 0)
        {
            EmptyTitle.Text = L.Get("ScanningPorts");
            EmptySubtitle.Text = L.Get("FirstScanHint");
            EmptyAction.Visibility = Visibility.Collapsed;
        }
        else if (_all.Count == 0)
        {
            EmptyTitle.Text = L.Get("NoPorts");
            EmptySubtitle.Text = L.Get("NoPortsHint");
            EmptyAction.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyTitle.Text = L.Get("NoMatches");
            EmptySubtitle.Text = L.Get("NoMatchesHint");
        }
    }

    private static string HistoryKey(PortEntry entry) => $"{entry.Port}:{entry.Protocol}:{entry.Pid}";

    private void RecordHistory(IReadOnlyList<PortEntry> current)
    {
        if (!_hasHistoryBaseline)
        {
            _hasHistoryBaseline = true;
            return;
        }

        var previous = _all.ToDictionary(HistoryKey, StringComparer.OrdinalIgnoreCase);
        var currentMap = current.ToDictionary(HistoryKey, StringComparer.OrdinalIgnoreCase);
        var events = new List<HistoryEvent>();

        foreach (var entry in current.Where(entry => !previous.ContainsKey(HistoryKey(entry))))
            events.Add(ToHistoryEvent(entry, "up"));

        foreach (var entry in _all.Where(entry => !currentMap.ContainsKey(HistoryKey(entry))))
            events.Add(ToHistoryEvent(entry, "down"));

        if (events.Count == 0) return;

        _historyStore.Append(events);
        foreach (var historyEvent in events)
            _history.Insert(0, historyEvent);

        while (_history.Count > 500) _history.RemoveAt(_history.Count - 1);
        UpdateHistoryEmptyState();

        var favoriteEvent = events.FirstOrDefault(item => _favorites.Contains(item.Port));
        if (favoriteEvent is null || _tray is null) return;

        var message = L.Format(
            favoriteEvent.Event == "down" ? "HistoryNotificationDownFormat" : "HistoryNotificationUpFormat",
            favoriteEvent.Port,
            favoriteEvent.Process);
        _tray.ShowBalloonTip(
            2500,
            L.Get("HistoryNotificationTitle"),
            message,
            favoriteEvent.Event == "down" ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    private static HistoryEvent ToHistoryEvent(PortEntry entry, string kind) => new()
    {
        Time = DateTime.Now,
        Event = kind,
        Port = entry.Port,
        Protocol = entry.Protocol,
        Pid = entry.Pid,
        Process = entry.ProcessName,
        Project = entry.Project?.Name ?? string.Empty,
        Address = entry.Addresses.FirstOrDefault() ?? string.Empty
    };

    private void UpdateHistoryEmptyState()
    {
        if (HistoryEmptyState is null) return;
        HistoryEmptyState.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        _historyStore.Clear();
        UpdateHistoryEmptyState();
        ShowToast(L.Get("HistoryCleared"));
    }

    private void UpdatePageTitles()
    {
        if (_view == "favorites")
        {
            PageTitle.Text = L.Get("FavoritesTitle");
            PageSubtitle.Text = L.Get("FavoritesSubtitle");
        }
        else
        {
            PageTitle.Text = L.Get("LocalServicesTitle");
            PageSubtitle.Text = L.Get("LocalServicesSubtitle");
        }
    }

    private void SetView(string view)
    {
        _view = view;

        _suppressNav = true;
        NavPorts.IsChecked = view == "ports";
        NavFavorites.IsChecked = view == "favorites";
        NavHistory.IsChecked = view == "history";
        NavSettings.IsChecked = view == "settings";
        _suppressNav = false;

        if (view == "settings")
        {
            DashboardView.Visibility = Visibility.Collapsed;
            HistoryView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
            return;
        }

        if (view == "history")
        {
            DashboardView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            HistoryView.Visibility = Visibility.Visible;
            return;
        }

        DashboardView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;

        if (view == "favorites")
        {
            _filter = "favorite";
            _suppressFilter = true;
            ChipFavorite.IsChecked = true;
            _suppressFilter = false;
            UpdatePageTitles();
        }
        else
        {
            _filter = "all";
            _suppressFilter = true;
            ChipAll.IsChecked = true;
            _suppressFilter = false;

            UpdatePageTitles();
        }

        ApplyFilters();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressNav || sender is not RadioButton button) return;
        SetView(button.Tag?.ToString() ?? "ports");
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressFilter || sender is not RadioButton button) return;
        _filter = button.Tag?.ToString() ?? "all";

        if (_filter == "favorite")
        {
            _view = "favorites";
        }
        else if (_view == "favorites")
        {
            _view = "ports";
        }

        _suppressNav = true;
        NavPorts.IsChecked = _view == "ports";
        NavFavorites.IsChecked = _view == "favorites";
        _suppressNav = false;

        UpdatePageTitles();
        ApplyFilters();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        _search = SearchBox.Text;
        SearchQuery = _search;
        ApplyFilters();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _autoRefresh = AutoRefreshToggle.IsChecked == true;
        if (_autoRefresh)
        {
            _refreshTimer.Start();
            ShowToast(L.Format("AutoRefreshOnFormat", _intervalSeconds));
        }
        else
        {
            _refreshTimer.Stop();
            ShowToast(L.Get("AutoRefreshOff"));
        }

        PersistSettings();
    }

    private void Interval_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not RadioButton button) return;
        if (!int.TryParse(button.Tag?.ToString(), out var seconds) || seconds < 1) return;

        _intervalSeconds = seconds;
        _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
        if (_autoRefresh) _refreshTimer.Start();
        ShowToast(L.Format("IntervalSetFormat", seconds));
        PersistSettings();
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressLanguage || sender is not RadioButton button) return;

        _languagePreference = button.Tag?.ToString() ?? "system";
        _settings.Language = _languagePreference;
        LocalizationService.Instance.SetLanguage(_languagePreference);
        PersistSettings();
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not RadioButton button) return;
        _theme = button.Tag?.ToString() == "light" ? "light" : "dark";
        _settings.Theme = _theme;
        PersistSettings();
        ((App)System.Windows.Application.Current).ChangeTheme(_theme);
    }

    private void Scale_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not RadioButton button) return;
        _uiScale = button.Tag?.ToString() == "125" ? 125 : 100;
        _settings.UiScale = _uiScale;
        PersistSettings();
        ((App)System.Windows.Application.Current).ChangeTheme(_theme);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLocalizedChrome();
        _ = RefreshAsync(silent: true);
    }

    private void UpdateLocalizedChrome()
    {
        UpdatePageTitles();
        UpdateGroupButtonVisual();
        UpdateHideSystemLabel();
        UpdateSortLabel();
        ResultCountText.Text = FormatPortCount(_visible.Count);
        HistoryList.Items.Refresh();
        UpdateHistoryEmptyState();
        UpdateTrayText();

        if (_isScanning)
            StatusText.Text = L.Get("ScanInProgress");
        else if (_lastScanFailed)
            StatusText.Text = L.Get("ScanFailed");
        else if (_lastScanAt is null)
            StatusText.Text = L.Get("ReadyToScan");
        else
            StatusText.Text = L.Get("ScanNormal");

        if (_lastScanAt is not null)
            LastScanText.Text = L.Format(
                "LastScanFormat",
                _lastScanAt.Value.ToString("HH:mm:ss"),
                _lastScanDuration);

        if (_lastStats is not null)
            ScanInfoText.Text = L.Format(
                "ScanInfoFormat",
                _lastStats.Ports,
                _lastStats.Processes,
                _lastScanDuration);

        if (KillOverlay.Visibility == Visibility.Visible && _pendingKill is not null)
            KillMessage.Text = L.Format(
                "KillMessageFormat",
                _pendingKill.ProcessName,
                _pendingKill.Pid,
                _pendingKill.Port);

        ApplyFilters();
    }

    private void ShowSystem_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _showSystem = ShowSystemToggle.IsChecked == true;
        UpdateHideSystemLabel();
        ApplyFilters();
        PersistSettings();
    }

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _autoCheckUpdates = AutoUpdateToggle.IsChecked == true;
        PersistSettings();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckUpdatesAsync(silent: false);

    private async Task CheckUpdatesAsync(bool silent)
    {
        if (_updateCheckInProgress) return;

        _updateCheckInProgress = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = L.Get("UpdateChecking");

        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var result = await UpdateService.CheckAsync(currentVersion);

            if (!result.Configured)
            {
                UpdateStatusText.Text = L.Get("UpdateNotConfigured");
                if (!silent) ShowToast(L.Get("UpdateNotConfigured"), true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                UpdateStatusText.Text = L.Format("UpdateCheckFailedFormat", result.Error);
                if (!silent) ShowToast(UpdateStatusText.Text, true);
                return;
            }

            if (result.UpdateAvailable)
            {
                var message = L.Format("UpdateAvailableFormat", result.LatestVersion);
                UpdateStatusText.Text = message;
                if (silent)
                {
                    _tray?.ShowBalloonTip(
                        3000,
                        L.Get("UpdateAvailableTitle"),
                        message,
                        Forms.ToolTipIcon.Info);
                    return;
                }

                var answer = MessageBox.Show(
                    $"{message}\n\n{L.Get("UpdateInstallPrompt")}",
                    L.Get("UpdateAvailableTitle"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information);

                if (answer == MessageBoxResult.Yes)
                    await InstallUpdateAsync(result);
                else if (answer == MessageBoxResult.No && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                    OpenExternal(result.ReleaseUrl);
                return;
            }

            UpdateStatusText.Text = L.Get("UpdateUpToDate");
            if (!silent) ShowToast(L.Get("UpdateUpToDate"));
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = L.Format("UpdateCheckFailedFormat", ex.Message);
            if (!silent) ShowToast(UpdateStatusText.Text, true);
        }
        finally
        {
            _updateCheckInProgress = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async Task InstallUpdateAsync(UpdateCheckResult update)
    {
        if (string.IsNullOrWhiteSpace(update.AssetUrl))
        {
            UpdateStatusText.Text = L.Get("UpdateNoAsset");
            ShowToast(UpdateStatusText.Text, true);
            return;
        }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) ||
            !Path.GetFileName(currentExe).StartsWith("PortDeck", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatusText.Text = L.Get("UpdatePackagedOnly");
            ShowToast(UpdateStatusText.Text, true);
            return;
        }

        try
        {
            var probe = Path.Combine(
                Path.GetDirectoryName(currentExe) ?? Path.GetTempPath(),
                $".portdeck-write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }
        catch
        {
            UpdateStatusText.Text = L.Get("UpdateNoPermission");
            ShowToast(UpdateStatusText.Text, true);
            return;
        }

        var destination = Path.Combine(
            Path.GetTempPath(),
            $"PortDeck-{update.LatestVersion}-win-x64.exe");

        try
        {
            UpdateStatusText.Text = L.Get("UpdateDownloading");
            var progress = new Progress<double>(fraction =>
                UpdateStatusText.Text = $"{L.Get("UpdateDownloading")} {fraction:P0}");

            await UpdateService.DownloadAndVerifyAsync(update, destination, progress);
            UpdateStatusText.Text = L.Get("UpdateInstalling");

            if (!UpdateService.LaunchUpdater(destination, currentExe))
                throw new InvalidOperationException("Could not launch the updater.");

            MessageBox.Show(
                L.Get("UpdateRestarting"),
                L.Get("UpdateAvailableTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _exitRequested = true;
            Close();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = L.Format("UpdateInstallFailedFormat", ex.Message);
            ShowToast(UpdateStatusText.Text, true);
        }
    }

    private static void OpenExternal(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // The browser may be unavailable; the message box already has the info.
        }
    }

    private void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _closeToTray = CloseToTrayToggle.IsChecked == true;
        PersistSettings();
        ShowToast(L.Get(_closeToTray ? "CloseToTrayOn" : "CloseToTrayOff"));
    }

    private List<PortEntry> SortEntries(IEnumerable<PortEntry> entries)
    {
        var list = entries.ToList();
        return _sortMode switch
        {
            1 => (_sortDescending
                    ? list.OrderByDescending(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                    : list.OrderBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase))
                .ThenBy(entry => entry.Port)
                .ToList(),
            2 => (_sortDescending
                    ? list.OrderByDescending(entry => entry.ProjectGroupKey, StringComparer.OrdinalIgnoreCase)
                    : list.OrderBy(entry => entry.ProjectGroupKey, StringComparer.OrdinalIgnoreCase))
                .ThenBy(entry => entry.Port)
                .ToList(),
            3 => (_sortDescending
                    ? list.OrderByDescending(entry => entry.AddressSummary, StringComparer.OrdinalIgnoreCase)
                    : list.OrderBy(entry => entry.AddressSummary, StringComparer.OrdinalIgnoreCase))
                .ThenBy(entry => entry.Port)
                .ToList(),
            4 => (_sortDescending
                    ? list.OrderByDescending(entry => entry.Pid)
                    : list.OrderBy(entry => entry.Pid))
                .ThenBy(entry => entry.Port)
                .ToList(),
            _ => (_sortDescending
                    ? list.OrderByDescending(entry => entry.Port)
                    : list.OrderBy(entry => entry.Port))
                .ThenBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private void UpdateSortLabel()
    {
        var key = _sortMode switch
        {
            1 => _sortDescending ? "SortProcessDesc" : "SortProcessAsc",
            2 => _sortDescending ? "SortProjectDesc" : "SortProjectAsc",
            3 => _sortDescending ? "SortAddressDesc" : "SortAddressAsc",
            4 => _sortDescending ? "SortPidDesc" : "SortPidAsc",
            _ => _sortDescending ? "SortPortDesc" : "SortPortAsc"
        };

        SortLabel.Text = L.Get(key);
        UpdateSortHeaders();
    }

    private void HeaderPort_Click(object sender, RoutedEventArgs e) => SetSort(0);

    private void HeaderProcess_Click(object sender, RoutedEventArgs e) => SetSort(1);

    private void HeaderProject_Click(object sender, RoutedEventArgs e) => SetSort(2);

    private void HeaderAddress_Click(object sender, RoutedEventArgs e) => SetSort(3);

    private void HeaderPid_Click(object sender, RoutedEventArgs e) => SetSort(4);

    private void SetSort(int mode)
    {
        if (_sortMode == mode)
            _sortDescending = !_sortDescending;
        else
        {
            _sortMode = mode;
            _sortDescending = false;
        }

        UpdateSortLabel();
        ApplyFilters();
        PersistSettings();
    }

    private void UpdateSortHeaders()
    {
        if (HeaderPort is null) return;

        HeaderPort.Content = L.Get("ColumnPort") + (_sortMode == 0 ? (_sortDescending ? "  ↓" : "  ↑") : "");
        HeaderProcess.Content = L.Get("ColumnProcess") + (_sortMode == 1 ? (_sortDescending ? "  ↓" : "  ↑") : "");
        HeaderProject.Content = L.Get("ColumnProject") + (_sortMode == 2 ? (_sortDescending ? "  ↓" : "  ↑") : "");
        HeaderAddress.Content = L.Get("ColumnAddress") + (_sortMode == 3 ? (_sortDescending ? "  ↓" : "  ↑") : "");
        HeaderPid.Content = "PID" + (_sortMode == 4 ? (_sortDescending ? "  ↓" : "  ↑") : "");
    }

    private void RestoreColumnWidths()
    {
        var parts = (_settings.ColumnLayout ?? "").Split('|');
        if (parts.Length != 7) return;

        var properties = new[]
        {
            StarColumnWidthProperty,
            PortColumnWidthProperty,
            ProcessColumnWidthProperty,
            ProjectColumnWidthProperty,
            AddressColumnWidthProperty,
            PidColumnWidthProperty,
            ActionsColumnWidthProperty
        };

        for (var i = 0; i < parts.Length; i++)
        {
            var text = parts[i].Trim();
            var isStar = text.EndsWith('*');
            var number = isStar ? text[..^1] : text;
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
                continue;

            SetValue(properties[i], isStar ? new GridLength(value, GridUnitType.Star) : new GridLength(value));
        }
    }

    private void ColumnResize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _settings.ColumnLayout = string.Join("|", new[]
        {
            FormatColumnWidth(StarColumnWidth),
            FormatColumnWidth(PortColumnWidth),
            FormatColumnWidth(ProcessColumnWidth),
            FormatColumnWidth(ProjectColumnWidth),
            FormatColumnWidth(AddressColumnWidth),
            FormatColumnWidth(PidColumnWidth),
            FormatColumnWidth(ActionsColumnWidth)
        });
        PersistSettings();
    }

    private static string FormatColumnWidth(GridLength width) =>
        width.IsStar
            ? width.Value.ToString("0.###", CultureInfo.InvariantCulture) + "*"
            : width.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private void ColumnResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Parent is not Grid grid)
            return;

        var index = Grid.GetColumn(thumb);
        if (index < 0 || index >= grid.ColumnDefinitions.Count)
            return;

        var actual = grid.ColumnDefinitions[index].ActualWidth;
        if (actual <= 0) return;

        var minimum = index switch
        {
            1 => 70d,
            2 => 140d,
            3 => 100d,
            4 => 90d,
            5 => 54d,
            6 => 60d,
            _ => 24d
        };
        var width = Math.Max(minimum, actual + e.HorizontalChange);
        var value = new GridLength(width);

        switch (index)
        {
            case 1: PortColumnWidth = value; break;
            case 2: ProcessColumnWidth = value; break;
            case 3: ProjectColumnWidth = value; break;
            case 4: AddressColumnWidth = value; break;
            case 5: PidColumnWidth = value; break;
            case 6: ActionsColumnWidth = value; break;
        }
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (_sortDescending)
        {
            _sortDescending = false;
            _sortMode = (_sortMode + 1) % 5;
        }
        else
        {
            _sortDescending = true;
        }

        UpdateSortLabel();
        ApplyFilters();
        PersistSettings();
    }

    private void ToggleSystem_Click(object sender, RoutedEventArgs e)
    {
        ShowSystemToggle.IsChecked = !_showSystem;
    }

    private void PortsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || PortsList.SelectedItem is not PortEntry entry) return;
        OpenDetail(entry);
        e.Handled = true;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = L.Get("Export"),
            Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
            DefaultExt = ".json",
            FileName = $"PortDeck-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var extension = Path.GetExtension(dialog.FileName);
            var content = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? BuildCsv(_visible)
                : BuildJson(_visible);
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            ShowToast(L.Get("ExportDone"));
        }
        catch (Exception ex)
        {
            ShowToast(L.Format("ExportFailedFormat", ex.Message), true);
        }
    }

    private static string BuildJson(IEnumerable<PortEntry> entries)
    {
        var data = entries.Select(entry => new
        {
            entry.Port,
            entry.Protocol,
            entry.Pid,
            Process = entry.ProcessName,
            entry.ProcessFile,
            Project = entry.ProjectName,
            ProjectPath = entry.Project?.Path,
            entry.WorkingDirectory,
            Addresses = entry.Addresses,
            Exposure = entry.Exposure,
            entry.CommandLine,
            StartedAt = entry.StartedAt?.ToString("o")
        });

        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildCsv(IEnumerable<PortEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("port,protocol,pid,process,project,projectPath,workingDirectory,addresses,exposure,commandLine,startedAt");
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                entry.Port.ToString(),
                entry.Protocol,
                entry.Pid.ToString(),
                Csv(entry.ProcessName),
                Csv(entry.ProjectName),
                Csv(entry.Project?.Path ?? string.Empty),
                Csv(entry.WorkingDirectory),
                Csv(string.Join(" ", entry.Addresses)),
                Csv(entry.Exposure),
                Csv(entry.CommandLine),
                Csv(entry.StartedAt?.ToString("o") ?? string.Empty)
            }));
        }

        return builder.ToString();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private void DetailCopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        try
        {
            var json = JsonSerializer.Serialize(_selected, new JsonSerializerOptions { WriteIndented = true });
            Clipboard.SetText(json);
            ShowToast(L.Get("CopyJsonDone"));
        }
        catch (Exception ex)
        {
            ShowToast(L.Format("CopyFailedFormat", ex.Message), true);
        }
    }

    private void DetailTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        try
        {
            ProcessActions.OpenTerminal(_selected.WorkingDirectory);
            ShowToast(L.Get("TerminalOpened"));
        }
        catch (Exception ex)
        {
            ShowToast(L.Format("TerminalFailedFormat", ex.Message), true);
        }
    }

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        _groupByProject = !_groupByProject;
        ApplyGrouping();
        UpdateGroupButtonVisual();
        ApplyFilters();
        PersistSettings();
        ShowToast(L.Get(_groupByProject ? "GroupedOn" : "GroupedOff"));
    }

    private void ApplyGrouping()
    {
        using (_collectionView.DeferRefresh())
        {
            _collectionView.GroupDescriptions.Clear();
            if (_groupByProject)
            {
                _collectionView.GroupDescriptions.Add(
                    new PropertyGroupDescription(nameof(PortEntry.ProjectGroupKey)));
            }
        }
    }

    private void UpdateGroupButtonVisual()
    {
        if (GroupLabel is null || GroupButton is null) return;

        GroupLabel.Text = L.Get(_groupByProject ? "Grouped" : "GroupByProject");
        GroupButton.Background = (Brush)FindResource(_groupByProject ? "AccentSoftBrush" : "PanelBrush");
        GroupButton.BorderBrush = (Brush)FindResource(_groupByProject ? "AccentBrush" : "BorderBrush");
        GroupButton.Foreground = (Brush)FindResource(_groupByProject ? "AccentBrush" : "TextBrush");
    }

    private void PersistSettings()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width >= MinWidth &&
            bounds.Height >= MinHeight &&
            double.IsFinite(bounds.Left) &&
            double.IsFinite(bounds.Top))
        {
            var scale = _uiScale / 100.0;
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width / scale;
            _settings.WindowHeight = bounds.Height / scale;
        }

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.AutoRefresh = _autoRefresh;
        _settings.RefreshIntervalSeconds = _intervalSeconds;
        _settings.ShowSystemProcesses = _showSystem;
        _settings.CloseToTray = _closeToTray;
        _settings.AutoCheckUpdates = _autoCheckUpdates;
        _settings.Theme = _theme;
        _settings.UiScale = _uiScale;
        _settings.SortDescending = _sortDescending;
        _settings.SortMode = _sortMode;
        _settings.GroupByProject = _groupByProject;
        _settings.Language = _languagePreference;
        _settings.CollapsedGroups = GroupExpansionStore.Serialize();
        _settingsStore.Save(_settings);
    }

    private void Group_Expanded(object sender, RoutedEventArgs e) => SetGroupExpanded(sender, true);

    private void Group_Collapsed(object sender, RoutedEventArgs e) => SetGroupExpanded(sender, false);

    private void SetGroupExpanded(object sender, bool expanded)
    {
        if (sender is not Expander { DataContext: System.Windows.Data.CollectionViewGroup group }) return;
        GroupExpansionStore.Set(group.Name?.ToString(), expanded);

        if (_initialized)
            PersistSettings();
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        _exitRequested = true;
        PersistSettings();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _search = string.Empty;

        _suppressFilter = true;
        ChipAll.IsChecked = true;
        _suppressFilter = false;
        _filter = "all";

        SetView("ports");
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PortEntry entry }) return;
        ToggleFavorite(entry.Port);
    }

    private void DetailFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) ToggleFavorite(_selected.Port);
    }

    private void ToggleFavorite(int port)
    {
        var nowFavorite = !_favorites.Contains(port);
        if (nowFavorite) _favorites.Add(port);
        else _favorites.Remove(port);
        _favoritesStore.Save(_favorites);

        foreach (var entry in _all.Where(item => item.Port == port))
            entry.IsFavorite = nowFavorite;

        StatFavorites.Text = _favorites.Count.ToString();

        if (_view == "favorites" || _filter == "favorite")
            ApplyFilters();

        ShowToast(L.Format(nowFavorite ? "FavoriteAddedFormat" : "FavoriteRemovedFormat", port));
    }

    private void OpenPort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) OpenEntry(entry);
    }

    private void CopyPort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) CopyEntry(entry);
    }

    private void KillPort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) ShowKillConfirmation(entry);
    }

    private static PortEntry? ContextEntry(object sender) =>
        sender is MenuItem { DataContext: PortEntry entry } ? entry : null;

    private async void ContextHealth_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } entry) await CheckHealthAsync(entry);
    }

    private void ContextDetails_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } entry) OpenDetail(entry);
    }

    private void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } entry) OpenEntry(entry);
    }

    private void ContextCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } entry) CopyEntry(entry);
    }

    private void ContextKill_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } entry) ShowKillConfirmation(entry);
    }

    private async void CheckHealth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) await CheckHealthAsync(entry);
    }

    private async void DetailHealth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) await CheckHealthAsync(entry);
    }

    private async Task CheckHealthAsync(PortEntry entry)
    {
        entry.SetHealth(HealthState.Checking);
        var result = await HealthCheckService.CheckAsync(entry);

        if (!result.Supported)
        {
            entry.SetHealth(HealthState.Unsupported);
            return;
        }

        if (result.Healthy)
        {
            entry.SetHealth(
                HealthState.Healthy,
                result.LatencyMs,
                statusCode: result.StatusCode,
                scheme: result.Scheme);
        }
        else
        {
            entry.SetHealth(
                HealthState.Unhealthy,
                result.LatencyMs,
                result.Error,
                result.StatusCode,
                result.Scheme);
        }
    }

    private void DetailOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) OpenEntry(entry);
    }

    private void DetailCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) CopyEntry(entry);
    }

    private void DetailKill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) ShowKillConfirmation(entry);
    }

    private void OpenEntry(PortEntry entry)
    {
        try
        {
            ProcessActions.OpenInBrowser(entry);
        }
        catch (Exception ex)
        {
            ShowToast(L.Format("OpenBrowserFailedFormat", ex.Message), true);
        }
    }

    private void CopyEntry(PortEntry entry)
    {
        try
        {
            Clipboard.SetText(ProcessActions.GetBrowserUrl(entry));
            ShowToast(L.Get("AddressCopied"));
        }
        catch (Exception ex)
        {
            ShowToast(L.Format("CopyFailedFormat", ex.Message), true);
        }
    }

    private void PortRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is not Border { Tag: PortEntry entry }) return;

        OpenDetail(entry);
    }

    private void ShowDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PortEntry entry }) OpenDetail(entry);
    }

    private void RowMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void OpenDetail(PortEntry entry)
    {
        _selected = entry;
        DetailPanel.DataContext = entry;
        DetailOverlay.Visibility = Visibility.Visible;
        UpdateDetailLayout();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized || DetailOverlay.Visibility != Visibility.Visible) return;
        UpdateDetailLayout();
    }

    private void UpdateDetailLayout()
    {
        if (DetailPanel is null || DetailResizeThumb is null) return;

        var useBottomSheet = ActualWidth < 1120;
        DetailResizeThumb.Visibility = useBottomSheet ? Visibility.Collapsed : Visibility.Visible;

        if (useBottomSheet)
        {
            DetailPanel.Width = double.NaN;
            DetailPanel.Height = Math.Clamp(ActualHeight * 0.62, 320, 560);
            DetailPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            DetailPanel.VerticalAlignment = VerticalAlignment.Bottom;
            DetailPanel.Margin = new Thickness(12, 0, 12, 12);
            DetailPanel.CornerRadius = new CornerRadius(16, 16, 0, 0);
        }
        else
        {
            var maxWidth = Math.Max(340, ActualWidth - 48);
            DetailPanel.Width = Math.Clamp(_settings.DetailWidth, 340, Math.Min(680, maxWidth));
            DetailPanel.Height = double.NaN;
            DetailPanel.HorizontalAlignment = HorizontalAlignment.Right;
            DetailPanel.VerticalAlignment = VerticalAlignment.Stretch;
            DetailPanel.Margin = new Thickness(0, 12, 12, 12);
            DetailPanel.CornerRadius = new CornerRadius(16);
        }
    }

    private void DetailResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DetailPanel is null) return;
        var maxWidth = Math.Max(340, ActualWidth - 48);
        DetailPanel.Width = Math.Clamp(
            DetailPanel.ActualWidth - e.HorizontalChange,
            340,
            Math.Min(680, maxWidth));
    }

    private void DetailResize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _settings.DetailWidth = DetailPanel.Width;
        PersistSettings();
    }

    private void DetailOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CloseDetail();

    private void DetailPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetail();

    private void CloseDetail()
    {
        DetailOverlay.Visibility = Visibility.Collapsed;
        DetailPanel.DataContext = null;
        _selected = null;
    }

    private void ShowKillConfirmation(PortEntry entry)
    {
        if (ProcessProtection.IsProtected(entry.Pid, entry.ProcessName))
        {
            MessageBox.Show(
                L.Get("ProtectedProcessMessage"),
                L.Get("ProtectedProcessTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _pendingKill = entry;
        KillMessage.Text = L.Format("KillMessageFormat", entry.ProcessName, entry.Pid, entry.Port);
        KillOverlay.Visibility = Visibility.Visible;
    }

    private void CancelKill_Click(object sender, RoutedEventArgs e)
    {
        KillOverlay.Visibility = Visibility.Collapsed;
        _pendingKill = null;
    }

    private async void ConfirmKill_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingKill is null) return;

        var target = _pendingKill;
        _pendingKill = null;
        KillOverlay.Visibility = Visibility.Collapsed;
        ConfirmKillButton.IsEnabled = false;

        var (ok, message) = ProcessActions.KillProcessTree(target.Pid);
        ConfirmKillButton.IsEnabled = true;
        ShowToast(message, !ok);

        if (ok)
        {
            if (_selected?.Pid == target.Pid) CloseDetail();
            await RefreshAsync(silent: true);
        }
    }

    private void ClearFavorites_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            L.Get("ConfirmClearFavoritesMessage"),
            L.Get("ConfirmClearFavoritesTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        _favorites.Clear();
        _favoritesStore.Save(_favorites);
        foreach (var entry in _all) entry.IsFavorite = false;
        StatFavorites.Text = "0";
        ApplyFilters();
        ShowToast(L.Get("FavoritesCleared"));
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log.OpenFolder();
        }
        catch (Exception ex)
        {
            ShowToast(L.Get("OpenLogs") + ": " + ex.Message, true);
        }
    }

    private void CreateTray()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource is not null)
                _trayIcon = new Drawing.Icon(resource.Stream);

            _tray = new Forms.NotifyIcon
            {
                Icon = _trayIcon ?? Drawing.SystemIcons.Application,
                Visible = true,
                Text = L.Get("TrayText")
            };

            var menu = new Forms.ContextMenuStrip();
            _trayOpenItem = new Forms.ToolStripMenuItem(L.Get("TrayOpen"));
            _trayOpenItem.Click += (_, _) => ActivateFromExternal();
            _trayRefreshItem = new Forms.ToolStripMenuItem(L.Get("TrayRefresh"));
            _trayRefreshItem.Click += async (_, _) => await RefreshAsync();
            _trayExitItem = new Forms.ToolStripMenuItem(L.Get("TrayExit"));
            _trayExitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(_trayOpenItem);
            menu.Items.Add(_trayRefreshItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_trayExitItem);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ActivateFromExternal();
        }
        catch
        {
            // The tray icon is optional; the main window remains fully usable.
        }
    }

    private void UpdateTrayText()
    {
        if (_tray is not null) _tray.Text = L.Get("TrayText");
        if (_trayOpenItem is not null) _trayOpenItem.Text = L.Get("TrayOpen");
        if (_trayRefreshItem is not null) _trayRefreshItem.Text = L.Get("TrayRefresh");
        if (_trayExitItem is not null) _trayExitItem.Text = L.Get("TrayExit");
    }

    public void ActivateFromExternal()
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            ShowInTaskbar = true;
            Activate();
            Topmost = true;
            Topmost = false;
        });
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void ShowToast(string message, bool error = false)
    {
        ToastText.Text = message;
        ToastGlyph.Text = error ? "\uE7BA" : "\uE73E";
        ToastGlyph.Foreground = (Brush)FindResource(error ? "DangerBrush" : "AccentBrush");
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, null);
        Toast.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            Toast.Visibility = Visibility.Collapsed;
            Toast.Opacity = 0;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse button is released between events.
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MaximizeGlyph.Text = "\uE923";
            WindowFrame.Margin = new Thickness(7);
            WindowFrame.BorderThickness = new Thickness(0);
        }
        else
        {
            MaximizeGlyph.Text = "\uE922";
            WindowFrame.Margin = new Thickness(0);
            WindowFrame.BorderThickness = new Thickness(1);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            _ = RefreshAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (KillOverlay.Visibility == Visibility.Visible)
            {
                CancelKill_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (DetailOverlay.Visibility == Visibility.Visible)
            {
                CloseDetail();
                e.Handled = true;
            }
        }
    }

    internal void CloseForRebuild()
    {
        _exitRequested = true;
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        PersistSettings();

        if (_closeToTray && !_exitRequested)
        {
            e.Cancel = true;
            Hide();
            _tray?.ShowBalloonTip(
                1800,
                L.Get("TrayBalloonTitle"),
                L.Get("TrayBalloonText"),
                Forms.ToolTipIcon.Info);
            return;
        }

        _refreshTimer.Stop();
        _toastTimer.Stop();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        System.Windows.Application.Current.SessionEnding -= OnSessionEnding;

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
            _tray = null;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Keep a custom-chrome window inside the monitor work area when maximized,
        // so it never covers the taskbar.
        if (msg == 0x0024)
        {
            ApplyWorkAreaToMaximize(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyWorkAreaToMaximize(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        info.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        info.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        info.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
        info.MaxTrackSize = info.MaxSize;
        Marshal.StructureToPtr(info, lParam, false);
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
