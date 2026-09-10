namespace Backend.DTOs;

public class SparePartMasterDto
{
    public int SparePartID { get; set; }
    public string? SparePartName { get; set; }
    public string? SparePartGroup { get; set; }
    public string? HSNGroup { get; set; }
    public string? Unit { get; set; }
    public decimal? Rate { get; set; }
    public string? SparePartType { get; set; }
    public decimal? MinimumStockQty { get; set; }
    public decimal? PurchaseOrderQuantity { get; set; }
    public string? StockRefCode { get; set; }
    public string? SupplierReference { get; set; }
    public string? Narration { get; set; }
    public bool IsDeletedTransaction { get; set; }
}

public class SparePartValidationResultDto
{
    public List<SparePartRowValidation> Rows { get; set; } = new();
    public ValidationSummary Summary { get; set; } = new();
    public bool IsValid { get; set; }
}

public class SparePartRowValidation
{
    public int RowIndex { get; set; }
    public SparePartMasterDto Data { get; set; } = new();
    public List<CellValidation> CellValidations { get; set; } = new();
    public ValidationStatus RowStatus { get; set; }
    public string? ErrorMessage { get; set; }
}

// Using shared ValidationStatus and CellValidation from LedgerMasterDto
// Using shared ValidationSummary from LedgerMasterDto

public class HSNGroupDto
{
    public int ProductHSNID { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ProductHSNName { get; set; }
    public string? HSNCode { get; set; }
}

public class UnitDto
{
    public int UnitID { get; set; }
    public string UnitSymbol { get; set; } = string.Empty;
}
