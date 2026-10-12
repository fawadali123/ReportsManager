using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RMS.Core.Abstractions;

namespace RMS.Infrastructure.ActiveDirectory;

/// <summary>
/// Stand-in for Active Directory on a developer machine. Accounts, passwords and group memberships
/// come from Authentication:DevelopmentUsers. Registration refuses to use this class outside the
/// Development environment.
/// </summary>
internal sealed class DevelopmentDirectoryAuthenticator(IOptions<DirectoryOptions> options) : IDirectoryAuthenticator
{
    public Task<DirectoryAuthResult> AuthenticateAsync(
        string username, string password, IReadOnlyCollection<string> groups, CancellationToken ct = default)
    {
        var account = options.Value.DevelopmentUsers
            .FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (account is null || account.Password.Length == 0 || !SamePassword(account.Password, password))
        {
            return Task.FromResult(new DirectoryAuthResult(DirectoryAuthStatus.InvalidCredentials));
        }

        var memberOf = groups.Where(g => account.Groups.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(new DirectoryAuthResult(
            DirectoryAuthStatus.Success, account.Username, account.DisplayName, account.Email, memberOf));
    }

    private static bool SamePassword(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
