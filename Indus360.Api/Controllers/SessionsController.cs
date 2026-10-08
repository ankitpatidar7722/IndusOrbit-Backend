using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Login-session tracking — heartbeat keeps a session alive, logout closes it, and the list
/// powers the Audit Logs "Sessions" view (who logged in, when, for how long).</summary>
[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly LoginSessionRepository _repo;
    public SessionsController(LoginSessionRepository repo) => _repo = repo;

    private int? UserId() => int.TryParse(Request.Headers["UserID"].ToString(), out var v) && v > 0 ? v : null;

    /// <summary>Keep the caller's session alive (called periodically by the app).</summary>
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat()
    {
        var uid = UserId();
        if (uid is not null)
        {
            var deviceId = Request.Headers["X-Device-Id"].ToString();
            await _repo.HeartbeatAsync(uid.Value, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim());
        }
        return Ok(new { success = true });
    }

    /// <summary>Close the caller's active session (on sign-out / tab close).</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var uid = UserId();
        if (uid is not null) await _repo.CloseAsync(uid.Value);
        return Ok(new { success = true });
    }

    /// <summary>Sessions list (newest first) for the Audit Logs → Sessions view.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? userId, [FromQuery] bool? activeOnly,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var (rows, total) = await _repo.ListAsync(new SessionQuery
        {
            UserId = userId, ActiveOnly = activeOnly,
            FromUtc = from?.ToUniversalTime(), ToUtc = to?.ToUniversalTime(),
            Page = page, PageSize = pageSize,
        });
        return Ok(new { success = true, rows, total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 200) });
    }
}
