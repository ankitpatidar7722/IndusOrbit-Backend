using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ActivityLogController : ControllerBase
{
    private readonly IActivityLogService _activityLogService;
    private readonly ILogger<ActivityLogController> _logger;

    public ActivityLogController(
        IActivityLogService activityLogService,
        ILogger<ActivityLogController> logger)
    {
        _activityLogService = activityLogService;
        _logger = logger;
    }

    /// <summary>
    /// Get activity logs with filtering and pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> GetActivityLogs([FromBody] ActivityLogFilterRequest filter)
    {
        try
        {
            var result = await _activityLogService.GetActivityLogsAsync(filter);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activity logs");
            return StatusCode(500, new { message = "Error fetching activity logs", error = ex.Message });
        }
    }

    /// <summary>
    /// Get activity log by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetActivityLogById(int id)
    {
        try
        {
            var log = await _activityLogService.GetActivityLogByIdAsync(id);
            if (log == null)
            {
                return NotFound(new { message = "Activity log not found" });
            }
            return Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activity log {Id}", id);
            return StatusCode(500, new { message = "Error fetching activity log", error = ex.Message });
        }
    }

    /// <summary>
    /// Get activity logs for a specific entity
    /// </summary>
    [HttpGet("entity/{entityName}/{entityId}")]
    public async Task<IActionResult> GetEntityActivityLogs(string entityName, int entityId)
    {
        try
        {
            var logs = await _activityLogService.GetEntityActivityLogsAsync(entityName, entityId);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity activity logs for {EntityName}:{EntityId}", entityName, entityId);
            return StatusCode(500, new { message = "Error fetching entity activity logs", error = ex.Message });
        }
    }

    /// <summary>
    /// Get activity summary statistics
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetActivitySummary()
    {
        try
        {
            var summary = await _activityLogService.GetActivitySummaryAsync();
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activity summary");
            return StatusCode(500, new { message = "Error fetching activity summary", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually log an activity (for testing or external integrations)
    /// </summary>
    [HttpPost("log")]
    public async Task<IActionResult> LogActivity([FromBody] CreateActivityLogRequest request)
    {
        try
        {
            // Get username from JWT if not provided
            if (string.IsNullOrEmpty(request.WebUserName))
            {
                request.WebUserName = User.FindFirst("userName")?.Value ?? "Unknown";
            }

            // Get IP address and user agent
            request.IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.UserAgent = HttpContext.Request.Headers["User-Agent"].ToString();

            var activityLogId = await _activityLogService.LogActivityAsync(request);
            return Ok(new { activityLogId, message = "Activity logged successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging activity");
            return StatusCode(500, new { message = "Error logging activity", error = ex.Message });
        }
    }

    /// <summary>
    /// Delete old activity logs (admin only)
    /// </summary>
    [HttpDelete("cleanup/{daysToKeep}")]
    public async Task<IActionResult> DeleteOldLogs(int daysToKeep)
    {
        try
        {
            if (daysToKeep < 30)
            {
                return BadRequest(new { message = "Must keep at least 30 days of logs" });
            }

            var deletedCount = await _activityLogService.DeleteOldLogsAsync(daysToKeep);
            return Ok(new { deletedCount, message = $"Deleted {deletedCount} old activity logs" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting old logs");
            return StatusCode(500, new { message = "Error deleting old logs", error = ex.Message });
        }
    }

    /// <summary>
    /// Get unique usernames from CompanyWebUser for filter dropdown
    /// </summary>
    [HttpGet("usernames")]
    public async Task<IActionResult> GetUsernames()
    {
        try
        {
            var usernames = await _activityLogService.GetUsernamesAsync();
            return Ok(usernames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching usernames");
            return StatusCode(500, new { message = "Error fetching usernames", error = ex.Message });
        }
    }
}
