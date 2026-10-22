using System.Globalization;
using System.Windows.Data;
using PortDeck.Services;

namespace PortDeck.Controls;

public sealed class GroupExpansionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        GroupExpansionStore.IsExpanded(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
