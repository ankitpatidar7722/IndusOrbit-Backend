using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Point Management (TMS) notifications — dbo.Notifications keyed by dbo.Users.UserID,
/// resolved from the app user's email. Powers the header bell (via PointManagement API).
/// (Chat/email notifications live in <see cref="AppNotificationRepository"/>.)
/// </summary>
public sealed class NotificationRepository
{
    private readonly Db _db;
    public NotificationRepository(Db db) => _db = db;

    private static Task<int?> ResolveTmsUserIdAsync(SqlConnection tms, string email)
        => tms.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @email", new { email });

    public async Task<IEnumerable<NotificationRow>> GetForEmailAsync(string email)
    {
        await using var tms = await _db.OpenTmsAsync();
        var uid = await ResolveTmsUserIdAsync(tms, email);
        if ((uid ?? 0) == 0) return Enumerable.Empty<NotificationRow>();
        return await tms.QueryAsync<NotificationRow>(
            "SELECT TOP 30 NotificationID, MessageText, NavigateURL, IsRead, DateCreated FROM dbo.Notifications WHERE UserID_To = @uid ORDER BY NotificationID DESC",
            new { uid });
    }

    public async Task<int> GetUnreadCountForEmailAsync(string email)
    {
        await using var tms = await _db.OpenTmsAsync();
        var uid = await ResolveTmsUserIdAsync(tms, email);
        if ((uid ?? 0) == 0) return 0;
        return await tms.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Notifications WHERE UserID_To = @uid AND IsRead = 0", new { uid });
    }

    public async Task MarkReadAsync(int id)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Notifications SET IsRead = 1 WHERE NotificationID = @id", new { id });
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("DELETE FROM dbo.Notifications WHERE NotificationID = @id", new { id });
    }

    public async Task MarkAllReadForEmailAsync(string email)
    {
        await using var tms = await _db.OpenTmsAsync();
        var uid = await ResolveTmsUserIdAsync(tms, email);
        if ((uid ?? 0) != 0)
            await tms.ExecuteAsync("UPDATE dbo.Notifications SET IsRead = 1 WHERE UserID_To = @uid AND IsRead = 0", new { uid });
    }

    private static Task CreateAsync(SqlConnection db, int userIdTo, string message, string url)
        => db.ExecuteAsync(
            "INSERT INTO dbo.Notifications (UserID_To, MessageText, NavigateURL, IsRead, DateCreated) VALUES (@userIdTo, @message, @url, 0, GETDATE())",
            new { userIdTo, message, url });

    public async Task NotifyUserAsync(int userId, string message, string url)
    {
        if (userId <= 0) return;
        await using var db = await _db.OpenTmsAsync();
        await CreateAsync(db, userId, message, url);
    }

    public async Task NotifyPointEventAsync(int pointId, string kind)
    {
        await using var db = await _db.OpenTmsAsync();
        var title = (await db.ExecuteScalarAsync<string>("SELECT Title FROM dbo.Points WHERE PointID = @pointId", new { pointId })) ?? "";
        int? target;
        string message, url;
        switch (kind)
        {
            case "assigned":
                target = await db.ExecuteScalarAsync<int?>("SELECT AssignedToID FROM dbo.Points WHERE PointID = @pointId", new { pointId });
                message = $"New point assigned: #{pointId} {title}";
                url = "/point-management/developer";
                break;
            case "sent-to-support":
                target = await db.ExecuteScalarAsync<int?>("SELECT ReportedByID FROM dbo.Points WHERE PointID = @pointId", new { pointId });
                message = $"Point #{pointId} awaiting your support verification";
                url = "/point-management/support";
                break;
            case "reopened":
                target = await db.ExecuteScalarAsync<int?>("SELECT AssignedToID FROM dbo.Points WHERE PointID = @pointId", new { pointId });
                message = $"Point #{pointId} was reopened for you";
                url = "/point-management/developer";
                break;
            case "closed":
                target = await db.ExecuteScalarAsync<int?>("SELECT ReportedByID FROM dbo.Points WHERE PointID = @pointId", new { pointId });
                message = $"Point #{pointId} was closed";
                url = "/point-management/manage-points";
                break;
            default:
                return;
        }
        if ((target ?? 0) > 0)
            await CreateAsync(db, target!.Value, message, url);
    }
}
