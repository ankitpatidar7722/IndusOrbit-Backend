namespace Backend.DTOs;

public class ItemGroupDto
{
    public int ItemGroupID { get; set; }
    public string ItemGroupName { get; set; } = string.Empty;
    public string ItemGroupPrefix { get; set; } = string.Empty;
    public string? ItemNameFormula { get; set; }
    public string? ItemDescriptionFormula { get; set; }
}
