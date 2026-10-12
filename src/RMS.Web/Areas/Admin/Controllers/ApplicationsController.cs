using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Web.Areas.Admin.Models;

namespace RMS.Web.Areas.Admin.Controllers;

public sealed class ApplicationsController(IApplicationRepository applications, IReportConnectionCatalog connections)
    : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var all = await applications.GetAllAsync(ct);
        ViewData["DefaultConnection"] = connections.DefaultConnectionName;
        ViewData["Databases"] = all.ToDictionary(a => a.Id, a => connections.GetKind(a.ConnectionName).DisplayName());
        return View(all);
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", WithConnections(new ApplicationEditModel()));

    [HttpPost]
    public async Task<IActionResult> Create(ApplicationEditModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (!ModelState.IsValid)
        {
            return View("Edit", WithConnections(model));
        }

        try
        {
            await applications.CreateAsync(model.ToEntity(), ct);
        }
        catch (DuplicateValueException)
        {
            ModelState.AddModelError(nameof(model.Code), "Another application already has this code.");
            return View("Edit", WithConnections(model));
        }

        Success($"Added application {model.Name}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var application = await applications.GetByIdAsync(id, ct);
        return application is null ? NotFound() : View(WithConnections(ApplicationEditModel.From(application)));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ApplicationEditModel model, CancellationToken ct)
    {
        model.Id = id;
        if (await applications.GetByIdAsync(id, ct) is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(WithConnections(model));
        }

        try
        {
            await applications.UpdateAsync(model.ToEntity(), ct);
        }
        catch (DuplicateValueException)
        {
            ModelState.AddModelError(nameof(model.Code), "Another application already has this code.");
            return View(WithConnections(model));
        }

        Success($"Saved application {model.Name}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var application = await applications.GetByIdAsync(id, ct);
        if (application is null)
        {
            return NotFound();
        }

        try
        {
            await applications.DeleteAsync(id, ct);
            Success($"Deleted application {application.Name}.");
        }
        catch (RecordInUseException)
        {
            Failure($"{application.Name} still has reports, so it cannot be deleted. Move or delete its reports first, or mark it inactive.");
        }

        return RedirectToAction(nameof(Index));
    }

    private ApplicationEditModel WithConnections(ApplicationEditModel model)
    {
        model.AvailableConnections = connections.ConnectionNames;
        model.ConnectionKinds = model.AvailableConnections.ToDictionary(name => name, connections.GetKind, StringComparer.OrdinalIgnoreCase);
        model.DefaultConnectionName = connections.DefaultConnectionName;
        model.DefaultConnectionKind = connections.GetKind(null);
        return model;
    }
}
