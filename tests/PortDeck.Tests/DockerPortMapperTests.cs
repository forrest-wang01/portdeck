using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class DockerPortMapperTests
{
    [Fact]
    public void Parses_published_tcp_and_udp_ports()
    {
        const string output =
            "web\tnginx:latest\t0.0.0.0:8080->80/tcp, [::]:8080->80/tcp\n" +
            "game\tminecraft:latest\t0.0.0.0:25565->25565/tcp, 0.0.0.0:19132->19132/udp\n";

        var mappings = DockerPortMapper.Parse(output);

        Assert.Equal("web (nginx:latest)", mappings["8080/tcp"]);
        Assert.Equal("game (minecraft:latest)", mappings["25565/tcp"]);
        Assert.Equal("game (minecraft:latest)", mappings["19132/udp"]);
    }

    [Fact]
    public void Ignores_unpublished_ports()
    {
        var mappings = DockerPortMapper.Parse("worker\tbusybox\t80/tcp\n");

        Assert.Empty(mappings);
    }
}
