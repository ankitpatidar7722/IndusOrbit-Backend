namespace Indus360.Api.Models;

/// <summary>An email recipient — mirrors the legacy EmailAddressDto (camelCase JSON).</summary>
public sealed class EmailAddressDto
{
    public string Email { get; set; } = "";
    public string? Name { get; set; }
}

/// <summary>A base64-encoded attachment — mirrors the legacy AttachmentDto.</summary>
public sealed class EmailAttachmentDto
{
    public string Filename { get; set; } = "";
    public string Content { get; set; } = "";     // base64 (no data: prefix)
    public string? ContentType { get; set; }
    public long? Size { get; set; }
}

/// <summary>
/// Send request. Keeps the legacy SendEmailRequest shape (to/cc/bcc/subject/htmlBody/textBody/
/// replyTo/attachments) and adds Client/Project/Issue context so the send can be recorded in
/// EmailHistory and linked to the originating record.
/// </summary>
public sealed class EmailSendRequest
{
    public List<EmailAddressDto> To { get; set; } = new();
    public List<EmailAddressDto>? Cc { get; set; }
    public List<EmailAddressDto>? Bcc { get; set; }
    public string Subject { get; set; } = "";
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public string? ReplyTo { get; set; }
    public List<EmailAttachmentDto>? Attachments { get; set; }

    // context (all optional)
    public string? ClientCode { get; set; }
    public string? ClientName { get; set; }
    public int? PointId { get; set; }
    public int? TicketId { get; set; }
    public string? Module { get; set; }
    public string? SentByEmail { get; set; }
    public string? SentByName { get; set; }
}

/// <summary>Resolved SMTP credentials (from IntegrationConfig 'Gmail.*').</summary>
public sealed class SmtpConfig
{
    public string Server { get; set; } = "";
    public string Port { get; set; } = "587";
    public string User { get; set; } = "";
    public string Pass { get; set; } = "";
    public string UseSsl { get; set; } = "True";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(User);
}

/// <summary>Outcome of a send attempt.</summary>
public sealed class EmailResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Provider { get; set; }
    public static EmailResult Ok(string msg, string provider) => new() { Success = true, Message = msg, Provider = provider };
    public static EmailResult Fail(string msg) => new() { Success = false, Message = msg };
}

/// <summary>A received email — list summary or full detail (IMAP). Mirrors the frontend Email shape.</summary>
public sealed class EmailMessageDto
{
    public string Id { get; set; } = "";              // IMAP UID (per folder)
    public string Folder { get; set; } = "inbox";
    public EmailAddressDto From { get; set; } = new();
    public List<EmailAddressDto> To { get; set; } = new();
    public List<EmailAddressDto> Cc { get; set; } = new();
    public string Subject { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string? BodyHtml { get; set; }             // only on detail fetch
    public string? BodyText { get; set; }             // only on detail fetch
    public DateTime ReceivedAt { get; set; }
    public bool IsRead { get; set; }
    public bool IsStarred { get; set; }
    public bool HasAttachments { get; set; }
    public List<EmailAttachmentMetaDto> Attachments { get; set; } = new();
}

/// <summary>Attachment metadata for a received email (download is a separate endpoint).</summary>
public sealed class EmailAttachmentMetaDto
{
    public string Id { get; set; } = "";
    public string Filename { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>A page of received emails + folder counts.</summary>
public sealed class EmailListResult
{
    public List<EmailMessageDto> Emails { get; set; } = new();
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>PATCH body for updating a received email's flags / location.</summary>
public sealed class EmailStatusUpdate
{
    public string? Email { get; set; }       // acting user (whose mailbox)
    public string Folder { get; set; } = "inbox";
    public bool? IsRead { get; set; }
    public bool? IsStarred { get; set; }
    public bool? IsArchived { get; set; }     // true → move to All Mail / Archive
}

/// <summary>A reusable email template (app.EmailTemplates). Subject/Body use {{placeholder}} syntax.</summary>
public sealed class EmailTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Attachment METADATA only (no base64 content) — content is fetched via templates/{id}/attachments.</summary>
    public List<EmailTemplateAttachmentDto> Attachments { get; set; } = new();
}

/// <summary>One template attachment. Content (base64) is populated only when fetching for compose.</summary>
public sealed class EmailTemplateAttachmentDto
{
    public int Id { get; set; }
    public string Filename { get; set; } = "";
    public string? ContentType { get; set; }
    public long? Size { get; set; }
    public string? Content { get; set; }   // base64 (no data: prefix) — null in list responses
}

/// <summary>One attachment in a template save: existing kept ones carry Id (no Content); new uploads carry Content (no Id).</summary>
public sealed class EmailTemplateAttachmentSave
{
    public int? Id { get; set; }           // present = keep existing row; null = new upload
    public string Filename { get; set; } = "";
    public string? ContentType { get; set; }
    public long? Size { get; set; }
    public string? Content { get; set; }   // base64 — present only for new uploads
}

/// <summary>Create/update payload for an email template.</summary>
public sealed class EmailTemplateSaveRequest
{
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Category { get; set; }
    /// <summary>Full attachment set after edit (kept-by-Id + new-with-Content). Null = leave attachments untouched.</summary>
    public List<EmailTemplateAttachmentSave>? Attachments { get; set; }
}

/// <summary>One row of sent-email history (app.EmailHistory).</summary>
public sealed class EmailHistoryRow
{
    public int Id { get; set; }
    public string? Recipients { get; set; }
    public string? Subject { get; set; }
    public string? ClientCode { get; set; }
    public string? ClientName { get; set; }
    public int? PointId { get; set; }
    public int? TicketId { get; set; }
    public string? Module { get; set; }
    public string? Provider { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AttachmentsJson { get; set; }
    public string? SentByName { get; set; }
    public DateTime SentAt { get; set; }
}
