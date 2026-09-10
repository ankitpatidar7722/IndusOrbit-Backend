using Backend.DTOs;

namespace Backend.Services;

public interface IActivityLogService
{
    /// <summary>
    /// Log a new activity
    /// </summary>
    Task<int> LogActivityAsync(CreateActivityLogRequest request);

    /// <summary>
    /// Get activity logs with filtering and pagination
    /// </summary>
    Task<ActivityLogResponse> GetActivityLogsAsync(ActivityLogFilterRequest filter);

    /// <summary>
    /// Get activity log by ID
    /// </summary>
    Task<ActivityLogDto?> GetActivityLogByIdAsync(int activityLogId);

    /// <summary>
    /// Get activity logs for a specific entity
    /// </summary>
    Task<List<ActivityLogDto>> GetEntityActivityLogsAsync(string entityName, int entityId);

    /// <summary>
    /// Get activity summary statistics
    /// </summary>
    Task<ActivityLogSummary> GetActivitySummaryAsync();

    /// <summary>
    /// Delete old activity logs (for maintenance)
    /// </summary>
    Task<int> DeleteOldLogsAsync(int daysToKeep);

    /// <summary>
    /// Get unique usernames from CompanyWebUser for filter dropdown
    /// </summary>
    Task<List<string>> GetUsernamesAsync();
}
