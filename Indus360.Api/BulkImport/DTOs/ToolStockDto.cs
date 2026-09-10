namespace Backend.DTOs;

public class ToolStockRowDto
{
    public int RowIndex { get; set; }
    public string? ToolGroupName { get; set; }
    public string? ToolCode { get; set; }
    public string? ToolName { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? StockUnit { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }

    // Resolved by backend
    public int ToolID { get; set; }
    public int ToolGroupID { get; set; }
    public int WarehouseID { get; set; }
}

public class ToolStockImportRequest
{
    public List<ToolStockRowDto> Rows { get; set; } = new();
}

public class ToolStockImportResult
{
    public bool Success { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int FailedRows { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> ErrorMessages { get; set; } = new();
}

public class ToolStockEnrichRequest
{
    public List<ToolStockEnrichRowDto> Rows { get; set; } = new();
}

public class ToolStockEnrichRowDto
{
    public string? ToolGroupName { get; set; }
    public string? ToolCode { get; set; }
    public string? ToolName { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? StockUnit { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
}

public class ToolStockEnrichedRow
{
    public string? ToolGroupName { get; set; }
    public string? ToolCode { get; set; }
    public string? ToolName { get; set; }
    public int ToolID { get; set; }
    public int ToolGroupID { get; set; }
    public decimal ReceiptQuantity { get; set; }
    public decimal LandedRate { get; set; }
    public string? BatchNo { get; set; }
    public string? SupplierBatchNo { get; set; }
    public string? StockUnit { get; set; }
    public string? WarehouseName { get; set; }
    public string? BinName { get; set; }
    public int WarehouseID { get; set; }
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class ToolStockEnrichResult
{
    public List<ToolStockEnrichedRow> Rows { get; set; } = new();
    public List<string> InvalidToolNames { get; set; } = new();
    public List<string> InvalidToolGroupNames { get; set; } = new();
}

public class ToolStockValidationRequest
{
    public List<ToolStockEnrichedRow> Rows { get; set; } = new();
}

public class ToolStockValidationResult
{
    public bool IsValid { get; set; }
    public ToolStockValidationSummary Summary { get; set; } = new();
    public List<ToolStockRowValidation> Rows { get; set; } = new();
}

public class ToolStockValidationSummary
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int DuplicateCount { get; set; }
    public int MissingDataCount { get; set; }
    public int MismatchCount { get; set; }
    public int InvalidContentCount { get; set; }
}

public class ToolStockRowValidation
{
    public int RowIndex { get; set; }
    public string RowStatus { get; set; } = "Valid";
    public string? ErrorMessage { get; set; }
    public List<ToolStockCellValidation> CellValidations { get; set; } = new();
}

public class ToolStockCellValidation
{
    public string ColumnName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ValidationMessage { get; set; } = string.Empty;
}
