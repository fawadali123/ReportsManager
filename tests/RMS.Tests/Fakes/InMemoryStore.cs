using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Tests.Fakes;

/// <summary>
/// In-memory stand-ins for the Oracle repositories, so business rules can be tested without a database.
/// They enforce the same uniqueness and foreign-key rules the schema does.
/// </summary>
public sealed partial class InMemoryStore :
    IUserRepository, IApplicationRepository, IReportCategoryRepository, IReportRepository,
    IUserReportRepository, IReportRunLogRepository
{
    private readonly object _gate = new();
    private int _nextId = 1;

    public List<AppUser> Users { get; } = [];
    public List<ApplicationInfo> Applications { get; } = [];
    public List<ReportCategory> Categories { get; } = [];
    public List<Report> Reports { get; } = [];
    public HashSet<(int UserId, int ReportId)> Assignments { get; } = [];
    public List<ReportRunLog> RunLog { get; } = [];

    private int NextId() => _nextId++;

    // ---- Users ----
    Task<IReadOnlyList<AppUser>> ICrudRepository<AppUser>.GetAllAsync(CancellationToken ct)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<AppUser>>([.. Users.OrderBy(u => u.Code, StringComparer.OrdinalIgnoreCase).Select(Copy)]);
    }

    Task<AppUser?> ICrudRepository<AppUser>.GetByIdAsync(int id, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Users.Where(u => u.Id == id).Select(Copy).FirstOrDefault());
    }

    public Task<AppUser?> GetByAdUsernameAsync(string adUsername, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(Users.Where(u => u.AdUsername.Equals(adUsername.Trim(), StringComparison.OrdinalIgnoreCase)).Select(Copy).FirstOrDefault());
    }

    public Task<int> CreateAsync(AppUser user, CancellationToken ct = default)
    {
        lock (_gate)
        {
            CheckUnique(user);
            var copy = Copy(user);
            copy.Id = NextId();
            Users.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        lock (_gate)
        {
            CheckUnique(user);
            Users[Users.FindIndex(u => u.Id == user.Id)] = Copy(user);
            return Task.CompletedTask;
        }
    }

    Task ICrudRepository<AppUser>.DeleteAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            Users.RemoveAll(u => u.Id == id);
            Assignments.RemoveWhere(a => a.UserId == id);
            foreach (var entry in RunLog.Where(l => l.UserId == id)) entry.UserId = null;
            return Task.CompletedTask;
        }
    }

    private void CheckUnique(AppUser user)
    {
        if (Users.Any(u => u.Id != user.Id && u.Code.Equals(user.Code, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateValueException("UK_RMS_USERS_CODE");
        if (Users.Any(u => u.Id != user.Id && u.AdUsername.Equals(user.AdUsername, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateValueException("UK_RMS_USERS_ADUSER");
    }

    private static AppUser Copy(AppUser u) => new()
    {
        Id = u.Id, Code = u.Code, FullName = u.FullName, Email = u.Email, AdUsername = u.AdUsername, Active = u.Active, Role = u.Role,
    };

    // ---- Applications ----
    Task<IReadOnlyList<ApplicationInfo>> ICrudRepository<ApplicationInfo>.GetAllAsync(CancellationToken ct)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<ApplicationInfo>>([.. Applications.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(Copy)]);
    }

    Task<ApplicationInfo?> ICrudRepository<ApplicationInfo>.GetByIdAsync(int id, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Applications.Where(a => a.Id == id).Select(Copy).FirstOrDefault());
    }

    public Task<int> CreateAsync(ApplicationInfo application, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Applications.Any(a => a.Code.Equals(application.Code, StringComparison.OrdinalIgnoreCase)))
                throw new DuplicateValueException("UK_RMS_APPLICATIONS_CODE");
            var copy = Copy(application);
            copy.Id = NextId();
            Applications.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task UpdateAsync(ApplicationInfo application, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Applications.Any(a => a.Id != application.Id && a.Code.Equals(application.Code, StringComparison.OrdinalIgnoreCase)))
                throw new DuplicateValueException("UK_RMS_APPLICATIONS_CODE");
            Applications[Applications.FindIndex(a => a.Id == application.Id)] = Copy(application);
            return Task.CompletedTask;
        }
    }

    Task ICrudRepository<ApplicationInfo>.DeleteAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            if (Reports.Any(r => r.ApplicationId == id)) throw new RecordInUseException("Application has reports.");
            Applications.RemoveAll(a => a.Id == id);
            return Task.CompletedTask;
        }
    }

    private static ApplicationInfo Copy(ApplicationInfo a) => new()
    {
        Id = a.Id, Code = a.Code, Name = a.Name, Description = a.Description, Active = a.Active, ConnectionName = a.ConnectionName,
    };

    // ---- Categories ----
    Task<IReadOnlyList<ReportCategory>> ICrudRepository<ReportCategory>.GetAllAsync(CancellationToken ct)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<ReportCategory>>([.. Categories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(Copy)]);
    }

    Task<ReportCategory?> ICrudRepository<ReportCategory>.GetByIdAsync(int id, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Categories.Where(c => c.Id == id).Select(Copy).FirstOrDefault());
    }

    public Task<int> CreateAsync(ReportCategory category, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Categories.Any(c => c.Code.Equals(category.Code, StringComparison.OrdinalIgnoreCase)))
                throw new DuplicateValueException("UK_RMS_REPORT_CAT_CODE");
            var copy = Copy(category);
            copy.Id = NextId();
            Categories.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task UpdateAsync(ReportCategory category, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Categories.Any(c => c.Id != category.Id && c.Code.Equals(category.Code, StringComparison.OrdinalIgnoreCase)))
                throw new DuplicateValueException("UK_RMS_REPORT_CAT_CODE");
            Categories[Categories.FindIndex(c => c.Id == category.Id)] = Copy(category);
            return Task.CompletedTask;
        }
    }

    Task ICrudRepository<ReportCategory>.DeleteAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            if (Reports.Any(r => r.CategoryId == id)) throw new RecordInUseException("Category has reports.");
            Categories.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }
    }

    private static ReportCategory Copy(ReportCategory c) => new()
    {
        Id = c.Id, Code = c.Code, Name = c.Name, Description = c.Description, Active = c.Active,
    };

    // ---- Reports ----
    Task<IReadOnlyList<Report>> ICrudRepository<Report>.GetAllAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Report>>([.. Reports.Select(Hydrate)
                .OrderBy(r => r.ApplicationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)]);
        }
    }

    Task<Report?> ICrudRepository<Report>.GetByIdAsync(int id, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Reports.Where(r => r.Id == id).Select(Hydrate).FirstOrDefault());
    }

    public Task<IReadOnlyList<ReportCatalogItem>> GetCatalogAsync(int? userId, bool includeUnavailable, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var items = Reports.Select(Hydrate)
                .Where(r => userId is null || Assignments.Contains((userId.Value, r.Id)))
                .Where(r => includeUnavailable || r.IsAvailable)
                .OrderBy(r => r.ApplicationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new ReportCatalogItem
                {
                    ReportId = r.Id, ReportCode = r.Code, ReportName = r.Name, Description = r.Description,
                    ApplicationId = r.ApplicationId, ApplicationName = r.ApplicationName ?? "",
                    CategoryId = r.CategoryId, CategoryName = r.CategoryName ?? "", Available = r.IsAvailable,
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<ReportCatalogItem>>(items);
        }
    }

    public Task<int> CreateAsync(Report report, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Reports.Any(r => r.Code.Equals(report.Code, StringComparison.OrdinalIgnoreCase)))
                throw new DuplicateValueException("UK_RMS_REPORTS_CODE");
            var copy = Copy(report);
            copy.Id = NextId();
            Reports.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task UpdateAsync(Report report, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Reports.Any(r => r.Id != report.Id && r.Code.Equals(report.Code, StringComparison.OrdinalIgnoreCase)))
                throw new DuplicateValueException("UK_RMS_REPORTS_CODE");
            Reports[Reports.FindIndex(r => r.Id == report.Id)] = Copy(report);
            return Task.CompletedTask;
        }
    }

    Task ICrudRepository<Report>.DeleteAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            Reports.RemoveAll(r => r.Id == id);
            Assignments.RemoveWhere(a => a.ReportId == id);
            foreach (var entry in RunLog.Where(l => l.ReportId == id)) entry.ReportId = null;
            Schedules.RemoveAll(s => s.ReportId == id); // ON DELETE CASCADE
            foreach (var run in ScheduleRuns.Where(r => Schedules.All(s => s.Id != r.ScheduleId))) run.ScheduleId = null;
            return Task.CompletedTask;
        }
    }

    private Report Hydrate(Report source)
    {
        var report = Copy(source);
        var application = Applications.FirstOrDefault(a => a.Id == report.ApplicationId);
        var category = Categories.FirstOrDefault(c => c.Id == report.CategoryId);
        report.ApplicationName = application?.Name;
        report.ApplicationActive = application?.Active ?? false;
        report.ConnectionName = application?.ConnectionName;
        report.CategoryName = category?.Name;
        report.CategoryActive = category?.Active ?? false;
        return report;
    }

    private static Report Copy(Report r) => new()
    {
        Id = r.Id, Code = r.Code, Name = r.Name, Description = r.Description, ApplicationId = r.ApplicationId,
        CategoryId = r.CategoryId, SqlQuery = r.SqlQuery, QueryType = r.QueryType, Active = r.Active,
        Parameters = [.. r.Parameters.Select(p => new ReportParameter
        {
            Id = p.Id, ReportId = r.Id, Name = p.Name, Label = p.Label, DataType = p.DataType, Required = p.Required,
            AllowMultiple = p.AllowMultiple, DefaultValue = p.DefaultValue, LookupSource = p.LookupSource,
            HelpText = p.HelpText, SortOrder = p.SortOrder,
        })],
    };

    // ---- Assignments ----
    public Task<IReadOnlyCollection<int>> GetReportIdsAsync(int userId, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyCollection<int>>([.. Assignments.Where(a => a.UserId == userId).Select(a => a.ReportId)]);
    }

    public Task<bool> IsAssignedAsync(int userId, int reportId, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(Assignments.Contains((userId, reportId)));
    }

    public Task SetAssignmentsAsync(int userId, IReadOnlyCollection<int> reportIds, string assignedBy, CancellationToken ct = default)
    {
        lock (_gate)
        {
            Assignments.RemoveWhere(a => a.UserId == userId);
            foreach (var reportId in reportIds) Assignments.Add((userId, reportId));
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyDictionary<int, int>> GetAssignmentCountsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyDictionary<int, int>>(
                Assignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Count()));
        }
    }

    // ---- Run log ----
    public Task AddAsync(ReportRunLog entry, CancellationToken ct = default)
    {
        lock (_gate)
        {
            entry.Id = RunLog.Count + 1;
            RunLog.Add(entry);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<ReportRunLog>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<ReportRunLog>>([.. RunLog.OrderByDescending(l => l.RunAt).ThenByDescending(l => l.Id).Take(take)]);
    }
}

/// <summary>Report executor that answers from a delegate and remembers the queries it was given.</summary>
public sealed class FakeReportExecutor(Func<ReportQuery, ReportTable> answer) : IReportExecutor
{
    public List<ReportQuery> Queries { get; } = [];

    public Task<IReportResultReader> ExecuteAsync(ReportQuery query, CancellationToken ct = default)
    {
        lock (Queries) Queries.Add(query);
        return Task.FromResult<IReportResultReader>(new TableReader(answer(query)));
    }

    private sealed class TableReader(ReportTable table) : IReportResultReader
    {
        private int _row = -1;
        public IReadOnlyList<ReportColumn> Columns => table.Columns;
        public ValueTask<bool> ReadAsync(CancellationToken ct = default) => new(++_row < table.Rows.Count);
        public object? GetValue(int ordinal) => table.Rows[_row][ordinal];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Connection catalog with a fixed set of names; anything not listed is Oracle.</summary>
public sealed class FakeConnectionCatalog : IReportConnectionCatalog
{
    public Dictionary<string, DatabaseKind> Kinds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ConnectionNames => [.. Kinds.Keys];
    public string DefaultConnectionName { get; set; } = "RmsDb";

    public DatabaseKind GetKind(string? connectionName) =>
        Kinds.GetValueOrDefault(string.IsNullOrWhiteSpace(connectionName) ? DefaultConnectionName : connectionName, DatabaseKind.Oracle);
}

public sealed class FakeDirectory : IDirectoryAuthenticator
{
    public DirectoryAuthResult Result { get; set; } = new(DirectoryAuthStatus.InvalidCredentials);
    public int Calls { get; private set; }

    public Task<DirectoryAuthResult> AuthenticateAsync(
        string username, string password, IReadOnlyCollection<string> groups, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
