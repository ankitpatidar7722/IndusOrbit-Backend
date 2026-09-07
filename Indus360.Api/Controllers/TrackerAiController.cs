using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>AI helpers for the client Tracker — generates a short Gemini summary of a row so a
/// manager can grasp it without reading every column. Each user uses their OWN Gemini key (set in
/// Settings) so no one hits a shared rate limit; falls back to a shared key if one is configured.</summary>
[ApiController]
[Route("api/tracker-ai")]
public sealed class TrackerAiController : ControllerBase
{
    private readonly GeminiService _gemini;
    private readonly UserAdminRepository _users;
    public TrackerAiController(GeminiService gemini, UserAdminRepository users) { _gemini = gemini; _users = users; }

    private long? ActingUserId()
        => Request.Headers.TryGetValue("UserID", out var h) && long.TryParse(h.ToString(), out var id) && id > 0 ? id : null;

    private async Task<string?> ResolveKeyAsync()
    {
        var uid = ActingUserId();
        if (uid is not null)
        {
            var userKey = await _users.GetGeminiKeyAsync(uid.Value);
            if (!string.IsNullOrWhiteSpace(userKey)) return userKey;
        }
        return _gemini.SharedKey;   // fall back to a shared key if one is set
    }

    /// <summary>Whether AI summaries are available for the acting user (their own key, or a shared one).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var key = await ResolveKeyAsync();
        return Ok(new { configured = !string.IsNullOrWhiteSpace(key) });
    }

    [HttpPost("summarize")]
    public async Task<IActionResult> Summarize([FromBody] SummarizeRequest? req)
    {
        var apiKey = await ResolveKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            return Ok(new { success = false, message = "Add your own free Gemini API key in Settings → AI to use summaries." });

        var lines = (req?.Fields ?? new())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && !IsNoise(kv.Key))
            .Select(kv => $"- {kv.Key}: {kv.Value!.Trim()}")
            .ToList();
        if (lines.Count == 0)
            return Ok(new { success = false, message = "Nothing to summarize yet — fill in some details first." });

        var entity = string.IsNullOrWhiteSpace(req!.Entity) ? "tracker item" : req.Entity!.Trim();
        var prompt =
            $"Summarize the following {entity} as a quick overview: 2 to 3 short bullet points a manager can grasp at a glance without reading the full row. " +
            "Cover what it is, its current status, and any key detail (who is responsible, timeline/dates, or a pending risk) — only when present. Do not invent facts not present below. " +
            "Keep each bullet to one short line. Start each bullet with \"- \". Use plain text only — no bold, no markdown, no asterisks. Output ONLY the bullet points, nothing else.\n\n" +
            string.Join("\n", lines);

        try
        {
            var summary = await _gemini.GenerateAsync(prompt, apiKey);
            return Ok(new { success = true, summary });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // Don't feed the summary field itself (or empty housekeeping labels) back into the prompt.
    private static bool IsNoise(string key)
    {
        var k = key.Trim().ToLowerInvariant();
        return k is "summary" or "id" or "";
    }

    /// <summary>Quick "AI overview" of an email — a few short bullet points, like the Gmail feature.</summary>
    [HttpPost("summarize-email")]
    public async Task<IActionResult> SummarizeEmail([FromBody] SummarizeEmailRequest? req)
    {
        var apiKey = await ResolveKeyAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            return Ok(new { success = false, message = "Add your own free Gemini API key in Settings → AI to use summaries." });

        var body = (req?.Body ?? "").Trim();
        if (body.Length == 0)
            return Ok(new { success = false, message = "This email has no text to summarize." });
        if (body.Length > 12000) body = body[..12000];   // keep the request light for very long threads

        var prompt =
            "Summarize the following email as a quick overview: 2 to 4 short bullet points for a busy reader. " +
            "Capture what the sender did or is asking, any action needed from the reader, and key specifics (names, numbers, dates, IDs). " +
            "Keep each bullet to one short line. Start each bullet with \"- \". Use plain text only — no bold, no markdown, no asterisks. Output ONLY the bullet points, nothing else.\n\n" +
            $"Subject: {req?.Subject}\nFrom: {req?.From}\n\n{body}";

        try
        {
            var summary = await _gemini.GenerateAsync(prompt, apiKey);
            return Ok(new { success = true, summary });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}

public sealed class SummarizeRequest
{
    public string? Entity { get; set; }                     // e.g. "Milestone Roadmap"
    public Dictionary<string, string?>? Fields { get; set; } // human-readable label -> value
}

public sealed class SummarizeEmailRequest
{
    public string? Subject { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
}
