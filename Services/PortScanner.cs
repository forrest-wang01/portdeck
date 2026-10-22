using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using PortDeck.Localization;
using PortDeck.Models;

namespace PortDeck.Services;

public sealed class PortScanner
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const int UdpTableOwnerPid = 1;
    private const uint ErrorInsufficientBuffer = 122;

    private static readonly HashSet<int> DevPorts = new()
    {
        3000, 3001, 3002, 4000, 4200, 4321, 5000, 5001, 5173, 5174, 5175,
        5500, 6006, 7000, 7860, 8000, 8001, 8080, 8081, 8082, 8443, 8888,
        9000, 9229, 11434, 12345, 19006, 27017, 5432, 6379
    };

    private static readonly Regex RuntimeRegex = new(
        "^(node|bun|deno|python|pythonw|py|dotnet|java|javaw|go|cargo|ruby|php|perl)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "System Idle Process", "smss", "csrss", "wininit",
        "services", "lsass", "svchost", "spoolsv", "dwm", "fontdrvhost",
        "winlogon", "sihost", "taskhostw", "ctfmon", "explorer", "SearchIndexer",
        "SearchHost", "RuntimeBroker", "SecurityHealthService", "MsMpEng",
        "Registry", "Memory Compression"
    };

    private static readonly (string File, string Type, int Score)[] ProjectMarkers =
    {
        (".git", "Git", 100),
        ("pnpm-lock.yaml", "Node", 82),
        ("yarn.lock", "Node", 82),
        ("package-lock.json", "Node", 80),
        ("pyproject.toml", "Python", 74),
        ("go.mod", "Go", 74),
        ("Cargo.toml", "Rust", 74),
        ("pom.xml", "Java", 74),
        ("build.gradle", "Java", 72),
        ("build.gradle.kts", "Java", 72),
        ("composer.json", "PHP", 72),
        ("Gemfile", "Ruby", 72),
        ("package.json", "Node", 56),
        ("requirements.txt", "Python", 42),
        ("setup.py", "Python", 40)
    };

    private static readonly HashSet<string> RuntimeExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "node.exe", "bun.exe", "deno.exe", "python.exe", "pythonw.exe", "py.exe",
        "dotnet.exe", "java.exe", "javaw.exe", "go.exe", "cargo.exe", "ruby.exe",
        "php.exe", "perl.exe", "Rscript.exe"
    };

    private readonly Dictionary<string, (DateTime At, ProjectInfo? Project)> _projectCache = new();
    private readonly Dictionary<int, (DateTime At, string? Path)> _workingDirectoryCache = new();
    private readonly object _cacheLock = new();

    public Task<ScanResult> ScanAsync(IReadOnlySet<int> favorites, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(favorites, cancellationToken), cancellationToken);

    private ScanResult Scan(IReadOnlySet<int> favorites, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (listeners, warning) = CollectListeners();
            cancellationToken.ThrowIfCancellationRequested();

            var processes = ReadProcesses();
            var dockerMappings = DockerPortMapper.GetMappings();
            var wslMappings = WslPortMapper.GetMappings();
            var entries = BuildEntries(
                listeners,
                processes,
                dockerMappings,
                wslMappings,
                favorites,
                cancellationToken);
            ApplyConflicts(entries);

            return new ScanResult
            {
                Ok = true,
                Ports = entries,
                Stats = BuildStats(entries),
                Warning = warning,
                DurationMs = stopwatch.ElapsedMilliseconds,
                GeneratedAt = DateTime.Now,
                Source = "Windows IP Helper API"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ScanResult
            {
                Ok = false,
                Error = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                GeneratedAt = DateTime.Now,
                Source = "Windows IP Helper API"
            };
        }
    }

    private List<PortEntry> BuildEntries(
        List<RawListener> listeners,
        Dictionary<int, ProcessMeta> processes,
        Dictionary<string, string> dockerMappings,
        Dictionary<string, string> wslMappings,
        IReadOnlySet<int> favorites,
        CancellationToken cancellationToken)
    {
        var entries = new List<PortEntry>();

        foreach (var group in listeners
                     .GroupBy(l => new { l.Pid, l.Port, l.Protocol })
                     .OrderBy(g => g.Key.Port)
                     .ThenBy(g => g.Key.Protocol)
                     .ThenBy(g => g.Key.Pid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            processes.TryGetValue(group.Key.Pid, out var meta);
            meta ??= ReadProcessFallback(group.Key.Pid);

            var processName = CleanProcessName(meta?.Name);
            var commandLine = meta?.CommandLine ?? string.Empty;
            var exePath = meta?.ExePath ?? string.Empty;
            var workingDirectory = GetWorkingDirectory(group.Key.Pid);
            var project = InferProject(exePath, commandLine, workingDirectory);
            var addresses = group
                .Select(l => l.Address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a)
                .ToList();

            var entry = new PortEntry
            {
                Port = group.Key.Port,
                Pid = group.Key.Pid,
                Protocol = group.Key.Protocol,
                Addresses = addresses,
                ProcessName = processName,
                ProcessFile = meta?.Name ?? string.Empty,
                ExePath = exePath,
                CommandLine = commandLine,
                WorkingDirectory = workingDirectory ?? string.Empty,
                Container = dockerMappings.TryGetValue(
                    $"{group.Key.Port}/{group.Key.Protocol.ToLowerInvariant()}",
                    out var container)
                    ? container
                    : string.Empty,
                Wsl = wslMappings.TryGetValue(
                    $"{group.Key.Port}/{group.Key.Protocol.ToLowerInvariant()}",
                    out var wsl)
                    ? wsl
                    : string.Empty,
                StartedAt = meta?.CreatedAt,
                Project = project,
                Exposure = ClassifyExposure(addresses),
                IsSystem = group.Key.Pid == 4 ||
                           SystemProcesses.Contains(processName) ||
                           SystemProcesses.Contains(Path.GetFileNameWithoutExtension(meta?.Name ?? string.Empty)),
                IsDev = DevPorts.Contains(group.Key.Port) ||
                        RuntimeRegex.IsMatch(processName) ||
                        project is not null
            };

            entry.IsFavorite = favorites.Contains(entry.Port);
            entries.Add(entry);
        }

        return entries
            .OrderBy(e => e.Port)
            .ThenBy(e => e.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyConflicts(List<PortEntry> entries)
    {
        var pidsByListener = entries
            .GroupBy(e => new { e.Port, e.Protocol })
            .ToDictionary(g => g.Key, g => g.Select(e => e.Pid).Distinct().Count());

        foreach (var entry in entries)
            entry.Conflict = pidsByListener[new { entry.Port, entry.Protocol }] > 1;
    }

    private static ScanStats BuildStats(List<PortEntry> entries) => new()
    {
        Ports = entries.Count,
        Processes = entries.Select(e => e.Pid).Distinct().Count(),
        Favorites = entries.Count(e => e.IsFavorite),
        Conflicts = entries.Where(e => e.Conflict).Select(e => e.Port).Distinct().Count(),
        DevPorts = entries.Count(e => e.IsDev)
    };

    private static (List<RawListener> Listeners, string? Warning) CollectListeners()
    {
        var listeners = new List<RawListener>();
        var errors = new List<string>();

        void Collect(string label, Func<List<RawListener>> read)
        {
            try
            {
                listeners.AddRange(read());
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: {ex.Message}");
            }
        }

        Collect("TCP IPv4", () => ReadTcpTable(AfInet, 24));
        Collect("TCP IPv6", () => ReadTcpTable(AfInet6, 56));
        Collect("UDP IPv4", () => ReadUdpTable(AfInet, 12));
        Collect("UDP IPv6", () => ReadUdpTable(AfInet6, 28));

        if (listeners.Count == 0 && errors.Count == 4)
            throw new InvalidOperationException(
                L.Get("ScanErrorTcpTable") + " " + string.Join("; ", errors));

        return (listeners, errors.Count > 0
            ? L.Format("ScanErrorAddressFamilyFormat", string.Join("; ", errors))
            : null);
    }

    private static List<RawListener> ReadTcpTable(int addressFamily, int rowSize)
    {
        var size = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);

        if (result != 0 && result != ErrorInsufficientBuffer)
            throw new InvalidOperationException(L.Format("TcpTableErrorFormat", result));

        if (size <= 0) return new List<RawListener>();

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(
                buffer, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);

            if (result != 0)
                throw new InvalidOperationException(L.Format("TcpTableErrorFormat", result));

            var count = Marshal.ReadInt32(buffer);
            var listeners = new List<RawListener>(count);

            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buffer, 4 + i * rowSize);
                if (addressFamily == AfInet)
                {
                    var localAddress = (uint)Marshal.ReadInt32(row, 4);
                    var localPort = (uint)Marshal.ReadInt32(row, 8);
                    var pid = Marshal.ReadInt32(row, 20);
                    var addressBytes = BitConverter.GetBytes(localAddress);
                    listeners.Add(new RawListener(
                        new IPAddress(addressBytes),
                        NetworkToHostPort(localPort),
                        pid,
                        "TCP"));
                }
                else
                {
                    var addressBytes = new byte[16];
                    Marshal.Copy(row, addressBytes, 0, 16);
                    var scopeId = (uint)Marshal.ReadInt32(row, 16);
                    var localPort = (uint)Marshal.ReadInt32(row, 20);
                    var pid = Marshal.ReadInt32(row, 52);
                    listeners.Add(new RawListener(
                        new IPAddress(addressBytes, scopeId),
                        NetworkToHostPort(localPort),
                        pid,
                        "TCP"));
                }
            }

            return listeners;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static List<RawListener> ReadUdpTable(int addressFamily, int rowSize)
    {
        var size = 0;
        var result = GetExtendedUdpTable(
            IntPtr.Zero, ref size, false, addressFamily, UdpTableOwnerPid, 0);

        if (result != 0 && result != ErrorInsufficientBuffer)
            throw new InvalidOperationException($"GetExtendedUdpTable failed ({result}).");

        if (size <= 0) return new List<RawListener>();

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedUdpTable(
                buffer, ref size, false, addressFamily, UdpTableOwnerPid, 0);

            if (result != 0)
                throw new InvalidOperationException($"GetExtendedUdpTable failed ({result}).");

            var count = Marshal.ReadInt32(buffer);
            var listeners = new List<RawListener>(count);

            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buffer, 4 + i * rowSize);
                if (addressFamily == AfInet)
                {
                    var localAddress = (uint)Marshal.ReadInt32(row, 0);
                    var localPort = (uint)Marshal.ReadInt32(row, 4);
                    var pid = Marshal.ReadInt32(row, 8);
                    listeners.Add(new RawListener(
                        new IPAddress(BitConverter.GetBytes(localAddress)),
                        NetworkToHostPort(localPort),
                        pid,
                        "UDP"));
                }
                else
                {
                    var addressBytes = new byte[16];
                    Marshal.Copy(row, addressBytes, 0, 16);
                    var scopeId = (uint)Marshal.ReadInt32(row, 16);
                    var localPort = (uint)Marshal.ReadInt32(row, 20);
                    var pid = Marshal.ReadInt32(row, 24);
                    listeners.Add(new RawListener(
                        new IPAddress(addressBytes, scopeId),
                        NetworkToHostPort(localPort),
                        pid,
                        "UDP"));
                }
            }

            return listeners;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int NetworkToHostPort(uint raw)
    {
        var value = (ushort)(raw & 0xFFFF);
        return (value >> 8) | ((value & 0xFF) << 8);
    }

    private static Dictionary<int, ProcessMeta> ReadProcesses()
    {
        var processes = new Dictionary<int, ProcessMeta>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, ExecutablePath, CommandLine, CreationDate FROM Win32_Process");
            using var results = searcher.Get();

            foreach (ManagementObject item in results)
            {
                try
                {
                    var pid = Convert.ToInt32(item["ProcessId"]);
                    var name = item["Name"]?.ToString() ?? string.Empty;
                    var exePath = TryGetString(item, "ExecutablePath");
                    var commandLine = TryGetString(item, "CommandLine");
                    DateTime? createdAt = null;

                    var rawDate = item["CreationDate"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(rawDate))
                    {
                        try
                        {
                            createdAt = ManagementDateTimeConverter.ToDateTime(rawDate);
                        }
                        catch
                        {
                            // Some protected processes expose a non-standard date string.
                        }
                    }

                    processes[pid] = new ProcessMeta(name, exePath, commandLine, createdAt);
                }
                catch
                {
                    // Individual WMI rows can fail for protected processes; ignore them.
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch
        {
            // WMI can be disabled or unavailable. Fall back to System.Diagnostics.
        }

        return processes;
    }

    private static string TryGetString(ManagementBaseObject item, string property)
    {
        try
        {
            return item[property]?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ProcessMeta? ReadProcessFallback(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName + ".exe";
            var exePath = string.Empty;
            DateTime? startedAt = null;

            try { exePath = process.MainModule?.FileName ?? string.Empty; } catch { }
            try { startedAt = process.StartTime; } catch { }

            return new ProcessMeta(name, exePath, string.Empty, startedAt);
        }
        catch
        {
            return null;
        }
    }

    private string? GetWorkingDirectory(int pid)
    {
        lock (_cacheLock)
        {
            if (_workingDirectoryCache.TryGetValue(pid, out var cached) &&
                DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(30))
                return cached.Path;
        }

        var path = ProcessWorkingDirectory.TryGet(pid);

        lock (_cacheLock)
        {
            if (_workingDirectoryCache.Count > 500)
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(30);
                foreach (var key in _workingDirectoryCache.Where(p => p.Value.At < cutoff).Select(p => p.Key).ToList())
                    _workingDirectoryCache.Remove(key);
            }

            _workingDirectoryCache[pid] = (DateTime.UtcNow, path);
        }

        return path;
    }

    private ProjectInfo? InferProject(string exePath, string commandLine, string? workingDirectory)
    {
        var cacheKey = exePath + "\0" + commandLine + "\0" + workingDirectory;
        lock (_cacheLock)
        {
            if (_projectCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(1))
                return cached.Project;
        }

        var candidates = ExtractCommandPaths(commandLine, workingDirectory);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            candidates.Insert(0, workingDirectory);

        var exeName = Path.GetFileName(exePath);
        if (!string.IsNullOrWhiteSpace(exePath) && RuntimeExes.Contains(exeName))
            candidates.Add(exePath);

        ProjectInfo? project = null;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = WalkProject(candidate);
            if (found is null) continue;

            project = found;
            if (!string.Equals(found.Type, "Web", StringComparison.Ordinal))
                break;
        }

        lock (_cacheLock)
        {
            if (_projectCache.Count > 800)
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(2);
                foreach (var key in _projectCache.Where(p => p.Value.At < cutoff).Select(p => p.Key).ToList())
                    _projectCache.Remove(key);
            }

            _projectCache[cacheKey] = (DateTime.UtcNow, project);
        }

        return project;
    }

    private static List<string> ExtractCommandPaths(string commandLine, string? workingDirectory)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return paths;

        void AddCandidate(string raw)
        {
            var value = CleanPath(raw);
            if (value.Length == 0 || value.StartsWith('-')) return;

            try
            {
                if (Path.IsPathRooted(value))
                {
                    paths.Add(value);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(workingDirectory) && LooksLikePath(value))
                    paths.Add(Path.Combine(workingDirectory, value));
            }
            catch
            {
                // Ignore malformed command-line fragments.
            }
        }

        foreach (Match match in Regex.Matches(commandLine, "\"([^\"]+)\""))
            AddCandidate(match.Groups[1].Value);

        foreach (Match match in Regex.Matches(commandLine, @"[A-Za-z]:\\[^\s""]+"))
            AddCandidate(match.Value);

        foreach (var token in commandLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('-')) continue;
            if (token.Contains('\\') || token.Contains('/'))
                AddCandidate(token);
        }

        var cwdMatch = Regex.Match(
            commandLine,
            @"(?:^|\s)(?:--cwd|--prefix|--directory|--workdir|-C|-w)\s+(?:""([^""]+)""|'([^']+)'|([^\s]+))",
            RegexOptions.IgnoreCase);
        if (cwdMatch.Success)
        {
            var value = CleanPath(cwdMatch.Groups[1].Value.Length > 0
                ? cwdMatch.Groups[1].Value
                : cwdMatch.Groups[2].Value.Length > 0
                    ? cwdMatch.Groups[2].Value
                    : cwdMatch.Groups[3].Value);
            if (value.Length > 0) paths.Insert(0, value);
        }

        return paths;
    }

    private static bool LooksLikePath(string value)
    {
        if (value.Contains('\\') || value.Contains('/')) return true;
        return Regex.IsMatch(
            value,
            @"\.(js|mjs|cjs|ts|tsx|jsx|py|go|rb|php|jar|dll|exe|json|toml|csproj|sln)$",
            RegexOptions.IgnoreCase);
    }

    private static string CleanPath(string value) =>
        value.Trim()
            .Trim('"', '\'')
            .TrimEnd(')', ']', ',', ';')
            .Trim();

    private static ProjectInfo? WalkProject(string startPath)
    {
        string? current;
        try
        {
            if (Directory.Exists(startPath)) current = startPath;
            else if (File.Exists(startPath)) current = Path.GetDirectoryName(startPath);
            else return null;
        }
        catch
        {
            return null;
        }

        var found = new List<(string Path, string Type, int Score)>();

        for (var depth = 0; depth < 10 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            if (IsIgnoredProjectRoot(current)) break;

            var inNodeModules = current.Contains(
                Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

            (string Path, string Type, int Score)? best = null;
            foreach (var marker in ProjectMarkers)
            {
                var markerPath = Path.Combine(current, marker.File);
                if (!File.Exists(markerPath) && !Directory.Exists(markerPath)) continue;

                var score = marker.File == "package.json" && inNodeModules ? 8 : marker.Score;
                if (best is null || score > best.Value.Score)
                    best = (current, marker.Type, score);
            }

            if (best is not null) found.Add(best.Value);

            try
            {
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }
            catch
            {
                break;
            }
        }

        if (found.Count == 0) return null;

        var winner = found
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.Path.Length)
            .First();

        return new ProjectInfo
        {
            Path = winner.Path,
            Name = GetProjectName(winner.Path, winner.Type),
            Type = winner.Type
        };
    }

    private static string GetProjectName(string projectPath, string projectType)
    {
        var fallback = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.Equals(projectType, "Node", StringComparison.Ordinal)) return fallback;

        try
        {
            var packagePath = Path.Combine(projectPath, "package.json");
            if (!File.Exists(packagePath)) return fallback;

            using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
            if (document.RootElement.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                var value = name.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch
        {
            // A malformed package.json should not hide the folder name.
        }

        return fallback;
    }

    private static bool IsIgnoredProjectRoot(string path)
    {
        try
        {
            var normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(normalized)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) return true;

            var ignoredFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };

            foreach (var folder in ignoredFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                var normalizedFolder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalized, normalizedFolder, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (normalized.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    (normalizedFolder.EndsWith("Windows", StringComparison.OrdinalIgnoreCase) ||
                     normalizedFolder.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
                     normalizedFolder.EndsWith("ProgramData", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            var ignoredSegments = new[]
            {
                ".dsh", ".vscode", ".cursor", ".cache", ".config", ".local", "AppData",
                "node_modules", ".pnpm", "pnpm", "npm", "yarn", "dependencies", "app.asar"
            };
            var segments = normalized.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => ignoredSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
                return true;

            var name = Path.GetFileName(normalized);
            if (name.Equals("Users", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AppData", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Local", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Roaming", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string ClassifyExposure(IReadOnlyCollection<string> addresses)
    {
        if (addresses.Count == 0) return "local";
        if (addresses.Any(a => a is "0.0.0.0" or "::" or "*" or "")) return "all";

        var parsed = new List<IPAddress>();
        foreach (var address in addresses)
        {
            if (IPAddress.TryParse(address, out var ip)) parsed.Add(ip);
        }

        if (parsed.Count == 0) return "local";
        var nonLoopback = parsed.Where(ip => !IPAddress.IsLoopback(ip)).ToList();
        if (nonLoopback.Count == 0) return "local";
        if (nonLoopback.Any(IsPrivate)) return "lan";
        return "public";
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
               (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);
    }

    private static string CleanProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return L.Get("UnknownProcess");
        var value = Path.GetFileNameWithoutExtension(name.Trim());
        return string.IsNullOrWhiteSpace(value) ? name.Trim() : value;
    }

    private sealed record RawListener(IPAddress Address, int Port, int Pid, string Protocol);

    private sealed record ProcessMeta(string Name, string ExePath, string CommandLine, DateTime? CreatedAt);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);
}
