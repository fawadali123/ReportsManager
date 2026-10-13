using System.ComponentModel.DataAnnotations;
using System.Globalization;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Scheduling;

namespace RMS.Web.Areas.Admin.Models;

public sealed class ScheduleListViewModel
{
    public IReadOnlyList<ReportSchedule> Schedules { get; set; } = [];

    /// <summary>Set when the list is limited to one report.</summary>
    public Report? Report { get; set; }

    /// <summary>Reports to choose from when adding a schedule.</summary>
    public IReadOnlyList<ReportCatalogItem> Reports { get; set; } = [];

    /// <summary>True when schedules should have run some time ago, which usually means the scheduler service is not running.</summary>
    public bool LooksStalled { get; set; }
}

public sealed class ScheduleRunsViewModel
{
    public IReadOnlyList<ScheduleRun> Runs { get; set; } = [];

    /// <summary>Set when the list is limited to one schedule.</summary>
    public ReportSchedule? Schedule { get; set; }

    /// <summary>Times are shown in this zone.</summary>
    public string TimeZoneId { get; set; } = "";
    public int PageSize { get; set; }
}

/// <summary>The schedule form. Parameter values are posted separately as p.&lt;name&gt; fields, like on the Reports screen.</summary>
public sealed class ScheduleEditModel
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a report.")]
    public int ReportId { get; set; }

    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    public bool Active { get; set; } = true;

    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;

    /// <summary>HH:mm from a time input.</summary>
    [Display(Name = "Time")]
    public string? RunTime { get; set; } = "07:00";

    [Display(Name = "Last run of the day")]
    public string? EndTime { get; set; }

    [Display(Name = "Every")]
    public int? IntervalHours { get; set; } = 1;

    [Display(Name = "Days")]
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    [Display(Name = "Day of the month")]
    public int? DayOfMonth { get; set; } = 1;

    [Display(Name = "Date and time")]
    public DateTime? RunOnceAt { get; set; }

    [Display(Name = "Starting from")]
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [Display(Name = "Ending on")]
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Time zone")]
    public string TimeZone { get; set; } = "";

    [Display(Name = "File format")]
    public string ExportFormat { get; set; } = "xlsx";

    [Display(Name = "Do not send anything when the report has no rows")]
    public bool SkipIfEmpty { get; set; }

    [Display(Name = "Send by email")]
    public bool SendEmail { get; set; } = true;

    [Display(Name = "To")]
    [StringLength(2000)]
    public string? EmailTo { get; set; }

    [Display(Name = "Cc")]
    [StringLength(2000)]
    public string? EmailCc { get; set; }

    [Display(Name = "Subject")]
    [StringLength(300)]
    public string? EmailSubject { get; set; }

    [Display(Name = "Message")]
    [StringLength(4000)]
    public string? EmailBody { get; set; }

    [Display(Name = "Save to a shared folder")]
    public bool SaveToShare { get; set; }

    [Display(Name = "Folder")]
    [StringLength(500)]
    public string? ShareFolder { get; set; }

    [Display(Name = "File name")]
    [StringLength(200)]
    public string? FileNamePattern { get; set; }

    // ---- Filled by the controller for display; not posted back ----

    public Report? Report { get; set; }
    public IReadOnlyList<ReportFormField> Fields { get; set; } = [];

    /// <summary>Values shown in the parameter fields: saved, posted, or the report's own defaults.</summary>
    public Dictionary<string, string[]> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IReportExporter> Exporters { get; set; } = [];
    public IReadOnlyList<TimeZoneInfo> TimeZones { get; set; } = [];
    public IReadOnlyList<DateTime> UpcomingRuns { get; set; } = [];
    public IReadOnlyList<string> AllowedEmailDomains { get; set; } = [];
    public IReadOnlyList<string> AllowedShareRoots { get; set; } = [];
    public string RunAsUsername { get; set; } = "";

    // Run state of a saved schedule.
    public DateTime? LastRunUtc { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? RunRequestedUtc { get; set; }

    public static ScheduleEditModel From(ReportSchedule s) => new()
    {
        Id = s.Id,
        ReportId = s.ReportId,
        Name = s.Name,
        Active = s.Active,
        Frequency = s.Frequency,
        RunTime = s.RunTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        EndTime = s.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        IntervalHours = s.IntervalHours ?? 1,
        DaysOfWeek = [.. s.DaysOfWeek],
        DayOfMonth = s.DayOfMonth ?? 1,
        RunOnceAt = s.RunOnceAt,
        StartDate = s.StartDate,
        EndDate = s.EndDate,
        TimeZone = s.TimeZone,
        ExportFormat = s.ExportFormat,
        SkipIfEmpty = s.SkipIfEmpty,
        SendEmail = s.SendEmail,
        EmailTo = s.EmailTo,
        EmailCc = s.EmailCc,
        EmailSubject = s.EmailSubject,
        EmailBody = s.EmailBody,
        SaveToShare = s.SaveToShare,
        ShareFolder = s.ShareFolder,
        FileNamePattern = s.FileNamePattern,
        ParameterValues = new Dictionary<string, string[]>(s.ParameterValues, StringComparer.OrdinalIgnoreCase),
        LastRunUtc = s.LastRunUtc,
        LastStatus = s.LastStatus,
        RunRequestedUtc = s.RunRequestedUtc,
    };

    public ReportSchedule ToEntity() => new()
    {
        Id = Id,
        ReportId = ReportId,
        Name = Name,
        Active = Active,
        Frequency = Frequency,
        RunTime = ParseTime(RunTime),
        EndTime = ParseTime(EndTime),
        IntervalHours = IntervalHours,
        DaysOfWeek = [.. DaysOfWeek.Distinct()],
        DayOfMonth = DayOfMonth,
        RunOnceAt = RunOnceAt,
        StartDate = StartDate?.Date,
        EndDate = EndDate?.Date,
        TimeZone = TimeZone,
        ExportFormat = ExportFormat,
        ParameterValues = new Dictionary<string, string[]>(ParameterValues, StringComparer.OrdinalIgnoreCase),
        SkipIfEmpty = SkipIfEmpty,
        SendEmail = SendEmail,
        EmailTo = EmailTo,
        EmailCc = EmailCc,
        EmailSubject = EmailSubject,
        EmailBody = EmailBody,
        SaveToShare = SaveToShare,
        ShareFolder = ShareFolder,
        FileNamePattern = FileNamePattern,
    };

    public static bool IsValidTime(string? text) => string.IsNullOrWhiteSpace(text) || ParseTime(text) is not null;

    private static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? new TimeOnly(time.Hour, time.Minute)
            : null;
}

/// <summary>Formatting shared by the schedule views.</summary>
public static class ScheduleDisplay
{
    public static string Local(DateTime? utc, string? timeZoneId, string format = "yyyy-MM-dd HH:mm")
    {
        if (utc is not { } value)
        {
            return "";
        }

        var zone = ScheduleTimeZones.TryFind(timeZoneId, out var found) ? found : TimeZoneInfo.Local;
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), zone)
            .ToString(format, CultureInfo.InvariantCulture);
    }

    public static string StatusClass(string? status) => status switch
    {
        ScheduleRunStatus.Succeeded => "rms-status-on",
        ScheduleRunStatus.Skipped => "rms-status-off",
        ScheduleRunStatus.Running => "rms-status-run",
        ScheduleRunStatus.Partial => "rms-status-warn",
        ScheduleRunStatus.Failed => "rms-status-bad",
        _ => "rms-status-off",
    };

    public static string Size(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes / 1024d / 1024d:0.#} MB",
    };
}
