using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ItemController : ControllerBase
{
    private readonly IItemService _itemService;

    public ItemController(IItemService itemService)
    {
        _itemService = itemService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllItems([FromQuery] int itemGroupId)
    {
        try
        {
            if (itemGroupId <= 0)
            {
                return BadRequest(new { message = "ItemGroupId is required" });
            }

            var items = await _itemService.GetAllItemsAsync(itemGroupId);
            return Ok(items);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetItemGroups()
    {
        try
        {
            var groups = await _itemService.GetItemGroupsAsync();
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
            var hsnGroups = await _itemService.GetHSNGroupsAsync();
            return Ok(hsnGroups);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("units/{itemGroupId}")]
    public async Task<IActionResult> GetUnits(int itemGroupId)
    {
        try
        {
            if (itemGroupId <= 0)
                return BadRequest(new { message = "ItemGroupId is required" });

            var units = await _itemService.GetFieldUnitsAsync(itemGroupId);
            return Ok(units);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("item-sub-groups")]
    public async Task<IActionResult> GetItemSubGroups([FromQuery] int itemGroupId)
    {
        try
        {
            if (itemGroupId <= 0)
            {
                return BadRequest(new { message = "ItemGroupId is required" });
            }

            var itemSubGroups = await _itemService.GetItemSubGroupsAsync(itemGroupId);
            return Ok(itemSubGroups);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDeleteItem(int id)
    {
        try
        {
            var success = await _itemService.SoftDeleteItemAsync(id);
            if (success)
            {
                return Ok(new { message = "Item deleted successfully" });
            }
            return NotFound(new { message = "Item not found" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateItems([FromBody] System.Text.Json.JsonElement payload)
    {
        ImportItemsRequest request;
        try
        {
            var rawPayload = payload.GetRawText();
            // Log payload for debugging
            try 
            { 
                var preview = rawPayload.Length > 1000 ? rawPayload.Substring(0, 1000) + "..." : rawPayload;
                System.IO.File.AppendAllText("debug_log.txt", 
                    $"[{DateTime.Now}] VALIDATE Payload: {preview}\n"); 
            } 
            catch { }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | 
                                System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            options.Converters.Add(new Backend.Helpers.NullableDateTimeConverter());
            request = System.Text.Json.JsonSerializer.Deserialize<ImportItemsRequest>(rawPayload, options);
        }
        catch (System.Text.Json.JsonException jex)
        {
            var errorDetails = $"JSON Error: {jex.Message} | Path: {jex.Path} | LineNumber: {jex.LineNumber}";
            try 
            { 
                System.IO.File.AppendAllText("debug_log.txt", 
                    $"[{DateTime.Now}] VALIDATE JSON ERROR: {errorDetails}\n"); 
            } 
            catch { }
            return BadRequest(new { message = "Invalid JSON format", error = jex.Message, path = jex.Path, line = jex.LineNumber });
        }

        if (request == null || request.Items == null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "No items provided" });
        }

        if (request.ItemGroupId <= 0)
        {
            return BadRequest(new { message = "ItemGroupId is required" });
        }

        try
        {
            var result = await _itemService.ValidateItemsAsync(request.Items, request.ItemGroupId);
            return Ok(new { validationResult = result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportItems([FromBody] System.Text.Json.JsonElement payload)
    {
        ImportItemsRequest request;
        try
        {
            // Log raw payload
            var rawPayload = payload.GetRawText();
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Item Import Raw Payload: {rawPayload.Substring(0, Math.Min(500, rawPayload.Length))}...\n"); } catch { }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | 
                                System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            options.Converters.Add(new Backend.Helpers.NullableDateTimeConverter());
            request = System.Text.Json.JsonSerializer.Deserialize<ImportItemsRequest>(rawPayload, options);
            
            if (request != null)
            {
                try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Deserialized successfully. Items count: {request.Items?.Count}\n"); } catch { }
            }
            else
            {
                try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Deserialized to null!\n"); } catch { }
            }
        }
        catch (System.Text.Json.JsonException jex)
        {
            var errMsg = $"JSON Parse Error (Import): {jex.Message} at Path: {jex.Path}";
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] {errMsg}\n"); } catch { }
            return BadRequest(new { message = "Invalid JSON format", error = errMsg });
        }

        if (request == null)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Item Import Request is NULL\n"); } catch { }
            return BadRequest(new { message = "Empty request" });
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Item Import Request has EMPTY Items list\n"); } catch { }
            return BadRequest(new { message = "No items provided" });
        }

        if (request.ItemGroupId <= 0)
        {
            return BadRequest(new { message = "ItemGroupId is required" });
        }

        // Manually validate the model
        try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Item Import Request: {request?.Items?.Count} items, ItemGroupID: {request?.ItemGroupId}\n"); } catch { }

        try
        {
            // Call bulk import directly — service handles per-row skipping and error reporting
            var importResult = await _itemService.ImportItemsAsync(request.Items, request.ItemGroupId);

            if (importResult.Success)
            {
                return Ok(importResult);
            }

            return BadRequest(importResult);
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Item Import Exception: {ex.Message}\nStack: {ex.StackTrace}\n"); } catch { }
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("clear-all-data")]
    public async Task<IActionResult> ClearAllItemData([FromBody] ClearItemDataRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Username and Reason are required." });
        }

        if (request.ItemGroupId <= 0)
        {
            return BadRequest(new { message = "ItemGroupId is required." });
        }
        
        // Password is optional - default to empty string if not provided
        var password = request.Password ?? string.Empty;

        try
        {
            var deletedCount = await _itemService.ClearAllItemDataAsync(request.Username, password, request.Reason, request.ItemGroupId);
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
