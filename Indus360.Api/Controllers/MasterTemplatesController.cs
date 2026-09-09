using Indus360.Api.Repositories;
using Indus360.Api.Services;
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
    private readonly TemplateStatusRepository _status;
    public MasterTemplatesController(IWebHostEnvironment env, TemplateStatusRepository status) { _env = env; _status = status; }

    private static readonly string[] AllowedExt = { ".xlsx", ".xls", ".csv" };
    private const string TrashDir = "_deleted";

    /// <summary>Persistent library — user uploads live here; survives redeploys (outside the deploy
    /// folder in prod). Uploads/deletes always target this.</summary>
    private string Root
    {
        get
        {
            var d = Path.Combine(_env.UploadsRoot(), "master-templates");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Default templates that SHIP inside the deploy folder — the csproj copies
    /// master-templates\** into the publish output, so "deploy only the publish folder" carries them.
    /// Read-only defaults. In dev this equals Root (same folder); in prod it's &lt;deployFolder&gt;\
    /// master-templates, DISTINCT from the persistent Root — so both must be read, otherwise the
    /// shipped templates never show on the server (the bug this fixes).</summary>
    private string ShippedRoot => Path.Combine(_env.ContentRootPath, "master-templates");

    private static bool IsAllowed(FileInfo f) => AllowedExt.Contains(f.Extension.ToLowerInvariant());

    private static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xls" => "application/vnd.ms-excel",
        ".csv" => "text/csv",
        _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>All templates across every group (group == "" for ungrouped root files). Unions the
    /// persistent library (Root) with the shipped defaults (ShippedRoot) — persistent wins on a name
    /// clash — and hides anything the user has soft-deleted (a tombstone in Root/_deleted), so a
    /// removed default stays removed across redeploys.</summary>
    private IEnumerable<(string group, FileInfo file)> Enumerate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // "group|name" already emitted
        var deleted = DeletedKeys();                                        // "group|nameNoExt" tombstoned
        foreach (var rootPath in new[] { Root, ShippedRoot })
        {
            var dir = new DirectoryInfo(rootPath);
            if (!dir.Exists) continue;
            foreach (var (grp, f) in EnumerateRoot(dir))
            {
                if (!seen.Add(grp + "|" + f.Name)) continue;                // persistent copy already emitted
                if (deleted.Contains(grp + "|" + Path.GetFileNameWithoutExtension(f.Name))) continue;
                yield return (grp, f);
            }
        }
    }

    private static IEnumerable<(string group, FileInfo file)> EnumerateRoot(DirectoryInfo root)
    {
        foreach (var f in root.GetFiles().Where(IsAllowed)) yield return ("", f);
        foreach (var d in root.GetDirectories().Where(d => !d.Name.Equals(TrashDir, StringComparison.OrdinalIgnoreCase)))
            foreach (var f in d.GetFiles().Where(IsAllowed)) yield return (d.Name, f);
    }

    /// <summary>Soft-deleted template keys ("group|nameNoExt") from Root/_deleted. Tombstone names are
    /// "{group}__{nameNoExt}_{yyyyMMddHHmmss}{ext}" (see Delete) — parse the group prefix and strip the
    /// trailing _timestamp.</summary>
    private HashSet<string> DeletedKeys()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trash = new DirectoryInfo(Path.Combine(Root, TrashDir));
        if (!trash.Exists) return set;
        foreach (var f in trash.GetFiles())
        {
            var stem = Path.GetFileNameWithoutExtension(f.Name);
            var grp = "";
            var gi = stem.IndexOf("__", StringComparison.Ordinal);
            if (gi >= 0) { grp = stem[..gi]; stem = stem[(gi + 2)..]; }
            var m = System.Text.RegularExpressions.Regex.Match(stem, @"^(.*)_\d{14}$");
            if (m.Success) stem = m.Groups[1].Value;
            set.Add(grp + "|" + stem);
        }
        return set;
    }

    /// <summary>Resolve a safe path for a (group, name) pair under a given root — blocks path traversal.</summary>
    private static string? ResolveUnder(string root, string? group, string? name)
    {
        var safeName = Path.GetFileName(name ?? "");
        if (string.IsNullOrEmpty(safeName)) return null;
        var safeGroup = string.IsNullOrWhiteSpace(group) ? "" : Path.GetFileName(group.Trim());
        return string.IsNullOrEmpty(safeGroup) ? Path.Combine(root, safeName) : Path.Combine(root, safeGroup, safeName);
    }

    /// <summary>Path under the persistent library (used for writes/deletes).</summary>
    private string? ResolvePath(string? group, string? name) => ResolveUnder(Root, group, name);

    /// <summary>Path to READ a template: the persistent copy if present, else the shipped default.</summary>
    private string? ResolveReadPath(string? group, string? name)
    {
        var persistent = ResolveUnder(Root, group, name);
        if (persistent != null && System.IO.File.Exists(persistent)) return persistent;
        var shipped = ResolveUnder(ShippedRoot, group, name);
        if (shipped != null && System.IO.File.Exists(shipped)) return shipped;
        return persistent;   // may not exist → callers check File.Exists
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

    // ---- "Sent to Client" status (recorded per client when templates are emailed) ----
    public sealed class MarkSentRequest
    {
        public string? ClientCode { get; set; }
        public int? SentBy { get; set; }
        public List<TemplateRef> Items { get; set; } = new();
        public sealed class TemplateRef { public string? Group { get; set; } public string? Name { get; set; } }
    }

    /// <summary>The templates already emailed to a client (group + name + when + by whom).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? clientCode)
    {
        if (string.IsNullOrWhiteSpace(clientCode)) return Ok(new { success = true, data = Array.Empty<object>() });
        var rows = await _status.GetForClientAsync(clientCode);
        return Ok(new { success = true, data = rows.Select(r => new { group = r.TemplateGroup, name = r.TemplateName, sentAt = r.SentAt, sentBy = r.SentByName }) });
    }

    /// <summary>Record that the given templates were emailed to a client (called after a successful send).</summary>
    [HttpPost("status")]
    public async Task<IActionResult> MarkSent([FromBody] MarkSentRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ClientCode) || req.Items.Count == 0)
            return BadRequest(new { success = false, message = "clientCode and items are required." });
        await _status.MarkSentAsync(req.ClientCode!, req.SentBy, req.Items.Select(i => (i.Group ?? "", i.Name ?? "")));
        return Ok(new { success = true });
    }

    /// <summary>Download one template file.</summary>
    [HttpGet("download")]
    public IActionResult Download([FromQuery] string name, [FromQuery] string? group)
    {
        var path = ResolveReadPath(group, name);
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
    /// app-wide no-hard-delete rule). A persistent copy is MOVED; a shipped default (read-only, inside
    /// the deploy folder) is COPIED to the trash as a tombstone so Enumerate hides it without touching
    /// the deploy folder — the removal then sticks across redeploys.</summary>
    [HttpDelete]
    public IActionResult Delete([FromQuery] string name, [FromQuery] string? group)
    {
        var path = ResolveReadPath(group, name);
        if (path is null || !System.IO.File.Exists(path)) return Ok(new { success = false, message = "Template not found." });

        var trash = Path.Combine(Root, TrashDir);
        Directory.CreateDirectory(trash);
        var prefix = string.IsNullOrWhiteSpace(group) ? "" : Path.GetFileName(group.Trim()) + "__";
        var dest = Path.Combine(trash, $"{prefix}{Path.GetFileNameWithoutExtension(name)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(name)}");
        var persistent = ResolvePath(group, name);
        if (persistent != null && System.IO.File.Exists(persistent))
            System.IO.File.Move(persistent, dest, overwrite: true);   // user upload → move to trash
        else
            System.IO.File.Copy(path, dest, overwrite: true);          // shipped default → tombstone only
        return Ok(new { success = true, message = "Template removed." });
    }
}
