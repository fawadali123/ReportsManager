using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Core.Services;

public enum SignInStatus
{
    Success,
    InvalidCredentials,
    DirectoryUnavailable,

    /// <summary>The AD account is valid but has no row in RMS_USERS.</summary>
    NotRegistered,

    /// <summary>The RMS_USERS row is marked inactive.</summary>
    Inactive,

    /// <summary>The account is in neither the admin nor the users AD group.</summary>
    NotInGroup,
}

/// <param name="Role">The role granted for this session; see <see cref="SignInService.ResolveRole"/>.</param>
/// <param name="Message">Text that is safe to show on the login page.</param>
/// <param name="Detail">Technical detail for the log only.</param>
public sealed record SignInResult(
    SignInStatus Status,
    string Message,
    AppUser? User = null,
    string? Role = null,
    string? DisplayName = null,
    string? Detail = null)
{
    public bool Succeeded => Status == SignInStatus.Success;
}

/// <summary>
/// The sign-in rules: the password must be right in Active Directory, the account must be an active
/// row in RMS_USERS, and AD group membership decides which role the session gets.
/// </summary>
public sealed class SignInService(IDirectoryAuthenticator directory, IUserRepository users, AccessOptions access)
{
    public async Task<SignInResult> SignInAsync(string? username, string? password, CancellationToken ct = default)
    {
        var account = NormalizeUsername(username);

        // An empty password must never reach the directory: LDAP treats it as an anonymous bind, which succeeds.
        if (account.Length == 0 || string.IsNullOrEmpty(password))
        {
            return new SignInResult(SignInStatus.InvalidCredentials, "Enter your username and password.");
        }

        // The directory is asked first so that nothing about RMS_USERS is revealed to someone without a valid password.
        var auth = await directory.AuthenticateAsync(account, password, [access.AdminGroup, access.UsersGroup], ct);
        switch (auth.Status)
        {
            case DirectoryAuthStatus.InvalidCredentials:
                return new SignInResult(SignInStatus.InvalidCredentials, "The username or password is not correct.", Detail: auth.Error);
            case DirectoryAuthStatus.Unavailable:
                return new SignInResult(SignInStatus.DirectoryUnavailable,
                    "Sign-in is not available right now because Active Directory could not be reached. Try again shortly.",
                    Detail: auth.Error);
        }

        var user = await users.GetByAdUsernameAsync(auth.SamAccountName ?? account, ct);
        if (user is null)
        {
            return new SignInResult(SignInStatus.NotRegistered,
                "Your account is not set up in Reports Management. Ask an administrator to add you.");
        }

        if (!user.Active)
        {
            return new SignInResult(SignInStatus.Inactive,
                "Your Reports Management account is inactive. Ask an administrator to reactivate it.", user);
        }

        var role = ResolveRole(user.Role, auth.IsMemberOf(access.AdminGroup), auth.IsMemberOf(access.UsersGroup));
        if (role is null)
        {
            return new SignInResult(SignInStatus.NotInGroup,
                $"Your account is not a member of the {access.UsersGroup} or {access.AdminGroup} group in Active Directory.", user);
        }

        var displayName = FirstNonEmpty(user.FullName, auth.DisplayName, user.AdUsername);
        return new SignInResult(SignInStatus.Success, "Signed in.", user, role, displayName);
    }

    /// <summary>
    /// Decides the role for a session, or null when sign-in must be refused.
    /// <list type="bullet">
    /// <item>Admin needs both: Role = Admin in RMS_USERS and membership of the admin AD group.</item>
    /// <item>Anyone else who is in the users group or the admin group signs in as Viewer.</item>
    /// <item>No group membership means no access.</item>
    /// </list>
    /// </summary>
    public static string? ResolveRole(string? databaseRole, bool inAdminGroup, bool inUsersGroup)
    {
        if (inAdminGroup && string.Equals(databaseRole, Roles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.Admin;
        }

        return inAdminGroup || inUsersGroup ? Roles.Viewer : null;
    }

    /// <summary>Accepts "user", "DOMAIN\user" and "user@domain" and returns just "user".</summary>
    public static string NormalizeUsername(string? input)
    {
        var text = (input ?? "").Trim();

        var slash = text.LastIndexOf('\\');
        if (slash >= 0)
        {
            text = text[(slash + 1)..];
        }

        var at = text.IndexOf('@');
        if (at > 0)
        {
            text = text[..at];
        }

        return text.Trim();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
