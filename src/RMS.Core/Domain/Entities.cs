namespace RMS.Core.Domain;

/// <summary>A person allowed to sign in. Maps to RMS_USERS.</summary>
public sealed class AppUser
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string? FullName { get; set; }
    public string? Email { get; set; }

    /// <summary>The sAMAccountName, without domain (for example "fawad.ali").</summary>
    public string AdUsername { get; set; } = "";

    public bool Active { get; set; } = true;
    public string Role { get; set; } = Roles.Viewer;
}

/// <summary>A source system that reports belong to. Maps to RMS_APPLICATIONS.</summary>
public sealed class ApplicationInfo
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Active { get; set; } = true;

    /// <summary>
    /// Name of the connection string its reports run against. Null means the default report connection.
    /// Only the name is stored; the connection string itself stays in configuration.
    /// </summary>
    public string? ConnectionName { get; set; }
}

/// <summary>Maps to RMS_REPORT_CATEGORIES.</summary>
public sealed class ReportCategory
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>How the text in RMS_REPORTS.SQL_QUERY is executed.</summary>
public enum ReportQueryType
{
    /// <summary>A single SELECT (or WITH ... SELECT) statement.</summary>
    Select = 0,

    /// <summary>A procedure call that returns its rows through a SYS_REFCURSOR OUT argument.</summary>
    StoredProcedure = 1,
}

/// <summary>A report definition. Maps to RMS_REPORTS.</summary>
public sealed class Report
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int ApplicationId { get; set; }
    public int CategoryId { get; set; }
    public string SqlQuery { get; set; } = "";
    public ReportQueryType QueryType { get; set; } = ReportQueryType.Select;
    public bool Active { get; set; } = true;

    /// <summary>Optional presentation settings for parameters found in <see cref="SqlQuery"/>.</summary>
    public List<ReportParameter> Parameters { get; set; } = [];

    // Read-only values joined in by the repository.
    public string? ApplicationName { get; set; }
    public bool ApplicationActive { get; set; } = true;
    public string? ConnectionName { get; set; }
    public string? CategoryName { get; set; }
    public bool CategoryActive { get; set; } = true;

    /// <summary>True when the report, its application and its category are all active.</summary>
    public bool IsAvailable => Active && ApplicationActive && CategoryActive;
}

public enum ParameterDataType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Date = 3,
    DateTime = 4,
    Boolean = 5,
}

/// <summary>
/// Describes how one @parameter is presented and validated. Maps to RMS_REPORT_PARAMETERS.
/// Rows are optional: a parameter with no row is shown as a required text box.
/// </summary>
public sealed class ReportParameter
{
    public int Id { get; set; }
    public int ReportId { get; set; }

    /// <summary>Name as written in the SQL, without the leading @.</summary>
    public string Name { get; set; } = "";

    public string Label { get; set; } = "";
    public ParameterDataType DataType { get; set; } = ParameterDataType.Text;
    public bool Required { get; set; } = true;

    /// <summary>When true the user may pick several values; write the SQL as <c>IN (@Name)</c>.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>A literal value, or for dates a token such as TODAY, START_OF_MONTH or TODAY-7.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Turns the field into a dropdown. Either a SELECT returning value and label columns,
    /// or a fixed list written as <c>value=Label</c> entries separated by semicolons or new lines.
    /// </summary>
    public string? LookupSource { get; set; }

    public string? HelpText { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>One line of the audit trail. Maps to RMS_REPORT_RUN_LOG.</summary>
public sealed class ReportRunLog
{
    public long Id { get; set; }

    /// <summary>Null once the report has been deleted; <see cref="ReportName"/> keeps what it was called.</summary>
    public int? ReportId { get; set; }
    public string? ReportName { get; set; }

    /// <summary>Null once the user has been deleted; <see cref="Username"/> keeps who it was.</summary>
    public int? UserId { get; set; }
    public string Username { get; set; } = "";
    public DateTime RunAt { get; set; }

    /// <summary>"View" or "Export:xlsx", "Export:pdf", "Export:csv".</summary>
    public string Action { get; set; } = "View";

    public long DurationMs { get; set; }
    public int? RowCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Parameter values as entered, serialised as JSON.</summary>
    public string? Parameters { get; set; }
}

/// <summary>A report as it appears in the Application / Category / Report pickers.</summary>
public sealed class ReportCatalogItem
{
    public int ReportId { get; set; }
    public string ReportCode { get; set; } = "";
    public string ReportName { get; set; } = "";
    public string? Description { get; set; }
    public int ApplicationId { get; set; }
    public string ApplicationName { get; set; } = "";
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";

    /// <summary>False when the report, its application or its category is inactive. Only administrators see those.</summary>
    public bool Available { get; set; } = true;
}
