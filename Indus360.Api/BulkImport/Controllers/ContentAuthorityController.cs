using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ContentAuthorityController : ControllerBase
{
    private readonly IContentAuthorityService _service;

    public ContentAuthorityController(IContentAuthorityService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetData()
    {
        var data = await _service.GetContentAuthorityDataAsync();
        return Ok(data);
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] ContentAuthoritySaveRequest request)
    {
        var result = await _service.SaveContentAuthorityAsync(request);
        return Ok(result);
    }

    [HttpPost("update-tech-details")]
    public async Task<IActionResult> UpdateTechDetails([FromBody] List<string> contentNames)
    {
        if (contentNames == null || contentNames.Count == 0)
            return BadRequest("No contents selected for update.");
        try
        {
            var result = await _service.UpdateContentDetailsAsync(contentNames);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to update content technical details.", detail = ex.Message });
        }
    }

    [HttpPost("update-keyline-details")]
    public async Task<IActionResult> UpdateKeylineDetails([FromBody] List<string> contentNames)
    {
        if (contentNames == null || contentNames.Count == 0)
            return BadRequest("No contents selected for keyline update.");
        try
        {
            var result = await _service.UpdateKeylineDetailsAsync(contentNames);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to update keyline details.", detail = ex.Message });
        }
    }
}
