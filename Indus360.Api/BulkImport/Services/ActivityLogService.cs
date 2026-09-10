using Backend.DTOs;
using Dapper;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Backend.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly string _indusConnectionString;
    private readonly ILogger<ActivityLogService> _logger;

    public ActivityLogService(IConfiguration configuration, ILogger<ActivityLogService> logger)
    {
        _indusConnectionString = configuration.GetConnectionString("IndusConnection")
            ?? throw new InvalidOperationException("IndusConnection string not found");
        _logger = logger;
    }

    public async Task<int> LogActivityAsync(CreateActivityLogRequest request)
    {
        try
        {
            using var connection = new SqlConnection(_indusConnectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT INTO ActivityLog (
                    WebUserId, WebUserName, LoginType, ActionType, ModuleName, EntityName, EntityID,
                    ActionDescription, OldValue, NewValue, IPAddress, UserAgent,
                    CreatedDate, IsSuccess, ErrorMessage
                )
                VALUES (
                    @WebUserId, @WebUserName, 'indus', @ActionType, @ModuleName, @EntityName, @EntityID,
                    @ActionDescription, @OldValue, @NewValue, @IPAddress, @UserAgent,
                    GETDATE(), @IsSuccess, @ErrorMessage
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var activityLogId = await connection.ExecuteScalarAsync<int>(sql, request);

            _logger.LogInformation(
                "[ActivityLog] {ActionType} by {User}: {Description}",
                request.ActionType,
                request.WebUserName,
                request.ActionDescription
            );

            return activityLogId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ActivityLog] Failed to log activity: {Description}", request.ActionDescription);
            // Don't throw - logging failures shouldn't break the main operation
            return 0;
        }
    }

    public async Task<ActivityLogResponse> GetActivityLogsAsync(ActivityLogFilterRequest filter)
    {
        using var connection = new SqlConnection(_indusConnectionString);
        await connection.OpenAsync();

        // Build WHERE clause
        var whereConditions = new List<string> { "1=1" };
        var parameters = new DynamicParameters();

        if (filter.WebUserId.HasValue)
        {
            whereConditions.Add("WebUserId = @WebUserId");
            parameters.Add("WebUserId", filter.WebUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.WebUserName))
        {
            whereConditions.Add("WebUserName = @WebUserName");
            parameters.Add("WebUserName", filter.WebUserName);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActionType))
        {
            whereConditions.Add("ActionType = @ActionType");
            parameters.Add("ActionType", filter.ActionType);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityName))
        {
            whereConditions.Add("EntityName = @EntityName");
            parameters.Add("EntityName", filter.EntityName);
        }

        if (filter.EntityID.HasValue)
        {
            whereConditions.Add("EntityID = @EntityID");
            parameters.Add("EntityID", filter.EntityID.Value);
        }

        if (filter.StartDate.HasValue)
        {
            whereConditions.Add("CreatedDate >= @StartDate");
            parameters.Add("StartDate", filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            whereConditions.Add("CreatedDate <= @EndDate");
            parameters.Add("EndDate", filter.EndDate.Value);
        }

        var whereClause = string.Join(" AND ", whereConditions);

        // Get total count
        var countSql = $"SELECT COUNT(*) FROM ActivityLog WHERE {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        // Get paginated data
        var offset = (filter.PageNumber - 1) * filter.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", filter.PageSize);

        var dataSql = $@"
            SELECT *
            FROM ActivityLog
            WHERE {whereClause}
            ORDER BY CreatedDate DESC
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY";

        var logs = (await connection.QueryAsync<ActivityLogDto>(dataSql, parameters)).ToList();

        return new ActivityLogResponse
        {
            Logs = logs,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)filter.PageSize)
        };
    }

    public async Task<ActivityLogDto?> GetActivityLogByIdAsync(int activityLogId)
    {
        using var connection = new SqlConnection(_indusConnectionString);
        await connection.OpenAsync();

        var sql = "SELECT * FROM ActivityLog WHERE ActivityLogID = @ActivityLogID";
        return await connection.QueryFirstOrDefaultAsync<ActivityLogDto>(sql, new { ActivityLogID = activityLogId });
    }

    public async Task<List<ActivityLogDto>> GetEntityActivityLogsAsync(string entityName, int entityId)
    {
        using var connection = new SqlConnection(_indusConnectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT *
            FROM ActivityLog
            WHERE EntityName = @EntityName AND EntityID = @EntityID
            ORDER BY CreatedDate DESC";

        var logs = await connection.QueryAsync<ActivityLogDto>(sql, new { EntityName = entityName, EntityID = entityId });
        return logs.ToList();
    }

    public async Task<ActivityLogSummary> GetActivitySummaryAsync()
    {
        using var connection = new SqlConnection(_indusConnectionString);
        await connection.OpenAsync();

        var summary = new ActivityLogSummary();

        // Total activities
        summary.TotalActivities = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ActivityLog"
        );

        // Today's activities
        summary.TodayActivities = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ActivityLog WHERE CAST(CreatedDate AS DATE) = CAST(GETDATE() AS DATE)"
        );

        // This week's activities
        summary.ThisWeekActivities = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ActivityLog WHERE CreatedDate >= DATEADD(DAY, -7, GETDATE())"
        );

        // Failed activities
        summary.FailedActivities = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ActivityLog WHERE IsSuccess = 0"
        );

        // Top actions
        var topActionsSql = @"
            SELECT TOP 10 ActionType, COUNT(*) AS Count
            FROM ActivityLog
            GROUP BY ActionType
            ORDER BY COUNT(*) DESC";

        summary.TopActions = (await connection.QueryAsync<ActionTypeCount>(topActionsSql)).ToList();

        // Top users
        var topUsersSql = @"
            SELECT TOP 10 WebUserId, WebUserName, COUNT(*) AS Count
            FROM ActivityLog
            GROUP BY WebUserId, WebUserName
            ORDER BY COUNT(*) DESC";

        summary.TopUsers = (await connection.QueryAsync<UserActivityCount>(topUsersSql)).ToList();

        return summary;
    }

    public async Task<int> DeleteOldLogsAsync(int daysToKeep)
    {
        using var connection = new SqlConnection(_indusConnectionString);
        await connection.OpenAsync();

        var sql = @"
            DELETE FROM ActivityLog
            WHERE CreatedDate < DATEADD(DAY, -@DaysToKeep, GETDATE())";

        var deletedCount = await connection.ExecuteAsync(sql, new { DaysToKeep = daysToKeep });

        _logger.LogInformation("[ActivityLog] Deleted {Count} old activity logs (older than {Days} days)", deletedCount, daysToKeep);

        return deletedCount;
    }

    public async Task<List<string>> GetUsernamesAsync()
    {
        using var connection = new SqlConnection(_indusConnectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT DISTINCT WebUsername
            FROM CompanyWebUser
            WHERE WebUsername IS NOT NULL
            ORDER BY WebUsername";

        var usernames = await connection.QueryAsync<string>(sql);
        return usernames.ToList();
    }
}
