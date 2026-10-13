using System.Collections.Concurrent;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Scheduling;
using RMS.Core.Services;

namespace RMS.Scheduler;

/// <summary>
/// Every few seconds, finds the schedules that are due, locks each one in the database (so a second scheduler
/// server would skip it), runs it, records the outcome and works out the next run.
/// </summary>
/// <remarks>
/// A slot missed while the service was stopped runs once when it starts again; further missed slots are skipped,
/// so a long outage does not produce a flood of old reports.
/// </remarks>
internal sealed class SchedulerWorker(
    IServiceScopeFactory scopes,
    SchedulingOptions options,
    TimeProvider clock,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    private readonly string _machine = Environment.MachineName;
    private readonly ConcurrentDictionary<int, Task> _running = new();

    /// <summary>Lock owner name. One scheduler per machine, so the machine name identifies it.</summary>
    private string Owner => _machine;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 5, 3600));
        var parallel = Math.Clamp(options.MaxParallelRuns, 1, 20);

        logger.LogInformation(
            "RMS Scheduler started on {Machine}. Reports run as {RunAs}; checking every {Seconds} seconds, up to {Parallel} at a time.",
            _machine, options.RunAsUsername, poll.TotalSeconds, parallel);

        await RecoverAsync(poll, stoppingToken);

        using var slots = new SemaphoreSlim(parallel, parallel);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartDueSchedulesAsync(slots, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Looking for due schedules failed. Trying again in {Seconds} seconds.", poll.TotalSeconds);
            }

            try
            {
                await Task.Delay(poll, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Runs still going are cancelled through the stopping token; wait for them to record that.
        await Task.WhenAll(_running.Values);
        logger.LogInformation("RMS Scheduler stopped.");
    }

    /// <summary>Marks runs a previous start of this service left unfinished as failed, and releases their locks.</summary>
    private async Task RecoverAsync(TimeSpan retry, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
                var failed = await repository.RecoverInterruptedAsync(Owner, _machine, UtcNow, ct);
                if (failed > 0)
                {
                    logger.LogWarning("Marked {Count} run(s) left unfinished by the previous start as failed.", failed);
                }
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cannot reach the RMS database. Trying again in {Seconds} seconds.", retry.TotalSeconds);
                try
                {
                    await Task.Delay(retry, clock, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task StartDueSchedulesAsync(SemaphoreSlim slots, CancellationToken ct)
    {
        if (slots.CurrentCount == 0)
        {
            return;
        }

        IReadOnlyList<int> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IScheduleRepository>()
                .GetDueIdsAsync(UtcNow, slots.CurrentCount + _running.Count, ct);
        }

        foreach (var id in due)
        {
            if (_running.ContainsKey(id) || !await slots.WaitAsync(0, ct))
            {
                continue;
            }

            bool claimed;
            try
            {
                claimed = await ClaimAsync(id, ct);
            }
            catch
            {
                slots.Release();
                throw;
            }

            if (!claimed)
            {
                slots.Release();
                continue;
            }

            _running[id] = Task.Run(async () =>
            {
                try
                {
                    await RunOneAsync(id, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Schedule {ScheduleId} could not be completed.", id);
                }
                finally
                {
                    _running.TryRemove(id, out _);
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task<bool> ClaimAsync(int id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var now = UtcNow;

        // Longer than a run may take, so the lock cannot run out while the report is still being produced.
        var lockUntil = now.AddMinutes(Math.Max(1, options.RunTimeoutMinutes) + 10);
        return await scope.ServiceProvider.GetRequiredService<IScheduleRepository>()
            .TryClaimAsync(id, Owner, now, lockUntil, ct);
    }

    private async Task RunOneAsync(int id, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();
        var runner = scope.ServiceProvider.GetRequiredService<ScheduledReportRunner>();

        var schedule = await repository.GetByIdAsync(id, CancellationToken.None);
        if (schedule is null)
        {
            return; // deleted after it was claimed
        }

        var manual = schedule.RunRequestedUtc is not null;
        var trigger = manual ? ScheduleTrigger.Manual : ScheduleTrigger.Schedule;
        var scheduledFor = manual ? schedule.RunRequestedUtc!.Value : schedule.NextRunUtc ?? UtcNow;

        logger.LogInformation("Running schedule {ScheduleId} \"{Schedule}\" for report \"{Report}\" ({Trigger}).",
            id, schedule.Name, schedule.ReportName, manual ? $"Run now, by {schedule.RunRequestedBy}" : "on schedule");

        var status = ScheduleRunStatus.Failed;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, options.RunTimeoutMinutes)));

            var run = await runner.RunAsync(schedule, trigger, scheduledFor, _machine, timeout.Token);
            status = run.Status;

            if (run.Status is ScheduleRunStatus.Succeeded or ScheduleRunStatus.Skipped)
            {
                logger.LogInformation(
                    "Schedule {ScheduleId} \"{Schedule}\": {Status}, {Rows} rows. {Share} {Email}",
                    id, schedule.Name, run.Status, run.RowCount, run.ShareResult, run.EmailResult);
            }
            else
            {
                logger.LogWarning(
                    "Schedule {ScheduleId} \"{Schedule}\": {Status}. {Error} {Share} {Email}",
                    id, schedule.Name, run.Status, run.ErrorMessage, run.ShareResult, run.EmailResult);
            }
        }
        finally
        {
            // Always release the lock and move the timetable on, even when the run itself could not be recorded.
            var fresh = await repository.GetByIdAsync(id, CancellationToken.None) ?? schedule;
            var nextRun = trigger == ScheduleTrigger.Schedule && fresh.Active
                ? ScheduleCalculator.GetNextRunUtc(fresh, Later(UtcNow, scheduledFor))
                : fresh.NextRunUtc;

            await repository.CompleteAsync(
                new ScheduleCompletion(id, Owner, trigger, nextRun, UtcNow, status, manual ? schedule.RunRequestedUtc : null),
                CancellationToken.None);
        }
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;
}
