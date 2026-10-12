using RMS.Core.Reporting;

namespace RMS.Core.Abstractions;

/// <summary>Runs a translated report query against a database and streams the rows back.</summary>
public interface IReportExecutor
{
    /// <exception cref="Domain.ReportExecutionException">The database rejected the query.</exception>
    Task<IReportResultReader> ExecuteAsync(ReportQuery query, CancellationToken ct = default);
}

/// <summary>
/// Forward-only view over a report result. Values are already normalised to
/// null, string, bool, DateTime, or a numeric type (decimal, double, long).
/// </summary>
public interface IReportResultReader : IAsyncDisposable
{
    IReadOnlyList<ReportColumn> Columns { get; }
    ValueTask<bool> ReadAsync(CancellationToken ct = default);
    object? GetValue(int ordinal);
}

/// <summary>
/// Writes a report result in one file format. Register another implementation
/// in dependency injection to add a format; it shows up on the Reports screen automatically.
/// </summary>
public interface IReportExporter
{
    /// <summary>Short key used in URLs, for example "xlsx".</summary>
    string Format { get; }

    /// <summary>Button text, for example "Excel".</summary>
    string DisplayName { get; }

    string ContentType { get; }
    string FileExtension { get; }

    /// <summary>Upper limit of data rows this format should be asked to write.</summary>
    int MaxRows { get; }

    Task<ExportSummary> ExportAsync(
        ReportExportContext context,
        IReportResultReader reader,
        Stream output,
        CancellationToken ct = default);
}

/// <summary>The configured database connections that reports may run against, and what kind of database each one is.</summary>
public interface IReportConnectionCatalog
{
    IReadOnlyList<string> ConnectionNames { get; }
    string DefaultConnectionName { get; }

    /// <summary>Database product behind a connection. Null or empty means the default report connection.</summary>
    Domain.DatabaseKind GetKind(string? connectionName);
}
