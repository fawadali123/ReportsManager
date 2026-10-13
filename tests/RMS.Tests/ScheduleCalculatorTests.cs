using RMS.Core.Domain;
using RMS.Core.Scheduling;

namespace RMS.Tests;

public class ScheduleCalculatorTests
{
    // Riyadh is UTC+3 all year, which keeps the expected UTC values easy to check.
    private const string Riyadh = "Asia/Riyadh";

    private static readonly DayOfWeek[] SundayToThursday =
        [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static ReportSchedule Daily(int hour, int minute = 0) => new()
    {
        Frequency = ScheduleFrequency.Daily, RunTime = new TimeOnly(hour, minute), TimeZone = Riyadh,
    };

    [Fact]
    public void Daily_runs_later_the_same_day_when_the_time_has_not_passed()
    {
        // 06:00 in Riyadh; the 07:00 run is an hour away.
        Assert.Equal(Utc(2026, 10, 13, 4), ScheduleCalculator.GetNextRunUtc(Daily(7), Utc(2026, 10, 13, 3)));
    }

    [Fact]
    public void Next_run_is_strictly_after_the_given_time()
    {
        Assert.Equal(Utc(2026, 10, 14, 4), ScheduleCalculator.GetNextRunUtc(Daily(7), Utc(2026, 10, 13, 4)));
    }

    [Fact]
    public void Daily_with_working_days_skips_the_weekend()
    {
        var schedule = Daily(7);
        schedule.DaysOfWeek = [.. SundayToThursday];

        // Thursday 08:00 in Riyadh: Friday and Saturday are skipped, next is Sunday 07:00.
        var next = ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 15, 5));

        Assert.Equal(Utc(2026, 10, 18, 4), next);
        Assert.Equal(DayOfWeek.Sunday, next!.Value.DayOfWeek);
    }

    [Fact]
    public void Weekly_runs_on_the_chosen_day()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Weekly, RunTime = new TimeOnly(9, 0), DaysOfWeek = [DayOfWeek.Monday], TimeZone = Riyadh,
        };

        Assert.Equal(Utc(2026, 10, 19, 6), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 12)));
    }

    [Fact]
    public void Weekly_without_days_never_runs()
    {
        var schedule = new ReportSchedule { Frequency = ScheduleFrequency.Weekly, RunTime = new TimeOnly(9, 0), TimeZone = Riyadh };
        Assert.Null(ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 12)));
    }

    [Fact]
    public void Monthly_day_31_runs_on_the_last_day_of_shorter_months()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Monthly, RunTime = new TimeOnly(8, 0), DayOfMonth = 31, TimeZone = Riyadh,
        };

        Assert.Equal(Utc(2026, 10, 31, 5), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 0)));
        Assert.Equal(Utc(2026, 11, 30, 5), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 31, 6)));
        Assert.Equal(Utc(2027, 2, 28, 5), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2027, 1, 31, 6)));
    }

    [Fact]
    public void Monthly_last_day_option_follows_the_month_length()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Monthly, RunTime = new TimeOnly(23, 0), DayOfMonth = ReportSchedule.LastDayOfMonth, TimeZone = Riyadh,
        };

        Assert.Equal(Utc(2028, 2, 29, 20), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2028, 2, 1, 0)));   // leap year
        Assert.Equal(Utc(2026, 4, 30, 20), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 4, 1, 0)));
    }

    [Fact]
    public void Monthly_first_day()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Monthly, RunTime = new TimeOnly(6, 30), DayOfMonth = 1, TimeZone = Riyadh,
        };

        Assert.Equal(Utc(2026, 11, 1, 3, 30), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 1, 4)));
    }

    [Fact]
    public void Hourly_runs_inside_the_window_on_the_chosen_days()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Hourly, RunTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), IntervalHours = 2,
            DaysOfWeek = [.. SundayToThursday], TimeZone = Riyadh,
        };

        // Tuesday 08:30 local -> 10:00 local.
        Assert.Equal(Utc(2026, 10, 13, 7), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 5, 30)));

        // Tuesday 17:30 local -> 18:00 local, the last slot of the window.
        Assert.Equal(Utc(2026, 10, 13, 15), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 14, 30)));

        // Tuesday 18:30 local -> Wednesday 08:00 local.
        Assert.Equal(Utc(2026, 10, 14, 5), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 15, 30)));

        // Thursday evening -> Sunday 08:00 local.
        Assert.Equal(Utc(2026, 10, 18, 5), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 15, 15, 30)));
    }

    [Fact]
    public void Hourly_without_an_end_time_runs_until_midnight()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Hourly, RunTime = new TimeOnly(0, 15), IntervalHours = 1, TimeZone = Riyadh,
        };

        var runs = ScheduleCalculator.GetUpcomingRunsUtc(schedule, Utc(2026, 10, 12, 21), 25);

        Assert.Equal(25, runs.Count);
        Assert.Equal(Utc(2026, 10, 12, 21, 15), runs[0]);     // 00:15 local on the 13th
        Assert.Equal(Utc(2026, 10, 13, 20, 15), runs[23]);    // 23:15 local
        Assert.Equal(Utc(2026, 10, 13, 21, 15), runs[24]);    // 00:15 local on the 14th
    }

    [Fact]
    public void Once_runs_at_its_time_and_then_never_again()
    {
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Once, RunOnceAt = new DateTime(2026, 10, 20, 9, 0, 0), TimeZone = Riyadh,
        };

        Assert.Equal(Utc(2026, 10, 20, 6), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 0)));
        Assert.Null(ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 20, 6)));
    }

    [Fact]
    public void Start_and_end_dates_limit_the_runs()
    {
        var schedule = Daily(7);
        schedule.StartDate = new DateTime(2026, 11, 1);
        schedule.EndDate = new DateTime(2026, 11, 3);

        var runs = ScheduleCalculator.GetUpcomingRunsUtc(schedule, Utc(2026, 10, 13, 0), 10);

        Assert.Equal([Utc(2026, 11, 1, 4), Utc(2026, 11, 2, 4), Utc(2026, 11, 3, 4)], runs);
    }

    [Fact]
    public void A_time_skipped_by_daylight_saving_moves_forward_an_hour()
    {
        // London moves from 01:00 GMT to 02:00 BST on 29 March 2026, so 01:30 does not exist that day.
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Daily, RunTime = new TimeOnly(1, 30), TimeZone = "Europe/London",
        };

        var runs = ScheduleCalculator.GetUpcomingRunsUtc(schedule, Utc(2026, 3, 27, 12), 3);

        Assert.Equal(Utc(2026, 3, 28, 1, 30), runs[0]);   // 01:30 GMT
        Assert.Equal(Utc(2026, 3, 29, 1, 30), runs[1]);   // 02:30 BST, the skipped time moved forward
        Assert.Equal(Utc(2026, 3, 30, 0, 30), runs[2]);   // 01:30 BST
    }

    [Fact]
    public void Windows_time_zone_ids_are_accepted_where_the_system_knows_them()
    {
        var schedule = Daily(7);
        schedule.TimeZone = "Arab Standard Time";

        // .NET converts between Windows and IANA ids when time zone data is available.
        if (ScheduleTimeZones.TryFind(schedule.TimeZone, out _))
        {
            Assert.Equal(Utc(2026, 10, 13, 4), ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 3)));
        }
    }

    [Fact]
    public void Unknown_time_zone_means_no_runs()
    {
        var schedule = Daily(7);
        schedule.TimeZone = "Mars/Olympus_Mons";
        Assert.Null(ScheduleCalculator.GetNextRunUtc(schedule, Utc(2026, 10, 13, 0)));
    }

    [Fact]
    public void Describe_reads_naturally()
    {
        var hourly = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Hourly, RunTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), IntervalHours = 2,
            DaysOfWeek = [.. SundayToThursday],
        };
        var weekly = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Weekly, RunTime = new TimeOnly(9, 0), DaysOfWeek = [DayOfWeek.Sunday, DayOfWeek.Wednesday],
        };
        var monthly = new ReportSchedule { Frequency = ScheduleFrequency.Monthly, RunTime = new TimeOnly(6, 0), DayOfMonth = 0 };
        var daily = new ReportSchedule { Frequency = ScheduleFrequency.Daily, RunTime = new TimeOnly(7, 5), DaysOfWeek = [.. ScheduleDays.Week] };

        Assert.Equal("Every 2 hours from 08:00 to 18:00, Sun to Thu", ScheduleCalculator.Describe(hourly));
        Assert.Equal("Weekly on Sun, Wed at 09:00", ScheduleCalculator.Describe(weekly));
        Assert.Equal("Monthly on the last day at 06:00", ScheduleCalculator.Describe(monthly));
        Assert.Equal("Every day at 07:05", ScheduleCalculator.Describe(daily));
    }

    [Fact]
    public void Days_round_trip_through_the_stored_text()
    {
        var text = ScheduleDays.Format([DayOfWeek.Thursday, DayOfWeek.Sunday, DayOfWeek.Monday]);

        Assert.Equal("Sun,Mon,Thu", text);
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Thursday], ScheduleDays.Parse(text));
        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], ScheduleDays.Parse("saturday; FRI"));
        Assert.Empty(ScheduleDays.Parse(null));
    }
}
