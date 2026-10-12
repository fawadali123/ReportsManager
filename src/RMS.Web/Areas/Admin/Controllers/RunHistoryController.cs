using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;

namespace RMS.Web.Areas.Admin.Controllers;

/// <summary>Read-only view of the audit trail: who ran or exported what, and how it went.</summary>
public sealed class RunHistoryController(IReportRunLogRepository runLog) : AdminController
{
    private const int PageSize = 200;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["PageSize"] = PageSize;
        return View(await runLog.GetRecentAsync(PageSize, ct));
    }
}
