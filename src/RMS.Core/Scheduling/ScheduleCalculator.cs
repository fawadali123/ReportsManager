using System.Globalization;
using RMS.Core.Domain;

namespace RMS.Core.Scheduling;

/// <summary>
/// Works out when a schedule runs next. Schedules are written in local time (their own time zone) and the
/// answer is in UTC, which is what is stored and compared, so servers in different time zones agree.
/// </summary>
public static class ScheduleCalculator
{
    // Long enough to find the next run of any valid schedule (the rarest is "day 31", every other month or so).
    private const int MaxDaysAhead = 800;

    /// <summary>The first run strictly after <paramref name="afterUtc"/>, or null when the schedule will not run again.</summary>
    public static DateTime? GetNextRunUtc(ReportSchedule schedule, DateTime afterUtc)
    {
        afterUtc = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        if (!ScheduleTimeZones.TryFind(schedule.TimeZone, out var zone))
        {
            return null;
        }

        if (schedule.Frequency == ScheduleFrequency.Once)
        {
            if (schedule.RunOnceAt is not { } once)
            {
                return null;
            }

            var utc = ToUtc(once, zone);
            return utc > afterUtc ? utc : null;
        }

        if (schedule.RunTime is null)
        {
            return null;
        }

        var times = TimesOfDay(schedule);
        if (times.Count == 0)
        {
            return null;
        }

        var day = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, zone).Date;
        if (schedule.StartDate is { } start && start.Date > day)
        {
            day = start.Date;
        }

        for (var i = 0; i < MaxDaysAhead; i++, day = day.AddDays(1))
        {
            if (schedule.EndDate is { } end && day > end.Date)
            {
                return null;
            }

            if (!RunsOn(schedule, day))
            {
                continue;
            }

            foreach (var time in times)
            {
                var utc = ToUtc(day + time, zone);
                if (utc > afterUtc)
                {
                    return utc;
                }
            }
        }

        return null;
    }

    /// <summary>The next few runs after <paramref name="afterUtc"/>, for showing on screen.</summary>
    public static IReadOnlyList<DateTime> GetUpcomingRunsUtc(ReportSchedule schedule, DateTime afterUtc, int count)
    {
        var runs = new List<DateTime>(count);
        var after = afterUtc;
        while (runs.Count < count && GetNextRunUtc(schedule, after) is { } next)
        {
            runs.Add(next);
            after = next;
        }
        return runs;
    }

    /// <summary>A sentence such as "Every 2 hours from 08:00 to 18:00, Sun to Thu".</summary>
    public static string Describe(ReportSchedule schedule)
    {
        var at = schedule.RunTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";
        var days = ScheduleDays.Describe(schedule.DaysOfWeek);

        return schedule.Frequency switch
        {
            ScheduleFrequency.Once => schedule.RunOnceAt is { } once
                ? $"Once, on {once.ToString("yyyy-MM-dd 'at' HH:mm", CultureInfo.InvariantCulture)}"
                : "Once",
            ScheduleFrequency.Hourly =>
                (schedule.IntervalHours is null or 1 ? "Every hour" : $"Every {schedule.IntervalHours} hours")
                + $" from {at} to {(schedule.EndTime ?? new TimeOnly(23, 59)).ToString("HH:mm", CultureInfo.InvariantCulture)}"
                + (schedule.DaysOfWeek.Count is > 0 and < 7 ? $", {days}" : ", every day"),
            ScheduleFrequency.Daily => schedule.DaysOfWeek.Count is > 0 and < 7
                ? $"Daily at {at}, {days}"
                : $"Every day at {at}",
            ScheduleFrequency.Weekly => $"Weekly on {days} at {at}",
            ScheduleFrequency.Monthly => schedule.DayOfMonth is null or ReportSchedule.LastDayOfMonth
                ? $"Monthly on the last day at {at}"
                : $"Monthly on day {schedule.DayOfMonth} at {at}",
            _ => schedule.Frequency.ToString(),
        };
    }

    /// <summary>Local time in the schedule's zone to UTC. A time skipped by a daylight-saving change moves forward an hour.</summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static bool RunsOn(ReportSchedule schedule, DateTime day) => schedule.Frequency switch
    {
        ScheduleFrequency.Hourly or ScheduleFrequency.Daily =>
            schedule.DaysOfWeek.Count == 0 || schedule.DaysOfWeek.Contains(day.DayOfWeek),
        ScheduleFrequency.Weekly => schedule.DaysOfWeek.Contains(day.DayOfWeek),
        ScheduleFrequency.Monthly => day.Day == TargetDay(schedule.DayOfMonth, day.Year, day.Month),
        _ => false,
    };

    /// <summary>The day of the month a monthly schedule runs: the chosen day, or the month's last day when it is shorter.</summary>
    private static int TargetDay(int? dayOfMonth, int year, int month)
    {
        var last = DateTime.DaysInMonth(year, month);
        return dayOfMonth is null or ReportSchedule.LastDayOfMonth or < 0 ? last : Math.Min(dayOfMonth.Value, last);
    }

    private static List<TimeSpan> TimesOfDay(ReportSchedule schedule)
    {
        var first = schedule.RunTime!.Value.ToTimeSpan();
        if (schedule.Frequency != ScheduleFrequency.Hourly)
        {
            return [first];
        }

        var step = TimeSpan.FromHours(Math.Clamp(schedule.IntervalHours ?? 1, 1, 23));
        var last = (schedule.EndTime ?? new TimeOnly(23, 59)).ToTimeSpan();
        var times = new List<TimeSpan>();
        for (var t = first; t <= last && t < TimeSpan.FromDays(1); t += step)
        {
            times.Add(t);
        }
        return times;
    }
}

/// <summary>Days of the week as stored in RMS_REPORT_SCHEDULES.DAYS_OF_WEEK ("Sun,Mon,Tue").</summary>
public static class ScheduleDays
{
    /// <summary>Sunday first, which is how the week is usually shown in Saudi Arabia and the US.</summary>
    public static readonly IReadOnlyList<DayOfWeek> Week =
    [
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    ];

    public static string ShortName(DayOfWeek day) => day.ToString()[..3];

    public static string Format(IEnumerable<DayOfWeek> days) =>
        string.Join(",", Week.Where(days.Contains).Select(ShortName));

    public static List<DayOfWeek> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. Week.Where(day => parts.Any(p =>
            p.Length >= 3 && day.ToString().StartsWith(p[..3], StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>"Sun to Thu" for a run of consecutive days, otherwise "Sun, Tue, Thu".</summary>
    public static string Describe(IReadOnlyCollection<DayOfWeek> days)
    {
        var ordered = Week.Where(days.Contains).ToList();
        if (ordered.Count == 0)
        {
            return "no days";
        }
        if (ordered.Count == 7)
        {
            return "every day";
        }

        var positions = ordered.Select(d => Week.IndexOf(d)).ToList();
        var consecutive = ordered.Count >= 3 && positions.Zip(positions.Skip(1)).All(p => p.Second == p.First + 1);
        return consecutive
            ? $"{ShortName(ordered[0])} to {ShortName(ordered[^1])}"
            : string.Join(", ", ordered.Select(ShortName));
    }

    private static int IndexOf(this IReadOnlyList<DayOfWeek> list, DayOfWeek day)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == day)
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>Time zone lookups that accept both Windows ids ("Arab Standard Time") and IANA ids ("Asia/Riyadh").</summary>
public static class ScheduleTimeZones
{
    public static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>The configured default, or this server's time zone.</summary>
    public static string DefaultId(string? configured) =>
        TryFind(configured, out var zone) ? zone.Id : TimeZoneInfo.Local.Id;

    public static string DisplayName(string? id) => TryFind(id, out var zone) ? zone.DisplayName : id ?? "";
}
