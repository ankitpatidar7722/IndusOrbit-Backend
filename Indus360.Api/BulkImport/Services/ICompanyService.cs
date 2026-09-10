using Backend.DTOs;

namespace Backend.Services;

public interface ICompanyService
{
    Task<CompanyDto> GetCompanyAsync();
    Task<bool> UpdateCompanyAsync(CompanyDto company);
}
