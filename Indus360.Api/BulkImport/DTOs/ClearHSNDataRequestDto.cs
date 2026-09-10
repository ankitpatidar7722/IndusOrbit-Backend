namespace Backend.DTOs;

public class ClearHSNDataRequestDto
{
    public int CompanyId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
