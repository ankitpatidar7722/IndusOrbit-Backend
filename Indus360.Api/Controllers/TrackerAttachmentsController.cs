using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Attachments on a Tracker row — PDF or image files (e.g. the client-signed Training Sheet).
/// GET list · POST upload (multipart "files") · GET {id}/file (inline view/download) · DELETE {id} (soft).
/// </summary>
[ApiController]
[Route("api/clients/{code}/tracker/{entity}/{rowId:int}/attachments")]
public class TrackerAttachmentsController : ControllerBase
{
    private static readonly string[] Entities = { "training", "milestone", "changerequest", "onsite" };
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".png"] = "image/png", [".webp"] = "image/webp", [".gif"] = "image/gif",
    };
    private const long MaxBytes = 15 * 1024 * 1024;   // per file

    private readonly TrackerAttachmentRepository _repo;
    private readonly IWebHostEnvironment _env;
    public TrackerAttachmentsController(TrackerAttachmentRepository repo, IWebHostEnvironment env) { _repo = repo; _env = env; }

    private static string Norm(string entity) => (entity ?? "").Trim().ToLowerInvariant();

    [HttpGet]
    public async Task<IActionResult> List(string code, string entity, int rowId)
    {
        entity = Norm(entity);
        if (!Entities.Contains(entity)) return BadRequest(new { success = false, message = "Unknown tracker entity." });
        return Ok(await _repo.ListAsync(code, entity, rowId));
    }

    [HttpPost]
    [RequestSizeLimit(80_000_000)]
    public async Task<IActionResult> Upload(string code, string entity, int rowId, [FromForm] List<IFormFile> files)
    {
        entity = Norm(entity);
        if (!Entities.Contains(entity)) return BadRequest(new { success = false, message = "Unknown tracker entity." });
        if (files is null || files.Count == 0) return BadRequest(new { success = false, message = "No file received." });

        foreach (var f in files)
        {
            var ext = Path.GetExtension(f.FileName ?? "");
            if (!Allowed.ContainsKey(ext)) return BadRequest(new { success = false, message = $"\"{f.FileName}\" is not allowed — only PDF or image files (JPG, PNG, WEBP, GIF)." });
            if (f.Length <= 0) return BadRequest(new { success = false, message = $"\"{f.FileName}\" is empty." });
            if (f.Length > MaxBytes) return BadRequest(new { success = false, message = $"\"{f.FileName}\" is larger than 15 MB." });
        }

        var safeCode = string.Concat((code ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'));
        var dir = Path.Combine(_env.UploadsRoot(), "tracker-uploads", safeCode, entity, rowId.ToString());
        Directory.CreateDirectory(dir);

        var saved = 0;
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            var path = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
            await using (var fs = System.IO.File.Create(path)) await f.CopyToAsync(fs);
            await _repo.InsertAsync(code!, entity, rowId, Path.GetFileName(f.FileName), path, Allowed[ext], f.Length, this.CurrentUserId());
            saved++;
        }
        return Ok(new { success = true, saved });
    }

    [HttpGet("{id:long}/file")]
    public async Task<IActionResult> GetFile(string code, long id, [FromQuery] bool download = false)
    {
        var a = await _repo.GetFileAsync(code, id);
        if (a is null || !System.IO.File.Exists(a.StoredPath)) return NotFound();
        var stream = System.IO.File.OpenRead(a.StoredPath);
        var type = a.ContentType ?? "application/octet-stream";
        return download ? File(stream, type, a.OriginalName) : File(stream, type);   // inline → opens in the browser tab
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(string code, long id)
        => await _repo.DeleteAsync(code, id, this.CurrentUserId()) ? Ok(new { success = true }) : NotFound();
}
