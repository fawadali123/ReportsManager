using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;
using RMS.Core.Scheduling;
using RMS.Core.Services;
using RMS.Infrastructure.Export;
using RMS.Tests.Fakes;

namespace RMS.Tests;

/// <summary>Saving schedules (ScheduleService) and running them (ScheduledReportRunner) against the in-memory store.</summary>
public sealed class SchedulingServiceTests : IDisposable
{
    // 10:00 in Riyadh on Tuesday 13 October 2026.
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStore _store = new();
    private readonly FakeReportExecutor _executor;
    private readonly FakeEmailSender _email = new();
    private readonly RecordingShareWriter _share = new();
    private readonly SchedulingOptions _options = new() { RunAsUsername = "svc_rms_scheduler", DefaultTimeZone = "Asia/Riyadh" };
    private readonly ReportService _reports;
    private readonly ScheduleService _schedules;
    private readonly ScheduledReportRunner _runner;
    private readonly int _salesReportId;
    private readonly int _emptyReportId;
    private ReportTable _answer = Table(3);

    public SchedulingServiceTests()
    {
        _executor = new FakeReportExecutor(query => query.Sql.Contains("branches", StringComparison.OrdinalIgnoreCase)
            ? new ReportTable { Columns = [new("ID", ReportColumnType.Number), new("NAME", ReportColumnType.Text)], Rows = [[1m, "Riyadh"], [2m, "Jeddah"]] }
            : _answer);

        var clock = new FixedClock(Now);
        _reports = new ReportService(_store, _store, _executor, _store, new FakeConnectionCatalog(), new ReportingOptions(), clock);
        IReportExporter[] exporters = [new CsvReportExporter()];
        _schedules = new ScheduleService(_store, _reports, exporters, _options, clock);
        _runner = new ScheduledReportRunner(_store, _reports, exporters, _email, _share, _options, clock);

        var app = _store.CreateAsync(new ApplicationInfo { Code = "ERP", Name = "ERP" }).Result;
        var cat = _store.CreateAsync(new ReportCategory { Code = "SALES", Name = "Sales" }).Result;
        _salesReportId = _store.CreateAsync(new Report
        {
            Code = "SALES_BY_REGION", Name = "Sales by region", ApplicationId = app, CategoryId = cat,
            SqlQuery = "SELECT region, total FROM sales WHERE sale_date BETWEEN @FromDate AND @ToDate AND branch_id IN (@Branches) AND owner = @RMS_USERNAME AND (@RMS_USER_ID IS NULL OR 1 = 1)",
            Parameters =
            [
                new ReportParameter { Name = "FromDate", Label = "From", DataType = ParameterDataType.Date, DefaultValue = "START_OF_MONTH", SortOrder = 1 },
                new ReportParameter { Name = "ToDate", Label = "To", DataType = ParameterDataType.Date, DefaultValue = "TODAY", SortOrder = 2 },
                new ReportParameter { Name = "Branches", Label = "Branches", DataType = ParameterDataType.Integer, AllowMultiple = true, Required = false, LookupSource = "SELECT id, name FROM branches", SortOrder = 3 },
            ],
        }).Result;
        _emptyReportId = _store.CreateAsync(new Report
        {
            Code = "EMPTY", Name = "Nothing to report", ApplicationId = app, CategoryId = cat, SqlQuery = "SELECT x FROM empty_table",
        }).Result;
    }

    public void Dispose() => _share.Dispose();

    private static ReportTable Table(int rows) => new()
    {
        Columns = [new("REGION", ReportColumnType.Text), new("TOTAL", ReportColumnType.Number)],
        Rows = [.. Enumerable.Range(1, rows).Select(i => new object?[] { "Region " + i, i * 100m })],
    };

    private ReportSchedule NewSchedule(int? reportId = null) => new()
    {
        ReportId = reportId ?? _salesReportId,
        Name = "Monthly sales",
        Frequency = ScheduleFrequency.Monthly,
        DayOfMonth = 1,
        RunTime = new TimeOnly(6, 0),
        TimeZone = "Asia/Riyadh",
        ExportFormat = "csv",
        SendEmail = true,
        EmailTo = "finance@company.com, cfo@company.com",
        SaveToShare = true,
        ShareFolder = _share.Root,
        ParameterValues = new(StringComparer.OrdinalIgnoreCase)
        {
            ["FromDate"] = ["START_OF_PREV_MONTH"],
            ["ToDate"] = ["END_OF_PREV_MONTH"],
            ["Branches"] = ["1", "2"],
        },
    };

    // ---------------------------------------------------------------- saving

    [Fact]
    public async Task Saving_works_out_the_next_run_and_tidies_the_values()
    {
        var schedule = NewSchedule();
        schedule.DaysOfWeek = [DayOfWeek.Monday];      // not used by a monthly schedule
        schedule.ParameterValues["Gone"] = ["x"];       // not a parameter of the report

        var id = await _schedules.SaveAsync(schedule, "fawad");
        var saved = (await ((IScheduleRepository)_store).GetByIdAsync(id))!;

        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), saved.NextRunUtc);   // 1 Nov, 06:00 Riyadh
        Assert.Empty(saved.DaysOfWeek);
        Assert.False(saved.ParameterValues.ContainsKey("Gone"));
        Assert.Equal("finance@company.com; cfo@company.com", saved.EmailTo);
        Assert.Equal("fawad", saved.CreatedBy);
    }

    [Fact]
    public async Task Saving_rejects_parameter_values_the_report_would_reject()
    {
        var schedule = NewSchedule();
        schedule.ParameterValues["FromDate"] = ["START_OF_MONHT"];   // typo
        schedule.ParameterValues["Branches"] = ["99"];               // not one of the choices

        var ex = await Assert.ThrowsAsync<ReportValidationException>(() => _schedules.SaveAsync(schedule, "fawad"));

        Assert.Contains("p.FromDate", ex.FieldErrors.Keys);
        Assert.Contains("p.Branches", ex.FieldErrors.Keys);
        Assert.Empty(_store.Schedules);
    }

    [Fact]
    public async Task An_inactive_schedule_has_no_next_run()
    {
        var schedule = NewSchedule();
        schedule.Active = false;

        var id = await _schedules.SaveAsync(schedule, "fawad");

        Assert.Null((await ((IScheduleRepository)_store).GetByIdAsync(id))!.NextRunUtc);
    }

    // ---------------------------------------------------------------- running

    [Fact]
    public async Task A_run_resolves_tokens_saves_the_file_and_emails_it()
    {
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        var schedule = (await ((IScheduleRepository)_store).GetByIdAsync(id))!;

        var run = await _runner.RunAsync(schedule, ScheduleTrigger.Schedule, schedule.NextRunUtc!.Value, "SRV01");

        Assert.Equal(ScheduleRunStatus.Succeeded, run.Status);
        Assert.Equal(3, run.RowCount);
        Assert.Equal("SALES_BY_REGION_20261101_0600.csv", run.FileName);

        // Tokens are worked out for the scheduled time (1 November), so the previous month is October.
        var query = _executor.Queries.Last(q => q.Sql.Contains("sales", StringComparison.OrdinalIgnoreCase));
        var binds = query.Binds.ToDictionary(b => b.Name, b => b.Value);
        Assert.Contains(new DateTime(2026, 10, 1), binds.Values);
        Assert.Contains(new DateTime(2026, 10, 31), binds.Values);
        Assert.Contains("svc_rms_scheduler", binds.Values);       // @RMS_USERNAME
        Assert.Contains(binds, b => b.Value is null);              // @RMS_USER_ID is empty for the service account

        // Saved to the share, then emailed with the file attached and the path mentioned.
        var saved = Assert.Single(Directory.GetFiles(_share.Root));
        Assert.Equal("SALES_BY_REGION_20261101_0600.csv", Path.GetFileName(saved));
        Assert.StartsWith("Saved to ", run.ShareResult);

        var mail = Assert.Single(_email.Sent);
        Assert.Equal(["finance@company.com", "cfo@company.com"], mail.To);
        Assert.Equal("Sales by region - 2026-11-01", mail.Subject);
        var attachment = Assert.Single(mail.Attachments);
        Assert.Equal(run.FileName, attachment.FileName);
        Assert.Equal(File.ReadAllBytes(saved), attachment.Content);
        Assert.Contains("Riyadh, Jeddah", mail.TextBody);             // dropdown labels, not ids
        Assert.Contains(saved, mail.TextBody);
        Assert.Equal("Sent to 2 recipients by test mail", run.EmailResult);

        // Recorded in the run history and in the general run log.
        var recorded = Assert.Single(_store.ScheduleRuns);
        Assert.Equal(ScheduleRunStatus.Succeeded, recorded.Status);
        Assert.NotNull(recorded.FinishedUtc);
        Assert.Contains(_store.RunLog, l => l.Action == "Schedule:csv" && l.Username == "svc_rms_scheduler" && l.UserId is null);
    }

    [Fact]
    public async Task An_empty_report_is_skipped_when_asked()
    {
        var schedule = NewSchedule(_emptyReportId);
        schedule.ParameterValues.Clear();
        schedule.SkipIfEmpty = true;
        var id = await _schedules.SaveAsync(schedule, "fawad");
        _answer = Table(0);

        var run = await _runner.RunAsync((await ((IScheduleRepository)_store).GetByIdAsync(id))!, ScheduleTrigger.Manual, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Skipped, run.Status);
        Assert.Empty(_email.Sent);
        Assert.Empty(Directory.GetFiles(_share.Root));
    }

    [Fact]
    public async Task A_file_too_large_to_attach_is_linked_from_the_share()
    {
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        _email.MaxAttachmentBytes = 10;

        var run = await _runner.RunAsync((await ((IScheduleRepository)_store).GetByIdAsync(id))!, ScheduleTrigger.Manual, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Succeeded, run.Status);
        var mail = Assert.Single(_email.Sent);
        Assert.Empty(mail.Attachments);
        Assert.Contains("too large to attach", mail.TextBody);
        Assert.Contains("without the file", run.EmailResult);
    }

    [Fact]
    public async Task A_file_too_large_to_attach_fails_the_email_when_there_is_no_share()
    {
        var schedule = NewSchedule();
        schedule.SaveToShare = false;
        var id = await _schedules.SaveAsync(schedule, "fawad");
        _email.MaxAttachmentBytes = 10;

        var run = await _runner.RunAsync((await ((IScheduleRepository)_store).GetByIdAsync(id))!, ScheduleTrigger.Manual, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Failed, run.Status);
        Assert.StartsWith("Failed: The file is", run.EmailResult);
    }

    [Fact]
    public async Task One_failed_delivery_makes_the_run_partial()
    {
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        _email.FailWith = "The SMTP server refused the sign-in.";

        var run = await _runner.RunAsync((await ((IScheduleRepository)_store).GetByIdAsync(id))!, ScheduleTrigger.Schedule, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Partial, run.Status);
        Assert.Equal("Failed: The SMTP server refused the sign-in.", run.EmailResult);
        Assert.StartsWith("Saved to", run.ShareResult);
    }

    [Fact]
    public async Task Recipients_outside_the_allowed_domains_are_refused_at_send_time()
    {
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        _options.AllowedEmailDomains = ["other.com"];   // tightened after the schedule was saved

        var run = await _runner.RunAsync((await ((IScheduleRepository)_store).GetByIdAsync(id))!, ScheduleTrigger.Schedule, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Partial, run.Status);
        Assert.Contains("outside the allowed domains", run.EmailResult);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task A_failing_query_is_recorded_as_failed()
    {
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        var brokenExecutor = new FakeReportExecutor(query => query.Sql.Contains("branches", StringComparison.OrdinalIgnoreCase)
            ? new ReportTable { Columns = [new("ID", ReportColumnType.Number), new("NAME", ReportColumnType.Text)], Rows = [[1m, "Riyadh"], [2m, "Jeddah"]] }
            : throw new ReportExecutionException("ORA-00942: table or view does not exist", "ORA-00942"));
        var brokenReports = new ReportService(_store, _store, brokenExecutor, _store, new FakeConnectionCatalog(), new ReportingOptions(), new FixedClock(Now));
        var failing = new ScheduledReportRunner(_store, brokenReports, [new CsvReportExporter()], _email, _share, _options, new FixedClock(Now));

        var run = await failing.RunAsync((await ((IScheduleRepository)_store).GetByIdAsync(id))!, ScheduleTrigger.Schedule, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Failed, run.Status);
        Assert.Equal("The report query failed: ORA-00942: table or view does not exist", run.ErrorMessage);
        Assert.Empty(_email.Sent);
        Assert.Equal(ScheduleRunStatus.Failed, Assert.Single(_store.ScheduleRuns).Status);
    }

    [Fact]
    public async Task A_deleted_report_is_recorded_as_failed()
    {
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        var schedule = (await ((IScheduleRepository)_store).GetByIdAsync(id))!;
        await ((ICrudRepository<Report>)_store).DeleteAsync(_salesReportId);

        var run = await _runner.RunAsync(schedule, ScheduleTrigger.Schedule, Now.UtcDateTime, "SRV01");

        Assert.Equal(ScheduleRunStatus.Failed, run.Status);
        Assert.Equal("The report no longer exists.", run.ErrorMessage);
    }

    // ---------------------------------------------------------------- claiming

    [Fact]
    public async Task Due_schedules_are_claimed_once_and_completed_with_the_next_run()
    {
        IScheduleRepository repository = _store;
        var id = await _schedules.SaveAsync(NewSchedule(), "fawad");
        var due = new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

        Assert.Empty(await repository.GetDueIdsAsync(due.AddSeconds(-1), 10));
        Assert.Equal([id], await repository.GetDueIdsAsync(due, 10));

        Assert.True(await repository.TryClaimAsync(id, "SRV01", due, due.AddHours(1)));
        Assert.False(await repository.TryClaimAsync(id, "SRV02", due, due.AddHours(1)));     // another scheduler
        Assert.Empty(await repository.GetDueIdsAsync(due.AddMinutes(1), 10));                // locked

        var next = ScheduleCalculator.GetNextRunUtc((await repository.GetByIdAsync(id))!, due);
        await repository.CompleteAsync(new ScheduleCompletion(id, "SRV01", ScheduleTrigger.Schedule, next, due, ScheduleRunStatus.Succeeded, null));

        var completed = (await repository.GetByIdAsync(id))!;
        Assert.Equal(new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc), completed.NextRunUtc);
        Assert.Null(completed.LockedBy);
        Assert.Equal(ScheduleRunStatus.Succeeded, completed.LastStatus);
    }

    [Fact]
    public async Task Run_now_is_due_immediately_and_leaves_the_timetable_alone()
    {
        IScheduleRepository repository = _store;
        var schedule = NewSchedule();
        schedule.Active = false;      // Run now works for an inactive schedule too, for testing it
        var id = await _schedules.SaveAsync(schedule, "fawad");
        var now = Now.UtcDateTime;

        await _schedules.RequestRunAsync(id, "fawad");
        Assert.Equal([id], await repository.GetDueIdsAsync(now, 10));
        Assert.True(await repository.TryClaimAsync(id, "SRV01", now, now.AddHours(1)));

        var requested = (await repository.GetByIdAsync(id))!.RunRequestedUtc;
        await repository.CompleteAsync(new ScheduleCompletion(id, "SRV01", ScheduleTrigger.Manual, null, now, ScheduleRunStatus.Succeeded, requested));

        var after = (await repository.GetByIdAsync(id))!;
        Assert.Null(after.RunRequestedUtc);
        Assert.Empty(await repository.GetDueIdsAsync(now.AddMinutes(1), 10));
    }

    /// <summary>Writes to a temporary folder through the real FileShareWriter.</summary>
    private sealed class RecordingShareWriter : IFileShareWriter, IDisposable
    {
        private readonly IFileShareWriter _inner;

        public RecordingShareWriter()
        {
            Root = Path.Combine(Path.GetTempPath(), "rms-share-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _inner = new RMS.Infrastructure.Delivery.FileShareWriter(
                Microsoft.Extensions.Options.Options.Create(new RMS.Infrastructure.Delivery.FileShareOptions()),
                new SchedulingOptions(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RMS.Infrastructure.Delivery.FileShareWriter>.Instance);
        }

        public string Root { get; }

        public Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default) =>
            _inner.SaveAsync(folder, fileName, content, ct);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
