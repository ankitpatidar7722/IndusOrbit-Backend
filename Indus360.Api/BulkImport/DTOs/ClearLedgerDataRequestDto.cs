namespace Backend.DTOs;

public class ClearLedgerDataRequestDto
{
    public int LedgerGroupId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
