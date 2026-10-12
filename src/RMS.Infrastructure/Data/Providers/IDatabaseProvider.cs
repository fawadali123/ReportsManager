using System.Data.Common;
using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Infrastructure.Data.Providers;

internal enum DbParamType
{
    Text,

    /// <summary>Text of any length: CLOB on Oracle, NVARCHAR(MAX) on SQL Server.</summary>
    LongText,
    Int,
    Long,
    Bool,
    Timestamp,
}

/// <summary>A bind variable described without reference to a database product.</summary>
/// <param name="Name">Name as written in the SQL, without a prefix: <c>p_code</c> for <c>@p_code</c>.</param>
internal readonly record struct DbParam(string Name, DbParamType Type, object? Value);

/// <summary>Short factory methods for bind variables.</summary>
internal static class P
{
    // Oracle stores an empty string as NULL. Doing the same everywhere keeps both databases behaving alike.
    public static DbParam Text(string name, string? value) =>
        new(name, DbParamType.Text, string.IsNullOrEmpty(value) ? null : value);

    public static DbParam LongText(string name, string? value) =>
        new(name, DbParamType.LongText, string.IsNullOrEmpty(value) ? null : value);

    public static DbParam Int(string name, int? value) => new(name, DbParamType.Int, value);

    public static DbParam Long(string name, long? value) => new(name, DbParamType.Long, value);

    public static DbParam Bool(string name, bool value) => new(name, DbParamType.Bool, value);

    public static DbParam Timestamp(string name, DateTime value) => new(name, DbParamType.Timestamp, value);
}

/// <summary>
/// Everything that differs between database products, in one place. The repositories and the report
/// executor are written once against System.Data.Common and ask the provider for the rest.
/// To support another database, implement this interface and register it in <see cref="DatabaseProviders"/>.
/// </summary>
internal interface IDatabaseProvider
{
    DatabaseKind Kind { get; }

    /// <summary>What a bind variable starts with in SQL text: ':' on Oracle, '@' on SQL Server.</summary>
    char ParameterPrefix { get; }

    /// <summary>SQL expression for the current date and time.</summary>
    string CurrentTimestampSql { get; }

    /// <summary>False when commands join their connection's transaction on their own (Oracle).</summary>
    bool CommandsNeedTransaction { get; }

    DbConnection CreateConnection(string connectionString);

    /// <summary>Applies product-specific settings to a new command.</summary>
    void ConfigureCommand(DbCommand command);

    DbParameter CreateParameter(DbParam parameter);

    /// <summary>Creates the bind variable for a report parameter value.</summary>
    DbParameter CreateReportParameter(BindValue bind);

    /// <summary>Runs an INSERT into a table with a generated ID column and returns the new ID.</summary>
    Task<int> ExecuteInsertAsync(DbCommand insert, CancellationToken ct);

    /// <summary>
    /// Maps a failed write to <see cref="DuplicateValueException"/> or <see cref="RecordInUseException"/>
    /// when that is what happened; null for anything else.
    /// </summary>
    Exception? TranslateWriteError(DbException exception);

    /// <summary>Describes a failed report query in words an administrator can act on.</summary>
    ReportExecutionException ToReportError(DbException exception, string connectionName, int timeoutSeconds);

    /// <summary>Applies product-specific settings to a report's data reader.</summary>
    void ConfigureReader(DbDataReader reader);

    /// <summary>Last resort for a value .NET could not convert: the database's own text form, if available.</summary>
    string? ReadAsText(DbDataReader reader, int ordinal);
}

/// <summary>Looks up the provider for a database product.</summary>
internal sealed class DatabaseProviders
{
    private readonly Dictionary<DatabaseKind, IDatabaseProvider> _providers = new()
    {
        [DatabaseKind.Oracle] = new OracleProvider(),
        [DatabaseKind.SqlServer] = new SqlServerProvider(),
    };

    public IDatabaseProvider Get(DatabaseKind kind) => _providers[kind];
}
