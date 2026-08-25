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
        return await db.ExecuteScalarAsync<int>(sql, new
        {
            req.ClientCode,
            DocType = docType,
            req.HtmlContent,
            req.SavedByUserId,
            req.SavedByName,
        });
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
}
