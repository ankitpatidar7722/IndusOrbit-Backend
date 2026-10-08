using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Read API for the Audit Logs screen — filtered + paged trail, plus filter facets.</summary>
[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogController : ControllerBase
{
    private readonly AuditLogRepository _repo;
    public AuditLogController(AuditLogRepository repo) => _repo = repo;

    /// <summary>Filtered, paged audit trail (newest first).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int? userId, [FromQuery] string? module, [FromQuery] string? client, [FromQuery] string? action,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = new AuditQuery
        {
            UserId = userId, Module = module, Client = client, Action = action, Search = search,
            FromUtc = from?.ToUniversalTime(), ToUtc = to?.ToUniversalTime(),
            Page = page, PageSize = pageSize,
        };
        var (rows, total) = await _repo.QueryAsync(q);
        return Ok(new { success = true, rows, total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 200) });
    }

    /// <summary>Distinct modules / actions / users for the filter dropdowns.</summary>
    [HttpGet("facets")]
    public async Task<IActionResult> Facets()
    {
        var (modules, actions, users) = await _repo.FacetsAsync();
        return Ok(new { success = true, modules, actions, users = users.Select(u => new { id = u.id, name = u.name }) });
    }
}
