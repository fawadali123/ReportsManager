using RMS.Core.Reporting;

namespace RMS.Tests;

public class SqlScannerTests
{
    private static List<string> Names(string sql) => [.. ReportParameterResolver.GetParameterNames(sql)];

    [Fact]
    public void Finds_parameters_in_order_of_first_appearance()
    {
        var names = Names("SELECT * FROM orders WHERE order_date >= @FromDate AND order_date < @ToDate AND region = @Region");
        Assert.Equal(new List<string> { "FromDate", "ToDate", "Region" }, names);
    }

    [Fact]
    public void Repeated_parameters_are_reported_once_whatever_their_case()
    {
        var names = Names("SELECT 1 FROM dual WHERE (@DeptId IS NULL OR dept_id = @deptid) AND x = @DEPTID");
        Assert.Equal(new List<string> { "DeptId" }, names);
    }

    [Fact]
    public void A_query_without_parameters_has_none()
    {
        Assert.Empty(Names("SELECT code, name FROM rms_applications ORDER BY name"));
    }

    [Theory]
    [InlineData("SELECT 'mail me @home' AS note FROM dual")]
    [InlineData("SELECT 'it''s @notparam' FROM dual")]
    [InlineData("SELECT q'[contact @admin]' FROM dual")]
    [InlineData("SELECT \"col@name\" FROM t")]
    [InlineData("SELECT 1 FROM dual -- filter by @Later")]
    [InlineData("SELECT /* @Commented */ 1 FROM dual")]
    [InlineData("SELECT /*+ INDEX(e @hint) */ 1 FROM dual")]
    public void An_at_sign_inside_a_literal_or_comment_is_not_a_parameter(string sql)
    {
        Assert.Empty(Names(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM employees@hr_link")]
    [InlineData("SELECT * FROM hr.employees@hr_link e WHERE 1 = 1")]
    [InlineData("SELECT * FROM \"Employees\"@hr_link")]
    public void Database_links_are_not_parameters(string sql)
    {
        Assert.Empty(Names(sql));
    }

    [Fact]
    public void A_parameter_next_to_a_database_link_is_still_found()
    {
        Assert.Equal(new List<string> { "Id" }, Names("SELECT * FROM employees@hr_link WHERE id=@Id"));
    }

    [Theory]
    [InlineData("WHERE a=@P")]
    [InlineData("WHERE a IN(@P)")]
    [InlineData("WHERE a LIKE '%'||@P||'%'")]
    [InlineData("WHERE a >\n@P")]
    public void Parameters_are_found_without_surrounding_spaces(string where)
    {
        Assert.Equal(new List<string> { "P" }, Names("SELECT 1 FROM t " + where));
    }

    [Fact]
    public void System_parameters_are_not_shown_to_the_user()
    {
        Assert.Equal(new List<string> { "Year" }, Names("SELECT 1 FROM sales WHERE owner = @RMS_USERNAME AND yr = @Year"));
    }

    [Fact]
    public void Scanning_then_joining_reproduces_the_text()
    {
        const string sql = "SELECT 'a''b', q'{x}' , \"c\" /* d */ FROM t@lnk -- e\nWHERE f = @G AND h IN (@I);";
        Assert.Equal(sql, string.Concat(SqlScanner.Scan(sql).Select(SqlScanner.ToSql)));
    }

    [Fact]
    public void Unterminated_literal_does_not_loop_or_throw()
    {
        Assert.Empty(Names("SELECT 'never closed @x FROM dual"));
    }
}
