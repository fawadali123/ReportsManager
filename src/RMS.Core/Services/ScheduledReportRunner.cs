using System.Globalization;
using System.Net;
using System.Text;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Scheduling;

namespace RMS.Core.Services;

/// <summary>
/// Runs one schedule: produces the file with the same engine as the Reports screen, then saves it to the
/// shared folder and/or emails it. Every outcome, including failures, ends up in RMS_SCHEDULE_RUNS.
/// </summary>
public sealed class ScheduledReportRunner(
    IScheduleRepository schedules,
    ReportService reports,
    IEnumerable<IReportExporter> exporters,
    IEmailSender email,
    IFileShareWriter shares,
    SchedulingOptions options,
    TimeProvider clock)
{
    /// <param name="scheduledForUtc">The slot being run. Date tokens and file names use this time.</param>
    /// <param name="machineName">Recorded on the run, to tell scheduler servers apart.</param>
    public async Task<ScheduleRun> RunAsync(
        ReportSchedule schedule,
        ScheduleTrigger trigger,
        DateTime scheduledForUtc,
        string machineName,
        CancellationToken ct = default)
    {
        var run = new ScheduleRun
        {
            ScheduleId = schedule.Id,
            ScheduleName = schedule.Name,
            ReportId = schedule.ReportId,
            ReportName = schedule.ReportName,
            Trigger = trigger,
            ScheduledForUtc = scheduledForUtc,
            StartedUtc = clock.GetUtcNow().UtcDateTime,
            Status = ScheduleRunStatus.Running,
            MachineName = Truncate(machineName, 100),
        };
        run.Id = await schedules.StartRunAsync(run, ct);

        // The report is written to a temporary file first, so it can go to both the share and the email.
        var tempFile = Path.Combine(Path.GetTempPath(), $"rms-schedule-{Guid.NewGuid():N}.tmp");
        try
        {
            await ProduceAndDeliverAsync(schedule, run, scheduledForUtc, tempFile, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Fail(run, "Stopped before it finished: the scheduler service was stopping, or the run took longer than "
                + $"{options.RunTimeoutMinutes} minutes.");
        }
        catch (ReportNotFoundException)
        {
            Fail(run, "The report no longer exists.");
        }
        catch (ReportValidationException ex)
        {
            Fail(run, ex.FieldErrors.Count == 0
                ? ex.Message
                : "Parameter values are not valid: " + string.Join(" ", ex.FieldErrors.Values));
        }
        catch (ReportExecutionException ex)
        {
            Fail(run, "The report query failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            Fail(run, ex.Message);
        }
        finally
        {
            run.FinishedUtc = clock.GetUtcNow().UtcDateTime;
            run.ErrorMessage = Truncate(run.ErrorMessage, 2000);
            run.EmailResult = Truncate(run.EmailResult, 1000);
            run.ShareResult = Truncate(run.ShareResult, 1000);

            // Recorded even when the service is stopping, so the run does not stay "Running".
            await schedules.FinishRunAsync(run, CancellationToken.None);

            TryDelete(tempFile);
        }

        return run;
    }

    private async Task ProduceAndDeliverAsync(
        ReportSchedule schedule, ScheduleRun run, DateTime scheduledForUtc, string tempFile, CancellationToken ct)
    {
        var user = UserContext.ServiceAccount(options.RunAsUsername);
        var exporter = exporters.FirstOrDefault(e => string.Equals(e.Format, schedule.ExportFormat, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The file format \"{schedule.ExportFormat}\" is not available.");

        var zone = ScheduleTimeZones.TryFind(schedule.TimeZone, out var found) ? found : TimeZoneInfo.Local;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(scheduledForUtc, DateTimeKind.Utc), zone);

        var form = await reports.GetFormAsync(user, schedule.ReportId, ct);
        var report = form.Report;
        run.ReportName = report.Name;

        var values = ScheduleParameterValues.ResolveForRun(form.Fields, schedule.ParameterValues, localTime);
        var placeholders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReportCode"] = report.Code,
            ["ReportName"] = report.Name,
            ["ScheduleName"] = schedule.Name,
        };
        var fileName = FileNameTemplate.ExpandFileName(schedule.FileNamePattern, placeholders, localTime) + exporter.FileExtension;

        ReportExportResult result;
        await using (var output = new FileStream(tempFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
        {
            result = await reports.ExportAsync(user, schedule.ReportId, values, exporter, output, ct, "Schedule:" + exporter.Format);
        }

        run.FileName = fileName;
        run.RowCount = result.Summary.RowCount;
        run.Truncated = result.Summary.Truncated;
        run.FileSize = new FileInfo(tempFile).Length;

        if (schedule.SkipIfEmpty && result.Summary.RowCount == 0)
        {
            run.Status = ScheduleRunStatus.Skipped;
            return;
        }

        var wanted = 0;
        var delivered = 0;
        string? savedPath = null;

        // The share goes first so that the email can point to the saved copy.
        if (schedule.SaveToShare)
        {
            wanted++;
            try
            {
                if (ShareFolderRules.Check(schedule.ShareFolder ?? "", options.AllowedShareRoots) is { } problem)
                {
                    throw new FileDeliveryException(problem);
                }

                await using var input = File.OpenRead(tempFile);
                savedPath = await shares.SaveAsync(schedule.ShareFolder!, fileName, input, ct);
                run.ShareResult = "Saved to " + savedPath;
                delivered++;
            }
            catch (FileDeliveryException ex)
            {
                run.ShareResult = "Failed: " + ex.Message;
            }
        }

        if (schedule.SendEmail)
        {
            wanted++;
            try
            {
                run.EmailResult = await SendEmailAsync(schedule, form, values, run, exporter, tempFile, savedPath, placeholders, localTime, zone, ct);
                delivered++;
            }
            catch (EmailDeliveryException ex)
            {
                run.EmailResult = "Failed: " + ex.Message;
            }
        }

        run.Status = delivered == wanted ? ScheduleRunStatus.Succeeded
            : delivered == 0 ? ScheduleRunStatus.Failed
            : ScheduleRunStatus.Partial;

        if (run.Status == ScheduleRunStatus.Failed)
        {
            run.ErrorMessage = "The file was produced but could not be delivered.";
        }
    }

    private async Task<string> SendEmailAsync(
        ReportSchedule schedule,
        ReportForm form,
        IReadOnlyDictionary<string, string[]> values,
        ScheduleRun run,
        IReportExporter exporter,
        string tempFile,
        string? savedPath,
        IReadOnlyDictionary<string, string?> placeholders,
        DateTime localTime,
        TimeZoneInfo zone,
        CancellationToken ct)
    {
        // Checked again here: the allowed domains may have changed since the schedule was saved.
        var problem = EmailAddressList.Check(schedule.EmailTo, options) ?? EmailAddressList.Check(schedule.EmailCc, options);
        if (problem is not null)
        {
            throw new EmailDeliveryException(problem);
        }

        var to = EmailAddressList.Parse(schedule.EmailTo);
        var cc = EmailAddressList.Parse(schedule.EmailCc);
        if (to.Count == 0)
        {
            throw new EmailDeliveryException("The schedule has no To address.");
        }

        var size = run.FileSize ?? 0;
        var attach = size <= email.MaxAttachmentBytes;
        if (!attach && savedPath is null)
        {
            throw new EmailDeliveryException(
                $"The file is {Megabytes(size)}, more than the {Megabytes(email.MaxAttachmentBytes)} that can be attached, "
                + "and it was not saved to a shared folder. Add a shared folder to the schedule, or narrow the report.");
        }

        var subject = FileNameTemplate.Expand(
            string.IsNullOrWhiteSpace(schedule.EmailSubject) ? FileNameTemplate.DefaultSubject : schedule.EmailSubject,
            placeholders, localTime);

        var (html, text) = ComposeBody(schedule, form, values, run, attach, savedPath, localTime, zone);

        await email.SendAsync(new EmailMessage
        {
            To = to,
            Cc = cc,
            Subject = subject.ReplaceLineEndings(" "),
            HtmlBody = html,
            TextBody = text,
            Attachments = attach
                ? [new EmailAttachment(run.FileName!, exporter.ContentType, await File.ReadAllBytesAsync(tempFile, ct))]
                : [],
        }, ct);

        var count = to.Count + cc.Count;
        var result = $"Sent to {count} recipient{(count == 1 ? "" : "s")} by {email.Name}";
        return attach ? result : result + ", without the file (too large to attach); the email gives the shared folder path";
    }

    private static (string Html, string Text) ComposeBody(
        ReportSchedule schedule,
        ReportForm form,
        IReadOnlyDictionary<string, string[]> values,
        ScheduleRun run,
        bool attached,
        string? savedPath,
        DateTime localTime,
        TimeZoneInfo zone)
    {
        var report = form.Report;
        var rows = new List<KeyValuePair<string, string>>
        {
            new("Report", report.Name),
            new("Schedule", schedule.Name),
            new("Generated", $"{localTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} ({zone.StandardName})"),
            new("Rows", (run.RowCount ?? 0).ToString("N0", CultureInfo.InvariantCulture)
                + (run.Truncated ? " (stopped at the row limit for this format; the report has more)" : "")),
        };
        rows.AddRange(ScheduleParameterValues.Describe(form.Fields, values));

        var notes = new List<string>();
        if (attached)
        {
            notes.Add($"The report is attached as {run.FileName}.");
            if (savedPath is not null)
            {
                notes.Add($"A copy is saved at {savedPath}");
            }
        }
        else
        {
            notes.Add($"The file was too large to attach. Open it from {savedPath}");
        }

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2933\">");
        if (!string.IsNullOrWhiteSpace(schedule.EmailBody))
        {
            foreach (var paragraph in schedule.EmailBody.ReplaceLineEndings("\n").Split("\n\n"))
            {
                html.Append("<p>").Append(WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>")).Append("</p>");
            }
        }

        foreach (var note in notes)
        {
            html.Append("<p>").Append(WebUtility.HtmlEncode(note)).Append("</p>");
        }

        html.Append("<table style=\"border-collapse:collapse;margin:12px 0\">");
        foreach (var (label, value) in rows)
        {
            html.Append("<tr><th style=\"text-align:left;padding:4px 16px 4px 0;color:#52606d;font-weight:600;vertical-align:top\">")
                .Append(WebUtility.HtmlEncode(label))
                .Append("</th><td style=\"padding:4px 0\">")
                .Append(WebUtility.HtmlEncode(value))
                .Append("</td></tr>");
        }
        html.Append("</table>");
        html.Append("<p style=\"color:#7b8794;font-size:12px\">Sent automatically by Reports Management. Replies to this address are not read.</p>");
        html.Append("</div>");

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(schedule.EmailBody))
        {
            text.AppendLine(schedule.EmailBody.Trim()).AppendLine();
        }
        foreach (var note in notes)
        {
            text.AppendLine(note);
        }
        text.AppendLine();
        foreach (var (label, value) in rows)
        {
            text.Append(label).Append(": ").AppendLine(value);
        }
        text.AppendLine().AppendLine("Sent automatically by Reports Management. Replies to this address are not read.");

        return (html.ToString(), text.ToString());
    }

    private static void Fail(ScheduleRun run, string message)
    {
        run.Status = ScheduleRunStatus.Failed;
        run.ErrorMessage = message;
    }

    private static string Megabytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..(max - 3)] + "...";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
