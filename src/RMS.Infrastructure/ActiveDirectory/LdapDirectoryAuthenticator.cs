using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RMS.Core.Abstractions;

namespace RMS.Infrastructure.ActiveDirectory;

/// <summary>
/// Signs users in against Active Directory over LDAP.
/// The user's own credentials are used for the bind, so no service account is needed:
/// a successful bind proves the password, and the same connection is then used to read
/// the user's entry and check group membership.
/// </summary>
internal sealed class LdapDirectoryAuthenticator(
    IOptions<DirectoryOptions> options,
    ILogger<LdapDirectoryAuthenticator> logger) : IDirectoryAuthenticator
{
    private const int InvalidCredentials = 49;

    /// <summary>LDAP_MATCHING_RULE_IN_CHAIN: makes memberOf follow nested groups on the server side.</summary>
    private const string InChain = "1.2.840.113556.1.4.1941";

    public Task<DirectoryAuthResult> AuthenticateAsync(
        string username, string password, IReadOnlyCollection<string> groups, CancellationToken ct = default) =>
        // System.DirectoryServices.Protocols is synchronous; keep it off the request thread.
        Task.Run(() => Authenticate(username, password, groups), ct);

    private DirectoryAuthResult Authenticate(string username, string password, IReadOnlyCollection<string> groups)
    {
        var ldap = options.Value.Ldap;

        // Never bind with an empty password: LDAP servers treat that as an anonymous bind and report success.
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new DirectoryAuthResult(DirectoryAuthStatus.InvalidCredentials, Error: "Empty username or password.");
        }

        if (string.IsNullOrWhiteSpace(ldap.Server))
        {
            logger.LogError("Authentication:Ldap:Server is not configured.");
            return new DirectoryAuthResult(DirectoryAuthStatus.Unavailable, Error: "LDAP server is not configured.");
        }

        try
        {
            using var connection = Bind(ldap, username, password);

            var searchBase = string.IsNullOrWhiteSpace(ldap.SearchBase) ? ReadDefaultNamingContext(connection) : ldap.SearchBase;
            var user = FindOne(connection, searchBase,
                $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={EscapeFilter(username)}))",
                "sAMAccountName", "displayName", "mail");

            if (user is null)
            {
                logger.LogError("User {Username} authenticated but was not found under search base {SearchBase}.", username, searchBase);
                return new DirectoryAuthResult(DirectoryAuthStatus.Unavailable, Error: "User entry not found under the search base.");
            }

            var memberOf = groups.Where(group => IsMember(connection, searchBase, user.DistinguishedName, group)).ToList();

            return new DirectoryAuthResult(
                DirectoryAuthStatus.Success,
                SamAccountName: Attribute(user, "sAMAccountName") ?? username,
                DisplayName: Attribute(user, "displayName"),
                Email: Attribute(user, "mail"),
                MemberOf: memberOf);
        }
        catch (LdapException ex) when (ex.ErrorCode == InvalidCredentials)
        {
            // ServerErrorMessage carries AD's reason code (52e wrong password, 775 locked out, 532 password expired...).
            logger.LogInformation("Directory rejected sign-in for {Username}: {Reason}", username, ex.ServerErrorMessage ?? ex.Message);
            return new DirectoryAuthResult(DirectoryAuthStatus.InvalidCredentials, Error: ex.ServerErrorMessage ?? ex.Message);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException or DirectoryException or PlatformNotSupportedException)
        {
            logger.LogError(ex, "Directory sign-in failed for {Username} against {Server}.", username, ldap.Server);
            return new DirectoryAuthResult(DirectoryAuthStatus.Unavailable, Error: ex.Message);
        }
    }

    private static LdapConnection Bind(LdapOptions ldap, string username, string password)
    {
        var port = ldap.Port > 0 ? ldap.Port : ldap.UseSsl ? 636 : 389;
        var servers = ldap.Server.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var identifier = new LdapDirectoryIdentifier(servers, port, fullyQualifiedDnsHostName: false, connectionless: false);

        var connection = new LdapConnection(identifier) { Timeout = TimeSpan.FromSeconds(Math.Max(1, ldap.TimeoutSeconds)) };
        try
        {
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

            if (ldap.UseSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
                if (ldap.IgnoreCertificateErrors && OperatingSystem.IsWindows())
                {
                    connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
                }
            }

            if (string.Equals(ldap.AuthType, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                if (!ldap.UseSsl)
                {
                    throw new DirectoryException(
                        "Authentication:Ldap:AuthType is Basic but UseSsl is off. A simple bind would send the password in clear text; turn on UseSsl or use Negotiate.");
                }

                connection.AuthType = AuthType.Basic;
                connection.Credential = new NetworkCredential(BasicBindName(ldap, username), password);
            }
            else
            {
                connection.AuthType = AuthType.Negotiate;
                connection.Credential = new NetworkCredential(username, password, ldap.Domain);

                if (!ldap.UseSsl && OperatingSystem.IsWindows())
                {
                    // Domain controllers commonly require signed LDAP; sealing also encrypts the session.
                    connection.SessionOptions.Signing = true;
                    connection.SessionOptions.Sealing = true;
                }
            }

            connection.Bind();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string BasicBindName(LdapOptions ldap, string username)
    {
        if (!string.IsNullOrWhiteSpace(ldap.UpnSuffix))
        {
            return $"{username}@{ldap.UpnSuffix.TrimStart('@')}";
        }
        return string.IsNullOrWhiteSpace(ldap.Domain) ? username : $@"{ldap.Domain}\{username}";
    }

    private bool IsMember(LdapConnection connection, string searchBase, string userDn, string groupName)
    {
        var filter = EscapeFilter(groupName);
        var group = FindOne(connection, searchBase,
            $"(&(objectCategory=group)(|(sAMAccountName={filter})(cn={filter})))", "cn");

        if (group is null)
        {
            logger.LogWarning("AD group {Group} was not found under {SearchBase}; nobody can be a member of it.", groupName, searchBase);
            return false;
        }

        // Ask about the user's own entry: does its memberOf chain (nested groups included) reach the group?
        var request = new SearchRequest(
            userDn, $"(memberOf:{InChain}:={EscapeFilter(group.DistinguishedName)})", SearchScope.Base, "cn");
        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count > 0;
    }

    private static SearchResultEntry? FindOne(LdapConnection connection, string searchBase, string filter, params string[] attributes)
    {
        var request = new SearchRequest(searchBase, filter, SearchScope.Subtree, attributes);
        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count > 0 ? response.Entries[0] : null;
    }

    private static string ReadDefaultNamingContext(LdapConnection connection)
    {
        var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
        var response = (SearchResponse)connection.SendRequest(request);
        var context = response.Entries.Count > 0 ? Attribute(response.Entries[0], "defaultNamingContext") : null;
        return context ?? throw new DirectoryException(
            "The directory did not report a default naming context. Set Authentication:Ldap:SearchBase.");
    }

    private static string? Attribute(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name))
        {
            return null;
        }

        var values = entry.Attributes[name].GetValues(typeof(string));
        return values.Length > 0 ? values[0] as string : null;
    }

    /// <summary>Escapes a value for use inside an LDAP search filter (RFC 4515).</summary>
    internal static string EscapeFilter(string value)
    {
        var escaped = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            escaped.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString(),
            });
        }
        return escaped.ToString();
    }
}
