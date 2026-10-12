using System.Globalization;
using System.Text.RegularExpressions;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

/// <summary>
/// Works out the value a parameter field starts with. Date fields accept moving tokens
/// so a report can open on "this month" without anyone editing it each month.
/// </summary>
public static partial class DefaultValueResolver
{
    /// <summary>Tokens understood by date and date-time parameters. Each may be followed by +N or -N days.</summary>
    public static readonly IReadOnlyList<string> DateTokens =
    [
        "TODAY", "NOW", "YESTERDAY", "TOMORROW",
        "START_OF_MONTH", "END_OF_MONTH", "START_OF_PREV_MONTH", "END_OF_PREV_MONTH",
        "START_OF_YEAR", "END_OF_YEAR",
    ];

    /// <returns>Text in the format the HTML input expects, or null when there is no default.</returns>
    public static string? Resolve(string? defaultValue, ParameterDataType dataType, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return null;
        }

        var text = defaultValue.Trim();
        if (dataType is not (ParameterDataType.Date or ParameterDataType.DateTime))
        {
            return text;
        }

        var match = Token().Match(text);
        if (!match.Success)
        {
            return text; // a literal date such as 2026-01-01
        }

        var today = now.Date;
        var value = match.Groups["token"].Value.ToUpperInvariant() switch
        {
            "TODAY" => today,
            "NOW" => now,
            "YESTERDAY" => today.AddDays(-1),
            "TOMORROW" => today.AddDays(1),
            "START_OF_MONTH" => new DateTime(today.Year, today.Month, 1),
            "END_OF_MONTH" => new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1),
            "START_OF_PREV_MONTH" => new DateTime(today.Year, today.Month, 1).AddMonths(-1),
            "END_OF_PREV_MONTH" => new DateTime(today.Year, today.Month, 1).AddDays(-1),
            "START_OF_YEAR" => new DateTime(today.Year, 1, 1),
            "END_OF_YEAR" => new DateTime(today.Year, 12, 31),
            _ => today,
        };

        if (match.Groups["offset"].Success)
        {
            var offset = int.Parse(match.Groups["offset"].Value.Replace(" ", ""), CultureInfo.InvariantCulture);
            value = value.AddDays(offset);
        }

        return dataType == ParameterDataType.Date
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(
        @"^\{?\s*(?<token>TODAY|NOW|YESTERDAY|TOMORROW|START_OF_MONTH|END_OF_MONTH|START_OF_PREV_MONTH|END_OF_PREV_MONTH|START_OF_YEAR|END_OF_YEAR)\s*(?<offset>[+-]\s*\d{1,5})?\s*\}?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Token();
}
