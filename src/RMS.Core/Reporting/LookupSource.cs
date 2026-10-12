namespace RMS.Core.Reporting;

/// <summary>Interprets <see cref="Domain.ReportParameter.LookupSource"/>.</summary>
public static class LookupSource
{
    private static readonly char[] Separators = [';', '\n', '\r'];

    /// <summary>True when the text is a query to run rather than a fixed list.</summary>
    public static bool IsQuery(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var text = source.TrimStart();
        return StartsWithWord(text, "SELECT") || StartsWithWord(text, "WITH");
    }

    /// <summary>
    /// Parses a fixed list such as <c>A=Active;I=Inactive</c>. Entries are separated by semicolons
    /// or new lines; an entry without '=' uses the same text for value and label.
    /// </summary>
    public static IReadOnlyList<LookupItem> ParseStatic(string? source)
    {
        var items = new List<LookupItem>();
        if (string.IsNullOrWhiteSpace(source))
        {
            return items;
        }

        foreach (var entry in source.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator < 0)
            {
                items.Add(new LookupItem(entry, entry));
            }
            else
            {
                var value = entry[..separator].Trim();
                var label = entry[(separator + 1)..].Trim();
                items.Add(new LookupItem(value, label.Length == 0 ? value : label));
            }
        }
        return items;
    }

    private static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
        && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]) && text[word.Length] != '=');
}
