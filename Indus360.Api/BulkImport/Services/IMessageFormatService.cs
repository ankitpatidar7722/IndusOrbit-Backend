using Backend.DTOs;

namespace Backend.Services;

public interface IMessageFormatService
{
    Task<MessageFormatListResponse> GetAllActiveAsync();
    Task<MessageFormatResponse> CreateAsync(MessageFormatSaveRequest request);
    Task<MessageFormatResponse> UpdateAsync(MessageFormatSaveRequest request);
    Task<MessageFormatResponse> DeleteAsync(int messageId);
}
