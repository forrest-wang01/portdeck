using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace PortDeck.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fallbackStrings = new(StringComparer.Ordinal);

    public static LocalizationService Instance { get; } = new();

    public string CurrentLanguage { get; private set; } = "zh-CN";

    public string Preference { get; private set; } = "system";

    public string this[string key] => Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public void Initialize(string? preference) => SetLanguage(preference ?? "system", notify: false);

    public void SetLanguage(string preference, bool notify = true)
    {
        var code = ResolveLanguage(preference);
        Preference = preference;
        CurrentLanguage = code;

        LoadStrings(code);
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (!notify) return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value)) return value;
        if (_fallbackStrings.TryGetValue(key, out value)) return value;
        return key;
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public static string ResolveLanguage(string? preference)
    {
        if (preference is "zh-CN" or "en-US") return preference;

        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US";
    }

    private void LoadStrings(string code)
    {
        _strings.Clear();
        _fallbackStrings.Clear();

        var fallbackCode = code == "zh-CN" ? "en-US" : "zh-CN";
        foreach (var pair in ReadResource(code)) _strings[pair.Key] = pair.Value;
        foreach (var pair in ReadResource(fallbackCode)) _fallbackStrings[pair.Key] = pair.Value;
    }

    private static Dictionary<string, string> ReadResource(string code)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri($"pack://application:,,,/Localization/Strings.{code}.json", UriKind.Absolute));

            if (resource is null) return new Dictionary<string, string>();

            using var reader = new StreamReader(resource.Stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
