namespace Backend.DTOs;

public class MasterColumnDto
{
    public string FieldName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int SequenceNo { get; set; }
    public string? UnitMeasurement { get; set; }
}
