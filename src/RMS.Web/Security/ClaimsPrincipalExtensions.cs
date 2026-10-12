using System.Globalization;
using System.Security.Claims;
using RMS.Core.Domain;
using RMS.Core.Services;

namespace RMS.Web.Security;

public static class RmsClaimTypes
{
    public const string Code = "rms:code";
    public const string DisplayName = "rms:display_name";
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>RMS_USERS.ID of the signed-in user, or null when nobody is signed in.</summary>
    public static int? GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(Roles.Admin);

    public static string GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(RmsClaimTypes.DisplayName) ?? principal.Identity?.Name ?? "";

    public static string GetRole(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Role) ?? Roles.Viewer;

    /// <summary>The signed-in user in the shape the business layer expects.</summary>
    public static UserContext ToUserContext(this ClaimsPrincipal principal) => new(
        principal.GetUserId() ?? throw new InvalidOperationException("No user is signed in."),
        principal.Identity?.Name ?? "",
        principal.FindFirstValue(RmsClaimTypes.Code) ?? "",
        principal.FindFirstValue(ClaimTypes.Email),
        principal.IsAdmin());

    public static ClaimsPrincipal CreatePrincipal(AppUser user, string role, string? displayName, string authenticationScheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.AdUsername),
            new(ClaimTypes.Role, role),
            new(RmsClaimTypes.Code, user.Code),
            new(RmsClaimTypes.DisplayName, string.IsNullOrWhiteSpace(displayName) ? user.AdUsername : displayName),
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
