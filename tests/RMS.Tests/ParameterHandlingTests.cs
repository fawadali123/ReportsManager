using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Tests;

public class ParameterHandlingTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 18, 48, 0);

    [Theory]
    [InlineData("FromDate", "From Date")]
    [InlineData("p_from_date", "From Date")]
    [InlineData("DEPT_ID", "Dept ID")]
    [InlineData("customerID", "Customer ID")]
    [InlineData("region", "Region")]
    [InlineData("HTTPStatus", "Http Status")]
    [InlineData("Quarter2", "Quarter 2")]
    public void Labels_are_derived_from_parameter_names(string name, string expected)
    {
        Assert.Equal(expected, ReportParameterResolver.CreateLabel(name));
    }

    [Fact]
    public void Saved_settings_are_merged_with_what_the_sql_contains()
    {
        var saved = new[]
        {
            new ReportParameter { Name = "todate", Label = "Until", DataType = ParameterDataType.Date, Required = false, SortOrder = 5 },
            new ReportParameter { Name = "Removed", Label = "No longer in the SQL" },
        };

        var effective = ReportParameterResolver.GetEffectiveParameters(
            "SELECT * FROM t WHERE d >= @FromDate AND d <= @ToDate AND r = @Region", saved);

        Assert.Equal(new List<string> { "ToDate", "FromDate", "Region" }, effective.Select(p => p.Name).ToList());
        Assert.Equal("Until", effective[0].Label);
        Assert.False(effective[0].Required);
        Assert.Equal(ParameterDataType.Date, effective[1].DataType);   // guessed from the name
        Assert.True(effective[1].Required);                            // new parameters start as required
        Assert.Equal(ParameterDataType.Text, effective[2].DataType);
    }

    [Theory]
    [InlineData("42", ParameterDataType.Integer, 42L)]
    [InlineData(" -7 ", ParameterDataType.Integer, -7L)]
    [InlineData("true", ParameterDataType.Boolean, true)]
    [InlineData("0", ParameterDataType.Boolean, false)]
    [InlineData("  hello ", ParameterDataType.Text, "hello")]
    public void Converts_text_to_typed_values(string raw, ParameterDataType type, object expected)
    {
        Assert.Equal(expected, ParameterValueConverter.Convert(raw, type));
    }

    [Fact]
    public void Converts_decimals_and_dates()
    {
        Assert.Equal(1250.75m, ParameterValueConverter.Convert("1250.75", ParameterDataType.Decimal));
        Assert.Equal(new DateTime(2026, 3, 9), ParameterValueConverter.Convert("2026-03-09", ParameterDataType.Date));
        Assert.Equal(new DateTime(2026, 3, 9), ParameterValueConverter.Convert("2026-03-09T14:30", ParameterDataType.Date));
        Assert.Equal(new DateTime(2026, 3, 9, 14, 30, 0), ParameterValueConverter.Convert("2026-03-09T14:30", ParameterDataType.DateTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_is_null_for_every_type(string? raw)
    {
        foreach (var type in Enum.GetValues<ParameterDataType>())
        {
            Assert.Null(ParameterValueConverter.Convert(raw, type));
        }
    }

    [Theory]
    [InlineData("twelve", ParameterDataType.Integer)]
    [InlineData("1.5", ParameterDataType.Integer)]
    [InlineData("1,2,3x", ParameterDataType.Decimal)]
    [InlineData("09/03/2026", ParameterDataType.Date)]
    [InlineData("2026-13-01", ParameterDataType.Date)]
    [InlineData("maybe", ParameterDataType.Boolean)]
    public void Bad_input_raises_a_format_error(string raw, ParameterDataType type)
    {
        Assert.Throws<FormatException>(() => ParameterValueConverter.Convert(raw, type));
    }

    [Theory]
    [InlineData("TODAY", "2026-10-10")]
    [InlineData("today", "2026-10-10")]
    [InlineData("{TODAY}", "2026-10-10")]
    [InlineData("YESTERDAY", "2026-10-09")]
    [InlineData("TODAY-7", "2026-10-03")]
    [InlineData("TODAY + 30", "2026-11-09")]
    [InlineData("START_OF_MONTH", "2026-10-01")]
    [InlineData("END_OF_MONTH", "2026-10-31")]
    [InlineData("START_OF_PREV_MONTH", "2026-09-01")]
    [InlineData("END_OF_PREV_MONTH", "2026-09-30")]
    [InlineData("START_OF_YEAR", "2026-01-01")]
    [InlineData("END_OF_YEAR", "2026-12-31")]
    [InlineData("2026-01-15", "2026-01-15")]
    public void Date_defaults_resolve_tokens(string token, string expected)
    {
        Assert.Equal(expected, DefaultValueResolver.Resolve(token, ParameterDataType.Date, Now));
    }

    [Fact]
    public void Other_defaults_are_passed_through()
    {
        Assert.Equal("2026-10-10T18:48", DefaultValueResolver.Resolve("NOW", ParameterDataType.DateTime, Now));
        Assert.Equal("TODAY", DefaultValueResolver.Resolve("TODAY", ParameterDataType.Text, Now));
        Assert.Null(DefaultValueResolver.Resolve("  ", ParameterDataType.Text, Now));
    }

    [Fact]
    public void Lookup_source_distinguishes_queries_from_fixed_lists()
    {
        Assert.True(LookupSource.IsQuery("  select id, name from branches"));
        Assert.True(LookupSource.IsQuery("WITH x AS (SELECT 1 FROM dual) SELECT * FROM x"));
        Assert.False(LookupSource.IsQuery("A=Active;I=Inactive"));
        Assert.False(LookupSource.IsQuery("Selected=Selected items"));

        var items = LookupSource.ParseStatic("A=Active; I = Inactive\nPending");
        Assert.Equal(3, items.Count);
        Assert.Equal(new LookupItem("I", "Inactive"), items[1]);
        Assert.Equal(new LookupItem("Pending", "Pending"), items[2]);
    }
}
