using Dapper;
using Indus360.Api.Data;

namespace Indus360.Api.Repositories;

/// <summary>One file attached to a Tracker row (e.g. the client-signed Training Sheet PDF / photo).</summary>
public sealed class TrackerAttachmentRow
{
    public long Id { get; set; }
    public string OriginalName { get; set; } = "";
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedByName { get; set; }
}

public sealed class TrackerAttachmentFile
{
    public string OriginalName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public string? ContentType { get; set; }
}

/// <summary>
/// Files attached to Tracker rows (app.TrackerAttachments) — keyed by ClientCode + Entity (training, …)
/// + RowId. The bytes live on disk under the uploads root; this table holds the metadata. Created on
/// first use, so a publish-only deploy needs no manual SQL. Deletes are soft (IsDeletedTransaction=1).
/// </summary>
public sealed class TrackerAttachmentRepository
{
    private readonly Db _db;
    public TrackerAttachmentRepository(Db db) => _db = db;

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
IF OBJECT_ID('app.TrackerAttachments','U') IS NULL
BEGIN
    CREATE TABLE app.TrackerAttachments (
        Id                   BIGINT IDENTITY(1,1) CONSTRAINT PK_TrackerAttachments PRIMARY KEY,
        ClientCode           NVARCHAR(100) NOT NULL,
        Entity               NVARCHAR(30)  NOT NULL,
        RowId                INT           NOT NULL,
        OriginalName         NVARCHAR(260) NOT NULL,
        StoredPath           NVARCHAR(500) NOT NULL,
        ContentType          NVARCHAR(120) NULL,
        SizeBytes            BIGINT        NOT NULL CONSTRAINT DF_TrackerAttachments_Size DEFAULT 0,
        UploadedBy           INT           NULL,
        UploadedAt           DATETIME      NOT NULL CONSTRAINT DF_TrackerAttachments_At DEFAULT (CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS datetime)),
        IsDeletedTransaction BIT           NOT NULL CONSTRAINT DF_TrackerAttachments_Del DEFAULT 0,
        DeletedBy            INT           NULL,
        DeletedDate          DATETIME      NULL
    );
    CREATE INDEX IX_TrackerAttachments_Row ON app.TrackerAttachments(ClientCode, Entity, RowId);
END");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<List<TrackerAttachmentRow>> ListAsync(string clientCode, string entity, int rowId)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        return (await c.QueryAsync<TrackerAttachmentRow>(@"
            SELECT a.Id, a.OriginalName, a.ContentType, a.SizeBytes, a.UploadedAt, u.FullName AS UploadedByName
            FROM app.TrackerAttachments a
            LEFT JOIN app.Users u ON u.UserId = a.UploadedBy
            WHERE a.ClientCode=@clientCode AND a.Entity=@entity AND a.RowId=@rowId AND a.IsDeletedTransaction=0
            ORDER BY a.Id", new { clientCode, entity, rowId })).ToList();
    }

    public async Task<long> InsertAsync(string clientCode, string entity, int rowId, string originalName, string storedPath, string? contentType, long size, int? userId)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        return await c.ExecuteScalarAsync<long>(@"
            INSERT INTO app.TrackerAttachments (ClientCode, Entity, RowId, OriginalName, StoredPath, ContentType, SizeBytes, UploadedBy)
            OUTPUT INSERTED.Id
            VALUES (@clientCode, @entity, @rowId, @originalName, @storedPath, @contentType, @size, @userId)",
            new { clientCode, entity, rowId, originalName, storedPath, contentType, size, userId });
    }

    public async Task<TrackerAttachmentFile?> GetFileAsync(string clientCode, long id)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        return await c.QuerySingleOrDefaultAsync<TrackerAttachmentFile>(@"
            SELECT OriginalName, StoredPath, ContentType FROM app.TrackerAttachments
            WHERE Id=@id AND ClientCode=@clientCode AND IsDeletedTransaction=0", new { id, clientCode });
    }

    /// <summary>Soft delete — the file stays on disk so the record can be restored if needed.</summary>
    public async Task<bool> DeleteAsync(string clientCode, long id, int? userId)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        return await c.ExecuteAsync(@"
            UPDATE app.TrackerAttachments SET IsDeletedTransaction=1, DeletedBy=@userId,
                DeletedDate=CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS datetime)
            WHERE Id=@id AND ClientCode=@clientCode AND IsDeletedTransaction=0", new { id, clientCode, userId }) > 0;
    }
}
