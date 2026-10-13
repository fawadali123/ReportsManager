using System.Diagnostics;
using System.Text.Json;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Core.Services;

public sealed record ReportExportResult(Report Report, ExportSummary Summary);

/// <summary>
/// Everything the Reports screen does: list what the user may run, build the parameter form,
/// run the query and export it. Access is checked here on every call, never only in the UI.
/// </summary>
public sealed class ReportService(
    IReportRepository reports,
    IUserReportRepository assignments,
    IReportExecutor executor,
    IReportRunLogRepository runLog,
    IReportConnectionCatalog connections,
    ReportingOptions options,
    TimeProvider clock)
{
    private const int MaxTextLength = 4000;
    private const string NoValueText = "(any)";

    /// <summary>Administrators get every report; viewers get the active reports assigned to them.</summary>
    public Task<IReadOnlyList<ReportCatalogItem>> GetCatalogAsync(UserContext user, CancellationToken ct = default) =>
        reports.GetCatalogAsync(user.IsAdmin ? null : user.UserId, includeUnavailable: user.IsAdmin, ct);

    /// <exception cref="ReportNotFoundException">The report does not exist or the user may not run it.</exception>
    public async Task<Report> GetAuthorizedReportAsync(UserContext user, int reportId, CancellationToken ct = default)
    {
        var report = await reports.GetByIdAsync(reportId, ct) ?? throw new ReportNotFoundException(reportId);
        if (user.IsAdmin)
        {
            return report;
        }

        if (user.UserId is not int userId || !report.IsAvailable || !await assignments.IsAssignedAsync(userId, reportId, ct))
        {
            // Same answer as "does not exist" so report Ids cannot be probed.
            throw new ReportNotFoundException(reportId);
        }

        return report;
    }

    public async Task<ReportForm> GetFormAsync(UserContext user, int reportId, CancellationToken ct = default)
    {
        var report = await GetAuthorizedReportAsync(user, reportId, ct);
        return new ReportForm { Report = report, Fields = await BuildFieldsAsync(user, report, ct) };
    }

    /// <summary>Runs the report for the on-screen grid, limited to <see cref="ReportingOptions.MaxPreviewRows"/>.</summary>
    /// <param name="rawValues">Posted form values keyed by parameter name.</param>
    public async Task<ReportRunResult> RunAsync(
        UserContext user, int reportId, IReadOnlyDictionary<string, string[]> rawValues, CancellationToken ct = default)
    {
        var report = await GetAuthorizedReportAsync(user, reportId, ct);
        var prepared = await PrepareAsync(user, report, rawValues, ct);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var reader = await executor.ExecuteAsync(prepared.Query, ct);
            var table = await reader.ReadTableAsync(options.MaxPreviewRows, ct);
            stopwatch.Stop();

            await LogAsync(user, report, "View", stopwatch.ElapsedMilliseconds, table.Rows.Count, null, prepared.ParametersJson);
            return new ReportRunResult(table, stopwatch.ElapsedMilliseconds);
        }
        catch (ReportExecutionException ex)
        {
            await LogAsync(user, report, "View", stopwatch.ElapsedMilliseconds, null, ex.Message, prepared.ParametersJson);
            throw;
        }
    }

    /// <summary>Runs the report again and writes it to <paramref name="output"/> in the exporter's format.</summary>
    /// <param name="logAction">Action recorded in the run history; defaults to "Export:&lt;format&gt;".</param>
    public async Task<ReportExportResult> ExportAsync(
        UserContext user,
        int reportId,
        IReadOnlyDictionary<string, string[]> rawValues,
        IReportExporter exporter,
        Stream output,
        CancellationToken ct = default,
        string? logAction = null)
    {
        var report = await GetAuthorizedReportAsync(user, reportId, ct);
        var prepared = await PrepareAsync(user, report, rawValues, ct);
        var action = logAction ?? "Export:" + exporter.Format;
        var stopwatch = Stopwatch.StartNew();

        var context = new ReportExportContext
        {
            Title = report.Name,
            Description = report.Description,
            ApplicationName = report.ApplicationName,
            CategoryName = report.CategoryName,
            GeneratedBy = user.Username,
            GeneratedAt = clock.GetLocalNow().DateTime,
            Parameters = prepared.Display,
            MaxRows = Math.Max(1, Math.Min(options.MaxExportRows, exporter.MaxRows)),
        };

        try
        {
            await using var reader = await executor.ExecuteAsync(prepared.Query, ct);
            var summary = await exporter.ExportAsync(context, reader, output, ct);
            stopwatch.Stop();

            await LogAsync(user, report, action, stopwatch.ElapsedMilliseconds, summary.RowCount, null, prepared.ParametersJson);
            return new ReportExportResult(report, summary);
        }
        catch (ReportExecutionException ex)
        {
            await LogAsync(user, report, action, stopwatch.ElapsedMilliseconds, null, ex.Message, prepared.ParametersJson);
            throw;
        }
    }

    /// <summary>
    /// Checks parameter values the way a run would (required fields, types, dropdown choices) without running
    /// the report. Used when a schedule is saved.
    /// </summary>
    /// <exception cref="ReportValidationException">A value is missing or not valid.</exception>
    public async Task ValidateValuesAsync(
        UserContext user, int reportId, IReadOnlyDictionary<string, string[]> rawValues, CancellationToken ct = default)
    {
        var report = await GetAuthorizedReportAsync(user, reportId, ct);
        await PrepareAsync(user, report, rawValues, ct);
    }

    private async Task<List<ReportFormField>> BuildFieldsAsync(UserContext user, Report report, CancellationToken ct)
    {
        var now = clock.GetLocalNow().DateTime;
        var fields = new List<ReportFormField>();

        var database = connections.GetKind(report.ConnectionName);

        foreach (var parameter in ReportParameterResolver.GetEffectiveParameters(
                     report.SqlQuery, report.Parameters, report.Id, database, report.QueryType))
        {
            IReadOnlyList<LookupItem>? choices = null;
            string? lookupError = null;

            if (!string.IsNullOrWhiteSpace(parameter.LookupSource))
            {
                try
                {
                    choices = await LoadLookupAsync(user, report, parameter, ct);
                }
                catch (Exception ex) when (ex is ReportExecutionException or ReportValidationException)
                {
                    choices = [];
                    lookupError = user.IsAdmin
                        ? "The choices could not be loaded: " + ex.Message
                        : "The choices for this field could not be loaded. Contact an administrator.";
                }
            }

            fields.Add(new ReportFormField
            {
                Name = parameter.Name,
                Label = parameter.Label,
                DataType = parameter.DataType,
                Required = parameter.Required,
                AllowMultiple = parameter.AllowMultiple && choices is not null,
                DefaultValue = DefaultValueResolver.Resolve(parameter.DefaultValue, parameter.DataType, now),
                HelpText = parameter.HelpText,
                Options = choices,
                LookupError = lookupError,
            });
        }

        return fields;
    }

    private async Task<IReadOnlyList<LookupItem>> LoadLookupAsync(
        UserContext user, Report report, ReportParameter parameter, CancellationToken ct)
    {
        if (!LookupSource.IsQuery(parameter.LookupSource))
        {
            return LookupSource.ParseStatic(parameter.LookupSource);
        }

        // A lookup runs on the same connection as its report, so it is written in the same SQL dialect.
        var database = connections.GetKind(report.ConnectionName);
        var problems = ReportSqlValidator.Validate(parameter.LookupSource, ReportQueryType.Select, database);
        if (problems.Count > 0)
        {
            throw new ReportValidationException(problems[0]);
        }

        // Lookup queries may use system parameters (to list only the user's own branches, say) but not other form fields.
        var query = ReportSqlTranslator.Translate(
            parameter.LookupSource!, ReportQueryType.Select, SystemParameters.ValuesFor(user), report.ConnectionName, database);

        await using var reader = await executor.ExecuteAsync(query, ct);
        var labelColumn = reader.Columns.Count > 1 ? 1 : 0;
        var items = new List<LookupItem>();

        while (items.Count < options.MaxLookupItems && await reader.ReadAsync(ct))
        {
            var value = ParameterValueConverter.ToDisplay(reader.GetValue(0));
            if (value.Length == 0)
            {
                continue;
            }

            var label = ParameterValueConverter.ToDisplay(reader.GetValue(labelColumn));
            items.Add(new LookupItem(value, label.Length == 0 ? value : label));
        }

        return items;
    }

    /// <summary>Validates the posted values and produces the executable query.</summary>
    private async Task<PreparedReport> PrepareAsync(
        UserContext user, Report report, IReadOnlyDictionary<string, string[]> rawValues, CancellationToken ct)
    {
        var database = connections.GetKind(report.ConnectionName);
        var problems = ReportSqlValidator.Validate(report.SqlQuery, report.QueryType, database);
        if (problems.Count > 0)
        {
            throw new ReportValidationException(user.IsAdmin
                ? "This report's SQL needs attention: " + string.Join(" ", problems)
                : "This report is not set up correctly. Contact an administrator.");
        }

        if (report.QueryType == ReportQueryType.StoredProcedure && !options.AllowStoredProcedures)
        {
            throw new ReportValidationException("Stored procedure reports are switched off in the configuration.");
        }

        var posted = new Dictionary<string, string[]>(rawValues, StringComparer.OrdinalIgnoreCase);
        var values = SystemParameters.ValuesFor(user);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var display = new List<KeyValuePair<string, string>>();
        var logged = new Dictionary<string, string[]>();

        foreach (var field in await BuildFieldsAsync(user, report, ct))
        {
            var texts = (posted.GetValueOrDefault(field.Name) ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (!field.AllowMultiple && texts.Count > 1)
            {
                texts.RemoveRange(1, texts.Count - 1);
            }

            logged[field.Name] = [.. texts];

            if (texts.Count == 0)
            {
                if (field.Required)
                {
                    errors[field.Name] = $"{field.Label} is required.";
                }

                values[field.Name] = new ParameterValue(field.DataType, field.AllowMultiple, field.AllowMultiple ? [] : [null]);
                display.Add(new(field.Label, NoValueText));
                continue;
            }

            if (field.LookupError is not null)
            {
                errors[field.Name] = field.LookupError;
                continue;
            }

            // A dropdown only accepts what it offered. This matters when the lookup is filtered per user.
            if (field.Options is not null && texts.Any(t => field.Options.All(o => o.Value != t)))
            {
                errors[field.Name] = "Choose a value from the list.";
                continue;
            }

            if (texts.Any(t => t.Length > MaxTextLength))
            {
                errors[field.Name] = $"Use at most {MaxTextLength} characters.";
                continue;
            }

            var typed = new List<object?>(texts.Count);
            try
            {
                typed.AddRange(texts.Select(t => ParameterValueConverter.Convert(t, field.DataType)));
            }
            catch (FormatException ex)
            {
                errors[field.Name] = ex.Message;
                continue;
            }

            values[field.Name] = new ParameterValue(field.DataType, field.AllowMultiple, typed);
            display.Add(new(field.Label, string.Join(", ", texts.Select((t, i) =>
                field.Options?.FirstOrDefault(o => o.Value == t)?.Label ?? ParameterValueConverter.ToDisplay(typed[i])))));
        }

        if (errors.Count > 0)
        {
            throw new ReportValidationException(errors);
        }

        var query = ReportSqlTranslator.Translate(report.SqlQuery, report.QueryType, values, report.ConnectionName, database);
        return new PreparedReport(query, display, JsonSerializer.Serialize(logged));
    }

    private Task LogAsync(
        UserContext user, Report report, string action, long elapsedMs, int? rowCount, string? error, string? parametersJson) =>
        // Not tied to the request's cancellation token: the audit line should be written even if the browser has gone.
        runLog.AddAsync(new ReportRunLog
        {
            ReportId = report.Id,
            ReportName = report.Name,
            UserId = user.UserId,
            Username = user.Username,
            RunAt = clock.GetLocalNow().DateTime,
            Action = action,
            DurationMs = elapsedMs,
            RowCount = rowCount,
            Success = error is null,
            ErrorMessage = Truncate(error, 1000),
            Parameters = Truncate(parametersJson, 4000),
        }, CancellationToken.None);

    private static string? Truncate(string? text, int maxLength) =>
        text is null || text.Length <= maxLength ? text : text[..maxLength];

    private sealed record PreparedReport(
        ReportQuery Query, IReadOnlyList<KeyValuePair<string, string>> Display, string ParametersJson);
}
