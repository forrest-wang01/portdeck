using System.IO;
using System.Text.Json;
using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.6.0", "0.6.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0.0", "2.0.0")]
    public void Parses_release_tags(string tag, string expected)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("release-latest")]
    public void Rejects_invalid_tags(string? tag)
    {
        Assert.False(UpdateService.TryParseVersion(tag, out _));
    }

    [Fact]
    public void Finds_the_architecture_specific_asset()
    {
        using var document = JsonDocument.Parse("""
            {
              "assets": [
                { "name": "PortDeck-1.0.0-win-x64.exe", "browser_download_url": "https://example/x64.exe" },
                { "name": "PortDeck-1.0.0-win-arm64.exe", "browser_download_url": "https://example/arm64.exe" }
              ]
            }
            """);

        Assert.Equal(
            "https://example/x64.exe",
            UpdateService.FindAsset(document.RootElement, "PortDeck-1.0.0-win-x64.exe"));
        Assert.Equal(
            "https://example/arm64.exe",
            UpdateService.FindAsset(document.RootElement, "PortDeck-1.0.0-win-arm64.exe"));
        Assert.Null(UpdateService.FindAsset(document.RootElement, "missing.exe"));
    }

    [Fact]
    public async Task Computes_sha256_for_downloaded_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"portdeck-hash-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "portdeck");
        try
        {
            var hash = await UpdateService.ComputeSha256Async(path);
            Assert.Equal("6afa4593d74dcf55abd0caba606597dd8950a2b58c3f3a0be74317475f7868fd", hash);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
