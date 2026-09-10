namespace Backend.DTOs;

public class LedgerMasterDto
{
    public int LedgerID { get; set; }
    public int LedgerGroupID { get; set; }
    public string? LedgerName { get; set; }
    public string? MailingName { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Pincode { get; set; }
    public string? TelephoneNo { get; set; }
    public string? Email { get; set; }
    public string? MobileNo { get; set; }
    public string? Website { get; set; }
    public string? PANNo { get; set; }
    public string? GSTNo { get; set; }
    public string? SalesRepresentative { get; set; }
    public string? SupplyTypeCode { get; set; }
    public bool? GSTApplicable { get; set; }
    public decimal? DeliveredQtyTolerance { get; set; }
    public string? RefCode { get; set; }
    public string? GSTRegistrationType { get; set; }
    public string? CreditDays { get; set; }  // Database column is nvarchar(64)
    public string? LegalName { get; set; }
    public string? MailingAddress { get; set; }
    public bool IsDeletedTransaction { get; set; }
    public decimal? Distance { get; set; }
    public string? CurrencyCode { get; set; }
    public string? DepartmentName { get; set; }
    public int? DepartmentID { get; set; }
    public string? Designation { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ClientName { get; set; }
    public int? RefClientID { get; set; }

    // Raw string values for fields that failed client-side type parsing
    public Dictionary<string, string>? RawValues { get; set; }
}

public class LedgerValidationResultDto
{
    public List<LedgerRowValidation> Rows { get; set; } = new();
    public ValidationSummary Summary { get; set; } = new();
    public bool IsValid { get; set; }
}

public class LedgerRowValidation
{
    public int RowIndex { get; set; }
    public LedgerMasterDto Data { get; set; } = new();
    public List<CellValidation> CellValidations { get; set; } = new();
    public ValidationStatus RowStatus { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CellValidation
{
    public string ColumnName { get; set; } = string.Empty;
    public string ValidationMessage { get; set; } = string.Empty;
    public ValidationStatus Status { get; set; }
}

public enum ValidationStatus
{
    Valid,
    Duplicate,      // RED
    MissingData,    // BLUE
    Mismatch,       // ORANGE/YELLOW
    InvalidContent  // PURPLE/OTHER
}

public class ValidationSummary
{
    public int DuplicateCount { get; set; }
    public int MissingDataCount { get; set; }
    public int MismatchCount { get; set; }
    public int InvalidContentCount { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
}

public class SalesRepresentativeDto
{
    public int EmployeeID { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
}

public class DepartmentDto
{
    public int DepartmentID { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
}
