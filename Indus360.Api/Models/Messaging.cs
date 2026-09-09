using System.Text.Json.Serialization;

namespace Indus360.Api.Models;

// ── Messaging (chat) — full clone of the legacy /activity/messages feature ──
// Response DTOs use [JsonPropertyName] to force PascalCase (RoomID, MessageID, …)
// exactly as the migrated frontend components consume them.

public sealed class ChatRoomDto
{
    [JsonPropertyName("RoomID")] public long RoomID { get; set; }
    [JsonPropertyName("CompanyID")] public int CompanyID { get; set; }
    [JsonPropertyName("Type")] public string Type { get; set; } = "DM";
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("Description")] public string? Description { get; set; }
    [JsonPropertyName("AvatarUrl")] public string? AvatarUrl { get; set; }
    [JsonPropertyName("IsPublic")] public bool IsPublic { get; set; }
    [JsonPropertyName("IsReadOnly")] public bool IsReadOnly { get; set; }
    [JsonPropertyName("CreatedBy")] public long CreatedBy { get; set; }
    [JsonPropertyName("CreatedDate")] public DateTime CreatedDate { get; set; }
    [JsonPropertyName("UpdatedDate")] public DateTime UpdatedDate { get; set; }
    [JsonPropertyName("LastMessageAt")] public DateTime? LastMessageAt { get; set; }
    [JsonPropertyName("LastMessagePreview")] public string? LastMessagePreview { get; set; }
    [JsonPropertyName("LastMessageBy")] public long? LastMessageBy { get; set; }
    [JsonPropertyName("IsArchived")] public bool IsArchived { get; set; }
    [JsonPropertyName("IsDeleted")] public bool IsDeleted { get; set; }
    [JsonPropertyName("Participants")] public string? Participants { get; set; } // JSON string
    [JsonPropertyName("UnreadCount")] public int UnreadCount { get; set; }
}

public sealed class ChatMessageDto
{
    [JsonPropertyName("MessageID")] public long MessageID { get; set; }
    [JsonPropertyName("RoomID")] public long RoomID { get; set; }
    [JsonPropertyName("CompanyID")] public int CompanyID { get; set; }
    [JsonPropertyName("UserID")] public long UserID { get; set; }
    [JsonPropertyName("Content")] public string? Content { get; set; }
    [JsonPropertyName("MessageType")] public string MessageType { get; set; } = "Text";
    [JsonPropertyName("ParentMessageID")] public long? ParentMessageID { get; set; }
    [JsonPropertyName("ReplyDepth")] public int ReplyDepth { get; set; }
    [JsonPropertyName("ReplyCount")] public int ReplyCount { get; set; }
    // The message this one is replying to (for the WhatsApp-style quote shown inside a reply bubble).
    [JsonPropertyName("ParentSenderName")] public string? ParentSenderName { get; set; }
    [JsonPropertyName("ParentContent")] public string? ParentContent { get; set; }
    [JsonPropertyName("ParentAttachments")] public string? ParentAttachments { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("EditedAt")] public DateTime? EditedAt { get; set; }
    [JsonPropertyName("IsEdited")] public bool IsEdited { get; set; }
    [JsonPropertyName("IsDeleted")] public bool IsDeleted { get; set; }
    [JsonPropertyName("Reactions")] public string? Reactions { get; set; }
    [JsonPropertyName("Attachments")] public string? Attachments { get; set; }
    [JsonPropertyName("IsPinned")] public bool IsPinned { get; set; }
    [JsonPropertyName("PinnedBy")] public long? PinnedBy { get; set; }
    [JsonPropertyName("IsStarred")] public bool IsStarred { get; set; }   // per-viewing-user
    [JsonPropertyName("UserName")] public string? UserName { get; set; }
    [JsonPropertyName("RoomName")] public string? RoomName { get; set; }
    [JsonPropertyName("RoomType")] public string? RoomType { get; set; }
}

public sealed class ChatContactDto
{
    [JsonPropertyName("UserID")] public long UserID { get; set; }
    [JsonPropertyName("UserName")] public string UserName { get; set; } = "";
    [JsonPropertyName("Designation")] public string? Designation { get; set; }
    [JsonPropertyName("Email")] public string? Email { get; set; }
}

public sealed class UnreadCountDto
{
    [JsonPropertyName("RoomID")] public long RoomID { get; set; }
    [JsonPropertyName("UnreadCount")] public int UnreadCount { get; set; }
}

// ── Request bodies (PascalCase from the frontend) ──
public sealed class CreateRoomRequest
{
    public string Type { get; set; } = "Group";   // Group | Channel
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public bool IsReadOnly { get; set; }
    public string? ParticipantsJson { get; set; }
}
public sealed class CreateDMRequest { public long TargetUserID { get; set; } public string? TargetUserName { get; set; } }
public sealed class SendMessageRequest { public string? Content { get; set; } public string? MessageType { get; set; } public long? ParentMessageID { get; set; } public string? AttachmentsJson { get; set; } public List<long>? Mentions { get; set; } }
public sealed class UpdateRoomRequest { public string? Name { get; set; } public string? Description { get; set; } public bool? IsPublic { get; set; } public bool? IsReadOnly { get; set; } public string? ParticipantsJson { get; set; } }
public sealed class EditMessageRequest { public string Content { get; set; } = ""; }
public sealed class ReactionsRequest { public string? ReactionsJson { get; set; } }
public sealed class MarkReadRequest { public long LastReadMessageID { get; set; } }
public sealed class PinRequest { public bool IsPinned { get; set; } }
public sealed class StarRequest { public bool IsStarred { get; set; } }

// ── Group management ──
public sealed class MemberInput { public long UserID { get; set; } public string? UserName { get; set; } }
public sealed class AddMembersRequest { public List<MemberInput> Members { get; set; } = new(); }
public sealed class SetReadOnlyRequest { public bool IsReadOnly { get; set; } }
public sealed class SetRoleRequest { public string Role { get; set; } = "Member"; }
public sealed class ChatPrefsRequest { public bool? Mute { get; set; } public bool? Pin { get; set; } public bool? Archive { get; set; } }

/// <summary>One participant in a room's Participants JSON array.</summary>
public sealed class ChatParticipant
{
    [JsonPropertyName("userId")] public long userId { get; set; }
    [JsonPropertyName("userName")] public string? userName { get; set; }
    [JsonPropertyName("role")] public string? role { get; set; }
    [JsonPropertyName("lastReadMessageId")] public long? lastReadMessageId { get; set; }
    [JsonPropertyName("lastReadAt")] public string? lastReadAt { get; set; }
    [JsonPropertyName("isMuted")] public bool isMuted { get; set; }
    // Per-user chat prefs (WhatsApp-style): pin this chat to the top of MY list; archive it out of MY
    // main list. Both live per-participant so they never affect other members.
    [JsonPropertyName("isPinned")] public bool isPinned { get; set; }
    [JsonPropertyName("archivedAt")] public string? archivedAt { get; set; }
    [JsonPropertyName("joinedAt")] public string? joinedAt { get; set; }
    // WhatsApp-style soft leave: set when the user leaves/is removed — they stay a "past member"
    // (read-only history up to this time), never hard-removed. null = active member.
    [JsonPropertyName("leftAt")] public string? leftAt { get; set; }
    // Per-user "delete group for me": set when a (usually left) user removes the group from THEIR
    // list only. The group/messages stay for everyone else. null = visible in their list.
    [JsonPropertyName("hiddenAt")] public string? hiddenAt { get; set; }
}
