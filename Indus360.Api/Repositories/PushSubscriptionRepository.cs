using System.Security.Cryptography;
using System.Text;
using Dapper;
using Indus360.Api.Data;

namespace Indus360.Api.Repositories;

/// <summary>A stored Web Push subscription (one browser/device endpoint for one user).</summary>
public sealed class StoredPushSubscription
{
    public long Id { get; set; }
    public long UserID { get; set; }
    public int CompanyID { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
}

/// <summary>
/// Web Push subscriptions (app.PushSubscriptions). One row per browser/device endpoint per user;
/// a user can have several (phone + laptop). Endpoints are unique (deduped via a persisted SHA-256
/// hash so the very long push URLs can still be indexed). The table is created on first use so a
/// publish-only deploy needs no manual SQL. See <see cref="AppNotificationRepository"/> for the
/// notification records themselves and Services/WebPushSender for the send side.
/// </summary>
public sealed class PushSubscriptionRepository
{
    private readonly Db _db;
    public PushSubscriptionRepository(Db db) => _db = db;

    private static bool _ready;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>SHA-256 of the (very long) push endpoint — a compact, indexable dedup key.</summary>
    private static byte[] HashOf(string endpoint) => SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));

    private async Task EnsureAsync(Microsoft.Data.SqlClient.SqlConnection c)
    {
        if (_ready) return;
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            // EndpointHash is a PLAIN varbinary(32) filled by the app (SHA-256 in C#), NOT a computed
            // column — so creating the unique index needs no special ANSI SET options (a computed-column
            // index does), which keeps this runnable on any client incl. plain sqlcmd.
            await c.ExecuteAsync(@"
IF OBJECT_ID('app.PushSubscriptions','U') IS NULL
BEGIN
    CREATE TABLE app.PushSubscriptions (
        Id           BIGINT IDENTITY(1,1) CONSTRAINT PK_PushSubscriptions PRIMARY KEY,
        UserID       BIGINT NOT NULL,
        CompanyID    INT NOT NULL CONSTRAINT DF_PushSubscriptions_Company DEFAULT 1,
        Endpoint     NVARCHAR(2048) NOT NULL,
        EndpointHash VARBINARY(32) NOT NULL,
        P256dh       NVARCHAR(300) NOT NULL,
        Auth         NVARCHAR(200) NOT NULL,
        UserAgent    NVARCHAR(400) NULL,
        CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_PushSubscriptions_Created DEFAULT SYSUTCDATETIME(),
        LastSeenAt   DATETIME2 NOT NULL CONSTRAINT DF_PushSubscriptions_Seen DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_PushSubscriptions_EndpointHash ON app.PushSubscriptions(EndpointHash);
    CREATE INDEX IX_PushSubscriptions_UserID ON app.PushSubscriptions(UserID);
END");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Insert-or-update a subscription, keyed by its (unique) endpoint.</summary>
    public async Task SaveAsync(long userId, int companyId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        var hash = HashOf(endpoint);
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        await c.ExecuteAsync(@"
UPDATE app.PushSubscriptions
   SET UserID=@userId, CompanyID=@companyId, P256dh=@p256dh, Auth=@auth, UserAgent=@userAgent, LastSeenAt=SYSUTCDATETIME()
 WHERE EndpointHash = @hash;
IF @@ROWCOUNT = 0
    INSERT INTO app.PushSubscriptions (UserID, CompanyID, Endpoint, EndpointHash, P256dh, Auth, UserAgent)
    VALUES (@userId, @companyId, @endpoint, @hash, @p256dh, @auth, @userAgent);",
            new { userId, companyId, endpoint, hash, p256dh, auth, userAgent });
    }

    /// <summary>All subscriptions for a user (all their devices).</summary>
    public async Task<List<StoredPushSubscription>> ListByUserAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var rows = await c.QueryAsync<StoredPushSubscription>(
            "SELECT Id, UserID, CompanyID, Endpoint, P256dh, Auth FROM app.PushSubscriptions WHERE UserID=@userId",
            new { userId });
        return rows.AsList();
    }

    /// <summary>Remove a subscription by endpoint (user unsubscribed / logged out on that device).</summary>
    public async Task DeleteByEndpointAsync(string endpoint)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        await c.ExecuteAsync(
            "DELETE FROM app.PushSubscriptions WHERE EndpointHash = @hash",
            new { hash = HashOf(endpoint) });
    }

    /// <summary>Remove a dead subscription (the push service returned 404/410 Gone).</summary>
    public async Task DeleteByIdAsync(long id)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("DELETE FROM app.PushSubscriptions WHERE Id=@id", new { id });
    }
}
