namespace Indus360.Api.Models;

// ── Client documents (Kick-Off / Sign-Off finalized HTML) ──
// Stored in app.ClientDocuments — one latest version per (ClientCode, DocType).

/// <summary>The saved finalized document (full self-contained HTML) for view/download/re-edit.</summary>
public sealed class ClientDocumentDto
{
    public int DocId { get; set; }
    public string ClientCode { get; set; } = "";
    public string DocType { get; set; } = "";       // 'KickOff' | 'SignOff'
    public string HtmlContent { get; set; } = "";
    public int? SavedByUserId { get; set; }
    public string? SavedByName { get; set; }
    public DateTime SavedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Lightweight status row (no HTML) so the tab can show "Saved on … by …" cheaply.</summary>
public sealed class ClientDocumentMeta
{
    public string DocType { get; set; } = "";
    public int? SavedByUserId { get; set; }
    public string? SavedByName { get; set; }
    public DateTime SavedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Save (upsert) the finalized document for a client + doc type.</summary>
public sealed class SaveClientDocumentRequest
{
    public string ClientCode { get; set; } = "";
    public string DocType { get; set; } = "";
    public string HtmlContent { get; set; } = "";
    public int? SavedByUserId { get; set; }
    public string? SavedByName { get; set; }
}

/// <summary>Render arbitrary (current, possibly-unsaved) document HTML to a clean PDF.</summary>
public sealed class RenderPdfRequest
{
    public string HtmlContent { get; set; } = "";
}
