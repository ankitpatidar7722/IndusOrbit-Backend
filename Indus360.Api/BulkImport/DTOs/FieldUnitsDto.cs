using System.Collections.Generic;

namespace Backend.DTOs;

public class FieldUnitsDto
{
    public List<string> PurchaseUnit { get; set; } = new List<string>();
    public List<string> EstimationUnit { get; set; } = new List<string>();
    public List<string> StockUnit { get; set; } = new List<string>();
}
