using System.Data;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// CRUD for reusable email templates (app.EmailTemplates, app DB). Subject/Body carry
/// {{placeholder}} tokens; the frontend derives the variable list from them. Templates can
/// also carry file attachments (app.EmailTemplateAttachments) that auto-load into the composer
/// when the template is applied. Managed from the /email → Templates UI.
/// </summary>
public sealed class EmailTemplateRepository
{
    private readonly Db _db;
    public EmailTemplateRepository(Db db) => _db = db;

    public async Task<IEnumerable<EmailTemplateDto>> ListAsync()
    {
        await using var c = await _db.OpenAsync();
        var templates = (await c.QueryAsync<EmailTemplateDto>(
            "SELECT Id, Name, Subject, Body, Category, IsActive FROM app.EmailTemplates WHERE ISNULL(IsActive,1) = 1 AND ISNULL(IsDeletedTransaction,0) = 0 ORDER BY Name")).ToList();

        if (templates.Count == 0) return templates;

        // Attachment METADATA only (no base64 Content) so the list stays light.
        var ids = templates.Select(t => t.Id).ToArray();
        var meta = await c.QueryAsync<(int TemplateId, int Id, string Filename, string? ContentType, long? Size)>(
            "SELECT TemplateId, Id, Filename, ContentType, Size FROM app.EmailTemplateAttachments WHERE TemplateId IN @ids ORDER BY Id",
            new { ids });
        var byTemplate = meta.GroupBy(m => m.TemplateId)
            .ToDictionary(g => g.Key, g => g.Select(m => new EmailTemplateAttachmentDto
            { Id = m.Id, Filename = m.Filename, ContentType = m.ContentType, Size = m.Size }).ToList());
        foreach (var t in templates)
            if (byTemplate.TryGetValue(t.Id, out var atts)) t.Attachments = atts;

        return templates;
    }

    /// <summary>Full attachments (WITH base64 content) for one template — used to auto-attach on compose.</summary>
    public async Task<IEnumerable<EmailTemplateAttachmentDto>> GetAttachmentsAsync(int templateId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<EmailTemplateAttachmentDto>(
            "SELECT Id, Filename, ContentType, Size, Content FROM app.EmailTemplateAttachments WHERE TemplateId=@templateId ORDER BY Id",
            new { templateId });
    }

    public async Task<int> CreateAsync(EmailTemplateSaveRequest r, int? actingUserId)
    {
        await using var c = await _db.OpenAsync();
        var id = await c.ExecuteScalarAsync<int>(@"
            INSERT INTO app.EmailTemplates (Name, Subject, Body, Category, IsActive, CreatedAt, UpdatedAt, CreatedBy, CreatedDate)
            OUTPUT INSERTED.Id
            VALUES (@Name, @Subject, @Body, @Category, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), @CreatedBy, SYSDATETIME())",
            new { r.Name, r.Subject, r.Body, r.Category, CreatedBy = actingUserId });
        await ReconcileAttachmentsAsync(c, id, r.Attachments);
        return id;
    }

    public async Task<int> UpdateAsync(int id, EmailTemplateSaveRequest r, int? actingUserId)
    {
        await using var c = await _db.OpenAsync();
        var n = await c.ExecuteAsync(@"
            UPDATE app.EmailTemplates
            SET Name=@Name, Subject=@Subject, Body=@Body, Category=@Category, UpdatedAt=SYSUTCDATETIME(),
                ModifiedBy=@ModifiedBy, ModifiedDate=SYSDATETIME()
            WHERE Id=@id",
            new { r.Name, r.Subject, r.Body, r.Category, id, ModifiedBy = actingUserId });
        await ReconcileAttachmentsAsync(c, id, r.Attachments);
        return n;
    }

    public async Task<int> DeleteAsync(int id, int? actingUserId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteAsync("UPDATE app.EmailTemplates SET IsDeletedTransaction=1, DeletedBy=@actingUserId, DeletedDate=SYSDATETIME() WHERE Id=@id", new { id, actingUserId });
    }

    /// <summary>
    /// Reconcile a template's attachments against the saved set: keep rows whose Id is present,
    /// delete rows that were removed, and insert new uploads (those carrying base64 Content).
    /// A null list means "leave attachments untouched" (e.g. a save that didn't send them).
    /// </summary>
    private static async Task ReconcileAttachmentsAsync(IDbConnection c, int templateId, List<EmailTemplateAttachmentSave>? items)
    {
        if (items == null) return;

        var keepIds = items.Where(i => i.Id is > 0).Select(i => i.Id!.Value).ToArray();
        if (keepIds.Length == 0)
            await c.ExecuteAsync("DELETE FROM app.EmailTemplateAttachments WHERE TemplateId=@templateId", new { templateId });
        else
            await c.ExecuteAsync("DELETE FROM app.EmailTemplateAttachments WHERE TemplateId=@templateId AND Id NOT IN @keepIds",
                new { templateId, keepIds });

        foreach (var a in items)
        {
            if (a.Id is > 0) continue;                       // existing kept row
            if (string.IsNullOrWhiteSpace(a.Content)) continue; // new upload must carry content
            await c.ExecuteAsync(@"
                INSERT INTO app.EmailTemplateAttachments (TemplateId, Filename, ContentType, Content, Size, CreatedAt)
                VALUES (@templateId, @Filename, @ContentType, @Content, @Size, SYSUTCDATETIME())",
                new { templateId, Filename = string.IsNullOrWhiteSpace(a.Filename) ? "attachment" : a.Filename, a.ContentType, a.Content, a.Size });
        }
    }
}
