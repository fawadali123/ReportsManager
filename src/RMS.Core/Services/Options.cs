namespace RMS.Core.Services;

/// <summary>The signed-in person, as the business layer sees them.</summary>
/// <param name="UserId">RMS_USERS.ID. Null only for the scheduler service account, which is not a row in RMS_USERS.</param>
public sealed record UserContext(int? UserId, string Username, string Code, string? Email, bool IsAdmin)
{
    /// <summary>
    /// The identity scheduled reports run under: an administrator (so any report can be scheduled) that is not
    /// an RMS user. @RMS_USERNAME is the configured name; @RMS_USER_ID, @RMS_USER_CODE and @RMS_USER_EMAIL are empty.
    /// </summary>
    public static UserContext ServiceAccount(string username) => new(null, username, "", null, IsAdmin: true);
}

/// <summary>Bound from the "Access" configuration section.</summary>
public sealed class AccessOptions
{
    /// <summary>AD group whose members may act as administrators (if also marked Admin in RMS_USERS).</summary>
    public string AdminGroup { get; set; } = "RMS_Admin";

    /// <summary>AD group whose members may view their assigned reports.</summary>
    public string UsersGroup { get; set; } = "RMS_Users";
}

/// <summary>Bound from the "Reporting" configuration section.</summary>
public sealed class ReportingOptions
{
    /// <summary>Connection string name used when an application has none of its own.</summary>
    public string DefaultConnectionName { get; set; } = "RmsDb";

    /// <summary>Rows shown in the on-screen grid. More are available through export.</summary>
    public int MaxPreviewRows { get; set; } = 5000;

    /// <summary>Upper limit for Excel and CSV exports.</summary>
    public int MaxExportRows { get; set; } = 100_000;

    /// <summary>Upper limit for PDF exports, which get unwieldy long before Excel does.</summary>
    public int MaxPdfRows { get; set; } = 10_000;

    public int CommandTimeoutSeconds { get; set; } = 120;

    /// <summary>Most entries a dropdown fed by a lookup query may hold.</summary>
    public int MaxLookupItems { get; set; } = 1000;

    /// <summary>Set to false to allow SELECT reports only.</summary>
    public bool AllowStoredProcedures { get; set; } = true;
}

/// <summary>
/// Bound from the "Scheduling" configuration section. The web application uses the first group of settings
/// to check schedules when they are saved; the scheduler service uses all of them and checks again before sending.
/// Keep the shared settings the same in both appsettings files.
/// </summary>
public sealed class SchedulingOptions
{
    /// <summary>Time zone new schedules start with (Windows or IANA id). Empty means this server's time zone.</summary>
    public string? DefaultTimeZone { get; set; }

    /// <summary>Email domains reports may be sent to, such as "company.com". Empty allows any domain.</summary>
    public List<string> AllowedEmailDomains { get; set; } = [];

    /// <summary>Folders (and their subfolders) reports may be saved to, such as \\fileserver\reports. Empty allows any folder.</summary>
    public List<string> AllowedShareRoots { get; set; } = [];

    /// <summary>Most To and Cc addresses one schedule may have.</summary>
    public int MaxRecipients { get; set; } = 50;

    // ---- Scheduler service only ----

    /// <summary>
    /// Name scheduled reports run under. It does not need to be an RMS user or a real account: it is what
    /// @RMS_USERNAME holds and what the run history shows. @RMS_USER_ID, @RMS_USER_CODE and @RMS_USER_EMAIL are empty.
    /// </summary>
    public string RunAsUsername { get; set; } = "svc_rms_scheduler";

    /// <summary>How often the service looks for schedules that are due.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>Most schedules the service runs at the same time.</summary>
    public int MaxParallelRuns { get; set; } = 2;

    /// <summary>A run taking longer than this is stopped and recorded as failed.</summary>
    public int RunTimeoutMinutes { get; set; } = 60;
}
