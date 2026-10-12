using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Web.Areas.Admin.Models;

namespace RMS.Web.Areas.Admin.Controllers;

/// <summary>Assigns reports to a user, and removes them again.</summary>
public sealed class AssignmentsController(
    IUserRepository users,
    IReportRepository reports,
    IUserReportRepository assignments) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(int? userId, CancellationToken ct)
    {
        var model = new AssignmentsViewModel { Users = await users.GetAllAsync(ct) };
        if (userId is null)
        {
            return View(model);
        }

        model.SelectedUser = model.Users.FirstOrDefault(u => u.Id == userId);
        if (model.SelectedUser is null)
        {
            return NotFound();
        }

        var assigned = (await assignments.GetReportIdsAsync(userId.Value, ct)).ToHashSet();
        var catalog = await reports.GetCatalogAsync(userId: null, includeUnavailable: true, ct);

        model.TotalReports = catalog.Count;
        model.Applications = [.. catalog
            .GroupBy(r => (r.ApplicationId, r.ApplicationName))
            .Select(application => new AssignmentApplicationGroup
            {
                ApplicationId = application.Key.ApplicationId,
                ApplicationName = application.Key.ApplicationName,
                Categories = [.. application
                    .GroupBy(r => r.CategoryName)
                    .Select(category => new AssignmentCategoryGroup
                    {
                        CategoryName = category.Key,
                        Reports = [.. category.Select(r => new AssignmentReportItem
                        {
                            ReportId = r.ReportId,
                            Code = r.ReportCode,
                            Name = r.ReportName,
                            Description = r.Description,
                            Available = r.Available,
                            Assigned = assigned.Contains(r.ReportId),
                        })],
                    })],
            })];

        return View(model);
    }

    /// <param name="reportIds">The complete set of reports the user should have after saving.</param>
    [HttpPost]
    public async Task<IActionResult> Save(int userId, int[]? reportIds, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return NotFound();
        }

        // Ignore anything that is not a real report Id, whatever the form posted.
        var known = (await reports.GetCatalogAsync(userId: null, includeUnavailable: true, ct)).Select(r => r.ReportId).ToHashSet();
        var wanted = (reportIds ?? []).Where(known.Contains).Distinct().ToList();

        await assignments.SetAssignmentsAsync(userId, wanted, User.Identity?.Name ?? "", ct);

        Success(wanted.Count == 1
            ? $"{user.AdUsername} now has 1 report."
            : $"{user.AdUsername} now has {wanted.Count} reports.");
        return RedirectToAction(nameof(Index), new { userId });
    }
}
