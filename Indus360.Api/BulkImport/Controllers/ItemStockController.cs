using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ItemStockController : ControllerBase
{
    private readonly IItemStockService _itemStockService;

    public ItemStockController(IItemStockService itemStockService)
    {
        _itemStockService = itemStockService;
    }

    [HttpGet("warehouses")]
    public async Task<IActionResult> GetWarehouses()
    {
        try
        {
            var warehouses = await _itemStockService.GetWarehousesAsync();
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

            var bins = await _itemStockService.GetBinsByWarehouseAsync(warehouseName);
            return Ok(bins);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load bins: {ex.Message}" });
        }
    }

    [HttpGet("load")]
    public async Task<IActionResult> GetStockData([FromQuery] int itemGroupId)
    {
        try
        {
            if (itemGroupId <= 0)
                return BadRequest(new { message = "itemGroupId is required" });

            var data = await _itemStockService.GetStockDataAsync(itemGroupId);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load stock data: {ex.Message}" });
        }
    }

    [HttpPost("enrich")]
    public async Task<IActionResult> EnrichStockRows([FromBody] ItemStockEnrichRequest request)
    {
        try
        {
            if (request.ItemGroupId <= 0)
                return BadRequest(new { message = "ItemGroupId is required" });

            var result = await _itemStockService.EnrichStockRowsAsync(request.Rows, request.ItemGroupId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to enrich stock data: {ex.Message}" });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateStockRows([FromBody] ItemStockValidationRequest request)
    {
        try
        {
            if (request.ItemGroupId <= 0)
                return BadRequest(new { message = "ItemGroupId is required" });

            var result = await _itemStockService.ValidateStockRowsAsync(request.Rows, request.ItemGroupId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to validate stock data: {ex.Message}" });
        }
    }

    [HttpPost("reset-item-stock")]
    public async Task<IActionResult> ResetItemStock([FromBody] ResetStockRequest request)
    {
        try
        {
            if (request.ItemGroupId <= 0)
                return BadRequest(new { message = "itemGroupId is required" });

            if (string.IsNullOrWhiteSpace(request.Username))
                return BadRequest(new { message = "Username is required" });

            var result = await _itemStockService.ResetItemStockAsync(
                request.ItemGroupId, request.Username, request.Password, request.Reason,
                request.ItemIds.Count > 0 ? request.ItemIds : null,
                request.FromDate, request.ToDate);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Ok(new ItemStockImportResult
            {
                Success = false,
                Message = "Invalid username or password."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Reset item stock failed: {ex.Message}" });
        }
    }

    [HttpPost("reset-floor-stock")]
    public async Task<IActionResult> ResetFloorStock([FromBody] ResetStockRequest request)
    {
        try
        {
            if (request.ItemGroupId <= 0)
                return BadRequest(new { message = "itemGroupId is required" });

            if (string.IsNullOrWhiteSpace(request.Username))
                return BadRequest(new { message = "Username is required" });

            var result = await _itemStockService.ResetFloorStockAsync(
                request.ItemGroupId, request.Username, request.Password, request.Reason,
                request.FromDate, request.ToDate);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Ok(new ItemStockImportResult
            {
                Success = false,
                Message = "Invalid username or password."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Reset floor stock failed: {ex.Message}" });
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportItemStock([FromBody] ItemStockImportRequest request)
    {
        try
        {
            if (request.ItemGroupId <= 0)
                return BadRequest(new { message = "ItemGroupId is required" });

            if (request.Rows == null || request.Rows.Count == 0)
                return BadRequest(new { message = "No stock rows provided" });

            var result = await _itemStockService.ImportItemStockAsync(request.Rows, request.ItemGroupId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Stock import failed: {ex.Message}" });
        }
    }

    [HttpGet("master-data")]
    public async Task<IActionResult> GetMasterData([FromQuery] int itemGroupId)
    {
        try
        {
            if (itemGroupId <= 0)
                return BadRequest(new { message = "itemGroupId is required" });

            var data = await _itemStockService.GetMasterDataAsync(itemGroupId);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to load master data: {ex.Message}" });
        }
    }
}
