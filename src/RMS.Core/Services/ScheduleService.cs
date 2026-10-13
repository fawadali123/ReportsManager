using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Scheduling;

namespace RMS.Core.Services;

/// <summary>What the schedule screens do: check and save schedules, and ask the scheduler service to run one now.</summary>
public sealed class ScheduleService(
    IScheduleRepository schedules,
    ReportService reports,
    IEnumerable<IReportExporter> exporters,
    SchedulingOptions options,
    TimeProvider clock)
{
    /// <summary>The identity scheduled runs use. Dropdown choices and checks use it too, so they match what the service will see.</summary>
    public UserContext RunAs => UserContext.ServiceAccount(options.RunAsUsername);

    public string DefaultTimeZoneId => ScheduleTimeZones.DefaultId(options.DefaultTimeZone);

    public IReadOnlyList<IReportExporter> Exporters { get; } = [.. exporters];

    /// <summary>The report's parameter fields as the scheduler service will see them.</summary>
    /// <exception cref="ReportNotFoundException">The report does not exist.</exception>
    public Task<ReportForm> GetParameterFormAsync(int reportId, CancellationToken ct = default) =>
        reports.GetFormAsync(RunAs, reportId, ct);

    /// <summary>Checks and saves a schedule, and works out its next run.</summary>
    /// <returns>The schedule Id.</returns>
    /// <exception cref="ReportValidationException">
    /// Something is wrong. <see cref="ReportValidationException.FieldErrors"/> is keyed by <see cref="ReportSchedule"/>
    /// property name, "p." plus the parameter name for a parameter value, or an empty key for the schedule as a whole.
    /// </exception>
    /// <exception cref="ReportNotFoundException">Editing a schedule that no longer exists.</exception>
    public async Task<int> SaveAsync(ReportSchedule schedule, string savedBy, CancellationToken ct = default)
    {
        var previous = schedule.Id == 0
            ? null
            : await schedules.GetByIdAsync(schedule.Id, ct) ?? throw new ReportNotFoundException(schedule.Id);

        Normalize(schedule);
        var now = clock.GetUtcNow().UtcDateTime;
        var errors = ScheduleValidator.Validate(schedule, options, [.. Exporters.Select(e => e.Format)], now, previous);

        if (schedule.ReportId > 0)
        {
            await CheckParameterValuesAsync(schedule, now, errors, ct);
        }

        if (errors.Count > 0)
        {
            throw new ReportValidationException(errors);
        }

        schedule.NextRunUtc = schedule.Active ? ScheduleCalculator.GetNextRunUtc(schedule, now) : null;

        if (previous is null)
        {
            schedule.CreatedBy = savedBy;
            return await schedules.CreateAsync(schedule, ct);
        }

        schedule.UpdatedBy = savedBy;
        await schedules.UpdateAsync(schedule, ct);
        return schedule.Id;
    }

    /// <summary>Asks the scheduler service to run the schedule at its next check, normally within a minute.</summary>
    public Task RequestRunAsync(int id, string requestedBy, CancellationToken ct = default) =>
        schedules.RequestRunAsync(id, requestedBy, clock.GetUtcNow().UtcDateTime, ct);

    /// <summary>The next few run times, in the schedule's own time zone.</summary>
    public IReadOnlyList<DateTime> GetUpcomingLocalRuns(ReportSchedule schedule, int count)
    {
        if (!schedule.Active || !ScheduleTimeZones.TryFind(schedule.TimeZone, out var zone))
        {
            return [];
        }

        return [.. ScheduleCalculator.GetUpcomingRunsUtc(schedule, clock.GetUtcNow().UtcDateTime, count)
            .Select(utc => TimeZoneInfo.ConvertTimeFromUtc(utc, zone))];
    }

    private async Task CheckParameterValuesAsync(
        ReportSchedule schedule, DateTime utcNow, Dictionary<string, string> errors, CancellationToken ct)
    {
        try
        {
            var form = await reports.GetFormAsync(RunAs, schedule.ReportId, ct);

            // Keep values only for parameters the report still has.
            schedule.ParameterValues = form.Fields.ToDictionary(
                f => f.Name,
                f => schedule.ParameterValues.GetValueOrDefault(f.Name) ?? [],
                StringComparer.OrdinalIgnoreCase);

            var zone = ScheduleTimeZones.TryFind(schedule.TimeZone, out var found) ? found : TimeZoneInfo.Local;
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
            var resolved = ScheduleParameterValues.ResolveForRun(form.Fields, schedule.ParameterValues, localNow);
            await reports.ValidateValuesAsync(RunAs, schedule.ReportId, resolved, ct);
        }
        catch (ReportNotFoundException)
        {
            errors.TryAdd(nameof(ReportSchedule.ReportId), "Choose a report.");
        }
        catch (ReportValidationException ex)
        {
            if (ex.FieldErrors.Count == 0)
            {
                errors.TryAdd(string.Empty, ex.Message);
            }

            foreach (var (name, message) in ex.FieldErrors)
            {
                errors.TryAdd("p." + name, message);
            }
        }
    }

    private static void Normalize(ReportSchedule schedule)
    {
        schedule.Name = schedule.Name?.Trim() ?? "";
        schedule.TimeZone = schedule.TimeZone?.Trim() ?? "";
        schedule.ExportFormat = schedule.ExportFormat?.Trim().ToLowerInvariant() ?? "";
        schedule.EmailTo = JoinAddresses(schedule.EmailTo);
        schedule.EmailCc = JoinAddresses(schedule.EmailCc);
        schedule.EmailSubject = Clean(schedule.EmailSubject);
        schedule.EmailBody = Clean(schedule.EmailBody);
        schedule.ShareFolder = Clean(schedule.ShareFolder);
        schedule.FileNamePattern = Clean(schedule.FileNamePattern);

        // Settings that do not apply to the chosen frequency are cleared, so the stored row says what it does.
        var frequency = schedule.Frequency;
        if (frequency == ScheduleFrequency.Once)
        {
            schedule.RunTime = null;
            schedule.StartDate = null;
            schedule.EndDate = null;
        }
        else
        {
            schedule.RunOnceAt = null;
        }

        if (frequency != ScheduleFrequency.Hourly)
        {
            schedule.IntervalHours = null;
            schedule.EndTime = null;
        }

        if (frequency is ScheduleFrequency.Once or ScheduleFrequency.Monthly)
        {
            schedule.DaysOfWeek = [];
        }

        if (frequency != ScheduleFrequency.Monthly)
        {
            schedule.DayOfMonth = null;
        }

        if (!schedule.SendEmail)
        {
            schedule.EmailTo = schedule.EmailCc = schedule.EmailSubject = schedule.EmailBody = null;
        }

        if (!schedule.SaveToShare)
        {
            schedule.ShareFolder = null;
        }

        static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        static string? JoinAddresses(string? text)
        {
            var list = EmailAddressList.Parse(text);
            return list.Count == 0 ? Clean(text) : string.Join("; ", list);
        }
    }
}
