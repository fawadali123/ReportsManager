using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Scheduling;
using RMS.Core.Services;
using RMS.Web.Areas.Admin.Models;

namespace RMS.Web.Areas.Admin.Controllers;

/// <summary>
/// Report schedules: when a report is generated automatically, with which parameter values, and where it goes.
/// The RMS Scheduler service does the actual running; this screen only edits the schedules and reads the history.
/// </summary>
public sealed class SchedulesController(
    IScheduleRepository schedules,
    IReportRepository reports,
    ScheduleService scheduleService,
    SchedulingOptions options,
    TimeProvider clock) : AdminController
{
    private const string ParameterPrefix = "p.";
    private const int RunsPageSize = 200;

    [HttpGet]
    public async Task<IActionResult> Index(int? reportId, CancellationToken ct)
    {
        var model = new ScheduleListViewModel
        {
            Schedules = await schedules.GetAllAsync(reportId, ct),
            Report = reportId is int id ? await reports.GetByIdAsync(id, ct) : null,
            Reports = await reports.GetCatalogAsync(null, includeUnavailable: true, ct),
        };

        if (reportId is not null && model.Report is null)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        model.LooksStalled = model.Schedules.Any(s =>
            (s.Active && s.NextRunUtc < now.AddMinutes(-10) && !(s.LockedUntilUtc > now))
            || s.RunRequestedUtc < now.AddMinutes(-5));

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int reportId, int? copyFrom, CancellationToken ct)
    {
        ScheduleEditModel model;
        if (copyFrom is int sourceId && await schedules.GetByIdAsync(sourceId, ct) is { } source)
        {
            model = ScheduleEditModel.From(source);
            model.Id = 0;
            model.Name = source.Name + " (copy)";
            model.LastRunUtc = null;
            model.LastStatus = null;
            model.RunRequestedUtc = null;
        }
        else
        {
            var report = reportId > 0 ? await reports.GetByIdAsync(reportId, ct) : null;
            if (report is null)
            {
                Failure("Choose the report to schedule.");
                return RedirectToAction(nameof(Index));
            }

            model = new ScheduleEditModel
            {
                ReportId = report.Id,
                Name = report.Name,
                TimeZone = scheduleService.DefaultTimeZoneId,
                DaysOfWeek = [.. ScheduleDays.Week],
            };
        }

        return View("Edit", await PrepareAsync(model, useReportDefaults: copyFrom is null, ct));
    }

    [HttpPost]
    public Task<IActionResult> Create(ScheduleEditModel model, CancellationToken ct)
    {
        model.Id = 0;
        return SaveAsync(model, ct);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var schedule = await schedules.GetByIdAsync(id, ct);
        if (schedule is null)
        {
            return NotFound();
        }

        var model = ScheduleEditModel.From(schedule);
        model.UpcomingRuns = scheduleService.GetUpcomingLocalRuns(schedule, 5);
        return View(await PrepareAsync(model, useReportDefaults: false, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ScheduleEditModel model, CancellationToken ct)
    {
        model.Id = id;
        if (await schedules.GetByIdAsync(id, ct) is null)
        {
            return NotFound();
        }
        return await SaveAsync(model, ct);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, int? reportId, CancellationToken ct)
    {
        var schedule = await schedules.GetByIdAsync(id, ct);
        if (schedule is null)
        {
            return NotFound();
        }

        await schedules.DeleteAsync(id, ct);
        Success($"Deleted schedule {schedule.Name}. Its run history is kept.");
        return RedirectToAction(nameof(Index), new { reportId });
    }

    /// <summary>Asks the scheduler service to run the schedule at its next check, whatever its timetable says.</summary>
    [HttpPost]
    public async Task<IActionResult> RunNow(int id, string? returnTo, CancellationToken ct)
    {
        var schedule = await schedules.GetByIdAsync(id, ct);
        if (schedule is null)
        {
            return NotFound();
        }

        await scheduleService.RequestRunAsync(id, User.Identity?.Name ?? "admin", ct);
        Success($"Asked the scheduler to run {schedule.Name}. It starts within {Math.Max(5, options.PollSeconds)} seconds "
            + "if the RMS Scheduler service is running; the result appears in its history.");

        return returnTo == "edit"
            ? RedirectToAction(nameof(Edit), new { id })
            : RedirectToAction(nameof(Index), new { reportId = returnTo == "report" ? schedule.ReportId : (int?)null });
    }

    [HttpGet]
    public async Task<IActionResult> Runs(int? id, CancellationToken ct)
    {
        var schedule = id is int scheduleId ? await schedules.GetByIdAsync(scheduleId, ct) : null;
        if (id is not null && schedule is null)
        {
            return NotFound();
        }

        return View(new ScheduleRunsViewModel
        {
            Runs = await schedules.GetRunsAsync(id, RunsPageSize, ct),
            Schedule = schedule,
            TimeZoneId = schedule?.TimeZone ?? scheduleService.DefaultTimeZoneId,
            PageSize = RunsPageSize,
        });
    }

    private async Task<IActionResult> SaveAsync(ScheduleEditModel model, CancellationToken ct)
    {
        model.ParameterValues = ReadParameters();

        if (!ScheduleEditModel.IsValidTime(model.RunTime))
        {
            ModelState.AddModelError(nameof(model.RunTime), "Enter the time as HH:MM.");
        }
        if (!ScheduleEditModel.IsValidTime(model.EndTime))
        {
            ModelState.AddModelError(nameof(model.EndTime), "Enter the time as HH:MM.");
        }

        if (ModelState.IsValid)
        {
            var schedule = model.ToEntity();
            try
            {
                var id = await scheduleService.SaveAsync(schedule, User.Identity?.Name ?? "admin", ct);
                Success(schedule.Active
                    ? $"Saved schedule {schedule.Name}. "
                      + (schedule.NextRunUtc is { } next ? $"Next run: {ScheduleDisplay.Local(next, schedule.TimeZone)}." : "It has no further runs.")
                    : $"Saved schedule {schedule.Name}. It is inactive, so it will not run.");
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (ReportNotFoundException)
            {
                return NotFound();
            }
            catch (ReportValidationException ex)
            {
                foreach (var (key, message) in ex.FieldErrors)
                {
                    ModelState.AddModelError(key, message);
                }
                if (ex.FieldErrors.Count == 0)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
            }
        }

        return View("Edit", await PrepareAsync(model, useReportDefaults: false, ct));
    }

    /// <summary>Fills in what the form needs besides the posted values: the report, its parameter fields and the lists.</summary>
    private async Task<ScheduleEditModel> PrepareAsync(ScheduleEditModel model, bool useReportDefaults, CancellationToken ct)
    {
        model.Exporters = scheduleService.Exporters;
        model.TimeZones = TimeZoneInfo.GetSystemTimeZones();
        model.AllowedEmailDomains = options.AllowedEmailDomains;
        model.AllowedShareRoots = options.AllowedShareRoots;
        model.RunAsUsername = options.RunAsUsername;
        if (string.IsNullOrWhiteSpace(model.TimeZone))
        {
            model.TimeZone = scheduleService.DefaultTimeZoneId;
        }

        try
        {
            var form = await scheduleService.GetParameterFormAsync(model.ReportId, ct);
            model.Report = form.Report;
            model.Fields = form.Fields;

            if (useReportDefaults)
            {
                // A new schedule starts from the report's own defaults, kept as tokens (START_OF_MONTH rather than
                // today's date) so that they keep moving with each run.
                foreach (var field in form.Fields)
                {
                    var raw = form.Report.Parameters
                        .FirstOrDefault(p => string.Equals(p.Name, field.Name, StringComparison.OrdinalIgnoreCase))?.DefaultValue
                        ?? field.DefaultValue;
                    model.ParameterValues[field.Name] = string.IsNullOrWhiteSpace(raw)
                        ? []
                        : field.AllowMultiple ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [raw];
                }
            }
        }
        catch (ReportNotFoundException)
        {
            ModelState.AddModelError(nameof(model.ReportId), "The report no longer exists.");
        }

        return model;
    }

    private Dictionary<string, string[]> ReadParameters() =>
        Request.HasFormContentType
            ? Request.Form
                .Where(field => field.Key.StartsWith(ParameterPrefix, StringComparison.Ordinal))
                .ToDictionary(
                    field => field.Key[ParameterPrefix.Length..],
                    field => field.Value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
}
