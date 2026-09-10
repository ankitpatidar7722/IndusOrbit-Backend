using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class ToolMasterDto
{
    public int? ToolID { get; set; }
    public string? ToolName { get; set; }
    public string? ToolCode { get; set; }
    public int? ToolGroupID { get; set; }
    public string? ToolGroupName { get; set; }
    public string? ToolType { get; set; }
    public string? JobName { get; set; }
    public string? ClientName { get; set; }
    public string? ToolRefCode { get; set; }
    public int? ProductHSNID { get; set; }
    public string? ProductHSNName { get; set; }
    public string? HSNCode { get; set; }

    // Tool-specific fields (PLATES, DIE, PRINTING CYLINDER, FLEXO DIE, etc.)
    public decimal? SizeL { get; set; }
    public decimal? SizeW { get; set; }
    public decimal? SizeH { get; set; }
    public int? UpsAround { get; set; }
    public int? UpsAcross { get; set; }
    public int? TotalUps { get; set; }
    public decimal? AroundGap { get; set; }
    public decimal? AcrossGap { get; set; }
    public string? UnitSymbol { get; set; }
    public string? ReferenceToolNo { get; set; }
    public decimal? EstimateRate { get; set; }
    public string? PurchaseUnit { get; set; }
    public decimal? PurchaseRate { get; set; }
    public string? Manufacturer { get; set; }
    public string? ManufecturerItemCode { get; set; }
    public int? NoOfTeeth { get; set; }
    public decimal? CircumferenceMM { get; set; }
    public decimal? CircumferenceInch { get; set; }
    public decimal? BCM { get; set; }
    public decimal? LPI { get; set; }
    public decimal? PurchaseOrderQuantity { get; set; }
    public int? ShelfLife { get; set; }
    public string? StockUnit { get; set; }
    public decimal? MinimumStockQty { get; set; }
    public bool? IsStandardItem { get; set; }
    public bool? IsRegularItem { get; set; }

    // SIM / SHIM-specific fields (ToolGroupID 13). Stored as text in ToolMasterDetails.
    public string? Positive { get; set; }
    public string? Negative { get; set; }
    public string? Master { get; set; }
    public string? Sim { get; set; }
    public string? Location { get; set; }

    public bool? IsDeletedTransaction { get; set; }

    // Raw string values for fields that failed client-side type parsing
    public Dictionary<string, string>? RawValues { get; set; }
}

public class ToolValidationResultDto
{
    public bool IsValid { get; set; }
    public List<ToolRowValidation> Rows { get; set; } = new();
    public ValidationSummary Summary { get; set; } = new();
}

public class ToolRowValidation
{
    public int RowIndex { get; set; }
    public ToolMasterDto Data { get; set; } = new();
    public List<CellValidation> CellValidations { get; set; } = new();
    public ValidationStatus RowStatus { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ImportToolsRequest
{
    public List<ToolMasterDto> Tools { get; set; } = new();

    public int ToolGroupId { get; set; }
}

public class ClearToolDataRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int ToolGroupId { get; set; }
}
