namespace Backend.DTOs;

public class ExcelPreviewDto
{
    public List<string> Headers { get; set; } = new();
    public List<List<object>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int TotalColumns { get; set; }
}
