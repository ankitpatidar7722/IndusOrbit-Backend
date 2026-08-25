using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>Point file attachments (ports PointAttachments + InsertAttachment/GetAllAttachmentsForPoint).</summary>
public sealed class AttachmentRepository
{
    private readonly Db _db;
    public AttachmentRepository(Db db) => _db = db;

    public async Task<int> InsertAsync(int pointId, string fileName, string filePath, int uploadedById, string originalName)
    {
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.PointAttachments (PointID, FileName, FilePath, UploadedByID, UploadTimestamp, OriginalFileName)
            OUTPUT INSERTED.AttachmentID
            VALUES (@pointId, @fileName, @filePath, @uploadedById, GETDATE(), @originalName)",
            new { pointId, fileName, filePath, uploadedById, originalName });
    }

    public async Task<IEnumerable<AttachmentRow>> GetForPointAsync(int pointId)
    {
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<AttachmentRow>(@"
            SELECT a.AttachmentID, a.PointID, a.FileName, a.OriginalFileName, a.UploadTimestamp, a.UploadedByID, u.FullName AS UploadedByName
            FROM dbo.PointAttachments a
            LEFT JOIN dbo.Users u ON u.UserID = a.UploadedByID
            WHERE a.PointID = @pointId AND ISNULL(a.IsDeletedTransaction,0) = 0
            ORDER BY a.AttachmentID DESC", new { pointId });
    }

    public async Task<AttachmentFile?> GetFileAsync(int attachmentId)
    {
        await using var db = await _db.OpenTmsAsync();
        return await db.QuerySingleOrDefaultAsync<AttachmentFile>(
            "SELECT FilePath, OriginalFileName FROM dbo.PointAttachments WHERE AttachmentID = @attachmentId AND ISNULL(IsDeletedTransaction,0) = 0", new { attachmentId });
    }

    /// <summary>SOFT delete: marks IsDeletedTransaction=1 and KEEPS the physical file (recoverable).
    /// Returns null so the controller does not remove the file from disk.</summary>
    public async Task<string?> DeleteAsync(int attachmentId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.PointAttachments SET IsDeletedTransaction=1 WHERE AttachmentID = @attachmentId", new { attachmentId });
        return null;
    }
}
