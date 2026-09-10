using Backend.Services;
using Backend.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ModuleAuthorityController : ControllerBase
{
    private readonly IModuleAuthorityService _service;
    private readonly ILogger<ModuleAuthorityController> _logger;

    public ModuleAuthorityController(IModuleAuthorityService service, ILogger<ModuleAuthorityController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Fetches source modules and compares with login DB to determine status.</summary>
    [HttpGet("GetData")]
    public async Task<IActionResult> GetModuleAuthorityData([FromQuery] string product = "Estimoprime")
    {
        try
        {
            var data = await _service.GetModuleAuthorityDataAsync(product);
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching module authority data");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Saves Module Authority changes (insert/enable/disable).</summary>
    [HttpPost("Save")]
    public async Task<IActionResult> SaveModuleAuthority([FromBody] ModuleAuthoritySaveRequest request)
    {
        try
        {
            var result = await _service.SaveModuleAuthorityAsync(request.Modules, request.Product);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving module authority data");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns all Indus Tool modules with IsEnabled flag for the given company.</summary>
    [HttpGet("indus-tools/{companyUserID}")]
    public async Task<IActionResult> GetModulesForCompany(string companyUserID)
    {
        try
        {
            var data = await _service.GetModulesForCompanyAsync(companyUserID);
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching indus tool modules for {CompanyUserID}", companyUserID);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Saves which Indus Tool modules are enabled for a company.</summary>
    [HttpPost("indus-tools")]
    public async Task<IActionResult> SaveCompanyModuleAuthority([FromBody] SaveModuleAuthorityRequest request)
    {
        try
        {
            var result = await _service.SaveCompanyModuleAuthorityAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving indus tool module authority for {CompanyUserID}", request.CompanyUserID);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
