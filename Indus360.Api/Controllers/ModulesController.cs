using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Module-authority tabs: module settings, copy, module groups.</summary>
[ApiController]
[Route("api/modules")]
public sealed class ModulesController : ControllerBase
{
    private readonly ModulesRepository _repo;
    public ModulesController(ModulesRepository repo) => _repo = repo;

    [HttpPost("settings")]
    public async Task<IActionResult> Settings([FromBody] GetModuleSettingsRequest req)
    {
        try { return Ok(new { success = true, message = "", data = await _repo.GetModuleSettingsAsync(req.ApplicationName, req.ConnectionString) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ModuleSettingsRow>() }); }
    }

    [HttpPost("settings/save")]
    public async Task<IActionResult> SaveSettings([FromBody] SaveModuleSettingsRequest req)
    {
        try { var (i, d) = await _repo.SaveModuleSettingsAsync(req); return Ok(new { success = true, message = $"Module settings saved. {i} activated, {d} removed.", inserted = i, deleted = d }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, inserted = 0, deleted = 0 }); }
    }

    [HttpGet("check")]
    public async Task<IActionResult> Check([FromQuery] string connectionString)
    {
        try { var (has, n) = await _repo.CheckModulesExistAsync(connectionString); return Ok(new { success = true, hasModules = has, moduleCount = n }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, hasModules = false, moduleCount = 0 }); }
    }

    [HttpPost("copy")]
    public async Task<IActionResult> Copy([FromBody] CopyModulesRequest req)
    {
        try { var n = await _repo.CopyModulesAsync(req); return Ok(new { success = true, message = $"Copied {n} modules.", copiedCount = n }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, copiedCount = 0 }); }
    }

    [HttpGet("groups/{app}")]
    public async Task<IActionResult> Groups(string app)
    {
        try { return Ok(new { success = true, data = await _repo.GetModuleGroupsAsync(app) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<string>() }); }
    }

    [HttpPost("group-modules")]
    public async Task<IActionResult> GroupModules([FromBody] ModuleGroupModulesRequest req)
    {
        try { return Ok(new { success = true, data = await _repo.GetModuleGroupModulesAsync(req.ApplicationName, req.ModuleGroupName) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ModuleGroupModuleRow>() }); }
    }

    [HttpGet("available/{app}")]
    public async Task<IActionResult> Available(string app)
    {
        try { return Ok(new { success = true, data = await _repo.GetAvailableModulesAsync(app) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ModuleGroupModuleRow>() }); }
    }

    [HttpPost("group/create")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateModuleGroupRequest req)
    {
        try { await _repo.CreateModuleGroupAsync(req); return Ok(new { success = true, message = "Module group created." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPut("group/update")]
    public async Task<IActionResult> UpdateGroup([FromBody] UpdateModuleGroupRequest req)
    {
        try { var (ins, del) = await _repo.UpdateModuleGroupAsync(req); return Ok(new { success = true, message = $"Group updated ({ins} added, {del} removed).", inserted = ins, deleted = del }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, inserted = 0, deleted = 0 }); }
    }

    [HttpPost("group/delete")]
    public async Task<IActionResult> DeleteGroup([FromBody] DeleteModuleGroupRequest req)
    {
        try { var n = await _repo.DeleteModuleGroupAsync(req); return Ok(new { success = true, message = $"Module group deleted.", deletedCount = n }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, deletedCount = 0 }); }
    }

    [HttpPost("group/apply")]
    public async Task<IActionResult> ApplyGroup([FromBody] ApplyModuleGroupRequest req)
    {
        try { var n = await _repo.ApplyModuleGroupToClientAsync(req); return Ok(new { success = true, message = $"Applied {n} modules to client.", totalModules = n }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, totalModules = 0 }); }
    }

    // ── New Module Addition (client DB ModuleMaster) ──
    [HttpPost("client-modules")]
    public async Task<IActionResult> ClientModules([FromBody] GetModuleSettingsRequest req)
    {
        try { return Ok(new { success = true, data = await _repo.GetClientModulesAsync(req.ConnectionString) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ClientModuleDto>() }); }
    }

    [HttpGet("catalog/{app}")]
    public async Task<IActionResult> Catalog(string app)
    {
        try { return Ok(new { success = true, data = await _repo.GetCatalogModulesAsync(app) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ModuleGroupModuleRow>() }); }
    }

    /// <summary>Rich catalog (head-display-name, set-group-index, display orders) for New Module auto-fill.</summary>
    [HttpGet("catalog-rich/{app}")]
    public async Task<IActionResult> CatalogRich(string app)
    {
        try { return Ok(new { success = true, data = await _repo.GetCatalogRichAsync(app) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ClientModuleDto>() }); }
    }

    [HttpPost("client-module/create")]
    public async Task<IActionResult> CreateClientModule([FromBody] ClientModuleRequest req)
    {
        try { var id = await _repo.CreateClientModuleAsync(req); return Ok(new { success = true, message = "Module created.", moduleId = id }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, moduleId = 0 }); }
    }

    [HttpPut("client-module/update")]
    public async Task<IActionResult> UpdateClientModule([FromBody] ClientModuleRequest req)
    {
        try { await _repo.UpdateClientModuleAsync(req); return Ok(new { success = true, message = "Module updated." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpDelete("client-module")]
    public async Task<IActionResult> DeleteClientModule([FromQuery] string connectionString, [FromQuery] int moduleId)
    {
        try { await _repo.SoftDeleteClientModuleAsync(connectionString, moduleId); return Ok(new { success = true, message = "Module removed." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ── Indus Tool Authority (IndusToolModuleMaster + CompanyModuleAuthority) ──
    [HttpGet("tool-authority/{companyUserId}")]
    public async Task<IActionResult> ToolAuthority(string companyUserId)
    {
        try { return Ok(new { success = true, data = await _repo.GetModulesForCompanyAsync(companyUserId) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<IndusToolModuleDto>() }); }
    }

    [HttpPost("tool-authority")]
    public async Task<IActionResult> SaveToolAuthority([FromBody] SaveToolAuthorityRequest req)
    {
        try { var n = await _repo.SaveCompanyModuleAuthorityAsync(req.CompanyUserID, req.EnabledModuleIDs); return Ok(new { success = true, message = $"Saved {n} module grants.", savedCount = n }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, savedCount = 0 }); }
    }
}
