using System.IO;
using System.Text.Json;

namespace PortDeck.Services;

public sealed class AppSettings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool AutoRefresh { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 5;
    public bool ShowSystemProcesses { get; set; } = false;
    public string Theme { get; set; } = "dark";
    public int UiScale { get; set; } = 100;
    public double DetailWidth { get; set; } = 440;
    public string ColumnLayout { get; set; } = "";
    public string CollapsedGroups { get; set; } = "";
    public bool CloseToTray { get; set; }
    public bool SortDescending { get; set; }
    public int SortMode { get; set; }
    public bool GroupByProject { get; set; } = true;
    public bool AutoCheckUpdates { get; set; }
    public string Language { get; set; } = "system";
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public SettingsStore()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortDeck");
        Directory.CreateDirectory(DataDirectory);
        _filePath = Path.Combine(DataDirectory, "settings.json");
    }

    public string DataDirectory { get; }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new AppSettings();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, _filePath, true);
        }
        catch
        {
            // Settings are a convenience; a read-only profile should not crash the app.
        }
    }
}
