namespace Indus360.Api.Models;

// ── Provisioning wizard DTOs (cloned faithfully from BulkImport CompanySubscription) ──
// NOTE: SetupDatabase contains real BACKUP/RESTORE logic for production use. In this
// environment we do NOT execute it against live databases.

public sealed class ServerListResponse
{
    public bool Success { get; set; } = true;
    public List<string> Servers { get; set; } = new();
}

public sealed class BackupDatabaseResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public List<string> Databases { get; set; } = new();
}

public sealed class SetupDatabaseRequest
{
    public string Server { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string? BackupType { get; set; }
    public string ClientName { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string? BackupDatabaseName { get; set; }
}

public sealed class SetupDatabaseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string Server { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string ClientName { get; set; } = "";
}

public sealed class CompanyMasterRequest
{
    public string ConnectionString { get; set; } = "";
    public int CompanyID { get; set; } = 2;
    public string CompanyName { get; set; } = "";
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

public sealed class CompanyMasterResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int CompanyID { get; set; }
}

public sealed class BranchMasterRequest
{
    public string ConnectionString { get; set; } = "";
    public int BranchID { get; set; } = 1;
    public string BranchName { get; set; } = "";
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

public sealed class ProductionUnitRequest
{
    public string ConnectionString { get; set; } = "";
    public string ProductionUnitName { get; set; } = "";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? GSTNo { get; set; }
    public string? Pincode { get; set; }
    public string? Country { get; set; }
    public string? PAN { get; set; }
}

public sealed class CompleteSetupRequest
{
    public string ConnectionString { get; set; } = "";
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string CompanyUserID { get; set; } = "";
}

public sealed class CompleteSetupResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? CompanyUserID { get; set; }
    public string? Password { get; set; }
    public string? UserName { get; set; }
    public string? UserPassword { get; set; }
}

public sealed class SimpleResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
