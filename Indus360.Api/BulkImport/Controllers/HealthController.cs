using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            message = "Bulk Import Backend API is running",
            timestamp = DateTime.UtcNow,
            version = "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }

    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping()
    {
        return Ok(new
        {
            status = "pong",
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult Version()
    {
        return Ok(new
        {
            version = "1.0.0",
            buildDate = "2026-03-24",
            framework = ".NET 8.0",
            platform = Environment.OSVersion.ToString()
        });
    }
}
