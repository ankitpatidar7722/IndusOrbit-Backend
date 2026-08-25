using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Client provisioning wizard (cloned from BulkImport CompanySubscription):
/// setup-database (BACKUP/RESTORE) → company/branch/production masters → complete-setup.
/// </summary>
[ApiController]
[Route("api/provisioning")]
public sealed class ProvisioningController : ControllerBase
{
    private readonly ProvisioningRepository _repo;
    public ProvisioningController(ProvisioningRepository repo) => _repo = repo;

    [HttpGet("servers")]
    public IActionResult Servers() => Ok(new ServerListResponse { Success = true, Servers = _repo.GetServers() });

    [HttpGet("backup-databases/{applicationName}")]
    public async Task<IActionResult> BackupDatabases(string applicationName)
    {
        try { return Ok(new BackupDatabaseResponse { Success = true, Databases = await _repo.GetBackupDatabasesAsync(applicationName) }); }
        catch (Exception ex) { return Ok(new BackupDatabaseResponse { Success = false, Message = ex.Message }); }
    }

    [HttpPost("setup-database")]
    public async Task<IActionResult> SetupDatabase([FromBody] SetupDatabaseRequest req)
    {
        try { return Ok(await _repo.SetupDatabaseAsync(req)); }
        catch (Exception ex) { return Ok(new SetupDatabaseResponse { Success = false, Message = ex.Message }); }
    }

    [HttpPost("company-master")]
    public async Task<IActionResult> CompanyMaster([FromBody] CompanyMasterRequest req)
    {
        try { return Ok(await _repo.SaveCompanyMasterAsync(req)); }
        catch (Exception ex) { return Ok(new CompanyMasterResponse { Success = false, Message = ex.Message }); }
    }

    [HttpPost("branch-master")]
    public async Task<IActionResult> BranchMaster([FromBody] BranchMasterRequest req)
    {
        try { return Ok(await _repo.SaveBranchMasterAsync(req)); }
        catch (Exception ex) { return Ok(new SimpleResult { Success = false, Message = ex.Message }); }
    }

    [HttpPost("production-unit")]
    public async Task<IActionResult> ProductionUnit([FromBody] ProductionUnitRequest req)
    {
        try { return Ok(await _repo.SaveProductionUnitAsync(req)); }
        catch (Exception ex) { return Ok(new SimpleResult { Success = false, Message = ex.Message }); }
    }

    [HttpPost("complete-setup")]
    public async Task<IActionResult> CompleteSetup([FromBody] CompleteSetupRequest req)
    {
        try { return Ok(await _repo.CompleteSetupAsync(req)); }
        catch (Exception ex) { return Ok(new CompleteSetupResponse { Success = false, Message = ex.Message }); }
    }
}
