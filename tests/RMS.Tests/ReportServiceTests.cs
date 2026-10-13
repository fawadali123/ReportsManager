using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Services;
using RMS.Tests.Fakes;

namespace RMS.Tests;

public class ReportServiceTests
{
    private static readonly UserContext Admin = new(1, "fawad", "U001", "fawad@example.com", IsAdmin: true);
    private static readonly UserContext Viewer = new(2, "sara", "U002", "sara@example.com", IsAdmin: false);

    private readonly InMemoryStore _store = new();
    private readonly FakeReportExecutor _executor;
    private readonly ReportService _service;
    private readonly int _salesReportId;
    private readonly int _otherReportId;

    public ReportServiceTests()
    {
        _executor = new FakeReportExecutor(query => query.Sql.Contains("branches", StringComparison.OrdinalIgnoreCase)
            ? Table(["ID", "NAME"], [1m, "Riyadh"], [2m, "Jeddah"], [3m, "Dammam"])
            : Table(["REGION", "TOTAL"], ["Central", 1200.50m], ["Western", 800m], ["Eastern", 450.25m]));

        _service = new ReportService(_store, _store, _executor, _store, new FakeConnectionCatalog(),
            new ReportingOptions { MaxPreviewRows = 2 },
            new FixedClock(new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero)));

        var applicationId = _store.CreateAsync(new ApplicationInfo { Code = "ERP", Name = "ERP", ConnectionName = "ErpDb" }).Result;
        var categoryId = _store.CreateAsync(new ReportCategory { Code = "SALES", Name = "Sales" }).Result;

        _salesReportId = _store.CreateAsync(new Report
        {
            Code = "SALES_BY_REGION",
            Name = "Sales by region",
            ApplicationId = applicationId,
            CategoryId = categoryId,
            SqlQuery = """
                       SELECT region, SUM(amount) total FROM sales
                        WHERE sale_date >= @FromDate AND sale_date < @ToDate + 1
                          AND (@Branches IS NULL OR branch_id IN (@Branches))
                          AND (@MinAmount IS NULL OR amount >= @MinAmount)
                          AND owner = @RMS_USERNAME
                        GROUP BY region
                       """,
            Parameters =
            [
                new ReportParameter { Name = "FromDate", Label = "From", DataType = ParameterDataType.Date, DefaultValue = "START_OF_MONTH", SortOrder = 1 },
                new ReportParameter { Name = "ToDate", Label = "To", DataType = ParameterDataType.Date, DefaultValue = "TODAY", SortOrder = 2 },
                new ReportParameter
                {
                    Name = "Branches", Label = "Branches", DataType = ParameterDataType.Integer, Required = false, AllowMultiple = true,
                    LookupSource = "SELECT id, name FROM branches WHERE manager = @RMS_USERNAME", SortOrder = 3,
                },
                new ReportParameter { Name = "MinAmount", Label = "Minimum amount", DataType = ParameterDataType.Decimal, Required = false, SortOrder = 4 },
            ],
        }).Result;

        _otherReportId = _store.CreateAsync(new Report
        {
            Code = "ALL_REGIONS", Name = "All regions", ApplicationId = applicationId, CategoryId = categoryId,
            SqlQuery = "SELECT region, total FROM region_totals",
        }).Result;

        _store.Assignments.Add((Viewer.UserId!.Value, _salesReportId));
    }

    private static ReportTable Table(string[] columns, params object?[][] rows) => new()
    {
        Columns = [.. columns.Select(c => new ReportColumn(c, ReportColumnType.Text))],
        Rows = [.. rows],
    };

    private static Dictionary<string, string[]> Input(params (string Name, string[] Values)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Values);

    // ---- access ----

    [Fact]
    public async Task Viewer_sees_only_assigned_reports()
    {
        var catalog = await _service.GetCatalogAsync(Viewer);
        Assert.Equal(_salesReportId, Assert.Single(catalog).ReportId);
    }

    [Fact]
    public async Task Admin_sees_every_report()
    {
        Assert.Equal(2, (await _service.GetCatalogAsync(Admin)).Count);
    }

    [Fact]
    public async Task Viewer_cannot_run_a_report_that_is_not_assigned()
    {
        await Assert.ThrowsAsync<ReportNotFoundException>(() => _service.RunAsync(Viewer, _otherReportId, Input()));
        await Assert.ThrowsAsync<ReportNotFoundException>(() => _service.GetFormAsync(Viewer, _otherReportId));
        Assert.Empty(_executor.Queries);
    }

    [Fact]
    public async Task Viewer_loses_access_when_the_report_is_deactivated_but_admin_keeps_it()
    {
        _store.Reports.Single(r => r.Id == _salesReportId).Active = false;

        await Assert.ThrowsAsync<ReportNotFoundException>(() => _service.GetFormAsync(Viewer, _salesReportId));
        Assert.Empty(await _service.GetCatalogAsync(Viewer));
        Assert.NotNull(await _service.GetFormAsync(Admin, _salesReportId));
    }

    [Fact]
    public async Task Viewer_loses_access_when_the_application_is_deactivated()
    {
        _store.Applications[0].Active = false;
        await Assert.ThrowsAsync<ReportNotFoundException>(() => _service.GetFormAsync(Viewer, _salesReportId));
    }

    [Fact]
    public async Task Unknown_report_is_not_found()
    {
        await Assert.ThrowsAsync<ReportNotFoundException>(() => _service.GetFormAsync(Admin, 9999));
    }

    // ---- form ----

    [Fact]
    public async Task Form_lists_fields_with_defaults_and_choices()
    {
        var form = await _service.GetFormAsync(Viewer, _salesReportId);

        Assert.Equal(new List<string> { "FromDate", "ToDate", "Branches", "MinAmount" }, form.Fields.Select(f => f.Name).ToList());
        Assert.Equal("2026-10-01", form.Fields[0].DefaultValue);
        Assert.Equal("2026-10-10", form.Fields[1].DefaultValue);
        Assert.Equal(3, form.Fields[2].Options!.Count);
        Assert.Equal(new LookupItem("2", "Jeddah"), form.Fields[2].Options![1]);
        Assert.True(form.Fields[2].AllowMultiple);
        Assert.Null(form.Fields[3].Options);
    }

    [Fact]
    public async Task Lookup_query_receives_the_signed_in_user_and_the_reports_connection()
    {
        await _service.GetFormAsync(Viewer, _salesReportId);

        var lookup = Assert.Single(_executor.Queries);
        Assert.Equal("ErpDb", lookup.ConnectionName);
        Assert.Equal("SELECT id, name FROM branches WHERE manager = :b0", lookup.Sql);
        Assert.Equal("sara", lookup.Binds[0].Value);
    }

    [Fact]
    public async Task Report_without_parameters_has_an_empty_form()
    {
        Assert.Empty((await _service.GetFormAsync(Admin, _otherReportId)).Fields);
    }

    // ---- run ----

    [Fact]
    public async Task Run_binds_typed_values_and_system_parameters()
    {
        var result = await _service.RunAsync(Viewer, _salesReportId, Input(
            ("FromDate", ["2026-10-01"]), ("ToDate", ["2026-10-10"]), ("Branches", ["1", "3"]), ("MinAmount", ["100.5"])));

        var query = _executor.Queries.Last();
        Assert.Equal("ErpDb", query.ConnectionName);
        Assert.Contains("sale_date >= :b0 AND sale_date < :b1 + 1", query.Sql);
        Assert.Contains("(:b2 IS NULL OR branch_id IN (:b2_0, :b2_1))", query.Sql);
        Assert.DoesNotContain("@", query.Sql);

        object? Bind(string name) => query.Binds.Single(b => b.Name == name).Value;
        Assert.Equal(new DateTime(2026, 10, 1), Bind("b0"));
        Assert.Equal(new DateTime(2026, 10, 10), Bind("b1"));
        Assert.Equal("1,3", Bind("b2"));
        Assert.Equal(1L, Bind("b2_0"));
        Assert.Equal(3L, Bind("b2_1"));
        Assert.Equal(100.5m, Bind("b3"));
        Assert.Equal("sara", Bind("b4"));

        Assert.Equal(2, result.Table.Rows.Count);   // MaxPreviewRows
        Assert.True(result.Table.Truncated);
    }

    [Fact]
    public async Task Optional_parameters_left_blank_are_bound_as_null()
    {
        await _service.RunAsync(Viewer, _salesReportId, Input(("FromDate", ["2026-10-01"]), ("ToDate", ["2026-10-10"])));

        var query = _executor.Queries.Last();
        Assert.Contains("(:b2 IS NULL OR branch_id IN (:b2_0))", query.Sql);
        Assert.Null(query.Binds.Single(b => b.Name == "b2").Value);
        Assert.Null(query.Binds.Single(b => b.Name == "b3").Value);
    }

    [Fact]
    public async Task Missing_required_and_malformed_values_are_reported_per_field()
    {
        var error = await Assert.ThrowsAsync<ReportValidationException>(() => _service.RunAsync(Viewer, _salesReportId, Input(
            ("ToDate", ["10/10/2026"]), ("MinAmount", ["lots"]))));

        Assert.Equal(3, error.FieldErrors.Count);
        Assert.Equal("From is required.", error.FieldErrors["FromDate"]);
        Assert.True(error.FieldErrors.ContainsKey("ToDate"));
        Assert.True(error.FieldErrors.ContainsKey("MinAmount"));
        Assert.Empty(_executor.Queries.Where(q => !q.Sql.Contains("branches")));
    }

    [Fact]
    public async Task A_dropdown_value_that_was_not_offered_is_refused()
    {
        var error = await Assert.ThrowsAsync<ReportValidationException>(() => _service.RunAsync(Viewer, _salesReportId, Input(
            ("FromDate", ["2026-10-01"]), ("ToDate", ["2026-10-10"]), ("Branches", ["1", "99"]))));

        Assert.True(error.FieldErrors.ContainsKey("Branches"));
    }

    [Fact]
    public async Task Posted_values_for_unknown_parameters_are_ignored()
    {
        await _service.RunAsync(Admin, _otherReportId, Input(("Injected", ["1; DROP TABLE x"])));
        Assert.Equal("SELECT region, total FROM region_totals", _executor.Queries.Last().Sql);
        Assert.Empty(_executor.Queries.Last().Binds);
    }

    [Fact]
    public async Task Every_run_is_written_to_the_log()
    {
        await _service.RunAsync(Viewer, _salesReportId, Input(("FromDate", ["2026-10-01"]), ("ToDate", ["2026-10-10"])));

        var entry = Assert.Single(_store.RunLog);
        Assert.Equal("View", entry.Action);
        Assert.Equal("sara", entry.Username);
        Assert.Equal("Sales by region", entry.ReportName);
        Assert.True(entry.Success);
        Assert.Equal(2, entry.RowCount);
        Assert.Contains("2026-10-01", entry.Parameters);
    }

    [Fact]
    public async Task A_database_error_is_logged_and_rethrown()
    {
        var failing = new FakeReportExecutor(_ => throw new ReportExecutionException("ORA-00942: table or view does not exist", "ORA-00942"));
        var service = new ReportService(_store, _store, failing, _store, new FakeConnectionCatalog(), new ReportingOptions(), TimeProvider.System);

        var error = await Assert.ThrowsAsync<ReportExecutionException>(() => service.RunAsync(Admin, _otherReportId, Input()));

        Assert.Equal("ORA-00942", error.ErrorCode);
        Assert.False(Assert.Single(_store.RunLog).Success);
    }

    [Fact]
    public async Task A_report_whose_sql_is_not_a_select_does_not_run()
    {
        _store.Reports.Single(r => r.Id == _otherReportId).SqlQuery = "DELETE FROM region_totals";

        await Assert.ThrowsAsync<ReportValidationException>(() => _service.RunAsync(Admin, _otherReportId, Input()));
        Assert.Empty(_executor.Queries);
    }
}
