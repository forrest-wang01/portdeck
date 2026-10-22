using System.IO;
using System.Text;

namespace PortDeck.Services;

public static class Log
{
    private static readonly object WriteLock = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PortDeck",
        "logs");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    public static void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(Directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Directory)
        {
            UseShellExecute = true
        });
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (WriteLock)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = Path.Combine(Directory, $"portdeck-{DateTime.Now:yyyy-MM-dd}.log");
                var builder = new StringBuilder()
                    .Append(DateTime.Now.ToString("O"))
                    .Append(' ')
                    .Append(level)
                    .Append(' ')
                    .Append(message);

                if (exception is not null)
                    builder.AppendLine().Append(exception);

                File.AppendAllText(path, builder.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the application.
        }
    }
}
