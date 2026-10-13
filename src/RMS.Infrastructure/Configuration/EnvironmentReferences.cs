using Microsoft.Extensions.Configuration;

namespace RMS.Infrastructure.Configuration;

/// <summary>
/// Lets any setting name an environment variable instead of holding the value itself, so passwords and
/// connection strings can stay out of the appsettings files:
/// <code>
/// "ConnectionStrings": { "RmsDb": "env:RMS_DB_CONNECTION" }
/// "Email": { "Smtp": { "Password": "env:RMS_SMTP_PASSWORD" } }
/// </code>
/// A value that does not start with <c>env:</c> is used as written. Each setting decides for itself, so plain
/// values (handy on a development machine) and environment variables can be mixed.
/// </summary>
public static class EnvironmentReferences
{
    public const string Prefix = "env:";

    /// <summary>
    /// Replaces every <c>env:NAME</c> value with the content of environment variable NAME. Call it after all
    /// configuration files are added. Values are read once, at startup: restart the application (or recycle the
    /// IIS application pool, or restart the Windows service) after changing a variable.
    /// </summary>
    /// <exception cref="InvalidOperationException">A referenced variable is not set. The message names the settings and variables, never values.</exception>
    public static IConfigurationManager ResolveEnvironmentReferences(
        this IConfigurationManager configuration, Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;

        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var (key, value) in ((IConfiguration)configuration).AsEnumerable())
        {
            if (value is null || !value.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = value.Trim()[Prefix.Length..].Trim();
            var content = name.Length == 0 ? null : readVariable(name);
            if (string.IsNullOrEmpty(content))
            {
                missing.Add(name.Length == 0 ? $"{key} (no variable name after \"{Prefix}\")" : $"{key} -> {name}");
                continue;
            }

            resolved[key] = content;
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "These settings refer to environment variables that are not set: " + string.Join("; ", missing)
                + ". Set the variables for the account the application runs under (for a Windows service or IIS, "
                + "set them as System variables), then restart it.");
        }

        if (resolved.Count > 0)
        {
            // Added last, so these values win over the "env:" text in the files.
            configuration.AddInMemoryCollection(resolved);
        }

        return configuration;
    }
}
