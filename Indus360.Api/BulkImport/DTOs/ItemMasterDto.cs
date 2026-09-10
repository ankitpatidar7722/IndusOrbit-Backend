using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class ItemMasterDto
{
    public int? ItemID { get; set; }
    public string? ItemName { get; set; }
    public string? ItemCode { get; set; }
    public int? ItemGroupID { get; set; }
    public string? ItemGroupName { get; set; }
    public int? ProductHSNID { get; set; }
    public string? HSNGroup { get; set; }
    public string? StockUnit { get; set; }
    public string? PurchaseUnit { get; set; }
    public string? EstimationUnit { get; set; }
    public decimal? UnitPerPacking { get; set; }
    public decimal? WtPerPacking { get; set; }
    public decimal? ConversionFactor { get; set; }
    public int? ItemSubGroupID { get; set; }
    public string? ItemSubGroupName { get; set; }
    public string? ItemType { get; set; }
    public string? StockType { get; set; }
    public string? StockCategory { get; set; }
    public decimal? SizeW { get; set; }
    public decimal? SizeL { get; set; }
    public string? ItemSize { get; set; }
    public decimal? PurchaseRate { get; set; }
    public string? StockRefCode { get; set; }
    public string? ItemDescription { get; set; }
    public bool? IsDeletedTransaction { get; set; }
    
    // Paper-specific fields
    public string? Quality { get; set; }
    public decimal? GSM { get; set; }
    public string? Manufecturer { get; set; }
    public string? Finish { get; set; }
    public string? ManufecturerItemCode { get; set; }
    public decimal? Caliper { get; set; }
    public int? ShelfLife { get; set; }
    public decimal? EstimationRate { get; set; }
    public decimal? MinimumStockQty { get; set; }
    public bool? IsStandardItem { get; set; }
    public bool? IsRegularItem { get; set; }
    public string? PackingType { get; set; }
    public string? CertificationType { get; set; }
    public string? PaperGroup { get; set; }
    public string? ProductHSNName { get; set; }
    public string? HSNCode { get; set; }

    // REEL-specific field
    public string? BF { get; set; }

    // INK & ADDITIVES-specific fields
    public string? InkColour { get; set; }
    public string? PantoneCode { get; set; }
    public decimal? PurchaseOrderQuantity { get; set; }

    // LAMINATION FILM-specific fields
    public decimal? Thickness { get; set; }
    public decimal? Density { get; set; }

    // ROLL-specific fields
    public decimal? ReleaseGSM { get; set; }
    public decimal? AdhesiveGSM { get; set; }
    public decimal? TotalGSM { get; set; }

    // SHIPPER CARTON-specific fields
    public decimal? SizeH { get; set; }
    public int? NoOfPly { get; set; }
    public decimal? EmptyCartonWt { get; set; }
    public decimal? Capacity { get; set; }
    public decimal? CBF { get; set; }
    public decimal? CBM { get; set; }

    // Dynamic fields from ItemMasterDetails
    public Dictionary<string, object?>? DynamicFields { get; set; }

    // Raw string values for fields that failed client-side type parsing
    // Key = field name (camelCase), Value = original string from Excel
    // Used by validation to detect invalid content when typed fields are null
    public Dictionary<string, string>? RawValues { get; set; }
}

public class ItemValidationResultDto
{
    public bool IsValid { get; set; }
    public List<ItemRowValidation> Rows { get; set; } = new();
    public ValidationSummary Summary { get; set; } = new();
}

public class ItemRowValidation
{
    public int RowIndex { get; set; }
    public ItemMasterDto Data { get; set; } = new();
    public List<CellValidation> CellValidations { get; set; } = new();
    public ValidationStatus RowStatus { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ImportItemsRequest
{
    [Required]
    public List<ItemMasterDto> Items { get; set; } = new();
    
    [Required]
    public int ItemGroupId { get; set; }
}

public class ClearItemDataRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int ItemGroupId { get; set; }
}

public class ItemSubGroupDto
{
    public int ItemSubGroupID { get; set; }
    public string ItemSubGroupName { get; set; } = string.Empty;
}
