using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Admin → Project Assignment: assign prod client projects to individual users.</summary>
[ApiController]
[Route("api/project-assignment")]
public sealed class ProjectAssignmentController : ControllerBase
{
    private readonly ProjectAssignmentRepository _repo;
    public ProjectAssignmentController(ProjectAssignmentRepository repo) => _repo = repo;

    /// <summary>All assignable projects (client companies) from the prod Indus DB.</summary>
    [HttpGet("projects")]
    public async Task<IActionResult> Projects()
    {
        try { return Ok(new { success = true, data = await _repo.GetProjectsAsync() }); }
        catch (Exception ex) { return Ok(new { success = false, error = ex.Message, data = Array.Empty<object>() }); }
    }

    /// <summary>Project codes currently assigned to a user.</summary>
    [HttpGet("user/{userId:long}")]
    public async Task<IActionResult> AssignedForUser(long userId)
    {
        try { return Ok(new { success = true, data = await _repo.GetAssignedCodesAsync(userId) }); }
        catch (Exception ex) { return Ok(new { success = false, error = ex.Message, data = Array.Empty<object>() }); }
    }

    /// <summary>Replace a user's assigned projects with the posted set of codes.</summary>
    [HttpPost("user/{userId:long}")]
    public async Task<IActionResult> Save(long userId, [FromBody] ProjectAssignmentSaveRequest r)
    {
        try
        {
            await _repo.SaveAsync(userId, r?.ProjectCodes ?? new(), r?.AssignedBy);
            return Ok(new { success = true, count = (r?.ProjectCodes ?? new()).Count });
        }
        catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
    }
}
