namespace Indus360.Api.Models;

/// <summary>Login payload from the next-auth Credentials provider.</summary>
public sealed class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// The user identity returned on login and projected into the next-auth session.
/// Field names match what indas-ui's API client expects on session.user
/// (UserID / CompanyID / ProductionUnitID / FYear / companyUsername / companyPassword).
/// </summary>
public sealed class SessionUser
{
    public long UserID { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "User";
    public int CompanyID { get; set; }
    public long ProductionUnitID { get; set; }
    public string FYear { get; set; } = "";
    public string CompanyUsername { get; set; } = "";
    public string CompanyPassword { get; set; } = "";
}

/// <summary>
/// One sidebar module row. PascalCase names are intentional — indas-ui's
/// groupAndSortModules/buildModuleHierarchy read ModuleHeadName, SetGroupIndex,
/// ModuleName, ParentModuleName, ModuleDisplayOrder, ModuleDisplayName exactly.
/// </summary>
public sealed class NavModule
{
    public string ModuleName { get; set; } = "";
    public string ModuleDisplayName { get; set; } = "";
    public string ModuleHeadName { get; set; } = "";
    public string? ModuleHeadDisplayName { get; set; }
    public double ModuleDisplayOrder { get; set; }
    public double SetGroupIndex { get; set; }
    public string? ModuleIcon { get; set; }
    public string? ParentModuleName { get; set; }
    public bool CanView { get; set; }
    public bool CanSave { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanPrint { get; set; }
    public bool CanExport { get; set; }
    public bool CanCancel { get; set; }
}
