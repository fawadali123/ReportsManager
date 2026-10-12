using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using RMS.Core.Abstractions;
using RMS.Core.Reporting;
using RMS.Core.Services;
using RMS.Infrastructure.Data;
using RMS.Infrastructure.Data.Providers;

namespace RMS.Infrastructure.Reporting;

/// <summary>
/// Runs report queries and streams the rows back. Works on any configured connection;
/// the connection's provider supplies what is specific to Oracle or SQL Server.
/// </summary>
internal sealed class ReportExecutor(
    IConnectionFactory factory,
    ReportingOptions options,
    ILogger<ReportExecutor> logger) : IReportExecutor
{
    public async Task<IReportResultReader> ExecuteAsync(ReportQuery query, CancellationToken ct = default)
    {
        var target = factory.CreateReportConnection(query.ConnectionName);
        var provider = target.Provider;
        DbCommand? command = null;

        try
        {
            await target.Connection.OpenAsync(ct);

            command = target.Connection.CreateCommand();
            provider.ConfigureCommand(command);
            command.CommandText = query.Sql;
            command.CommandTimeout = options.CommandTimeoutSeconds;

            foreach (var bind in query.Binds)
            {
                command.Parameters.Add(provider.CreateReportParameter(bind));
            }

            var reader = await command.ExecuteReaderAsync(ct);
            provider.ConfigureReader(reader);
            return new ReportResultReader(target.Connection, command, reader, provider);
        }
        catch (DbException ex)
        {
            var error = provider.ToReportError(ex, target.Name, options.CommandTimeoutSeconds);
            logger.LogWarning(ex, "Report query failed on connection {Connection} ({Database}): {Code}",
                target.Name, provider.Kind, error.ErrorCode);
            await DisposeAsync(command, target.Connection);
            throw error;
        }
        catch
        {
            await DisposeAsync(command, target.Connection);
            throw;
        }
    }

    private static async ValueTask DisposeAsync(DbCommand? command, DbConnection connection)
    {
        if (command is not null)
        {
            await command.DisposeAsync();
        }
        await connection.DisposeAsync();
    }
}

/// <summary>Adapts a data reader to <see cref="IReportResultReader"/> and owns the connection behind it.</summary>
internal sealed class ReportResultReader : IReportResultReader
{
    private readonly DbConnection _connection;
    private readonly DbCommand _command;
    private readonly DbDataReader _reader;
    private readonly IDatabaseProvider _provider;

    public ReportResultReader(DbConnection connection, DbCommand command, DbDataReader reader, IDatabaseProvider provider)
    {
        _connection = connection;
        _command = command;
        _reader = reader;
        _provider = provider;

        var columns = new List<ReportColumn>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            columns.Add(new ReportColumn(string.IsNullOrEmpty(name) ? $"Column{i + 1}" : name, MapType(reader.GetFieldType(i))));
        }
        Columns = columns;
    }

    public IReadOnlyList<ReportColumn> Columns { get; }

    public ValueTask<bool> ReadAsync(CancellationToken ct = default) => new(_reader.ReadAsync(ct));

    public object? GetValue(int ordinal)
    {
        if (_reader.IsDBNull(ordinal))
        {
            return null;
        }

        object value;
        try
        {
            value = _reader.GetValue(ordinal);
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException or IOException or NotSupportedException)
        {
            // A value .NET cannot represent (an out-of-range date or number, a database-specific type).
            return _provider.ReadAsText(_reader, ordinal);
        }

        return value switch
        {
            string or bool or DateTime or decimal or double => value,
            float f => (double)f,
            sbyte or byte or short or ushort or int or uint or long => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.DateTime,
            byte[] bytes => $"[binary data, {bytes.Length:N0} bytes]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync();
        await _command.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ReportColumnType MapType(Type type)
    {
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return ReportColumnType.Date;
        }
        if (type == typeof(bool))
        {
            return ReportColumnType.Boolean;
        }
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)
            || type == typeof(long) || type == typeof(int) || type == typeof(short) || type == typeof(byte))
        {
            return ReportColumnType.Number;
        }
        return ReportColumnType.Text;
    }
}
