using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class HSNMasterDto
{
    public int ProductHSNID { get; set; }
    public string? ProductHSNName { get; set; } // Group Name
    public string? DisplayName { get; set; }
    public string? HSNCode { get; set; }
    public decimal? GSTTaxPercentage { get; set; }
    public decimal? CGSTTaxPercentage { get; set; }
    public decimal? SGSTTaxPercentage { get; set; }
    public decimal? IGSTTaxPercentage { get; set; }
    public string? ItemGroupName { get; set; } // For lookup/display
    public int? ItemGroupID { get; set; }
    public string? ProductCategory { get; set; } // ProductType
    
    // Additional fields for completeness if needed, but primary focus on import columns
    public int CompanyID { get; set; } = 2; // Default
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsDeletedTransaction { get; set; }
}

public class HSNValidationResultDto
{
    public List<HSNRowValidation> Rows { get; set; } = new();
    public ValidationSummary Summary { get; set; } = new();
    public bool IsValid { get; set; }
}

public class HSNRowValidation
{
    public int RowIndex { get; set; }
    public HSNMasterDto Data { get; set; } = new();
    public List<CellValidation> CellValidations { get; set; } = new();
    public ValidationStatus RowStatus { get; set; }
    public string? ErrorMessage { get; set; }
}
