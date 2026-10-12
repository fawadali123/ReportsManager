using Microsoft.Extensions.Logging;
using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Infrastructure.Data.Providers;

namespace RMS.Infrastructure.Data;

internal sealed class UserReportRepository(AppDatabase db) : IUserReportRepository
{
    public async Task<IReadOnlyCollection<int>> GetReportIdsAsync(int userId, CancellationToken ct = default) =>
        await db.QueryAsync(
            "SELECT REPORT_ID FROM RMS_USER_REPORTS WHERE USER_ID = @p_user_id",
            r => r.Int("REPORT_ID"), ct, P.Int("p_user_id", userId));

    public async Task<bool> IsAssignedAsync(int userId, int reportId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            "SELECT 1 AS FOUND FROM RMS_USER_REPORTS WHERE USER_ID = @p_user_id AND REPORT_ID = @p_report_id",
            r => r.Int("FOUND"), ct, P.Int("p_user_id", userId), P.Int("p_report_id", reportId));
        return rows.Count > 0;
    }

    public Task SetAssignmentsAsync(
        int userId, IReadOnlyCollection<int> reportIds, string assignedBy, CancellationToken ct = default) =>
        db.InTransactionAsync(async session =>
        {
            var current = await session.QueryAsync(
                "SELECT REPORT_ID FROM RMS_USER_REPORTS WHERE USER_ID = @p_user_id",
                r => r.Int("REPORT_ID"), ct, P.Int("p_user_id", userId));

            var wanted = reportIds.ToHashSet();

            // Only the differences are written, so unchanged rows keep who assigned them and when.
            foreach (var reportId in current.Where(id => !wanted.Contains(id)))
            {
                await session.ExecuteAsync(
                    "DELETE FROM RMS_USER_REPORTS WHERE USER_ID = @p_user_id AND REPORT_ID = @p_report_id",
                    ct, P.Int("p_user_id", userId), P.Int("p_report_id", reportId));
            }

            foreach (var reportId in wanted.Except(current))
            {
                await session.ExecuteAsync(
                    """
                    INSERT INTO RMS_USER_REPORTS (USER_ID, REPORT_ID, ASSIGNED_BY)
                    VALUES (@p_user_id, @p_report_id, @p_assigned_by)
                    """,
                    ct, P.Int("p_user_id", userId), P.Int("p_report_id", reportId), P.Text("p_assigned_by", assignedBy));
            }

            return 0;
        }, ct);

    public async Task<IReadOnlyDictionary<int, int>> GetAssignmentCountsAsync(CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            "SELECT USER_ID, COUNT(*) AS REPORT_COUNT FROM RMS_USER_REPORTS GROUP BY USER_ID",
            r => (UserId: r.Int("USER_ID"), Count: r.Int("REPORT_COUNT")), ct);
        return rows.ToDictionary(x => x.UserId, x => x.Count);
    }
}

internal sealed class ReportRunLogRepository(AppDatabase db, ILogger<ReportRunLogRepository> logger) : IReportRunLogRepository
{
    public async Task AddAsync(ReportRunLog entry, CancellationToken ct = default)
    {
        try
        {
            await db.ExecuteAsync(
                """
                INSERT INTO RMS_REPORT_RUN_LOG
                       (REPORT_ID, REPORT_NAME, USER_ID, USERNAME, RUN_AT, ACTION, DURATION_MS, ROW_COUNT, SUCCESS, ERROR_MESSAGE, PARAMETERS)
                VALUES (@p_report_id, @p_report_name, @p_user_id, @p_username, @p_run_at, @p_action, @p_duration_ms, @p_row_count,
                        @p_success, @p_error_message, @p_parameters)
                """,
                ct,
                P.Int("p_report_id", entry.ReportId),
                P.Text("p_report_name", entry.ReportName),
                P.Int("p_user_id", entry.UserId),
                P.Text("p_username", entry.Username),
                P.Timestamp("p_run_at", entry.RunAt),
                P.Text("p_action", entry.Action),
                P.Long("p_duration_ms", entry.DurationMs),
                P.Int("p_row_count", entry.RowCount),
                P.Bool("p_success", entry.Success),
                P.Text("p_error_message", entry.ErrorMessage),
                P.LongText("p_parameters", entry.Parameters));
        }
        catch (Exception ex)
        {
            // By contract a failed audit write must not fail the report the user is waiting for.
            logger.LogError(ex, "Could not write the run log entry for report {ReportId} and user {Username}",
                entry.ReportId, entry.Username);
        }
    }

    public async Task<IReadOnlyList<ReportRunLog>> GetRecentAsync(int take, CancellationToken ct = default) =>
        await db.QueryAsync(
            """
            SELECT ID, REPORT_ID, REPORT_NAME, USER_ID, USERNAME, RUN_AT, ACTION, DURATION_MS,
                   ROW_COUNT, SUCCESS, ERROR_MESSAGE, PARAMETERS
              FROM RMS_REPORT_RUN_LOG
             ORDER BY RUN_AT DESC, ID DESC
            OFFSET 0 ROWS FETCH NEXT @p_take ROWS ONLY
            """,
            r => new ReportRunLog
            {
                Id = r.Long("ID"),
                ReportId = r.IntOrNull("REPORT_ID"),
                ReportName = r.TextOrNull("REPORT_NAME"),
                UserId = r.IntOrNull("USER_ID"),
                Username = r.Text("USERNAME"),
                RunAt = r.Date("RUN_AT"),
                Action = r.Text("ACTION"),
                DurationMs = r.Long("DURATION_MS"),
                RowCount = r.IntOrNull("ROW_COUNT"),
                Success = r.Bool("SUCCESS"),
                ErrorMessage = r.TextOrNull("ERROR_MESSAGE"),
                Parameters = r.TextOrNull("PARAMETERS"),
            },
            ct, P.Int("p_take", take));
}
