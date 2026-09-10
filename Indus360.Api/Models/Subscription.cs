namespace Indus360.Api.Models;

/// <summary>
/// A client subscription card (safe subset for the Customers list — no password).
/// Mirrors dbo.Indus_Company_Authentication_For_Web_Modules in the Indus control DB.
/// </summary>
public sealed class SubscriptionCard
{
    public string CompanyUserID { get; set; } = "";
    public string? CompanyUniqueCode { get; set; }
    public string CompanyName { get; set; } = "";
    public string? CompanyCode { get; set; }
    public string? ApplicationName { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? SubscriptionStatus { get; set; }
    public string? StatusDescription { get; set; }
    public string? SubscriptionStatusMessage { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Gstin { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime? PaymentDueDate { get; set; }
    public long? LoginAllowed { get; set; }
    public DateTime? LastLoginDateTime { get; set; }
    public string? CloudSubscriptionStatus { get; set; }
    /// <summary>DB name parsed from the connection string (never the full string) — lets the UI show
    /// "Client (Code) (DatabaseName)" so same-named clients stay distinguishable.</summary>
    public string? DatabaseName { get; set; }
}

/// <summary>Full subscription record (detail / edit) — includes sensitive fields.</summary>
public class SubscriptionDto
{
    public string CompanyUserID { get; set; } = "";
    public string? Password { get; set; }
    public string? Conn_String { get; set; }
    public string CompanyName { get; set; } = "";
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
    public int? UserLimit { get; set; }
    public string? Gstin { get; set; }
    public string? LatestVersion { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public bool? IsMessageActive { get; set; }
    public int? MessageDurationValue { get; set; }
    public string? MessageDurationType { get; set; }
    public string? CloudSubscriptionStatus { get; set; }
    public DateTime? CloudFromDate { get; set; }
    public DateTime? CloudToDate { get; set; }
    public DateTime? CloudPaymentDueDate { get; set; }
    // Subscription period ("1 Month" … "2 Year") — drives the To Date / Payment Due auto-calc; now persisted.
    public string? ErpSubscriptionPeriod { get; set; }
    public string? CloudSubscriptionPeriod { get; set; }
}

/// <summary>Create/update payload — carries OriginalCompanyUserID to support key rename on edit.</summary>
public sealed class SubscriptionSaveRequest : SubscriptionDto
{
    public string? OriginalCompanyUserID { get; set; }
}

/// <summary>Lightweight client entry for the "copy modules" target dropdown.</summary>
public sealed class ClientDropdownItem
{
    public string CompanyName { get; set; } = "";
    public string CompanyUserID { get; set; } = "";
    public string? ApplicationName { get; set; }
}

/// <summary>Predefined ERP message template (dbo.MessageFormatMaster in the Indus control DB).</summary>
public sealed class MessageFormatDto
{
    public long MessageID { get; set; }
    public string MessageTitle { get; set; } = "";
    public string MessageContent { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class MessageFormatSaveRequest
{
    public long? MessageID { get; set; }
    public string MessageTitle { get; set; } = "";
    public string MessageContent { get; set; } = "";
    public bool? IsActive { get; set; }
}

/// <summary>Password-gated soft delete (auth validated against the app's Users).</summary>
public sealed class DeleteSubscriptionRequest
{
    public string CompanyUserID { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? CompanyUniqueCode { get; set; }
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Reason { get; set; }
}
