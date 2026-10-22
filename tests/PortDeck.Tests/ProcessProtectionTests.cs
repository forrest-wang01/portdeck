using PortDeck.Services;
using Xunit;

namespace PortDeck.Tests;

public sealed class ProcessProtectionTests
{
    [Theory]
    [InlineData(4, "System")]
    [InlineData(100, "svchost")]
    [InlineData(101, "lsass.exe")]
    [InlineData(102, "csrss")]
    public void Critical_processes_are_protected(int pid, string name) =>
        Assert.True(ProcessProtection.IsProtected(pid, name));

    [Theory]
    [InlineData(200, "node")]
    [InlineData(201, "python")]
    [InlineData(202, "my-app.exe")]
    public void User_processes_are_not_protected(int pid, string name) =>
        Assert.False(ProcessProtection.IsProtected(pid, name));
}
