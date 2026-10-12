using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

public enum ReportColumnType
{
    Text,
    Number,
    Date,
    Boolean,
}

public sealed record ReportColumn(string Name, ReportColumnType Type);

/// <summary>A bind variable to attach to the database command.</summary>
/// <param name="Name">Bind name without the leading colon.</param>
/// <param name="IsCursor">True for the SYS_REFCURSOR OUT argument of a stored procedure report.</param>
public sealed record BindValue(string Name, ParameterDataType DataType, object? Value, bool IsCursor = false);

/// <summary>A report query ready to execute: native SQL plus its bind variables.</summary>
/// <param name="ConnectionName">Named connection to use, or null for the default report connection.</param>
public sealed record ReportQuery(string? ConnectionName, string Sql, IReadOnlyList<BindValue> Binds);

/// <summary>The typed value(s) supplied for one @parameter.</summary>
public sealed record ParameterValue(ParameterDataType DataType, bool AllowMultiple, IReadOnlyList<object?> Values)
{
    public static ParameterValue Single(ParameterDataType dataType, object? value) =>
        new(dataType, false, [value]);

    public static ParameterValue Text(string? value) => Single(ParameterDataType.Text, value);
}

/// <summary>A fully buffered result, used for the on-screen grid.</summary>
public sealed class ReportTable
{
    public IReadOnlyList<ReportColumn> Columns { get; init; } = [];
    public List<object?[]> Rows { get; init; } = [];

    /// <summary>True when the query returned more rows than the limit and the rest were left unread.</summary>
    public bool Truncated { get; init; }
}

public sealed record LookupItem(string Value, string Label);

/// <summary>One input on the report's parameter form.</summary>
public sealed class ReportFormField
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public ParameterDataType DataType { get; init; }
    public bool Required { get; init; }
    public bool AllowMultiple { get; init; }
    public string? DefaultValue { get; init; }
    public string? HelpText { get; init; }

    /// <summary>Choices for a dropdown; null for free-entry fields.</summary>
    public IReadOnlyList<LookupItem>? Options { get; init; }

    /// <summary>Set when the lookup query failed, so the problem is visible instead of an empty list.</summary>
    public string? LookupError { get; init; }
}

public sealed class ReportForm
{
    public required Report Report { get; init; }
    public required IReadOnlyList<ReportFormField> Fields { get; init; }
}

/// <summary>What an exporter needs to know besides the rows.</summary>
public sealed class ReportExportContext
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? ApplicationName { get; init; }
    public string? CategoryName { get; init; }
    public required string GeneratedBy { get; init; }
    public DateTime GeneratedAt { get; init; }

    /// <summary>Parameter labels and the values the user entered, in form order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; init; } = [];

    /// <summary>Stop after this many data rows.</summary>
    public int MaxRows { get; init; }
}

public sealed record ExportSummary(int RowCount, bool Truncated);

public sealed record ReportRunResult(ReportTable Table, long ElapsedMs);

public static class ReportResultReaderExtensions
{
    /// <summary>Reads up to <paramref name="maxRows"/> rows into memory.</summary>
    public static async Task<ReportTable> ReadTableAsync(
        this IReportResultReader reader, int maxRows, CancellationToken ct = default)
    {
        var rows = new List<object?[]>();
        var columnCount = reader.Columns.Count;
        var truncated = false;

        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }

            var row = new object?[columnCount];
            for (var i = 0; i < columnCount; i++)
            {
                row[i] = reader.GetValue(i);
            }
            rows.Add(row);
        }

        return new ReportTable { Columns = reader.Columns, Rows = rows, Truncated = truncated };
    }
}
