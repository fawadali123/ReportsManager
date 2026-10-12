using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RMS.Web.Security;

namespace RMS.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for every maintenance screen. The policy is on the base class so a new admin
/// controller cannot be left open by forgetting an attribute.
/// </summary>
[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public abstract class AdminController : Controller
{
    /// <summary>Message shown once, at the top of the next page.</summary>
    protected void Success(string message) => TempData["Success"] = message;

    protected void Failure(string message) => TempData["Error"] = message;
}
