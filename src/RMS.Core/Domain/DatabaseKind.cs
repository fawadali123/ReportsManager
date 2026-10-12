namespace RMS.Core.Domain;

/// <summary>The database products the application can store its data in and run reports on.</summary>
public enum DatabaseKind
{
    Oracle = 0,
    SqlServer = 1,
}

public static class DatabaseKinds
{
    public static string DisplayName(this DatabaseKind kind) => kind == DatabaseKind.SqlServer ? "SQL Server" : "Oracle";

    /// <summary>Reads a provider name from configuration. Accepts "Oracle", "SqlServer", "SQL Server" and "MSSQL".</summary>
    public static bool TryParse(string? text, out DatabaseKind kind)
    {
        var key = new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        switch (key)
        {
            case "ORACLE":
                kind = DatabaseKind.Oracle;
                return true;
            case "SQLSERVER" or "MSSQL" or "MSSQLSERVER":
                kind = DatabaseKind.SqlServer;
                return true;
            default:
                kind = DatabaseKind.Oracle;
                return false;
        }
    }
}
