using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>The acting user's effective view/edit permission for each client-detail tab.</summary>
[ApiController]
[Route("api/client-tab-permissions")]
public sealed class ClientTabPermissionController : ControllerBase
{
    private readonly ClientTabPermissionRepository _repo;
    public ClientTabPermissionController(ClientTabPermissionRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] long userId)
    {
        try { return Ok(new { success = true, data = await _repo.GetForUserAsync(userId) }); }
        catch (Exception ex) { return Ok(new { success = false, error = ex.Message, data = Array.Empty<object>() }); }
    }
}
