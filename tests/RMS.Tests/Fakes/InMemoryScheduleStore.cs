using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Tests.Fakes;

/// <summary>In-memory RMS_REPORT_SCHEDULES and RMS_SCHEDULE_RUNS, following the same rules as the SQL in ScheduleRepository.</summary>
public sealed partial class InMemoryStore : IScheduleRepository
{
    public List<ReportSchedule> Schedules { get; } = [];
    public List<ScheduleRun> ScheduleRuns { get; } = [];
    private long _nextRunId = 1;

    public Task<IReadOnlyList<ReportSchedule>> GetAllAsync(int? reportId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ReportSchedule>>([.. Schedules
                .Where(s => reportId is null || s.ReportId == reportId)
                .Select(HydrateSchedule)
                .OrderBy(s => s.ReportName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    Task<ReportSchedule?> IScheduleRepository.GetByIdAsync(int id, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Schedules.Where(s => s.Id == id).Select(HydrateSchedule).FirstOrDefault());
    }

    public Task<int> CreateAsync(ReportSchedule schedule, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Reports.All(r => r.Id != schedule.ReportId))
            {
                throw new InvalidOperationException("FK_RMS_SCHEDULES_REPORT");
            }

            var copy = CopySchedule(schedule);
            copy.Id = NextId();
            Schedules.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task UpdateAsync(ReportSchedule schedule, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var index = Schedules.FindIndex(s => s.Id == schedule.Id);
            if (index < 0) return Task.CompletedTask;

            // Definition and next run only; run state stays as it is, like the SQL UPDATE.
            var current = Schedules[index];
            var copy = CopySchedule(schedule);
            copy.LastRunUtc = current.LastRunUtc;
            copy.LastStatus = current.LastStatus;
            copy.RunRequestedUtc = current.RunRequestedUtc;
            copy.RunRequestedBy = current.RunRequestedBy;
            copy.LockedBy = current.LockedBy;
            copy.LockedUntilUtc = current.LockedUntilUtc;
            copy.CreatedBy = current.CreatedBy;
            Schedules[index] = copy;
            return Task.CompletedTask;
        }
    }

    Task IScheduleRepository.DeleteAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            Schedules.RemoveAll(s => s.Id == id);
            foreach (var run in ScheduleRuns.Where(r => r.ScheduleId == id)) run.ScheduleId = null;
            return Task.CompletedTask;
        }
    }

    public Task RequestRunAsync(int id, string requestedBy, DateTime utcNow, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var s in Schedules.Where(s => s.Id == id))
            {
                s.RunRequestedUtc = utcNow;
                s.RunRequestedBy = requestedBy;
            }
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyDictionary<int, int>> GetCountsByReportAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyDictionary<int, int>>(
                Schedules.GroupBy(s => s.ReportId).ToDictionary(g => g.Key, g => g.Count()));
    }

    public Task<IReadOnlyList<ScheduleRun>> GetRunsAsync(int? scheduleId, int take, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ScheduleRun>>([.. ScheduleRuns
                .Where(r => scheduleId is null || r.ScheduleId == scheduleId)
                .OrderByDescending(r => r.StartedUtc).ThenByDescending(r => r.Id)
                .Take(take)
                .Select(CopyRun)]);
    }

    public Task<IReadOnlyList<int>> GetDueIdsAsync(DateTime utcNow, int max, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<int>>([.. Schedules
                .Where(s => IsDue(s, utcNow))
                .OrderBy(s => s.RunRequestedUtc ?? s.NextRunUtc).ThenBy(s => s.Id)
                .Take(Math.Max(1, max))
                .Select(s => s.Id)]);
    }

    public Task<bool> TryClaimAsync(int id, string owner, DateTime utcNow, DateTime lockUntilUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var schedule = Schedules.FirstOrDefault(s => s.Id == id);
            if (schedule is null || !IsDue(schedule, utcNow)) return Task.FromResult(false);
            schedule.LockedBy = owner;
            schedule.LockedUntilUtc = lockUntilUtc;
            return Task.FromResult(true);
        }
    }

    public Task CompleteAsync(ScheduleCompletion completion, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var s = Schedules.FirstOrDefault(x => x.Id == completion.ScheduleId && x.LockedBy == completion.Owner);
            if (s is null) return Task.CompletedTask;

            if (completion.Trigger != ScheduleTrigger.Manual) s.NextRunUtc = completion.NextRunUtc;
            s.LastRunUtc = completion.LastRunUtc;
            s.LastStatus = completion.LastStatus;
            if (s.RunRequestedUtc <= completion.ClearRunRequestUtc)
            {
                s.RunRequestedUtc = null;
                s.RunRequestedBy = null;
            }
            s.LockedBy = null;
            s.LockedUntilUtc = null;
            return Task.CompletedTask;
        }
    }

    public Task<long> StartRunAsync(ScheduleRun run, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var copy = CopyRun(run);
            copy.Id = _nextRunId++;
            ScheduleRuns.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task FinishRunAsync(ScheduleRun run, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var index = ScheduleRuns.FindIndex(r => r.Id == run.Id);
            if (index >= 0)
            {
                var copy = CopyRun(run);
                copy.ScheduleId = ScheduleRuns[index].ScheduleId; // may have been cleared by a delete meanwhile
                ScheduleRuns[index] = copy;
            }
            return Task.CompletedTask;
        }
    }

    public Task<int> RecoverInterruptedAsync(string owner, string machineName, DateTime utcNow, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var run in ScheduleRuns.Where(r => r.Status == ScheduleRunStatus.Running && r.MachineName == machineName))
            {
                run.Status = ScheduleRunStatus.Failed;
                run.FinishedUtc = utcNow;
                run.ErrorMessage = "The scheduler service stopped before this run finished.";
                count++;
            }
            foreach (var s in Schedules.Where(s => s.LockedBy == owner))
            {
                s.LockedBy = null;
                s.LockedUntilUtc = null;
            }
            return Task.FromResult(count);
        }
    }

    private static bool IsDue(ReportSchedule s, DateTime now) =>
        ((s.Active && s.NextRunUtc <= now) || s.RunRequestedUtc is not null)
        && (s.LockedUntilUtc is null || s.LockedUntilUtc < now);

    private ReportSchedule HydrateSchedule(ReportSchedule source)
    {
        var copy = CopySchedule(source);
        var report = Reports.FirstOrDefault(r => r.Id == source.ReportId);
        copy.ReportCode = report?.Code;
        copy.ReportName = report?.Name;
        copy.ReportActive = report?.Active ?? false;
        return copy;
    }

    private static ReportSchedule CopySchedule(ReportSchedule s) => new()
    {
        Id = s.Id, ReportId = s.ReportId, Name = s.Name, Active = s.Active, Frequency = s.Frequency,
        RunTime = s.RunTime, EndTime = s.EndTime, IntervalHours = s.IntervalHours, DaysOfWeek = [.. s.DaysOfWeek],
        DayOfMonth = s.DayOfMonth, RunOnceAt = s.RunOnceAt, StartDate = s.StartDate, EndDate = s.EndDate,
        TimeZone = s.TimeZone, ExportFormat = s.ExportFormat,
        ParameterValues = new Dictionary<string, string[]>(s.ParameterValues, StringComparer.OrdinalIgnoreCase),
        SkipIfEmpty = s.SkipIfEmpty, SendEmail = s.SendEmail, EmailTo = s.EmailTo, EmailCc = s.EmailCc,
        EmailSubject = s.EmailSubject, EmailBody = s.EmailBody, SaveToShare = s.SaveToShare, ShareFolder = s.ShareFolder,
        FileNamePattern = s.FileNamePattern, NextRunUtc = s.NextRunUtc, LastRunUtc = s.LastRunUtc, LastStatus = s.LastStatus,
        RunRequestedUtc = s.RunRequestedUtc, RunRequestedBy = s.RunRequestedBy, LockedBy = s.LockedBy,
        LockedUntilUtc = s.LockedUntilUtc, CreatedBy = s.CreatedBy, UpdatedBy = s.UpdatedBy,
    };

    private static ScheduleRun CopyRun(ScheduleRun r) => new()
    {
        Id = r.Id, ScheduleId = r.ScheduleId, ScheduleName = r.ScheduleName, ReportId = r.ReportId, ReportName = r.ReportName,
        Trigger = r.Trigger, ScheduledForUtc = r.ScheduledForUtc, StartedUtc = r.StartedUtc, FinishedUtc = r.FinishedUtc,
        Status = r.Status, RowCount = r.RowCount, Truncated = r.Truncated, FileName = r.FileName, FileSize = r.FileSize,
        EmailResult = r.EmailResult, ShareResult = r.ShareResult, ErrorMessage = r.ErrorMessage, MachineName = r.MachineName,
    };
}

/// <summary>Email sender that keeps the messages instead of sending them.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public string Name => "test mail";
    public long MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;
    public string? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (FailWith is not null) throw new EmailDeliveryException(FailWith);
        lock (Sent) Sent.Add(message);
        return Task.CompletedTask;
    }
}
