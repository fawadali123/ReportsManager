using System.ComponentModel.DataAnnotations;
using RMS.Core.Domain;

namespace RMS.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your username.")]
    [Display(Name = "Username")]
    [StringLength(256)]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; set; }
}

public sealed class DashboardViewModel
{
    public int Users { get; set; }
    public int ActiveUsers { get; set; }
    public int Applications { get; set; }
    public int Categories { get; set; }
    public int Reports { get; set; }
    public int ActiveReports { get; set; }
    public IReadOnlyList<ReportRunLog> RecentRuns { get; set; } = [];
}

public sealed record ExportFormat(string Format, string DisplayName);

public sealed class ReportsPageViewModel
{
    public IReadOnlyList<ReportCatalogItem> Catalog { get; set; } = [];
    public int? SelectedReportId { get; set; }
    public IReadOnlyList<ExportFormat> Exporters { get; set; } = [];
    public int MaxPreviewRows { get; set; }
    public bool IsAdmin { get; set; }
}
