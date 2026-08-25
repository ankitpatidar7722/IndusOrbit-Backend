using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Master-data Excel template library — GROUP-aware and GLOBAL (shown for every client, no
/// per-client upload). Admins upload blank master templates organised by group (e.g. Item
/// Master, Ledger Master, Tool Master); from any client's "Template Master Excel" tab they can
/// download, or multi-select and email them to the client to fill in for master upload.
/// Files live under &lt;contentRoot&gt;/master-templates/&lt;group&gt;/&lt;file&gt;.
/// </summary>
[ApiController]
[Route("api/master-templates")]
public sealed class MasterTemplatesController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    public MasterTemplatesController(IWebHostEnvironment env) => _env = env;

    private static readonly string[] AllowedExt = { ".xlsx", ".xls", ".csv" };
    private const string TrashDir = "_deleted";

    private string Root
    {
        get
        {
            var d = Path.Combine(_env.ContentRootPath, "master-templates");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static bool IsAllowed(FileInfo f) => AllowedExt.Contains(f.Extension.ToLowerInvariant());

    private static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xls" => "application/vnd.ms-excel",
        ".csv" => "text/csv",
        _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>All templates across every group (group == "" for ungrouped root files).</summary>
    private IEnumerable<(string group, FileInfo file)> Enumerate()
    {
        var root = new DirectoryInfo(Root);
        foreach (var f in root.GetFiles().Where(IsAllowed)) yield return ("", f);
        foreach (var d in root.GetDirectories().Where(d => !d.Name.Equals(TrashDir, StringComparison.OrdinalIgnoreCase)))
            foreach (var f in d.GetFiles().Where(IsAllowed)) yield return (d.Name, f);
    }

    /// <summary>Resolve a safe path for a (group, name) pair — blocks path traversal.</summary>
    private string? ResolvePath(string? group, string? name)
    {
        var safeName = Path.GetFileName(name ?? "");
        if (string.IsNullOrEmpty(safeName)) return null;
        var safeGroup = string.IsNullOrWhiteSpace(group) ? "" : Path.GetFileName(group.Trim());
        return string.IsNullOrEmpty(safeGroup) ? Path.Combine(Root, safeName) : Path.Combine(Root, safeGroup, safeName);
    }

    /// <summary>All templates in the library, grouped.</summary>
    [HttpGet]
    public IActionResult List()
    {
        var data = Enumerate()
            .OrderBy(x => x.group).ThenBy(x => x.file.Name)
            .Select(x => new { group = x.group, name = x.file.Name, size = x.file.Length, modifiedAt = x.file.LastWriteTime });
        return Ok(new { success = true, data });
    }

    /// <summary>Download one template file.</summary>
    [HttpGet("download")]
    public IActionResult Download([FromQuery] string name, [FromQuery] string? group)
    {
        var path = ResolvePath(group, name);
        if (path is null || !System.IO.File.Exists(path)) return NotFound(new { success = false, message = "Template not found." });
        return PhysicalFile(path, ContentTypeFor(name), Path.GetFileName(name));
    }

    /// <summary>Upload a template into a group (.xlsx/.xls/.csv). Blank group = ungrouped.</summary>
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? group)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { success = false, message = "No file selected." });
        var name = Path.GetFileName(file.FileName);
        if (!AllowedExt.Contains(Path.GetExtension(name).ToLowerInvariant()))
            return BadRequest(new { success = false, message = "Only Excel/CSV files (.xlsx, .xls, .csv) are allowed." });

        var safeGroup = string.IsNullOrWhiteSpace(group) ? "" : Path.GetFileName(group.Trim());
        var dir = string.IsNullOrEmpty(safeGroup) ? Root : Path.Combine(Root, safeGroup);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        await using (var fs = new FileStream(path, FileMode.Create))
            await file.CopyToAsync(fs);
        return Ok(new { success = true, message = "Template uploaded.", group = safeGroup, name });
    }

    /// <summary>Soft-delete: move the file into master-templates/_deleted (recoverable, per the
    /// app-wide no-hard-delete rule).</summary>
    [HttpDelete]
    public IActionResult Delete([FromQuery] string name, [FromQuery] string? group)
    {
        var path = ResolvePath(group, name);
        if (path is null || !System.IO.File.Exists(path)) return Ok(new { success = false, message = "Template not found." });

        var trash = Path.Combine(Root, TrashDir);
        Directory.CreateDirectory(trash);
        var prefix = string.IsNullOrWhiteSpace(group) ? "" : Path.GetFileName(group.Trim()) + "__";
        var dest = Path.Combine(trash, $"{prefix}{Path.GetFileNameWithoutExtension(name)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(name)}");
        System.IO.File.Move(path, dest, overwrite: true);
        return Ok(new { success = true, message = "Template removed." });
    }
}
