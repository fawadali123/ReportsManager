using System.Data.Common;
using RMS.Infrastructure.Data.Providers;
using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Infrastructure.Data;

internal sealed class ApplicationRepository(AppDatabase db) : IApplicationRepository
{
    private const string Select = "SELECT ID, CODE, NAME, DESCRIPTION, ACTIVE, CONNECTION_NAME FROM RMS_APPLICATIONS";

    public async Task<IReadOnlyList<ApplicationInfo>> GetAllAsync(CancellationToken ct = default) =>
        await db.QueryAsync($"{Select} ORDER BY UPPER(NAME)", Map, ct);

    public Task<ApplicationInfo?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.QuerySingleAsync($"{Select} WHERE ID = @p_id", Map, ct, P.Int("p_id", id));

    public Task<int> CreateAsync(ApplicationInfo application, CancellationToken ct = default) =>
        db.InsertAsync(
            """
            INSERT INTO RMS_APPLICATIONS (CODE, NAME, DESCRIPTION, ACTIVE, CONNECTION_NAME)
            VALUES (@p_code, @p_name, @p_description, @p_active, @p_connection_name)
            """,
            ct, Values(application));

    public Task UpdateAsync(ApplicationInfo application, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE RMS_APPLICATIONS
               SET CODE = @p_code, NAME = @p_name, DESCRIPTION = @p_description, ACTIVE = @p_active,
                   CONNECTION_NAME = @p_connection_name, UPDATED_AT = {now}
             WHERE ID = @p_id
            """,
            ct, [.. Values(application), P.Int("p_id", application.Id)]);

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM RMS_APPLICATIONS WHERE ID = @p_id", ct, P.Int("p_id", id));

    private static DbParam[] Values(ApplicationInfo application) =>
    [
        P.Text("p_code", application.Code.Trim()),
        P.Text("p_name", application.Name.Trim()),
        P.Text("p_description", application.Description?.Trim()),
        P.Bool("p_active", application.Active),
        P.Text("p_connection_name", application.ConnectionName?.Trim()),
    ];

    private static ApplicationInfo Map(DbDataReader r) => new()
    {
        Id = r.Int("ID"),
        Code = r.Text("CODE"),
        Name = r.Text("NAME"),
        Description = r.TextOrNull("DESCRIPTION"),
        Active = r.Bool("ACTIVE"),
        ConnectionName = r.TextOrNull("CONNECTION_NAME"),
    };
}

internal sealed class ReportCategoryRepository(AppDatabase db) : IReportCategoryRepository
{
    private const string Select = "SELECT ID, CODE, NAME, DESCRIPTION, ACTIVE FROM RMS_REPORT_CATEGORIES";

    public async Task<IReadOnlyList<ReportCategory>> GetAllAsync(CancellationToken ct = default) =>
        await db.QueryAsync($"{Select} ORDER BY UPPER(NAME)", Map, ct);

    public Task<ReportCategory?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.QuerySingleAsync($"{Select} WHERE ID = @p_id", Map, ct, P.Int("p_id", id));

    public Task<int> CreateAsync(ReportCategory category, CancellationToken ct = default) =>
        db.InsertAsync(
            """
            INSERT INTO RMS_REPORT_CATEGORIES (CODE, NAME, DESCRIPTION, ACTIVE)
            VALUES (@p_code, @p_name, @p_description, @p_active)
            """,
            ct, Values(category));

    public Task UpdateAsync(ReportCategory category, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE RMS_REPORT_CATEGORIES
               SET CODE = @p_code, NAME = @p_name, DESCRIPTION = @p_description, ACTIVE = @p_active,
                   UPDATED_AT = {now}
             WHERE ID = @p_id
            """,
            ct, [.. Values(category), P.Int("p_id", category.Id)]);

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM RMS_REPORT_CATEGORIES WHERE ID = @p_id", ct, P.Int("p_id", id));

    private static DbParam[] Values(ReportCategory category) =>
    [
        P.Text("p_code", category.Code.Trim()),
        P.Text("p_name", category.Name.Trim()),
        P.Text("p_description", category.Description?.Trim()),
        P.Bool("p_active", category.Active),
    ];

    private static ReportCategory Map(DbDataReader r) => new()
    {
        Id = r.Int("ID"),
        Code = r.Text("CODE"),
        Name = r.Text("NAME"),
        Description = r.TextOrNull("DESCRIPTION"),
        Active = r.Bool("ACTIVE"),
    };
}
