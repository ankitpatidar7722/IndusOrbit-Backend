using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SparePartController : ControllerBase
{
    private readonly ISparePartService _sparePartService;

    public SparePartController(ISparePartService sparePartService)
    {
        _sparePartService = sparePartService;
    }

    [HttpGet("hsn-groups")]
    public async Task<IActionResult> GetHSNGroups()
    {
        try
        {
            var hsnGroups = await _sparePartService.GetHSNGroupsAsync();
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
            var units = await _sparePartService.GetUnitsAsync();
            return Ok(units);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllSpareParts()
    {
        try
        {
            var spareParts = await _sparePartService.GetAllSparePartsAsync();
            return Ok(spareParts);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("soft-delete/{sparePartId}")]
    public async Task<IActionResult> SoftDeleteSparePart(int sparePartId)
    {
        try
        {
            var success = await _sparePartService.SoftDeleteSparePartAsync(sparePartId);
            if (success)
            {
                return Ok(new { message = "Spare part soft deleted successfully", sparePartId });
            }
            return NotFound(new { message = "Spare part not found" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateSpareParts([FromBody] System.Text.Json.JsonElement payload)
    {
        ValidateSparePartsRequest request;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };
            options.Converters.Add(new Backend.Helpers.NullableDateTimeConverter());
            request = System.Text.Json.JsonSerializer.Deserialize<ValidateSparePartsRequest>(payload.GetRawText(), options);
        }
        catch (System.Text.Json.JsonException jex)
        {
            var errMsg = $"JSON Parse Error: {jex.Message} at Path: {jex.Path}";
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] {errMsg}\nPayload: {payload.GetRawText().Substring(0, Math.Min(100, payload.GetRawText().Length))}...\n"); } catch { }
            return BadRequest(new { message = "Invalid JSON format", error = errMsg });
        }

        if (request == null) return BadRequest(new { message = "Empty request" });

        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values
                                    .SelectMany(v => v.Errors)
                                    .Select(e => e.ErrorMessage + " " + e.Exception?.Message));
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Validation Error: {errors}\n"); } catch { }
            return BadRequest(new { message = "Validation failed", errors = errors });
        }
        try
        {
            var validationResult = await _sparePartService.ValidateSparePartsAsync(request.SpareParts);
            return Ok(validationResult);
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Service Error: {ex.Message}\n"); } catch { }
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("clear-all-data")]
    public async Task<IActionResult> ClearAllSparePartData([FromBody] ClearSparePartDataRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Username and Reason are required." });
        }
        
        // Password is optional - default to empty string if not provided
        var password = request.Password ?? string.Empty;

        try
        {
            var deletedCount = await _sparePartService.ClearAllSparePartDataAsync(request.Username, password, request.Reason);
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

    [HttpPost("import")]
    public async Task<IActionResult> ImportSpareParts([FromBody] System.Text.Json.JsonElement payload)
    {
        ImportSparePartsRequest request;
        try
        {
            // Log raw payload
            var rawPayload = payload.GetRawText();
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Raw Payload: {rawPayload.Substring(0, Math.Min(500, rawPayload.Length))}...\n"); } catch { }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };
            options.Converters.Add(new Backend.Helpers.NullableDateTimeConverter());
            request = System.Text.Json.JsonSerializer.Deserialize<ImportSparePartsRequest>(rawPayload, options);
            
            if (request != null)
            {
                try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Deserialized successfully. SpareParts count: {request.SpareParts?.Count}\n"); } catch { }
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
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Request is NULL\n"); } catch { }
            return BadRequest(new { message = "Empty request" });
        }

        if (request.SpareParts == null || request.SpareParts.Count == 0)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Request has EMPTY SpareParts list\n"); } catch { }
            return BadRequest(new {message = "No spare parts provided" });
        }

        // Manually validate the model
        try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Request: {request?.SpareParts?.Count} spare parts\n"); } catch { }
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(request);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, validationContext, validationResults, true))
        {
            var errors = string.Join("; ", validationResults.Select(e => e.ErrorMessage));
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Model Validation Error: {errors}\n"); } catch { }
            return BadRequest(new { message = "Validation failed", errors = errors });
        }

        try
        {
            // First validate logic
            var validationResult = await _sparePartService.ValidateSparePartsAsync(request.SpareParts);

            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    message = "Validation failed. Please fix errors before importing.",
                    validationResult
                });
            }

            // Then import
            var importResult = await _sparePartService.ImportSparePartsAsync(request.SpareParts);

            if (importResult.Success)
            {
                return Ok(importResult);
            }

            return BadRequest(importResult);
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Error: {ex.Message}\nInner: {ex.InnerException?.Message}"); } catch { }
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ValidateSparePartsRequest
{
    public List<SparePartMasterDto> SpareParts { get; set; } = new();
}

public class ImportSparePartsRequest
{
    public List<SparePartMasterDto> SpareParts { get; set; } = new();
}

public class ClearSparePartDataRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
