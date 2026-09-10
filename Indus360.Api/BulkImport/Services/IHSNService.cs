using Backend.DTOs;

namespace Backend.Services;

public interface IHSNService
{
    Task<List<HSNMasterDto>> GetHSNListAsync(int companyId);
    Task<HSNValidationResultDto> ValidateHSNsAsync(List<HSNMasterDto> hsns);
    Task<ImportResultDto> ImportHSNsAsync(List<HSNMasterDto> hsns, int userId);
    Task<ImportResultDto> ClearHSNDataAsync(int companyId, string username, string password, string reason);
    Task<ImportResultDto> SoftDeleteHSNAsync(int hsnId, int userId);
    Task<List<string>> GetItemGroupsAsync(int companyId);
}
