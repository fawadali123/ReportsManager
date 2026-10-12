using System.Data.Common;
using RMS.Infrastructure.Data.Providers;
using RMS.Core.Abstractions;
using RMS.Core.Domain;

namespace RMS.Infrastructure.Data;

internal sealed class ReportRepository(AppDatabase db) : IReportRepository
{
    private const string Select =
        """
        SELECT r.ID, r.CODE, r.NAME, r.DESCRIPTION, r.APPLICATION_ID, r.REPORTS_CATEGORY_ID,
               r.SQL_QUERY, r.QUERY_TYPE, r.ACTIVE,
               a.NAME AS APPLICATION_NAME, a.ACTIVE AS APPLICATION_ACTIVE, a.CONNECTION_NAME,
               c.NAME AS CATEGORY_NAME, c.ACTIVE AS CATEGORY_ACTIVE
          FROM RMS_REPORTS r
          JOIN RMS_APPLICATIONS a ON a.ID = r.APPLICATION_ID
          JOIN RMS_REPORT_CATEGORIES c ON c.ID = r.REPORTS_CATEGORY_ID
        """;

    private const string InsertParameter =
        """
        INSERT INTO RMS_REPORT_PARAMETERS
               (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
        VALUES (@p_report_id, @p_name, @p_label, @p_data_type, @p_required, @p_allow_multiple, @p_default_value,
                @p_lookup_source, @p_help_text, @p_sort_order)
        """;

    public async Task<IReadOnlyList<Report>> GetAllAsync(CancellationToken ct = default) =>
        await db.QueryAsync($"{Select} ORDER BY UPPER(a.NAME), UPPER(c.NAME), UPPER(r.NAME)", Map, ct);

    public async Task<Report?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var report = await db.QuerySingleAsync($"{Select} WHERE r.ID = @p_id", Map, ct, P.Int("p_id", id));
        if (report is null)
        {
            return null;
        }

        report.Parameters = await db.QueryAsync(
            """
            SELECT ID, REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE,
                   LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER
              FROM RMS_REPORT_PARAMETERS
             WHERE REPORT_ID = @p_id
             ORDER BY SORT_ORDER, ID
            """,
            MapParameter, ct, P.Int("p_id", id));

        return report;
    }

    public async Task<IReadOnlyList<ReportCatalogItem>> GetCatalogAsync(
        int? userId, bool includeUnavailable, CancellationToken ct = default) =>
        await db.QueryAsync(
            """
            SELECT r.ID AS REPORT_ID, r.CODE AS REPORT_CODE, r.NAME AS REPORT_NAME, r.DESCRIPTION,
                   a.ID AS APPLICATION_ID, a.NAME AS APPLICATION_NAME,
                   c.ID AS CATEGORY_ID, c.NAME AS CATEGORY_NAME,
                   CASE WHEN r.ACTIVE = 1 AND a.ACTIVE = 1 AND c.ACTIVE = 1 THEN 1 ELSE 0 END AS AVAILABLE
              FROM RMS_REPORTS r
              JOIN RMS_APPLICATIONS a ON a.ID = r.APPLICATION_ID
              JOIN RMS_REPORT_CATEGORIES c ON c.ID = r.REPORTS_CATEGORY_ID
             WHERE (@p_user_id IS NULL
                    OR EXISTS (SELECT 1 FROM RMS_USER_REPORTS ur WHERE ur.REPORT_ID = r.ID AND ur.USER_ID = @p_user_id))
               AND (@p_include_all = 1 OR (r.ACTIVE = 1 AND a.ACTIVE = 1 AND c.ACTIVE = 1))
             ORDER BY UPPER(a.NAME), UPPER(c.NAME), UPPER(r.NAME)
            """,
            r => new ReportCatalogItem
            {
                ReportId = r.Int("REPORT_ID"),
                ReportCode = r.Text("REPORT_CODE"),
                ReportName = r.Text("REPORT_NAME"),
                Description = r.TextOrNull("DESCRIPTION"),
                ApplicationId = r.Int("APPLICATION_ID"),
                ApplicationName = r.Text("APPLICATION_NAME"),
                CategoryId = r.Int("CATEGORY_ID"),
                CategoryName = r.Text("CATEGORY_NAME"),
                Available = r.Bool("AVAILABLE"),
            },
            ct, P.Int("p_user_id", userId), P.Bool("p_include_all", includeUnavailable));

    public Task<int> CreateAsync(Report report, CancellationToken ct = default) =>
        db.InTransactionAsync(async session =>
        {
            var id = await session.InsertAsync(
                """
                INSERT INTO RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, SQL_QUERY, QUERY_TYPE, ACTIVE)
                VALUES (@p_code, @p_name, @p_description, @p_application_id, @p_category_id, @p_sql_query, @p_query_type, @p_active)
                """,
                ct, Values(report));

            await InsertParametersAsync(session, id, report.Parameters, ct);
            return id;
        }, ct);

    public Task UpdateAsync(Report report, CancellationToken ct = default) =>
        db.InTransactionAsync(async session =>
        {
            await session.ExecuteAsync(
                """
                UPDATE RMS_REPORTS
                   SET CODE = @p_code, NAME = @p_name, DESCRIPTION = @p_description, APPLICATION_ID = @p_application_id,
                       REPORTS_CATEGORY_ID = @p_category_id, SQL_QUERY = @p_sql_query, QUERY_TYPE = @p_query_type,
                       ACTIVE = @p_active, UPDATED_AT = {now}
                 WHERE ID = @p_id
                """,
                ct, [.. Values(report), P.Int("p_id", report.Id)]);

            // Parameter settings are small and edited as a set, so they are simply replaced.
            await session.ExecuteAsync(
                "DELETE FROM RMS_REPORT_PARAMETERS WHERE REPORT_ID = @p_id", ct, P.Int("p_id", report.Id));
            await InsertParametersAsync(session, report.Id, report.Parameters, ct);
            return 0;
        }, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        // Parameter settings and user assignments go with it (ON DELETE CASCADE); run history keeps the report name.
        db.ExecuteAsync("DELETE FROM RMS_REPORTS WHERE ID = @p_id", ct, P.Int("p_id", id));

    private static async Task InsertParametersAsync(
        DbSession session, int reportId, IEnumerable<ReportParameter> parameters, CancellationToken ct)
    {
        foreach (var parameter in parameters)
        {
            await session.ExecuteAsync(InsertParameter, ct,
                P.Int("p_report_id", reportId),
                P.Text("p_name", parameter.Name.Trim()),
                P.Text("p_label", parameter.Label.Trim()),
                P.Text("p_data_type", parameter.DataType.ToString()),
                P.Bool("p_required", parameter.Required),
                P.Bool("p_allow_multiple", parameter.AllowMultiple),
                P.Text("p_default_value", parameter.DefaultValue?.Trim()),
                P.LongText("p_lookup_source", parameter.LookupSource?.Trim()),
                P.Text("p_help_text", parameter.HelpText?.Trim()),
                P.Int("p_sort_order", parameter.SortOrder));
        }
    }

    private static DbParam[] Values(Report report) =>
    [
        P.Text("p_code", report.Code.Trim()),
        P.Text("p_name", report.Name.Trim()),
        P.Text("p_description", report.Description?.Trim()),
        P.Int("p_application_id", report.ApplicationId),
        P.Int("p_category_id", report.CategoryId),
        P.LongText("p_sql_query", report.SqlQuery),
        P.Text("p_query_type", report.QueryType.ToString()),
        P.Bool("p_active", report.Active),
    ];

    private static Report Map(DbDataReader r) => new()
    {
        Id = r.Int("ID"),
        Code = r.Text("CODE"),
        Name = r.Text("NAME"),
        Description = r.TextOrNull("DESCRIPTION"),
        ApplicationId = r.Int("APPLICATION_ID"),
        CategoryId = r.Int("REPORTS_CATEGORY_ID"),
        SqlQuery = r.Text("SQL_QUERY"),
        QueryType = Enum.TryParse<ReportQueryType>(r.Text("QUERY_TYPE"), ignoreCase: true, out var type) ? type : ReportQueryType.Select,
        Active = r.Bool("ACTIVE"),
        ApplicationName = r.Text("APPLICATION_NAME"),
        ApplicationActive = r.Bool("APPLICATION_ACTIVE"),
        ConnectionName = r.TextOrNull("CONNECTION_NAME"),
        CategoryName = r.Text("CATEGORY_NAME"),
        CategoryActive = r.Bool("CATEGORY_ACTIVE"),
    };

    private static ReportParameter MapParameter(DbDataReader r) => new()
    {
        Id = r.Int("ID"),
        ReportId = r.Int("REPORT_ID"),
        Name = r.Text("NAME"),
        Label = r.Text("LABEL"),
        DataType = Enum.TryParse<ParameterDataType>(r.Text("DATA_TYPE"), ignoreCase: true, out var type) ? type : ParameterDataType.Text,
        Required = r.Bool("REQUIRED"),
        AllowMultiple = r.Bool("ALLOW_MULTIPLE"),
        DefaultValue = r.TextOrNull("DEFAULT_VALUE"),
        LookupSource = r.TextOrNull("LOOKUP_SOURCE"),
        HelpText = r.TextOrNull("HELP_TEXT"),
        SortOrder = r.Int("SORT_ORDER"),
    };
}
