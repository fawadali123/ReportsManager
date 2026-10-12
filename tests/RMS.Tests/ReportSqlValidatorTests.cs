using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Services;

namespace RMS.Tests;

public class ReportSqlValidatorTests
{
    [Theory]
    [InlineData("SELECT * FROM t")]
    [InlineData("  select * from t where a = @A;  ")]
    [InlineData("-- monthly sales\nSELECT * FROM t")]
    [InlineData("/* header */ WITH x AS (SELECT 1 n FROM dual) SELECT * FROM x")]
    [InlineData("(SELECT 1 FROM dual) UNION ALL (SELECT 2 FROM dual)")]
    [InlineData("SELECT 'a;b' FROM dual")]
    [InlineData("SELECT * FROM t WHERE owner = @RMS_USERNAME")]
    public void Accepts_single_select_statements(string sql)
    {
        Assert.Empty(ReportSqlValidator.Validate(sql, ReportQueryType.Select));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- only a comment")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("DROP TABLE t")]
    [InlineData("BEGIN NULL; END;")]
    [InlineData("SELECT 1 FROM dual; DELETE FROM t")]
    [InlineData("SELECT * FROM t WHERE a = @RMS_NOT_A_THING")]
    [InlineData("SELECT @RMS_CURSOR FROM dual")]
    public void Rejects_anything_else(string sql)
    {
        Assert.NotEmpty(ReportSqlValidator.Validate(sql, ReportQueryType.Select));
    }

    [Theory]
    [InlineData("get_sales(@Year, @RMS_CURSOR)")]
    [InlineData("pkg_reports.sales_by_region(p_from => @FromDate, p_to => @ToDate, p_result => @RMS_CURSOR)")]
    [InlineData("hr.pkg_reports.headcount(@RMS_CURSOR);")]
    public void Accepts_procedure_calls_with_a_cursor(string sql)
    {
        Assert.Empty(ReportSqlValidator.Validate(sql, ReportQueryType.StoredProcedure));
    }

    [Theory]
    [InlineData("get_sales(@Year)")]
    [InlineData("BEGIN get_sales(@Year, @RMS_CURSOR); END;")]
    [InlineData("CALL get_sales(@Year, @RMS_CURSOR)")]
    [InlineData("SELECT * FROM t")]
    public void Rejects_malformed_procedure_calls(string sql)
    {
        Assert.NotEmpty(ReportSqlValidator.Validate(sql, ReportQueryType.StoredProcedure));
    }

    [Fact]
    public void Definition_validator_checks_parameter_settings()
    {
        var report = new Report
        {
            SqlQuery = "SELECT * FROM t WHERE a = @A AND b IN (@B) AND c = @C AND d >= @D",
            Parameters =
            [
                new ReportParameter { Name = "A", Label = "A", DataType = ParameterDataType.Integer, DefaultValue = "abc" },
                new ReportParameter { Name = "B", Label = "B", AllowMultiple = true },
                new ReportParameter { Name = "C", Label = "C", LookupSource = "SELECT id, name FROM branches WHERE region = @A" },
                new ReportParameter { Name = "D", Label = "D", DataType = ParameterDataType.Date, DefaultValue = "START_OF_MONTH" },
            ],
        };

        var errors = ReportDefinitionValidator.Validate(report, new ReportingOptions(), new DateTime(2026, 10, 10));

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("@A default value"));
        Assert.Contains(errors, e => e.StartsWith("@B:"));
        Assert.Contains(errors, e => e.StartsWith("@C choices"));
    }

    [Fact]
    public void Stored_procedures_can_be_switched_off()
    {
        var report = new Report { SqlQuery = "p(@RMS_CURSOR)", QueryType = ReportQueryType.StoredProcedure };
        var errors = ReportDefinitionValidator.Validate(report, new ReportingOptions { AllowStoredProcedures = false }, DateTime.Now);
        Assert.Single(errors);
    }
}
