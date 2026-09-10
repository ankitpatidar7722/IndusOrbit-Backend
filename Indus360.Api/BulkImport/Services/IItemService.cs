using Backend.DTOs;

namespace Backend.Services;

public interface IItemService
{
    Task<List<ItemMasterDto>> GetAllItemsAsync(int itemGroupId);
    Task<bool> SoftDeleteItemAsync(int itemId);
    Task<ItemValidationResultDto> ValidateItemsAsync(List<ItemMasterDto> items, int itemGroupId);
    Task<ImportResultDto> ImportItemsAsync(List<ItemMasterDto> items, int itemGroupId);
    Task<List<ItemGroupDto>> GetItemGroupsAsync();
    Task<List<HSNGroupDto>> GetHSNGroupsAsync();
    Task<FieldUnitsDto> GetFieldUnitsAsync(int itemGroupId);
    Task<List<ItemSubGroupDto>> GetItemSubGroupsAsync(int itemGroupId);
    Task<int> ClearAllItemDataAsync(string username, string password, string reason, int itemGroupId);
}
