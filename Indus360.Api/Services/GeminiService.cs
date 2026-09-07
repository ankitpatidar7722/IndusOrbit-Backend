using System.Text;
using System.Text.Json;

namespace Indus360.Api.Services;

/// <summary>
/// Thin client for Google's free Gemini API (Google AI Studio key). Used to generate short
/// summaries of tracker rows. The API key is read from the GEMINI_API_KEY env var (survives
/// redeploys) or appsettings "Gemini:ApiKey"; the model from "Gemini:Model" (default
/// gemini-2.0-flash). <see cref="IsConfigured"/> is false until a key is set, so callers can show a
/// friendly "not configured yet" message instead of failing.
/// </summary>
public sealed class GeminiService
{
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    public GeminiService(IConfiguration cfg, IHttpClientFactory httpFactory) { _cfg = cfg; _httpFactory = httpFactory; }

    /// <summary>The shared/fallback key (env var or appsettings) — used when a user has no personal key.</summary>
    public string? SharedKey => Environment.GetEnvironmentVariable("GEMINI_API_KEY") is { Length: > 0 } e ? e : _cfg["Gemini:ApiKey"];
    private string Model => _cfg["Gemini:Model"] is { Length: > 0 } m ? m : "gemini-3.6-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SharedKey);

    /// <summary>Send a prompt to Gemini and return the generated text. Uses <paramref name="apiKeyOverride"/>
    /// (the acting user's personal key) when given, else the shared key. Throws with a readable message
    /// when no key is available or the API rejects the request.</summary>
    public async Task<string> GenerateAsync(string prompt, string? apiKeyOverride = null, CancellationToken ct = default)
    {
        var key = !string.IsNullOrWhiteSpace(apiKeyOverride) ? apiKeyOverride : SharedKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("No Gemini API key. Add your own key in Settings → AI, or ask an administrator to set a shared key.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={key}";
        var payload = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.3, maxOutputTokens = 2048 },
        };

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini API error ({(int)res.StatusCode}): {Trunc(body, 300)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini returned no result (the content may have been blocked or empty).");
        var text = cands[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        return (text ?? "").Trim();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
