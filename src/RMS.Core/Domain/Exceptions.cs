namespace RMS.Core.Domain;

/// <summary>Thrown by repositories when a unique constraint is violated.</summary>
public sealed class DuplicateValueException(string constraintName, Exception? inner = null)
    : Exception($"Unique constraint {constraintName} violated.", inner)
{
    /// <summary>Name of the violated constraint or index, for example UK_RMS_USERS_CODE.</summary>
    public string ConstraintName { get; } = constraintName;
}

/// <summary>Thrown by repositories when a row cannot be deleted because other rows reference it.</summary>
public sealed class RecordInUseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The report does not exist, or the current user may not see it.</summary>
public sealed class ReportNotFoundException(int reportId)
    : Exception($"Report {reportId} was not found or is not available to the current user.")
{
    public int ReportId { get; } = reportId;
}

/// <summary>The values entered for a report, or the report definition itself, are not valid.</summary>
public sealed class ReportValidationException : Exception
{
    public ReportValidationException(string message) : base(message)
    {
        FieldErrors = new Dictionary<string, string>();
    }

    public ReportValidationException(IReadOnlyDictionary<string, string> fieldErrors)
        : base("Some values are missing or not valid.")
    {
        FieldErrors = fieldErrors;
    }

    /// <summary>Errors keyed by parameter name. Empty when the problem is not tied to one field.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; }
}

/// <summary>The database rejected or failed the report query.</summary>
public sealed class ReportExecutionException(string message, string? errorCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Database error code such as "ORA-00942", when known.</summary>
    public string? ErrorCode { get; } = errorCode;
}
