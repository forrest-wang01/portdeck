using System.Globalization;
using System.Windows.Data;

namespace PortDeck.Localization;

public sealed class LocalizedCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int number ? number : 0;
        return L.Format(count == 1 ? "PortCountOne" : "PortCountFormat", count);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
