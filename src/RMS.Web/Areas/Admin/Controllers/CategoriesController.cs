using Microsoft.AspNetCore.Mvc;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Web.Areas.Admin.Models;

namespace RMS.Web.Areas.Admin.Controllers;

public sealed class CategoriesController(IReportCategoryRepository categories) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await categories.GetAllAsync(ct));

    [HttpGet]
    public IActionResult Create() => View("Edit", new CategoryEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(CategoryEditModel model, CancellationToken ct)
    {
        model.Id = 0;
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        try
        {
            await categories.CreateAsync(model.ToEntity(), ct);
        }
        catch (DuplicateValueException)
        {
            ModelState.AddModelError(nameof(model.Code), "Another category already has this code.");
            return View("Edit", model);
        }

        Success($"Added category {model.Name}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var category = await categories.GetByIdAsync(id, ct);
        return category is null ? NotFound() : View(CategoryEditModel.From(category));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, CategoryEditModel model, CancellationToken ct)
    {
        model.Id = id;
        if (await categories.GetByIdAsync(id, ct) is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await categories.UpdateAsync(model.ToEntity(), ct);
        }
        catch (DuplicateValueException)
        {
            ModelState.AddModelError(nameof(model.Code), "Another category already has this code.");
            return View(model);
        }

        Success($"Saved category {model.Name}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var category = await categories.GetByIdAsync(id, ct);
        if (category is null)
        {
            return NotFound();
        }

        try
        {
            await categories.DeleteAsync(id, ct);
            Success($"Deleted category {category.Name}.");
        }
        catch (RecordInUseException)
        {
            Failure($"{category.Name} still has reports, so it cannot be deleted. Move or delete its reports first, or mark it inactive.");
        }

        return RedirectToAction(nameof(Index));
    }
}
