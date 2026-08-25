using System.Text.Json;
using Indus360.Api.Hubs;
using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;

namespace Indus360.Api.Controllers;

/// <summary>Chat / messaging REST API — full clone of the legacy /api/messaging feature.</summary>
[ApiController]
[Route("api/messaging")]
public sealed class MessagingController : ControllerBase
{
    private readonly MessagingRepository _repo;
    private readonly IHubContext<MessagingHub> _hub;
    private readonly PresenceTracker _presence;
    private readonly IWebHostEnvironment _env;
    private readonly NotificationPusher _pusher;

    public MessagingController(MessagingRepository repo, IHubContext<MessagingHub> hub, PresenceTracker presence, IWebHostEnvironment env, NotificationPusher pusher)
    { _repo = repo; _hub = hub; _presence = presence; _env = env; _pusher = pusher; }

    private Task Broadcast(int companyId, long roomId, string ev, object payload)
        => _hub.Clients.Group(MessagingHub.ConvGroup(companyId, roomId)).SendAsync(ev, payload);

    /// <summary>Push a WhatsApp-style notification to every other room member (chat message received).</summary>
    private async Task NotifyMessageRecipients(int companyId, long roomId, long senderId, ChatMessageDto msg)
    {
        var room = await _repo.GetRoomAsync(companyId, senderId, roomId);
        if (room == null) return;
        List<ChatParticipant> participants;
        try { participants = JsonSerializer.Deserialize<List<ChatParticipant>>(room.Participants ?? "[]") ?? new(); }
        catch { return; }

        var senderName = string.IsNullOrWhiteSpace(msg.UserName) ? "Someone" : msg.UserName!;
        var isGroup = room.Type != "DM";
        var preview = string.IsNullOrWhiteSpace(msg.Content)
            ? "📎 Attachment"
            : (msg.Content!.Length > 140 ? msg.Content[..140] : msg.Content);
        var title = isGroup ? $"{senderName} · {room.Name ?? "Group"}" : senderName;
        var link = $"/activity/messages?conv={roomId}";
        var icon = Initials(senderName);

        foreach (var p in participants)
        {
            if (p.userId == senderId) continue;
            await _pusher.PushAsync(companyId, p.userId, "Message", title, preview, link, roomId.ToString(), icon);
        }
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = string.Concat(parts.Take(2).Select(p => p[0]));
        return s.ToUpperInvariant();
    }

    // ── Conversations ─────────────────────────────────────────────────────
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] string? type)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        return Ok(await _repo.GetConversationsAsync(this.CurrentCompanyId(), uid.Value, type));
    }

    [HttpPost("conversations")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        if (string.IsNullOrWhiteSpace(req?.Name)) return BadRequest("Name is required for groups and channels");
        var cid = this.CurrentCompanyId();
        var name = await _repo.GetUserNameAsync(cid, uid.Value);
        return Ok(await _repo.CreateRoomAsync(cid, uid.Value, name, req!) ?? (object)new { });
    }

    [HttpPost("conversations/dm")]
    public async Task<IActionResult> GetOrCreateDM([FromBody] CreateDMRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        if (req is null || req.TargetUserID <= 0) return BadRequest("TargetUserID is required");
        var cid = this.CurrentCompanyId();
        var myName = await _repo.GetUserNameAsync(cid, uid.Value);
        return Ok(await _repo.GetOrCreateDMAsync(cid, uid.Value, myName, req.TargetUserID, req.TargetUserName) ?? (object)new { });
    }

    [HttpPost("conversations/{roomId:long}/update")]
    public async Task<IActionResult> UpdateRoom(long roomId, [FromBody] UpdateRoomRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        return Ok(await _repo.UpdateRoomAsync(this.CurrentCompanyId(), uid.Value, roomId, req ?? new()) ?? (object)new { });
    }

    // ── Messages ──────────────────────────────────────────────────────────
    [HttpGet("conversations/{roomId:long}/messages")]
    public async Task<IActionResult> GetMessages(long roomId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var uid = this.CurrentUserId() ?? 0;
        return Ok(await _repo.GetMessagesAsync(this.CurrentCompanyId(), roomId, uid, Math.Max(1, page), Math.Clamp(pageSize, 1, 100)));
    }

    [HttpPost("conversations/{roomId:long}/messages")]
    public async Task<IActionResult> SendMessage(long roomId, [FromBody] SendMessageRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        if (req is null || (string.IsNullOrWhiteSpace(req.Content) && string.IsNullOrWhiteSpace(req.AttachmentsJson)))
            return BadRequest("Message content or attachments required");
        var cid = this.CurrentCompanyId();
        if (!await _repo.IsRoomMemberAsync(cid, roomId, uid.Value)) return StatusCode(403, new { error = "Not a member of this conversation" });
        // "Only admins can send" groups: block non-admin members.
        if (await _repo.IsRoomReadOnlyAsync(cid, roomId) && !await _repo.IsRoomAdminAsync(cid, roomId, uid.Value))
            return StatusCode(403, new { error = "Only admins can send messages in this group" });
        var msg = await _repo.InsertMessageAsync(cid, roomId, uid.Value, req.Content, req.MessageType, req.ParentMessageID, req.AttachmentsJson);
        if (msg != null)
        {
            await Broadcast(cid, roomId, "ReceiveMessage", new { roomId, message = msg });
            await NotifyMessageRecipients(cid, roomId, uid.Value, msg);
        }
        return Ok((object?)msg ?? new { });
    }

    [HttpPost("messages/{messageId:long}/edit")]
    public async Task<IActionResult> EditMessage(long messageId, [FromBody] EditMessageRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        var roomId = await _repo.GetMessageRoomIdAsync(cid, messageId);
        var msg = await _repo.EditMessageAsync(cid, messageId, req?.Content ?? "");
        if (msg != null && roomId.HasValue) await Broadcast(cid, roomId.Value, "MessageEdited", new { roomId = roomId.Value, message = msg });
        return Ok((object?)msg ?? new { });
    }

    [HttpPost("messages/{messageId:long}/delete")]
    public async Task<IActionResult> DeleteMessage(long messageId)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        var roomId = await _repo.GetMessageRoomIdAsync(cid, messageId);
        await _repo.DeleteMessageAsync(cid, messageId);
        if (roomId.HasValue) await Broadcast(cid, roomId.Value, "MessageDeleted", new { roomId = roomId.Value, messageId });
        return Ok(new { Message = "Deleted" });
    }

    // ── Threads ───────────────────────────────────────────────────────────
    [HttpGet("messages/{messageId:long}/replies")]
    public async Task<IActionResult> GetReplies(long messageId)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        return Ok(await _repo.GetRepliesAsync(this.CurrentCompanyId(), messageId));
    }

    [HttpPost("messages/{messageId:long}/replies")]
    public async Task<IActionResult> Reply(long messageId, [FromBody] SendMessageRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        var roomId = await _repo.GetMessageRoomIdAsync(cid, messageId);
        if (roomId is null) return NotFound(new { error = "Parent message not found" });
        var msg = await _repo.InsertMessageAsync(cid, roomId.Value, uid.Value, req?.Content, req?.MessageType, messageId, req?.AttachmentsJson);
        if (msg != null)
        {
            await Broadcast(cid, roomId.Value, "ReceiveMessage", new { roomId = roomId.Value, message = msg });
            await NotifyMessageRecipients(cid, roomId.Value, uid.Value, msg);
        }
        return Ok((object?)msg ?? new { });
    }

    // ── Reactions ─────────────────────────────────────────────────────────
    [HttpPost("messages/{messageId:long}/reactions")]
    public async Task<IActionResult> UpdateReactions(long messageId, [FromBody] ReactionsRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        var roomId = await _repo.GetMessageRoomIdAsync(cid, messageId);
        var msg = await _repo.UpdateReactionsAsync(cid, messageId, req?.ReactionsJson);
        if (msg != null && roomId.HasValue) await Broadcast(cid, roomId.Value, "ReactionUpdated", new { roomId = roomId.Value, message = msg });
        return Ok((object?)msg ?? new { });
    }

    // ── Pin / Star / Delete-for-me (WhatsApp-style message menu) ────────────
    [HttpPost("messages/{messageId:long}/pin")]
    public async Task<IActionResult> PinMessage(long messageId, [FromBody] PinRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        var roomId = await _repo.SetPinAsync(cid, messageId, uid.Value, req?.IsPinned ?? false);
        if (roomId.HasValue)
            await Broadcast(cid, roomId.Value, "MessagePinned", new { roomId = roomId.Value, messageId, isPinned = req?.IsPinned ?? false, pinnedBy = uid.Value });
        return Ok(new { Message = (req?.IsPinned ?? false) ? "Pinned" : "Unpinned" });
    }

    [HttpGet("conversations/{roomId:long}/pinned")]
    public async Task<IActionResult> GetPinned(long roomId)
    {
        var uid = this.CurrentUserId() ?? 0;
        return Ok(await _repo.GetPinnedAsync(this.CurrentCompanyId(), roomId, uid));
    }

    [HttpPost("messages/{messageId:long}/star")]
    public async Task<IActionResult> StarMessage(long messageId, [FromBody] StarRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        await _repo.SetStarAsync(this.CurrentCompanyId(), uid.Value, messageId, req?.IsStarred ?? false);
        return Ok(new { Message = (req?.IsStarred ?? false) ? "Starred" : "Unstarred" });
    }

    /// <summary>Delete for me — hides the message for the caller only (no broadcast).</summary>
    [HttpPost("messages/{messageId:long}/delete-for-me")]
    public async Task<IActionResult> DeleteForMe(long messageId)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        await _repo.HideForUserAsync(this.CurrentCompanyId(), uid.Value, messageId);
        return Ok(new { Message = "Deleted for you" });
    }

    // ── Group management (members / admin / read-only / delete) ────────────
    /// <summary>Broadcast the updated room (Participants, IsReadOnly, Name…) to everyone viewing it.</summary>
    private async Task BroadcastRoom(int companyId, long roomId)
    {
        var room = await _repo.GetRoomAsync(companyId, 0, roomId);
        if (room != null) await Broadcast(companyId, roomId, "RoomUpdated", new { roomId, room });
    }

    [HttpPost("conversations/{roomId:long}/members")]
    public async Task<IActionResult> AddMembers(long roomId, [FromBody] AddMembersRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        if (!await _repo.IsRoomAdminAsync(cid, roomId, uid.Value)) return StatusCode(403, new { error = "Only group admins can add members" });
        var members = (req?.Members ?? new()).Select(m => new ChatParticipant { userId = m.UserID, userName = m.UserName });
        var added = await _repo.AddMembersAsync(cid, roomId, members);
        await BroadcastRoom(cid, roomId);
        return Ok(new { Message = $"Added {added.Count} member(s)", added });
    }

    [HttpPost("conversations/{roomId:long}/members/{targetUserId:long}/remove")]
    public async Task<IActionResult> RemoveMember(long roomId, long targetUserId)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        if (!await _repo.IsRoomAdminAsync(cid, roomId, uid.Value)) return StatusCode(403, new { error = "Only group admins can remove members" });
        if (await _repo.GetRoomCreatedByAsync(cid, roomId) == targetUserId) return StatusCode(403, new { error = "The group owner can't be removed" });
        var name = await _repo.RemoveMemberAsync(cid, roomId, targetUserId);
        await BroadcastRoom(cid, roomId);
        return Ok(new { Message = name == null ? "Not a member" : $"Removed {name}" });
    }

    [HttpPost("conversations/{roomId:long}/leave")]
    public async Task<IActionResult> LeaveGroup(long roomId)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        await _repo.RemoveMemberAsync(cid, roomId, uid.Value);
        await BroadcastRoom(cid, roomId);
        return Ok(new { Message = "You left the group" });
    }

    [HttpPost("conversations/{roomId:long}/settings")]
    public async Task<IActionResult> UpdateGroupSettings(long roomId, [FromBody] SetReadOnlyRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        if (!await _repo.IsRoomAdminAsync(cid, roomId, uid.Value)) return StatusCode(403, new { error = "Only group admins can change settings" });
        await _repo.SetReadOnlyAsync(cid, roomId, req?.IsReadOnly ?? false);
        await BroadcastRoom(cid, roomId);
        return Ok(new { Message = "Settings updated" });
    }

    [HttpPost("conversations/{roomId:long}/members/{targetUserId:long}/role")]
    public async Task<IActionResult> SetMemberRole(long roomId, long targetUserId, [FromBody] SetRoleRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        if (!await _repo.IsRoomAdminAsync(cid, roomId, uid.Value)) return StatusCode(403, new { error = "Only group admins can change roles" });
        var role = string.Equals(req?.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Member";
        await _repo.SetMemberRoleAsync(cid, roomId, targetUserId, role);
        await BroadcastRoom(cid, roomId);
        return Ok(new { Message = $"Role updated to {role}" });
    }

    [HttpPost("conversations/{roomId:long}/delete")]
    public async Task<IActionResult> DeleteRoom(long roomId)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        var createdBy = await _repo.GetRoomCreatedByAsync(cid, roomId);
        if (createdBy != uid.Value && !await _repo.IsRoomAdminAsync(cid, roomId, uid.Value))
            return StatusCode(403, new { error = "Only the group owner or an admin can delete this group" });
        await _repo.DeleteRoomAsync(cid, roomId);
        await Broadcast(cid, roomId, "RoomDeleted", new { roomId });
        return Ok(new { Message = "Group deleted" });
    }

    // ── Read status ───────────────────────────────────────────────────────
    [HttpPost("conversations/{roomId:long}/read")]
    public async Task<IActionResult> MarkRead(long roomId, [FromBody] MarkReadRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var cid = this.CurrentCompanyId();
        await _repo.MarkReadAsync(cid, roomId, uid.Value, req?.LastReadMessageID ?? 0);
        await Broadcast(cid, roomId, "ReadReceipt", new { roomId, userId = uid.Value, lastReadMessageId = req?.LastReadMessageID ?? 0 });
        return Ok(new { Message = "Marked as read" });
    }

    [HttpGet("unread-counts")]
    public async Task<IActionResult> GetUnreadCounts()
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        return Ok(await _repo.GetUnreadCountsAsync(this.CurrentCompanyId(), uid.Value));
    }

    [HttpGet("online-users")]
    public IActionResult GetOnlineUsers() => Ok(_presence.OnlineUsers(this.CurrentCompanyId()));

    /// <summary>WhatsApp-style presence for one user: whether they're online now + their last-seen time.</summary>
    [HttpGet("last-seen/{userId:long}")]
    public async Task<IActionResult> GetLastSeen(long userId)
    {
        var isOnline = _presence.OnlineUsers(this.CurrentCompanyId()).Contains(userId.ToString());
        var lastSeenAt = await _repo.GetLastSeenAsync(userId);
        return Ok(new { userId, isOnline, lastSeenAt });
    }

    // ── Search & contacts ─────────────────────────────────────────────────
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 50)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
        return Ok(await _repo.SearchAsync(this.CurrentCompanyId(), uid.Value, q, Math.Clamp(limit, 1, 100)));
    }

    [HttpGet("contacts")]
    public async Task<IActionResult> GetContacts()
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        return Ok(await _repo.GetContactsAsync(this.CurrentCompanyId(), uid.Value));
    }

    // ── File upload / download (self-contained; no static middleware needed) ──
    private string UploadDir => Path.Combine(_env.ContentRootPath, "chat-uploads");

    [HttpPost("upload")]
    [RequestSizeLimit(26_214_400)] // 25 MB
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file" });
        Directory.CreateDirectory(UploadDir);
        var ext = Path.GetExtension(file.FileName);
        var stored = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = System.IO.File.Create(Path.Combine(UploadDir, stored)))
            await file.CopyToAsync(fs);
        var url = $"{Request.Scheme}://{Request.Host}/api/messaging/files/{stored}";
        return Ok(new { FileName = file.FileName, FileUrl = url, FileSize = file.Length, MimeType = file.ContentType });
    }

    [HttpGet("files/{name}")]
    public IActionResult GetFile(string name)
    {
        if (name.Contains("..") || name.Contains('/') || name.Contains('\\')) return BadRequest();
        var path = Path.Combine(UploadDir, name);
        if (!System.IO.File.Exists(path)) return NotFound();
        if (!new FileExtensionContentTypeProvider().TryGetContentType(path, out var ct)) ct = "application/octet-stream";
        return PhysicalFile(path, ct);
    }
}
