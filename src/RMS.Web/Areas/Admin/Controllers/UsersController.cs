using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Services;
using RMS.Web.Areas.Admin.Models;
using RMS.Web.Security;

namespace RMS.Web.Areas.Admin.Controllers;

public sealed class UsersController(IUserRepository users, IUserReportRepository assignments) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new UserListViewModel
    {
        Users = await users.GetAllAsync(ct),
        AssignedReportCounts = await assignments.GetAssignmentCountsAsync(ct),
        CurrentUserId = User.GetUserId(),
    });

    [HttpGet]
    public IActionResult Create() => View("Edit", new UserEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(UserEditModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (!Validate(model))
        {
            return View("Edit", model);
        }

        var user = ToEntity(model);
        try
        {
            await users.CreateAsync(user, ct);
        }
        catch (DuplicateValueException ex)
        {
            AddDuplicateError(ex);
            return View("Edit", model);
        }

        Success($"Added user {user.AdUsername}. Assign reports to them next.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct);
        return user is null ? NotFound() : View(UserEditModel.From(user));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, UserEditModel model, CancellationToken ct)
    {
        model.Id = id;
        if (await users.GetByIdAsync(id, ct) is null)
        {
            return NotFound();
        }

        // Guard against locking yourself out: someone else has to demote or deactivate an administrator.
        if (id == User.GetUserId() && (!model.Active || !string.Equals(model.Role, Roles.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(string.Empty,
                "You cannot deactivate your own account or remove your own Admin role. Ask another administrator to do it.");
        }

        if (!Validate(model))
        {
            return View(model);
        }

        var user = ToEntity(model);
        try
        {
            await users.UpdateAsync(user, ct);
        }
        catch (DuplicateValueException ex)
        {
            AddDuplicateError(ex);
            return View(model);
        }

        Success($"Saved user {user.AdUsername}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id == User.GetUserId())
        {
            Failure("You cannot delete your own account.");
            return RedirectToAction(nameof(Index));
        }

        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
        {
            return NotFound();
        }

        await users.DeleteAsync(id, ct);
        Success($"Deleted user {user.AdUsername}. Their report assignments were removed; run history is kept.");
        return RedirectToAction(nameof(Index));
    }

    private bool Validate(UserEditModel model)
    {
        if (!Roles.IsValid(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Choose a role.");
        }
        return ModelState.IsValid;
    }

    private static AppUser ToEntity(UserEditModel model) => new()
    {
        Id = model.Id,
        Code = model.Code,
        FullName = model.FullName,
        Email = model.Email,
        // "CORP\name" and "name@corp" are stored as plain "name", which is what sign-in compares against.
        AdUsername = SignInService.NormalizeUsername(model.AdUsername),
        Role = Roles.Normalize(model.Role),
        Active = model.Active,
    };

    private void AddDuplicateError(DuplicateValueException ex)
    {
        if (ex.ConstraintName.Contains("ADUSER", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(UserEditModel.AdUsername), "Another user already has this AD username.");
        }
        else
        {
            ModelState.AddModelError(nameof(UserEditModel.Code), "Another user already has this code.");
        }
    }
}
