using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Infrastructure.Data.Providers;

/// <summary>Oracle Database through ODP.NET (Oracle.ManagedDataAccess.Core).</summary>
internal sealed partial class OracleProvider : IDatabaseProvider
{
    private const string NewIdParameter = "p_new_id";

    public DatabaseKind Kind => DatabaseKind.Oracle;
    public char ParameterPrefix => ':';
    public string CurrentTimestampSql => "SYSTIMESTAMP";

    // An ODP.NET command always runs in its connection's current transaction.
    public bool CommandsNeedTransaction => false;

    public DbConnection CreateConnection(string connectionString) => new OracleConnection(connectionString);

    public void ConfigureCommand(DbCommand command)
    {
        var oracle = (OracleCommand)command;
        oracle.BindByName = true;           // ODP.NET binds by position unless told otherwise
        oracle.InitialLOBFetchSize = -1;    // return CLOB/BLOB content with the row
        oracle.InitialLONGFetchSize = -1;
    }

    public DbParameter CreateParameter(DbParam parameter)
    {
        var (type, value) = parameter.Type switch
        {
            DbParamType.LongText => (OracleDbType.Clob, parameter.Value),
            DbParamType.Int => (OracleDbType.Int32, parameter.Value),
            DbParamType.Long => (OracleDbType.Int64, parameter.Value),
            DbParamType.Bool => (OracleDbType.Int16, (object)(parameter.Value is true ? 1 : 0)),
            DbParamType.Timestamp => (OracleDbType.TimeStamp, parameter.Value),
            _ => (OracleDbType.Varchar2, parameter.Value),
        };

        return new OracleParameter(parameter.Name, type) { Value = value ?? DBNull.Value };
    }

    public DbParameter CreateReportParameter(BindValue bind)
    {
        if (bind.IsCursor)
        {
            // ODP.NET exposes a REF CURSOR OUT argument as the command's data reader.
            return new OracleParameter(bind.Name, OracleDbType.RefCursor) { Direction = ParameterDirection.Output };
        }

        // Dates are bound as DATE rather than TIMESTAMP on purpose: comparing a DATE column with a
        // TIMESTAMP bind makes Oracle convert the column, which stops it from using an index on it.
        var (type, value) = bind.DataType switch
        {
            ParameterDataType.Integer => (OracleDbType.Int64, bind.Value),
            ParameterDataType.Decimal => (OracleDbType.Decimal, bind.Value),
            ParameterDataType.Date => (OracleDbType.Date, bind.Value),
            ParameterDataType.DateTime => (OracleDbType.Date, bind.Value),
            ParameterDataType.Boolean => (OracleDbType.Int16, bind.Value is bool flag ? (object)(flag ? 1 : 0) : null),
            _ => (OracleDbType.Varchar2, bind.Value),
        };

        return new OracleParameter(bind.Name, type) { Direction = ParameterDirection.Input, Value = value ?? DBNull.Value };
    }

    public async Task<int> ExecuteInsertAsync(DbCommand insert, CancellationToken ct)
    {
        var newId = new OracleParameter(NewIdParameter, OracleDbType.Int32) { Direction = ParameterDirection.Output };
        insert.CommandText += $" RETURNING ID INTO :{NewIdParameter}";
        insert.Parameters.Add(newId);
        await insert.ExecuteNonQueryAsync(ct);

        // ODP.NET hands back an OracleDecimal here; going through text avoids depending on that type.
        return int.Parse(Convert.ToString(newId.Value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
    }

    public Exception? TranslateWriteError(DbException exception) => (exception as OracleException)?.Number switch
    {
        // ORA-00001: unique constraint (SCHEMA.NAME) violated
        1 => new DuplicateValueException(
            ConstraintName().Match(exception.Message) is { Success: true } match ? match.Groups[1].Value : "", exception),

        // ORA-02292: integrity constraint violated - child record found
        2292 => new RecordInUseException("The record is referenced by other records.", exception),
        _ => null,
    };

    public ReportExecutionException ToReportError(DbException exception, string connectionName, int timeoutSeconds)
    {
        var number = (exception as OracleException)?.Number ?? 0;
        var message = number switch
        {
            // ORA-01013: user requested cancel of current operation (this is how a command timeout surfaces)
            1013 => $"The report ran longer than {timeoutSeconds} seconds and was stopped. Narrow the parameters and try again.",

            // Listener, service name and network problems
            12154 or 12170 or 12514 or 12541 or 12543 or 12545 or 12547 or 3113 or 3114 or 3135 or 50000 =>
                $"The database for connection '{connectionName}' could not be reached. {exception.Message}",

            // ORA-01017 / ORA-28000 / ORA-28001: the connection's own account cannot log on
            1017 or 28000 or 28001 =>
                $"The database account for connection '{connectionName}' could not log on. {exception.Message}",

            _ => exception.Message,
        };

        return new ReportExecutionException(message, $"ORA-{number:00000}", exception);
    }

    public void ConfigureReader(DbDataReader reader)
    {
        if (reader is OracleDataReader oracle)
        {
            // Round NUMBER values with more than 28 digits instead of throwing when they are read as decimal.
            oracle.SuppressGetDecimalInvalidCastException = true;
        }
    }

    public string? ReadAsText(DbDataReader reader, int ordinal) =>
        reader is OracleDataReader oracle ? oracle.GetOracleValue(ordinal)?.ToString() : null;

    [GeneratedRegex(@"\((?:[^.()]+\.)?([^.()]+)\)")]
    private static partial Regex ConstraintName();
}
