using System.Text;
using System.Text.RegularExpressions;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

/// <summary>
/// Checks a report definition before it is saved or run.
/// </summary>
/// <remarks>
/// This is a guard against mistakes (pasting a script, an UPDATE, two statements), not a security boundary.
/// Report SQL is written by administrators; the real protection is a read-only database account
/// for the report connections.
/// </remarks>
public static partial class ReportSqlValidator
{
    private static readonly string[] StatementKeywords =
    [
        "SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "CREATE", "ALTER", "TRUNCATE",
        "GRANT", "REVOKE", "DECLARE", "BEGIN", "SET", "USE", "IF", "WHILE", "CALL", "EXEC", "EXECUTE",
    ];

    public static IReadOnlyList<string> Validate(
        string? sql, ReportQueryType queryType, DatabaseKind database = DatabaseKind.Oracle)
    {
        var errors = new List<string>();
        var tokens = SqlScanner.Scan(sql, database, queryType);
        var code = CodeOnly(tokens);

        if (code.Length == 0)
        {
            errors.Add("Enter the SQL for this report.");
            return errors;
        }

        if (code.Contains(';'))
        {
            errors.Add("Use a single statement and remove any ';' from the middle of the query.");
        }

        var parameters = tokens.Where(t => t.Kind == SqlTokenKind.Parameter).Select(t => t.Text).ToList();
        var usesCursor = parameters.Any(SystemParameters.IsCursor);

        foreach (var unknown in parameters
                     .Where(p => SystemParameters.IsSystem(p) && !SystemParameters.IsCursor(p))
                     .Where(p => !SystemParameters.ValueNames.Contains(p, StringComparer.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"@{unknown} is not a known system parameter. Names starting with {SystemParameters.Prefix} are reserved; "
                       + $"available ones are {string.Join(", ", SystemParameters.ValueNames.Select(n => "@" + n))}.");
        }

        if (queryType == ReportQueryType.Select)
        {
            var firstWord = FirstWord().Match(code.TrimStart('(', ' ', '\t', '\r', '\n')).Value;
            if (!firstWord.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
                && !firstWord.Equals("WITH", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("A query report must start with SELECT or WITH.");
            }

            if (usesCursor)
            {
                errors.Add($"@{SystemParameters.Cursor} is only for Oracle stored procedure reports.");
            }
        }
        else if (database == DatabaseKind.SqlServer)
        {
            var call = SqlServerProcedureCall().Match(code);
            var firstName = call.Success ? FirstWord().Match(call.Groups["name"].Value).Value : "";
            if (!call.Success || StatementKeywords.Contains(firstName, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Write only the procedure call, for example: EXEC dbo.usp_SalesByRegion @FromDate, @ToDate");
            }

            if (usesCursor)
            {
                errors.Add($"@{SystemParameters.Cursor} is for Oracle only. A SQL Server procedure returns its rows with a SELECT; remove the argument.");
            }
        }
        else
        {
            if (!OracleProcedureCall().IsMatch(code))
            {
                errors.Add("Write the call as package.procedure(arguments) without BEGIN, END, CALL or EXEC.");
            }

            if (!usesCursor)
            {
                errors.Add($"Pass @{SystemParameters.Cursor} for the procedure's SYS_REFCURSOR OUT argument so its rows can be read.");
            }
        }

        return errors;
    }

    /// <summary>
    /// The statement with strings and comments blanked out, parameters and quoted names replaced by
    /// placeholders, and any leading or trailing terminator removed.
    /// </summary>
    private static string CodeOnly(List<SqlToken> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            sb.Append(token.Kind switch
            {
                SqlTokenKind.Code => token.Text,
                SqlTokenKind.Parameter => "?",
                SqlTokenKind.Literal when token.Text.StartsWith('\'') => "''",
                SqlTokenKind.Literal => "quoted_name",
                _ => " ",
            });
        }
        return ReportSqlTranslator.TrimTerminator(sb.ToString()).TrimStart(';', ' ', '\t', '\r', '\n').Trim();
    }

    [GeneratedRegex(@"^[A-Za-z_]+")]
    private static partial Regex FirstWord();

    // schema.package.procedure( ... )  with one to three name parts
    [GeneratedRegex(@"^[A-Za-z][\w$#]*(\s*\.\s*[A-Za-z][\w$#]*){0,2}\s*\(.*\)$", RegexOptions.Singleline)]
    private static partial Regex OracleProcedureCall();

    // [EXEC] server.database.schema.procedure [arguments]   with one to four name parts
    [GeneratedRegex(@"^(?:EXEC(?:UTE)?\s+)?(?<name>[A-Za-z_#][\w$#]*(?:\s*\.\s*[A-Za-z_][\w$#]*){0,3})(?:\s+.*)?$",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SqlServerProcedureCall();
}
