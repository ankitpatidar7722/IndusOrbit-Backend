namespace Indus360.Api.Models;

public sealed class ModuleSettingsRow
{
    public string ModuleHeadName { get; set; } = "";
    public string ModuleDisplayName { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public bool Status { get; set; }
}

public sealed class ModuleGroupModuleRow
{
    public string ModuleHeadName { get; set; } = "";
    public string ModuleDisplayName { get; set; } = "";
    public string ModuleName { get; set; } = "";
}

public sealed class GetModuleSettingsRequest
{
    public string ApplicationName { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

public sealed class ModuleToggle
{
    public string ModuleName { get; set; } = "";
    public bool Status { get; set; }
}

public sealed class SaveModuleSettingsRequest
{
    public string ApplicationName { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public List<ModuleToggle> Modules { get; set; } = new();
}

public sealed class CopyModulesRequest
{
    public string SourceConnectionString { get; set; } = "";
    public string TargetCompanyUserID { get; set; } = "";
}

public sealed class ModuleGroupModulesRequest
{
    public string ApplicationName { get; set; } = "";
    public string ModuleGroupName { get; set; } = "";
}

public sealed class CreateModuleGroupRequest
{
    public string ApplicationName { get; set; } = "";
    public string ModuleGroupName { get; set; } = "";
    public List<string> SelectedModuleNames { get; set; } = new();
}

public sealed class ApplyModuleGroupRequest
{
    public string ApplicationName { get; set; } = "";
    public string ModuleGroupName { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

/// <summary>Edit an existing module group (diff-based add/remove of its modules).</summary>
public sealed class UpdateModuleGroupRequest
{
    public string ApplicationName { get; set; } = "";
    public string ModuleGroupName { get; set; } = "";
    public List<string> SelectedModuleNames { get; set; } = new();
}

/// <summary>Delete a module group — gated by a CompanyWebUser credential + a reason (audit).</summary>
public sealed class DeleteModuleGroupRequest
{
    public string ApplicationName { get; set; } = "";
    public string ModuleGroupName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string Reason { get; set; } = "";
}

// ── New Module Addition tab ──
public sealed class ClientModuleDto
{
    public int ModuleId { get; set; }
    public string ModuleName { get; set; } = "";
    public string? ModuleHeadName { get; set; }
    public string? ModuleDisplayName { get; set; }
    public string? ModuleHeadDisplayName { get; set; }
    public int? ModuleHeadDisplayOrder { get; set; }
    public double? ModuleDisplayOrder { get; set; }
    public double? SetGroupIndex { get; set; }
}

public sealed class ClientModuleRequest
{
    public string ConnectionString { get; set; } = "";
    public ClientModuleDto Module { get; set; } = new();
}

// ── Indus Tool Authority tab (existing IndusToolModuleMaster + CompanyModuleAuthority) ──
public sealed class IndusToolModuleDto
{
    public int ModuleID { get; set; }
    public string ModuleName { get; set; } = "";
    public string? ModulePath { get; set; }
    public string? ModuleIcon { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class SaveToolAuthorityRequest
{
    public string CompanyUserID { get; set; } = "";
    public List<int> EnabledModuleIDs { get; set; } = new();
}
