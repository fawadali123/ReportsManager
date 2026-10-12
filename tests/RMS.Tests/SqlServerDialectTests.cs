using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Services;
using RMS.Tests.Fakes;

namespace RMS.Tests;

/// <summary>What changes when a report runs on SQL Server instead of Oracle.</summary>
public class SqlServerDialectTests
{
    private const DatabaseKind SqlServer = DatabaseKind.SqlServer;

    private static List<string> Names(string sql, ReportQueryType type = ReportQueryType.Select) =>
        [.. ReportParameterResolver.GetParameterNames(sql, SqlServer, type)];

    private static Dictionary<string, ParameterValue> Values(params (string Name, ParameterValue Value)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Value, StringComparer.OrdinalIgnoreCase);

    // ---- scanner ----

    [Fact]
    public void Parameters_are_found_the_same_way()
    {
        Assert.Equal(new List<string> { "FromDate", "Region" },
            Names("SELECT TOP (100) * FROM dbo.Sales WHERE SaleDate >= @FromDate AND Region = @Region"));
    }

    [Theory]
    [InlineData("SELECT [e@mail], [user@name] FROM dbo.Contacts")]
    [InlineData("SELECT [odd]]@name] FROM dbo.T")]
    [InlineData("SELECT @@ROWCOUNT AS n, @@VERSION AS v")]
    [InlineData("SELECT N'contact @support' AS note")]
    public void Brackets_globals_and_literals_do_not_hide_parameters(string sql)
    {
        Assert.Empty(Names(sql));
    }

    [Fact]
    public void Brackets_are_plain_text_on_oracle()
    {
        // An Oracle MODEL clause uses square brackets around cell references.
        var names = ReportParameterResolver.GetParameterNames("SELECT 1 FROM t MODEL DIMENSION BY (y) MEASURES (v) RULES (v[@Year] = 1)");
        Assert.Equal(new List<string> { "Year" }, names.ToList());
    }

    [Fact]
    public void Procedure_argument_names_are_not_report_parameters()
    {
        var names = Names("EXEC dbo.usp_SalesByRegion @From = @FromDate, @To=@ToDate, @Top = 10", ReportQueryType.StoredProcedure);
        Assert.Equal(new List<string> { "FromDate", "ToDate" }, names);
    }

    [Fact]
    public void In_a_query_a_parameter_before_an_equals_sign_is_still_a_parameter()
    {
        Assert.Equal(new List<string> { "Region" }, Names("SELECT * FROM dbo.Sales WHERE @Region = Region"));
    }

    // ---- translator ----

    [Fact]
    public void Binds_use_the_at_prefix()
    {
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM dbo.Sales WHERE SaleDate >= @FromDate AND (@Region IS NULL OR Region = @Region)",
            ReportQueryType.Select,
            Values(("FromDate", ParameterValue.Single(ParameterDataType.Date, new DateTime(2026, 1, 1))),
                   ("Region", ParameterValue.Text(null))),
            "ErpDb", SqlServer);

        Assert.Equal("SELECT * FROM dbo.Sales WHERE SaleDate >= @b0 AND (@b1 IS NULL OR Region = @b1)", query.Sql);
        Assert.Equal(new List<string> { "b0", "b1" }, query.Binds.Select(b => b.Name).ToList());
    }

    [Fact]
    public void Multi_value_parameters_expand_the_same_way()
    {
        var ids = new ParameterValue(ParameterDataType.Integer, true, [1L, 2L]);
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM dbo.Sales WHERE (@Ids IS NULL OR BranchId IN (@Ids))", ReportQueryType.Select, Values(("Ids", ids)), null, SqlServer);

        Assert.Equal("SELECT * FROM dbo.Sales WHERE (@b0 IS NULL OR BranchId IN (@b0_0, @b0_1))", query.Sql);
    }

    [Theory]
    [InlineData(";WITH x AS (SELECT 1 AS n) SELECT * FROM x;", "WITH x AS (SELECT 1 AS n) SELECT * FROM x\n")]
    [InlineData("SELECT @@SERVERNAME AS s, [a@b] FROM dbo.T", "SELECT @@SERVERNAME AS s, [a@b] FROM dbo.T")]
    public void Terminators_globals_and_brackets_pass_through(string sql, string expected)
    {
        Assert.Equal(expected, ReportSqlTranslator.Translate(sql, ReportQueryType.Select, Values(), null, SqlServer).Sql);
    }

    [Theory]
    [InlineData("EXEC dbo.usp_SalesByRegion @FromDate, @ToDate", "EXEC dbo.usp_SalesByRegion @b0, @b1")]
    [InlineData("dbo.usp_SalesByRegion @FromDate, @ToDate;", "EXEC dbo.usp_SalesByRegion @b0, @b1")]
    [InlineData("execute [dbo].[usp Sales] @From = @FromDate, @To = @ToDate", "EXEC [dbo].[usp Sales] @From = @b0, @To = @b1")]
    public void Procedure_calls_become_exec_statements(string sql, string expected)
    {
        var query = ReportSqlTranslator.Translate(sql, ReportQueryType.StoredProcedure,
            Values(("FromDate", ParameterValue.Single(ParameterDataType.Date, new DateTime(2026, 1, 1))),
                   ("ToDate", ParameterValue.Single(ParameterDataType.Date, new DateTime(2026, 1, 31)))),
            null, SqlServer);

        Assert.Equal(expected, query.Sql.Trim());
        Assert.False(query.Binds.Any(b => b.IsCursor));
    }

    // ---- validator ----

    [Theory]
    [InlineData("SELECT TOP (10) * FROM dbo.Sales WHERE Region = @Region")]
    [InlineData(";WITH x AS (SELECT 1 AS n) SELECT * FROM x")]
    [InlineData("SELECT [a;b] FROM dbo.T")]
    public void Accepts_select_statements(string sql)
    {
        Assert.Empty(ReportSqlValidator.Validate(sql, ReportQueryType.Select, SqlServer));
    }

    [Theory]
    [InlineData("EXEC dbo.usp_SalesByRegion @FromDate, @ToDate")]
    [InlineData("dbo.usp_SalesByRegion")]
    [InlineData("EXECUTE Reporting.dbo.usp_Sales @From = @FromDate")]
    [InlineData("EXEC [dbo].[usp Sales] @FromDate;")]
    public void Accepts_procedure_calls(string sql)
    {
        Assert.Empty(ReportSqlValidator.Validate(sql, ReportQueryType.StoredProcedure, SqlServer));
    }

    [Theory]
    [InlineData("EXEC dbo.usp_Sales @FromDate, @RMS_CURSOR")]        // Oracle-only placeholder
    [InlineData("DELETE FROM dbo.Sales")]
    [InlineData("SELECT * FROM dbo.Sales")]
    [InlineData("EXEC dbo.usp_A; EXEC dbo.usp_B")]
    [InlineData("EXEC")]
    public void Rejects_what_is_not_a_single_procedure_call(string sql)
    {
        Assert.NotEmpty(ReportSqlValidator.Validate(sql, ReportQueryType.StoredProcedure, SqlServer));
    }

    [Fact]
    public void Oracle_procedure_syntax_is_refused_for_sql_server_and_the_other_way_round()
    {
        Assert.NotEmpty(ReportSqlValidator.Validate("pkg.proc(@A, @RMS_CURSOR)", ReportQueryType.StoredProcedure, SqlServer));
        Assert.NotEmpty(ReportSqlValidator.Validate("EXEC dbo.usp_Sales @A", ReportQueryType.StoredProcedure, DatabaseKind.Oracle));
    }

    // ---- provider names in configuration ----

    [Theory]
    [InlineData("Oracle", DatabaseKind.Oracle)]
    [InlineData("oracle", DatabaseKind.Oracle)]
    [InlineData("SqlServer", DatabaseKind.SqlServer)]
    [InlineData("SQL Server", DatabaseKind.SqlServer)]
    [InlineData("mssql", DatabaseKind.SqlServer)]
    public void Provider_names_are_read_leniently(string text, DatabaseKind expected)
    {
        Assert.True(DatabaseKinds.TryParse(text, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MySql")]
    [InlineData(null)]
    public void Unknown_provider_names_are_refused(string? text)
    {
        Assert.False(DatabaseKinds.TryParse(text, out _));
    }

    // ---- one site, two databases ----

    [Fact]
    public async Task Each_report_is_translated_for_the_database_its_application_points_at()
    {
        var store = new InMemoryStore();
        var executor = new FakeReportExecutor(_ => new ReportTable());
        var catalog = new FakeConnectionCatalog { Kinds = { ["ErpDb"] = DatabaseKind.Oracle, ["CrmDb"] = DatabaseKind.SqlServer } };
        var service = new ReportService(store, store, executor, store, catalog, new ReportingOptions(), TimeProvider.System);
        var admin = new UserContext(1, "fawad", "U001", null, IsAdmin: true);

        var erp = await store.CreateAsync(new ApplicationInfo { Code = "ERP", Name = "ERP", ConnectionName = "ErpDb" });
        var crm = await store.CreateAsync(new ApplicationInfo { Code = "CRM", Name = "CRM", ConnectionName = "CrmDb" });
        var category = await store.CreateAsync(new ReportCategory { Code = "C", Name = "C" });

        var onOracle = await store.CreateAsync(new Report
        {
            Code = "A", Name = "A", ApplicationId = erp, CategoryId = category,
            SqlQuery = "SELECT * FROM orders WHERE owner = @RMS_USERNAME AND region = @Region",
        });
        var onSqlServer = await store.CreateAsync(new Report
        {
            Code = "B", Name = "B", ApplicationId = crm, CategoryId = category, QueryType = ReportQueryType.StoredProcedure,
            SqlQuery = "EXEC dbo.usp_Leads @Owner = @RMS_USERNAME, @Region = @Region",
        });

        var input = new Dictionary<string, string[]> { ["Region"] = ["Central"] };
        await service.RunAsync(admin, onOracle, input);
        await service.RunAsync(admin, onSqlServer, input);

        Assert.Equal("SELECT * FROM orders WHERE owner = :b0 AND region = :b1", executor.Queries[0].Sql);
        Assert.Equal("ErpDb", executor.Queries[0].ConnectionName);
        Assert.Equal("EXEC dbo.usp_Leads @Owner = @b0, @Region = @b1", executor.Queries[1].Sql);
        Assert.Equal("CrmDb", executor.Queries[1].ConnectionName);

        var form = await service.GetFormAsync(admin, onSqlServer);
        Assert.Equal("Region", Assert.Single(form.Fields).Name);   // @Owner and @Region= are the procedure's own names
    }
}
