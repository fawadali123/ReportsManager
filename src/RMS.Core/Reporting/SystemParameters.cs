using RMS.Core.Domain;
using RMS.Core.Services;

namespace RMS.Core.Reporting;

/// <summary>
/// Parameters the application fills in by itself. They are never shown on the form,
/// so a query can filter by the signed-in user without trusting anything the user types.
/// Every name starting with RMS_ is reserved for this purpose.
/// </summary>
public static class SystemParameters
{
    public const string Prefix = "RMS_";

    /// <summary>AD account name of the person running the report.</summary>
    public const string Username = "RMS_USERNAME";

    /// <summary>RMS_USERS.ID of the person running the report.</summary>
    public const string UserId = "RMS_USER_ID";

    /// <summary>RMS_USERS.CODE of the person running the report.</summary>
    public const string UserCode = "RMS_USER_CODE";

    /// <summary>RMS_USERS.EMAIL of the person running the report.</summary>
    public const string UserEmail = "RMS_USER_EMAIL";

    /// <summary>Placeholder for the SYS_REFCURSOR OUT argument of a stored procedure report.</summary>
    public const string Cursor = "RMS_CURSOR";

    public static bool IsSystem(string name) => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsCursor(string name) => string.Equals(name, Cursor, StringComparison.OrdinalIgnoreCase);

    /// <summary>Names that may be used in SQL, for validation and help text.</summary>
    public static readonly IReadOnlyList<string> ValueNames = [Username, UserId, UserCode, UserEmail];

    public static Dictionary<string, ParameterValue> ValuesFor(UserContext user) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Username] = ParameterValue.Text(user.Username),
            [UserId] = ParameterValue.Single(ParameterDataType.Integer, user.UserId is int id ? (long)id : null),
            [UserCode] = ParameterValue.Text(string.IsNullOrEmpty(user.Code) ? null : user.Code),
            [UserEmail] = ParameterValue.Text(user.Email),
        };
}
