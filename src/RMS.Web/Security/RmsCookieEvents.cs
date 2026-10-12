using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Web.Security;

/// <summary>
/// Keeps a signed-in session honest: every couple of minutes the user's row is re-read, and the
/// session ends if the account was deactivated, deleted or demoted in the meantime.
/// Also answers AJAX calls with 401/403 instead of redirecting them to an HTML page.
/// </summary>
public sealed class RmsCookieEvents(IUserRepository users, TimeProvider clock, ILogger<RmsCookieEvents> logger)
    : CookieAuthenticationEvents
{
    private const string LastCheckedKey = "rms.checked";
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(2);

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var now = clock.GetUtcNow();
        if (context.Properties.Items.TryGetValue(LastCheckedKey, out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            && now.UtcTicks - ticks < RecheckInterval.Ticks)
        {
            return;
        }

        var principal = context.Principal;
        var userId = principal?.GetUserId();
        if (principal is null || userId is null)
        {
            await RejectAsync(context);
            return;
        }

        AppUser? user;
        try
        {
            user = await users.GetByIdAsync(userId.Value, context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database is unreachable. Keep the session for now and check again on the next request,
            // so the error page can still render instead of failing inside authentication.
            logger.LogError(ex, "Could not re-validate the session of user {UserId}.", userId);
            return;
        }

        var stillAdmin = !principal.IsAdmin() || string.Equals(user?.Role, Roles.Admin, StringComparison.OrdinalIgnoreCase);
        if (user is null || !user.Active || !stillAdmin)
        {
            logger.LogInformation("Ending the session of user {UserId}: account removed, deactivated or demoted.", userId);
            await RejectAsync(context);
            return;
        }

        context.Properties.Items[LastCheckedKey] = now.UtcTicks.ToString(CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsAjax(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        return base.RedirectToLogin(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsAjax(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return base.RedirectToAccessDenied(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static bool IsAjax(HttpRequest request) =>
        string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
