using RMS.Core.Domain;
using RMS.Core.Scheduling;
using RMS.Core.Services;

namespace RMS.Tests;

public class ScheduleRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Formats = ["xlsx", "pdf", "csv"];

    private static ReportSchedule Valid() => new()
    {
        ReportId = 1,
        Name = "Daily sales",
        Frequency = ScheduleFrequency.Daily,
        RunTime = new TimeOnly(7, 0),
        TimeZone = "Asia/Riyadh",
        ExportFormat = "xlsx",
        SendEmail = true,
        EmailTo = "finance@company.com; cfo@company.com",
    };

    private static Dictionary<string, string> Validate(ReportSchedule schedule, SchedulingOptions? options = null, ReportSchedule? previous = null) =>
        ScheduleValidator.Validate(schedule, options ?? new SchedulingOptions(), Formats, Now, previous);

    [Fact]
    public void A_complete_schedule_is_valid()
    {
        Assert.Empty(Validate(Valid()));
    }

    [Fact]
    public void Each_frequency_requires_its_own_settings()
    {
        var weekly = Valid();
        weekly.Frequency = ScheduleFrequency.Weekly;
        Assert.Contains(nameof(ReportSchedule.DaysOfWeek), Validate(weekly).Keys);

        var monthly = Valid();
        monthly.Frequency = ScheduleFrequency.Monthly;
        monthly.DayOfMonth = 32;
        Assert.Contains(nameof(ReportSchedule.DayOfMonth), Validate(monthly).Keys);

        var hourly = Valid();
        hourly.Frequency = ScheduleFrequency.Hourly;
        hourly.IntervalHours = 0;
        hourly.EndTime = new TimeOnly(6, 0);
        var hourlyErrors = Validate(hourly);
        Assert.Contains(nameof(ReportSchedule.IntervalHours), hourlyErrors.Keys);
        Assert.Contains(nameof(ReportSchedule.EndTime), hourlyErrors.Keys);

        var noTime = Valid();
        noTime.RunTime = null;
        Assert.Contains(nameof(ReportSchedule.RunTime), Validate(noTime).Keys);
    }

    [Fact]
    public void A_one_off_run_must_be_in_the_future_unless_it_is_unchanged()
    {
        var once = Valid();
        once.Frequency = ScheduleFrequency.Once;
        once.RunOnceAt = new DateTime(2026, 10, 13, 8, 0, 0);   // 05:00 UTC, before Now

        Assert.Contains(nameof(ReportSchedule.RunOnceAt), Validate(once).Keys);

        // Editing the recipients of a one-off schedule that already ran is fine.
        var saved = Valid();
        saved.RunOnceAt = once.RunOnceAt;
        Assert.DoesNotContain(nameof(ReportSchedule.RunOnceAt), Validate(once, previous: saved).Keys);
    }

    [Fact]
    public void A_schedule_that_can_never_run_is_rejected()
    {
        var ended = Valid();
        ended.EndDate = new DateTime(2026, 1, 31);

        Assert.Contains(string.Empty, Validate(ended).Keys);
    }

    [Fact]
    public void Something_must_be_delivered()
    {
        var nowhere = Valid();
        nowhere.SendEmail = false;

        Assert.Equal("Choose where the report goes: by email, to a shared folder, or both.", Validate(nowhere)[string.Empty]);
    }

    [Fact]
    public void Email_addresses_are_checked_and_limited_to_allowed_domains()
    {
        var bad = Valid();
        bad.EmailTo = "finance@company.com; not-an-address";
        Assert.Contains("not a valid email address", Validate(bad)[nameof(ReportSchedule.EmailTo)]);

        var outside = Valid();
        outside.EmailCc = "someone@gmail.com";
        var options = new SchedulingOptions { AllowedEmailDomains = ["@company.com"] };
        Assert.Contains("outside the allowed domains", Validate(outside, options)[nameof(ReportSchedule.EmailCc)]);

        var empty = Valid();
        empty.EmailTo = " ; ";
        Assert.Contains(nameof(ReportSchedule.EmailTo), Validate(empty).Keys);

        var many = Valid();
        many.EmailTo = string.Join(";", Enumerable.Range(1, 6).Select(i => $"user{i}@company.com"));
        Assert.Contains("at most 5", Validate(many, new SchedulingOptions { MaxRecipients = 5 })[nameof(ReportSchedule.EmailTo)]);
    }

    [Theory]
    [InlineData(@"\\fileserver\reports\finance", true)]
    [InlineData(@"\\FILESERVER\Reports", true)]
    [InlineData(@"\\fileserver\reports-archive", false)]
    [InlineData(@"\\fileserver\reports\..\hr", false)]
    [InlineData(@"\\otherserver\reports", false)]
    [InlineData(@"reports\finance", false)]
    public void Share_folders_must_be_under_an_allowed_root(string folder, bool allowed)
    {
        var problem = ShareFolderRules.Check(folder, [@"\\fileserver\reports\"]);
        Assert.Equal(allowed, problem is null);
    }

    [Theory]
    [InlineData(@"D:\Reports\Finance", @"D:\Reports\Finance")]
    [InlineData(@"\\srv\share\a\\b\", @"\\srv\share\a\b")]
    [InlineData("//srv/share/a", @"\\srv\share\a")]
    [InlineData(@"\\srv", null)]
    [InlineData(@"C:\a\.\b", null)]
    [InlineData("relative", null)]
    public void Folder_paths_are_normalised(string path, string? expected)
    {
        Assert.Equal(expected, ShareFolderRules.Normalize(path));
    }

    [Fact]
    public void File_names_fill_in_placeholders_and_dates_and_drop_unsafe_characters()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReportCode"] = "SALES_BY_REGION",
            ["ReportName"] = "Sales: by region?",
            ["ScheduleName"] = "Daily",
        };
        var at = new DateTime(2026, 10, 13, 7, 5, 0);

        Assert.Equal("SALES_BY_REGION_20261013_0705", FileNameTemplate.ExpandFileName(null, values, at));
        Assert.Equal("Sales_ by region_ - 2026-10-13", FileNameTemplate.ExpandFileName("{ReportName} - {yyyy-MM-dd}", values, at));
        Assert.Equal("Daily {Unknown}", FileNameTemplate.ExpandFileName("{ScheduleName} {Unknown}", values, at));
        Assert.Equal("Sales: by region? - 2026-10-13", FileNameTemplate.Expand(FileNameTemplate.DefaultSubject, values, at));
        Assert.True(FileNameTemplate.HasInvalidCharacters("reports/{yyyy}"));
        Assert.False(FileNameTemplate.HasInvalidCharacters("{ReportCode}_{yyyyMMdd}"));
    }

    [Fact]
    public void Parameter_values_resolve_date_tokens_at_run_time()
    {
        var fields = new List<RMS.Core.Reporting.ReportFormField>
        {
            new() { Name = "FromDate", Label = "From", DataType = ParameterDataType.Date },
            new() { Name = "ToDate", Label = "To", DataType = ParameterDataType.Date },
            new() { Name = "Region", Label = "Region", DataType = ParameterDataType.Text },
            new() { Name = "Added", Label = "Added later", DataType = ParameterDataType.Text, DefaultValue = "X" },
        };
        var stored = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FromDate"] = ["START_OF_PREV_MONTH"],
            ["ToDate"] = ["END_OF_PREV_MONTH"],
            ["Region"] = ["Central"],
        };

        var resolved = ScheduleParameterValues.ResolveForRun(fields, stored, new DateTime(2026, 3, 1, 6, 0, 0));

        Assert.Equal(["2026-02-01"], resolved["FromDate"]);
        Assert.Equal(["2026-02-28"], resolved["ToDate"]);
        Assert.Equal(["Central"], resolved["Region"]);
        Assert.Equal(["X"], resolved["Added"]);

        var json = ScheduleParameterValues.Serialize(stored);
        Assert.Equal(["START_OF_PREV_MONTH"], ScheduleParameterValues.Deserialize(json)["fromdate"]);
        Assert.Empty(ScheduleParameterValues.Deserialize("not json"));
    }
}
