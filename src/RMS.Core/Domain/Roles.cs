namespace RMS.Core.Domain;

/// <summary>Application roles. Stored as text in RMS_USERS.ROLE.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Viewer];

    public static bool IsValid(string? role) =>
        role is not null && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the canonical spelling ("admin" becomes "Admin"). Unknown values fall back to Viewer.</summary>
    public static string Normalize(string? role) =>
        All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) ?? Viewer;
}
