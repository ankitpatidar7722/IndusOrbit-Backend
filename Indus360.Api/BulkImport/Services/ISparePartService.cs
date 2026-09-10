using Backend.DTOs;

namespace Backend.Services;

public interface ISparePartService
{
    Task<List<SparePartMasterDto>> GetAllSparePartsAsync();
    Task<bool> SoftDeleteSparePartAsync(int sparePartId);
    Task<SparePartValidationResultDto> ValidateSparePartsAsync(List<SparePartMasterDto> spareParts);
    Task<ImportResultDto> ImportSparePartsAsync(List<SparePartMasterDto> spareParts);
    Task<List<HSNGroupDto>> GetHSNGroupsAsync();
    Task<List<UnitDto>> GetUnitsAsync();
    Task<int> ClearAllSparePartDataAsync(string username, string password, string reason);
}
