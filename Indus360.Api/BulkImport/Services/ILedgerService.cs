using Backend.DTOs;

namespace Backend.Services;

public interface ILedgerService
{
    Task<List<LedgerMasterDto>> GetLedgersByGroupAsync(int ledgerGroupId);
    Task<bool> SoftDeleteLedgerAsync(int ledgerId);
    Task<LedgerValidationResultDto> ValidateLedgersAsync(List<LedgerMasterDto> ledgers, int ledgerGroupId);
    Task<ImportResultDto> ImportLedgersAsync(List<LedgerMasterDto> ledgers, int ledgerGroupId);
    Task<List<CountryStateDto>> GetCountryStatesAsync();
    Task<int> ClearAllLedgerDataAsync(int ledgerGroupId, string username, string password, string reason);
    Task<List<SalesRepresentativeDto>> GetSalesRepresentativesAsync();
    Task<List<DepartmentDto>> GetDepartmentsAsync();
    Task<List<ClientDto>> GetClientsAsync();
}
