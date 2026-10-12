namespace RMS.Core.Services;

/// <summary>The signed-in person, as the business layer sees them.</summary>
public sealed record UserContext(int UserId, string Username, string Code, string? Email, bool IsAdmin);

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
