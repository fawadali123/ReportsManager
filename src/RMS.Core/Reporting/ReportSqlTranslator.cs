using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

/// <summary>
/// Turns report SQL written with @parameters into SQL with bind variables for the target database
/// (<c>:b0</c> on Oracle, <c>@b0</c> on SQL Server). Values always travel as binds and are never
/// concatenated into the statement, so nothing a user types can change the query.
/// </summary>
public static partial class ReportSqlTranslator
{
    /// <summary>Oracle refuses more than 1000 expressions in an IN list; the same limit is kept for SQL Server.</summary>
    public const int MaxListValues = 1000;

    public const string CursorBindName = "rms_cursor";

    /// <param name="values">Typed values for every parameter in the SQL, user-entered and system alike.</param>
    /// <exception cref="ReportValidationException">A parameter has no value, or a list is too long.</exception>
    public static ReportQuery Translate(
        string sql,
        ReportQueryType queryType,
        IReadOnlyDictionary<string, ParameterValue> values,
        string? connectionName = null,
        DatabaseKind database = DatabaseKind.Oracle)
    {
        var tokens = SqlScanner.Scan(sql, database, queryType);
        StripTerminator(tokens);
        var prefix = database == DatabaseKind.SqlServer ? '@' : ':';

        var output = new StringBuilder(sql.Length + 32);
        var binds = new List<BindValue>();
        var bindIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        for (var t = 0; t < tokens.Count; t++)
        {
            var token = tokens[t];
            if (token.Kind != SqlTokenKind.Parameter)
            {
                output.Append(token.Text);
                continue;
            }

            var name = token.Text;

            if (SystemParameters.IsCursor(name))
            {
                if (queryType != ReportQueryType.StoredProcedure || database != DatabaseKind.Oracle)
                {
                    throw new ReportValidationException($"@{SystemParameters.Cursor} is only for Oracle stored procedure reports.");
                }

                output.Append(prefix).Append(CursorBindName);
                if (emitted.Add(CursorBindName))
                {
                    binds.Add(new BindValue(CursorBindName, ParameterDataType.Text, null, IsCursor: true));
                }
                continue;
            }

            if (!values.TryGetValue(name, out var value))
            {
                throw new ReportValidationException($"No value was supplied for @{name}.");
            }

            // Bind names are generated (b0, b1, ...) rather than reusing the parameter name,
            // because Oracle rejects reserved words such as :date, :from, :to or :user as bind names,
            // and one multi-value parameter needs several binds on either database.
            if (!bindIndex.TryGetValue(name, out var index))
            {
                index = bindIndex.Count;
                bindIndex[name] = index;
            }

            if (value.AllowMultiple && IsListContext(output, tokens, t))
            {
                AppendList(output, binds, emitted, index, name, value, prefix);
            }
            else
            {
                var bindName = $"b{index}";
                output.Append(prefix).Append(bindName);
                if (emitted.Add(bindName))
                {
                    binds.Add(ScalarBind(bindName, value));
                }
            }
        }

        var text = output.ToString().TrimStart(';', ' ', '\t', '\r', '\n'); // tolerate T-SQL's habitual ";WITH"
        if (queryType == ReportQueryType.StoredProcedure && database == DatabaseKind.SqlServer)
        {
            // The first result set the procedure returns becomes the report.
            text = "EXEC " + LeadingExec().Replace(text, "").Trim();
        }
        else if (queryType == ReportQueryType.StoredProcedure)
        {
            // The line break keeps a trailing "-- comment" from swallowing the END.
            text = $"BEGIN {text.Trim()}\n; END;";
        }

        return new ReportQuery(connectionName, text, binds);
    }

    /// <summary>Removes a trailing ';' or SQL*Plus '/' that Oracle drivers reject.</summary>
    public static string TrimTerminator(string text)
    {
        var result = text.TrimEnd();
        while (true)
        {
            if (result.EndsWith(';'))
            {
                result = result[..^1].TrimEnd();
            }
            else if (result.EndsWith('/') && IsAloneOnLine(result))
            {
                result = result[..^1].TrimEnd();
            }
            else
            {
                return result;
            }
        }
    }

    private static bool IsAloneOnLine(string text)
    {
        for (var i = text.Length - 2; i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                return true;
            }
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }
        return false;
    }

    private static void StripTerminator(List<SqlToken> tokens)
    {
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i];
            if (token.Kind == SqlTokenKind.Comment || (token.Kind == SqlTokenKind.Code && string.IsNullOrWhiteSpace(token.Text)))
            {
                continue;
            }

            if (token.Kind == SqlTokenKind.Code)
            {
                var trimmed = TrimTerminator(token.Text);
                if (trimmed.Length != token.Text.TrimEnd().Length)
                {
                    // Keep a line break so a following "-- comment" cannot swallow anything.
                    tokens[i] = new SqlToken(SqlTokenKind.Code, trimmed + "\n");
                }
            }
            return;
        }
    }

    /// <summary>
    /// True when the parameter is the only thing inside a pair of parentheses, as in <c>IN (@Ids)</c>.
    /// Only then is a multi-value parameter expanded into one bind per value; anywhere else
    /// (for example <c>@Ids IS NULL</c>) it is bound once as a comma-separated string.
    /// </summary>
    private static bool IsListContext(StringBuilder output, List<SqlToken> tokens, int tokenIndex)
    {
        var i = output.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(output[i]))
        {
            i--;
        }
        if (i < 0 || output[i] != '(')
        {
            return false;
        }

        if (tokenIndex + 1 >= tokens.Count || tokens[tokenIndex + 1].Kind != SqlTokenKind.Code)
        {
            return false;
        }
        return tokens[tokenIndex + 1].Text.TrimStart().StartsWith(')');
    }

    private static void AppendList(
        StringBuilder output, List<BindValue> binds, HashSet<string> emitted, int index, string name, ParameterValue value, char prefix)
    {
        if (value.Values.Count > MaxListValues)
        {
            throw new ReportValidationException(new Dictionary<string, string>
            {
                [name] = $"Select at most {MaxListValues} values.",
            });
        }

        // An empty selection becomes IN (NULL), which matches nothing.
        IReadOnlyList<object?> items = value.Values.Count == 0 ? [null] : value.Values;

        for (var j = 0; j < items.Count; j++)
        {
            var bindName = $"b{index}_{j}";
            if (j > 0)
            {
                output.Append(", ");
            }
            output.Append(prefix).Append(bindName);
            if (emitted.Add(bindName))
            {
                binds.Add(new BindValue(bindName, value.DataType, items[j]));
            }
        }
    }

    private static BindValue ScalarBind(string bindName, ParameterValue value)
    {
        if (!value.AllowMultiple)
        {
            return new BindValue(bindName, value.DataType, value.Values.Count > 0 ? value.Values[0] : null);
        }

        var joined = value.Values.Count == 0
            ? null
            : string.Join(",", value.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
        return new BindValue(bindName, ParameterDataType.Text, joined);
    }

    [GeneratedRegex(@"^\s*EXEC(UTE)?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingExec();
}
