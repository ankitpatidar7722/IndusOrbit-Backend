using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Finalized Kick-Off / Sign-Off documents for a client. The template is edited in the
/// browser (public/kickoff.html + signoff.html), then the finalized HTML is saved here so
/// it can be viewed and downloaded later for future reference (after emailing the client).
/// Stored in app.ClientDocuments (local IndusTaskManagement) — one latest version per
/// (ClientCode, DocType).
/// </summary>
[ApiController]
[Route("api/client-documents")]
public sealed class ClientDocumentsController : ControllerBase
{
    private readonly ClientDocumentRepository _repo;
    private readonly PdfRenderer _pdf;
    public ClientDocumentsController(ClientDocumentRepository repo, PdfRenderer pdf)
    {
        _repo = repo;
        _pdf = pdf;
    }

    /// <summary>Save (upsert) the finalized document.</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveClientDocumentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ClientCode))
            return BadRequest(new { success = false, message = "ClientCode is required." });
        if (string.IsNullOrWhiteSpace(req.HtmlContent))
            return BadRequest(new { success = false, message = "Document content is empty." });

        var docId = await _repo.SaveAsync(req);
        return Ok(new { success = true, message = "Document saved.", docId, savedAt = DateTime.Now });
    }

    /// <summary>Status of saved documents for a client (no HTML) — drives the tab badges.</summary>
    [HttpGet("{clientCode}")]
    public async Task<IActionResult> Meta(string clientCode)
    {
        var rows = await _repo.GetMetaAsync(clientCode);
        return Ok(new { success = true, data = rows });
    }

    /// <summary>Full saved document (with HTML) for view / download / re-edit.</summary>
    [HttpGet("{clientCode}/{docType}")]
    public async Task<IActionResult> Get(string clientCode, string docType)
    {
        var doc = await _repo.GetAsync(clientCode, docType);
        if (doc is null)
            return Ok(new { success = false, message = "No saved document.", data = (ClientDocumentDto?)null });
        return Ok(new { success = true, data = doc });
    }

    /// <summary>
    /// The saved finalized document rendered to a PDF (headless-browser print of its own A4
    /// print CSS) — used to attach a proper PDF when emailing the client. Returns 404 when no
    /// document is saved, 503 when the server has no browser to render with (caller falls back
    /// to the HTML attachment).
    /// </summary>
    [HttpGet("{clientCode}/{docType}/pdf")]
    public async Task<IActionResult> Pdf(string clientCode, string docType)
    {
        var doc = await _repo.GetAsync(clientCode, docType);
        if (doc is null || string.IsNullOrWhiteSpace(doc.HtmlContent))
            return NotFound("No saved document to render.");
        if (!_pdf.IsAvailable)
            return StatusCode(503, "PDF rendering is unavailable (no browser found on the server).");

        var bytes = await _pdf.RenderAsync(doc.HtmlContent);
        if (bytes is null || bytes.Length == 0)
            return StatusCode(500, "Could not render the PDF.");

        var safe = $"{docType}-{clientCode}".Replace(' ', '-');
        return File(bytes, "application/pdf", $"{safe}.pdf");
    }
}
