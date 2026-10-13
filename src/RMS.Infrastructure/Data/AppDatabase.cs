using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using RMS.Infrastructure.Data.Providers;

namespace RMS.Infrastructure.Data;

/// <summary>
/// Thin helper over ADO.NET for the application's own tables. Repository SQL is written once, in a
/// neutral form, and adapted here to whichever database RmsDb points at:
/// <list type="bullet">
/// <item>bind variables are written <c>@p_name</c> and become <c>:p_name</c> on Oracle;</item>
/// <item><c>{now}</c> becomes the database's current-timestamp expression;</item>
/// <item>inserts are plain INSERT statements and the provider adds its way of returning the new ID.</item>
/// </list>
/// </summary>
internal sealed class AppDatabase(IConnectionFactory factory)
{
    public async Task<List<T>> QueryAsync<T>(
        string sql, Func<DbDataReader, T> map, CancellationToken ct, params DbParam[] parameters)
    {
        await using var session = await OpenAsync(ct);
        return await session.QueryAsync(sql, map, ct, parameters);
    }

    public async Task<T?> QuerySingleAsync<T>(
        string sql, Func<DbDataReader, T> map, CancellationToken ct, params DbParam[] parameters)
        where T : class
    {
        var rows = await QueryAsync(sql, map, ct, parameters);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct, params DbParam[] parameters)
    {
        await using var session = await OpenAsync(ct);
        return await session.ExecuteAsync(sql, ct, parameters);
    }

    /// <summary>Runs an INSERT into a table with a generated ID and returns the new ID.</summary>
    public async Task<int> InsertAsync(string sql, CancellationToken ct, params DbParam[] parameters)
    {
        await using var session = await OpenAsync(ct);
        return await session.InsertAsync(sql, ct, parameters);
    }

    /// <summary>Runs <paramref name="work"/> in one transaction; commits when it returns, rolls back when it throws.</summary>
    public async Task<T> InTransactionAsync<T>(Func<DbSession, Task<T>> work, CancellationToken ct)
    {
        await using var session = await OpenAsync(ct);
        await session.BeginTransactionAsync(ct);
        try
        {
            var result = await work(session);
            await session.CommitAsync(ct);
            return result;
        }
        catch
        {
            await session.RollbackAsync();
            throw;
        }
    }

    private async Task<DbSession> OpenAsync(CancellationToken ct)
    {
        var target = factory.CreateAppConnection();
        try
        {
            await target.Connection.OpenAsync(ct);
            return new DbSession(target.Connection, target.Provider);
        }
        catch
        {
            await target.Connection.DisposeAsync();
            throw;
        }
    }
}

/// <summary>One open connection, optionally inside a transaction.</summary>
internal sealed partial class DbSession(DbConnection connection, IDatabaseProvider provider) : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, string> OracleSql = new();
    private DbTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken ct) =>
        _transaction = await connection.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
        }
    }

    public async Task RollbackAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql, Func<DbDataReader, T> map, CancellationToken ct, params DbParam[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }
        return rows;
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct, params DbParam[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        try
        {
            return await command.ExecuteNonQueryAsync(ct);
        }
        catch (DbException ex) when (provider.TranslateWriteError(ex) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<int> InsertAsync(string sql, CancellationToken ct, params DbParam[] parameters)
    {
        await using var command = CreateCommand(sql, parameters);
        try
        {
            return await provider.ExecuteInsertAsync(command, ct);
        }
        catch (DbException ex) when (provider.TranslateWriteError(ex) is { } translated)
        {
            throw translated;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }
        await connection.DisposeAsync();
    }

    private DbCommand CreateCommand(string sql, DbParam[] parameters)
    {
        var command = connection.CreateCommand();
        provider.ConfigureCommand(command);
        command.CommandText = Adapt(sql);

        if (_transaction is not null && provider.CommandsNeedTransaction)
        {
            command.Transaction = _transaction;
        }

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(provider.CreateParameter(parameter));
        }
        return command;
    }

    private string Adapt(string sql)
    {
        if (provider.ParameterPrefix == '@')
        {
            return sql.Replace("{now}", provider.CurrentTimestampSql, StringComparison.Ordinal);
        }

        // Every bind variable in the repositories is named p_something, which makes the swap unambiguous.
        return OracleSql.GetOrAdd(sql, text => BindVariable()
            .Replace(text, provider.ParameterPrefix + "$1")
            .Replace("{now}", provider.CurrentTimestampSql, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"@(p_\w+)")]
    private static partial Regex BindVariable();
}

/// <summary>Column readers that tolerate the different .NET types the two databases return for the same column.</summary>
internal static class DataReaderExtensions
{
    public static int Int(this DbDataReader reader, string column) =>
        Convert.ToInt32(reader[column], CultureInfo.InvariantCulture);

    public static int? IntOrNull(this DbDataReader reader, string column) =>
        reader[column] is DBNull ? null : Convert.ToInt32(reader[column], CultureInfo.InvariantCulture);

    public static long Long(this DbDataReader reader, string column) =>
        Convert.ToInt64(reader[column], CultureInfo.InvariantCulture);

    /// <summary>Reads NUMBER(1) on Oracle and BIT on SQL Server alike.</summary>
    public static bool Bool(this DbDataReader reader, string column) =>
        reader[column] is not DBNull && Convert.ToInt32(reader[column], CultureInfo.InvariantCulture) != 0;

    public static string Text(this DbDataReader reader, string column) =>
        reader[column] is DBNull ? "" : Convert.ToString(reader[column], CultureInfo.InvariantCulture) ?? "";

    public static string? TextOrNull(this DbDataReader reader, string column) =>
        reader[column] is DBNull ? null : Convert.ToString(reader[column], CultureInfo.InvariantCulture);

    public static DateTime Date(this DbDataReader reader, string column) =>
        Convert.ToDateTime(reader[column], CultureInfo.InvariantCulture);

    public static DateTime? DateOrNull(this DbDataReader reader, string column) =>
        reader[column] is DBNull ? null : Convert.ToDateTime(reader[column], CultureInfo.InvariantCulture);

    /// <summary>Reads a UTC timestamp column; the databases return it without a kind.</summary>
    public static DateTime? UtcOrNull(this DbDataReader reader, string column) =>
        reader.DateOrNull(column) is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null;

    public static long? LongOrNull(this DbDataReader reader, string column) =>
        reader[column] is DBNull ? null : Convert.ToInt64(reader[column], CultureInfo.InvariantCulture);
}
