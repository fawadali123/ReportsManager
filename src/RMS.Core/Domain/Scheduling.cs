namespace RMS.Core.Domain;

public enum ScheduleFrequency
{
    /// <summary>A single run at <see cref="ReportSchedule.RunOnceAt"/>.</summary>
    Once = 0,

    /// <summary>Every <see cref="ReportSchedule.IntervalHours"/> hours from <see cref="ReportSchedule.RunTime"/> until <see cref="ReportSchedule.EndTime"/>.</summary>
    Hourly = 1,

    /// <summary>Every day (or the chosen days of the week) at <see cref="ReportSchedule.RunTime"/>.</summary>
    Daily = 2,

    /// <summary>On the chosen days of the week at <see cref="ReportSchedule.RunTime"/>.</summary>
    Weekly = 3,

    /// <summary>On <see cref="ReportSchedule.DayOfMonth"/> of every month at <see cref="ReportSchedule.RunTime"/>.</summary>
    Monthly = 4,
}

/// <summary>What started a scheduled run.</summary>
public enum ScheduleTrigger
{
    /// <summary>The schedule's own timetable.</summary>
    Schedule = 0,

    /// <summary>An administrator pressed "Run now".</summary>
    Manual = 1,
}

/// <summary>Outcome of one run. Stored as text in RMS_SCHEDULE_RUNS.STATUS and RMS_REPORT_SCHEDULES.LAST_STATUS.</summary>
public static class ScheduleRunStatus
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";

    /// <summary>The file was produced but one of the two deliveries (email or share) failed.</summary>
    public const string Partial = "Partial";

    public const string Failed = "Failed";

    /// <summary>The report returned no rows and the schedule says not to send empty reports.</summary>
    public const string Skipped = "Skipped";
}

/// <summary>A report that the RMS Scheduler service generates and delivers by itself. Maps to RMS_REPORT_SCHEDULES.</summary>
public sealed class ReportSchedule
{
    /// <summary>Value of <see cref="DayOfMonth"/> meaning the last day of each month.</summary>
    public const int LastDayOfMonth = 0;

    public int Id { get; set; }
    public int ReportId { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;

    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;

    /// <summary>Time of day in <see cref="TimeZone"/>. For Hourly, the first run of the day. Not used by Once.</summary>
    public TimeOnly? RunTime { get; set; }

    /// <summary>Hourly only: no run starts after this time of day. Null means until midnight.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>Hourly only: hours between runs, 1 to 23.</summary>
    public int? IntervalHours { get; set; }

    /// <summary>Required for Weekly. For Hourly and Daily, an optional filter (empty means every day).</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    /// <summary>Monthly only: 1 to 31, or <see cref="LastDayOfMonth"/>. A day the month lacks means its last day.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Once only: local date and time in <see cref="TimeZone"/>.</summary>
    public DateTime? RunOnceAt { get; set; }

    /// <summary>Optional first day the schedule may run, in <see cref="TimeZone"/>.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Optional last day the schedule may run, in <see cref="TimeZone"/>.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Windows or IANA time zone id the times above are in, for example "Arab Standard Time".</summary>
    public string TimeZone { get; set; } = "";

    /// <summary>Exporter format key: xlsx, pdf or csv.</summary>
    public string ExportFormat { get; set; } = "xlsx";

    /// <summary>
    /// Values for the report's parameters, by parameter name. Date parameters may hold tokens such as
    /// START_OF_PREV_MONTH, which are worked out when the schedule runs.
    /// </summary>
    public Dictionary<string, string[]> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Do not deliver anything when the report returns no rows.</summary>
    public bool SkipIfEmpty { get; set; }

    public bool SendEmail { get; set; }

    /// <summary>Addresses separated by semicolons or commas.</summary>
    public string? EmailTo { get; set; }
    public string? EmailCc { get; set; }

    /// <summary>Optional; may contain {ReportName}, {ScheduleName} and date formats such as {yyyy-MM-dd}.</summary>
    public string? EmailSubject { get; set; }

    /// <summary>Optional text placed above the standard summary in the email.</summary>
    public string? EmailBody { get; set; }

    public bool SaveToShare { get; set; }

    /// <summary>Folder the file is saved to, for example \\fileserver\reports\finance. Must sit under an allowed share root.</summary>
    public string? ShareFolder { get; set; }

    /// <summary>File name without extension; may contain {ReportCode}, {ReportName}, {ScheduleName} and date formats.</summary>
    public string? FileNamePattern { get; set; }

    // Kept up to date by the web application and the scheduler service.
    public DateTime? NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? RunRequestedUtc { get; set; }
    public string? RunRequestedBy { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntilUtc { get; set; }

    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Read-only values joined in by the repository.
    public string? ReportCode { get; set; }
    public string? ReportName { get; set; }
    public bool ReportActive { get; set; } = true;
}

/// <summary>One run of a schedule and what became of it. Maps to RMS_SCHEDULE_RUNS.</summary>
public sealed class ScheduleRun
{
    public long Id { get; set; }

    /// <summary>Null once the schedule has been deleted; <see cref="ScheduleName"/> keeps what it was called.</summary>
    public int? ScheduleId { get; set; }
    public string ScheduleName { get; set; } = "";
    public int? ReportId { get; set; }
    public string? ReportName { get; set; }
    public ScheduleTrigger Trigger { get; set; }

    /// <summary>The slot this run was for. For a manual run, when it was requested.</summary>
    public DateTime? ScheduledForUtc { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string Status { get; set; } = ScheduleRunStatus.Running;
    public int? RowCount { get; set; }
    public bool Truncated { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }

    /// <summary>Null when email was not requested; otherwise what happened, for example "Sent to 3 recipients".</summary>
    public string? EmailResult { get; set; }

    /// <summary>Null when saving to a share was not requested; otherwise the saved path or the error.</summary>
    public string? ShareResult { get; set; }

    public string? ErrorMessage { get; set; }
    public string? MachineName { get; set; }
}
