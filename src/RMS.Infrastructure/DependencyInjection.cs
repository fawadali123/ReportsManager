using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RMS.Core.Abstractions;
using RMS.Core.Services;
using RMS.Infrastructure.ActiveDirectory;
using RMS.Infrastructure.Data;
using RMS.Infrastructure.Data.Providers;
using RMS.Infrastructure.Export;
using RMS.Infrastructure.Reporting;

namespace RMS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the business services and their database (Oracle or SQL Server), LDAP and export implementations.
    /// Everything is added with TryAdd, so a registration made before this call wins. That is the
    /// extension point for swapping a piece (another identity source, another database) or for tests.
    /// </summary>
    public static IServiceCollection AddReportsManagement(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Options
        services.TryAddSingleton(configuration.GetSection("Reporting").Get<ReportingOptions>() ?? new ReportingOptions());
        services.TryAddSingleton(configuration.GetSection("Access").Get<AccessOptions>() ?? new AccessOptions());
        services.Configure<DirectoryOptions>(configuration.GetSection("Authentication"));
        services.Configure<PdfExportOptions>(configuration.GetSection("Export:Pdf"));
        services.TryAddSingleton(TimeProvider.System);

        // Data access (plain ADO.NET). Which product each connection uses comes from the Database section.
        var database = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();
        database.Validate();
        services.TryAddSingleton(database);
        services.TryAddSingleton<DatabaseProviders>();
        services.TryAddSingleton<ConnectionFactory>();
        services.TryAddSingleton<IConnectionFactory>(sp => sp.GetRequiredService<ConnectionFactory>());
        services.TryAddSingleton<IReportConnectionCatalog>(sp => sp.GetRequiredService<ConnectionFactory>());
        services.TryAddSingleton<AppDatabase>();

        services.TryAddScoped<IUserRepository, UserRepository>();
        services.TryAddScoped<IApplicationRepository, ApplicationRepository>();
        services.TryAddScoped<IReportCategoryRepository, ReportCategoryRepository>();
        services.TryAddScoped<IReportRepository, ReportRepository>();
        services.TryAddScoped<IUserReportRepository, UserReportRepository>();
        services.TryAddScoped<IReportRunLogRepository, ReportRunLogRepository>();

        // Report execution and export formats. Add another IReportExporter here to offer another format.
        services.TryAddScoped<IReportExecutor, ReportExecutor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, ExcelReportExporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, PdfReportExporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, CsvReportExporter>());

        // Sign-in
        var mode = configuration["Authentication:Mode"] ?? DirectoryOptions.LdapMode;
        if (string.Equals(mode, DirectoryOptions.DevelopmentMode, StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Authentication:Mode is 'Development', which accepts the fixed test accounts from configuration. "
                    + "That is only allowed when ASPNETCORE_ENVIRONMENT is Development. Set Authentication:Mode to 'Ldap'.");
            }
            services.TryAddSingleton<IDirectoryAuthenticator, DevelopmentDirectoryAuthenticator>();
        }
        else
        {
            services.TryAddSingleton<IDirectoryAuthenticator, LdapDirectoryAuthenticator>();
        }

        // Business services
        services.TryAddScoped<SignInService>();
        services.TryAddScoped<ReportService>();

        return services;
    }
}
