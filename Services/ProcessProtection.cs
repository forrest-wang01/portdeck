using System.IO;

namespace PortDeck.Services;

public static class ProcessProtection
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "System Idle Process", "smss", "csrss", "wininit",
        "services", "lsass", "svchost", "winlogon", "dwm", "fontdrvhost",
        "Registry", "Memory Compression"
    };

    public static bool IsProtected(int pid, string processName) =>
        pid <= 4 || ProtectedNames.Contains(processName) ||
        ProtectedNames.Contains(Path.GetFileNameWithoutExtension(processName));
}
