namespace Indus360.Api.Models;

/// <summary>
/// Subscription "Exceed Days" — an extension granted on top of the Payment Due date.
/// Stored in OUR local app DB (app.ClientSubscriptionExceed), NOT the shared production
/// control DB, so it never touches what the ERP products read. Payment Due stays untouched;
/// the UI shows Exceed Date = Payment Due + ExceedDays separately.
/// </summary>
public sealed class ExceedEntry
{
    public bool Active { get; set; }
    public int Days { get; set; }
}

/// <summary>Save payload for a client's ERP and/or Cloud exceed values (whole-client save).</summary>
public sealed class ClientExceedSaveRequest
{
    public ExceedEntry? Erp { get; set; }
    public ExceedEntry? Cloud { get; set; }
}

/// <summary>One audit row: how many times / when / by whom the exceed days changed.</summary>
public sealed class ExceedHistoryRow
{
    public string Kind { get; set; } = "";
    public int OldDays { get; set; }
    public int NewDays { get; set; }
    public int? ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public DateTime ChangedDate { get; set; }
}

/// <summary>Current exceed state for a client + the change history (newest first).</summary>
public sealed class ClientExceedResponse
{
    public ExceedEntry Erp { get; set; } = new();
    public ExceedEntry Cloud { get; set; } = new();
    public List<ExceedHistoryRow> History { get; set; } = new();
}
