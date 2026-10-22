using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class WslPortMapperTests
{
    [Fact]
    public void Parses_tcp_and_udp_listeners()
    {
        const string output =
            "LISTEN 0 511 127.0.0.1:3000 0.0.0.0:* users:((\"node\",pid=42,fd=20))\n" +
            "UNCONN 0 0 0.0.0.0:5353 0.0.0.0:* users:((\"avahi-daemon\",pid=7,fd=12))\n";

        var mappings = WslPortMapper.Parse("Ubuntu", output);

        Assert.Equal("WSL · Ubuntu · node", mappings["3000/tcp"]);
        Assert.Equal("WSL · Ubuntu · avahi-daemon", mappings["5353/udp"]);
    }

    [Fact]
    public void Ignores_lines_without_process_owners()
    {
        var mappings = WslPortMapper.Parse("Debian", "LISTEN 0 128 0.0.0.0:22 0.0.0.0:*\n");

        Assert.Empty(mappings);
    }
}
