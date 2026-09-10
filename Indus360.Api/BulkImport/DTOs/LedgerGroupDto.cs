namespace Backend.DTOs;

public class LedgerGroupDto
{
    public int LedgerGroupID { get; set; }
    public string LedgerGroupName { get; set; } = string.Empty;
    public string LedgerGroupNameDisplay { get; set; } = string.Empty;
    public int? LedgerGroupNameID { get; set; }
}
