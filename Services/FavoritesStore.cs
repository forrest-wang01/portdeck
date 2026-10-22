using System.IO;
using System.Text.Json;

namespace PortDeck.Services;

public sealed class FavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public FavoritesStore()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortDeck");
        Directory.CreateDirectory(DataDirectory);
        _filePath = Path.Combine(DataDirectory, "favorites.json");
    }

    public string DataDirectory { get; }

    public string FilePath => _filePath;

    public HashSet<int> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new HashSet<int>();
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<FavoritesFile>(json, JsonOptions);
            return new HashSet<int>((data?.Ports ?? new List<int>())
                .Where(p => p is > 0 and <= 65535));
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    public void Save(IEnumerable<int> ports)
    {
        try
        {
            var data = new FavoritesFile
            {
                Ports = ports.Where(p => p is > 0 and <= 65535).Distinct().OrderBy(p => p).ToList()
            };
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(tempPath, _filePath, true);
        }
        catch
        {
            // Favorites are a convenience; a read-only profile should not crash the app.
        }
    }

    private sealed class FavoritesFile
    {
        public List<int> Ports { get; set; } = new();
    }
}
