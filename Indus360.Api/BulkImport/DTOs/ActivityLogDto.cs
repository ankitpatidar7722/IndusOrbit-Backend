namespace Backend.DTOs;

public class ActivityLogDto
{
    public int ActivityLogID { get; set; }
    public int? WebUserId { get; set; }
    public string WebUserName { get; set; } = string.Empty;
    public string LoginType { get; set; } = "indus";
    public string ActionType { get; set; } = string.Empty;
    public string ModuleName { get; set; } = "Company Subscription";
    public string? EntityName { get; set; }
    public int? EntityID { get; set; }
    public string ActionDescription { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreateActivityLogRequest
{
    public int? WebUserId { get; set; }
    public string WebUserName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = "Company Subscription";
    public string ActionType { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public int? EntityID { get; set; }
    public string ActionDescription { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public class ActivityLogFilterRequest
{
    public int? WebUserId { get; set; }
    public string? WebUserName { get; set; }
    public string? ActionType { get; set; }
    public string? EntityName { get; set; }
    public int? EntityID { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class ActivityLogResponse
{
    public List<ActivityLogDto> Logs { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class ActivityLogSummary
{
    public int TotalActivities { get; set; }
    public int TodayActivities { get; set; }
    public int ThisWeekActivities { get; set; }
    public int FailedActivities { get; set; }
    public List<ActionTypeCount> TopActions { get; set; } = new();
    public List<UserActivityCount> TopUsers { get; set; } = new();
}

public class ActionTypeCount
{
    public string ActionType { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class UserActivityCount
{
    public int? WebUserId { get; set; }
    public string WebUserName { get; set; } = string.Empty;
    public int Count { get; set; }
}
