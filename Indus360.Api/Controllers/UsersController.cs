using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>Admin → User Management: user CRUD + per-user Module Authentication.</summary>
[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly UserAdminRepository _repo;
    private readonly IWebHostEnvironment _env;
    public UsersController(UserAdminRepository repo, IWebHostEnvironment env)
    {
        _repo = repo;
        _env = env;
    }

    // Profile photos live on the backend's own disk (like pm-uploads / chat-uploads),
    // with the stored filename saved in app.Users.PhotoPath and served back by GET below.
    private string PhotoDir => Path.Combine(_env.ContentRootPath, "user-uploads");

    [HttpGet]
    public async Task<IActionResult> List()
    {
        try { return Ok(new { success = true, data = await _repo.ListAsync() }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<UserListRow>() }); }
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups()
    {
        try { return Ok(new { success = true, data = await _repo.GetLookupsAsync() }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        try
        {
            var u = await _repo.GetAsync(id);
            return u is null ? NotFound(new { success = false, message = "User not found." })
                             : Ok(new { success = true, data = u });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Email))
            return Ok(new { success = false, message = "Full name and email are required." });
        if (string.IsNullOrWhiteSpace(req.Password))
            return Ok(new { success = false, message = "Password is required for a new user." });
        try { var id = await _repo.CreateAsync(req, this.CurrentUserId()); return Ok(new { success = true, message = "User created.", userId = id }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UserSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Email))
            return Ok(new { success = false, message = "Full name and email are required." });
        req.UserId = id;
        try { await _repo.UpdateAsync(req, this.CurrentUserId()); return Ok(new { success = true, message = "User updated." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        try { var n = await _repo.DeleteAsync(id, this.CurrentUserId()); return Ok(new { success = n > 0, message = n > 0 ? "User deleted." : "User not found." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ---------------- Self-service (Settings page: the logged-in user edits their own account) ----------------

    [HttpPost("{id:long}/self-profile")]
    public async Task<IActionResult> SelfProfile(long id, [FromBody] SelfProfileRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Email))
            return Ok(new { success = false, message = "Name and email are required." });
        try { await _repo.UpdateSelfProfileAsync(id, req.FullName.Trim(), req.Email.Trim()); return Ok(new { success = true, message = "Profile updated." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{id:long}/self-password")]
    public async Task<IActionResult> SelfPassword(long id, [FromBody] SelfPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return Ok(new { success = false, message = "Current and new password are required." });
        try
        {
            var r = await _repo.ChangeSelfPasswordAsync(id, req.CurrentPassword, req.NewPassword);
            return Ok(new { success = r == "Success", message = r });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("{id:long}/self-smtp")]
    public async Task<IActionResult> SelfSmtp(long id, [FromBody] UserSaveRequest req)
    {
        try { await _repo.UpdateSelfSmtpAsync(id, req); return Ok(new { success = true, message = "Email settings saved." }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    /// <summary>Lightweight, privacy-safe profile card (name/role/email/mobile/photo) for any user —
    /// used by the "view profile" popup in chat. Never returns SMTP / password fields.</summary>
    [HttpGet("{id:long}/card")]
    public async Task<IActionResult> Card(long id)
    {
        try
        {
            var u = await _repo.GetAsync(id);
            if (u is null) return Ok(new { success = false, message = "User not found." });
            return Ok(new { success = true, data = new
            {
                userId = u.UserId, fullName = u.FullName, email = u.Email,
                mobile = u.Mobile, role = u.Role, employeeCode = u.EmployeeCode,
                hasPhoto = !string.IsNullOrWhiteSpace(u.PhotoPath),
            } });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ── Profile photo (disk-stored, WhatsApp-style circular avatar) ──

    /// <summary>Upload/replace a user's profile photo (cropped square image as a data URL / base64).</summary>
    [HttpPost("{id:long}/photo")]
    [RequestSizeLimit(10_485_760)] // 10 MB
    public async Task<IActionResult> UploadPhoto(long id, [FromBody] UserPhotoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.ImageBase64)) return Ok(new { success = false, message = "No image provided." });
        try
        {
            var raw = req.ImageBase64;
            var ext = raw.Contains("image/png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            var comma = raw.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (comma >= 0) raw = raw[(comma + 7)..];
            byte[] bytes;
            try { bytes = Convert.FromBase64String(raw); } catch { return Ok(new { success = false, message = "Invalid image data." }); }
            if (bytes.Length == 0) return Ok(new { success = false, message = "Empty image." });

            Directory.CreateDirectory(PhotoDir);
            var stored = $"{id}_{Guid.NewGuid():N}{ext}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(PhotoDir, stored), bytes);

            var old = await _repo.SetPhotoAsync(id, stored);
            if (!string.IsNullOrWhiteSpace(old))
            {
                var op = Path.Combine(PhotoDir, old);
                if (System.IO.File.Exists(op)) { try { System.IO.File.Delete(op); } catch { /* best effort */ } }
            }
            return Ok(new { success = true, photoUrl = $"/api/users/{id}/photo", version = stored });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    /// <summary>Serve a user's profile photo (or 404 if none). Public read — used as an &lt;img&gt; src.</summary>
    [HttpGet("{id:long}/photo")]
    public async Task<IActionResult> GetPhoto(long id)
    {
        var name = await _repo.GetPhotoAsync(id);
        if (string.IsNullOrWhiteSpace(name)) return NotFound();
        var full = Path.Combine(PhotoDir, name);
        if (!System.IO.File.Exists(full)) return NotFound();
        var ct = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return PhysicalFile(full, ct);
    }

    /// <summary>Remove a user's profile photo (file + PhotoPath).</summary>
    [HttpDelete("{id:long}/photo")]
    public async Task<IActionResult> DeletePhoto(long id)
    {
        var old = await _repo.SetPhotoAsync(id, null);
        if (!string.IsNullOrWhiteSpace(old))
        {
            var op = Path.Combine(PhotoDir, old);
            if (System.IO.File.Exists(op)) { try { System.IO.File.Delete(op); } catch { /* best effort */ } }
        }
        return Ok(new { success = true });
    }

    [HttpGet("{id:long}/modules")]
    public async Task<IActionResult> ModuleAuth(long id)
    {
        try { return Ok(new { success = true, data = await _repo.GetModuleAuthAsync(id) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, data = Array.Empty<ModuleAuthRow>() }); }
    }

    [HttpPost("{id:long}/modules")]
    public async Task<IActionResult> SaveModuleAuth(long id, [FromBody] SaveModuleAuthRequest req)
    {
        req.UserId = id;
        try { var n = await _repo.SaveModuleAuthAsync(req); return Ok(new { success = true, message = $"Saved authority for {n} module(s).", savedCount = n }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message, savedCount = 0 }); }
    }
}
