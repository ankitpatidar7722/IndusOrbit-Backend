using Microsoft.Extensions.Options;

namespace Indus360.Api.Services;

/// <summary>Config for the WhatsApp (WhatsFly / Whatsify) integration. Disabled by default —
/// set Enabled=true and fill the secrets in appsettings to turn it on.</summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; } = false;
    public string Secret { get; set; } = "";
    public string Account { get; set; } = "";
    public string ApiUrl { get; set; } = "https://api.whatsify.me/api/send/whatsapp";
    public string ReportMobile { get; set; } = "";
}

/// <summary>Sends WhatsApp text messages via WhatsFly (ports WhatsAppNotifier send).</summary>
public sealed class WhatsAppSender
{
    private readonly HttpClient _http;
    private readonly WhatsAppOptions _opt;
    private readonly ILogger<WhatsAppSender> _log;

    public WhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> opt, ILogger<WhatsAppSender> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;
    }

    public async Task<bool> SendAsync(string recipient, string message)
    {
        if (!_opt.Enabled || string.IsNullOrWhiteSpace(_opt.Secret) || string.IsNullOrWhiteSpace(recipient))
            return false;

        if (!recipient.StartsWith('+')) recipient = "+91" + recipient.TrimStart('0');

        var form = new Dictionary<string, string>
        {
            ["secret"] = _opt.Secret,
            ["account"] = _opt.Account,
            ["recipient"] = recipient,
            ["message"] = message,
            ["type"] = "text",
        };
        try
        {
            var res = await _http.PostAsync(_opt.ApiUrl, new FormUrlEncodedContent(form));
            var body = await res.Content.ReadAsStringAsync();
            return body.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WhatsApp send failed for {Recipient}", recipient);
            return false;
        }
    }
}
