using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.DTOs;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ModuleController : ControllerBase
{
    private readonly IModuleService _moduleService;
    private readonly ILogger<ModuleController> _logger;

    public ModuleController(IModuleService moduleService, ILogger<ModuleController> logger)
    {
        _moduleService = moduleService;
        _logger = logger;
    }

    [HttpGet("GetModules")]
    public async Task<IActionResult> GetModules([FromQuery] string headName = "Masters")
    {
        try
        {
            if (headName == "ALL")
            {
                var all = await _moduleService.GetAllModulesAsync();
                return Ok(all);
            }

            var modules = await _moduleService.GetModulesByHeadNameAsync(headName);
            return Ok(modules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching modules");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("GetAll")]
    public async Task<IActionResult> GetAllModules()
    {
        try
        {
            var modules = await _moduleService.GetAllModulesAsync();
            return Ok(modules);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("DebugDB")]
    public async Task<IActionResult> DebugDB()
    {
        try
        {
            var result = await _moduleService.GetDebugModuleData();
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { Error = ex.Message });
        }
    }

    [HttpPost("Create")]
    public async Task<IActionResult> CreateModule([FromBody] ModuleDto module)
    {
        try
        {
            // If display order already exists, shift existing ones first
            if (module.SetGroupIndex.HasValue && module.ModuleHeadDisplayOrder.HasValue)
            {
                bool orderExists = await _moduleService.CheckDisplayOrderExistsAsync(
                    module.ModuleHeadDisplayOrder.Value, module.SetGroupIndex.Value);
                if (orderExists)
                {
                    await _moduleService.ShiftDisplayOrdersAsync(
                        module.ModuleHeadDisplayOrder.Value, module.SetGroupIndex.Value);
                }
            }

            // ModuleDisplayOrder mirrors ModuleHeadDisplayOrder
            module.ModuleDisplayOrder = module.ModuleHeadDisplayOrder;

            var id = await _moduleService.CreateModuleAsync(module);
            return Ok(new { Message = "Module created successfully", ModuleId = id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPut("Update")]
    public async Task<IActionResult> UpdateModule([FromBody] ModuleDto module)
    {
        try
        {
            var success = await _moduleService.UpdateModuleAsync(module);
            if (success) return Ok(new { Message = "Module updated successfully" });
            return BadRequest(new { Message = "Failed to update module" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("Delete/{id}")]
    public async Task<IActionResult> DeleteModule(int id)
    {
        try
        {
            var success = await _moduleService.DeleteModuleAsync(id);
            if (success) return Ok(new { Message = "Module deleted successfully" });
            return BadRequest(new { Message = "Failed to delete module" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("GetHeads")]
    public async Task<IActionResult> GetModuleHeads()
    {
        try
        {
            var heads = await _moduleService.GetUniqueModuleHeadsAsync();
            return Ok(heads);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // Create Module Form Endpoints
    // ─────────────────────────────────────────────

    /// <summary>Returns distinct module names from IndusEnterpriseDemo for the searchable dropdown.</summary>
    [HttpGet("IndusModuleNames")]
    public async Task<IActionResult> GetIndusModuleNames()
    {
        try
        {
            var names = await _moduleService.GetIndusModuleNamesAsync();
            return Ok(names);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Indus module names");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns full module details from IndusEnterpriseDemo for interconnected dropdown filtering.</summary>
    [HttpGet("IndusModules")]
    public async Task<IActionResult> GetIndusModules()
    {
        try
        {
            var modules = await _moduleService.GetIndusModulesAsync();
            return Ok(modules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching full Indus modules list");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns auto-fill data for a given module name from IndusEnterpriseDemo.</summary>
    [HttpGet("IndusModuleInfo")]
    public async Task<IActionResult> GetIndusModuleInfo([FromQuery] string moduleName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(moduleName))
                return BadRequest(new { error = "moduleName is required" });

            var info = await _moduleService.GetIndusModuleInfoAsync(moduleName);
            if (info == null)
                return NotFound(new { error = $"Module '{moduleName}' not found in IndusEnterpriseDemo" });

            return Ok(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Indus module info for {ModuleName}", moduleName);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns default system values: CompanyID, UserID (admin), FYear.</summary>
    [HttpGet("SystemDefaults")]
    public async Task<IActionResult> GetSystemDefaults()
    {
        try
        {
            var defaults = await _moduleService.GetSystemDefaultsAsync();
            return Ok(defaults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system defaults");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns the next available display order for a given SetGroupIndex.</summary>
    [HttpGet("NextDisplayOrder")]
    public async Task<IActionResult> GetNextDisplayOrder([FromQuery] int setGroupIndex)
    {
        try
        {
            var next = await _moduleService.GetNextDisplayOrderAsync(setGroupIndex);
            return Ok(new { nextOrder = next });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns { exists: bool } – whether a module name already exists in the current DB.</summary>
    [HttpGet("CheckModuleExists")]
    public async Task<IActionResult> CheckModuleExists([FromQuery] string moduleName)
    {
        try
        {
            var exists = await _moduleService.CheckModuleExistsAsync(moduleName);
            return Ok(new { exists });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Returns { exists: bool } – whether a display order is taken for this SetGroupIndex.</summary>
    [HttpGet("CheckDisplayOrderExists")]
    public async Task<IActionResult> CheckDisplayOrderExists([FromQuery] int order, [FromQuery] int setGroupIndex)
    {
        try
        {
            var exists = await _moduleService.CheckDisplayOrderExistsAsync(order, setGroupIndex);
            return Ok(new { exists });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("CheckGroupIndexInUse")]
    public async Task<IActionResult> CheckGroupIndexInUse([FromQuery] int groupIndex, [FromQuery] string headName)
    {
        try
        {
            var inUse = await _moduleService.CheckGroupIndexInUseAsync(groupIndex, headName);
            return Ok(new { inUse });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("ItemGroupComparison")]
    public async Task<IActionResult> GetItemGroupComparison([FromQuery] string type = "Item")
    {
        try
        {
            var result = await _moduleService.GetItemGroupComparisonAsync(type);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching {type} comparison");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("SyncItemGroups")]
    public async Task<IActionResult> SyncItemGroups([FromBody] List<ItemGroupComparisonDto> syncData, [FromQuery] string type = "Item")
    {
        try
        {
            var success = await _moduleService.SyncItemGroupsAsync(type, syncData);
            if (success) return Ok(new { Message = $"{type} groups synchronized successfully" });
            return BadRequest(new { Message = $"Failed to synchronize {type} groups" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error syncing {type} groups");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    // ─────────────────────────────────────────────
    // Client-DB-Aware Endpoints (for CompanySubscription "New Module Addition" Tab)
    // These operate on the specified client's database, not the admin DB.
    // ─────────────────────────────────────────────

    [HttpPost("GetModulesForClient")]
    public async Task<IActionResult> GetModulesForClient([FromBody] GetModulesForClientRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return BadRequest(new { error = "connectionString is required" });

            await using var conn = GetClientConnection(request.ConnectionString);
            var query = @"
                SELECT 
                    ModuleId, ModuleName, ModuleHeadName, ModuleDisplayName,
                    ModuleHeadDisplayName, ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex
                FROM ModuleMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0
                ORDER BY ModuleHeadName, ModuleDisplayName";
            var modules = await conn.QueryAsync<ModuleDto>(query);
            return Ok(modules);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class GetModulesForClientRequest
    {
        public string ConnectionString { get; set; } = string.Empty;
    }

    [HttpGet("ItemGroupComparisonForClient")]
    public async Task<IActionResult> GetItemGroupComparisonForClient([FromQuery] string type, [FromQuery] string connectionString)
    {
        try
        {
            var result = await _moduleService.GetItemGroupComparisonForClientAsync(type, connectionString);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("SyncItemGroupsForClient")]
    public async Task<IActionResult> SyncItemGroupsForClient([FromBody] SyncForClientRequest request)
    {
        try
        {
            var success = await _moduleService.SyncItemGroupsForClientAsync(request.Type, request.SyncData, request.ConnectionString);
            if (success) return Ok(new { Message = "Groups synchronized successfully" });
            return BadRequest(new { Message = "Failed to synchronize groups" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class SyncForClientRequest
    {
        public string Type { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public List<ItemGroupComparisonDto> SyncData { get; set; } = new();
    }

    [HttpPut("UpdateForClient")]
    public async Task<IActionResult> UpdateModuleForClient([FromBody] CreateModuleForClientRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return BadRequest(new { error = "connectionString is required" });

            await using var conn = GetClientConnection(request.ConnectionString);
            var module = request.Module;

            var updateQuery = @"
                UPDATE ModuleMaster SET
                    ModuleName = @ModuleName,
                    ModuleHeadName = @ModuleHeadName,
                    ModuleDisplayName = @ModuleDisplayName,
                    ModuleHeadDisplayName = @ModuleHeadDisplayName,
                    ModuleHeadDisplayOrder = @ModuleHeadDisplayOrder,
                    ModuleDisplayOrder = @ModuleDisplayOrder,
                    SetGroupIndex = @SetGroupIndex
                WHERE ModuleID = @ModuleId";

            await conn.ExecuteAsync(updateQuery, module);
            return Ok(new { Message = "Module updated successfully in client database." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("DeleteForClient")]
    public async Task<IActionResult> DeleteModuleForClient([FromQuery] int moduleId, [FromQuery] string connectionString)
    {
        try
        {
            await using var conn = GetClientConnection(connectionString);
            await conn.ExecuteAsync(
                "UPDATE ModuleMaster SET IsDeletedTransaction = 1 WHERE ModuleID = @moduleId",
                new { moduleId });
            return Ok(new { Message = "Module deleted successfully from client database." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static SqlConnection GetClientConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.");

        if (!connectionString.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
        {
            if (!connectionString.TrimEnd().EndsWith(";")) connectionString += ";";
            connectionString += "TrustServerCertificate=True;";
        }
        return new SqlConnection(connectionString);
    }

    [HttpGet("GetSystemDefaultsForClient")]
    public async Task<IActionResult> GetSystemDefaultsForClient([FromQuery] string connectionString)
    {
        try
        {
            await using var conn = GetClientConnection(connectionString);
            var companyId = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction = 0 ORDER BY CompanyID DESC");
            var userId = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT TOP 1 UserID FROM UserMaster WHERE UserName = 'admin' AND ISNULL(IsBlocked, 0) = 0");
            var now = DateTime.Now;
            var fYear = now.Month >= 4 ? $"{now.Year}-{now.Year + 1}" : $"{now.Year - 1}-{now.Year}";
            return Ok(new { companyID = companyId, userID = userId, fYear });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("GetIndusModuleInfoForClient")]
    public async Task<IActionResult> GetIndusModuleInfoForClient([FromQuery] string moduleName, [FromQuery] string connectionString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(moduleName)) return BadRequest(new { error = "moduleName is required" });
            var info = await _moduleService.GetIndusModuleInfoForClientAsync(moduleName, connectionString);
            if (info == null) return NotFound(new { error = $"Module '{moduleName}' not found in IndusEnterpriseDemo" });
            return Ok(info);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("CheckModuleExistsForClient")]
    public async Task<IActionResult> CheckModuleExistsForClient([FromQuery] string moduleName, [FromQuery] string connectionString)
    {
        try
        {
            await using var conn = GetClientConnection(connectionString);
            var count = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM ModuleMaster WHERE ModuleName = @ModuleName AND IsDeletedTransaction = 0",
                new { ModuleName = moduleName });
            return Ok(new { exists = count > 0 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("CheckDisplayOrderExistsForClient")]
    public async Task<IActionResult> CheckDisplayOrderExistsForClient([FromQuery] int order, [FromQuery] int setGroupIndex, [FromQuery] string connectionString)
    {
        try
        {
            await using var conn = GetClientConnection(connectionString);
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ModuleMaster WHERE ModuleHeadDisplayOrder = @order AND SetGroupIndex = @setGroupIndex",
                new { order, setGroupIndex });
            return Ok(new { exists = count > 0 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("CheckGroupIndexInUseForClient")]
    public async Task<IActionResult> CheckGroupIndexInUseForClient([FromQuery] int groupIndex, [FromQuery] string headName, [FromQuery] string connectionString)
    {
        try
        {
            await using var conn = GetClientConnection(connectionString);
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ModuleMaster WHERE SetGroupIndex = @groupIndex AND ModuleHeadName != @headName",
                new { groupIndex, headName = headName ?? "" });
            return Ok(new { inUse = count > 0 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("CreateForClient")]
    public async Task<IActionResult> CreateModuleForClient([FromBody] CreateModuleForClientRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return BadRequest(new { error = "connectionString is required" });

            await using var conn = GetClientConnection(request.ConnectionString);
            var module = request.Module;

            // Shift display orders if needed
            if (module.SetGroupIndex.HasValue && module.ModuleHeadDisplayOrder.HasValue)
            {
                var orderCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM ModuleMaster WHERE ModuleHeadDisplayOrder = @order AND SetGroupIndex = @sg",
                    new { order = module.ModuleHeadDisplayOrder.Value, sg = module.SetGroupIndex.Value });
                if (orderCount > 0)
                {
                    await conn.ExecuteAsync(
                        @"UPDATE ModuleMaster SET ModuleHeadDisplayOrder = ModuleHeadDisplayOrder + 1,
                          ModuleDisplayOrder = ModuleDisplayOrder + 1
                          WHERE ModuleHeadDisplayOrder >= @FromOrder AND SetGroupIndex = @SetGroupIndex AND IsDeletedTransaction = 0",
                        new { FromOrder = module.ModuleHeadDisplayOrder.Value, SetGroupIndex = module.SetGroupIndex.Value });
                }
            }

            module.ModuleDisplayOrder = module.ModuleHeadDisplayOrder;

            var insertQuery = @"
                INSERT INTO ModuleMaster (
                    ModuleName, ModuleHeadName, ModuleDisplayName,
                    ModuleHeadDisplayName, ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, CompanyID
                ) VALUES (
                    @ModuleName, @ModuleHeadName, @ModuleDisplayName,
                    @ModuleHeadDisplayName, @ModuleHeadDisplayOrder, @ModuleDisplayOrder, @SetGroupIndex, 2
                );
                SELECT CAST(SCOPE_IDENTITY() as int);";

            var newModuleId = await conn.ExecuteScalarAsync<int>(insertQuery, module);

            // Grant admin user full permissions
            var adminUserId = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 UserID FROM UserMaster WHERE UserName = 'admin' AND ISNULL(IsBlocked, 0) = 0");
            if (adminUserId.HasValue)
            {
                await conn.ExecuteAsync(
                    @"INSERT INTO UserModuleAuthentication
                      (UserID, ModuleID, ModuleName, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, IsHomePage, CompanyID, IsLocked, CreatedBy)
                      VALUES (@UserID, @ModuleID, @ModuleName, 1, 1, 1, 1, 1, 1, 1, 0, 2, 0, @UserID)",
                    new { UserID = adminUserId.Value, ModuleID = newModuleId, ModuleName = module.ModuleName });
            }

            return Ok(new { Message = "Module created successfully in client database.", ModuleId = newModuleId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class CreateModuleForClientRequest
{
    public string ConnectionString { get; set; } = "";
    public ModuleDto Module { get; set; } = new();
}
