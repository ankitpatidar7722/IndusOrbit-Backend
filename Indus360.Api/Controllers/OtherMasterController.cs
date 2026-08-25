using System.Text.Json;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Serves the endpoint indas-ui's DynamicSidebar calls
/// (<c>api/othermaster/createdynamicmenuwithsubmenu</c>). indas-ui sends the
/// logged-in user's id in the <c>UserID</c> request header and expects a raw
/// JSON array of PascalCase module rows.
/// </summary>
[ApiController]
[Route("api/othermaster")]
public sealed class OtherMasterController : ControllerBase
{
    private readonly NavRepository _repo;
    public OtherMasterController(NavRepository repo) => _repo = repo;

    // Preserve PascalCase — indas-ui reads ModuleHeadName / SetGroupIndex / ModuleName verbatim.
    private static readonly JsonSerializerOptions PascalJson = new(JsonSerializerDefaults.General);

    [HttpGet("createdynamicmenuwithsubmenu")]
    [HttpPost("createdynamicmenuwithsubmenu")]
    public async Task<IActionResult> CreateDynamicMenuWithSubMenu()
    {
        long userId = 0;
        if (Request.Headers.TryGetValue("UserID", out var uidHeader))
            long.TryParse(uidHeader.ToString(), out userId);
        if (userId == 0 && Request.Query.TryGetValue("userId", out var uidQuery))
            long.TryParse(uidQuery.ToString(), out userId);

        if (userId == 0)
            return Content("[]", "application/json");

        var modules = await _repo.GetModulesAsync(userId);
        var json = JsonSerializer.Serialize(modules, PascalJson);
        return Content(json, "application/json");
    }
}
