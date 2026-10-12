using RMS.Core.Domain;

namespace RMS.Infrastructure.Data;

/// <summary>
/// Bound from the "Database" configuration section. Says which database product each connection string is for.
/// </summary>
/// <example>
/// <code>
/// "Database": {
///   "Provider": "Oracle",                               // every connection, unless listed below
///   "ConnectionProviders": { "ErpDb": "SqlServer" }      // exceptions, by connection string name
/// }
/// </code>
/// </example>
public sealed class DatabaseOptions
{
    /// <summary>"Oracle" or "SqlServer". Applies to RmsDb and to every report connection without its own entry.</summary>
    public string Provider { get; set; } = "Oracle";

    /// <summary>Connection string name to provider, for report connections on a different product than the default.</summary>
    public Dictionary<string, string> ConnectionProviders { get; set; } = [];

    public DatabaseKind KindFor(string connectionName)
    {
        var overrideEntry = ConnectionProviders.FirstOrDefault(
            entry => string.Equals(entry.Key, connectionName, StringComparison.OrdinalIgnoreCase));

        return overrideEntry.Key is null
            ? Parse(Provider, "Database:Provider")
            : Parse(overrideEntry.Value, $"Database:ConnectionProviders:{overrideEntry.Key}");
    }

    /// <summary>Fails with a message naming the setting when a provider name is not recognised.</summary>
    public void Validate()
    {
        Parse(Provider, "Database:Provider");
        foreach (var entry in ConnectionProviders)
        {
            Parse(entry.Value, $"Database:ConnectionProviders:{entry.Key}");
        }
    }

    private static DatabaseKind Parse(string? value, string setting) =>
        DatabaseKinds.TryParse(value, out var kind)
            ? kind
            : throw new InvalidOperationException(
                $"The setting {setting} is '{value}', which is not a supported database. Use \"Oracle\" or \"SqlServer\".");
}
