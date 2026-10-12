using System.Text;
using RMS.Core.Domain;

namespace RMS.Core.Reporting;

public enum SqlTokenKind
{
    /// <summary>Ordinary SQL text.</summary>
    Code,

    /// <summary>A 'string literal', a "quoted identifier", and per database q'[...]' (Oracle) or [bracketed name] (SQL Server).</summary>
    Literal,

    /// <summary>A -- line comment or /* block comment */ (optimizer hints included).</summary>
    Comment,

    /// <summary>An @parameter. <see cref="SqlToken.Text"/> holds the name without the @.</summary>
    Parameter,
}

public readonly record struct SqlToken(SqlTokenKind Kind, string Text);

/// <summary>
/// Splits report SQL into code, literals, comments and @parameters.
/// Joining the tokens back together (with @ restored on parameters) reproduces the input exactly.
/// </summary>
/// <remarks>
/// An @ is only a report parameter when it stands on its own. These are left alone:
/// an @ inside a literal or comment; an @ glued to a name, as in the Oracle database link
/// <c>employees@hr_link</c>; SQL Server globals such as <c>@@ROWCOUNT</c>; and, in a SQL Server
/// procedure call, the procedure's own argument names (<c>@From</c> in <c>@From = @FromDate</c>).
/// </remarks>
public static class SqlScanner
{
    public static List<SqlToken> Scan(
        string? sql, DatabaseKind database = DatabaseKind.Oracle, ReportQueryType queryType = ReportQueryType.Select)
    {
        var tokens = new List<SqlToken>();
        if (string.IsNullOrEmpty(sql))
        {
            return tokens;
        }

        var code = new StringBuilder();
        var i = 0;
        var n = sql.Length;

        void FlushCode()
        {
            if (code.Length > 0)
            {
                tokens.Add(new SqlToken(SqlTokenKind.Code, code.ToString()));
                code.Clear();
            }
        }

        void Emit(SqlTokenKind kind, int end)
        {
            FlushCode();
            tokens.Add(new SqlToken(kind, sql[i..end]));
            i = end;
        }

        while (i < n)
        {
            var c = sql[i];

            if (c == '\'')
            {
                var alternative = database == DatabaseKind.Oracle && IsAlternativeQuote(sql, i);
                Emit(SqlTokenKind.Literal, alternative ? FindAlternativeQuoteEnd(sql, i) : FindQuoteEnd(sql, i));
            }
            else if (c == '"')
            {
                var close = sql.IndexOf('"', i + 1);
                Emit(SqlTokenKind.Literal, close < 0 ? n : close + 1);
            }
            else if (c == '[' && database == DatabaseKind.SqlServer)
            {
                Emit(SqlTokenKind.Literal, FindBracketEnd(sql, i));
            }
            else if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                var newline = sql.IndexOf('\n', i);
                Emit(SqlTokenKind.Comment, newline < 0 ? n : newline);
            }
            else if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                Emit(SqlTokenKind.Comment, close < 0 ? n : close + 2);
            }
            else if (c == '@' && i + 1 < n && IsNameStart(sql[i + 1]) && !(i > 0 && IsGluedTo(sql[i - 1])))
            {
                var end = i + 2;
                while (end < n && IsNamePart(sql[end]))
                {
                    end++;
                }

                FlushCode();
                tokens.Add(new SqlToken(SqlTokenKind.Parameter, sql[(i + 1)..end]));
                i = end;
            }
            else
            {
                code.Append(c);
                i++;
            }
        }

        FlushCode();

        if (database == DatabaseKind.SqlServer && queryType == ReportQueryType.StoredProcedure)
        {
            KeepProcedureArgumentNames(tokens);
        }

        return tokens;
    }

    /// <summary>Rebuilds the original text of a token.</summary>
    public static string ToSql(SqlToken token) =>
        token.Kind == SqlTokenKind.Parameter ? "@" + token.Text : token.Text;

    /// <summary>
    /// In <c>EXEC dbo.usp_Sales @From = @FromDate</c> the name before the equals sign belongs to the
    /// procedure, not to the report, so it is turned back into plain SQL text.
    /// </summary>
    private static void KeepProcedureArgumentNames(List<SqlToken> tokens)
    {
        for (var t = 0; t + 1 < tokens.Count; t++)
        {
            if (tokens[t].Kind != SqlTokenKind.Parameter || tokens[t + 1].Kind != SqlTokenKind.Code)
            {
                continue;
            }

            if (tokens[t + 1].Text.TrimStart().StartsWith('='))
            {
                tokens[t] = new SqlToken(SqlTokenKind.Code, "@" + tokens[t].Text);
            }
        }
    }

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsNamePart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Characters that make a following @ part of something else: the end of a table name
    /// (database link) or another @ (SQL Server global variable).
    /// </summary>
    private static bool IsGluedTo(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '"' or ']' or '@';

    private static int FindQuoteEnd(string sql, int start)
    {
        var j = start + 1;
        while (j < sql.Length)
        {
            if (sql[j] == '\'')
            {
                if (j + 1 < sql.Length && sql[j + 1] == '\'')
                {
                    j += 2; // doubled quote inside the literal
                    continue;
                }
                return j + 1;
            }
            j++;
        }
        return sql.Length;
    }

    /// <summary>End of a SQL Server [bracketed identifier]; a doubled ]] is an escaped bracket.</summary>
    private static int FindBracketEnd(string sql, int start)
    {
        var j = start + 1;
        while (j < sql.Length)
        {
            if (sql[j] == ']')
            {
                if (j + 1 < sql.Length && sql[j + 1] == ']')
                {
                    j += 2;
                    continue;
                }
                return j + 1;
            }
            j++;
        }
        return sql.Length;
    }

    /// <summary>True when the quote at <paramref name="quoteIndex"/> opens an Oracle q'[...]' literal.</summary>
    private static bool IsAlternativeQuote(string sql, int quoteIndex)
    {
        if (quoteIndex < 1 || quoteIndex + 1 >= sql.Length || char.ToUpperInvariant(sql[quoteIndex - 1]) != 'Q')
        {
            return false;
        }

        // The q must stand alone (or follow the national-character prefix N), not end an identifier.
        var before = quoteIndex - 2;
        if (before >= 0 && char.ToUpperInvariant(sql[before]) == 'N')
        {
            before--;
        }
        return before < 0 || !(char.IsLetterOrDigit(sql[before]) || sql[before] is '_' or '$' or '#');
    }

    private static int FindAlternativeQuoteEnd(string sql, int quoteIndex)
    {
        var open = sql[quoteIndex + 1];
        var close = open switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => open,
        };

        for (var j = quoteIndex + 2; j + 1 < sql.Length; j++)
        {
            if (sql[j] == close && sql[j + 1] == '\'')
            {
                return j + 2;
            }
        }
        return sql.Length;
    }
}
