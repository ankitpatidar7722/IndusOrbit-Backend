using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Email compose + send + history. Migrated from the legacy Indas Estimo "Email"
/// feature (EmailController + EmailService) — SMTP transport only (the credential
/// store here has no Microsoft Graph rows). Credentials come from IntegrationConfig
/// ('Gmail.*') at runtime; nothing is hard-coded. Every send is recorded in
/// app.EmailHistory (linked to Client/Point/Ticket context when supplied).
/// </summary>
[ApiController]
[Route("api/email")]
public sealed class EmailController : ControllerBase
{
    private readonly EmailRepository _repo;
    private readonly EmailSender _sender;
    private readonly ImapReader _imap;
    private readonly EmailTemplateRepository _templates;
    private readonly AppNotificationRepository _notifRepo;
    private readonly NotificationPusher _pusher;
    public EmailController(EmailRepository repo, EmailSender sender, ImapReader imap, EmailTemplateRepository templates,
        AppNotificationRepository notifRepo, NotificationPusher pusher)
    {
        _repo = repo;
        _sender = sender;
        _imap = imap;
        _templates = templates;
        _notifRepo = notifRepo;
        _pusher = pusher;
    }

    /// <summary>Poll the inbox for new mail and raise Email notifications for anything since the last check.
    /// The frontend calls this periodically so email notifications arrive app-wide (not only on /email).</summary>
    [HttpGet("check-notifications")]
    public async Task<IActionResult> CheckEmailNotifications([FromQuery] string? email)
    {
        var uid = this.CurrentUserId(); if (uid is null) return Ok(new { created = 0 });
        var cid = this.CurrentCompanyId();
        if (!await _notifRepo.WantsEmailsAsync(uid.Value)) return Ok(new { created = 0 });
        var cfg = await _repo.GetSmtpConfigAsync(email, perUserOnly: true);
        if (!cfg.IsConfigured) return Ok(new { created = 0, error = "not-configured" });

        // Only notify about mail newer than the last check (first run: last 10 minutes, to avoid a flood).
        var last = await _notifRepo.GetLastEmailNotifiedAsync(uid.Value) ?? DateTime.UtcNow.AddMinutes(-10);
        var maxSeen = last;
        var created = 0;
        try
        {
            var res = await _imap.ListAsync(cfg, "inbox", 1, 15);
            foreach (var m in res.Emails.OrderBy(x => x.ReceivedAt))
            {
                if (m.ReceivedAt <= last) continue;
                if (m.ReceivedAt > maxSeen) maxSeen = m.ReceivedAt;
                var from = string.IsNullOrWhiteSpace(m.From.Name) ? m.From.Email : m.From.Name!;
                var body = string.IsNullOrWhiteSpace(m.Subject) ? m.Snippet : m.Subject;
                await _pusher.PushAsync(cid, uid.Value, "Email", from, body, "/email", m.Id, EmailInitials(from));
                created++;
            }
            await _notifRepo.SetLastEmailNotifiedAsync(uid.Value, maxSeen);
        }
        catch { /* IMAP hiccup — skip this cycle */ }
        return Ok(new { created });
    }

    private static string EmailInitials(string name)
    {
        var parts = name.Split(new[] { ' ', '@', '.' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    /// <summary>Whether SMTP is configured + the mailbox we send from.</summary>
    private const string FromName = "Indus Analytics";

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig([FromQuery] string? email)
    {
        var cfg = await _repo.GetSmtpConfigAsync(email);
        var signature = await _repo.GetSignatureAsync(email);
        return Ok(new
        {
            configured = cfg.IsConfigured,
            provider = "Gmail/SMTP",
            mailbox = cfg.User,
            fromName = FromName,
            senderDisplay = cfg.IsConfigured ? $"{FromName} <{cfg.User}>" : "",
            smtpConfigured = cfg.IsConfigured,
            graphConfigured = false,
            signature,
        });
    }

    /// <summary>Send an email and record it in history.</summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] EmailSendRequest req)
    {
        if (req.To is null || req.To.Count == 0 || req.To.All(t => string.IsNullOrWhiteSpace(t?.Email)))
            return BadRequest(new { success = false, error = "At least one recipient is required." });
        if (string.IsNullOrWhiteSpace(req.Subject))
            return BadRequest(new { success = false, error = "Subject is required." });

        var cfg = await _repo.GetSmtpConfigAsync(req.SentByEmail);
        if (!cfg.IsConfigured)
            return Ok(new { success = false, error = "Email is not configured (no SMTP credentials found)." });

        // prefer HTML; fall back to text wrapped as HTML
        var html = !string.IsNullOrWhiteSpace(req.HtmlBody) ? req.HtmlBody!
                 : System.Net.WebUtility.HtmlEncode(req.TextBody ?? "").Replace("\n", "<br/>");

        // From = the logged-in user's email (falls back to the mailbox); replies route to the
        // sender (or an explicit reply-to). The SMTP account stays the authenticated agent.
        var replyTo = !string.IsNullOrWhiteSpace(req.ReplyTo) ? req.ReplyTo : req.SentByEmail;
        var fromName = !string.IsNullOrWhiteSpace(req.SentByName) ? req.SentByName : FromName;
        var result = await _sender.SendAsync(cfg, req.To, req.Cc, req.Bcc, req.Subject, html, req.Attachments, req.SentByEmail, fromName, replyTo);

        // record every attempt (success or failure)
        int historyId = 0;
        try { historyId = await _repo.SaveHistoryAsync(req, result); } catch { /* never fail the send on a history error */ }

        return Ok(new
        {
            success = result.Success,
            provider = result.Provider,
            message = result.Message,
            error = result.Success ? null : result.Message,
            historyId,
        });
    }

    /// <summary>Sent-email history, optionally scoped to a client and/or point (issue).</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string? clientCode, [FromQuery] int? pointId, [FromQuery] int take = 100)
        => Ok(await _repo.GetHistoryAsync(clientCode, pointId, Math.Clamp(take, 1, 500)));

    /// <summary>Distinct addresses this user has emailed before (Gmail-style recipient autocomplete).
    /// Scoped to the given sender when <paramref name="forEmail"/> is supplied.</summary>
    [HttpGet("recipients")]
    public async Task<IActionResult> Recipients([FromQuery] string? forEmail)
        => Ok(await _repo.GetRecipientSuggestionsAsync(string.IsNullOrWhiteSpace(forEmail) ? null : forEmail));

    // ─────────────────────────── Templates (app.EmailTemplates) ───────────────────────────

    [HttpGet("templates")]
    public async Task<IActionResult> Templates()
    {
        try { return Ok(new { success = true, data = await _templates.ListAsync() }); }
        catch (Exception ex) { return Ok(new { success = false, error = ex.Message, data = Array.Empty<object>() }); }
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] EmailTemplateSaveRequest r)
    {
        if (string.IsNullOrWhiteSpace(r?.Name)) return BadRequest(new { success = false, error = "Template name is required." });
        var id = await _templates.CreateAsync(r!, this.CurrentUserId());
        return Ok(new { success = true, id });
    }

    [HttpPut("templates/{id:int}")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] EmailTemplateSaveRequest r)
    {
        if (string.IsNullOrWhiteSpace(r?.Name)) return BadRequest(new { success = false, error = "Template name is required." });
        await _templates.UpdateAsync(id, r!, this.CurrentUserId());
        return Ok(new { success = true });
    }

    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        await _templates.DeleteAsync(id, this.CurrentUserId());
        return Ok(new { success = true });
    }

    /// <summary>Full attachments (with base64 content) for a template — used to auto-attach when the composer applies it.</summary>
    [HttpGet("templates/{id:int}/attachments")]
    public async Task<IActionResult> TemplateAttachments(int id)
    {
        try { return Ok(new { success = true, data = await _templates.GetAttachmentsAsync(id) }); }
        catch (Exception ex) { return Ok(new { success = false, error = ex.Message, data = Array.Empty<object>() }); }
    }

    // ─────────────────────────── Receive (IMAP, per-user) ───────────────────────────

    /// <summary>List a page of messages from the acting user's mailbox folder (newest first).</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] string? email, [FromQuery] string folder = "inbox", [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        var cfg = await _repo.GetSmtpConfigAsync(email, perUserOnly: true);
        if (!cfg.IsConfigured)
            return Ok(new { success = false, error = "Your email is not configured. Add SMTP details (with a Gmail App Password) in User Management → Emails." });
        try
        {
            var res = await _imap.ListAsync(cfg, folder, page, limit);
            return Ok(new { success = true, data = res });
        }
        catch (Exception ex) { return Ok(new { success = false, error = Friendly(ex) }); }
    }

    /// <summary>Full message (body + attachment metadata) by IMAP UID.</summary>
    [HttpGet("messages/{uid}")]
    public async Task<IActionResult> Message(string uid, [FromQuery] string? email, [FromQuery] string folder = "inbox")
    {
        if (!uint.TryParse(uid, out var id)) return BadRequest(new { success = false, error = "Invalid message id." });
        var cfg = await _repo.GetSmtpConfigAsync(email, perUserOnly: true);
        if (!cfg.IsConfigured) return Ok(new { success = false, error = "Email not configured." });
        try
        {
            var msg = await _imap.GetAsync(cfg, folder, id);
            return msg is null ? Ok(new { success = false, error = "Message not found." })
                               : Ok(new { success = true, data = msg });
        }
        catch (Exception ex) { return Ok(new { success = false, error = Friendly(ex) }); }
    }

    /// <summary>Update flags (read/star) or archive a message.</summary>
    [HttpPatch("messages/{uid}")]
    public async Task<IActionResult> UpdateMessage(string uid, [FromBody] EmailStatusUpdate body)
    {
        if (!uint.TryParse(uid, out var id)) return BadRequest(new { success = false, error = "Invalid message id." });
        var cfg = await _repo.GetSmtpConfigAsync(body?.Email, perUserOnly: true);
        if (!cfg.IsConfigured) return Ok(new { success = false, error = "Email not configured." });
        var folder = string.IsNullOrWhiteSpace(body?.Folder) ? "inbox" : body!.Folder;
        try
        {
            if (body!.IsArchived == true)
                await _imap.MoveAsync(cfg, folder, id, MailKit.SpecialFolder.All);
            else if (body.IsRead.HasValue || body.IsStarred.HasValue)
                await _imap.SetFlagsAsync(cfg, folder, id, body.IsRead, body.IsStarred);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, error = Friendly(ex) }); }
    }

    /// <summary>Delete a message (move to Trash).</summary>
    [HttpDelete("messages/{uid}")]
    public async Task<IActionResult> DeleteMessage(string uid, [FromQuery] string? email, [FromQuery] string folder = "inbox")
    {
        if (!uint.TryParse(uid, out var id)) return BadRequest(new { success = false, error = "Invalid message id." });
        var cfg = await _repo.GetSmtpConfigAsync(email, perUserOnly: true);
        if (!cfg.IsConfigured) return Ok(new { success = false, error = "Email not configured." });
        try
        {
            await _imap.MoveAsync(cfg, folder, id, MailKit.SpecialFolder.Trash);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, error = Friendly(ex) }); }
    }

    /// <summary>Permanently delete a message (expunge). Used from the Trash folder — not recoverable.</summary>
    [HttpDelete("messages/{uid}/permanent")]
    public async Task<IActionResult> DeleteMessagePermanent(string uid, [FromQuery] string? email, [FromQuery] string folder = "trash")
    {
        if (!uint.TryParse(uid, out var id)) return BadRequest(new { success = false, error = "Invalid message id." });
        var cfg = await _repo.GetSmtpConfigAsync(email, perUserOnly: true);
        if (!cfg.IsConfigured) return Ok(new { success = false, error = "Email not configured." });
        try
        {
            await _imap.DeleteForeverAsync(cfg, folder, id);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, error = Friendly(ex) }); }
    }

    /// <summary>Download an attachment (0-based index among the message's file attachments).</summary>
    [HttpGet("messages/{uid}/attachments/{index}")]
    public async Task<IActionResult> Attachment(string uid, int index, [FromQuery] string? email, [FromQuery] string folder = "inbox")
    {
        if (!uint.TryParse(uid, out var id)) return BadRequest("Invalid message id.");
        var cfg = await _repo.GetSmtpConfigAsync(email, perUserOnly: true);
        if (!cfg.IsConfigured) return BadRequest("Email not configured.");
        try
        {
            var att = await _imap.DownloadAttachmentAsync(cfg, folder, id, index);
            if (att is null) return NotFound("Attachment not found.");
            return File(att.Value.bytes, att.Value.contentType, att.Value.filename);
        }
        catch (Exception ex) { return StatusCode(500, Friendly(ex)); }
    }

    private static string Friendly(Exception ex)
    {
        var m = ex.Message ?? "";
        if (m.Contains("AUTHENTICATE", StringComparison.OrdinalIgnoreCase) || m.Contains("credential", StringComparison.OrdinalIgnoreCase) || m.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
            return "IMAP authentication failed. Ensure IMAP is enabled and use a Gmail App Password (not the normal password).";
        if (m.Contains("timed out", StringComparison.OrdinalIgnoreCase) || m.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Mail server connection timed out. Please try again.";
        return "Could not reach the mailbox: " + m;
    }
}
