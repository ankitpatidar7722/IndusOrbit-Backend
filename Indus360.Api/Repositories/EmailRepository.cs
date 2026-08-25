using System.Text.Json;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Email data access: SMTP credentials from the central IntegrationConfig store
/// (control DB, code 'Gmail'), plus sent-email history in the app DB (app.EmailHistory).
/// Mirrors the legacy IntegrationConfig lookup; all SQL is parameterized.
/// </summary>
public sealed class EmailRepository
{
    private readonly Db _db;
    public EmailRepository(Db db) => _db = db;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class UserSmtpRow
    {
        public string? SmtpServer { get; set; }
        public string? SmtpPort { get; set; }
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public bool? SmtpUseSSL { get; set; }
    }

    /// <summary>
    /// Resolve SMTP credentials for a send. Prefers the acting user's own per-user SMTP
    /// (app.Users, matched by email — set on the User Management → Emails tab) so mail goes
    /// out as that user; falls back to the shared company config (IntegrationConfig 'Gmail').
    /// </summary>
    public async Task<SmtpConfig> GetSmtpConfigAsync(string? senderEmail = null, bool perUserOnly = false)
    {
        // 1) per-user SMTP
        if (!string.IsNullOrWhiteSpace(senderEmail))
        {
            await using var app = await _db.OpenAsync();
            var u = await app.QuerySingleOrDefaultAsync<UserSmtpRow>(@"
                SELECT TOP 1 SmtpServer, SmtpPort, SmtpUsername, SmtpPassword, SmtpUseSSL
                FROM app.Users
                WHERE Email = @email
                  AND ISNULL(IsDeletedTransaction,0) = 0
                  AND NULLIF(LTRIM(RTRIM(SmtpServer)),'')   IS NOT NULL
                  AND NULLIF(LTRIM(RTRIM(SmtpUsername)),'') IS NOT NULL
                ORDER BY UserId", new { email = senderEmail });
            if (u is not null && !string.IsNullOrWhiteSpace(u.SmtpServer) && !string.IsNullOrWhiteSpace(u.SmtpUsername))
                return new SmtpConfig
                {
                    Server = u.SmtpServer!,
                    Port = string.IsNullOrWhiteSpace(u.SmtpPort) ? "587" : u.SmtpPort!,
                    User = u.SmtpUsername!,
                    Pass = u.SmtpPassword ?? "",
                    UseSsl = (u.SmtpUseSSL ?? true) ? "True" : "False",
                };
        }

        // Receive is strictly per-user (each user sees only their own mailbox) — no shared fallback.
        if (perUserOnly) return new SmtpConfig();

        // 2) shared company config
        using var c = await _db.OpenControlAsync();
        var rows = (await c.QueryAsync<(string KeyName, string? KeyValue)>(
            "SELECT KeyName, KeyValue FROM IntegrationConfig WHERE IntegrationCode = @code",
            new { code = "Gmail" })).ToList();
        string Get(string k) => rows.FirstOrDefault(r => string.Equals(r.KeyName, k, StringComparison.OrdinalIgnoreCase)).KeyValue ?? "";
        return new SmtpConfig
        {
            Server = Get("SmtpServer"),
            Port = string.IsNullOrWhiteSpace(Get("SmtpPort")) ? "587" : Get("SmtpPort"),
            User = Get("SmtpUser"),
            Pass = Get("SmtpPass"),
            UseSsl = string.IsNullOrWhiteSpace(Get("SmtpUseSSL")) ? "True" : Get("SmtpUseSSL"),
        };
    }

    /// <summary>The acting user's saved HTML email signature (app.Users), or "" if none.</summary>
    public async Task<string> GetSignatureAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";
        await using var app = await _db.OpenAsync();
        return (await app.QuerySingleOrDefaultAsync<string?>(
            "SELECT TOP 1 EmailSignature FROM app.Users WHERE Email = @email AND ISNULL(IsDeletedTransaction,0) = 0 ORDER BY UserId",
            new { email })) ?? "";
    }

    /// <summary>Record a send attempt (success or failure) in app.EmailHistory.</summary>
    public async Task<int> SaveHistoryAsync(EmailSendRequest req, EmailResult result)
    {
        var recipients = string.Join(", ", req.To.Select(FormatAddr));
        var attachmentsMeta = (req.Attachments ?? new()).Select(a => new
        {
            filename = a.Filename,
            contentType = a.ContentType,
            size = a.Size ?? EstimateSize(a.Content),
        });

        const string sql = @"
            INSERT INTO app.EmailHistory
              (Recipients, ToJson, CcJson, BccJson, Subject, BodyHtml, BodyText, AttachmentsJson,
               ClientCode, ClientName, PointId, TicketId, Module, Provider, Status, ErrorMessage, SentByEmail, SentByName, SentAt)
            OUTPUT INSERTED.Id
            VALUES
              (@Recipients, @ToJson, @CcJson, @BccJson, @Subject, @BodyHtml, @BodyText, @AttachmentsJson,
               @ClientCode, @ClientName, @PointId, @TicketId, @Module, @Provider, @Status, @ErrorMessage, @SentByEmail, @SentByName, GETDATE());";

        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, new
        {
            Recipients = recipients,
            ToJson = JsonSerializer.Serialize(req.To, Json),
            CcJson = req.Cc is { Count: > 0 } ? JsonSerializer.Serialize(req.Cc, Json) : null,
            BccJson = req.Bcc is { Count: > 0 } ? JsonSerializer.Serialize(req.Bcc, Json) : null,
            req.Subject,
            BodyHtml = req.HtmlBody,
            BodyText = req.TextBody,
            AttachmentsJson = req.Attachments is { Count: > 0 } ? JsonSerializer.Serialize(attachmentsMeta, Json) : null,
            req.ClientCode,
            req.ClientName,
            req.PointId,
            req.TicketId,
            req.Module,
            result.Provider,
            Status = result.Success ? "Sent" : "Failed",
            ErrorMessage = result.Success ? null : result.Message,
            req.SentByEmail,
            req.SentByName,
        });
    }

    /// <summary>Sent-email history, optionally filtered by client and/or point.</summary>
    public async Task<IEnumerable<EmailHistoryRow>> GetHistoryAsync(string? clientCode, int? pointId, int take = 100)
    {
        await using var db = await _db.OpenAsync();
        return await db.QueryAsync<EmailHistoryRow>($@"
            SELECT TOP (@take) Id, Recipients, Subject, ClientCode, ClientName, PointId, TicketId, Module,
                   Provider, Status, ErrorMessage, AttachmentsJson, SentByName, SentAt
            FROM app.EmailHistory
            WHERE (@clientCode IS NULL OR ClientCode = @clientCode)
              AND (@pointId IS NULL OR PointId = @pointId)
            ORDER BY Id DESC",
            new { take, clientCode, pointId });
    }

    private static string FormatAddr(EmailAddressDto a)
        => string.IsNullOrWhiteSpace(a.Name) ? a.Email : $"{a.Name} <{a.Email}>";

    private static long EstimateSize(string base64)
        => string.IsNullOrEmpty(base64) ? 0 : (long)(base64.Length * 0.75);
}
