using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly ProvisioningJobStore _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    public ProvisioningController(ProvisioningRepository repo, ProvisioningJobStore jobs, IServiceScopeFactory scopeFactory)
    { _repo = repo; _jobs = jobs; _scopeFactory = scopeFactory; }

    [HttpGet("servers")]
    public IActionResult Servers() => Ok(new ServerListResponse { Success = true, Servers = _repo.GetServers() });

    [HttpGet("backup-databases/{applicationName}")]
    public async Task<IActionResult> BackupDatabases(string applicationName)
    {
        try { return Ok(new BackupDatabaseResponse { Success = true, Databases = await _repo.GetBackupDatabasesAsync(applicationName) }); }
        catch (Exception ex) { return Ok(new BackupDatabaseResponse { Success = false, Message = ex.Message }); }
    }

    /// <summary>
    /// Start the database BACKUP → transfer → RESTORE in the BACKGROUND and return a job id immediately,
    /// so the HTTP call can't time out on a long cross-server transfer. The wizard polls
    /// setup-database/progress/{jobId} for the live stage + percentage and the final result.
    /// </summary>
    [HttpPost("setup-database")]
    public IActionResult SetupDatabase([FromBody] SetupDatabaseRequest req)
    {
        var jobId = _jobs.New();
        _ = Task.Run(async () =>
        {
            // The request scope is gone once this method returns, so resolve a fresh scoped repository.
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ProvisioningRepository>();
            try
            {
                var res = await repo.SetupDatabaseAsync(req, (stage, pct, msg) =>
                    _jobs.Update(jobId, p => { p.Stage = stage; p.Percent = pct; p.Message = msg; }));
                _jobs.Update(jobId, p =>
                {
                    p.Done = true; p.Success = res.Success; p.Result = res; p.Message = res.Message;
                    p.Stage = res.Success ? "done" : "error";
                    p.Percent = res.Success ? 100 : p.Percent;
                });
            }
            catch (Exception ex)
            {
                _jobs.Update(jobId, p => { p.Done = true; p.Success = false; p.Stage = "error"; p.Message = ex.Message; });
            }
        });
        return Ok(new { success = true, jobId });
    }

    /// <summary>Poll a setup-database job: live stage + percent, and (when done) the SetupDatabaseResponse.</summary>
    [HttpGet("setup-database/progress/{jobId}")]
    public IActionResult SetupDatabaseProgress(string jobId)
    {
        var p = _jobs.Get(jobId);
        if (p is null) return Ok(new { success = false, done = true, ok = false, message = "Unknown or expired job." });
        return Ok(new
        {
            success = true, done = p.Done, ok = p.Success, stage = p.Stage,
            percent = p.Percent, message = p.Message, result = p.Result,
        });
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
