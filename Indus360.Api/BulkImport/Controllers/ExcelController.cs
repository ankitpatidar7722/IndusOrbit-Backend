using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ExcelController : ControllerBase
{
    private readonly IExcelService _excelService;
    private readonly ILogger<ExcelController> _logger;

    public ExcelController(IExcelService excelService, ILogger<ExcelController> logger)
    {
        _excelService = excelService;
        _logger = logger;
    }

    [HttpPost("Preview")]
    public async Task<IActionResult> PreviewExcel(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded." });
            }

            // Validate file extension - EPPlus only supports .xlsx (not old .xls format)
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".xlsx")
            {
                return BadRequest(new { error = "Only .xlsx Excel files are supported. Please convert old .xls files to .xlsx format." });
            }

            using var stream = file.OpenReadStream();
            var preview = await _excelService.PreviewExcelAsync(stream);
            
            return Ok(preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing Excel file");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("Import")]
    public async Task<IActionResult> ImportExcel(IFormFile file, [FromQuery] string tableName, [FromQuery] int? subModuleId = null)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded." });
            }

            if (string.IsNullOrWhiteSpace(tableName))
            {
                return BadRequest(new { error = "Table name is required." });
            }

            // Validate file extension - EPPlus only supports .xlsx (not old .xls format)
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".xlsx")
            {
                return BadRequest(new { error = "Only .xlsx Excel files are supported. Please convert old .xls files to .xlsx format." });
            }

            using var stream = file.OpenReadStream();
            var result = await _excelService.ImportExcelAsync(stream, tableName, subModuleId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing Excel file");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("LedgerGroups")]
    public async Task<IActionResult> GetLedgerGroups()
    {
        try
        {
            var ledgerGroups = await _excelService.GetLedgerGroupsAsync();
            return Ok(ledgerGroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ledger groups");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("MasterColumns/{ledgerGroupId}")]
    public async Task<IActionResult> GetMasterColumns(int ledgerGroupId)
    {
        try
        {
            var columns = await _excelService.GetMasterColumnsAsync(ledgerGroupId);
            return Ok(columns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching master columns");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("ImportLedger")]
    public async Task<IActionResult> ImportLedger(IFormFile file, [FromQuery] int ledgerGroupId)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded." });
            }

            if (ledgerGroupId <= 0)
            {
                return BadRequest(new { error = "Valid Ledger Group ID is required." });
            }

            // Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".xlsx")
            {
                return BadRequest(new { error = "Only .xlsx Excel files are supported." });
            }

            using var stream = file.OpenReadStream();
            var result = await _excelService.ImportLedgerMasterWithValidationAsync(stream, ledgerGroupId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing ledger data");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ==========================================
    // ITEM MASTER ENDPOINTS
    // ==========================================

    [HttpGet("ItemGroups")]
    public async Task<IActionResult> GetItemGroups()
    {
        try
        {
            var itemGroups = await _excelService.GetItemGroupsAsync();
            return Ok(itemGroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching item groups");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("ItemMasterColumns/{itemGroupId}")]
    public async Task<IActionResult> GetItemMasterColumns(int itemGroupId)
    {
        try
        {
            var columns = await _excelService.GetItemMasterColumnsAsync(itemGroupId);
            return Ok(columns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching item master columns");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("ImportItem")]
    public async Task<IActionResult> ImportItemMaster(IFormFile file, [FromQuery] int itemGroupId)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded." });
            }

            if (itemGroupId <= 0)
            {
                return BadRequest(new { error = "Valid Item Group ID is required." });
            }

            // Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".xlsx")
            {
                return BadRequest(new { error = "Only .xlsx Excel files are supported." });
            }

            using var stream = file.OpenReadStream();
            var result = await _excelService.ImportItemMasterWithValidationAsync(stream, itemGroupId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing item data");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ==========================================
    // TOOL MASTER ENDPOINTS
    // ==========================================

    [HttpGet("ToolGroups")]
    public async Task<IActionResult> GetToolGroups()
    {
        try
        {
            var toolGroups = await _excelService.GetToolGroupsAsync();
            return Ok(toolGroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tool groups");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("ImportTool")]
    public async Task<IActionResult> ImportToolMaster(IFormFile file, [FromQuery] int toolGroupId)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded." });
            }

            if (toolGroupId <= 0)
            {
                return BadRequest(new { error = "Valid Tool Group ID is required." });
            }

            // Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".xlsx")
            {
                return BadRequest(new { error = "Only .xlsx Excel files are supported." });
            }

            using var stream = file.OpenReadStream();
            var result = await _excelService.ImportToolMasterAsync(stream, "Tool Master", toolGroupId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing tool data");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

