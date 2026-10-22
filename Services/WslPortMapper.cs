using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PortDeck.Services;

/// <summary>
/// Best-effort mapping between Windows localhost ports and listeners inside
/// WSL distros. WSL2 forwards localhost ports, so the guest port usually
/// matches the Windows port.
/// </summary>
public static class WslPortMapper
{
    private static readonly object CacheLock = new();
    private static Dictionary<string, string> _cache = new();
    private static DateTime _cacheAt = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly Regex ListenerPattern = new(
        @":(?<port>\d+)\s+.*?users:\(\(""(?<process>[^""]+)""",
        RegexOptions.Compiled);

    public static Dictionary<string, string> GetMappings()
    {
        lock (CacheLock)
        {
            if (DateTime.UtcNow - _cacheAt < CacheTtl) return new Dictionary<string, string>(_cache);
        }

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (!File.Exists(wsl))
        {
            Store(mappings);
            return mappings;
        }

        try
        {
            var distros = Run(wsl, new[] { "-l", "-q" }, UnicodeOutput: true, timeoutMs: 2500)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim('\r', ' ', '\0'))
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var distro in distros)
            {
                var output = Run(
                    wsl,
                    new[] { "-d", distro, "--", "sh", "-lc", "ss -ltnp 2>/dev/null; ss -lunp 2>/dev/null" },
                    UnicodeOutput: false,
                    timeoutMs: 4000);

                foreach (var pair in Parse(distro, output))
                    mappings[pair.Key] = pair.Value;
            }
        }
        catch
        {
            // WSL may not be installed or may have no running distro.
        }

        Store(mappings);
        return new Dictionary<string, string>(mappings);
    }

    public static Dictionary<string, string> Parse(string distro, string output)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ListenerPattern.Match(line);
            if (!match.Success) continue;

            var protocol = line.TrimStart().StartsWith("UNCONN", StringComparison.OrdinalIgnoreCase)
                ? "udp"
                : "tcp";
            var key = $"{match.Groups["port"].Value}/{protocol}";
            mappings[key] = $"WSL · {distro} · {match.Groups["process"].Value}";
        }

        return mappings;
    }

    private static string Run(string executable, string[] arguments, bool UnicodeOutput, int timeoutMs)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = UnicodeOutput ? Encoding.Unicode : Encoding.UTF8
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null) return string.Empty;

        var outputTask = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return string.Empty;
        }

        return outputTask.GetAwaiter().GetResult();
    }

    private static void Store(Dictionary<string, string> mappings)
    {
        lock (CacheLock)
        {
            _cache = mappings;
            _cacheAt = DateTime.UtcNow;
        }
    }
}
