using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text.RegularExpressions;
using Indus360.Api.Models;

namespace Indus360.Api.Services;

/// <summary>
/// Sends email over SMTP — a faithful re-implementation of the legacy
/// EmailService.SendViaSMTP (Indas Estimo). From address = the SMTP user; body is
/// always HTML; attachments are base64 → MemoryStream; CC/BCC supported. Credentials
/// are supplied by the caller (read from IntegrationConfig), never hard-coded here.
/// </summary>
public sealed class EmailSender
{
    /// <summary>Send one message. Never throws — failures come back as EmailResult.Fail.</summary>
    public async Task<EmailResult> SendAsync(
        SmtpConfig cfg,
        IEnumerable<EmailAddressDto> to,
        IEnumerable<EmailAddressDto>? cc,
        IEnumerable<EmailAddressDto>? bcc,
        string subject,
        string htmlBody,
        IEnumerable<EmailAttachmentDto>? attachments,
        string? fromEmail = null,
        string? fromName = null,
        string? replyTo = null)
    {
        var streams = new List<MemoryStream>();
        try
        {
            // From = the acting (logged-in) user's email when supplied, else the mailbox.
            // The SMTP account (cfg.User) stays the authenticated agent via the Sender header —
            // required by Gmail; it may still rewrite From when the address isn't a verified alias.
            var fromAddr = MakeAddr(!string.IsNullOrWhiteSpace(fromEmail) ? fromEmail! : cfg.User, fromName)
                           ?? new MailAddress(cfg.User);

            // Pull any inline data-URI images (e.g. a signature logo) out into linked
            // resources with a Content-ID — Gmail/Outlook strip base64 data: images, but
            // render cid: inline attachments. Falls back to a plain HTML body when none.
            var (processedHtml, inlineResources) = ExtractInlineImages(htmlBody ?? "", streams);

            using var mm = new MailMessage
            {
                From = fromAddr,
                Subject = subject ?? "",
                Priority = MailPriority.High,
            };
            if (inlineResources.Count > 0)
            {
                var htmlView = AlternateView.CreateAlternateViewFromString(processedHtml, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html);
                foreach (var lr in inlineResources) htmlView.LinkedResources.Add(lr);
                mm.AlternateViews.Add(htmlView);
            }
            else
            {
                mm.Body = htmlBody ?? "";
                mm.IsBodyHtml = true;
            }
            if (!string.Equals(fromAddr.Address, cfg.User, StringComparison.OrdinalIgnoreCase))
                try { mm.Sender = new MailAddress(cfg.User); } catch { /* ignore */ }
            if (!string.IsNullOrWhiteSpace(replyTo))
                try { mm.ReplyToList.Add(new MailAddress(replyTo.Trim())); } catch { /* ignore bad reply-to */ }

            foreach (var a in to)
                if (!string.IsNullOrWhiteSpace(a?.Email)) mm.To.Add(ToAddress(a));
            if (cc != null)
                foreach (var a in cc)
                    if (!string.IsNullOrWhiteSpace(a?.Email)) mm.CC.Add(ToAddress(a));
            if (bcc != null)
                foreach (var a in bcc)
                    if (!string.IsNullOrWhiteSpace(a?.Email)) mm.Bcc.Add(ToAddress(a));

            if (mm.To.Count == 0)
                return EmailResult.Fail("No valid recipients.");

            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    if (att == null || string.IsNullOrWhiteSpace(att.Content)) continue;
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(StripDataUri(att.Content)); }
                    catch { continue; } // skip malformed attachment rather than failing the whole send
                    var ms = new MemoryStream(bytes);
                    streams.Add(ms);
                    mm.Attachments.Add(new Attachment(ms, string.IsNullOrWhiteSpace(att.Filename) ? "attachment" : att.Filename,
                        string.IsNullOrWhiteSpace(att.ContentType) ? "application/octet-stream" : att.ContentType));
                }
            }

            using var smtp = new SmtpClient
            {
                Host = cfg.Server,
                Port = int.TryParse(cfg.Port, out var p) ? p : 587,
                EnableSsl = !bool.TryParse(cfg.UseSsl, out var ssl) || ssl, // default true
                Credentials = new NetworkCredential(cfg.User, cfg.Pass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            await smtp.SendMailAsync(mm);
            return EmailResult.Ok("Email sent successfully via SMTP.", "Gmail/SMTP");
        }
        catch (Exception ex)
        {
            return EmailResult.Fail("SMTP Error: " + (ex.InnerException?.Message ?? ex.Message));
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    private static MailAddress ToAddress(EmailAddressDto a)
        => string.IsNullOrWhiteSpace(a.Name) ? new MailAddress(a.Email.Trim()) : new MailAddress(a.Email.Trim(), a.Name);

    /// <summary>Build a MailAddress, tolerating a null display name and returning null on an invalid address.</summary>
    private static MailAddress? MakeAddr(string email, string? name)
    {
        try { return string.IsNullOrWhiteSpace(name) ? new MailAddress(email.Trim()) : new MailAddress(email.Trim(), name); }
        catch { return null; }
    }

    private static string StripDataUri(string content)
    {
        var idx = content.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? content[(idx + 7)..] : content;
    }

    // Matches an <img> src that is a base64 data URI, e.g. src="data:image/png;base64,AAAA…".
    private static readonly Regex DataImgSrcRe = new(
        @"src\s*=\s*(?<q>[""'])data:(?<mime>image/[a-z0-9.+\-]+);base64,(?<data>[A-Za-z0-9+/=\s]+)\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Replace every inline base64 data-URI image in the HTML with a <c>cid:</c> reference and
    /// return matching <see cref="LinkedResource"/>s (so the image travels as an inline attachment
    /// that Gmail/Outlook actually render). Malformed data URIs are left untouched. The backing
    /// MemoryStreams are added to <paramref name="streams"/> for disposal after the send.
    /// </summary>
    private static (string html, List<LinkedResource> resources) ExtractInlineImages(string html, List<MemoryStream> streams)
    {
        var resources = new List<LinkedResource>();
        if (string.IsNullOrEmpty(html) || html.IndexOf("data:image", StringComparison.OrdinalIgnoreCase) < 0)
            return (html, resources);

        int n = 0;
        var result = DataImgSrcRe.Replace(html, m =>
        {
            var mime = m.Groups["mime"].Value.ToLowerInvariant();
            var b64 = Regex.Replace(m.Groups["data"].Value, @"\s+", "");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch { return m.Value; } // leave malformed data URIs as-is
            if (bytes.Length == 0) return m.Value;

            var ms = new MemoryStream(bytes);
            streams.Add(ms);
            var cid = $"inl{n++}@indus360";
            resources.Add(new LinkedResource(ms, mime)
            {
                ContentId = cid,
                TransferEncoding = TransferEncoding.Base64,
            });
            return $"src=\"cid:{cid}\"";
        });
        return (result, resources);
    }
}
