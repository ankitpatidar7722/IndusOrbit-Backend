using Backend.DTOs;

namespace Backend.Services;

public interface IToolStockService
{
    Task<List<WarehouseDto>> GetWarehousesAsync();
    Task<List<WarehouseDto>> GetBinsByWarehouseAsync(string warehouseName);
    Task<ToolStockEnrichResult> EnrichStockRowsAsync(List<ToolStockEnrichRowDto> rows);
    Task<ToolStockImportResult> ImportToolStockAsync(List<ToolStockRowDto> rows);
    Task<ToolStockValidationResult> ValidateStockRowsAsync(List<ToolStockEnrichedRow> rows);
    Task<List<ToolStockEnrichedRow>> GetStockDataAsync(int toolGroupId);
    Task<List<ToolStockEnrichedRow>> GetMasterDataAsync(int toolGroupId);
}
