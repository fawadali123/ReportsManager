using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Infrastructure.Data.Providers;

/// <summary>Microsoft SQL Server through Microsoft.Data.SqlClient.</summary>
internal sealed partial class SqlServerProvider : IDatabaseProvider
{
    private const int MaxInlineText = 4000;

    public DatabaseKind Kind => DatabaseKind.SqlServer;
    public char ParameterPrefix => '@';
    public string CurrentTimestampSql => "SYSDATETIME()";

    // A SqlCommand must be handed the transaction explicitly or it refuses to run inside one.
    public bool CommandsNeedTransaction => true;

    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public void ConfigureCommand(DbCommand command)
    {
        // SqlClient binds by name and returns large values with the row; nothing to switch on.
    }

    public DbParameter CreateParameter(DbParam parameter) => parameter.Type switch
    {
        DbParamType.LongText => Text(parameter.Name, parameter.Value as string, alwaysMax: true),
        DbParamType.Int => Create(parameter.Name, SqlDbType.Int, parameter.Value),
        DbParamType.Long => Create(parameter.Name, SqlDbType.BigInt, parameter.Value),
        DbParamType.Bool => Create(parameter.Name, SqlDbType.Bit, parameter.Value is true),
        DbParamType.Timestamp => Create(parameter.Name, SqlDbType.DateTime2, parameter.Value),
        _ => Text(parameter.Name, parameter.Value as string, alwaysMax: false),
    };

    public DbParameter CreateReportParameter(BindValue bind) => bind.DataType switch
    {
        // INT whenever the value fits: functions such as DATEADD and TOP refuse a BIGINT argument.
        ParameterDataType.Integer when bind.Value is long big && (big < int.MinValue || big > int.MaxValue) =>
            Create(bind.Name, SqlDbType.BigInt, big),
        ParameterDataType.Integer => Create(bind.Name, SqlDbType.Int, bind.Value is null ? null : Convert.ToInt32(bind.Value, CultureInfo.InvariantCulture)),
        ParameterDataType.Decimal => Create(bind.Name, SqlDbType.Decimal, bind.Value),
        ParameterDataType.Date => Create(bind.Name, SqlDbType.DateTime2, bind.Value),
        ParameterDataType.DateTime => Create(bind.Name, SqlDbType.DateTime2, bind.Value),
        ParameterDataType.Boolean => Create(bind.Name, SqlDbType.Bit, bind.Value),

        // NVARCHAR so that Arabic and other non-Latin text survives whatever the column's code page is.
        _ => Text(bind.Name, bind.Value is null ? null : Convert.ToString(bind.Value, CultureInfo.InvariantCulture), alwaysMax: false),
    };

    public async Task<int> ExecuteInsertAsync(DbCommand insert, CancellationToken ct)
    {
        insert.CommandText += "; SELECT CAST(SCOPE_IDENTITY() AS int);";
        return Convert.ToInt32(await insert.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    public Exception? TranslateWriteError(DbException exception)
    {
        switch ((exception as SqlException)?.Number)
        {
            // 2627: Violation of UNIQUE KEY constraint 'NAME'.   2601: duplicate key row ... with unique index 'NAME'.
            case 2627 or 2601:
                var names = QuotedName().Matches(exception.Message).Select(m => m.Groups[1].Value).ToList();
                var constraint = names.FirstOrDefault(n => n.StartsWith("UK_", StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault() ?? "";
                return new DuplicateValueException(constraint, exception);

            // 547 covers every constraint conflict; only a blocked DELETE means "still referenced".
            case 547 when exception.Message.Contains("DELETE", StringComparison.OrdinalIgnoreCase):
                return new RecordInUseException("The record is referenced by other records.", exception);

            default:
                return null;
        }
    }

    public ReportExecutionException ToReportError(DbException exception, string connectionName, int timeoutSeconds)
    {
        var number = (exception as SqlException)?.Number ?? 0;
        var message = number switch
        {
            // -2: the client-side command timeout expired
            -2 => $"The report ran longer than {timeoutSeconds} seconds and was stopped. Narrow the parameters and try again.",

            // Server not found, connection refused or dropped, database not available
            -1 or 2 or 40 or 53 or 121 or 233 or 4060 or 10053 or 10054 or 10060 or 10061 or 11001 =>
                $"The database for connection '{connectionName}' could not be reached. {exception.Message}",

            // 18456: login failed.  18486/18487/18488: account locked or password expired
            18456 or 18486 or 18487 or 18488 =>
                $"The database account for connection '{connectionName}' could not log on. {exception.Message}",

            _ => exception.Message,
        };

        return new ReportExecutionException(message, $"SQL Server error {number}", exception);
    }

    public void ConfigureReader(DbDataReader reader)
    {
    }

    public string? ReadAsText(DbDataReader reader, int ordinal) => "[value of an unsupported type]";

    private static SqlParameter Create(string name, SqlDbType type, object? value) =>
        new("@" + name, type) { Value = value ?? DBNull.Value };

    private static SqlParameter Text(string name, string? value, bool alwaysMax) =>
        new("@" + name, SqlDbType.NVarChar)
        {
            // A fixed size for ordinary text lets SQL Server reuse one query plan whatever the value's length.
            Size = alwaysMax || value?.Length > MaxInlineText ? -1 : MaxInlineText,
            Value = (object?)value ?? DBNull.Value,
        };

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex QuotedName();
}
