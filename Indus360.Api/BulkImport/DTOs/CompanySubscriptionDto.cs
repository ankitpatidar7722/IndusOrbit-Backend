namespace Backend.DTOs;

public class CompanySubscriptionDto
{
    public string CompanyUserID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Conn_String { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? ApiCompanyUserName { get; set; }
    public string? ApiCompanyPassword { get; set; }
    public string? ApplicationName { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? DataBaseLocation { get; set; }
    public DateTime? LastLoginDateTime { get; set; }
    public bool? IsActive { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? ApplicationBaseURL { get; set; }
    public string? CompanyCode { get; set; }
    public string? CompanyUniqueCode { get; set; }
    public int? MaxCompanyUniqueCode { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? FYear { get; set; }
    public DateTime? PaymentDueDate { get; set; }
    public string? SubscriptionStatus { get; set; }
    public string? StatusDescription { get; set; }
    public string? SubscriptionStatusMessage { get; set; }
    public long? LoginAllowed { get; set; }
    public string? GSTIN { get; set; }
    public string? LatestVersion { get; set; }
    public long? LoginAllowedOldVersion { get; set; }
    public string? OldVersion { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public bool? IsMessageActive { get; set; }
    public int? MessageDurationValue { get; set; }
    public string? MessageDurationType { get; set; }
    // Cloud Subscription
    public DateTime? CloudFromDate { get; set; }
    public DateTime? CloudToDate { get; set; }
    public DateTime? CloudPaymentDueDate { get; set; }
    public string? CloudSubscriptionStatus { get; set; }
}

public class CompanySubscriptionListResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<CompanySubscriptionDto> Data { get; set; } = new();
}

public class CompanySubscriptionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public CompanySubscriptionDto? Data { get; set; }
}

public class CompanySubscriptionSaveRequest
{
    public string CompanyUserID { get; set; } = string.Empty;
    public string? OriginalCompanyUserID { get; set; }
    public string Password { get; set; } = string.Empty;
    public string? Conn_String { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? ApplicationName { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? SubscriptionStatus { get; set; }
    public string? StatusDescription { get; set; }
    public string? SubscriptionStatusMessage { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? CompanyCode { get; set; }
    public string? CompanyUniqueCode { get; set; }
    public int? MaxCompanyUniqueCode { get; set; }
    public string? GSTIN { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public long? LoginAllowed { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime? PaymentDueDate { get; set; }
    public string? FYear { get; set; }
    public bool? IsMessageActive { get; set; }
    public int? MessageDurationValue { get; set; }
    public string? MessageDurationType { get; set; }
    // Cloud Subscription
    public DateTime? CloudFromDate { get; set; }
    public DateTime? CloudToDate { get; set; }
    public DateTime? CloudPaymentDueDate { get; set; }
    public string? CloudSubscriptionStatus { get; set; }
}

public class NextClientCodeResponse
{
    public bool Success { get; set; }
    public string CompanyUniqueCode { get; set; } = string.Empty;
    public int MaxCompanyUniqueCode { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SetupDatabaseRequest
{
    public string Server { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string? BackupDatabaseName { get; set; }
}

public class SetupDatabaseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
}

public class DynamicBackupDatabaseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Databases { get; set; } = new();
}

public class ServerListResponse
{
    public bool Success { get; set; }
    public List<string> Servers { get; set; } = new();
}

// ─── Step 3: Company Master ───
public class CompanyMasterRequest
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CompanyID { get; set; } = 2;
    public string CompanyName { get; set; } = string.Empty;
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? ContactNO { get; set; }
    public string? MobileNO { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? StateTinNo { get; set; }
    public string? CINNo { get; set; }
    public string? ProductionUnitAddress { get; set; }
    public string? Address { get; set; }
    public string? GSTIN { get; set; }
    public string? ProductionUnitName { get; set; }
    public string? PAN { get; set; }
}

public class CompanyMasterResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CompanyID { get; set; }
}

// ─── Step 4: Branch Master ───
public class BranchMasterRequest
{
    public string ConnectionString { get; set; } = string.Empty;
    public int BranchID { get; set; } = 1;
    public string BranchName { get; set; } = string.Empty;
    // Auto-fill from previous steps
    public string? MailingName { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? MobileNo { get; set; }
    public string? Email { get; set; }
    public string? StateTinNo { get; set; }
    public string? GSTIN { get; set; }
    public int? CompanyID { get; set; }
}

public class BranchMasterResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

// ─── Step 5: Production Unit Master ───
public class ProductionUnitRequest
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ProductionUnitName { get; set; } = string.Empty;
    public string? Address { get; set; }
    // Auto-fill
    public string? City { get; set; }
    public string? State { get; set; }
    public string? GSTNo { get; set; }
    public string? Pincode { get; set; }
    public string? Country { get; set; }
    public string? PAN { get; set; }
}

public class ProductionUnitResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

// ─── Final Step: Complete Setup ───
public class CompleteSetupRequest
{
    public string ConnectionString { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string CompanyUserID { get; set; } = string.Empty;
}

public class CompleteSetupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string CompanyUserID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserPassword { get; set; } = string.Empty;
}

// ─── Module Settings ───
public class ModuleSettingsRow
{
    public string ModuleHeadName { get; set; } = string.Empty;
    public string ModuleDisplayName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool Status { get; set; }
}

public class GetModuleSettingsRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}

public class ModuleSettingsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ModuleSettingsRow> Data { get; set; } = new();
}

public class SaveModuleSettingsRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public List<ModuleSettingsSaveItem> Modules { get; set; } = new();
}

public class ModuleSettingsSaveItem
{
    public string ModuleName { get; set; } = string.Empty;
    public bool Status { get; set; }
}

public class SaveModuleSettingsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Inserted { get; set; }
    public int Deleted { get; set; }
}

// ─── Copy Modules ───
public class ClientDropdownItem
{
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyUserID { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
}

public class ClientDropdownResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ClientDropdownItem> Data { get; set; } = new();
}

public class CopyModulesRequest
{
    public string SourceConnectionString { get; set; } = string.Empty;
    public string TargetCompanyUserID { get; set; } = string.Empty;
}

public class CopyModulesResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CopiedCount { get; set; }
}

// ─── Module Group Authority ───
public class ModuleGroupRow
{
    public string ModuleGroupName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
}

public class ModuleGroupDropdownResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Data { get; set; } = new();
}

public class ModuleGroupModulesRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ModuleGroupName { get; set; } = string.Empty;
}

public class ModuleGroupModuleRow
{
    public string ModuleHeadName { get; set; } = string.Empty;
    public string ModuleDisplayName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
}

public class ModuleGroupModulesResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ModuleGroupModuleRow> Data { get; set; } = new();
}

public class CreateModuleGroupRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ModuleGroupName { get; set; } = string.Empty;
    public List<string> SelectedModuleNames { get; set; } = new();
}

public class CreateModuleGroupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UpdateModuleGroupRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ModuleGroupName { get; set; } = string.Empty;
    public List<string> SelectedModuleNames { get; set; } = new();
}

public class UpdateModuleGroupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Inserted { get; set; }
    public int Deleted { get; set; }
}

public class ApplyModuleGroupToClientRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ModuleGroupName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}

public class ApplyModuleGroupToClientResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalModules { get; set; }
}

public class CheckModulesExistResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool HasModules { get; set; }
    public int ModuleCount { get; set; }
}

public class DeleteModuleGroupRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ModuleGroupName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class DeleteModuleGroupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DeletedCount { get; set; }
}

public class DeleteCompanySubscriptionRequest
{
    public string CompanyUserID { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyUniqueCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class DeleteCompanySubscriptionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
