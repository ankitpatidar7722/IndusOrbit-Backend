namespace Backend.DTOs;

public class MessageFormatDto
{
    public int MessageID { get; set; }
    public string MessageTitle { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class MessageFormatListResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<MessageFormatDto> Data { get; set; } = new();
}

public class MessageFormatResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public MessageFormatDto? Data { get; set; }
}

public class MessageFormatSaveRequest
{
    public int? MessageID { get; set; }
    public string MessageTitle { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
