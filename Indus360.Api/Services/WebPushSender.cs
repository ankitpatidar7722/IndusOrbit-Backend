using System.Text.Json;
using Indus360.Api.Repositories;
using WebPush;

namespace Indus360.Api.Services;

/// <summary>
/// Sends OS-level Web Push notifications (works even when the app/tab is closed). Signs each message
/// with the app's VAPID keys and delivers it to every stored subscription for the target user, then
/// prunes any endpoint the push service reports as gone (404/410). Stateless singleton — VAPID keys
/// come from config (Vapid:PublicKey / :PrivateKey / :Subject, with VAPID_* env fallbacks), mirroring
/// GeminiService. Fired from <see cref="NotificationPusher"/> right after the DB insert + SignalR push.
/// </summary>
public sealed class WebPushSender
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebPushSender> _log;
    private readonly WebPushClient _client = new();
    private readonly VapidDetails? _vapid;

    public WebPushSender(IConfiguration cfg, IServiceScopeFactory scopes, ILogger<WebPushSender> log)
    {
        _scopes = scopes;
        _log = log;

        var pub = Env("VAPID_PUBLIC_KEY") ?? cfg["Vapid:PublicKey"];
        var priv = Env("VAPID_PRIVATE_KEY") ?? cfg["Vapid:PrivateKey"];
        var subject = Env("VAPID_SUBJECT") ?? cfg["Vapid:Subject"] ?? "mailto:admin@indusanalytics.in";
        if (!string.IsNullOrWhiteSpace(pub) && !string.IsNullOrWhiteSpace(priv))
        {
            try { _vapid = new VapidDetails(subject, pub, priv); }
            catch (Exception ex) { _log.LogError(ex, "Invalid VAPID keys — Web Push disabled."); }
        }
        else
        {
            _log.LogWarning("VAPID keys not configured — Web Push disabled (Vapid:PublicKey / :PrivateKey).");
        }
    }

    private static string? Env(string k) =>
        Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : null;

    /// <summary>True once VAPID keys are present (push can be sent / offered to clients).</summary>
    public bool Enabled => _vapid is not null;

    /// <summary>The VAPID public key the browser needs for pushManager.subscribe().</summary>
    public string? PublicKey => _vapid?.PublicKey;

    /// <summary>
    /// Deliver a push to all of the user's devices. Never throws — a push failure must not break the
    /// notification flow. <paramref name="payload"/> is serialized to JSON and read by the service
    /// worker's `push` handler (expects { title, body, url, icon, tag }).
    /// </summary>
    public async Task SendToUserAsync(int companyId, long userId, object payload)
    {
        if (_vapid is null) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<PushSubscriptionRepository>();
            var subs = await repo.ListByUserAsync(userId);
            if (subs.Count == 0) return;

            var json = JsonSerializer.Serialize(payload);
            foreach (var s in subs)
            {
                try
                {
                    await _client.SendNotificationAsync(
                        new PushSubscription(s.Endpoint, s.P256dh, s.Auth), json, _vapid);
                }
                catch (WebPushException wpe)
                {
                    var code = (int)wpe.StatusCode;
                    if (code == 404 || code == 410) // Gone — endpoint expired/unsubscribed
                    {
                        await repo.DeleteByIdAsync(s.Id);
                        _log.LogInformation("Pruned dead push subscription {Id} for user {User} ({Code}).", s.Id, userId, code);
                    }
                    else
                    {
                        _log.LogWarning("Web Push to sub {Id} (user {User}) failed: {Code} {Msg}", s.Id, userId, code, wpe.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Web Push send to user {User} failed.", userId);
        }
    }
}
