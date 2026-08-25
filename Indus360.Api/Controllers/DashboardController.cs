using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ClientRepository _repo;
    public DashboardController(ClientRepository repo) => _repo = repo;

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var (total, inImpl, goLive, openCr) = await _repo.GetStatsAsync();
        return Ok(new { total, inImplementation = inImpl, goLive, openChangeRequests = openCr });
    }
}
