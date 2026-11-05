using System.Net;
using System.Security.Cryptography;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Controllers;

/// <summary>
/// "Forgot Password?" on the login page — self-service reset with a one-time code sent by email.
///   1. POST forgot-password {email}: if the email belongs to an ACTIVE user, a 6-digit OTP is emailed to the
///      address saved on that user (never to anything typed in). The answer is always the same generic
///      message, so the screen never reveals which emails are registered.
///   2. POST reset-password {email, otp, newPassword}: checks the OTP and sets the new password (BCrypt).
/// OTPs are stored hashed, expire after 15 min, allow 5 wrong tries, are single-use, and a new one can be
/// requested once a minute. Table app.PasswordResetOtps is created on first use.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class PasswordResetController : ControllerBase
{
    private const int OtpMinutes = 15, MaxAttempts = 5, ResendSeconds = 60, MinPasswordLength = 6;
    private const string Generic = "If this email is registered, an OTP has been sent to it. It is valid for 15 minutes.";
    private const string BadOtp = "This OTP is invalid or has expired. Please request a new one.";

    private readonly Db _db;
    private readonly EmailRepository _emailRepo;
    private readonly EmailSender _sender;
    private readonly ILogger<PasswordResetController> _log;
    public PasswordResetController(Db db, EmailRepository emailRepo, EmailSender sender, ILogger<PasswordResetController> log)
    { _db = db; _emailRepo = emailRepo; _sender = sender; _log = log; }

    public sealed record ForgotRequest(string? Email);
    public sealed record ResetRequest(string? Email, string? Otp, string? NewPassword);

    private sealed class UserRow { public long UserId { get; set; } public string? FullName { get; set; } public string? Email { get; set; } }
    private sealed class OtpRow { public long Id { get; set; } public string OtpHash { get; set; } = ""; public int Attempts { get; set; } }

    private static volatile bool _tableReady;
    private static async Task EnsureTableAsync(SqlConnection c)
    {
        if (_tableReady) return;
        await c.ExecuteAsync(@"
            IF OBJECT_ID('app.PasswordResetOtps') IS NULL
            BEGIN
                CREATE TABLE app.PasswordResetOtps (
                    Id         bigint IDENTITY(1,1) PRIMARY KEY,
                    UserId     bigint        NOT NULL,
                    OtpHash    nvarchar(100) NOT NULL,
                    ExpiresAt  datetime2     NOT NULL,
                    Attempts   int           NOT NULL DEFAULT 0,
                    UsedAt     datetime2     NULL,
                    CreatedAt  datetime2     NOT NULL DEFAULT SYSDATETIME(),
                    RequestIp  nvarchar(64)  NULL
                );
                CREATE INDEX IX_PasswordResetOtps_User ON app.PasswordResetOtps (UserId, CreatedAt DESC);
            END");
        _tableReady = true;
    }

    // Same lookup as login (EmployeeAuthController): trimmed email, not deleted; only ACTIVE users get an OTP.
    private static Task<UserRow?> FindActiveUserAsync(SqlConnection c, string email) =>
        c.QueryFirstOrDefaultAsync<UserRow>(@"
            SELECT TOP 1 UserId, FullName, LTRIM(RTRIM(Email)) AS Email FROM app.Users
            WHERE LTRIM(RTRIM(Email)) = @email AND ISNULL(IsDeletedTransaction,0) = 0 AND ISNULL(IsActive,0) = 1
            ORDER BY UserId", new { email });

    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot([FromBody] ForgotRequest req)
    {
        var email = (req?.Email ?? "").Trim();
        if (email.Length == 0) return BadRequest(new { success = false, message = "Please enter your email." });

        await using var c = await _db.OpenAsync();
        await EnsureTableAsync(c);
        var u = await FindActiveUserAsync(c, email);
        if (u is null) return Ok(new { success = true, message = Generic });

        // One code a minute per user — stops someone flooding a mailbox.
        var recent = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM app.PasswordResetOtps WHERE UserId=@id AND CreatedAt > DATEADD(SECOND, -@s, SYSDATETIME())",
            new { id = u.UserId, s = ResendSeconds });
        if (recent > 0) return Ok(new { success = true, message = Generic });

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var ip = Request.Headers["X-Forwarded-For"].ToString().Split(',').FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // A new code replaces any older unused one.
        await c.ExecuteAsync(@"
            UPDATE app.PasswordResetOtps SET UsedAt = SYSDATETIME() WHERE UserId = @id AND UsedAt IS NULL;
            INSERT INTO app.PasswordResetOtps (UserId, OtpHash, ExpiresAt, RequestIp)
            VALUES (@id, @hash, DATEADD(MINUTE, @m, SYSDATETIME()), @ip);",
            new { id = u.UserId, hash = PasswordHasher.Hash(otp), m = OtpMinutes, ip = UserAgentParser.NormalizeIp(ip) });

        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(u.FullName) ? "there" : u.FullName!);
        var html = $@"<div style=""font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1e293b;max-width:520px"">
  <p>Hello {name},</p>
  <p>We received a request to reset the password of your <b>Indus Command Center</b> account.</p>
  <p>Your one-time code is:</p>
  <p style=""font-size:30px;font-weight:700;letter-spacing:8px;color:#0b4f6c;margin:8px 0 16px"">{otp}</p>
  <p>It is valid for {OtpMinutes} minutes and can be used only once.</p>
  <p style=""color:#64748b"">If you did not ask for this, just ignore this email — your password stays the same.</p>
  <p>Regards,<br/>Indus Analytics</p>
</div>";
        try
        {
            var cfg = await _emailRepo.GetSmtpConfigAsync();   // shared company mailbox (IntegrationConfig 'Gmail')
            var r = await _sender.SendAsync(cfg, new[] { new EmailAddressDto { Email = u.Email!, Name = u.FullName } }, null, null,
                "Your password reset code — Indus Command Center", html, null);
            if (!r.Success) _log.LogWarning("Password-reset OTP email to user {UserId} failed: {Msg}", u.UserId, r.Message);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Password-reset OTP email to user {UserId} failed", u.UserId); }

        return Ok(new { success = true, message = Generic });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset([FromBody] ResetRequest req)
    {
        var email = (req?.Email ?? "").Trim();
        var otp = (req?.Otp ?? "").Trim();
        var pwd = (req?.NewPassword ?? "").Trim();
        if (email.Length == 0 || otp.Length == 0) return BadRequest(new { success = false, message = "Email and OTP are required." });
        if (pwd.Length < MinPasswordLength)
            return BadRequest(new { success = false, message = $"The new password must be at least {MinPasswordLength} characters." });

        await using var c = await _db.OpenAsync();
        await EnsureTableAsync(c);
        var u = await FindActiveUserAsync(c, email);
        if (u is null) return BadRequest(new { success = false, message = BadOtp });

        var row = await c.QueryFirstOrDefaultAsync<OtpRow>(@"
            SELECT TOP 1 Id, OtpHash, Attempts FROM app.PasswordResetOtps
            WHERE UserId = @id AND UsedAt IS NULL AND ExpiresAt > SYSDATETIME()
            ORDER BY CreatedAt DESC", new { id = u.UserId });
        if (row is null || row.Attempts >= MaxAttempts) return BadRequest(new { success = false, message = BadOtp });

        if (!PasswordHasher.Verify(otp, row.OtpHash))
        {
            var left = MaxAttempts - row.Attempts - 1;
            await c.ExecuteAsync(@"UPDATE app.PasswordResetOtps SET Attempts = Attempts + 1,
                                     UsedAt = CASE WHEN Attempts + 1 >= @max THEN SYSDATETIME() ELSE UsedAt END WHERE Id = @rid",
                new { rid = row.Id, max = MaxAttempts });
            return BadRequest(new { success = false, message = left > 0
                ? $"Incorrect OTP. {left} attempt{(left == 1 ? "" : "s")} left."
                : "Incorrect OTP. This code is now blocked — please request a new one." });
        }

        await c.ExecuteAsync(@"
            UPDATE app.Users SET PasswordHash = @h WHERE UserId = @id;
            UPDATE app.PasswordResetOtps SET UsedAt = SYSDATETIME() WHERE UserId = @id AND UsedAt IS NULL;",
            new { h = PasswordHasher.Hash(pwd), id = u.UserId });
        _log.LogInformation("Password reset by OTP for user {UserId}", u.UserId);
        return Ok(new { success = true, message = "Your password has been reset. Please sign in with the new password." });
    }
}
