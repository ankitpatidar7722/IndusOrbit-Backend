using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Login-session tracking (app.LoginSession) — opens a session at login, keeps it alive via a client
/// heartbeat (LastSeen), and closes it on logout OR when it goes idle (sweep). Emits matching Login /
/// Logout rows into the audit trail (incl. session duration). One active session per user.
/// Best-effort — a tracking failure never breaks login/logout.
/// </summary>
public sealed class LoginSessionRepository
{
    private readonly Db _db;
    private readonly AuditLogRepository _audit;
    public LoginSessionRepository(Db db, AuditLogRepository audit) { _db = db; _audit = audit; }

    public const int IdleMinutes = 5;   // no heartbeat for this long → session considered ended

    private static bool _ready;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private async Task EnsureAsync(Microsoft.Data.SqlClient.SqlConnection c)
    {
        if (_ready) return;
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            await c.ExecuteAsync(@"
IF OBJECT_ID('app.LoginSession','U') IS NULL
BEGIN
    CREATE TABLE app.LoginSession (
        Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_LoginSession PRIMARY KEY,
        UserId      INT NULL,
        UserName    NVARCHAR(200) NULL,
        LoginAt     DATETIME2 NOT NULL CONSTRAINT DF_LoginSession_LoginAt DEFAULT SYSUTCDATETIME(),
        LastSeen    DATETIME2 NOT NULL CONSTRAINT DF_LoginSession_LastSeen DEFAULT SYSUTCDATETIME(),
        LogoutAt    DATETIME2 NULL,
        DurationMin INT NULL,
        EndReason   NVARCHAR(40) NULL,
        Active      BIT NOT NULL CONSTRAINT DF_LoginSession_Active DEFAULT 1,
        IpAddress   NVARCHAR(60) NULL,
        Browser     NVARCHAR(80) NULL,
        Os          NVARCHAR(80) NULL,
        Device      NVARCHAR(40) NULL,
        DeviceId    NVARCHAR(80) NULL
    );
    CREATE INDEX IX_LoginSession_User   ON app.LoginSession(UserId);
    CREATE INDEX IX_LoginSession_Active ON app.LoginSession(Active);
    CREATE INDEX IX_LoginSession_Login  ON app.LoginSession(LoginAt DESC);
END
ELSE IF COL_LENGTH('app.LoginSession','DeviceId') IS NULL
    ALTER TABLE app.LoginSession ADD DeviceId NVARCHAR(80) NULL;");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Open a fresh session at login. Any prior active session for the user is closed as
    /// "Superseded". Also writes a "Logged in" audit entry.</summary>
    public async Task OpenAsync(int userId, string? userName, string? ip, string? ua)
    {
        try
        {
            var (browser, os, device) = Services.UserAgentParser.Parse(ua);
            await using var c = await _db.OpenAsync();
            await EnsureAsync(c);
            // close any dangling active session for this user
            await c.ExecuteAsync(@"
UPDATE app.LoginSession
   SET Active = 0, LogoutAt = SYSUTCDATETIME(), EndReason = 'Superseded',
       DurationMin = DATEDIFF(MINUTE, LoginAt, SYSUTCDATETIME())
 WHERE UserId = @userId AND Active = 1", new { userId });
            await c.ExecuteAsync(@"
INSERT INTO app.LoginSession (UserId, UserName, LoginAt, LastSeen, Active, IpAddress, Browser, Os, Device)
VALUES (@userId, @userName, SYSUTCDATETIME(), SYSUTCDATETIME(), 1, @ip, @browser, @os, @device)",
                new { userId, userName, ip, browser, os, device });

            await _audit.InsertAsync(new AuditEntry
            {
                CreatedAt = DateTime.UtcNow, UserId = userId, UserName = userName, Action = "Login",
                Module = "Authentication", Summary = "Logged in", Success = true,
                IpAddress = ip, Browser = browser, Os = os, Device = device, UserAgent = ua,
            });
        }
        catch { }
    }

    /// <summary>Keep the user's active session alive (heartbeat). Also stamps the originating device id
    /// (the login request itself is server-side via next-auth, so the device is learned here).</summary>
    public async Task HeartbeatAsync(int userId, string? deviceId = null)
    {
        try
        {
            await using var c = await _db.OpenAsync();
            await EnsureAsync(c);
            await c.ExecuteAsync(
                "UPDATE app.LoginSession SET LastSeen = SYSUTCDATETIME(), DeviceId = COALESCE(@deviceId, DeviceId) WHERE UserId = @userId AND Active = 1",
                new { userId, deviceId });
        }
        catch { }
    }

    private sealed class SessionClose { public int DurationMin { get; set; } public string? UserName { get; set; } }

    /// <summary>Close the user's active session (explicit logout) + write a "Logged out · N min" audit entry.</summary>
    public async Task CloseAsync(int userId, string reason = "Logout")
    {
        try
        {
            await using var c = await _db.OpenAsync();
            await EnsureAsync(c);
            var closed = (await c.QueryAsync<SessionClose>(@"
UPDATE app.LoginSession
   SET Active = 0, LogoutAt = SYSUTCDATETIME(), EndReason = @reason,
       DurationMin = DATEDIFF(MINUTE, LoginAt, SYSUTCDATETIME())
 OUTPUT INSERTED.DurationMin AS DurationMin, INSERTED.UserName AS UserName
 WHERE UserId = @userId AND Active = 1", new { userId, reason })).FirstOrDefault();
            if (closed is null) return;   // nothing was active → no audit entry

            await _audit.InsertAsync(new AuditEntry
            {
                CreatedAt = DateTime.UtcNow, UserId = userId, UserName = closed.UserName, Action = "Logout",
                Module = "Authentication", Summary = $"Logged out · session {FormatDur(closed.DurationMin)}", Success = true,
            });
        }
        catch { }
    }

    /// <summary>Auto-close sessions with no heartbeat for &gt; IdleMinutes (duration counted to LastSeen).</summary>
    public async Task SweepStaleAsync()
    {
        try
        {
            await using var c = await _db.OpenAsync();
            await EnsureAsync(c);
            await c.ExecuteAsync($@"
UPDATE app.LoginSession
   SET Active = 0, LogoutAt = LastSeen, EndReason = 'Timed out',
       DurationMin = DATEDIFF(MINUTE, LoginAt, LastSeen)
 WHERE Active = 1 AND LastSeen < DATEADD(MINUTE, -{IdleMinutes}, SYSUTCDATETIME())");
        }
        catch { }
    }

    /// <summary>Sessions list (newest first), filtered + paged. Sweeps stale sessions first.</summary>
    public async Task<(IReadOnlyList<LoginSessionRow> rows, int total)> ListAsync(SessionQuery q)
    {
        await SweepStaleAsync();
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);

        var where = new List<string>();
        var p = new DynamicParameters();
        if (q.UserId is > 0) { where.Add("UserId = @UserId"); p.Add("UserId", q.UserId); }
        if (q.ActiveOnly == true) where.Add("Active = 1");
        if (q.FromUtc is not null) { where.Add("LoginAt >= @FromUtc"); p.Add("FromUtc", q.FromUtc); }
        if (q.ToUtc is not null) { where.Add("LoginAt <= @ToUtc"); p.Add("ToUtc", q.ToUtc); }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var total = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM app.LoginSession {whereSql}", p);
        var size = Math.Clamp(q.PageSize, 1, 200);
        p.Add("skip", (Math.Max(1, q.Page) - 1) * size);
        p.Add("take", size);
        var rows = (await c.QueryAsync<LoginSessionRow>($@"
SELECT Id, UserId, UserName, LoginAt, LastSeen, LogoutAt,
       -- for still-active rows show the live duration (now − LoginAt)
       CASE WHEN Active = 1 THEN DATEDIFF(MINUTE, LoginAt, SYSUTCDATETIME()) ELSE DurationMin END AS DurationMin,
       EndReason, Active, IpAddress, Browser, Os, Device, DeviceId
FROM app.LoginSession {whereSql}
ORDER BY Id DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", p)).AsList();

        return (rows, total);
    }

    private static string FormatDur(int? min)
    {
        var m = min ?? 0;
        if (m < 60) return $"{m} min";
        return $"{m / 60}h {m % 60}m";
    }
}
