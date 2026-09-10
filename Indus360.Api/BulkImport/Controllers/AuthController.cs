using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.DTOs;
using Backend.Services;
using System.Security.Claims;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICompanySessionStore _sessionStore;

    public AuthController(IAuthService authService, ICompanySessionStore sessionStore)
    {
        _authService = authService;
        _sessionStore = sessionStore;
    }

    [HttpPost("company-login")]
    [AllowAnonymous]
    public async Task<IActionResult> CompanyLogin([FromBody] CompanyLoginRequest request)
    {
        var response = await _authService.CompanyLoginAsync(request);
        // Always return 200 — frontend checks response.Success
        // Returning 401 here triggers the global auth interceptor (session expiry handler)
        return Ok(response);
    }

    [HttpPost("indus-login")]
    [AllowAnonymous]
    public async Task<IActionResult> IndusLogin([FromBody] IndusLoginRequest request)
    {
        var response = await _authService.IndusLoginAsync(request);
        return Ok(response);
    }

    [HttpPost("user-login")]
    [Authorize]
    public async Task<IActionResult> UserLogin([FromBody] UserLoginRequest request)
    {
        // Extract SessionID from claims (from Step 1 Token)
        var sessionIdClaim = User.FindFirst("sessionId")?.Value;
        if (string.IsNullOrEmpty(sessionIdClaim) || !Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            return Ok(new UserLoginResponse { Success = false, Message = "Invalid Session ID. Please re-login." });
        }

        var response = await _authService.UserLoginAsync(request, sessionId);
        // Always return 200 — frontend checks response.Success
        return Ok(response);
    }
    
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var sessionIdClaim = User.FindFirst("sessionId")?.Value;
        if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            _sessionStore.RemoveSession(sessionId);
        }
        return Ok(new { Message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        return Ok(new 
        { 
            User = User.Identity?.Name,
            Claims = User.Claims.Select(c => new { c.Type, c.Value })
        });
    }

    [HttpGet("check-session")]
    [Authorize]
    public IActionResult CheckSession()
    {
        var sessionIdClaim = User.FindFirst("sessionId")?.Value;
        var loginType = User.FindFirst("loginType")?.Value;

        if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            if (_sessionStore.TryGetSession(sessionId, out var session) && session != null)
            {
                return Ok(new { Success = true, Message = "Session is valid", LoginType = loginType ?? "customer" });
            }
        }

        // Session not found in server-side store (e.g. app pool recycled / server restarted).
        // JWT may still be cryptographically valid, but without the in-memory session the
        // connection string is gone — return 401 so the frontend redirects to login.
        return Unauthorized(new { Success = false, Message = "Session expired. Please login again." });
    }
}
