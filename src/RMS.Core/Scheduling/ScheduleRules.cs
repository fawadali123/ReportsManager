using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Services;

namespace RMS.Core.Scheduling;

/// <summary>Checks a schedule before it is saved. The scheduler service checks recipients and folders again before delivering.</summary>
public static class ScheduleValidator
{
    /// <returns>Errors keyed by <see cref="ReportSchedule"/> property name; an empty key is a general error.</returns>
    /// <param name="previous">The saved version when editing, so a one-off run already in the past can still be edited.</param>
    public static Dictionary<string, string> Validate(
        ReportSchedule schedule,
        SchedulingOptions options,
        IReadOnlyCollection<string> exportFormats,
        DateTime utcNow,
        ReportSchedule? previous = null)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string message) => errors.TryAdd(key, message);

        if (string.IsNullOrWhiteSpace(schedule.Name))
        {
            Add(nameof(schedule.Name), "Enter a name.");
        }
        else if (schedule.Name.Trim().Length > 200)
        {
            Add(nameof(schedule.Name), "Use at most 200 characters.");
        }

        if (schedule.ReportId <= 0)
        {
            Add(nameof(schedule.ReportId), "Choose a report.");
        }

        var zoneOk = ScheduleTimeZones.TryFind(schedule.TimeZone, out var zone);
        if (!zoneOk)
        {
            Add(nameof(schedule.TimeZone), "Choose a time zone.");
        }

        if (!exportFormats.Contains(schedule.ExportFormat, StringComparer.OrdinalIgnoreCase))
        {
            Add(nameof(schedule.ExportFormat), "Choose a file format.");
        }

        switch (schedule.Frequency)
        {
            case ScheduleFrequency.Once:
                if (schedule.RunOnceAt is not { } once)
                {
                    Add(nameof(schedule.RunOnceAt), "Choose the date and time to run it.");
                }
                else if (zoneOk && schedule.Active && ScheduleCalculator.ToUtc(once, zone) <= utcNow
                         && (previous is null || previous.RunOnceAt != schedule.RunOnceAt))
                {
                    Add(nameof(schedule.RunOnceAt), "Choose a time in the future.");
                }
                break;

            case ScheduleFrequency.Hourly:
                RequireRunTime();
                if (schedule.IntervalHours is not (>= 1 and <= 23))
                {
                    Add(nameof(schedule.IntervalHours), "Choose every 1 to 23 hours.");
                }
                if (schedule.RunTime is { } from && schedule.EndTime is { } to && to < from)
                {
                    Add(nameof(schedule.EndTime), "The last run must be after the first.");
                }
                break;

            case ScheduleFrequency.Daily:
                RequireRunTime();
                break;

            case ScheduleFrequency.Weekly:
                RequireRunTime();
                if (schedule.DaysOfWeek.Count == 0)
                {
                    Add(nameof(schedule.DaysOfWeek), "Choose at least one day.");
                }
                break;

            case ScheduleFrequency.Monthly:
                RequireRunTime();
                if (schedule.DayOfMonth is not (>= ReportSchedule.LastDayOfMonth and <= 31))
                {
                    Add(nameof(schedule.DayOfMonth), "Choose a day of the month.");
                }
                break;

            default:
                Add(nameof(schedule.Frequency), "Choose how often it runs.");
                break;
        }

        if (schedule.Frequency != ScheduleFrequency.Once
            && schedule.StartDate is { } startDate && schedule.EndDate is { } endDate && endDate.Date < startDate.Date)
        {
            Add(nameof(schedule.EndDate), "The end date must not be before the start date.");
        }

        // Only worth saying when nothing more specific is wrong with the timing.
        if (errors.Count == 0 && schedule.Active && schedule.Frequency != ScheduleFrequency.Once
            && ScheduleCalculator.GetNextRunUtc(schedule, utcNow) is null)
        {
            Add(string.Empty, "With these settings the schedule never runs. Check the days and the end date.");
        }

        if (!schedule.SendEmail && !schedule.SaveToShare)
        {
            Add(string.Empty, "Choose where the report goes: by email, to a shared folder, or both.");
        }

        if (schedule.SendEmail)
        {
            var to = EmailAddressList.Parse(schedule.EmailTo);
            var cc = EmailAddressList.Parse(schedule.EmailCc);

            if (to.Count == 0)
            {
                Add(nameof(schedule.EmailTo), "Enter at least one address.");
            }
            else if (EmailAddressList.Check(schedule.EmailTo, options) is { } toProblem)
            {
                Add(nameof(schedule.EmailTo), toProblem);
            }

            if (EmailAddressList.Check(schedule.EmailCc, options) is { } ccProblem)
            {
                Add(nameof(schedule.EmailCc), ccProblem);
            }

            if (to.Count + cc.Count > Math.Max(1, options.MaxRecipients))
            {
                Add(nameof(schedule.EmailTo), $"Use at most {options.MaxRecipients} addresses in To and Cc together.");
            }

            if (schedule.EmailSubject?.Length > 300)
            {
                Add(nameof(schedule.EmailSubject), "Use at most 300 characters.");
            }

            if (schedule.EmailBody?.Length > 4000)
            {
                Add(nameof(schedule.EmailBody), "Use at most 4000 characters.");
            }
        }

        if (schedule.SaveToShare)
        {
            if (string.IsNullOrWhiteSpace(schedule.ShareFolder))
            {
                Add(nameof(schedule.ShareFolder), "Enter the folder, for example \\\\fileserver\\reports.");
            }
            else if (ShareFolderRules.Check(schedule.ShareFolder, options.AllowedShareRoots) is { } folderProblem)
            {
                Add(nameof(schedule.ShareFolder), folderProblem);
            }
        }

        if (!string.IsNullOrWhiteSpace(schedule.FileNamePattern))
        {
            if (schedule.FileNamePattern.Length > 200)
            {
                Add(nameof(schedule.FileNamePattern), "Use at most 200 characters.");
            }
            else if (FileNameTemplate.HasInvalidCharacters(schedule.FileNamePattern))
            {
                Add(nameof(schedule.FileNamePattern), "A file name cannot contain \\ / : * ? \" < > |.");
            }
        }

        return errors;

        void RequireRunTime()
        {
            if (schedule.RunTime is null)
            {
                Add(nameof(schedule.RunTime), "Choose the time.");
            }
        }
    }
}

/// <summary>Recipient lists as typed by an administrator: addresses separated by semicolons, commas or new lines.</summary>
public static class EmailAddressList
{
    private static readonly char[] Separators = [';', ',', '\n', '\r', '\t', ' '];

    public static List<string> Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <returns>Why the list is not acceptable, or null when it is fine.</returns>
    public static string? Check(string? text, SchedulingOptions options)
    {
        var allowed = options.AllowedEmailDomains
            .Select(d => d.Trim().TrimStart('@'))
            .Where(d => d.Length > 0)
            .ToList();

        foreach (var address in Parse(text))
        {
            if (!MailAddress.TryCreate(address, out var parsed)
                || !string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase)
                || !parsed.Host.Contains('.'))
            {
                return $"\"{address}\" is not a valid email address.";
            }

            if (allowed.Count > 0 && !allowed.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase))
            {
                return $"{address} is outside the allowed domains ({string.Join(", ", allowed)}).";
            }
        }

        return null;
    }
}

/// <summary>Which folders reports may be saved to.</summary>
public static class ShareFolderRules
{
    /// <returns>Why the folder is not acceptable, or null when it is fine.</returns>
    public static string? Check(string folder, IReadOnlyCollection<string> allowedRoots)
    {
        var normalized = Normalize(folder);
        if (normalized is null)
        {
            return "Enter a full path, such as \\\\fileserver\\reports\\finance or D:\\Reports.";
        }

        var roots = allowedRoots.Select(Normalize).OfType<string>().ToList();
        if (roots.Count > 0 && !roots.Any(root =>
                normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            return "Reports can only be saved under " + string.Join(" or ", allowedRoots.Select(r => r.Trim())) + ".";
        }

        return null;
    }

    /// <summary>
    /// The path with one kind of separator and no trailing one, or null when it is not a full path or tries to
    /// climb out with "..". Works on text only, so the same rules apply whatever machine runs the check.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var text = path.Trim().Replace('/', '\\');
        var isUnc = text.StartsWith(@"\\", StringComparison.Ordinal) && text.Length > 2 && text[2] != '\\';
        var isDrive = text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '\\';
        var isUnixRoot = path.Trim().StartsWith('/');
        if (!isUnc && !isDrive && !isUnixRoot)
        {
            return null;
        }

        var segments = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".." || s.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0)
            || (isUnc && segments.Length < 2))
        {
            return null;
        }

        var prefix = isUnc ? @"\\" : isUnixRoot ? @"\" : "";
        return prefix + string.Join('\\', segments);
    }
}

/// <summary>
/// Fills in file names and email subjects. Placeholders are {ReportCode}, {ReportName}, {ScheduleName}
/// and any date format such as {yyyy-MM-dd} or {yyyyMMdd_HHmm}, which uses the time of the scheduled run.
/// </summary>
public static partial class FileNameTemplate
{
    public const string DefaultFileName = "{ReportCode}_{yyyyMMdd_HHmm}";
    public const string DefaultSubject = "{ReportName} - {yyyy-MM-dd}";

    private static readonly char[] WindowsInvalid = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    public static string Expand(string? pattern, IReadOnlyDictionary<string, string?> values, DateTime localTime) =>
        Placeholder().Replace(pattern ?? "", match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (values.TryGetValue(key, out var value))
            {
                return value ?? "";
            }

            return DateFormat().IsMatch(key)
                ? localTime.ToString(key, CultureInfo.InvariantCulture)
                : match.Value;
        });

    /// <summary>A safe file name (without extension) from the pattern.</summary>
    public static string ExpandFileName(string? pattern, IReadOnlyDictionary<string, string?> values, DateTime localTime)
    {
        var text = Expand(string.IsNullOrWhiteSpace(pattern) ? DefaultFileName : pattern, values, localTime);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsControl(c) || Array.IndexOf(WindowsInvalid, c) >= 0 || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }

        var name = builder.ToString().Trim().TrimEnd('.');
        if (name.Length > 150)
        {
            name = name[..150];
        }
        return name.Length == 0 ? "report" : name;
    }

    public static bool HasInvalidCharacters(string pattern) =>
        Placeholder().Replace(pattern, "").IndexOfAny(WindowsInvalid) >= 0;

    [GeneratedRegex(@"\{([^{}]{1,40})\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[yMdHhmsft_\-\. ]+$")]
    private static partial Regex DateFormat();
}

/// <summary>Parameter values stored on a schedule (RMS_REPORT_SCHEDULES.PARAMETER_VALUES).</summary>
public static class ScheduleParameterValues
{
    public static string? Serialize(IReadOnlyDictionary<string, string[]> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values);

    public static Dictionary<string, string[]> Deserialize(string? json)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
            foreach (var (name, values) in parsed ?? [])
            {
                result[name] = values ?? [];
            }
        }
        catch (JsonException)
        {
            // A hand-edited value that is not valid JSON: treat as no values, so the run reports what is missing.
        }

        return result;
    }

    /// <summary>
    /// The values to run with: the schedule's own values, with date tokens such as START_OF_PREV_MONTH worked out
    /// for <paramref name="localNow"/>, and the parameter's default for any parameter added after the schedule was saved.
    /// </summary>
    public static Dictionary<string, string[]> ResolveForRun(
        IReadOnlyList<ReportFormField> fields, IReadOnlyDictionary<string, string[]> stored, DateTime localNow)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (!stored.TryGetValue(field.Name, out var values))
            {
                result[field.Name] = field.DefaultValue is null ? [] : [field.DefaultValue];
                continue;
            }

            result[field.Name] = [.. values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => DefaultValueResolver.Resolve(v, field.DataType, localNow) ?? v)];
        }
        return result;
    }

    /// <summary>Label and value pairs for the email summary, using dropdown labels where there are any.</summary>
    public static List<KeyValuePair<string, string>> Describe(
        IReadOnlyList<ReportFormField> fields, IReadOnlyDictionary<string, string[]> resolved)
    {
        var lines = new List<KeyValuePair<string, string>>();
        foreach (var field in fields)
        {
            var values = resolved.GetValueOrDefault(field.Name) ?? [];
            var text = values.Length == 0
                ? "(any)"
                : string.Join(", ", values.Select(v => field.Options?.FirstOrDefault(o => o.Value == v)?.Label ?? v));
            lines.Add(new(field.Label, text));
        }
        return lines;
    }
}
