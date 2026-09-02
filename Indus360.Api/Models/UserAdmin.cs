namespace Indus360.Api.Models;

// ── User Management (Admin → User Management) ──

/// <summary>One row in the users grid (Indus360App.dbo.Users + Employees mobile).</summary>
public sealed class UserListRow
{
    public long UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Role { get; set; }
    public int? ReportingManagerId { get; set; }
    public string? ReportingManagerName { get; set; }
    public bool IsActive { get; set; }
    public int CompanyId { get; set; }
    public string? EmployeeCode { get; set; }
}

/// <summary>Full user for the edit form (no password returned).</summary>
public sealed class UserDetailDto
{
    public long UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Role { get; set; }
    public int? ReportingManagerId { get; set; }
    public bool IsActive { get; set; }
    public int CompanyId { get; set; }
    public long ProductionUnitId { get; set; }
    public string? FYear { get; set; }
    public string? EmployeeCode { get; set; }
    // Per-user email/SMTP settings
    public string? EmailProvider { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpServer { get; set; }
    public string? SmtpPort { get; set; }
    public bool? SmtpAuthenticate { get; set; }
    public bool? SmtpUseSSL { get; set; }
    public bool HasSmtpPassword { get; set; }   // true if a SMTP password is stored (value itself never returned)
    public string? EmailSignature { get; set; } // per-user HTML email signature (auto-appended when composing)
    public string? PhotoPath { get; set; }      // stored profile-photo filename (served via /api/users/{id}/photo)
}

/// <summary>Body of a profile-photo upload — the cropped square image as a data URL / base64.</summary>
public sealed class UserPhotoRequest
{
    public string ImageBase64 { get; set; } = "";
}

public sealed class UserSaveRequest
{
    public long UserId { get; set; }               // 0 = create
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Password { get; set; }          // create: required; edit: blank = keep current
    public string? Mobile { get; set; }
    public string? Role { get; set; }
    public int? ReportingManagerId { get; set; }
    public bool IsActive { get; set; } = true;
    public int CompanyId { get; set; } = 1;
    // Per-user email/SMTP settings (Emails tab). SmtpPassword blank on edit = keep current.
    public string? EmailProvider { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpServer { get; set; }
    public string? SmtpPort { get; set; }
    public bool? SmtpAuthenticate { get; set; }
    public bool? SmtpUseSSL { get; set; }
    public string? EmailSignature { get; set; }
}

public sealed class ManagerOption { public long UserId { get; set; } public string FullName { get; set; } = ""; }

/// <summary>Settings → Profile: the logged-in user updating their own name/email.</summary>
public sealed class SelfProfileRequest
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
}

/// <summary>Settings → Reset Password: the logged-in user changing their own password.</summary>
public sealed class SelfPasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class UserLookups
{
    public List<string> Roles { get; set; } = new();
    public List<ManagerOption> Managers { get; set; } = new();
}

/// <summary>One module row in the Module Authentication matrix (with this user's flags).</summary>
public sealed class ModuleAuthRow
{
    public long ModuleID { get; set; }
    public string ModuleName { get; set; } = "";
    public string? ModuleDisplayName { get; set; }
    public string? ModuleHeadName { get; set; }
    public string? ModuleHeadDisplayName { get; set; }
    public double? SetGroupIndex { get; set; }
    public double? ModuleDisplayOrder { get; set; }
    public bool CanView { get; set; }
    public bool CanSave { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanPrint { get; set; }
    public bool CanExport { get; set; }
    public bool CanCancel { get; set; }
}

public sealed class ModuleAuthToggle
{
    public long ModuleID { get; set; }
    public bool CanView { get; set; }
    public bool CanSave { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanPrint { get; set; }
    public bool CanExport { get; set; }
    public bool CanCancel { get; set; }
}

public sealed class SaveModuleAuthRequest
{
    public long UserId { get; set; }
    public List<ModuleAuthToggle> Modules { get; set; } = new();
}

/// <summary>Save a user's granted feature-permission keys (opt-in list).</summary>
public sealed class SaveFeaturePermissionsRequest
{
    public List<string> Keys { get; set; } = new();
}
