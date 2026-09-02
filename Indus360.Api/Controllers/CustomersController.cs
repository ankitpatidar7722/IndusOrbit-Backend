using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Customers = client subscriptions (cloned from BulkImport's Company Subscription).
/// Phase 1: card list + detail. Create/edit/delete + module settings come next.
/// </summary>
[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly SubscriptionRepository _repo;
    private readonly ProjectAssignmentRepository _assign;
    private readonly ClientExceedRepository _exceed;
    public CustomersController(SubscriptionRepository repo, ProjectAssignmentRepository assign, ClientExceedRepository exceed)
    { _repo = repo; _assign = assign; _exceed = exceed; }

    /// <summary>
    /// Active subscriptions (card list), scoped by Project Assignment:
    ///  • admin (or no UserID header) → ALL clients;
    ///  • non-admin WITH assignments → only the clients (by CompanyUniqueCode) assigned to them;
    ///  • non-admin WITHOUT any assignment → ALL by default, or NOTHING when <paramref name="scope"/>="assigned".
    ///
    /// The optional <c>?scope=assigned</c> (used by the Implementation module's client pickers)
    /// makes scoping STRICT: a non-admin sees only their assigned clients even with zero
    /// assignments (empty list → admin must assign first). The no-scope default keeps the
    /// app-wide "0 assignments = unrestricted" fallback so existing unassigned users on /clients
    /// aren't locked out.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? scope = null)
    {
        var data = await _repo.GetAllAsync();
        var uid = this.CurrentUserId();
        if (uid is null || await _repo.IsAdminAsync(uid.Value)) return Ok(data);

        var strict = string.Equals(scope, "assigned", StringComparison.OrdinalIgnoreCase);
        var codes = (await _assign.GetAssignedCodesAsync(uid.Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) return Ok(strict ? data.Where(_ => false) : data); // strict → none; default → unrestricted

        var filtered = data.Where(d => d.CompanyUniqueCode != null && codes.Contains(d.CompanyUniqueCode.Trim()));
        return Ok(filtered);
    }

    /// <summary>Header stats: total / active / expired.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var (total, active, expired) = await _repo.GetStatsAsync();
        return Ok(new { total, active, expired });
    }

    /// <summary>Full detail for one subscription.</summary>
    [HttpGet("{companyUserId}")]
    public async Task<IActionResult> GetOne(string companyUserId)
    {
        var dto = await _repo.GetByKeyAsync(companyUserId);
        return dto is null ? NotFound(new { message = "Subscription not found." }) : Ok(dto);
    }

    /// <summary>Non-Desktop clients for the copy-modules target dropdown.</summary>
    [HttpGet("dropdown")]
    public async Task<IActionResult> Dropdown() => Ok(new { success = true, data = await _repo.GetClientDropdownAsync() });

    /// <summary>Distinct non-empty ApplicationBaseURL values — for the Application URL dropdown.</summary>
    [HttpGet("app-urls")]
    public async Task<IActionResult> AppUrls() => Ok(await _repo.GetAppBaseUrlsAsync());

    /// <summary>Current ERP/Cloud exceed days + change history for a client (by CompanyUniqueCode).
    /// Read from OUR local app DB — never the shared production control DB.</summary>
    [HttpGet("{code}/exceed")]
    public async Task<IActionResult> GetExceed(string code) => Ok(await _exceed.GetAsync(code));

    /// <summary>Upsert a client's ERP/Cloud exceed days; each day-count change is logged to history.</summary>
    [HttpPost("{code}/exceed")]
    public async Task<IActionResult> SaveExceed(string code, [FromBody] ClientExceedSaveRequest req)
    {
        await _exceed.SaveAsync(code, req, this.CurrentUserId());
        return Ok(await _exceed.GetAsync(code));
    }

    /// <summary>Next client code (IA#####) for a new subscription.</summary>
    [HttpGet("next-code")]
    public async Task<IActionResult> GetNextCode()
    {
        var (code, max) = await _repo.GetNextCodeAsync();
        return Ok(new { companyUniqueCode = code, maxCompanyUniqueCode = max });
    }

    /// <summary>Create a new subscription.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SubscriptionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyUserID))
            return BadRequest(new { success = false, message = "Company Login Name is required." });
        if (string.IsNullOrWhiteSpace(req.CompanyName))
            return BadRequest(new { success = false, message = "Client Name is required." });
        if (await _repo.ExistsAsync(req.CompanyUserID))
            return Conflict(new { success = false, message = $"Login '{req.CompanyUserID}' already exists." });

        await _repo.CreateAsync(req);
        return Ok(new { success = true, message = "Subscription created." });
    }

    /// <summary>Update an existing subscription (supports login-name rename).</summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SubscriptionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyName))
            return BadRequest(new { success = false, message = "Client Name is required." });

        var rows = await _repo.UpdateAsync(req);
        return rows > 0
            ? Ok(new { success = true, message = "Subscription updated." })
            : NotFound(new { success = false, message = "Subscription not found." });
    }

    /// <summary>Password-gated soft delete (auth validated against app Admin/Manager users).</summary>
    [HttpPost("delete-with-auth")]
    public async Task<IActionResult> DeleteWithAuth([FromBody] DeleteSubscriptionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { success = false, message = "User name and password are required." });
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, message = "Reason is required." });

        if (!await _repo.ValidateActorAsync(req.UserName, req.Password))
            return Unauthorized(new { success = false, message = "Invalid credentials or insufficient rights." });

        var rows = await _repo.SoftDeleteAsync(req.CompanyUserID);
        return rows > 0
            ? Ok(new { success = true, message = "Subscription deleted." })
            : NotFound(new { success = false, message = "Subscription not found." });
    }
}
