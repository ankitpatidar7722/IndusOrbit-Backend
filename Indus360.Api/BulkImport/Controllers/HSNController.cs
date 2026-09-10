using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class HSNController : ControllerBase
{
    private readonly IHSNService _hsnService;

    public HSNController(IHSNService hsnService)
    {
        _hsnService = hsnService;
    }

    [HttpGet("itemgroups")]
    public async Task<IActionResult> GetItemGroups([FromQuery] int companyId = 2)
    {
        try
        {
            var result = await _hsnService.GetItemGroupsAsync(companyId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetHSNs([FromQuery] int companyId = 2)
    {
        try
        {
            var result = await _hsnService.GetHSNListAsync(companyId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportHSNs([FromBody] List<HSNMasterDto> hsns)
    {
        try
        {
            // First validate
            var validationResult = await _hsnService.ValidateHSNsAsync(hsns);
            if (!validationResult.IsValid)
            {
                // In Import flow, we usually just return the failing validation result if we want strict blocking,
                // BUT the requirement is to handle like Ledger Master: It likely accepts the validated list and inserts valid ones.
                // However, usually we post "raw" data, get validation, then user fixes and posts again.
                // OR we post "valid" rows only.
                
                // Let's assume this endpoint is for FINAL import of validated data.
                // Re-validating for safety.
                
                // If the user wants to Import, we should only import VALID rows.
                var validRows = validationResult.Rows.Where(r => r.RowStatus == ValidationStatus.Valid).Select(r => r.Data).ToList();
                if (validRows.Count == 0 && hsns.Count > 0)
                {
                    return BadRequest(new { message = "No valid rows to import.", validation = validationResult });
                }
                
                var result = await _hsnService.ImportHSNsAsync(validRows, 2); // Default User 2
                return Ok(result);
            }
            
            var importResult = await _hsnService.ImportHSNsAsync(hsns, 2);
            return Ok(importResult);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] List<HSNMasterDto> hsns)
    {
        try
        {
            var result = await _hsnService.ValidateHSNsAsync(hsns);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("clear")]
    public async Task<IActionResult> ClearHSNData([FromBody] ClearHSNDataRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Username and Reason are required." });
        }

        try
        {
            var result = await _hsnService.ClearHSNDataAsync(request.CompanyId, request.Username, request.Password, request.Reason);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpDelete("delete/{id}")]
    public async Task<IActionResult> DeleteHSN(int id)
    {
        try
        {
            var result = await _hsnService.SoftDeleteHSNAsync(id, 2);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
