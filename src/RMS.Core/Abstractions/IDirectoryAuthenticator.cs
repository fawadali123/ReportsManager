namespace RMS.Core.Abstractions;

public enum DirectoryAuthStatus
{
    Success,
    InvalidCredentials,

    /// <summary>The directory could not be reached or returned an unexpected error.</summary>
    Unavailable,
}

/// <param name="SamAccountName">Account name as the directory spells it.</param>
/// <param name="MemberOf">The subset of the requested groups the user belongs to, directly or through nested groups.</param>
/// <param name="Error">Technical detail for the log. Never shown to the user.</param>
public sealed record DirectoryAuthResult(
    DirectoryAuthStatus Status,
    string? SamAccountName = null,
    string? DisplayName = null,
    string? Email = null,
    IReadOnlyCollection<string>? MemberOf = null,
    string? Error = null)
{
    public bool IsMemberOf(string group) =>
        MemberOf is not null && MemberOf.Contains(group, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Validates a username and password against the corporate directory and reports group membership.
/// The default implementation talks LDAP to Active Directory; swap it to plug in another identity source.
/// </summary>
public interface IDirectoryAuthenticator
{
    /// <param name="username">Account name without domain.</param>
    /// <param name="groups">Group names whose membership should be checked.</param>
    Task<DirectoryAuthResult> AuthenticateAsync(
        string username,
        string password,
        IReadOnlyCollection<string> groups,
        CancellationToken ct = default);
}
