using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using PortDeck.Models;

namespace PortDeck.Services;

public sealed record HealthCheckResult(
    bool Supported,
    bool Healthy,
    long? LatencyMs,
    string? Error,
    int? StatusCode = null,
    string? Scheme = null);

public static class HealthCheckService
{
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromMilliseconds(2200);
    private static readonly HashSet<int> HttpPorts = new()
    {
        80, 443, 3000, 3001, 3002, 4000, 4200, 4321, 5000, 5001,
        5173, 5174, 5175, 5500, 8000, 8080, 8081, 8088, 8443,
        8888, 9000, 9090, 19006
    };

    public static async Task<HealthCheckResult> CheckAsync(
        PortEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!entry.IsTcp)
            return new HealthCheckResult(false, false, null, "udp");

        var host = ResolveHost(entry);
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TcpTimeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, entry.Port, timeout.Token);
            stopwatch.Stop();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(true, false, null, "timeout");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(true, false, null, ex.Message);
        }

        var tcpLatency = stopwatch.ElapsedMilliseconds;
        if (!ShouldCheckHttp(entry))
            return new HealthCheckResult(true, true, tcpLatency, null);

        var scheme = entry.Port is 443 or 8443 ? "https" : "http";
        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var http = new HttpClient(handler) { Timeout = HttpTimeout };
            var httpStopwatch = Stopwatch.StartNew();
            using var response = await http.GetAsync(
                $"{scheme}://{host}:{entry.Port}/",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            httpStopwatch.Stop();

            var statusCode = (int)response.StatusCode;
            return new HealthCheckResult(
                true,
                statusCode < 500,
                httpStopwatch.ElapsedMilliseconds,
                null,
                statusCode,
                scheme);
        }
        catch (Exception ex)
        {
            // A TCP service that does not speak HTTP is still healthy.
            return new HealthCheckResult(true, true, tcpLatency, ex.Message);
        }
    }

    private static bool ShouldCheckHttp(PortEntry entry) =>
        HttpPorts.Contains(entry.Port) ||
        entry.Project?.Type is "Node" or "Web" ||
        entry.CommandLine.Contains("vite", StringComparison.OrdinalIgnoreCase) ||
        entry.CommandLine.Contains("next", StringComparison.OrdinalIgnoreCase) ||
        entry.CommandLine.Contains("http", StringComparison.OrdinalIgnoreCase);

    private static string ResolveHost(PortEntry entry)
    {
        if (entry.Addresses.Count == 0) return "127.0.0.1";

        if (entry.Addresses.Any(address => address is "127.0.0.1" or "0.0.0.0"))
            return "127.0.0.1";

        if (entry.Addresses.Any(address => address is "::" or "::1"))
            return "::1";

        var address = entry.Addresses[0].Trim('[', ']');
        return IPAddress.TryParse(address, out var parsed) ? parsed.ToString() : "127.0.0.1";
    }
}
