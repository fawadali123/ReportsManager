namespace RMS.Infrastructure.ActiveDirectory;

/// <summary>Bound from the "Authentication" configuration section.</summary>
public sealed class DirectoryOptions
{
    public const string LdapMode = "Ldap";
    public const string DevelopmentMode = "Development";

    /// <summary>
    /// "Ldap" checks passwords against Active Directory. "Development" uses the fixed accounts in
    /// <see cref="DevelopmentUsers"/> and is refused unless ASPNETCORE_ENVIRONMENT is Development.
    /// </summary>
    public string Mode { get; set; } = LdapMode;

    public LdapOptions Ldap { get; set; } = new();

    public List<DevelopmentUser> DevelopmentUsers { get; set; } = [];
}

public sealed class LdapOptions
{
    /// <summary>
    /// Domain controller host name, or the AD DNS domain name (for example "corp.example.com") to let
    /// DNS pick a controller. Several hosts can be given separated by commas.
    /// </summary>
    public string Server { get; set; } = "";

    /// <summary>0 picks the standard port: 389, or 636 when <see cref="UseSsl"/> is on.</summary>
    public int Port { get; set; }

    /// <summary>Connect with LDAPS. Required when <see cref="AuthType"/> is Basic.</summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// "Negotiate" (Kerberos/NTLM; the password never crosses the wire, recommended on Windows servers)
    /// or "Basic" (simple bind; only safe over LDAPS, and the usual choice on Linux servers).
    /// </summary>
    public string AuthType { get; set; } = "Negotiate";

    /// <summary>NetBIOS domain name, for example "CORP". Used for Negotiate, and for Basic when no UPN suffix is set.</summary>
    public string Domain { get; set; } = "";

    /// <summary>For Basic binds: sign in as user@suffix, for example "corp.example.com".</summary>
    public string UpnSuffix { get; set; } = "";

    /// <summary>Where to look for users and groups, for example "DC=corp,DC=example,DC=com". Empty uses the domain root.</summary>
    public string SearchBase { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Accept any LDAPS certificate. For test environments only; has effect on Windows servers only.</summary>
    public bool IgnoreCertificateErrors { get; set; }
}

/// <summary>A stand-in account for running the application on a machine with no Active Directory.</summary>
public sealed class DevelopmentUser
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }

    /// <summary>AD groups this account pretends to be in, for example ["RMS_Admin"].</summary>
    public List<string> Groups { get; set; } = [];
}
