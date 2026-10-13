using RMS.Core.Domain;

namespace RMS.Core.Abstractions;

/// <summary>Schedules and their run history. Maps to RMS_REPORT_SCHEDULES and RMS_SCHEDULE_RUNS.</summary>
public interface IScheduleRepository
{
    // ---- Used by the web application ----

    /// <param name="reportId">Only the schedules of this report; null for all.</param>
    Task<IReadOnlyList<ReportSchedule>> GetAllAsync(int? reportId, CancellationToken ct = default);

    Task<ReportSchedule?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <returns>The generated Id.</returns>
    Task<int> CreateAsync(ReportSchedule schedule, CancellationToken ct = default);

    /// <summary>Saves the definition and <see cref="ReportSchedule.NextRunUtc"/>. Run state (locks, last run) is left alone.</summary>
    Task UpdateAsync(ReportSchedule schedule, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Asks the scheduler service to run the schedule as soon as it next looks, whatever its timetable says.</summary>
    Task RequestRunAsync(int id, string requestedBy, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Number of schedules per report Id.</summary>
    Task<IReadOnlyDictionary<int, int>> GetCountsByReportAsync(CancellationToken ct = default);

    /// <param name="scheduleId">Only this schedule's runs; null for all.</param>
    Task<IReadOnlyList<ScheduleRun>> GetRunsAsync(int? scheduleId, int take, CancellationToken ct = default);

    // ---- Used by the scheduler service ----

    /// <summary>
    /// Schedules that should run now: active and past their next run time, or with a "Run now" request,
    /// and not locked by a running scheduler.
    /// </summary>
    Task<IReadOnlyList<int>> GetDueIdsAsync(DateTime utcNow, int max, CancellationToken ct = default);

    /// <summary>
    /// Locks a due schedule for <paramref name="owner"/> until <paramref name="lockUntilUtc"/>.
    /// Returns false when it is no longer due or another scheduler took it first.
    /// </summary>
    Task<bool> TryClaimAsync(int id, string owner, DateTime utcNow, DateTime lockUntilUtc, CancellationToken ct = default);

    /// <summary>Records the outcome on the schedule, sets its next run and releases the lock.</summary>
    Task CompleteAsync(ScheduleCompletion completion, CancellationToken ct = default);

    /// <returns>The generated run Id.</returns>
    Task<long> StartRunAsync(ScheduleRun run, CancellationToken ct = default);

    Task FinishRunAsync(ScheduleRun run, CancellationToken ct = default);

    /// <summary>
    /// Marks runs this machine left in the Running state (the service was stopped or crashed) as failed,
    /// and releases the schedules it had locked.
    /// </summary>
    /// <returns>Number of runs marked failed.</returns>
    Task<int> RecoverInterruptedAsync(string owner, string machineName, DateTime utcNow, CancellationToken ct = default);
}

/// <param name="ScheduleId">Schedule that ran.</param>
/// <param name="Owner">Lock owner; the update only applies while the lock is still this owner's.</param>
/// <param name="NextRunUtc">New next run time. Ignored for a manual run, which leaves the timetable alone.</param>
/// <param name="Trigger">What started the run.</param>
/// <param name="ClearRunRequestUtc">For a manual run, the request time it served; a newer request is kept.</param>
public sealed record ScheduleCompletion(
    int ScheduleId,
    string Owner,
    ScheduleTrigger Trigger,
    DateTime? NextRunUtc,
    DateTime LastRunUtc,
    string LastStatus,
    DateTime? ClearRunRequestUtc);

/// <summary>A file attached to an email.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed class EmailMessage
{
    public required IReadOnlyList<string> To { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

/// <summary>Sends email. Implemented over SMTP (Exchange, Office 365) and Microsoft Graph.</summary>
public interface IEmailSender
{
    /// <summary>Name shown in run results, for example "SMTP" or "Microsoft Graph".</summary>
    string Name { get; }

    /// <summary>Largest attachment this sender will accept. A bigger file is not attached.</summary>
    long MaxAttachmentBytes { get; }

    /// <exception cref="EmailDeliveryException">The message could not be sent; the message says why.</exception>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Saves report files to a shared folder.</summary>
public interface IFileShareWriter
{
    /// <summary>Saves <paramref name="content"/> as <paramref name="fileName"/> in <paramref name="folder"/>.</summary>
    /// <returns>Full path of the saved file. A number is added to the name if the file already exists.</returns>
    /// <exception cref="FileDeliveryException">The folder is not allowed or could not be written.</exception>
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default);
}

public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class FileDeliveryException(string message, Exception? inner = null) : Exception(message, inner);
