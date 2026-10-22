using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PortDeck.Services;

public sealed record UpdateCheckResult(
    bool Configured,
    bool UpdateAvailable,
    Version? LatestVersion,
    string? ReleaseUrl,
    string? Error,
    string? AssetUrl = null,
    string? ChecksumUrl = null);

public static class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public static async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        var repository = GetRepository();
        if (string.IsNullOrWhiteSpace(repository) ||
            repository.Contains("your-name", StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult(false, false, null, null, "repository-not-configured");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("PortDeck-UpdateCheck");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(true, false, null, null, $"HTTP {(int)response.StatusCode}");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;
            var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (!TryParseVersion(tag, out var latest))
                return new UpdateCheckResult(true, false, null, releaseUrl, "invalid-tag");

            var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "win-arm64"
                : "win-x64";
            var assetUrl = FindAsset(root, $"PortDeck-{latest}-{architecture}.exe");
            var checksumUrl = FindAsset(root, $"PortDeck-{latest}-{architecture}.exe.sha256");

            return new UpdateCheckResult(
                true,
                latest > currentVersion,
                latest,
                releaseUrl,
                null,
                assetUrl,
                checksumUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(true, false, null, null, "timeout");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(true, false, null, null, ex.Message);
        }
    }

    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return Version.TryParse(tag.Trim().TrimStart('v', 'V'), out version!);
    }

    public static string? FindAsset(JsonElement release, string fileName)
    {
        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) ||
                !string.Equals(nameElement.GetString(), fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            return asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
        }

        return null;
    }

    public static async Task<string> DownloadAndVerifyAsync(
        UpdateCheckResult update,
        string destination,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetUrl))
            throw new InvalidOperationException("Update asset URL is missing.");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await client.GetAsync(
            update.AssetUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(destination))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0) progress?.Report((double)received / total);
            }
        }

        if (!string.IsNullOrWhiteSpace(update.ChecksumUrl))
        {
            var checksumText = await client.GetStringAsync(update.ChecksumUrl, cancellationToken);
            var expected = checksumText
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(expected) &&
                !string.Equals(expected, await ComputeSha256Async(destination, cancellationToken),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                throw new InvalidOperationException("Downloaded update checksum does not match.");
            }
        }

        return destination;
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool LaunchUpdater(string downloadedFile, string targetFile)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"portdeck-update-{Guid.NewGuid():N}.ps1");
        var logPath = Path.Combine(Path.GetTempPath(), "portdeck-update.log");
        var script = $$"""
$ErrorActionPreference = 'Stop'
try {
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 900
    Copy-Item -LiteralPath '{{Escape(downloadedFile)}}' -Destination '{{Escape(targetFile)}}' -Force
    Start-Process -FilePath '{{Escape(targetFile)}}'
    "OK $(Get-Date -Format o)" | Add-Content -LiteralPath '{{Escape(logPath)}}'
} catch {
    $_ | Out-String | Add-Content -LiteralPath '{{Escape(logPath)}}'
}
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
""";

        File.WriteAllText(scriptPath, script);
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath
                }
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Escape(string path) => path.Replace("'", "''");

    public static string GetRepository()
    {
        var metadata = typeof(UpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => item.Key == "RepositoryUrl");
        return metadata?.Value?.Trim() ?? string.Empty;
    }
}
