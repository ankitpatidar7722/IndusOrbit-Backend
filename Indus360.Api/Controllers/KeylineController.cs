using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Keyline enterprise-catalog lookups (Module Name / Sub Module Name for Change Requests).</summary>
[ApiController]
[Route("api/keyline")]
public sealed class KeylineController : ControllerBase
{
    private readonly KeylineRepository _repo;
    public KeylineController(KeylineRepository repo) => _repo = repo;

    /// <summary>All (Module Name, Sub Module Name) pairs from the Keyline ModuleMaster.</summary>
    [HttpGet("modules")]
    public async Task<IActionResult> Modules()
    {
        try { return Ok(new { success = true, data = await _repo.GetModulesAsync() }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<KeylineModuleRow>() }); }
    }

    /// <summary>Web modules for the "SOP of Web Modules" grid (Keyline ModuleMaster catalog).</summary>
    [HttpGet("sop-modules")]
    public async Task<IActionResult> SopModules()
    {
        try { return Ok(new { success = true, data = await _repo.GetSopModulesAsync() }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<KeylineSopModuleRow>() }); }
    }
}
