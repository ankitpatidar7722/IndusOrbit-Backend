namespace Indus360.Api.Models;

/// <summary>One audit-trail row. Self-contained (user name + UA details denormalized) so the history
/// stays meaningful even if the user is later renamed/removed.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }          // UTC
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "Other";    // Create | Update | Delete | Login | Logout | Export | View | Other
    public string? Module { get; set; }              // top module (Clients / SOP Modules / …)
    public string? SubModule { get; set; }           // the tab / sub-area (e.g. "Tracker · Milestones")
    public string? Client { get; set; }              // client the action targeted ("IA00274 · AnkitTesting")
    public string? EntityType { get; set; }          // the record type acted on (Milestone / Change Request …)
    public string? EntityId { get; set; }            // record key from the route
    public string? Summary { get; set; }             // human-readable one-liner
    public string? Changes { get; set; }             // JSON before/after (field-level diff)
    public string? HttpMethod { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public bool? Success { get; set; }
    public int? DurationMs { get; set; }
    public string? IpAddress { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? Device { get; set; }              // Desktop | Mobile | Tablet | Bot
    public string? DeviceId { get; set; }            // persistent per-browser device id (forensics)
    public string? Fingerprint { get; set; }         // device-traits hash (survives storage wipe)
    public string? UserAgent { get; set; }
    public string? Payload { get; set; }             // sanitized request body snapshot
}

/// <summary>Filters for the audit-log read API.</summary>
public sealed class AuditQuery
{
    public int? UserId { get; set; }
    public string? Module { get; set; }
    public string? Client { get; set; }      // matches the audited client (code or name), LIKE
    public string? Action { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? Search { get; set; }              // matches summary / entity / user / path
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
