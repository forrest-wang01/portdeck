using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using PortDeck.Localization;

namespace PortDeck.Models;

public sealed class ProjectInfo
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

public enum HealthState
{
    Unknown,
    Checking,
    Healthy,
    Unhealthy,
    Unsupported
}

public sealed class PortEntry : INotifyPropertyChanged
{
    private bool _isFavorite;
    private HealthState _healthState = HealthState.Unknown;
    private long? _healthLatencyMs;
    private string? _healthError;
    private int? _healthStatusCode;
    private string? _healthScheme;

    public int Port { get; init; }
    public int Pid { get; init; }
    public string Protocol { get; init; } = "TCP";
    public List<string> Addresses { get; init; } = new();
    public string ProcessName { get; init; } = L.Get("UnknownProcess");
    public string ProcessFile { get; init; } = string.Empty;
    public string ExePath { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Container { get; init; } = string.Empty;
    public string Wsl { get; init; } = string.Empty;
    public DateTime? StartedAt { get; init; }
    public ProjectInfo? Project { get; init; }
    public bool IsSystem { get; init; }
    public bool IsDev { get; init; }
    public bool Conflict { get; set; }
    public string Exposure { get; init; } = "local";

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteAutomationName));
        }
    }

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteAutomationName =>
        L.Format(IsFavorite ? "UnfavoritePortFormat" : "FavoritePortFormat", Port);

    public string DetailAutomationName => L.Format("DetailsPortFormat", Port);

    public string OpenAutomationName => L.Format("OpenPortFormat", Port);

    public string CopyAutomationName => L.Format("CopyPortFormat", Port);

    public string KillAutomationName => L.Format("KillPortFormat", Port);

    public string HealthAutomationName => L.Format("HealthPortFormat", Port);

    public string PortLabel => Port.ToString();

    public string ProcessInitial
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(ProcessName) ? "?" : ProcessName.Trim();
            return name[..1].ToUpperInvariant();
        }
    }

    public string AddressSummary
    {
        get
        {
            if (Addresses.Count == 0) return "—";
            if (Addresses.Any(IsWildcardAddress)) return L.Get("AllInterfaces");
            var first = FormatAddress(Addresses[0]);
            return Addresses.Count > 1 ? $"{first}  +{Addresses.Count - 1}" : first;
        }
    }

    public string AddressDetail => Addresses.Count == 0
        ? "—"
        : string.Join("\n", Addresses.Select(FormatAddress));

    public string ExposureLabel => Exposure switch
    {
        "all" => L.Get("ExposureAll"),
        "lan" => L.Get("ExposureLan"),
        "public" => L.Get("ExposurePublic"),
        _ => L.Get("ExposureLocal")
    };

    public string ProjectName => Project?.Name ?? "—";

    public string ProjectType => Project?.Type ?? string.Empty;

    public string ProjectPathLabel => Project?.Path ?? L.Get("NoProjectPath");

    public string ProjectGroupKey => Project?.Name ?? L.Get("UnknownProject");

    public string WorkingDirectoryLabel => string.IsNullOrWhiteSpace(WorkingDirectory) ? "—" : WorkingDirectory;

    public bool HasContainer => !string.IsNullOrWhiteSpace(Container);

    public string ContainerLabel => HasContainer ? Container : "—";

    public string ContainerSummary => HasContainer ? L.Format("ContainerSummaryFormat", Container) : string.Empty;

    public bool HasWsl => !string.IsNullOrWhiteSpace(Wsl);

    public string WslSummary => HasWsl ? L.Format("WslSummaryFormat", Wsl) : string.Empty;

    public bool HasProject => Project is not null;

    public string PidLabel => $"PID {Pid}";

    public string StartedAtLabel => StartedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";

    public string CommandPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CommandLine)) return L.Get("NoCommandLine");
            var value = CommandLine.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 92 ? value[..92] + "…" : value;
        }
    }

    public string CommandDisplay => string.IsNullOrWhiteSpace(CommandLine)
        ? L.Get("NoCommandLineDetail")
        : CommandLine;

    public string ProjectSummary => Project is null
        ? L.Get("NoProjectPath")
        : $"{Project.Name}  ·  {Project.Type}";

    public string ProtocolLabel => Protocol;

    public bool IsTcp => string.Equals(Protocol, "TCP", StringComparison.OrdinalIgnoreCase);

    public HealthState HealthState => _healthState;

    public long? HealthLatencyMs => _healthLatencyMs;

    public string? HealthError => _healthError;

    public int? HealthStatusCode => _healthStatusCode;

    public string? HealthScheme => _healthScheme;

    public bool HasHealthResult => _healthState != HealthState.Unknown;

    public string HealthTone => _healthState switch
    {
        HealthState.Healthy => "healthy",
        HealthState.Unhealthy => "unhealthy",
        HealthState.Unsupported => "unsupported",
        _ => "unknown"
    };

    public string HealthLabel => _healthState switch
    {
        HealthState.Checking => L.Get("HealthChecking"),
        HealthState.Healthy when _healthStatusCode is not null =>
            L.Format("HealthHttpFormat", _healthStatusCode, _healthLatencyMs ?? 0),
        HealthState.Healthy => L.Format("HealthHealthyFormat", _healthLatencyMs ?? 0),
        HealthState.Unhealthy when _healthStatusCode is not null =>
            L.Format("HealthHttpFormat", _healthStatusCode, _healthLatencyMs ?? 0),
        HealthState.Unhealthy => L.Get("HealthUnhealthy"),
        HealthState.Unsupported => L.Get("HealthUnsupported"),
        _ => L.Get("HealthUnknown")
    };

    public string HealthDetail => _healthState switch
    {
        HealthState.Healthy when _healthStatusCode is not null =>
            L.Format("HealthHttpDetailFormat", _healthStatusCode, _healthLatencyMs ?? 0),
        HealthState.Healthy => L.Format("HealthHealthyDetail", _healthLatencyMs ?? 0),
        HealthState.Unhealthy when _healthStatusCode is not null =>
            L.Format("HealthHttpDetailFormat", _healthStatusCode, _healthLatencyMs ?? 0),
        HealthState.Unhealthy => L.Format(
            "HealthUnhealthyDetail",
            string.Equals(_healthError, "timeout", StringComparison.OrdinalIgnoreCase)
                ? L.Get("HealthTimeout")
                : _healthError ?? L.Get("HealthTimeout")),
        HealthState.Unsupported => L.Get("HealthUnsupported"),
        _ => L.Get("HealthUnknown")
    };

    public void SetHealth(
        HealthState state,
        long? latencyMs = null,
        string? error = null,
        int? statusCode = null,
        string? scheme = null)
    {
        _healthState = state;
        _healthLatencyMs = latencyMs;
        _healthError = error;
        _healthStatusCode = statusCode;
        _healthScheme = scheme;
        OnPropertyChanged(nameof(HealthState));
        OnPropertyChanged(nameof(HasHealthResult));
        OnPropertyChanged(nameof(HealthTone));
        OnPropertyChanged(nameof(HealthLabel));
        OnPropertyChanged(nameof(HealthDetail));
    }

    private static bool IsWildcardAddress(string address) =>
        address is "0.0.0.0" or "::" or "*" or "";

    private static string FormatAddress(string address) => address switch
    {
        "0.0.0.0" => "0.0.0.0",
        "::" => "[::]",
        "::1" => "[::1]",
        _ when address.Contains(':') => $"[{address}]",
        _ => address
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ScanStats
{
    public int Ports { get; init; }
    public int Processes { get; init; }
    public int Favorites { get; init; }
    public int Conflicts { get; init; }
    public int DevPorts { get; init; }
}

public sealed class ScanResult
{
    public bool Ok { get; init; }
    public List<PortEntry> Ports { get; init; } = new();
    public ScanStats Stats { get; init; } = new();
    public string? Error { get; init; }
    public string? Warning { get; init; }
    public string Source { get; init; } = "Windows IP Helper API";
    public long DurationMs { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
}
