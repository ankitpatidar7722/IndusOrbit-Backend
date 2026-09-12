using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Serves the "SOP of Web Modules" HTML documents. Each web module's SOP is a self-contained HTML
/// file (branded header + definition + steps + screenshot walkthrough + a Save-as-PDF button),
/// generated from the source PDF decks and shipped under &lt;contentRoot&gt;/sop-web-modules/&lt;slug&gt;.html.
/// The grid shows a "View" button for any module whose slug (from its display name) has a SOP file.
/// </summary>
[ApiController]
[Route("api/sop-modules")]
public sealed class SopModulesController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    public SopModulesController(IWebHostEnvironment env) => _env = env;

    private string SopDir => Path.Combine(_env.ContentRootPath, "sop-web-modules");

    /// <summary>The slugs that have a SOP document (so the grid can show/hide the View button).</summary>
    [HttpGet("list")]
    public IActionResult List()
    {
        var dir = new DirectoryInfo(SopDir);
        if (!dir.Exists) return Ok(new { success = true, slugs = Array.Empty<string>() });
        var slugs = dir.GetFiles("*.html").Select(f => Path.GetFileNameWithoutExtension(f.Name)).OrderBy(s => s).ToArray();
        return Ok(new { success = true, slugs });
    }

    /// <summary>Return one SOP as rendered HTML (opens directly in the browser; has its own Save-as-PDF).</summary>
    [HttpGet("view")]
    public IActionResult View([FromQuery] string slug)
    {
        var safe = Path.GetFileName(slug ?? "");                       // block path traversal
        if (string.IsNullOrWhiteSpace(safe)) return BadRequest("Missing slug.");
        var path = Path.Combine(SopDir, safe + ".html");
        if (!System.IO.File.Exists(path)) return NotFound("SOP not found for this module.");
        return PhysicalFile(path, "text/html; charset=utf-8");
    }
}
