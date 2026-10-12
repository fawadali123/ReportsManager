using System.ComponentModel.DataAnnotations;
using RMS.Core.Domain;

namespace RMS.Web.Areas.Admin.Models;

// ---------------------------------------------------------------- Users

public sealed class UserListViewModel
{
    public IReadOnlyList<AppUser> Users { get; set; } = [];
    public IReadOnlyDictionary<int, int> AssignedReportCounts { get; set; } = new Dictionary<int, int>();
    public int? CurrentUserId { get; set; }
}

public sealed class UserEditModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a code.")]
    [StringLength(50)]
    public string Code { get; set; } = "";

    [Display(Name = "Full name")]
    [StringLength(200)]
    public string? FullName { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Enter the AD username.")]
    [Display(Name = "AD username")]
    [StringLength(100)]
    public string AdUsername { get; set; } = "";

    [Required]
    public string Role { get; set; } = Roles.Viewer;

    public bool Active { get; set; } = true;

    public static UserEditModel From(AppUser user) => new()
    {
        Id = user.Id,
        Code = user.Code,
        FullName = user.FullName,
        Email = user.Email,
        AdUsername = user.AdUsername,
        Role = user.Role,
        Active = user.Active,
    };
}

// ---------------------------------------------------------------- Applications

public sealed class ApplicationEditModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a code.")]
    [StringLength(50)]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Display(Name = "Database connection")]
    [StringLength(100)]
    public string? ConnectionName { get; set; }

    public bool Active { get; set; } = true;

    // Filled by the controller for the dropdown; not posted back.
    public IReadOnlyList<string> AvailableConnections { get; set; } = [];
    public IReadOnlyDictionary<string, DatabaseKind> ConnectionKinds { get; set; } = new Dictionary<string, DatabaseKind>();
    public string DefaultConnectionName { get; set; } = "";
    public DatabaseKind DefaultConnectionKind { get; set; }

    public static ApplicationEditModel From(ApplicationInfo application) => new()
    {
        Id = application.Id,
        Code = application.Code,
        Name = application.Name,
        Description = application.Description,
        ConnectionName = application.ConnectionName,
        Active = application.Active,
    };

    public ApplicationInfo ToEntity() => new()
    {
        Id = Id,
        Code = Code,
        Name = Name,
        Description = Description,
        ConnectionName = string.IsNullOrWhiteSpace(ConnectionName) ? null : ConnectionName,
        Active = Active,
    };
}

// ---------------------------------------------------------------- Categories

public sealed class CategoryEditModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a code.")]
    [StringLength(50)]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool Active { get; set; } = true;

    public static CategoryEditModel From(ReportCategory category) => new()
    {
        Id = category.Id,
        Code = category.Code,
        Name = category.Name,
        Description = category.Description,
        Active = category.Active,
    };

    public ReportCategory ToEntity() => new()
    {
        Id = Id,
        Code = Code,
        Name = Name,
        Description = Description,
        Active = Active,
    };
}

// ---------------------------------------------------------------- Report definitions

public sealed class ReportParameterEditModel
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(200)]
    public string? Label { get; set; }

    public ParameterDataType DataType { get; set; } = ParameterDataType.Text;
    public bool Required { get; set; } = true;
    public bool AllowMultiple { get; set; }

    [StringLength(500)]
    public string? DefaultValue { get; set; }

    public string? LookupSource { get; set; }

    [StringLength(500)]
    public string? HelpText { get; set; }

    public int SortOrder { get; set; }

    public static ReportParameterEditModel From(ReportParameter parameter) => new()
    {
        Name = parameter.Name,
        Label = parameter.Label,
        DataType = parameter.DataType,
        Required = parameter.Required,
        AllowMultiple = parameter.AllowMultiple,
        DefaultValue = parameter.DefaultValue,
        LookupSource = parameter.LookupSource,
        HelpText = parameter.HelpText,
        SortOrder = parameter.SortOrder,
    };

    public ReportParameter ToEntity() => new()
    {
        Name = Name.Trim(),
        Label = Label?.Trim() ?? "",
        DataType = DataType,
        Required = Required,
        AllowMultiple = AllowMultiple,
        DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue.Trim(),
        LookupSource = string.IsNullOrWhiteSpace(LookupSource) ? null : LookupSource.Trim(),
        HelpText = string.IsNullOrWhiteSpace(HelpText) ? null : HelpText.Trim(),
        SortOrder = SortOrder,
    };
}

/// <summary>Where an application's reports run: the connection name and the database product behind it.</summary>
public sealed record ApplicationDatabase(string ConnectionName, DatabaseKind Kind);

public sealed class ReportEditModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a code.")]
    [StringLength(50)]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose an application.")]
    [Display(Name = "Application")]
    public int ApplicationId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Display(Name = "Type")]
    public ReportQueryType QueryType { get; set; } = ReportQueryType.Select;

    [Required(ErrorMessage = "Enter the SQL for this report.")]
    [Display(Name = "SQL")]
    public string SqlQuery { get; set; } = "";

    public bool Active { get; set; } = true;

    public List<ReportParameterEditModel> Parameters { get; set; } = [];

    // Filled by the controller for the dropdowns; not posted back.
    public IReadOnlyList<ApplicationInfo> Applications { get; set; } = [];
    public IReadOnlyList<ReportCategory> Categories { get; set; } = [];
    public IReadOnlyDictionary<int, ApplicationDatabase> ApplicationDatabases { get; set; } = new Dictionary<int, ApplicationDatabase>();
    public bool StoredProceduresAllowed { get; set; } = true;

    public static ReportEditModel From(Report report) => new()
    {
        Id = report.Id,
        Code = report.Code,
        Name = report.Name,
        Description = report.Description,
        ApplicationId = report.ApplicationId,
        CategoryId = report.CategoryId,
        QueryType = report.QueryType,
        SqlQuery = report.SqlQuery,
        Active = report.Active,
    };

    public Report ToEntity() => new()
    {
        Id = Id,
        Code = Code,
        Name = Name,
        Description = Description,
        ApplicationId = ApplicationId,
        CategoryId = CategoryId,
        QueryType = QueryType,
        SqlQuery = SqlQuery,
        Active = Active,
    };
}

// ---------------------------------------------------------------- Assignments

public sealed class AssignmentReportItem
{
    public int ReportId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Available { get; set; }
    public bool Assigned { get; set; }
}

public sealed class AssignmentCategoryGroup
{
    public string CategoryName { get; set; } = "";
    public List<AssignmentReportItem> Reports { get; set; } = [];
}

public sealed class AssignmentApplicationGroup
{
    public int ApplicationId { get; set; }
    public string ApplicationName { get; set; } = "";
    public List<AssignmentCategoryGroup> Categories { get; set; } = [];
}

public sealed class AssignmentsViewModel
{
    public IReadOnlyList<AppUser> Users { get; set; } = [];
    public AppUser? SelectedUser { get; set; }
    public List<AssignmentApplicationGroup> Applications { get; set; } = [];
    public int TotalReports { get; set; }
}
