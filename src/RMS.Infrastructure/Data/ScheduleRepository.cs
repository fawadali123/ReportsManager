using System.Data.Common;
using System.Globalization;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Scheduling;
using RMS.Infrastructure.Data.Providers;

namespace RMS.Infrastructure.Data;

/// <summary>
/// Schedules and their run history. All times the scheduler compares (next run, locks) are UTC and are passed in
/// by the caller rather than taken from the database clock, so the web server, the scheduler server and the
/// database may each be in a different time zone.
/// </summary>
internal sealed class ScheduleRepository(AppDatabase db) : IScheduleRepository
{
    private const string Select =
        """
        SELECT s.ID, s.REPORT_ID, s.NAME, s.ACTIVE, s.FREQUENCY, s.RUN_TIME, s.END_TIME, s.INTERVAL_HOURS,
               s.DAYS_OF_WEEK, s.DAY_OF_MONTH, s.RUN_ONCE_AT, s.START_DATE, s.END_DATE, s.TIME_ZONE, s.EXPORT_FORMAT,
               s.PARAMETER_VALUES, s.SKIP_IF_EMPTY, s.SEND_EMAIL, s.EMAIL_TO, s.EMAIL_CC, s.EMAIL_SUBJECT, s.EMAIL_BODY,
               s.SAVE_TO_SHARE, s.SHARE_FOLDER, s.FILE_NAME_PATTERN, s.NEXT_RUN_UTC, s.LAST_RUN_UTC, s.LAST_STATUS,
               s.RUN_REQUESTED_UTC, s.RUN_REQUESTED_BY, s.LOCKED_BY, s.LOCKED_UNTIL_UTC, s.CREATED_BY, s.UPDATED_BY,
               r.CODE AS REPORT_CODE, r.NAME AS REPORT_NAME, r.ACTIVE AS REPORT_ACTIVE
          FROM RMS_REPORT_SCHEDULES s
          JOIN RMS_REPORTS r ON r.ID = s.REPORT_ID
        """;

    // Due: active and past its next run, or asked to run now; and not locked by a scheduler that is still within its time.
    private const string DueCondition =
        """
        ((ACTIVE = 1 AND NEXT_RUN_UTC <= @p_now) OR RUN_REQUESTED_UTC IS NOT NULL)
           AND (LOCKED_UNTIL_UTC IS NULL OR LOCKED_UNTIL_UTC < @p_now)
        """;

    public async Task<IReadOnlyList<ReportSchedule>> GetAllAsync(int? reportId, CancellationToken ct = default) =>
        await db.QueryAsync(
            $"{Select} WHERE (@p_report_id IS NULL OR s.REPORT_ID = @p_report_id) ORDER BY UPPER(r.NAME), UPPER(s.NAME)",
            Map, ct, P.Int("p_report_id", reportId));

    public Task<ReportSchedule?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.QuerySingleAsync($"{Select} WHERE s.ID = @p_id", Map, ct, P.Int("p_id", id));

    public Task<int> CreateAsync(ReportSchedule schedule, CancellationToken ct = default) =>
        db.InsertAsync(
            """
            INSERT INTO RMS_REPORT_SCHEDULES
                   (REPORT_ID, NAME, ACTIVE, FREQUENCY, RUN_TIME, END_TIME, INTERVAL_HOURS, DAYS_OF_WEEK, DAY_OF_MONTH,
                    RUN_ONCE_AT, START_DATE, END_DATE, TIME_ZONE, EXPORT_FORMAT, PARAMETER_VALUES, SKIP_IF_EMPTY,
                    SEND_EMAIL, EMAIL_TO, EMAIL_CC, EMAIL_SUBJECT, EMAIL_BODY, SAVE_TO_SHARE, SHARE_FOLDER,
                    FILE_NAME_PATTERN, NEXT_RUN_UTC, CREATED_BY)
            VALUES (@p_report_id, @p_name, @p_active, @p_frequency, @p_run_time, @p_end_time, @p_interval_hours,
                    @p_days_of_week, @p_day_of_month, @p_run_once_at, @p_start_date, @p_end_date, @p_time_zone,
                    @p_export_format, @p_parameter_values, @p_skip_if_empty, @p_send_email, @p_email_to, @p_email_cc,
                    @p_email_subject, @p_email_body, @p_save_to_share, @p_share_folder, @p_file_name_pattern,
                    @p_next_run_utc, @p_user)
            """,
            ct, [.. Values(schedule), P.Text("p_user", schedule.CreatedBy)]);

    public Task UpdateAsync(ReportSchedule schedule, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE RMS_REPORT_SCHEDULES
               SET REPORT_ID = @p_report_id, NAME = @p_name, ACTIVE = @p_active, FREQUENCY = @p_frequency,
                   RUN_TIME = @p_run_time, END_TIME = @p_end_time, INTERVAL_HOURS = @p_interval_hours,
                   DAYS_OF_WEEK = @p_days_of_week, DAY_OF_MONTH = @p_day_of_month, RUN_ONCE_AT = @p_run_once_at,
                   START_DATE = @p_start_date, END_DATE = @p_end_date, TIME_ZONE = @p_time_zone,
                   EXPORT_FORMAT = @p_export_format, PARAMETER_VALUES = @p_parameter_values,
                   SKIP_IF_EMPTY = @p_skip_if_empty, SEND_EMAIL = @p_send_email, EMAIL_TO = @p_email_to,
                   EMAIL_CC = @p_email_cc, EMAIL_SUBJECT = @p_email_subject, EMAIL_BODY = @p_email_body,
                   SAVE_TO_SHARE = @p_save_to_share, SHARE_FOLDER = @p_share_folder,
                   FILE_NAME_PATTERN = @p_file_name_pattern, NEXT_RUN_UTC = @p_next_run_utc,
                   UPDATED_BY = @p_user, UPDATED_AT = {now}
             WHERE ID = @p_id
            """,
            ct, [.. Values(schedule), P.Text("p_user", schedule.UpdatedBy), P.Int("p_id", schedule.Id)]);

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        // Run history stays (ON DELETE SET NULL) and keeps the schedule's name.
        db.ExecuteAsync("DELETE FROM RMS_REPORT_SCHEDULES WHERE ID = @p_id", ct, P.Int("p_id", id));

    public Task RequestRunAsync(int id, string requestedBy, DateTime utcNow, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "UPDATE RMS_REPORT_SCHEDULES SET RUN_REQUESTED_UTC = @p_now, RUN_REQUESTED_BY = @p_user WHERE ID = @p_id",
            ct, P.Timestamp("p_now", utcNow), P.Text("p_user", requestedBy), P.Int("p_id", id));

    public async Task<IReadOnlyDictionary<int, int>> GetCountsByReportAsync(CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            "SELECT REPORT_ID, COUNT(*) AS SCHEDULE_COUNT FROM RMS_REPORT_SCHEDULES GROUP BY REPORT_ID",
            r => (ReportId: r.Int("REPORT_ID"), Count: r.Int("SCHEDULE_COUNT")), ct);
        return rows.ToDictionary(x => x.ReportId, x => x.Count);
    }

    public async Task<IReadOnlyList<ScheduleRun>> GetRunsAsync(int? scheduleId, int take, CancellationToken ct = default) =>
        await db.QueryAsync(
            """
            SELECT ID, SCHEDULE_ID, SCHEDULE_NAME, REPORT_ID, REPORT_NAME, TRIGGER_TYPE, SCHEDULED_FOR_UTC, STARTED_UTC,
                   FINISHED_UTC, STATUS, ROW_COUNT, TRUNCATED, FILE_NAME, FILE_SIZE, EMAIL_RESULT, SHARE_RESULT,
                   ERROR_MESSAGE, MACHINE_NAME
              FROM RMS_SCHEDULE_RUNS
             WHERE (@p_schedule_id IS NULL OR SCHEDULE_ID = @p_schedule_id)
             ORDER BY STARTED_UTC DESC, ID DESC
            OFFSET 0 ROWS FETCH NEXT @p_take ROWS ONLY
            """,
            MapRun, ct, P.Int("p_schedule_id", scheduleId), P.Int("p_take", take));

    public async Task<IReadOnlyList<int>> GetDueIdsAsync(DateTime utcNow, int max, CancellationToken ct = default) =>
        await db.QueryAsync(
            $"""
            SELECT ID
              FROM RMS_REPORT_SCHEDULES
             WHERE {DueCondition}
             ORDER BY COALESCE(RUN_REQUESTED_UTC, NEXT_RUN_UTC), ID
            OFFSET 0 ROWS FETCH NEXT @p_max ROWS ONLY
            """,
            r => r.Int("ID"), ct, P.Timestamp("p_now", utcNow), P.Int("p_max", Math.Max(1, max)));

    public async Task<bool> TryClaimAsync(
        int id, string owner, DateTime utcNow, DateTime lockUntilUtc, CancellationToken ct = default)
    {
        // A single conditional UPDATE: whichever scheduler's statement commits first gets the row.
        var updated = await db.ExecuteAsync(
            $"""
            UPDATE RMS_REPORT_SCHEDULES
               SET LOCKED_BY = @p_owner, LOCKED_UNTIL_UTC = @p_until
             WHERE ID = @p_id
               AND {DueCondition}
            """,
            ct, P.Text("p_owner", owner), P.Timestamp("p_until", lockUntilUtc), P.Int("p_id", id), P.Timestamp("p_now", utcNow));
        return updated == 1;
    }

    public Task CompleteAsync(ScheduleCompletion completion, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE RMS_REPORT_SCHEDULES
               SET NEXT_RUN_UTC = CASE WHEN @p_manual = 1 THEN NEXT_RUN_UTC ELSE @p_next END,
                   LAST_RUN_UTC = @p_last,
                   LAST_STATUS = @p_status,
                   RUN_REQUESTED_BY = CASE WHEN RUN_REQUESTED_UTC <= @p_request THEN NULL ELSE RUN_REQUESTED_BY END,
                   RUN_REQUESTED_UTC = CASE WHEN RUN_REQUESTED_UTC <= @p_request THEN NULL ELSE RUN_REQUESTED_UTC END,
                   LOCKED_BY = NULL,
                   LOCKED_UNTIL_UTC = NULL
             WHERE ID = @p_id
               AND LOCKED_BY = @p_owner
            """,
            ct,
            P.Bool("p_manual", completion.Trigger == ScheduleTrigger.Manual),
            P.Timestamp("p_next", completion.NextRunUtc),
            P.Timestamp("p_last", completion.LastRunUtc),
            P.Text("p_status", completion.LastStatus),
            P.Timestamp("p_request", completion.ClearRunRequestUtc),
            P.Int("p_id", completion.ScheduleId),
            P.Text("p_owner", completion.Owner));

    public async Task<long> StartRunAsync(ScheduleRun run, CancellationToken ct = default) =>
        await db.InsertAsync(
            """
            INSERT INTO RMS_SCHEDULE_RUNS
                   (SCHEDULE_ID, SCHEDULE_NAME, REPORT_ID, REPORT_NAME, TRIGGER_TYPE, SCHEDULED_FOR_UTC, STARTED_UTC,
                    STATUS, MACHINE_NAME)
            VALUES (@p_schedule_id, @p_schedule_name, @p_report_id, @p_report_name, @p_trigger, @p_scheduled_for,
                    @p_started, @p_status, @p_machine)
            """,
            ct,
            P.Int("p_schedule_id", run.ScheduleId),
            P.Text("p_schedule_name", run.ScheduleName),
            P.Int("p_report_id", run.ReportId),
            P.Text("p_report_name", run.ReportName),
            P.Text("p_trigger", run.Trigger.ToString()),
            P.Timestamp("p_scheduled_for", run.ScheduledForUtc),
            P.Timestamp("p_started", run.StartedUtc),
            P.Text("p_status", run.Status),
            P.Text("p_machine", run.MachineName));

    public Task FinishRunAsync(ScheduleRun run, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE RMS_SCHEDULE_RUNS
               SET REPORT_NAME = @p_report_name, FINISHED_UTC = @p_finished, STATUS = @p_status, ROW_COUNT = @p_rows,
                   TRUNCATED = @p_truncated, FILE_NAME = @p_file_name, FILE_SIZE = @p_file_size,
                   EMAIL_RESULT = @p_email_result, SHARE_RESULT = @p_share_result, ERROR_MESSAGE = @p_error
             WHERE ID = @p_id
            """,
            ct,
            P.Text("p_report_name", run.ReportName),
            P.Timestamp("p_finished", run.FinishedUtc),
            P.Text("p_status", run.Status),
            P.Int("p_rows", run.RowCount),
            P.Bool("p_truncated", run.Truncated),
            P.Text("p_file_name", run.FileName),
            P.Long("p_file_size", run.FileSize),
            P.Text("p_email_result", run.EmailResult),
            P.Text("p_share_result", run.ShareResult),
            P.Text("p_error", run.ErrorMessage),
            P.Long("p_id", run.Id));

    public Task<int> RecoverInterruptedAsync(string owner, string machineName, DateTime utcNow, CancellationToken ct = default) =>
        db.InTransactionAsync(async session =>
        {
            var failed = await session.ExecuteAsync(
                """
                UPDATE RMS_SCHEDULE_RUNS
                   SET STATUS = @p_failed, FINISHED_UTC = @p_now,
                       ERROR_MESSAGE = 'The scheduler service stopped before this run finished.'
                 WHERE STATUS = @p_running
                   AND MACHINE_NAME = @p_machine
                """,
                ct,
                P.Text("p_failed", ScheduleRunStatus.Failed),
                P.Timestamp("p_now", utcNow),
                P.Text("p_running", ScheduleRunStatus.Running),
                P.Text("p_machine", machineName));

            await session.ExecuteAsync(
                "UPDATE RMS_REPORT_SCHEDULES SET LOCKED_BY = NULL, LOCKED_UNTIL_UTC = NULL WHERE LOCKED_BY = @p_owner",
                ct, P.Text("p_owner", owner));

            return failed;
        }, ct);

    private static DbParam[] Values(ReportSchedule s) =>
    [
        P.Int("p_report_id", s.ReportId),
        P.Text("p_name", s.Name),
        P.Bool("p_active", s.Active),
        P.Text("p_frequency", s.Frequency.ToString()),
        P.Text("p_run_time", FormatTime(s.RunTime)),
        P.Text("p_end_time", FormatTime(s.EndTime)),
        P.Int("p_interval_hours", s.IntervalHours),
        P.Text("p_days_of_week", ScheduleDays.Format(s.DaysOfWeek)),
        P.Int("p_day_of_month", s.DayOfMonth),
        P.Timestamp("p_run_once_at", s.RunOnceAt),
        P.Timestamp("p_start_date", s.StartDate?.Date),
        P.Timestamp("p_end_date", s.EndDate?.Date),
        P.Text("p_time_zone", s.TimeZone),
        P.Text("p_export_format", s.ExportFormat),
        P.LongText("p_parameter_values", ScheduleParameterValues.Serialize(s.ParameterValues)),
        P.Bool("p_skip_if_empty", s.SkipIfEmpty),
        P.Bool("p_send_email", s.SendEmail),
        P.Text("p_email_to", s.EmailTo),
        P.Text("p_email_cc", s.EmailCc),
        P.Text("p_email_subject", s.EmailSubject),
        P.Text("p_email_body", s.EmailBody),
        P.Bool("p_save_to_share", s.SaveToShare),
        P.Text("p_share_folder", s.ShareFolder),
        P.Text("p_file_name_pattern", s.FileNamePattern),
        P.Timestamp("p_next_run_utc", s.NextRunUtc),
    ];

    private static string? FormatTime(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static ReportSchedule Map(DbDataReader r) => new()
    {
        Id = r.Int("ID"),
        ReportId = r.Int("REPORT_ID"),
        Name = r.Text("NAME"),
        Active = r.Bool("ACTIVE"),
        Frequency = Enum.TryParse<ScheduleFrequency>(r.Text("FREQUENCY"), ignoreCase: true, out var frequency) ? frequency : ScheduleFrequency.Daily,
        RunTime = ParseTime(r.TextOrNull("RUN_TIME")),
        EndTime = ParseTime(r.TextOrNull("END_TIME")),
        IntervalHours = r.IntOrNull("INTERVAL_HOURS"),
        DaysOfWeek = ScheduleDays.Parse(r.TextOrNull("DAYS_OF_WEEK")),
        DayOfMonth = r.IntOrNull("DAY_OF_MONTH"),
        RunOnceAt = r.DateOrNull("RUN_ONCE_AT"),
        StartDate = r.DateOrNull("START_DATE"),
        EndDate = r.DateOrNull("END_DATE"),
        TimeZone = r.Text("TIME_ZONE"),
        ExportFormat = r.Text("EXPORT_FORMAT"),
        ParameterValues = ScheduleParameterValues.Deserialize(r.TextOrNull("PARAMETER_VALUES")),
        SkipIfEmpty = r.Bool("SKIP_IF_EMPTY"),
        SendEmail = r.Bool("SEND_EMAIL"),
        EmailTo = r.TextOrNull("EMAIL_TO"),
        EmailCc = r.TextOrNull("EMAIL_CC"),
        EmailSubject = r.TextOrNull("EMAIL_SUBJECT"),
        EmailBody = r.TextOrNull("EMAIL_BODY"),
        SaveToShare = r.Bool("SAVE_TO_SHARE"),
        ShareFolder = r.TextOrNull("SHARE_FOLDER"),
        FileNamePattern = r.TextOrNull("FILE_NAME_PATTERN"),
        NextRunUtc = r.UtcOrNull("NEXT_RUN_UTC"),
        LastRunUtc = r.UtcOrNull("LAST_RUN_UTC"),
        LastStatus = r.TextOrNull("LAST_STATUS"),
        RunRequestedUtc = r.UtcOrNull("RUN_REQUESTED_UTC"),
        RunRequestedBy = r.TextOrNull("RUN_REQUESTED_BY"),
        LockedBy = r.TextOrNull("LOCKED_BY"),
        LockedUntilUtc = r.UtcOrNull("LOCKED_UNTIL_UTC"),
        CreatedBy = r.TextOrNull("CREATED_BY"),
        UpdatedBy = r.TextOrNull("UPDATED_BY"),
        ReportCode = r.TextOrNull("REPORT_CODE"),
        ReportName = r.TextOrNull("REPORT_NAME"),
        ReportActive = r.Bool("REPORT_ACTIVE"),
    };

    private static ScheduleRun MapRun(DbDataReader r) => new()
    {
        Id = r.Long("ID"),
        ScheduleId = r.IntOrNull("SCHEDULE_ID"),
        ScheduleName = r.Text("SCHEDULE_NAME"),
        ReportId = r.IntOrNull("REPORT_ID"),
        ReportName = r.TextOrNull("REPORT_NAME"),
        Trigger = Enum.TryParse<ScheduleTrigger>(r.Text("TRIGGER_TYPE"), ignoreCase: true, out var trigger) ? trigger : ScheduleTrigger.Schedule,
        ScheduledForUtc = r.UtcOrNull("SCHEDULED_FOR_UTC"),
        StartedUtc = r.UtcOrNull("STARTED_UTC") ?? default,
        FinishedUtc = r.UtcOrNull("FINISHED_UTC"),
        Status = r.Text("STATUS"),
        RowCount = r.IntOrNull("ROW_COUNT"),
        Truncated = r.Bool("TRUNCATED"),
        FileName = r.TextOrNull("FILE_NAME"),
        FileSize = r.LongOrNull("FILE_SIZE"),
        EmailResult = r.TextOrNull("EMAIL_RESULT"),
        ShareResult = r.TextOrNull("SHARE_RESULT"),
        ErrorMessage = r.TextOrNull("ERROR_MESSAGE"),
        MachineName = r.TextOrNull("MACHINE_NAME"),
    };
}
