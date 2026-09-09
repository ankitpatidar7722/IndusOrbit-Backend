using System.Data;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Client Kick-Off / Sign-Off finalized documents (app.ClientDocuments, local IndusTaskManagement).
/// One latest version per (ClientCode, DocType) — Save is an upsert. All SQL is parameterized.
/// </summary>
public sealed class ClientDocumentRepository
{
    private readonly Db _db;
    public ClientDocumentRepository(Db db) => _db = db;

    private const string DocTypeKickOff = "KickOff";
    private const string DocTypeSignOff = "SignOff";

    private static string NormalizeType(string? t) =>
        string.Equals(t, DocTypeSignOff, StringComparison.OrdinalIgnoreCase) ? DocTypeSignOff : DocTypeKickOff;

    /// <summary>Upsert the finalized document; returns the row's DocId.</summary>
    public async Task<int> SaveAsync(SaveClientDocumentRequest req)
    {
        var docType = NormalizeType(req.DocType);
        const string sql = @"
            MERGE app.ClientDocuments AS tgt
            USING (SELECT @ClientCode AS ClientCode, @DocType AS DocType) AS src
              ON  tgt.ClientCode = src.ClientCode AND tgt.DocType = src.DocType
            WHEN MATCHED THEN
              UPDATE SET HtmlContent = @HtmlContent,
                         SavedByUserId = @SavedByUserId,
                         SavedByName = @SavedByName,
                         UpdatedAt = SYSDATETIME(),
                         ModifiedBy = @SavedByUserId,
                         ModifiedDate = SYSDATETIME()
            WHEN NOT MATCHED THEN
              INSERT (ClientCode, DocType, HtmlContent, SavedByUserId, SavedByName, SavedAt, CreatedBy, CreatedDate)
              VALUES (@ClientCode, @DocType, @HtmlContent, @SavedByUserId, @SavedByName, SYSDATETIME(), @SavedByUserId, SYSDATETIME())
            OUTPUT INSERTED.DocId;";

        await using var db = await _db.OpenAsync();
        var docId = await db.ExecuteScalarAsync<int>(sql, new
        {
            req.ClientCode,
            DocType = docType,
            req.HtmlContent,
            req.SavedByUserId,
            req.SavedByName,
        });
        // Audit: log the save with the version at this moment.
        var version = await GetVersionStringAsync(db, req.ClientCode, docType);
        await LogEventAsync(db, req.ClientCode, docType, "Saved", version, req.SavedByUserId, req.SavedByName, null);
        return docId;
    }

    /// <summary>Full saved document (with HTML) for view/download/re-edit, or null if none.</summary>
    public async Task<ClientDocumentDto?> GetAsync(string clientCode, string docType)
    {
        await using var db = await _db.OpenAsync();
        return await db.QuerySingleOrDefaultAsync<ClientDocumentDto>(@"
            SELECT DocId, ClientCode, DocType, HtmlContent, SavedByUserId, SavedByName, SavedAt, UpdatedAt
            FROM app.ClientDocuments
            WHERE ClientCode = @clientCode AND DocType = @docType",
            new { clientCode, docType = NormalizeType(docType) });
    }

    /// <summary>All saved-document status rows (no HTML) for a client — drives the tab badges.</summary>
    public async Task<IEnumerable<ClientDocumentMeta>> GetMetaAsync(string clientCode)
    {
        await using var db = await _db.OpenAsync();
        return await db.QueryAsync<ClientDocumentMeta>(@"
            SELECT DocType, SavedByUserId, SavedByName, SavedAt, UpdatedAt
            FROM app.ClientDocuments
            WHERE ClientCode = @clientCode",
            new { clientCode });
    }

    /// <summary>Record that the finalized document was EMAILED to the client: writes an audit-log
    /// entry (who / to whom / version sent) and bumps the send-revision counter. The version shown
    /// on next open is 1.{Revision}, so the first send stays 1.0, the next becomes 1.1, and so on.
    /// No-op bump if the client has no saved row yet (email is only offered once a doc is saved).</summary>
    public async Task<int> MarkSentAsync(string clientCode, string docType, int? actorUserId, string? actorName, string? recipient)
    {
        var dt = NormalizeType(docType);
        await using var db = await _db.OpenAsync();
        // The version being sent is the current 1.{Revision} (logged BEFORE the bump).
        var version = await GetVersionStringAsync(db, clientCode, dt);
        await LogEventAsync(db, clientCode, dt, "Emailed", version, actorUserId, actorName, recipient);
        return await db.ExecuteAsync(
            "UPDATE app.ClientDocuments SET Revision = ISNULL(Revision,0) + 1 WHERE ClientCode = @clientCode AND DocType = @dt",
            new { clientCode, dt });
    }

    /// <summary>Current document version ("1.{Revision}") for a client + doc type. "1.0" if unsaved.</summary>
    public async Task<string> GetVersionAsync(string clientCode, string docType)
    {
        await using var db = await _db.OpenAsync();
        return await GetVersionStringAsync(db, clientCode, NormalizeType(docType));
    }

    /// <summary>Full Save + Email audit trail for a client document, newest first.</summary>
    public async Task<IEnumerable<ClientDocumentHistoryDto>> GetHistoryAsync(string clientCode, string docType)
    {
        await using var db = await _db.OpenAsync();
        await EnsureHistoryTableAsync(db);
        return await db.QueryAsync<ClientDocumentHistoryDto>(@"
            SELECT HistoryId, ClientCode, DocType, Action, Version, ActorUserId, ActorName, Recipient, CreatedAt
            FROM app.ClientDocumentHistory
            WHERE ClientCode = @clientCode AND DocType = @docType
            ORDER BY HistoryId DESC",
            new { clientCode, docType = NormalizeType(docType) });
    }

    // ── history plumbing ──────────────────────────────────────────────────────
    private static async Task<string> GetVersionStringAsync(IDbConnection db, string clientCode, string docType)
    {
        var rev = await db.ExecuteScalarAsync<int?>(
            "SELECT Revision FROM app.ClientDocuments WHERE ClientCode = @clientCode AND DocType = @docType",
            new { clientCode, docType }) ?? 0;
        return $"1.{rev}";
    }

    private static async Task LogEventAsync(IDbConnection db, string clientCode, string docType, string action,
        string? version, int? actorUserId, string? actorName, string? recipient)
    {
        await EnsureHistoryTableAsync(db);
        await db.ExecuteAsync(@"
            INSERT INTO app.ClientDocumentHistory (ClientCode, DocType, Action, Version, ActorUserId, ActorName, Recipient)
            VALUES (@clientCode, @docType, @action, @version, @actorUserId, @actorName, @recipient)",
            new { clientCode, docType, action, version, actorUserId, actorName, recipient });
    }

    // Once the history table is confirmed to exist we skip the check on every subsequent call —
    // over a WAN link to the server DB that IF-OBJECT_ID round-trip added noticeable latency to
    // every document Save. (Process-lifetime cache; a fresh deploy re-checks once.)
    private static volatile bool _historyTableReady;

    /// <summary>Create the audit-log table on first use (idempotent) — no separate migration needed.</summary>
    private static async Task EnsureHistoryTableAsync(IDbConnection db)
    {
        if (_historyTableReady) return;
        await db.ExecuteAsync(@"
            IF OBJECT_ID('app.ClientDocumentHistory') IS NULL
            CREATE TABLE app.ClientDocumentHistory (
                HistoryId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClientDocumentHistory PRIMARY KEY,
                ClientCode  NVARCHAR(100) NOT NULL,
                DocType     NVARCHAR(20)  NOT NULL,
                Action      NVARCHAR(20)  NOT NULL,
                Version     NVARCHAR(20)  NULL,
                ActorUserId INT           NULL,
                ActorName   NVARCHAR(200) NULL,
                Recipient   NVARCHAR(400) NULL,
                CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_ClientDocumentHistory_CreatedAt DEFAULT SYSDATETIME()
            );");
        _historyTableReady = true;
    }
}
