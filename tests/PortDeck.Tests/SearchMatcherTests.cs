using PortDeck.Models;
using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class SearchMatcherTests
{
    private static PortEntry Entry() => new()
    {
        Port = 3000,
        Pid = 1234,
        ProcessName = "node",
        ProcessFile = "node.exe",
        Project = new ProjectInfo
        {
            Name = "demo-app",
            Path = @"C:\src\demo-app",
            Type = "Node"
        },
        WorkingDirectory = @"C:\src\demo-app",
        Addresses = new List<string> { "127.0.0.1" },
        CommandLine = "node server.js 3000",
        Exposure = "local",
        IsDev = true
    };

    [Theory]
    [InlineData("node")]
    [InlineData("port:3000")]
    [InlineData("p:3000")]
    [InlineData("pid:1234")]
    [InlineData("project:demo")]
    [InlineData("process:node")]
    [InlineData("cwd:demo-app")]
    [InlineData("addr:127.0.0.1")]
    [InlineData("port:3000 process:node")]
    public void Matches_known_fields(string query) =>
        Assert.True(SearchMatcher.Matches(Entry(), query));

    [Theory]
    [InlineData("port:4000")]
    [InlineData("pid:9999")]
    [InlineData("project:other")]
    [InlineData("process:python")]
    [InlineData("cwd:elsewhere")]
    [InlineData("addr:10.0.0.1")]
    [InlineData("missing")]
    public void Rejects_non_matching_fields(string query) =>
        Assert.False(SearchMatcher.Matches(Entry(), query));

    [Fact]
    public void Unknown_key_falls_back_to_plain_text() =>
        Assert.True(SearchMatcher.Matches(Entry(), "demo-app"));
}
