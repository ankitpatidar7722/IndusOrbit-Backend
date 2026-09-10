using Backend.DTOs;

namespace Backend.Services;

public interface ISparePartMasterStockService
{
    Task<List<WarehouseDto>> GetWarehousesAsync();
    Task<List<WarehouseDto>> GetBinsByWarehouseAsync(string warehouseName);
    Task<SparePartStockEnrichResult> EnrichStockRowsAsync(List<SparePartStockEnrichRowDto> rows);
    Task<SparePartStockImportResult> ImportSparePartStockAsync(List<SparePartStockRowDto> rows);
    Task<SparePartStockValidationResult> ValidateStockRowsAsync(List<SparePartStockEnrichedRow> rows);
    Task<List<SparePartStockEnrichedRow>> GetStockDataAsync();
    Task<List<SparePartStockEnrichedRow>> GetMasterDataAsync();
}
