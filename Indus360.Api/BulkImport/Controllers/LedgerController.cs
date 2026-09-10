using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class LedgerController : ControllerBase
{
    private readonly ILedgerService _ledgerService;

    public LedgerController(ILedgerService ledgerService)
    {
        _ledgerService = ledgerService;
    }

    [HttpGet("country-states")]
    public async Task<IActionResult> GetCountryStates()
    {
        try
        {
            var countryStates = await _ledgerService.GetCountryStatesAsync();
            return Ok(countryStates);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("sales-representatives")]
    public async Task<IActionResult> GetSalesRepresentatives()
    {
        try
        {
            var salesRepresentatives = await _ledgerService.GetSalesRepresentativesAsync();
            return Ok(salesRepresentatives);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments()
    {
        try
        {
            var departments = await _ledgerService.GetDepartmentsAsync();
            return Ok(departments);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("bygroup/{ledgerGroupId}")]
    public async Task<IActionResult> GetLedgersByGroup(int ledgerGroupId)
    {
        try
        {
            var ledgers = await _ledgerService.GetLedgersByGroupAsync(ledgerGroupId);
            return Ok(ledgers);
        }
        catch (Exception ex)
        {
            // Enhanced error logging
            try
            {
                var logMsg = $"[{DateTime.Now}] GetLedgersByGroup Error (GroupID: {ledgerGroupId}): {ex.Message}\nStack: {ex.StackTrace}\nInner: {ex.InnerException?.Message}\n";
                System.IO.File.AppendAllText("debug_log.txt", logMsg);
            }
            catch { }
            return StatusCode(500, new { error = ex.Message, details = ex.InnerException?.Message });
        }
    }

    [HttpGet("clients")]
    public async Task<IActionResult> GetClients()
    {
        try
        {
            var clients = await _ledgerService.GetClientsAsync();
            return Ok(clients);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("soft-delete/{ledgerId}")]
    public async Task<IActionResult> SoftDeleteLedger(int ledgerId)
    {
        try
        {
            var success = await _ledgerService.SoftDeleteLedgerAsync(ledgerId);
            if (success)
            {
                return Ok(new { message = "Ledger soft deleted successfully", ledgerId });
            }
            return NotFound(new { message = "Ledger not found" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateLedgers([FromBody] System.Text.Json.JsonElement payload)
    {
        ValidateLedgersRequest request;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };
            options.Converters.Add(new Backend.Helpers.NullableDateTimeConverter());
            request = System.Text.Json.JsonSerializer.Deserialize<ValidateLedgersRequest>(payload.GetRawText(), options);
        }
        catch (System.Text.Json.JsonException jex)
        {
             var errMsg = $"JSON Parse Error: {jex.Message} at Path: {jex.Path}";
             try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] {errMsg}\nPayload: {payload.GetRawText().Substring(0, Math.Min(100, payload.GetRawText().Length))}...\n"); } catch {}
             return BadRequest(new { message = "Invalid JSON format", error = errMsg });
        }

        if (request == null) return BadRequest(new { message = "Empty request" });

        if (!ModelState.IsValid)
        {
             var errors = string.Join("; ", ModelState.Values
                                    .SelectMany(v => v.Errors)
                                    .Select(e => e.ErrorMessage + " " + e.Exception?.Message));
             try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Validation Error: {errors}\n"); } catch {}
             return BadRequest(new { message = "Validation failed", errors = errors });
        }
        try
        {
            var validationResult = await _ledgerService.ValidateLedgersAsync(request.Ledgers, request.LedgerGroupId);
            return Ok(validationResult);
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Service Error: {ex.Message}\n"); } catch {}
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("clear-all-data")]
    public async Task<IActionResult> ClearAllLedgerData([FromBody] ClearLedgerDataRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Username and Reason are required." });
        }

        try
        {
            var deletedCount = await _ledgerService.ClearAllLedgerDataAsync(request.LedgerGroupId, request.Username, request.Password, request.Reason);
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
    public async Task<IActionResult> ImportLedgers([FromBody] System.Text.Json.JsonElement payload)
    {
        ImportLedgersRequest request;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };
            options.Converters.Add(new Backend.Helpers.NullableDateTimeConverter());
            request = System.Text.Json.JsonSerializer.Deserialize<ImportLedgersRequest>(payload.GetRawText(), options);
        }
        catch (System.Text.Json.JsonException jex)
        {
             var errMsg = $"JSON Parse Error (Import): {jex.Message} at Path: {jex.Path}";
             try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] {errMsg}\n"); } catch {}
             return BadRequest(new { message = "Invalid JSON format", error = errMsg });
        }

        if (request == null) return BadRequest(new { message = "Empty request" });

        // Manually validate the model (since we bypassed automatic validation)
        try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Request: {request?.Ledgers?.Count} ledgers, Group: {request?.LedgerGroupId}\n"); } catch {}
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(request);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, validationContext, validationResults, true))
        {
             var errors = string.Join("; ", validationResults.Select(e => e.ErrorMessage));
             try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Model Validation Error: {errors}\n"); } catch {}
             return BadRequest(new { message = "Validation failed", errors = errors });
        }

        try
        {
            var importResult = await _ledgerService.ImportLedgersAsync(request.Ledgers, request.LedgerGroupId);
            
            if (importResult.Success)
            {
                return Ok(importResult);
            }
            
            return BadRequest(importResult);
        }
        catch (Exception ex)
        {
             try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Import Error: {ex.Message}\nInner: {ex.InnerException?.Message}"); } catch {}
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ValidateLedgersRequest
{
    public List<LedgerMasterDto> Ledgers { get; set; } = new();
    public int LedgerGroupId { get; set; }
}

public class ImportLedgersRequest
{
    public List<LedgerMasterDto> Ledgers { get; set; } = new();
    public int LedgerGroupId { get; set; }
}
