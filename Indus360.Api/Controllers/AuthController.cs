using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly NavRepository _repo;
    public AuthController(NavRepository repo) => _repo = repo;

    /// <summary>Called by the frontend's next-auth Credentials provider.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Email and password are required." });

        var user = await _repo.AuthenticateAsync(req.Email.Trim(), req.Password);
        if (user is null)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(user);
    }
}
