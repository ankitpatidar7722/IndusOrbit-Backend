using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ToolController : ControllerBase
{
    private readonly IToolService _toolService;

    public ToolController(IToolService toolService)
    {
        _toolService = toolService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllTools([FromQuery] int toolGroupId)
    {
        try
        {
            if (toolGroupId <= 0)
                return BadRequest(new { message = "ToolGroupId is required" });

            var tools = await _toolService.GetAllToolsAsync(toolGroupId);
            return Ok(tools);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetToolGroups()
    {
        try
        {
            var groups = await _toolService.GetToolGroupsAsync();
            return Ok(groups);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("hsn-groups")]
    public async Task<IActionResult> GetHSNGroups()
    {
        try
        {
            var hsnGroups = await _toolService.GetToolHSNGroupsAsync();
            return Ok(hsnGroups);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("units")]
    public async Task<IActionResult> GetUnits()
    {
        try
        {
            var units = await _toolService.GetToolUnitsAsync();
            return Ok(units);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDeleteTool(int id)
    {
        try
        {
            var success = await _toolService.SoftDeleteToolAsync(id);
            if (success)
                return Ok(new { message = "Tool deleted successfully" });
            return NotFound(new { message = "Tool not found" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("validate")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ValidateTools([FromBody] ImportToolsRequest request)
    {
        if (request == null || request.Tools == null || request.Tools.Count == 0)
            return BadRequest(new { message = "No tools provided" });

        if (request.ToolGroupId <= 0)
            return BadRequest(new { message = "ToolGroupId is required" });

        try
        {
            var result = await _toolService.ValidateToolsAsync(request.Tools, request.ToolGroupId);
            return Ok(new { validationResult = result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("import")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ImportTools([FromBody] ImportToolsRequest request)
    {
        if (request == null || request.Tools == null || request.Tools.Count == 0)
            return BadRequest(new { message = "No tools provided" });

        if (request.ToolGroupId <= 0)
            return BadRequest(new { message = "ToolGroupId is required" });

        try
        {
            // First validate
            var validationResult = await _toolService.ValidateToolsAsync(request.Tools, request.ToolGroupId);

            if (!validationResult.IsValid)
                return BadRequest(new { message = "Validation failed. Please fix errors before importing.", validationResult });

            // Then import
            var importResult = await _toolService.ImportToolsAsync(request.Tools, request.ToolGroupId);

            if (importResult.Success)
                return Ok(importResult);

            return BadRequest(importResult);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("clear-all-data")]
    public async Task<IActionResult> ClearAllToolData([FromBody] ClearToolDataRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Username and Reason are required." });

        if (request.ToolGroupId <= 0)
            return BadRequest(new { message = "ToolGroupId is required." });

        var password = request.Password ?? string.Empty;

        try
        {
            var deletedCount = await _toolService.ClearAllToolDataAsync(request.Username, password, request.Reason, request.ToolGroupId);
            return Ok(new { message = "All data cleared successfully.", deletedCount });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
