using System.Collections.Generic;

namespace PortDeck.Services;

public static class GroupExpansionStore
{
    private static readonly HashSet<string> Collapsed = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(string? serialized)
    {
        Collapsed.Clear();
        if (string.IsNullOrWhiteSpace(serialized)) return;

        foreach (var name in serialized.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Collapsed.Add(name.Trim());
    }

    public static bool IsExpanded(string? groupName) =>
        string.IsNullOrWhiteSpace(groupName) || !Collapsed.Contains(groupName);

    public static void Set(string? groupName, bool expanded)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        if (expanded)
            Collapsed.Remove(groupName);
        else
            Collapsed.Add(groupName);
    }

    public static string Serialize() => string.Join('\n', Collapsed);
}
