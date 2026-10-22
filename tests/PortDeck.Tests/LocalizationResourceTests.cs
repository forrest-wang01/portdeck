using System.IO;
using System.Text.Json;
using Xunit;

namespace PortDeck.Tests;

public sealed class LocalizationResourceTests
{
    private static Dictionary<string, string> Load(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", file);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }

    [Fact]
    public void Chinese_and_English_resources_have_the_same_keys()
    {
        var chinese = Load("Strings.zh-CN.json");
        var english = Load("Strings.en-US.json");

        Assert.Equal(
            chinese.Keys.OrderBy(key => key, StringComparer.Ordinal),
            english.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void No_localized_value_is_empty()
    {
        foreach (var pair in Load("Strings.zh-CN.json").Concat(Load("Strings.en-US.json")))
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"Empty localization value: {pair.Key}");
    }
}
