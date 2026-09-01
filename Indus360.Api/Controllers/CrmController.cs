using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Read-only bridge into the internal CRM app (IndusInternalApp)'s Clients list —
/// backs the "CRM Client" picker in the /clients provisioning wizard.</summary>
[ApiController]
[Route("api/crm")]
public sealed class CrmController : ControllerBase
{
    private readonly CrmRepository _repo;
    public CrmController(CrmRepository repo) => _repo = repo;

    [HttpGet("clients")]
    public async Task<IActionResult> GetClients()
    {
        try { return Ok(await _repo.GetClientsAsync()); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("mark-provisioned")]
    public async Task<IActionResult> MarkProvisioned([FromBody] MarkCrmProvisionedRequest req)
    {
        try { await _repo.MarkProvisionedAsync(req, this.CurrentUserId()); return Ok(new { success = true }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }
}
