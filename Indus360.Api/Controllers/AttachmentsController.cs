using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Upload / list / download / delete Point Management file attachments.</summary>
[ApiController]
[Route("api/point-management")]
public sealed class AttachmentsController : ControllerBase
{
    private readonly AttachmentRepository _repo;
    private readonly IWebHostEnvironment _env;

    public AttachmentsController(AttachmentRepository repo, IWebHostEnvironment env)
    {
        _repo = repo;
        _env = env;
    }

    private string UploadDir => Path.Combine(_env.ContentRootPath, "pm-uploads");

    [HttpPost("points/{id:int}/attachments")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> Upload(int id, IFormFile file, [FromForm] int uploadedBy)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file provided." });

        Directory.CreateDirectory(UploadDir);
        var ext = Path.GetExtension(file.FileName);
        var stored = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(UploadDir, stored);
        await using (var fs = System.IO.File.Create(full))
            await file.CopyToAsync(fs);

        var attachmentId = await _repo.InsertAsync(id, stored, full, uploadedBy, file.FileName);
        return Ok(new { attachmentId });
    }

    [HttpGet("points/{id:int}/attachments")]
    public async Task<IActionResult> List(int id) => Ok(await _repo.GetForPointAsync(id));

    [HttpGet("attachments/{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var f = await _repo.GetFileAsync(id);
        if (f is null || !System.IO.File.Exists(f.FilePath)) return NotFound();
        var name = string.IsNullOrWhiteSpace(f.OriginalFileName) ? Path.GetFileName(f.FilePath) : f.OriginalFileName!;
        return PhysicalFile(f.FilePath, "application/octet-stream", name);
    }

    [HttpDelete("attachments/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var path = await _repo.DeleteAsync(id);
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try { System.IO.File.Delete(path); } catch { /* best effort */ }
        }
        return Ok(new { ok = true });
    }
}
