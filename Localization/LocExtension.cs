using System.Windows.Data;
using System.Windows.Markup;

namespace PortDeck.Localization;

/// <summary>
/// XAML helper: <c>Text="{loc:Loc NavAllPorts}"</c>.
/// The binding points at the service indexer, so changing the language
/// refreshes every localized element without restarting the window.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
