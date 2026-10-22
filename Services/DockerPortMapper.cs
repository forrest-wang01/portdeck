using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PortDeck.Services;

public static class DockerPortMapper
{
    private static readonly object CacheLock = new();
    private static Dictionary<string, string> _cache = new();
    private static DateTime _cacheAt = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly Regex PortPattern = new(
        @":(\d+)->\d+/(tcp|udp)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Dictionary<string, string> GetMappings()
    {
        lock (CacheLock)
        {
            if (DateTime.UtcNow - _cacheAt < CacheTtl) return new Dictionary<string, string>(_cache);
        }

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("ps");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{.Names}}\t{{.Image}}\t{{.Ports}}");

            using var process = Process.Start(startInfo);
            if (process is null) return mappings;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(2500))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                mappings = new Dictionary<string, string>();
            }
            else
            {
                mappings = Parse(outputTask.GetAwaiter().GetResult());
            }

            _ = errorTask;
        }
        catch
        {
            // Docker may not be installed, or the daemon may not be running.
        }

        lock (CacheLock)
        {
            _cache = mappings;
            _cacheAt = DateTime.UtcNow;
        }

        return new Dictionary<string, string>(mappings);
    }

    public static Dictionary<string, string> Parse(string dockerOutput)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in dockerOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Trim().Split('\t');
            if (columns.Length < 3) continue;

            var name = columns[0].Trim();
            var image = columns[1].Trim();
            foreach (var mapping in columns[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = PortPattern.Match(mapping);
                if (!match.Success) continue;

                var key = $"{match.Groups[1].Value}/{match.Groups[2].Value.ToLowerInvariant()}";
                mappings[key] = $"{name} ({image})";
            }
        }

        return mappings;
    }
}
