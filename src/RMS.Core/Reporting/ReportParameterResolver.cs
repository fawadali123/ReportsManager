using System.Text;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

/// <summary>
/// Works out which inputs a report needs. The SQL is the source of truth for which
/// parameters exist; RMS_REPORT_PARAMETERS only adds labels, types and other presentation detail.
/// </summary>
public static class ReportParameterResolver
{
    /// <summary>Distinct user-facing @parameter names in order of first appearance (system parameters excluded).</summary>
    public static IReadOnlyList<string> GetParameterNames(
        string? sql, DatabaseKind database = DatabaseKind.Oracle, ReportQueryType queryType = ReportQueryType.Select)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var token in SqlScanner.Scan(sql, database, queryType))
        {
            if (token.Kind == SqlTokenKind.Parameter && !SystemParameters.IsSystem(token.Text) && seen.Add(token.Text))
            {
                names.Add(token.Text);
            }
        }
        return names;
    }

    /// <summary>
    /// Combines the parameters found in the SQL with any saved settings.
    /// Parameters without settings get sensible defaults; settings for parameters
    /// no longer in the SQL are dropped.
    /// </summary>
    public static List<ReportParameter> GetEffectiveParameters(
        string? sql,
        IEnumerable<ReportParameter>? saved,
        int reportId = 0,
        DatabaseKind database = DatabaseKind.Oracle,
        ReportQueryType queryType = ReportQueryType.Select)
    {
        var byName = new Dictionary<string, ReportParameter>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in saved ?? [])
        {
            byName.TryAdd(parameter.Name, parameter);
        }

        var names = GetParameterNames(sql, database, queryType);
        var result = new List<ReportParameter>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (byName.TryGetValue(name, out var existing))
            {
                existing.Name = name; // keep the spelling used in the SQL
                if (string.IsNullOrWhiteSpace(existing.Label))
                {
                    existing.Label = CreateLabel(name);
                }
                result.Add(existing);
            }
            else
            {
                result.Add(CreateDefault(name, reportId, sortOrder: (i + 1) * 10));
            }
        }

        // Saved rows keep the order chosen by the administrator; new ones follow in SQL order.
        return result
            .Select((p, position) => (p, position))
            .OrderBy(x => x.p.SortOrder)
            .ThenBy(x => x.position)
            .Select(x => x.p)
            .ToList();
    }

    public static ReportParameter CreateDefault(string name, int reportId = 0, int sortOrder = 0) => new()
    {
        ReportId = reportId,
        Name = name,
        Label = CreateLabel(name),
        DataType = GuessDataType(name),
        Required = true,
        SortOrder = sortOrder,
    };

    /// <summary>Only dates are guessed. Everything else starts as text, which Oracle converts implicitly.</summary>
    public static ParameterDataType GuessDataType(string name)
    {
        var upper = name.ToUpperInvariant();
        return upper.Contains("DATE") || upper.EndsWith("_DT")
            ? ParameterDataType.Date
            : ParameterDataType.Text;
    }

    /// <summary>"FromDate", "p_from_date" and "FROM_DATE" all become "From Date".</summary>
    public static string CreateLabel(string name)
    {
        var text = name;
        if (text.Length > 2 && (text.StartsWith("p_", StringComparison.OrdinalIgnoreCase)
                                || text.StartsWith("v_", StringComparison.OrdinalIgnoreCase)))
        {
            text = text[2..];
        }

        var spaced = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '_')
            {
                spaced.Append(' ');
                continue;
            }

            if (i > 0 && text[i - 1] != '_')
            {
                var previous = text[i - 1];
                var startsWord =
                    (char.IsUpper(c) && char.IsLower(previous))
                    || (char.IsUpper(c) && char.IsUpper(previous) && i + 1 < text.Length && char.IsLower(text[i + 1]))
                    || (char.IsDigit(c) && char.IsLetter(previous));
                if (startsWord)
                {
                    spaced.Append(' ');
                }
            }
            spaced.Append(c);
        }

        var words = spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
        {
            if (word.Equals("ID", StringComparison.OrdinalIgnoreCase))
            {
                return "ID";
            }
            return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        });

        var label = string.Join(' ', words);
        return label.Length == 0 ? name : label;
    }
}
