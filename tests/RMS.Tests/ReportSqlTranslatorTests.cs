using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Tests;

public class ReportSqlTranslatorTests
{
    private static Dictionary<string, ParameterValue> Values(params (string Name, ParameterValue Value)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Replaces_parameters_with_generated_bind_names()
    {
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM t WHERE d >= @From AND d < @To",
            ReportQueryType.Select,
            Values(("From", ParameterValue.Single(ParameterDataType.Date, new DateTime(2026, 1, 1))),
                   ("To", ParameterValue.Single(ParameterDataType.Date, new DateTime(2026, 2, 1)))));

        Assert.Equal("SELECT * FROM t WHERE d >= :b0 AND d < :b1", query.Sql);
        Assert.Equal(2, query.Binds.Count);
        Assert.Equal("b0", query.Binds[0].Name);
        Assert.Equal(new DateTime(2026, 1, 1), query.Binds[0].Value);
        Assert.Equal(ParameterDataType.Date, query.Binds[1].DataType);
    }

    [Fact]
    public void A_parameter_used_twice_is_bound_once()
    {
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM t WHERE (@Dept IS NULL OR dept = @dept)",
            ReportQueryType.Select,
            Values(("Dept", ParameterValue.Single(ParameterDataType.Integer, null))));

        Assert.Equal("SELECT * FROM t WHERE (:b0 IS NULL OR dept = :b0)", query.Sql);
        Assert.Null(Assert.Single(query.Binds).Value);
    }

    [Fact]
    public void User_text_never_reaches_the_sql()
    {
        const string hostile = "x' OR '1'='1'; DROP TABLE rms_users --";
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM t WHERE name = @Name", ReportQueryType.Select, Values(("Name", ParameterValue.Text(hostile))));

        Assert.Equal("SELECT * FROM t WHERE name = :b0", query.Sql);
        Assert.Equal(hostile, query.Binds[0].Value);
    }

    [Fact]
    public void Multi_value_parameter_expands_inside_parentheses()
    {
        var ids = new ParameterValue(ParameterDataType.Integer, true, [10L, 20L, 30L]);
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM t WHERE (@Ids IS NULL OR id IN ( @Ids ))", ReportQueryType.Select, Values(("Ids", ids)));

        Assert.Equal("SELECT * FROM t WHERE (:b0 IS NULL OR id IN ( :b0_0, :b0_1, :b0_2 ))", query.Sql);
        Assert.Equal(4, query.Binds.Count);
        Assert.Equal("10,20,30", query.Binds[0].Value);
        Assert.Equal(ParameterDataType.Text, query.Binds[0].DataType);
        Assert.Equal(20L, query.Binds[2].Value);
        Assert.Equal(ParameterDataType.Integer, query.Binds[2].DataType);
    }

    [Fact]
    public void Empty_multi_value_selection_binds_null()
    {
        var none = new ParameterValue(ParameterDataType.Text, true, []);
        var query = ReportSqlTranslator.Translate(
            "SELECT * FROM t WHERE (@Codes IS NULL OR code IN (@Codes))", ReportQueryType.Select, Values(("Codes", none)));

        Assert.Equal("SELECT * FROM t WHERE (:b0 IS NULL OR code IN (:b0_0))", query.Sql);
        Assert.True(query.Binds.All(b => b.Value is null));
    }

    [Fact]
    public void Too_many_list_values_is_a_validation_error()
    {
        var many = new ParameterValue(ParameterDataType.Integer, true, [.. Enumerable.Range(0, 1001).Select(i => (object?)(long)i)]);
        var error = Assert.Throws<ReportValidationException>(() => ReportSqlTranslator.Translate(
            "SELECT * FROM t WHERE id IN (@Ids)", ReportQueryType.Select, Values(("Ids", many))));
        Assert.True(error.FieldErrors.ContainsKey("Ids"));
    }

    [Theory]
    [InlineData("SELECT 1 FROM dual;", "SELECT 1 FROM dual\n")]
    [InlineData("SELECT 1 FROM dual ;  \n", "SELECT 1 FROM dual\n")]
    [InlineData("SELECT 1 FROM dual\n/\n", "SELECT 1 FROM dual\n")]
    [InlineData("SELECT 1 FROM dual; -- done", "SELECT 1 FROM dual\n-- done")]
    [InlineData("SELECT 6/2 FROM dual", "SELECT 6/2 FROM dual")]
    public void Trailing_statement_terminators_are_removed(string sql, string expected)
    {
        var query = ReportSqlTranslator.Translate(sql, ReportQueryType.Select, Values());
        Assert.Equal(expected, query.Sql);
    }

    [Fact]
    public void Missing_value_is_reported()
    {
        var error = Assert.Throws<ReportValidationException>(() =>
            ReportSqlTranslator.Translate("SELECT * FROM t WHERE a = @Missing", ReportQueryType.Select, Values()));
        Assert.Contains("@Missing", error.Message);
    }

    [Fact]
    public void Stored_procedure_is_wrapped_and_gets_a_cursor_bind()
    {
        var query = ReportSqlTranslator.Translate(
            "pkg_sales.monthly(@Year, @RMS_CURSOR)",
            ReportQueryType.StoredProcedure,
            Values(("Year", ParameterValue.Single(ParameterDataType.Integer, 2026L))));

        Assert.Equal("BEGIN pkg_sales.monthly(:b0, :rms_cursor)\n; END;", query.Sql);
        Assert.True(query.Binds.Single(b => b.Name == "rms_cursor").IsCursor);
    }

    [Fact]
    public void Cursor_placeholder_is_refused_in_a_select()
    {
        Assert.Throws<ReportValidationException>(() =>
            ReportSqlTranslator.Translate("SELECT @RMS_CURSOR FROM dual", ReportQueryType.Select, Values()));
    }

    [Fact]
    public void Literals_comments_and_database_links_pass_through_untouched()
    {
        const string sql = "SELECT /*+ FIRST_ROWS */ 'a@b', \"x@y\" FROM emp@hr_link WHERE id = @Id -- trailing @note";
        var query = ReportSqlTranslator.Translate(sql, ReportQueryType.Select,
            Values(("Id", ParameterValue.Single(ParameterDataType.Integer, 7L))));

        Assert.Equal("SELECT /*+ FIRST_ROWS */ 'a@b', \"x@y\" FROM emp@hr_link WHERE id = :b0 -- trailing @note", query.Sql);
    }
}
