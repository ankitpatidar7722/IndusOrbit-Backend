using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// The central activity/audit trail (app.AuditLog). Writes are best-effort — an audit failure must
/// never break the request that triggered it. Reads power the Audit Logs screen (filtered + paged).
/// Table is created on first use (publish-only deploy needs no manual SQL).
/// </summary>
public sealed class AuditLogRepository
{
    private readonly Db _db;
    public AuditLogRepository(Db db) => _db = db;

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
IF OBJECT_ID('app.AuditLog','U') IS NULL
BEGIN
    CREATE TABLE app.AuditLog (
        Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_AuditLog PRIMARY KEY,
        CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_AuditLog_CreatedAt DEFAULT SYSUTCDATETIME(),
        UserId      INT NULL,
        UserName    NVARCHAR(200) NULL,
        [Action]    NVARCHAR(40) NOT NULL,
        Module      NVARCHAR(120) NULL,
        SubModule   NVARCHAR(120) NULL,
        Client      NVARCHAR(150) NULL,
        EntityType  NVARCHAR(120) NULL,
        EntityId    NVARCHAR(100) NULL,
        Summary     NVARCHAR(500) NULL,
        Changes     NVARCHAR(MAX) NULL,
        HttpMethod  NVARCHAR(10) NULL,
        [Path]      NVARCHAR(500) NULL,
        StatusCode  INT NULL,
        Success     BIT NULL,
        DurationMs  INT NULL,
        IpAddress   NVARCHAR(60) NULL,
        Browser     NVARCHAR(80) NULL,
        Os          NVARCHAR(80) NULL,
        Device      NVARCHAR(40) NULL,
        DeviceId    NVARCHAR(80) NULL,
        Fingerprint NVARCHAR(40) NULL,
        UserAgent   NVARCHAR(500) NULL,
        Payload     NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_AuditLog_CreatedAt ON app.AuditLog(CreatedAt DESC);
    CREATE INDEX IX_AuditLog_User      ON app.AuditLog(UserId);
    CREATE INDEX IX_AuditLog_Module    ON app.AuditLog(Module);
    CREATE INDEX IX_AuditLog_Action    ON app.AuditLog([Action]);
END
ELSE
BEGIN
    IF COL_LENGTH('app.AuditLog','SubModule')   IS NULL ALTER TABLE app.AuditLog ADD SubModule   NVARCHAR(120) NULL;
    IF COL_LENGTH('app.AuditLog','Client')      IS NULL ALTER TABLE app.AuditLog ADD Client      NVARCHAR(150) NULL;
    IF COL_LENGTH('app.AuditLog','DeviceId')    IS NULL ALTER TABLE app.AuditLog ADD DeviceId    NVARCHAR(80) NULL;
    IF COL_LENGTH('app.AuditLog','Fingerprint') IS NULL ALTER TABLE app.AuditLog ADD Fingerprint NVARCHAR(40) NULL;
END");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Best-effort insert — swallows all errors so auditing never breaks the real request.</summary>
    public async Task InsertAsync(AuditEntry e)
    {
        try
        {
            await using var c = await _db.OpenAsync();
            await EnsureAsync(c);
            await c.ExecuteAsync(@"
INSERT INTO app.AuditLog
  (CreatedAt, UserId, UserName, [Action], Module, SubModule, Client, EntityType, EntityId, Summary, Changes,
   HttpMethod, [Path], StatusCode, Success, DurationMs, IpAddress, Browser, Os, Device, DeviceId, Fingerprint, UserAgent, Payload)
VALUES
  (@CreatedAt, @UserId, @UserName, @Action, @Module, @SubModule, @Client, @EntityType, @EntityId, @Summary, @Changes,
   @HttpMethod, @Path, @StatusCode, @Success, @DurationMs, @IpAddress, @Browser, @Os, @Device, @DeviceId, @Fingerprint, @UserAgent, @Payload)",
                new
                {
                    e.CreatedAt, e.UserId, e.UserName, e.Action, e.Module, e.SubModule, e.Client, e.EntityType, e.EntityId,
                    Summary = Trim(e.Summary, 500), e.Changes, e.HttpMethod, Path = Trim(e.Path, 500),
                    e.StatusCode, e.Success, e.DurationMs, e.IpAddress, e.Browser, e.Os, e.Device, e.DeviceId, e.Fingerprint,
                    UserAgent = Trim(e.UserAgent, 500), e.Payload,
                });
        }
        catch { /* auditing is best-effort; never surface to the caller */ }
    }

    private static string? Trim(string? s, int max) => s is { Length: > 0 } && s.Length > max ? s[..max] : s;

    /// <summary>Filtered + paged read for the Audit Logs screen. Returns the page rows + the total count.</summary>
    public async Task<(IReadOnlyList<AuditEntry> rows, int total)> QueryAsync(AuditQuery q)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);

        var where = new List<string>();
        var p = new DynamicParameters();
        if (q.UserId is > 0) { where.Add("UserId = @UserId"); p.Add("UserId", q.UserId); }
        if (!string.IsNullOrWhiteSpace(q.Module)) { where.Add("Module = @Module"); p.Add("Module", q.Module.Trim()); }
        if (!string.IsNullOrWhiteSpace(q.Client)) { where.Add("Client LIKE @Client"); p.Add("Client", "%" + q.Client.Trim() + "%"); }
        if (!string.IsNullOrWhiteSpace(q.Action)) { where.Add("[Action] = @Action"); p.Add("Action", q.Action.Trim()); }
        if (q.FromUtc is not null) { where.Add("CreatedAt >= @FromUtc"); p.Add("FromUtc", q.FromUtc); }
        if (q.ToUtc is not null) { where.Add("CreatedAt <= @ToUtc"); p.Add("ToUtc", q.ToUtc); }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            where.Add("(Summary LIKE @kw OR EntityId LIKE @kw OR UserName LIKE @kw OR [Path] LIKE @kw OR Module LIKE @kw)");
            p.Add("kw", "%" + q.Search.Trim() + "%");
        }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var total = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM app.AuditLog {whereSql}", p);

        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);
        p.Add("skip", (page - 1) * size);
        p.Add("take", size);
        var rows = (await c.QueryAsync<AuditEntry>($@"
SELECT Id, CreatedAt, UserId, UserName, [Action], Module, SubModule, Client, EntityType, EntityId, Summary, Changes,
       HttpMethod, [Path] AS Path, StatusCode, Success, DurationMs, IpAddress, Browser, Os, Device, DeviceId, Fingerprint, UserAgent, Payload
FROM app.AuditLog {whereSql}
ORDER BY Id DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", p)).AsList();

        return (rows, total);
    }

    /// <summary>Distinct values for the filter dropdowns (modules, actions, users).</summary>
    public async Task<(List<string> modules, List<string> actions, List<(int id, string name)> users)> FacetsAsync()
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var modules = (await c.QueryAsync<string>("SELECT DISTINCT Module FROM app.AuditLog WHERE Module IS NOT NULL ORDER BY Module")).AsList();
        var actions = (await c.QueryAsync<string>("SELECT DISTINCT [Action] FROM app.AuditLog WHERE [Action] IS NOT NULL ORDER BY [Action]")).AsList();
        var users = (await c.QueryAsync<(int id, string name)>(
            "SELECT DISTINCT UserId AS id, UserName AS name FROM app.AuditLog WHERE UserId IS NOT NULL AND UserName IS NOT NULL ORDER BY UserName")).AsList();
        return (modules, actions, users);
    }
}
