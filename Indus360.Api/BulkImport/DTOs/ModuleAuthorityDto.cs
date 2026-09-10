namespace Backend.DTOs;

// ─── Indus Tool Module Authority (dynamic sidebar per company) ────────────────

public class IndusToolModuleDto
{
    public int ModuleID { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string ModulePath { get; set; } = string.Empty;
    public string ModuleIcon  { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; }   // company-specific; true = access granted
}

public class SaveModuleAuthorityRequest
{
    public string CompanyUserID { get; set; } = string.Empty;
    public List<int> EnabledModuleIDs { get; set; } = new();
}

public class ModuleAuthorityResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int SavedCount { get; set; }
}

// ─── Legacy Module Authority (existing feature) ───────────────────────────────

/// <summary>
/// Row returned by Module Authority: source module + status from login DB comparison.
/// </summary>
public class ModuleAuthorityRowDto
{
    public string ModuleHeadName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string ModuleDisplayName { get; set; } = string.Empty;
    public bool Status { get; set; }
    public bool ExistsInLoginDb { get; set; }
}

/// <summary>
/// Payload sent from the frontend to save Module Authority changes.
/// </summary>
public class ModuleAuthoritySaveDto
{
    public string ModuleHeadName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string ModuleDisplayName { get; set; } = string.Empty;
    public bool Status { get; set; }
}

/// <summary>
/// Request body for saving Module Authority changes.
/// </summary>
public class ModuleAuthoritySaveRequest
{
    public string Product { get; set; } = string.Empty;
    public List<ModuleAuthoritySaveDto> Modules { get; set; } = new();
}
