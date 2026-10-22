using PortDeck.Models;

namespace PortDeck.Services;

public static class SearchMatcher
{
    public static bool Matches(PortEntry entry, string query)
    {
        foreach (var token in query.Split(
                     ' ',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = token.IndexOf(':');
            if (separator > 0)
            {
                var key = token[..separator].ToLowerInvariant();
                var value = token[(separator + 1)..];
                var matched = key switch
                {
                    "port" or "p" => entry.Port.ToString().Contains(value, StringComparison.OrdinalIgnoreCase),
                    "pid" => entry.Pid.ToString().Contains(value, StringComparison.OrdinalIgnoreCase),
                    "project" or "proj" => entry.ProjectName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                                           entry.ProjectPathLabel.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "process" or "proc" => entry.ProcessName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                                           entry.ProcessFile.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "cwd" => entry.WorkingDirectory.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "addr" or "address" => entry.Addresses.Any(
                        address => address.Contains(value, StringComparison.OrdinalIgnoreCase)),
                    _ => false
                };

                if (matched) continue;
                if (key is "port" or "p" or "pid" or "project" or "proj" or "process" or "proc" or "cwd" or "addr" or "address")
                    return false;
            }

            if (!entry.Port.ToString().Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !entry.ProcessName.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !entry.ProjectName.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !entry.AddressSummary.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !entry.CommandLine.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
