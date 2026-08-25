using Indus360.Api.Services;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace Indus360.Api.Hubs;

/// <summary>
/// Real-time messaging hub (ASP.NET Core SignalR) — rewritten from the legacy classic-SignalR
/// MessagingHub. The frontend connects with ?userId=&companyId= in the hub URL. Server → client
/// events (matched by MessagingContext): ReceiveMessage, MessageEdited, MessageDeleted,
/// ReactionUpdated, ReadReceipt, PresenceChanged, TypingIndicator.
/// </summary>
public sealed class MessagingHub : Hub
{
    private readonly PresenceTracker _presence;
    private readonly MessagingRepository _repo;
    public MessagingHub(PresenceTracker presence, MessagingRepository repo)
    { _presence = presence; _repo = repo; }

    private (int companyId, long userId) Ctx()
    {
        var q = Context.GetHttpContext()?.Request.Query;
        int companyId = int.TryParse(q?["companyId"], out var c) ? c : 1;
        long userId = long.TryParse(q?["userId"], out var u) ? u : 0;
        return (companyId, userId);
    }

    public static string CompanyGroup(int companyId) => $"company_{companyId}";
    public static string ConvGroup(int companyId, long roomId) => $"conv_{companyId}_{roomId}";
    /// <summary>Per-user group — for notifications pushed to one specific user (all their tabs).</summary>
    public static string UserGroup(int companyId, long userId) => $"user_{companyId}_{userId}";

    public override async Task OnConnectedAsync()
    {
        var (companyId, userId) = Ctx();
        await Groups.AddToGroupAsync(Context.ConnectionId, CompanyGroup(companyId));
        if (userId > 0) await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(companyId, userId));
        if (userId > 0 && _presence.Connect(companyId, userId))
        {
            await _repo.SetLastSeenAsync(userId); // keep a fresh baseline even if a disconnect is later missed
            await Clients.Group(CompanyGroup(companyId)).SendAsync("PresenceChanged", new { userId, isOnline = true });
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var (companyId, userId) = Ctx();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, CompanyGroup(companyId));
        if (userId > 0 && _presence.Disconnect(companyId, userId))
        {
            var lastSeenAt = await _repo.SetLastSeenAsync(userId);
            await Clients.Group(CompanyGroup(companyId)).SendAsync("PresenceChanged", new { userId, isOnline = false, lastSeenAt });
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Client joins a conversation's real-time group (to receive its messages/typing).</summary>
    public Task JoinConversation(long conversationId)
    {
        var (companyId, _) = Ctx();
        return Groups.AddToGroupAsync(Context.ConnectionId, ConvGroup(companyId, conversationId));
    }

    public Task LeaveConversation(long conversationId)
    {
        var (companyId, _) = Ctx();
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ConvGroup(companyId, conversationId));
    }

    /// <summary>Broadcast a typing indicator to the other members of a conversation.
    /// Signature matches the client call: conn.invoke('SendTyping', roomId, isTyping).</summary>
    public Task SendTyping(long conversationId, bool isTyping)
    {
        var (companyId, userId) = Ctx();
        return Clients.OthersInGroup(ConvGroup(companyId, conversationId))
            .SendAsync("TypingIndicator", new { roomId = conversationId, userId, isTyping });
    }
}
