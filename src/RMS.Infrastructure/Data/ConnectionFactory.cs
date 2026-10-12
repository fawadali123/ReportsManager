using System.Data.Common;
using Microsoft.Extensions.Configuration;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Services;
using RMS.Infrastructure.Data.Providers;

namespace RMS.Infrastructure.Data;

/// <summary>A new, unopened connection together with the provider that knows how to talk to it.</summary>
internal readonly record struct ProviderConnection(DbConnection Connection, IDatabaseProvider Provider, string Name);

internal interface IConnectionFactory
{
    /// <summary>Connection to the database that holds the RMS_* tables.</summary>
    ProviderConnection CreateAppConnection();

    /// <summary>Connection a report runs on. Null or empty means the default report connection.</summary>
    ProviderConnection CreateReportConnection(string? connectionName);
}

/// <summary>
/// Resolves connections by name from the ConnectionStrings configuration section, and their database
/// product from the Database section. "RmsDb" is the application's own database; every other entry
/// is a database that reports can target, and each may be Oracle or SQL Server independently.
/// </summary>
internal sealed class ConnectionFactory(
    IConfiguration configuration,
    ReportingOptions reporting,
    DatabaseOptions database,
    DatabaseProviders providers) : IConnectionFactory, IReportConnectionCatalog
{
    public const string AppConnectionName = "RmsDb";

    public string DefaultConnectionName => reporting.DefaultConnectionName;

    public IReadOnlyList<string> ConnectionNames =>
        configuration.GetSection("ConnectionStrings").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => c.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public DatabaseKind GetKind(string? connectionName) => database.KindFor(Resolve(connectionName));

    public ProviderConnection CreateAppConnection()
    {
        var connectionString = configuration.GetConnectionString(AppConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{AppConnectionName}' is not configured. Set ConnectionStrings:{AppConnectionName} in appsettings.json, user secrets or an environment variable.");
        }

        var provider = providers.Get(database.KindFor(AppConnectionName));
        return new ProviderConnection(provider.CreateConnection(connectionString), provider, AppConnectionName);
    }

    public ProviderConnection CreateReportConnection(string? connectionName)
    {
        var name = Resolve(connectionName);
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ReportExecutionException(
                $"The database connection '{name}' is not configured on the server. Add it under ConnectionStrings, or pick another connection for the application.");
        }

        var provider = providers.Get(database.KindFor(name));
        return new ProviderConnection(provider.CreateConnection(connectionString), provider, name);
    }

    private string Resolve(string? connectionName) =>
        string.IsNullOrWhiteSpace(connectionName) ? reporting.DefaultConnectionName : connectionName.Trim();
}
