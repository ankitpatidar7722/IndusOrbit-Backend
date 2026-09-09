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

    // Ticket title, trimmed + truncated, for a clean one-line notification (e.g. — "Login page crashes…").
    private static string TitleContext(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0) return "";
        if (t.Length > 50) t = t[..50].TrimEnd() + "…";
        return $" — \"{t}\"";
    }

    private sealed record PointNotifyInfo(string? Title, int? AssignedToID, int? ReportedByID);

    /// <summary>
    /// Point-workflow notifications. Each <paramref name="kind"/> targets the ONE person waiting on
    /// that hand-off — the assigned developer or the reporter — with a clear, professional message
    /// and a deep link to the board where they act on it.
    /// </summary>
    public async Task NotifyPointEventAsync(int pointId, string kind)
    {
        await using var db = await _db.OpenTmsAsync();
        var p = await db.QuerySingleOrDefaultAsync<PointNotifyInfo>(
            "SELECT Title, AssignedToID, ReportedByID FROM dbo.Points WHERE PointID = @pointId", new { pointId });
        if (p is null) return;
        var ctx = TitleContext(p.Title);

        int? target;
        string message, url;
        switch (kind)
        {
            // ── To the assigned developer ──
            case "assigned":
                target = p.AssignedToID;
                message = $"Ticket #{pointId} has been assigned to you{ctx}. Please review the details and begin work.";
                url = "/point-management/developer"; break;
            case "support-verified":
                target = p.AssignedToID;
                message = $"Ticket #{pointId} has cleared Support verification{ctx}. You can now send it for merge.";
                url = "/point-management/developer"; break;
            case "reopened":
                target = p.AssignedToID;
                message = $"Ticket #{pointId} has been reopened and needs your attention{ctx}. Please review the remarks and resume work.";
                url = "/point-management/developer"; break;
            case "merge-reopened":
                target = p.AssignedToID;
                message = $"Ticket #{pointId} was returned from Merge review{ctx}. Please address the remarks and resubmit.";
                url = "/point-management/developer"; break;

            // ── To the reporter / support owner ──
            case "sent-to-support":
                target = p.ReportedByID;
                message = $"Ticket #{pointId} is ready for your Support verification{ctx}. Please review and confirm.";
                url = "/point-management/support"; break;
            case "verified":
                target = p.ReportedByID;
                message = $"Ticket #{pointId} has been approved and queued for assignment{ctx}.";
                url = "/point-management/manage-points"; break;
            case "rejected":
                target = p.ReportedByID;
                message = $"Ticket #{pointId} has been marked Un-Active{ctx}. Please review the admin remarks for details.";
                url = "/point-management/manage-points"; break;
            case "closed":
                target = p.ReportedByID;
                message = $"Ticket #{pointId} has been verified and closed{ctx}. Thank you for reporting.";
                url = "/point-management/manage-points"; break;

            default:
                return;
        }
        if ((target ?? 0) > 0)
            await CreateAsync(db, target!.Value, message, url);
    }
}
