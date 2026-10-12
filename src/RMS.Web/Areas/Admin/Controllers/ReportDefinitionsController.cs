using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Services;
using RMS.Web.Areas.Admin.Models;

namespace RMS.Web.Areas.Admin.Controllers;

/// <summary>Maintains report definitions: the SQL, where it runs, and how its @parameters are presented.</summary>
public sealed class ReportDefinitionsController(
    IReportRepository reports,
    IApplicationRepository applications,
    IReportCategoryRepository categories,
    IReportConnectionCatalog connections,
    ReportingOptions options,
    TimeProvider clock) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await reports.GetAllAsync(ct));

    /// <param name="copyFrom">Start from a copy of this report.</param>
    [HttpGet]
    public async Task<IActionResult> Create(int? copyFrom, CancellationToken ct)
    {
        var model = new ReportEditModel();

        if (copyFrom is not null && await reports.GetByIdAsync(copyFrom.Value, ct) is { } source)
        {
            model = ReportEditModel.From(source);
            model.Id = 0;
            model.Code = "";
            model.Name = source.Name + " (copy)";
            model.Parameters = ToEditModels(ReportParameterResolver.GetEffectiveParameters(
                source.SqlQuery, source.Parameters, 0, connections.GetKind(source.ConnectionName), source.QueryType));
        }

        return View("Edit", await WithListsAsync(model, ct));
    }

    [HttpPost]
    public Task<IActionResult> Create(ReportEditModel model, CancellationToken ct)
    {
        model.Id = 0;
        return SaveAsync(model, ct);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(id, ct);
        if (report is null)
        {
            return NotFound();
        }

        var model = ReportEditModel.From(report);
        model.Parameters = ToEditModels(ReportParameterResolver.GetEffectiveParameters(
            report.SqlQuery, report.Parameters, report.Id, connections.GetKind(report.ConnectionName), report.QueryType));
        return View(await WithListsAsync(model, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ReportEditModel model, CancellationToken ct)
    {
        model.Id = id;
        if (await reports.GetByIdAsync(id, ct) is null)
        {
            return NotFound();
        }
        return await SaveAsync(model, ct);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(id, ct);
        if (report is null)
        {
            return NotFound();
        }

        await reports.DeleteAsync(id, ct);
        Success($"Deleted report {report.Name}. It was removed from every user; run history is kept.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Called by the editor's "Check SQL" button: validates the SQL and returns the @parameters found in it
    /// with suggested settings, so the parameter list can be refreshed without saving.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Inspect(
        [FromForm] string? sql, [FromForm] ReportQueryType queryType, [FromForm] int applicationId, CancellationToken ct)
    {
        var database = await DatabaseOfAsync(applicationId, ct);
        var errors = ReportSqlValidator.Validate(sql, queryType, database);
        var parameters = ReportParameterResolver.GetParameterNames(sql, database, queryType)
            .Select(name => ReportParameterResolver.CreateDefault(name))
            .Select(p => new
            {
                name = p.Name,
                label = p.Label,
                dataType = p.DataType.ToString(),
            });

        return Json(new { errors, parameters, database = database.DisplayName() });
    }

    private async Task<IActionResult> SaveAsync(ReportEditModel model, CancellationToken ct)
    {
        var report = model.ToEntity();

        // The application decides which database the report runs on, and with it the SQL dialect.
        var database = await DatabaseOfAsync(report.ApplicationId, ct);

        // The SQL decides which parameters exist. Posted settings are matched to it by name;
        // parameters without settings get defaults, settings without a parameter are dropped.
        var posted = model.Parameters.Select(p => p.ToEntity()).ToList();
        report.Parameters = ReportParameterResolver.GetEffectiveParameters(
            report.SqlQuery, posted, report.Id, database, report.QueryType);

        if (ModelState.IsValid)
        {
            foreach (var error in ReportDefinitionValidator.Validate(report, options, clock.GetLocalNow().DateTime, database))
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }

        if (ModelState.IsValid)
        {
            for (var i = 0; i < report.Parameters.Count; i++)
            {
                report.Parameters[i].SortOrder = (i + 1) * 10;
            }

            try
            {
                if (report.Id == 0)
                {
                    report.Id = await reports.CreateAsync(report, ct);
                }
                else
                {
                    await reports.UpdateAsync(report, ct);
                }

                Success($"Saved report {report.Name}.");
                return RedirectToAction(nameof(Edit), new { id = report.Id });
            }
            catch (DuplicateValueException)
            {
                ModelState.AddModelError(nameof(model.Code), "Another report already has this code.");
            }
        }

        // Redisplay with the merged parameter list rather than the raw posted one.
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith(nameof(model.Parameters), StringComparison.Ordinal)).ToList())
        {
            if (ModelState[key]!.Errors.Count == 0)
            {
                ModelState.Remove(key);
            }
        }
        model.Parameters = ToEditModels(report.Parameters);

        return View("Edit", await WithListsAsync(model, ct));
    }

    private async Task<ReportEditModel> WithListsAsync(ReportEditModel model, CancellationToken ct)
    {
        model.Applications = await applications.GetAllAsync(ct);
        model.Categories = await categories.GetAllAsync(ct);
        model.ApplicationDatabases = model.Applications.ToDictionary(
            a => a.Id,
            a => new ApplicationDatabase(
                string.IsNullOrEmpty(a.ConnectionName) ? connections.DefaultConnectionName : a.ConnectionName,
                connections.GetKind(a.ConnectionName)));
        model.StoredProceduresAllowed = options.AllowStoredProcedures;
        return model;
    }

    private async Task<DatabaseKind> DatabaseOfAsync(int applicationId, CancellationToken ct)
    {
        var application = applicationId > 0 ? await applications.GetByIdAsync(applicationId, ct) : null;
        return connections.GetKind(application?.ConnectionName);
    }

    private static List<ReportParameterEditModel> ToEditModels(IEnumerable<ReportParameter> parameters) =>
        [.. parameters.Select(ReportParameterEditModel.From)];
}
