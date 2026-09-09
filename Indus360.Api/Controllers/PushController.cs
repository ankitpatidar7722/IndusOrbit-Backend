using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Web Push: hands the browser the VAPID public key, and stores / removes a device's push
/// subscription. The actual sending happens server-side in <see cref="WebPushSender"/> whenever a
/// notification is created (see <see cref="Services.NotificationPusher"/>).
/// </summary>
[ApiController]
[Route("api/push")]
public sealed class PushController : ControllerBase
{
    private readonly PushSubscriptionRepository _repo;
    private readonly WebPushSender _push;
    public PushController(PushSubscriptionRepository repo, WebPushSender push) { _repo = repo; _push = push; }

    /// <summary>Public VAPID key for pushManager.subscribe(). `enabled=false` → push not configured.</summary>
    [HttpGet("vapid-public-key")]
    public IActionResult VapidPublicKey()
        => Ok(new { enabled = _push.Enabled, key = _push.PublicKey });

    public sealed class SubscribeRequest
    {
        public string Endpoint { get; set; } = "";
        public SubKeys? Keys { get; set; }
        public string? P256dh { get; set; }   // also accepted flat (some clients)
        public string? Auth { get; set; }
        public sealed class SubKeys { public string? P256dh { get; set; } public string? Auth { get; set; } }
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        var uid = this.CurrentUserId();
        if (uid is null) return BadRequest("Missing UserID header");
        var p256dh = req.Keys?.P256dh ?? req.P256dh;
        var auth = req.Keys?.Auth ?? req.Auth;
        if (string.IsNullOrWhiteSpace(req.Endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
            return BadRequest("Incomplete subscription (endpoint / keys).");

        var ua = Request.Headers.UserAgent.ToString();
        await _repo.SaveAsync(uid.Value, this.CurrentCompanyId(), req.Endpoint, p256dh!, auth!,
            string.IsNullOrWhiteSpace(ua) ? null : (ua.Length > 400 ? ua[..400] : ua));
        return Ok(new { ok = true });
    }

    public sealed class UnsubscribeRequest { public string Endpoint { get; set; } = ""; }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.Endpoint))
            await _repo.DeleteByEndpointAsync(req.Endpoint);
        return Ok(new { ok = true });
    }
}
