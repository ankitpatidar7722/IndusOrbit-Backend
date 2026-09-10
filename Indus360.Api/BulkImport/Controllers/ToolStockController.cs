using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ToolStockController : ControllerBase
{
    private readonly IToolStockService _service;

    public ToolStockController(IToolStockService service)
    {
        _service = service;
    }

    [HttpGet("warehouses")]
    public async Task<IActionResult> GetWarehouses()
    {
        try
        {
            var warehouses = await _service.GetWarehousesAsync();
            return Ok(warehouses);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load warehouses: {ex.Message}" });
        }
    }

    [HttpGet("bins")]
    public async Task<IActionResult> GetBins([FromQuery] string warehouseName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(warehouseName))
                return BadRequest(new { message = "warehouseName is required" });

            var bins = await _service.GetBinsByWarehouseAsync(warehouseName);
            return Ok(bins);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load bins: {ex.Message}" });
        }
    }

    [HttpGet("load")]
    public async Task<IActionResult> GetStockData([FromQuery] int toolGroupId)
    {
        try
        {
            if (toolGroupId <= 0)
                return BadRequest(new { message = "toolGroupId is required" });

            var data = await _service.GetStockDataAsync(toolGroupId);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load stock data: {ex.Message}" });
        }
    }

    [HttpPost("enrich")]
    public async Task<IActionResult> EnrichStockRows([FromBody] ToolStockEnrichRequest request)
    {
        try
        {
            var result = await _service.EnrichStockRowsAsync(request.Rows);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to enrich stock data: {ex.Message}" });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateStockRows([FromBody] ToolStockValidationRequest request)
    {
        try
        {
            var result = await _service.ValidateStockRowsAsync(request.Rows);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to validate stock data: {ex.Message}" });
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportToolStock([FromBody] ToolStockImportRequest request)
    {
        try
        {
            if (request.Rows == null || request.Rows.Count == 0)
                return BadRequest(new { message = "No stock rows provided" });

            var result = await _service.ImportToolStockAsync(request.Rows);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Stock import failed: {ex.Message}" });
        }
    }

    [HttpGet("master-data")]
    public async Task<IActionResult> GetMasterData([FromQuery] int toolGroupId)
    {
        try
        {
            if (toolGroupId <= 0)
                return BadRequest(new { message = "toolGroupId is required" });

            var data = await _service.GetMasterDataAsync(toolGroupId);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load master data: {ex.Message}" });
        }
    }
}
