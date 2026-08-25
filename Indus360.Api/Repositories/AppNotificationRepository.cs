using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Chat + email notifications (app.Notifications) + per-user notification settings
/// (app.Users.NotifyMessages / NotifyEmails). Powers the real-time toasts, header bell and
/// the SignalR "Notification" push. (Point-Management notifications live in the separate,
/// TMS-keyed <see cref="NotificationRepository"/>.)
/// </summary>
public sealed class AppNotificationRepository
{
    private readonly Db _db;
    public AppNotificationRepository(Db db) => _db = db;

    public async Task<long> CreateAsync(int companyId, long userId, string type, string? title, string? body, string? link, string? refId, string? iconKey)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<long>(@"
            INSERT INTO app.Notifications (UserID, CompanyID, Type, Title, Body, Link, RefId, IconKey)
            OUTPUT INSERTED.NotificationID
            VALUES (@userId, @companyId, @type, @title, @body, @link, @refId, @iconKey)",
            new { userId, companyId, type, title, body, link, refId, iconKey });
    }

    public async Task<NotificationDto?> GetAsync(long id)
    {
        await using var c = await _db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<NotificationDto>(
            "SELECT NotificationID, UserID, Type, Title, Body, Link, RefId, IconKey, IsRead, CreatedAt FROM app.Notifications WHERE NotificationID=@id",
            new { id });
    }

    public async Task<IEnumerable<NotificationDto>> ListAsync(long userId, int limit)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<NotificationDto>(@"
            SELECT TOP (@limit) NotificationID, UserID, Type, Title, Body, Link, RefId, IconKey, IsRead, CreatedAt
            FROM app.Notifications WHERE UserID=@userId ORDER BY NotificationID DESC",
            new { userId, limit });
    }

    public async Task<int> UnreadCountAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM app.Notifications WHERE UserID=@userId AND IsRead=0", new { userId });
    }

    public async Task MarkReadAsync(long userId, long id)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.Notifications SET IsRead=1 WHERE NotificationID=@id AND UserID=@userId", new { id, userId });
    }

    public async Task MarkAllReadAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.Notifications SET IsRead=1 WHERE UserID=@userId AND IsRead=0", new { userId });
    }

    public async Task DeleteAsync(long userId, long id)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("DELETE FROM app.Notifications WHERE NotificationID=@id AND UserID=@userId", new { id, userId });
    }

    /// <summary>Delete all already-read notifications for the user.</summary>
    public async Task<int> ClearReadAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteAsync("DELETE FROM app.Notifications WHERE UserID=@userId AND IsRead=1", new { userId });
    }

    // ── Settings ──
    public async Task<NotificationSettingsDto> GetSettingsAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<NotificationSettingsDto>(
            "SELECT ISNULL(NotifyMessages,1) AS NotifyMessages, ISNULL(NotifyEmails,1) AS NotifyEmails FROM app.Users WHERE UserId=@userId",
            new { userId }) ?? new NotificationSettingsDto();
    }

    public async Task SaveSettingsAsync(long userId, bool notifyMessages, bool notifyEmails)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.Users SET NotifyMessages=@notifyMessages, NotifyEmails=@notifyEmails WHERE UserId=@userId",
            new { userId, notifyMessages, notifyEmails });
    }

    public async Task<bool> WantsMessagesAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return (await c.ExecuteScalarAsync<bool?>("SELECT ISNULL(NotifyMessages,1) FROM app.Users WHERE UserId=@userId", new { userId })) ?? true;
    }

    public async Task<bool> WantsEmailsAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return (await c.ExecuteScalarAsync<bool?>("SELECT ISNULL(NotifyEmails,1) FROM app.Users WHERE UserId=@userId", new { userId })) ?? true;
    }

    // High-water mark so the email poller only notifies about genuinely new mail.
    public async Task<DateTime?> GetLastEmailNotifiedAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<DateTime?>("SELECT LastEmailNotifiedAt FROM app.Users WHERE UserId=@userId", new { userId });
    }

    public async Task SetLastEmailNotifiedAsync(long userId, DateTime at)
    {
        await using var c = await _db.OpenAsync();
        await c.ExecuteAsync("UPDATE app.Users SET LastEmailNotifiedAt=@at WHERE UserId=@userId", new { at, userId });
    }
}
