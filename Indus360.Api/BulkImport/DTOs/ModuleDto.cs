namespace Backend.DTOs;

public class ModuleDto
{
    public int ModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string? ModuleHeadName { get; set; }
    public string? ModuleDisplayName { get; set; }
    public string? Description { get; set; }

    // Display Fields
    public string? ModuleHeadDisplayName { get; set; }
    public int? ModuleHeadDisplayOrder { get; set; }
    public int? ModuleDisplayOrder { get; set; }
    public int? SetGroupIndex { get; set; }

    // Print Fields
    public string? PrintDocumentWebPage { get; set; }
    public string? PrintDocumentName { get; set; }
    public string? PrintDocumentWebPage1 { get; set; }
    public string? PrintDocumentName1 { get; set; }

    // System Fields
    public int? CompanyID { get; set; }
    public int? UserID { get; set; }
    public string? FYear { get; set; }
}

/// <summary>
/// Auto-fill info returned when a ModuleName is selected from IndusEnterpriseDemo.
/// </summary>
public class IndusModuleInfoDto
{
    public string ModuleName { get; set; } = string.Empty;
    public string? ModuleDisplayName { get; set; }
    public string? ModuleHeadName { get; set; }
    public string? ModuleHeadDisplayName { get; set; }
    public int? SetGroupIndex { get; set; }    // From current DB (login DB)
    public int? SuggestedHeadDisplayOrder { get; set; }  // MAX + 1 for SetGroupIndex
}

/// <summary>
/// Default system values (CompanyID, UserID, FYear) for the form.
/// </summary>
public class ModuleSystemDefaultsDto
{
    public int CompanyID { get; set; }
    public int UserID { get; set; }
    public string FYear { get; set; } = string.Empty;
    public int SuggestedHeadDisplayOrder { get; set; }
    public int SuggestedDisplayOrder { get; set; }
}

public class ItemGroupComparisonDto
{
    public int ItemGroupId { get; set; }
    public string ItemGroupName { get; set; } = string.Empty;
    public bool ExistsInSource { get; set; }
    public bool ExistsInClient { get; set; }
    public bool IsDeletedInClient { get; set; }
    public bool Status { get; set; } // The final CheckBox value: ExistsInBoth AND !IsDeleted
}

// ── Dynamic Field Master (sync a group's FIELDS from the Source DB into a client DB) ──
/// <summary>One "Sub Module" = a group of the chosen master (Item/Ledger/Tool), e.g. the Ledger
/// group "Client". Populates the Sub Module dropdown.</summary>
public class FieldSubModuleDto
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
}

/// <summary>One field row in the Dynamic Field Master grid — a field of the selected group in the
/// Source DB, flagged by whether it also exists (not soft-deleted) in the client DB.</summary>
public class FieldComparisonDto
{
    public int FieldId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string FieldDisplayName { get; set; } = string.Empty;
    public string FieldDataType { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool ExistsInSource { get; set; }
    public bool ExistsInClient { get; set; }
    public bool IsDeletedInClient { get; set; }
    public bool Status { get; set; } // ticked = present+active in the client (ExistsInBoth AND !IsDeleted)
}
