namespace Indus360.Api.Models;

/// <summary>One login session — who signed in, when, how long they stayed. Duration is tracked via a
/// client heartbeat (LastSeen), so an abandoned browser still yields an accurate "used for X minutes".</summary>
public sealed class LoginSessionRow
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime LoginAt { get; set; }         // UTC
    public DateTime LastSeen { get; set; }        // UTC — bumped by heartbeat
    public DateTime? LogoutAt { get; set; }       // UTC — set on logout / timeout
    public int? DurationMin { get; set; }         // minutes used (LogoutAt|LastSeen − LoginAt)
    public string? EndReason { get; set; }        // Logout | Timed out | Superseded | (null = Active)
    public bool Active { get; set; }
    public string? IpAddress { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? Device { get; set; }
    public string? DeviceId { get; set; }         // persistent per-browser device id
}

/// <summary>Filters for the Sessions list.</summary>
public sealed class SessionQuery
{
    public int? UserId { get; set; }
    public bool? ActiveOnly { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
