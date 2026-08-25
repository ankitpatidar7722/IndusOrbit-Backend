using Indus360.Api.Hubs;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace Indus360.Api.Services;

/// <summary>
/// Persists a notification and pushes it in real time to the target user's SignalR group
/// (all their open tabs). Used by chat (message notifications) and email (new-mail notifications).
/// Honours the user's per-type on/off setting.
/// </summary>
public sealed class NotificationPusher
{
    private readonly AppNotificationRepository _repo;
    private readonly IHubContext<MessagingHub> _hub;
    public NotificationPusher(AppNotificationRepository repo, IHubContext<MessagingHub> hub)
    { _repo = repo; _hub = hub; }

    /// <summary>Create + push a notification to one user (skips if that type is disabled in their settings).</summary>
    public async Task PushAsync(int companyId, long userId, string type,
        string? title, string? body, string? link, string? refId, string? iconKey)
    {
        // Respect the user's per-type preference.
        var wanted = type == "Email" ? await _repo.WantsEmailsAsync(userId)
                   : type == "Message" ? await _repo.WantsMessagesAsync(userId)
                   : true;
        if (!wanted) return;

        var id = await _repo.CreateAsync(companyId, userId, type, title, body, link, refId, iconKey);
        var dto = await _repo.GetAsync(id);
        if (dto == null) return;
        var unread = await _repo.UnreadCountAsync(userId);
        await _hub.Clients.Group(MessagingHub.UserGroup(companyId, userId))
            .SendAsync("Notification", new { notification = dto, unread });
    }
}
