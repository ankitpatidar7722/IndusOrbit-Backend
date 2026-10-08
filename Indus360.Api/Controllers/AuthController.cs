using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly NavRepository _repo;
    private readonly LoginSessionRepository _sessions;
    public AuthController(NavRepository repo, LoginSessionRepository sessions) { _repo = repo; _sessions = sessions; }

    /// <summary>Called by the frontend's next-auth Credentials provider.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Email and password are required." });

        var user = await _repo.AuthenticateAsync(req.Email.Trim(), req.Password);
        if (user is null)
            return Unauthorized(new { message = "Invalid email or password." });

        // Open an audited login session (who / when / from where) — best-effort.
        var ip = Request.Headers["X-Forwarded-For"].ToString().Split(',').FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        ip = Services.UserAgentParser.NormalizeIp(ip);
        await _sessions.OpenAsync((int)user.UserID, user.FullName, ip, Request.Headers["User-Agent"].ToString());

        return Ok(user);
    }
}
