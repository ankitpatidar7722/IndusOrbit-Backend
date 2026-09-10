using Backend.DTOs;

namespace Backend.Services;

public interface IModuleService
{
    Task<List<ModuleDto>> GetModulesByHeadNameAsync(string headName);

    // CRUD
    Task<List<ModuleDto>> GetAllModulesAsync();
    Task<int> CreateModuleAsync(ModuleDto module);
    Task<bool> UpdateModuleAsync(ModuleDto module);
    Task<bool> DeleteModuleAsync(int moduleId);
    Task<List<string>> GetUniqueModuleHeadsAsync();
    Task<IEnumerable<dynamic>> GetDebugModuleData();

    // Create Module Form helpers
    Task<List<ModuleDto>> GetIndusModulesAsync();
    Task<List<string>> GetIndusModuleNamesAsync();
    Task<IndusModuleInfoDto?> GetIndusModuleInfoAsync(string moduleName);
    Task<IndusModuleInfoDto?> GetIndusModuleInfoForClientAsync(string moduleName, string connectionString);
    Task<ModuleSystemDefaultsDto> GetSystemDefaultsAsync();
    Task<int> GetNextDisplayOrderAsync(int setGroupIndex);
    Task<bool> CheckModuleExistsAsync(string moduleName);
    Task<bool> CheckDisplayOrderExistsAsync(int order, int setGroupIndex);
    Task<bool> CheckGroupIndexInUseAsync(int groupIndex, string currentHeadName);
    Task ShiftDisplayOrdersAsync(int fromOrder, int setGroupIndex);
    
    // Item/Ledger Group Comparison (Masters.aspx / LedgerMaster.aspx)
    Task<List<ItemGroupComparisonDto>> GetItemGroupComparisonAsync(string type);
    Task<bool> SyncItemGroupsAsync(string type, List<ItemGroupComparisonDto> syncData);
    
    // Client-DB variants
    Task<List<ItemGroupComparisonDto>> GetItemGroupComparisonForClientAsync(string type, string connectionString);
    Task<bool> SyncItemGroupsForClientAsync(string type, List<ItemGroupComparisonDto> syncData, string connectionString);
}
