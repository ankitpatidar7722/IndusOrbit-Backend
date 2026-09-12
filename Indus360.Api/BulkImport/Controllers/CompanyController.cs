using Backend.DTOs;
using Backend.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CompanyController : ControllerBase
{
    private readonly ICompanyService _companyService;
    public CompanyController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCompany()
    {
        try
        {
            var company = await _companyService.GetCompanyAsync();
            return Ok(company);
        }
        catch (Exception ex)
        {
             return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateCompany([FromBody] CompanyDto? company)
    {
        if (company == null)
        {
            return BadRequest(new { error = "Company data is required" });
        }

        try
        {
            var result = await _companyService.UpdateCompanyAsync(company);
            if (result)
            {
                return Ok(new { message = "Company updated successfully" });
            }
            return BadRequest(new { error = "Failed to update company" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Update Failed: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
