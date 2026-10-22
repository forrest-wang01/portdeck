using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PortDeck.Controls;

public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(string), typeof(HighlightTextBlock),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HighlightTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();

        var text = SourceText ?? string.Empty;
        var terms = (Query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveFieldPrefix)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (text.Length == 0) return;
        if (terms.Length == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var position = 0;

        while (position < text.Length)
        {
            var bestIndex = -1;
            string? bestTerm = null;

            foreach (var term in terms)
            {
                var index = text.IndexOf(term, position, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (bestIndex < 0 || index < bestIndex))
                {
                    bestIndex = index;
                    bestTerm = term;
                }
            }

            if (bestIndex < 0)
            {
                Inlines.Add(new Run(text[position..]));
                break;
            }

            if (bestIndex > position)
                Inlines.Add(new Run(text[position..bestIndex]));

            Inlines.Add(new Run(text.Substring(bestIndex, bestTerm!.Length))
            {
                Foreground = accent,
                FontWeight = FontWeights.SemiBold
            });
            position = bestIndex + bestTerm.Length;
        }
    }

    private static string RemoveFieldPrefix(string term)
    {
        var separator = term.IndexOf(':');
        var value = separator > 0 ? term[(separator + 1)..] : term;
        return value.Trim('"', '\'', '“', '”');
    }
}
