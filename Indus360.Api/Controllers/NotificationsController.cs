using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Per-user notifications (chat + email) — list, unread count, mark-read, and settings.</summary>
[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly AppNotificationRepository _repo;
    public NotificationsController(AppNotificationRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 30)
    {
        var uid = this.CurrentUserId();
        if (uid is null) return Ok(new { data = Array.Empty<NotificationDto>(), unread = 0 });
        var data = await _repo.ListAsync(uid.Value, Math.Clamp(limit, 1, 100));
        var unread = await _repo.UnreadCountAsync(uid.Value);
        return Ok(new { data, unread });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
        => Ok(new { unread = await _repo.UnreadCountAsync(this.CurrentUserId() ?? 0) });

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        await _repo.MarkReadAsync(uid.Value, id);
        return Ok(new { unread = await _repo.UnreadCountAsync(uid.Value) });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAll()
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        await _repo.MarkAllReadAsync(uid.Value);
        return Ok(new { unread = 0 });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        await _repo.DeleteAsync(uid.Value, id);
        return Ok(new { unread = await _repo.UnreadCountAsync(uid.Value) });
    }

    [HttpPost("clear-read")]
    public async Task<IActionResult> ClearRead()
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        var n = await _repo.ClearReadAsync(uid.Value);
        return Ok(new { cleared = n });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
        => Ok(await _repo.GetSettingsAsync(this.CurrentUserId() ?? 0));

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SaveNotifSettingsRequest req)
    {
        var uid = this.CurrentUserId(); if (uid is null) return BadRequest("Missing UserID header");
        await _repo.SaveSettingsAsync(uid.Value, req?.NotifyMessages ?? true, req?.NotifyEmails ?? true);
        return Ok(new { Message = "Notification settings saved" });
    }
}
