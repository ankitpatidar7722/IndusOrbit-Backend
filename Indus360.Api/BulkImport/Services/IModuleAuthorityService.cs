using Backend.DTOs;

namespace Backend.Services;

public interface IModuleAuthorityService
{
    // ── Legacy ERP module authority (within a client DB) ──────────────────────
    Task<List<ModuleAuthorityRowDto>> GetModuleAuthorityDataAsync(string product);
    Task<object> SaveModuleAuthorityAsync(List<ModuleAuthoritySaveDto> modules, string product);

    // ── Indus Tool Module Authority (sidebar access per company) ──────────────
    Task<List<IndusToolModuleDto>> GetModulesForCompanyAsync(string companyUserID);
    Task<ModuleAuthorityResult> SaveCompanyModuleAuthorityAsync(SaveModuleAuthorityRequest request);
}
