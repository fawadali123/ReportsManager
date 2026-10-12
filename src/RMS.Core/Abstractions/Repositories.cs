using RMS.Core.Domain;

namespace RMS.Core.Abstractions;

/// <summary>
/// Basic create / read / update / delete contract shared by the maintenance screens.
/// Implementations throw <see cref="DuplicateValueException"/> for unique-key clashes
/// and <see cref="RecordInUseException"/> when a delete is blocked by a foreign key.
/// </summary>
public interface ICrudRepository<T>
{
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <returns>The generated Id.</returns>
    Task<int> CreateAsync(T entity, CancellationToken ct = default);

    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IUserRepository : ICrudRepository<AppUser>
{
    /// <summary>Case-insensitive lookup by AD account name.</summary>
    Task<AppUser?> GetByAdUsernameAsync(string adUsername, CancellationToken ct = default);
}

public interface IApplicationRepository : ICrudRepository<ApplicationInfo>;

public interface IReportCategoryRepository : ICrudRepository<ReportCategory>;

public interface IReportRepository : ICrudRepository<Report>
{
    /// <summary>
    /// Reports for the pickers on the Reports screen.
    /// </summary>
    /// <param name="userId">Limit to reports assigned to this user. Null returns every report.</param>
    /// <param name="includeUnavailable">Also return inactive reports and reports under an inactive application or category.</param>
    Task<IReadOnlyList<ReportCatalogItem>> GetCatalogAsync(int? userId, bool includeUnavailable, CancellationToken ct = default);
}

/// <summary>Which reports each user may run. Maps to RMS_USER_REPORTS.</summary>
public interface IUserReportRepository
{
    Task<IReadOnlyCollection<int>> GetReportIdsAsync(int userId, CancellationToken ct = default);
    Task<bool> IsAssignedAsync(int userId, int reportId, CancellationToken ct = default);

    /// <summary>Makes the user's assignments exactly <paramref name="reportIds"/>, adding and removing as needed.</summary>
    Task SetAssignmentsAsync(int userId, IReadOnlyCollection<int> reportIds, string assignedBy, CancellationToken ct = default);

    /// <summary>Number of assigned reports per user Id.</summary>
    Task<IReadOnlyDictionary<int, int>> GetAssignmentCountsAsync(CancellationToken ct = default);
}

public interface IReportRunLogRepository
{
    /// <summary>
    /// Records a report run. Implementations must not throw: a failure to write the audit line
    /// is logged and swallowed so that it never fails the report itself.
    /// </summary>
    Task AddAsync(ReportRunLog entry, CancellationToken ct = default);

    Task<IReadOnlyList<ReportRunLog>> GetRecentAsync(int take, CancellationToken ct = default);
}
