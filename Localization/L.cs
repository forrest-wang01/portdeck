namespace PortDeck.Localization;

public static class L
{
    public static string Get(string key) => LocalizationService.Instance.Get(key);

    public static string Format(string key, params object?[] args) =>
        LocalizationService.Instance.Format(key, args);
}
