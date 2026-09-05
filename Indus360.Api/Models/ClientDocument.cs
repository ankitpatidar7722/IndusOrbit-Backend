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

/// <summary>One audit-log entry for a client document: a Save or an Email, with who + when.</summary>
public sealed class ClientDocumentHistoryDto
{
    public long HistoryId { get; set; }
    public string ClientCode { get; set; } = "";
    public string DocType { get; set; } = "";
    public string Action { get; set; } = "";     // 'Saved' | 'Emailed'
    public string? Version { get; set; }          // document version at that moment (e.g. "1.0")
    public int? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Recipient { get; set; }        // email recipient(s), for 'Emailed'
    public DateTime CreatedAt { get; set; }
}

/// <summary>Body for mark-sent: who emailed the finalized document, and to whom.</summary>
public sealed class MarkSentRequest
{
    public int? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Recipient { get; set; }
}
