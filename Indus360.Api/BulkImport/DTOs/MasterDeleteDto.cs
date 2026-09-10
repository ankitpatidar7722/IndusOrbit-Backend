namespace Backend.DTOs;

public class CheckMasterUsageRequestDto
{
    public string ModuleName { get; set; } = string.Empty;
    public int SubModuleId { get; set; }
}

public class MasterUsageResultDto
{
    public bool IsUsed { get; set; }
    public List<UsageDetail> Usages { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public int TotalItemsInGroup { get; set; }
    public int ItemsUsedInTransactions { get; set; }
    public int UnusedItemsCount { get; set; }
}

public class UsageDetail
{
    public string Area { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class DeleteMasterDataRequestDto
{
    public string ModuleName { get; set; } = string.Empty;
    public int SubModuleId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class DeleteMasterDataResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DeletedCount { get; set; }
}
