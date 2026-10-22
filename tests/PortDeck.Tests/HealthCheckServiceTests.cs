using System.Net;
using System.Net.Sockets;
using PortDeck.Models;
using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class HealthCheckServiceTests
{
    [Fact]
    public async Task Detects_a_listening_tcp_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var entry = new PortEntry
            {
                Port = port,
                Protocol = "TCP",
                Addresses = new List<string> { "127.0.0.1" }
            };

            var result = await HealthCheckService.CheckAsync(entry);

            Assert.True(result.Supported);
            Assert.True(result.Healthy);
            Assert.NotNull(result.LatencyMs);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reads_http_status_for_a_node_project()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var responseTask = Task.Run(async () =>
            {
                // The health check first opens a plain TCP probe, then a second
                // HTTP request when the entry looks like an HTTP service.
                using (var probe = await listener.AcceptTcpClientAsync())
                {
                    probe.Close();
                }

                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var requestBuffer = new byte[2048];
                await stream.ReadAtLeastAsync(requestBuffer, 1, throwOnEndOfStream: false);
                var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK";
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response));
            });

            var entry = new PortEntry
            {
                Port = port,
                Protocol = "TCP",
                Addresses = new List<string> { "127.0.0.1" },
                Project = new ProjectInfo { Name = "http-test", Type = "Node" }
            };

            var result = await HealthCheckService.CheckAsync(entry);
            await responseTask;

            Assert.True(result.Healthy);
            Assert.True(result.StatusCode == 200, result.Error);
            Assert.Equal("http", result.Scheme);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Reports_udp_as_unsupported()
    {
        var entry = new PortEntry
        {
            Port = 5000,
            Protocol = "UDP",
            Addresses = new List<string> { "127.0.0.1" }
        };

        var result = await HealthCheckService.CheckAsync(entry);

        Assert.False(result.Supported);
        Assert.False(result.Healthy);
    }
}
