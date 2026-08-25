using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.IO;
using Indus360.Api.Models;

namespace Indus360.Api.Services;

/// <summary>
/// Per-user IMAP mailbox access (MailKit). Credentials come from the acting user's own
/// SMTP config (app.Users) via EmailRepository.GetSmtpConfigAsync; the IMAP host is derived
/// from the SMTP host (smtp.gmail.com → imap.gmail.com, etc.). One short-lived connection
/// per call — no pooling (Phase 1). Requires IMAP enabled + an App Password on the account.
/// </summary>
public sealed class ImapReader
{
    private static (string host, int port) ImapHost(string smtpServer)
    {
        var s = (smtpServer ?? "").Trim().ToLowerInvariant();
        if (s.Contains("gmail")) return ("imap.gmail.com", 993);
        if (s.Contains("office365") || s.Contains("outlook") || s.Contains("hotmail") || s.Contains("live")) return ("outlook.office365.com", 993);
        if (s.Contains("yahoo")) return ("imap.mail.yahoo.com", 993);
        if (s.StartsWith("smtp.")) return ("imap." + s.Substring(5), 993);
        return string.IsNullOrWhiteSpace(s) ? ("imap.gmail.com", 993) : (s, 993);
    }

    private static async Task<ImapClient> ConnectAsync(SmtpConfig cfg)
    {
        var (host, port) = ImapHost(cfg.Server);
        var client = new ImapClient { Timeout = 60000 };
        await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(cfg.User, cfg.Pass);
        return client;
    }

    // Map a logical folder name to the account's actual (special-use) IMAP folder.
    private static IMailFolder Resolve(ImapClient client, string folder)
    {
        IMailFolder? f = (folder ?? "inbox").ToLowerInvariant() switch
        {
            "sent" => TryGet(client, SpecialFolder.Sent),
            "archive" => TryGet(client, SpecialFolder.All),
            "trash" => TryGet(client, SpecialFolder.Trash),
            "drafts" => TryGet(client, SpecialFolder.Drafts),
            "spam" => TryGet(client, SpecialFolder.Junk),
            _ => client.Inbox,   // inbox + starred both live in INBOX
        };
        return f ?? client.Inbox;
    }
    private static IMailFolder? TryGet(ImapClient client, SpecialFolder sf)
    {
        try { return client.GetFolder(sf); } catch { return null; }
    }

    private static EmailAddressDto First(InternetAddressList? list)
    {
        var m = list?.Mailboxes.FirstOrDefault();
        return m == null ? new EmailAddressDto() : new EmailAddressDto { Email = m.Address ?? "", Name = m.Name };
    }
    private static List<EmailAddressDto> All(InternetAddressList? list)
        => list?.Mailboxes.Select(m => new EmailAddressDto { Email = m.Address ?? "", Name = m.Name }).ToList() ?? new();

    /// <summary>A page of message summaries (newest first) + folder counts.</summary>
    public async Task<EmailListResult> ListAsync(SmtpConfig cfg, string folderName, int page, int limit)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);
        bool starredOnly = string.Equals(folderName, "starred", StringComparison.OrdinalIgnoreCase);

        using var client = await ConnectAsync(cfg);
        try
        {
            var folder = Resolve(client, folderName);
            await folder.OpenAsync(FolderAccess.ReadOnly);

            int total = folder.Count;
            int unread;
            try { unread = (await folder.SearchAsync(SearchQuery.NotSeen)).Count; } catch { unread = 0; }

            var result = new EmailListResult
            {
                TotalCount = total,
                UnreadCount = unread,
                HasMore = !starredOnly && total > page * limit,
            };
            if (total <= 0) return result;

            int endIndex = total - 1 - (page - 1) * limit;   // 0-based, newest side
            if (endIndex < 0) return result;
            int startIndex = Math.Max(0, endIndex - limit + 1);

            var items = MessageSummaryItems.Envelope | MessageSummaryItems.Flags
                      | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure
                      | MessageSummaryItems.PreviewText | MessageSummaryItems.InternalDate;
            var summaries = await folder.FetchAsync(startIndex, endIndex, items);

            var list = summaries.Select(s => Map(s, folderName)).ToList();
            list.Reverse();   // newest first
            if (starredOnly) list = list.Where(e => e.IsStarred).ToList();
            result.Emails = list;
            return result;
        }
        finally { try { await client.DisconnectAsync(true); } catch { /* ignore */ } }
    }

    private static EmailMessageDto Map(IMessageSummary s, string folderName)
    {
        var env = s.Envelope;
        return new EmailMessageDto
        {
            Id = s.UniqueId.Id.ToString(),
            Folder = (folderName ?? "inbox").ToLowerInvariant(),
            From = First(env?.From),
            To = All(env?.To),
            Cc = All(env?.Cc),
            Subject = string.IsNullOrEmpty(env?.Subject) ? "(No Subject)" : env!.Subject!,
            ReceivedAt = (env?.Date ?? s.InternalDate ?? DateTimeOffset.UtcNow).UtcDateTime,
            IsRead = s.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            IsStarred = s.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
            HasAttachments = s.Attachments?.Any() ?? false,
            Snippet = s.PreviewText ?? "",
        };
    }

    /// <summary>Full message (html + text body + attachment metadata).</summary>
    public async Task<EmailMessageDto?> GetAsync(SmtpConfig cfg, string folderName, uint uid)
    {
        using var client = await ConnectAsync(cfg);
        try
        {
            var folder = Resolve(client, folderName);
            await folder.OpenAsync(FolderAccess.ReadOnly);
            var u = new UniqueId(uid);
            var msg = await folder.GetMessageAsync(u);
            var sums = await folder.FetchAsync(new[] { u }, MessageSummaryItems.Flags);
            var flags = sums.FirstOrDefault()?.Flags ?? MessageFlags.None;

            var atts = msg.Attachments.OfType<MimePart>().Select((a, i) => new EmailAttachmentMetaDto
            {
                Id = uid + "-" + i,
                Filename = a.FileName ?? ("attachment-" + i),
                ContentType = a.ContentType?.MimeType ?? "application/octet-stream",
                Size = 0,
            }).ToList();

            return new EmailMessageDto
            {
                Id = uid.ToString(),
                Folder = (folderName ?? "inbox").ToLowerInvariant(),
                From = First(msg.From),
                To = All(msg.To),
                Cc = All(msg.Cc),
                Subject = string.IsNullOrEmpty(msg.Subject) ? "(No Subject)" : msg.Subject,
                BodyHtml = msg.HtmlBody,
                BodyText = msg.TextBody,
                ReceivedAt = msg.Date.UtcDateTime,
                IsRead = flags.HasFlag(MessageFlags.Seen),
                IsStarred = flags.HasFlag(MessageFlags.Flagged),
                HasAttachments = atts.Count > 0,
                Attachments = atts,
            };
        }
        finally { try { await client.DisconnectAsync(true); } catch { /* ignore */ } }
    }

    /// <summary>Set/clear the Seen (read) and/or Flagged (star) flags.</summary>
    public async Task SetFlagsAsync(SmtpConfig cfg, string folderName, uint uid, bool? isRead, bool? isStarred)
    {
        using var client = await ConnectAsync(cfg);
        try
        {
            var folder = Resolve(client, folderName);
            await folder.OpenAsync(FolderAccess.ReadWrite);
            var u = new UniqueId(uid);
            if (isRead.HasValue)
            {
                if (isRead.Value) await folder.AddFlagsAsync(u, MessageFlags.Seen, true);
                else await folder.RemoveFlagsAsync(u, MessageFlags.Seen, true);
            }
            if (isStarred.HasValue)
            {
                if (isStarred.Value) await folder.AddFlagsAsync(u, MessageFlags.Flagged, true);
                else await folder.RemoveFlagsAsync(u, MessageFlags.Flagged, true);
            }
        }
        finally { try { await client.DisconnectAsync(true); } catch { /* ignore */ } }
    }

    /// <summary>Move a message to a special-use folder (Archive = All Mail, Trash = delete).</summary>
    public async Task MoveAsync(SmtpConfig cfg, string folderName, uint uid, SpecialFolder dest)
    {
        using var client = await ConnectAsync(cfg);
        try
        {
            var src = Resolve(client, folderName);
            await src.OpenAsync(FolderAccess.ReadWrite);
            var destFolder = TryGet(client, dest);
            if (destFolder != null) await src.MoveToAsync(new UniqueId(uid), destFolder);
        }
        finally { try { await client.DisconnectAsync(true); } catch { /* ignore */ } }
    }

    /// <summary>Download one attachment (by its 0-based index among the message's file attachments).</summary>
    public async Task<(byte[] bytes, string filename, string contentType)?> DownloadAttachmentAsync(SmtpConfig cfg, string folderName, uint uid, int index)
    {
        using var client = await ConnectAsync(cfg);
        try
        {
            var folder = Resolve(client, folderName);
            await folder.OpenAsync(FolderAccess.ReadOnly);
            var msg = await folder.GetMessageAsync(new UniqueId(uid));
            var parts = msg.Attachments.OfType<MimePart>().ToList();
            if (index < 0 || index >= parts.Count) return null;
            var part = parts[index];
            if (part.Content == null) return null;
            using var ms = new MemoryStream();
            await part.Content.DecodeToAsync(ms);
            return (ms.ToArray(), part.FileName ?? ("attachment-" + index), part.ContentType?.MimeType ?? "application/octet-stream");
        }
        finally { try { await client.DisconnectAsync(true); } catch { /* ignore */ } }
    }
}
