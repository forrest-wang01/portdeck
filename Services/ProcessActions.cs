using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PortDeck.Localization;
using PortDeck.Models;

namespace PortDeck.Services;

public static class ProcessActions
{
    public static (bool Ok, string Message) KillProcessTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
            return (true, L.Get("ProcessKilled"));
        }
        catch (ArgumentException)
        {
            return (false, L.Get("ProcessAlreadyExited"));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return (false, L.Get("ProcessKillPermission"));
        }
        catch (Exception ex)
        {
            return (false, L.Format("KillFailedFormat", ex.Message));
        }
    }

    public static void OpenInBrowser(PortEntry entry)
    {
        var host = GetBrowserHost(entry);
        var scheme = entry.Port is 443 or 8443 ? "https" : "http";
        var url = $"{scheme}://{host}:{entry.Port}/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static string GetBrowserUrl(PortEntry entry)
    {
        var host = GetBrowserHost(entry);
        var scheme = entry.Port is 443 or 8443 ? "https" : "http";
        return $"{scheme}://{host}:{entry.Port}/";
    }

    public static void OpenTerminal(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException(workingDirectory);

        var commandPrompt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        Process.Start(new ProcessStartInfo(commandPrompt)
        {
            Arguments = $"/K cd /d \"{workingDirectory}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
    }

    public static void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }
    }

    private static string GetBrowserHost(PortEntry entry)
    {
        if (entry.Addresses.Count == 0) return "localhost";
        if (entry.Addresses.Any(a => a is "127.0.0.1" or "0.0.0.0" or "::" or "::1"))
            return "localhost";

        var address = entry.Addresses.FirstOrDefault(a => a is not ("0.0.0.0" or "::"));
        if (string.IsNullOrWhiteSpace(address)) return "localhost";
        return address.Contains(':') ? $"[{address}]" : address;
    }
}
