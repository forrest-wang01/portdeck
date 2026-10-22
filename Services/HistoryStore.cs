using System.IO;
using System.Text;
using System.Text.Json;
using PortDeck.Models;

namespace PortDeck.Services;

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _filePath;

    public HistoryStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortDeck");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "history.jsonl");
    }

    public string FilePath => _filePath;

    public List<HistoryEvent> Load(int max = 500)
    {
        try
        {
            if (!File.Exists(_filePath)) return new List<HistoryEvent>();

            var lines = File.ReadLines(_filePath).TakeLast(max);
            var events = new List<HistoryEvent>();
            foreach (var line in lines)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<HistoryEvent>(line);
                    if (item is not null) events.Add(item);
                }
                catch
                {
                    // Ignore a partially written line.
                }
            }

            events.Reverse();
            return events;
        }
        catch
        {
            return new List<HistoryEvent>();
        }
    }

    public void Append(IEnumerable<HistoryEvent> events)
    {
        var list = events.ToList();
        if (list.Count == 0) return;

        try
        {
            var builder = new StringBuilder();
            foreach (var item in list)
                builder.AppendLine(JsonSerializer.Serialize(item, JsonOptions));

            File.AppendAllText(_filePath, builder.ToString(), Encoding.UTF8);
            TrimIfNeeded();
        }
        catch
        {
            // History is diagnostic; never break scanning because of it.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        catch
        {
            // Ignore a locked history file.
        }
    }

    private void TrimIfNeeded()
    {
        try
        {
            var lines = File.ReadAllLines(_filePath);
            if (lines.Length <= 2000) return;

            File.WriteAllLines(_filePath, lines.TakeLast(1000), Encoding.UTF8);
        }
        catch
        {
            // Best-effort retention only.
        }
    }
}
