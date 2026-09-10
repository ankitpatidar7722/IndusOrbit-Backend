using Backend.DTOs;

namespace Backend.Services;

public interface IItemStockService
{
    Task<List<WarehouseDto>> GetWarehousesAsync();
    Task<List<WarehouseDto>> GetBinsByWarehouseAsync(string warehouseName);
    Task<ItemStockEnrichResult> EnrichStockRowsAsync(List<ItemStockEnrichRowDto> rows, int itemGroupId);
    Task<ItemStockImportResult> ImportItemStockAsync(List<ItemStockRowDto> rows, int itemGroupId);
    Task<ItemStockValidationResult> ValidateStockRowsAsync(List<ItemStockEnrichedRow> rows, int itemGroupId);
    Task<List<ItemStockEnrichedRow>> GetStockDataAsync(int itemGroupId);
    Task<ItemStockImportResult> ResetItemStockAsync(int itemGroupId, string username, string password, string reason, List<int>? itemIds = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<ItemStockImportResult> ResetFloorStockAsync(int itemGroupId, string username, string password, string reason, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<ItemStockEnrichedRow>> GetMasterDataAsync(int itemGroupId);
}
