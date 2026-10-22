using System.IO;
using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class ProcessWorkingDirectoryTests
{
    [Fact]
    public void Reads_own_working_directory()
    {
        var directory = ProcessWorkingDirectory.TryGet(Environment.ProcessId);

        Assert.False(string.IsNullOrWhiteSpace(directory));
        Assert.True(Directory.Exists(directory));
    }
}
