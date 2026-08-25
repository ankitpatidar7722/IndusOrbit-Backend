using System.Text.Json.Serialization;

namespace Indus360.Api.Models;

/// <summary>A user-facing notification (chat message / email / system) — PascalCase for the frontend.</summary>
public sealed class NotificationDto
{
    [JsonPropertyName("NotificationID")] public long NotificationID { get; set; }
    [JsonPropertyName("UserID")] public long UserID { get; set; }
    [JsonPropertyName("Type")] public string Type { get; set; } = "Message"; // Message | Email | System
    [JsonPropertyName("Title")] public string? Title { get; set; }
    [JsonPropertyName("Body")] public string? Body { get; set; }
    [JsonPropertyName("Link")] public string? Link { get; set; }
    [JsonPropertyName("RefId")] public string? RefId { get; set; }
    [JsonPropertyName("IconKey")] public string? IconKey { get; set; }
    [JsonPropertyName("IsRead")] public bool IsRead { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; }
}

public sealed class NotificationSettingsDto
{
    [JsonPropertyName("NotifyMessages")] public bool NotifyMessages { get; set; } = true;
    [JsonPropertyName("NotifyEmails")] public bool NotifyEmails { get; set; } = true;
}

public sealed class SaveNotifSettingsRequest
{
    public bool NotifyMessages { get; set; } = true;
    public bool NotifyEmails { get; set; } = true;
}
