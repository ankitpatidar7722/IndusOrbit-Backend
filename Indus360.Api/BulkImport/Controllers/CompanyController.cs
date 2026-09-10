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
        try {
             System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] CompanyController Initialized\n");
        } catch {}
    }

    [HttpGet]
    public async Task<IActionResult> GetCompany()
    {
        try
        {
            System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] GET Request Received\n");
            var company = await _companyService.GetCompanyAsync();
            return Ok(company);
        }
        catch (Exception ex)
        {
             var errorMsg = $"[{DateTime.Now}] GET Failed: {ex.Message}\nStackTrace: {ex.StackTrace}\nInner: {ex.InnerException?.Message}\n\n";
             System.IO.File.AppendAllText("debug_log.txt", errorMsg);
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

        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Update Request Received for ID: {company.CompanyId}\n");
        try
        {
            Console.WriteLine($"[DEBUG] Updating Company: {company.CompanyId} - {company.CompanyName}");
            var result = await _companyService.UpdateCompanyAsync(company);
            if (result)
            {
                return Ok(new { message = "Company updated successfully" });
            }
            return BadRequest(new { error = "Failed to update company" });
        }
        catch (Exception ex)
        {
            var errorMsg = $"[{DateTime.Now}] Update Failed: {ex.Message}\nStackTrace: {ex.StackTrace}\nInner: {ex.InnerException?.Message}\n\n";
            System.IO.File.AppendAllText("debug_log.txt", errorMsg); // LOG TO DEBUG FILE
            
            Console.WriteLine($"[ERROR] Update Failed: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
