namespace Indus360.Api.Services;

/// <summary>Lightweight User-Agent → (Browser, OS, Device) parser. No external dependency; covers the
/// browsers/OSes the ERP's users actually run. Good enough for an audit trail (not analytics-grade).</summary>
public static class UserAgentParser
{
    public static (string browser, string os, string device) Parse(string? ua)
    {
        ua ??= "";
        return (Browser(ua), Os(ua), Device(ua));
    }

    private static string Browser(string ua)
    {
        // Order matters — Edge/Opera/Brave spoof Chrome, Chrome spoofs Safari.
        if (ua.Contains("Edg/") || ua.Contains("EdgA/") || ua.Contains("Edge/")) return "Edge";
        if (ua.Contains("OPR/") || ua.Contains("Opera")) return "Opera";
        if (ua.Contains("Brave")) return "Brave";
        if (ua.Contains("SamsungBrowser")) return "Samsung Internet";
        if (ua.Contains("Firefox/") || ua.Contains("FxiOS")) return "Firefox";
        if (ua.Contains("Chrome/") || ua.Contains("CriOS")) return "Chrome";
        if (ua.Contains("Safari/")) return "Safari";
        if (ua.Contains("MSIE") || ua.Contains("Trident/")) return "Internet Explorer";
        if (ua.Length == 0) return "Unknown";
        return "Other";
    }

    private static string Os(string ua)
    {
        if (ua.Contains("Windows NT 10.0")) return "Windows 10/11";
        if (ua.Contains("Windows NT 6.3")) return "Windows 8.1";
        if (ua.Contains("Windows NT 6.1")) return "Windows 7";
        if (ua.Contains("Windows")) return "Windows";
        if (ua.Contains("Android")) return "Android";
        if (ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("iOS")) return "iOS";
        if (ua.Contains("Mac OS X") || ua.Contains("Macintosh")) return "macOS";
        if (ua.Contains("CrOS")) return "ChromeOS";
        if (ua.Contains("Linux")) return "Linux";
        if (ua.Length == 0) return "Unknown";
        return "Other";
    }

    private static string Device(string ua)
    {
        if (ua.Contains("bot", StringComparison.OrdinalIgnoreCase) || ua.Contains("spider", StringComparison.OrdinalIgnoreCase) || ua.Contains("crawler", StringComparison.OrdinalIgnoreCase)) return "Bot";
        if (ua.Contains("iPad") || ua.Contains("Tablet")) return "Tablet";
        if (ua.Contains("Mobile") || ua.Contains("Android") || ua.Contains("iPhone")) return "Mobile";
        return "Desktop";
    }

    /// <summary>Tidy the captured client IP: unwrap IPv4-mapped IPv6 (::ffff:1.2.3.4 → 1.2.3.4) and
    /// normalize the IPv6 loopback (::1 → 127.0.0.1). Returns the real client IP in production.</summary>
    public static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        ip = ip.Trim();
        if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase)) ip = ip["::ffff:".Length..];
        if (ip == "::1") ip = "127.0.0.1";
        return ip;
    }
}
