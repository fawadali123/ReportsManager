using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RMS.Core.Services;
using RMS.Web.Models;
using RMS.Web.Security;

namespace RMS.Web.Controllers;

public sealed class AccountController(SignInService signIn, ILogger<AccountController> logger) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await signIn.SignInAsync(model.Username, model.Password, ct);
        if (!result.Succeeded)
        {
            logger.LogWarning("Sign-in refused for {Username}: {Status}. {Detail}",
                SignInService.NormalizeUsername(model.Username), result.Status, result.Detail);

            ModelState.AddModelError(string.Empty, result.Message);
            model.Password = "";
            return View(model);
        }

        var principal = ClaimsPrincipalExtensions.CreatePrincipal(
            result.User!, result.Role!, result.DisplayName, CookieAuthenticationDefaults.AuthenticationScheme);

        // A session cookie: it goes when the browser closes, and expires after Authentication:SessionMinutes of inactivity.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        logger.LogInformation("{Username} signed in as {Role}.", result.User!.AdUsername, result.Role);
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        logger.LogInformation("{Username} signed out.", username);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>Only ever redirects inside this site, so a crafted returnUrl cannot send people elsewhere.</summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
