using System.Globalization;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

/// <summary>Converts the text posted from the parameter form into typed values, and back for display.</summary>
public static class ParameterValueConverter
{
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

    /// <summary>Returns null for blank input; otherwise the typed value.</summary>
    /// <exception cref="FormatException">The text is not a valid value of the type. The message is safe to show.</exception>
    public static object? Convert(string? raw, ParameterDataType dataType)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        switch (dataType)
        {
            case ParameterDataType.Integer:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? whole
                    : throw new FormatException("Enter a whole number.");

            case ParameterDataType.Decimal:
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw new FormatException("Enter a number, for example 1250.75.");

            case ParameterDataType.Date:
                return TryParseDate(text, out var date)
                    ? date.Date
                    : throw new FormatException("Enter a date as YYYY-MM-DD.");

            case ParameterDataType.DateTime:
                return TryParseDate(text, out var dateTime)
                    ? dateTime
                    : throw new FormatException("Enter a date and time as YYYY-MM-DD HH:MM.");

            case ParameterDataType.Boolean:
                return text.ToLowerInvariant() switch
                {
                    "true" or "1" or "on" or "yes" or "y" => true,
                    "false" or "0" or "off" or "no" or "n" => false,
                    _ => throw new FormatException("Choose yes or no."),
                };

            default:
                return text;
        }
    }

    /// <summary>Formats a typed value for report headers and the audit log.</summary>
    public static string ToDisplay(object? value) => value switch
    {
        null => "",
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static bool TryParseDate(string text, out DateTime value) =>
        DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
