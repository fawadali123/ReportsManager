using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Services;
using RMS.Web.Models;
using RMS.Web.Security;

namespace RMS.Web.Controllers;

/// <summary>
/// The Reports screen: pick application, category and report, fill in the parameters, see the grid, export.
/// The page itself is one view; everything after that is JSON so the grid can refresh in place.
/// </summary>
public sealed partial class ReportsController(
    ReportService reports,
    IEnumerable<IReportExporter> exporters,
    ReportingOptions options,
    ILogger<ReportsController> logger) : Controller
{
    private const string ParameterPrefix = "p.";

    [HttpGet]
    public async Task<IActionResult> Index(int? reportId, CancellationToken ct)
    {
        var user = User.ToUserContext();
        return View(new ReportsPageViewModel
        {
            Catalog = await reports.GetCatalogAsync(user, ct),
            SelectedReportId = reportId,
            Exporters = [.. exporters.Select(e => new ExportFormat(e.Format, e.DisplayName))],
            MaxPreviewRows = options.MaxPreviewRows,
            IsAdmin = user.IsAdmin,
        });
    }

    /// <summary>The parameter form for one report: fields, defaults and dropdown choices.</summary>
    [HttpGet]
    public async Task<IActionResult> Definition(int id, CancellationToken ct)
    {
        try
        {
            var form = await reports.GetFormAsync(User.ToUserContext(), id, ct);
            return Json(new
            {
                id = form.Report.Id,
                name = form.Report.Name,
                description = form.Report.Description,
                available = form.Report.IsAvailable,
                fields = form.Fields.Select(f => new
                {
                    name = f.Name,
                    label = f.Label,
                    type = f.DataType.ToString().ToLowerInvariant(),
                    required = f.Required,
                    multiple = f.AllowMultiple,
                    defaultValue = f.DefaultValue,
                    help = f.HelpText,
                    options = f.Options?.Select(o => new { value = o.Value, label = o.Label }),
                    lookupError = f.LookupError,
                }),
            });
        }
        catch (ReportNotFoundException)
        {
            return NotFound(new { error = "This report is not available to you." });
        }
    }

    /// <summary>Runs the report and returns the first rows for the grid.</summary>
    [HttpPost]
    public async Task<IActionResult> Run(int id, CancellationToken ct)
    {
        try
        {
            var result = await reports.RunAsync(User.ToUserContext(), id, ReadParameters(), ct);
            var table = result.Table;

            return Json(new
            {
                columns = table.Columns.Select(c => new { name = c.Name, type = c.Type.ToString().ToLowerInvariant() }),
                rows = table.Rows.Select(row => row.Select(ParameterValueConverter.ToDisplay)),
                rowCount = table.Rows.Count,
                truncated = table.Truncated,
                elapsedMs = result.ElapsedMs,
            });
        }
        catch (Exception ex) when (ToErrorResult(ex) is { } error)
        {
            return error;
        }
    }

    /// <summary>Runs the report again and returns it as a file in the requested format.</summary>
    [HttpPost]
    public async Task<IActionResult> Export(int id, string format, CancellationToken ct)
    {
        var exporter = exporters.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase));
        if (exporter is null)
        {
            return BadRequest(new { error = "That export format is not available." });
        }

        // Exports can be large, so they are spooled to a temporary file that deletes itself when the response ends.
        var spool = new FileStream(
            Path.Combine(Path.GetTempPath(), $"rms-export-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
            var result = await reports.ExportAsync(User.ToUserContext(), id, ReadParameters(), exporter, spool, ct);
            await spool.FlushAsync(ct);
            spool.Position = 0;

            var fileName = $"{SafeFileName().Replace(result.Report.Code, "_")}_{DateTime.Now:yyyyMMdd_HHmm}{exporter.FileExtension}";
            Response.Headers["X-File-Name"] = fileName;
            Response.Headers["X-Row-Count"] = result.Summary.RowCount.ToString(CultureInfo.InvariantCulture);
            if (result.Summary.Truncated)
            {
                Response.Headers["X-Report-Truncated"] = "true";
            }

            return File(spool, exporter.ContentType, fileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await spool.DisposeAsync();

            if (ToErrorResult(ex) is { } error)
            {
                return error;
            }

            logger.LogError(ex, "Export of report {ReportId} as {Format} failed.", id, exporter.Format);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = User.IsAdmin()
                    ? $"The {exporter.DisplayName} file could not be created: {ex.Message}"
                    : $"The {exporter.DisplayName} file could not be created. Try another format or contact an administrator.",
            });
        }
        catch
        {
            await spool.DisposeAsync();
            throw;
        }
    }

    /// <summary>Form fields named "p.Name" carry the report parameters; a field can repeat for multi-select.</summary>
    private Dictionary<string, string[]> ReadParameters() =>
        Request.Form
            .Where(field => field.Key.StartsWith(ParameterPrefix, StringComparison.Ordinal))
            .ToDictionary(
                field => field.Key[ParameterPrefix.Length..],
                field => field.Value.Where(v => v is not null).Select(v => v!).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private IActionResult? ToErrorResult(Exception exception)
    {
        switch (exception)
        {
            case ReportNotFoundException:
                return NotFound(new { error = "This report is not available to you." });

            case ReportValidationException validation:
                return BadRequest(new { error = validation.Message, fieldErrors = validation.FieldErrors });

            case ReportExecutionException execution:
                // Database messages can name tables and columns, so only administrators get the full text.
                return UnprocessableEntity(new
                {
                    error = User.IsAdmin()
                        ? execution.Message
                        : "The report could not be run. Contact an administrator"
                          + (execution.ErrorCode is null ? "." : $" and mention {execution.ErrorCode}."),
                });

            default:
                return null;
        }
    }

    [GeneratedRegex(@"[^A-Za-z0-9_\-]+")]
    private static partial Regex SafeFileName();
}
