namespace Backend.DTOs;

public class SparePartStockRowDto
{
    public int RowIndex { get; set; }
    public string? SparePartCode { get; set; }
    public string? SparePartName { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? StockUnit { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }

    // Resolved by backend
    public int SpareID { get; set; }
    public int SpareGroupID { get; set; }
    public int WarehouseID { get; set; }
}

public class SparePartStockImportRequest
{
    public List<SparePartStockRowDto> Rows { get; set; } = new();
}

public class SparePartStockImportResult
{
    public bool Success { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int FailedRows { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> ErrorMessages { get; set; } = new();
}

public class SparePartStockEnrichRequest
{
    public List<SparePartStockEnrichRowDto> Rows { get; set; } = new();
}

public class SparePartStockEnrichRowDto
{
    public string? SparePartCode { get; set; }
    public string? SparePartName { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? StockUnit { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
}

public class SparePartStockEnrichedRow
{
    public string? SparePartCode { get; set; }
    public string? SparePartName { get; set; }
    public int SpareID { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? StockUnit { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class SparePartStockEnrichResult
{
    public List<SparePartStockEnrichedRow> Rows { get; set; } = new();
    public List<string> InvalidSparePartNames { get; set; } = new();
}

public class SparePartStockValidationRequest
{
    public List<SparePartStockEnrichedRow> Rows { get; set; } = new();
}

public class SparePartStockValidationResult
{
    public bool IsValid { get; set; }
    public SparePartStockValidationSummary Summary { get; set; } = new();
    public List<SparePartStockRowValidation> Rows { get; set; } = new();
}

public class SparePartStockValidationSummary
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int DuplicateCount { get; set; }
    public int MissingDataCount { get; set; }
    public int MismatchCount { get; set; }
    public int InvalidContentCount { get; set; }
}

public class SparePartStockRowValidation
{
    public int RowIndex { get; set; }
    public string RowStatus { get; set; } = "Valid";
    public string? ErrorMessage { get; set; }
    public List<SparePartStockCellValidation> CellValidations { get; set; } = new();
}

public class SparePartStockCellValidation
{
    public string ColumnName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ValidationMessage { get; set; } = string.Empty;
}
