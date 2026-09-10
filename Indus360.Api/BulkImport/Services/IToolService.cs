using Backend.DTOs;

namespace Backend.Services;

public interface IToolService
{
    Task<List<ToolMasterDto>> GetAllToolsAsync(int toolGroupId);
    Task<bool> SoftDeleteToolAsync(int toolId);
    Task<ToolValidationResultDto> ValidateToolsAsync(List<ToolMasterDto> tools, int toolGroupId);
    Task<ImportResultDto> ImportToolsAsync(List<ToolMasterDto> tools, int toolGroupId);
    Task<List<ToolGroupDto>> GetToolGroupsAsync();
    Task<List<HSNGroupDto>> GetToolHSNGroupsAsync();
    Task<List<UnitDto>> GetToolUnitsAsync();
    Task<int> ClearAllToolDataAsync(string username, string password, string reason, int toolGroupId);
}
