namespace Backend.DTOs;

public class ItemStockRowDto
{
    public int RowIndex { get; set; }
    public string? ItemCode { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? StockUnit { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public string? ItemName { get; set; }
    public string? Quality { get; set; }
    public decimal? GSM { get; set; }
    public string? Manufecturer { get; set; }
    public string? Finish { get; set; }
    public decimal? SizeL { get; set; }
    public decimal? SizeW { get; set; }

    // Resolved by backend
    public int ItemID { get; set; }
    public int ItemGroupID { get; set; }
    public int WarehouseID { get; set; }
}

public class ItemStockImportRequest
{
    public List<ItemStockRowDto> Rows { get; set; } = new();
    public int ItemGroupId { get; set; }
}

public class ItemStockImportResult
{
    public bool Success { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int FailedRows { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> ErrorMessages { get; set; } = new();
}

public class ItemStockEnrichRequest
{
    public List<ItemStockEnrichRowDto> Rows { get; set; } = new();
    public int ItemGroupId { get; set; }
}

public class ItemStockEnrichRowDto
{
    public string? ItemCode { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? StockUnit { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? ItemName { get; set; }
    public string? Quality { get; set; }
    public decimal? GSM { get; set; }
    public string? Manufecturer { get; set; }
    public string? Finish { get; set; }
    public decimal? SizeL { get; set; }
    public decimal? SizeW { get; set; }
}

public class ItemStockEnrichedRow
{
    public string? ItemCode { get; set; }
    public int ItemID { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? StockUnit { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public string? ItemName { get; set; }
    public string? Quality { get; set; }
    public decimal? GSM { get; set; }
    public string? Manufecturer { get; set; }
    public string? Finish { get; set; }
    public decimal? SizeL { get; set; }
    public decimal? SizeW { get; set; }
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class ItemStockEnrichResult
{
    public List<ItemStockEnrichedRow> Rows { get; set; } = new();
    public List<string> InvalidItemCodes { get; set; } = new();
}

public class WarehouseDto
{
    public int WarehouseID { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string? BinName { get; set; }
}

public class ResetStockRequest
{
    public int ItemGroupId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<int> ItemIds { get; set; } = new(); // empty = reset all items

    // Optional date range for Floor Stock Reset — filters on the floor-issue VoucherDate.
    // Both null = reset ALL floor stock (legacy behaviour).
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class ItemStockValidationRequest
{
    public List<ItemStockEnrichedRow> Rows { get; set; } = new();
    public int ItemGroupId { get; set; }
}

public class ItemStockValidationResult
{
    public bool IsValid { get; set; }
    public ItemStockValidationSummary Summary { get; set; } = new();
    public List<ItemStockRowValidation> Rows { get; set; } = new();
}

public class ItemStockValidationSummary
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int DuplicateCount { get; set; }
    public int MissingDataCount { get; set; }
    public int MismatchCount { get; set; }
    public int InvalidContentCount { get; set; }
}

public class ItemStockRowValidation
{
    public int RowIndex { get; set; }
    public string RowStatus { get; set; } = "Valid";
    public string? ErrorMessage { get; set; }
    public List<ItemStockCellValidation> CellValidations { get; set; } = new();
}

public class ItemStockCellValidation
{
    public string ColumnName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ValidationMessage { get; set; } = string.Empty;
}
