using System.Text.RegularExpressions;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Which login-page DESIGN (skin) is active — a single global, admin-chosen value. Stored as a tiny
/// text file under the persistent uploads root (survives redeploys; no DB change). This ONLY controls
/// the login page's look — the auth/login logic is unchanged. The pre-auth login page reads it via the
/// public GET; the admin picks it from Settings → "Change Login Page" (POST).
/// </summary>
[ApiController]
[Route("api/login-design")]
public sealed class LoginDesignController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    public LoginDesignController(IWebHostEnvironment env) => _env = env;

    private string FilePath => Path.Combine(_env.UploadsRoot(), "login-design.txt");

    /// <summary>Public — the (pre-auth) login page reads which skin to render. Default = "default".</summary>
    [HttpGet]
    public IActionResult Get()
    {
        var design = "default";
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                var v = System.IO.File.ReadAllText(FilePath).Trim();
                if (v.Length > 0) design = v;
            }
        }
        catch { /* unreadable → fall back to default */ }
        return Ok(new { design });
    }

    /// <summary>Set the active login design (admin — the Settings UI gates who can reach this).</summary>
    [HttpPost]
    public IActionResult Set([FromBody] LoginDesignRequest req)
    {
        var design = (req?.Design ?? "default").Trim();
        if (design.Length == 0 || design.Length > 64 || !Regex.IsMatch(design, "^[a-zA-Z0-9_-]+$"))
            return BadRequest(new { error = "Invalid design id" });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            System.IO.File.WriteAllText(FilePath, design);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        return Ok(new { design });
    }
}

public sealed class LoginDesignRequest { public string? Design { get; set; } }
