using System.Diagnostics;
using System.Text;

namespace Indus360.Api.Services;

/// <summary>
/// Renders self-contained HTML to a PDF using an installed Chromium browser
/// (Chrome / Edge / Brave) in headless <c>--print-to-pdf</c> mode — the same engine as the
/// browser's "Save as PDF", so the document's own A4 print CSS is honoured. No bundled
/// Chromium / NuGet: we shell out to whatever browser the machine already has. Falls back
/// gracefully (RenderAsync → null) when no browser is present or rendering fails, so callers
/// can attach the raw HTML instead.
/// </summary>
public sealed class PdfRenderer
{
    private readonly string? _browser;
    public PdfRenderer() => _browser = FindBrowser();

    public bool IsAvailable => _browser is not null;

    /// <summary>HTML → PDF bytes, or null if no browser is available or rendering fails.</summary>
    public async Task<byte[]?> RenderAsync(string html, CancellationToken ct = default)
    {
        if (_browser is null || string.IsNullOrWhiteSpace(html)) return null;

        // Force background colours/images to print (print CSS otherwise drops them).
        const string forceColor = "<style>@media print{*{-webkit-print-color-adjust:exact !important;print-color-adjust:exact !important;}}</style>";
        html = html.Contains("</head>", StringComparison.OrdinalIgnoreCase)
            ? html.Replace("</head>", forceColor + "</head>", StringComparison.OrdinalIgnoreCase)
            : forceColor + html;

        var dir = Path.Combine(Path.GetTempPath(), "indus360-pdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var inPath = Path.Combine(dir, "doc.html");
        var outPath = Path.Combine(dir, "doc.pdf");
        try
        {
            await File.WriteAllTextAsync(inPath, html, new UTF8Encoding(false), ct);

            var psi = new ProcessStartInfo
            {
                FileName = _browser,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in new[]
            {
                "--headless=new",
                "--disable-gpu",
                "--no-sandbox",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-extensions",
                "--run-all-compositor-stages-before-draw",
                "--virtual-time-budget=8000",
                "--print-to-pdf-no-header",
                $"--print-to-pdf={outPath}",
                new Uri(inPath).AbsoluteUri, // file:///…
            }) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await proc.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { proc.Kill(true); } catch { /* ignore */ } return null; }

            if (!File.Exists(outPath)) return null;
            var bytes = await File.ReadAllBytesAsync(outPath, ct);
            return bytes.Length > 0 ? bytes : null;
        }
        catch { return null; }
        finally { try { Directory.Delete(dir, true); } catch { /* best-effort cleanup */ } }
    }

    /// <summary>First installed Chromium browser found, or null. Override with INDUS360_CHROME.</summary>
    private static string? FindBrowser()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("INDUS360_CHROME"),
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pfx86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pfx86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
        };
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
        return null;
    }
}
