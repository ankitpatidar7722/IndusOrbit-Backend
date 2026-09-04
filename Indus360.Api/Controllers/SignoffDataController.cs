using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Live auto-fill data for the client Sign-Off document. Fetched by the frontend when the user
/// opens Sign-Off, then merged into the signoff.html template (text placeholders + In-Scope
/// module checkboxes). Read-only.
/// </summary>
[ApiController]
[Route("api/signoff-data")]
public sealed class SignoffDataController : ControllerBase
{
    private readonly SignoffDataRepository _repo;
    public SignoffDataController(SignoffDataRepository repo) => _repo = repo;

    /// <summary>Auto-fill values for a client (keyed by the control-DB CompanyUserID).</summary>
    [HttpGet("{companyUserId}")]
    public async Task<IActionResult> Get(string companyUserId)
    {
        if (string.IsNullOrWhiteSpace(companyUserId))
            return BadRequest(new { success = false, message = "companyUserId is required." });
        var data = await _repo.GetAsync(companyUserId);
        return Ok(new { success = true, data });
    }
}
