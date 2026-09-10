namespace Backend.DTOs;

public class ImportResultDto
{
    public bool Success { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int ErrorRows { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
