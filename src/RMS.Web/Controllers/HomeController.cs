using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Web.Models;
using RMS.Web.Security;

namespace RMS.Web.Controllers;

public sealed class HomeController(
    IUserRepository users,
    IApplicationRepository applications,
    IReportCategoryRepository categories,
    IReportRepository reports,
    IReportRunLogRepository runLog) : Controller
{
    /// <summary>Administrators land on an overview; viewers go straight to their reports.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!User.IsAdmin())
        {
            return RedirectToAction("Index", "Reports");
        }

        var allUsers = await users.GetAllAsync(ct);
        var allReports = await reports.GetAllAsync(ct);

        return View(new DashboardViewModel
        {
            Users = allUsers.Count,
            ActiveUsers = allUsers.Count(u => u.Active),
            Applications = (await applications.GetAllAsync(ct)).Count,
            Categories = (await categories.GetAllAsync(ct)).Count,
            Reports = allReports.Count,
            ActiveReports = allReports.Count(r => r.IsAvailable),
            RecentRuns = await runLog.GetRecentAsync(8, ct),
        });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    /// <summary>Friendly page for bare status codes such as 404.</summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int id) => View(id);
}
