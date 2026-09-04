using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// API for the Point Management module (migrated TMS). Reads/writes the
/// IndusTaskManagement database via the Tms* repositories. Lookups + admin
/// dashboard first; per-page workflow endpoints are added in later phases.
/// </summary>
[ApiController]
[Route("api/point-management")]
public sealed class PointManagementController : ControllerBase
{
    private readonly TmsLookupRepository _lookups;
    private readonly PointRepository _points;
    private readonly TmsUserRepository _users;
    private readonly PointWorkflowRepository _workflow;
    private readonly TicketRepository _tickets;
    private readonly TmsReportRepository _reports;
    private readonly TmsAdminRepository _admin;
    private readonly NotificationRepository _notify;
    private readonly ClientRepository _clients;

    public PointManagementController(
        TmsLookupRepository lookups, PointRepository points, TmsUserRepository users,
        PointWorkflowRepository workflow, TicketRepository tickets, TmsReportRepository reports,
        TmsAdminRepository admin, NotificationRepository notify, ClientRepository clients)
    {
        _lookups = lookups;
        _points = points;
        _users = users;
        _workflow = workflow;
        _tickets = tickets;
        _reports = reports;
        _admin = admin;
        _notify = notify;
        _clients = clients;
    }

    // ---------------- Identity / authorization ----------------
    /// <summary>Resolve the logged-in Indus360 user to their TMS identity + allowed PM modules.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me([FromQuery] long userId, [FromQuery] string? email)
        => Ok(await _users.GetContextAsync(userId, email ?? ""));

    // ---------------- Lookups ----------------
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? role)
        => Ok(await _lookups.GetUsersAsync(string.IsNullOrWhiteSpace(role) ? null : role));

    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers() => Ok(await _lookups.GetCustomersAsync());

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts() => Ok(await _lookups.GetProductsAsync());

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories() => Ok(await _lookups.GetCategoriesAsync());

    // ---------------- Admin dashboard ----------------
    [HttpGet("dashboard/admin/stats")]
    public async Task<IActionResult> GetAdminStats(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? devId, [FromQuery] int? custId)
        => Ok(await _points.GetAdminStatsAsync(from, to, devId, custId));

    /// <summary>Points behind a stat card / filtered grid.</summary>
    [HttpGet("points")]
    public async Task<IActionResult> GetPoints(
        [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? devId, [FromQuery] int? custId, [FromQuery] int? reportedBy)
        => Ok(await _points.GetFilteredAsync(status, from, to, devId, custId, reportedBy));

    [HttpGet("points/queue")]
    public async Task<IActionResult> GetQueue() => Ok(await _points.GetQueueAsync());

    // ---------------- Create (Add Point) ----------------
    [HttpPost("points")]
    public async Task<IActionResult> CreatePoint([FromBody] NewPointRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(new { message = "Description is required." });
        if (req.ReportedByID <= 0)
            return BadRequest(new { message = "Your account is not linked to a Point Management (TMS) user, so it can't report points. Ask an admin to add you in Manage Users, or log in as a team member." });
        if ((req.CustomerID <= 0 && string.IsNullOrWhiteSpace(req.CustomerName)) || req.ProductID <= 0)
            return BadRequest(new { message = "Customer and Product are required." });
        var id = await _workflow.InsertPointAsync(req);
        return Ok(new { pointId = id });
    }

    // ---------------- Edit / Delete a Queue point (Manage Points) ----------------
    /// <summary>Edit an editable (Queue-status) point. Returns { ok, message } (ok=false with a reason
    /// when the point isn't a Queue point) so the client can surface the message.</summary>
    [HttpPut("points/{id:int}")]
    public async Task<IActionResult> UpdatePoint(int id, [FromBody] NewPointRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Description))
            return Ok(new { ok = false, message = "Description is required." });
        var (ok, message) = await _workflow.UpdateQueuePointAsync(id, req);
        return Ok(new { ok, message });
    }

    /// <summary>Soft-delete a Queue point (IsDeletedTransaction=1). ok=false when it isn't a Queue point.</summary>
    [HttpDelete("points/{id:int}")]
    public async Task<IActionResult> DeletePoint(int id)
    {
        var (ok, message) = await _workflow.DeleteQueuePointAsync(id);
        return Ok(new { ok, message });
    }

    // ---------------- Triage (Verify Tickets) ----------------
    [HttpGet("points/verification")]
    public async Task<IActionResult> GetVerificationQueue([FromQuery] int vs = 0)
        => Ok(await _points.GetVerificationQueueAsync(vs));

    [HttpPost("points/{id:int}/verify")]
    public async Task<IActionResult> Verify(int id)
        => await _workflow.MarkVerifiedAsync(id) ? Ok(new { ok = true }) : NotFound();

    public sealed class UnActiveRequest { public string? AdminRemark { get; set; } }

    [HttpPost("points/{id:int}/unactive")]
    public async Task<IActionResult> UnActive(int id, [FromBody] UnActiveRequest req)
        => await _workflow.MarkUnActiveAsync(id, req?.AdminRemark) ? Ok(new { ok = true }) : NotFound();

    /// <summary>Undo an "Un-Active" — put a rejected point back in the pending verification queue.</summary>
    [HttpPost("points/{id:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int id)
        => await _workflow.ReactivateAsync(id) ? Ok(new { ok = true }) : NotFound();

    // ---------------- Role boards ----------------
    [HttpGet("developer/board")]
    public async Task<IActionResult> DeveloperBoard([FromQuery] int? userId) => Ok(await _points.GetDeveloperBoardAsync(userId));

    [HttpGet("tester/queue")]
    public async Task<IActionResult> TesterQueue() => Ok(await _points.GetTesterQueueAsync());

    [HttpGet("support/queue")]
    public async Task<IActionResult> SupportQueue([FromQuery] int? userId) => Ok(await _points.GetSupportQueueAsync(userId));

    [HttpGet("merge/queue")]
    public async Task<IActionResult> MergeQueue() => Ok(await _points.GetMergeQueueAsync());

    // ---------------- Point detail + history ----------------
    [HttpGet("points/{id:int}/detail")]
    public async Task<IActionResult> Detail(int id)
    {
        var d = await _points.GetDetailAsync(id);
        return d is null ? NotFound() : Ok(d);
    }

    [HttpGet("points/{id:int}/history")]
    public async Task<IActionResult> History(int id) => Ok(await _points.GetHistoryAsync(id));

    // ---------------- Developer timer + transitions ----------------
    [HttpPost("points/{id:int}/timer/start")]
    public async Task<IActionResult> StartTimer(int id, [FromBody] PointActionRequest r)
    { await _workflow.StartTimerAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/timer/pause")]
    public async Task<IActionResult> PauseTimer(int id, [FromBody] PointActionRequest r)
    { await _workflow.PauseTimerAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/timer/resume")]
    public async Task<IActionResult> ResumeTimer(int id, [FromBody] PointActionRequest r)
    { await _workflow.ResumeTimerAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/timer/complete")]
    public async Task<IActionResult> CompleteTimer(int id, [FromBody] PointActionRequest r)
    { await _workflow.CompleteTimerAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/send-to-support")]
    public async Task<IActionResult> SendToSupport(int id, [FromBody] PointActionRequest r)
    { await _workflow.SendToSupportAsync(id, r.UserId, r.Remark); await _notify.NotifyPointEventAsync(id, "sent-to-support"); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/send-to-merge")]
    public async Task<IActionResult> SendToMerge(int id, [FromBody] PointActionRequest r)
        => await _workflow.SendToMergeAsync(id, r.UserId) ? Ok(new { ok = true }) : BadRequest(new { message = "Point is not in SupportVerified state." });

    [HttpPost("points/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] PointActionRequest r)
    {
        if (r.Status is not ("Hold" or "Reject"))
            return BadRequest(new { message = "Only Hold or Reject allowed here." });
        return await _workflow.SetStatusAsync(id, r.UserId, r.Status, r.Remark) ? Ok(new { ok = true }) : NotFound();
    }

    // ---------------- Tester / QC ----------------
    [HttpPost("points/{id:int}/tester/start")]
    public async Task<IActionResult> TesterStart(int id, [FromBody] PointActionRequest r)
    { await _workflow.StartTesterAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/tester/complete")]
    public async Task<IActionResult> TesterComplete(int id, [FromBody] PointActionRequest r)
    { await _workflow.CompleteTestingAsync(id, r.UserId); return Ok(new { ok = true }); }

    /// <summary>Close a point (admin action from the Manage Points grid).</summary>
    [HttpPost("points/{id:int}/close")]
    public async Task<IActionResult> ClosePoint(int id)
        => await _workflow.ClosePointAsync(id) ? Ok(new { ok = true }) : Ok(new { ok = false, message = "Point is already closed or not found." });

    /// <summary>"Send To → Tracker": create (or reuse, if already linked) a Change Request from
    /// this point under the matching client, so it shows up in that client's /clients Tracker tab.</summary>
    [HttpPost("points/{id:int}/send-to-tracker")]
    public async Task<IActionResult> SendToTracker(int id)
    {
        try
        {
            var (crId, clientCode, alreadyLinked) = await _clients.SendPointToTrackerAsync(id, this.CurrentUserId());
            return Ok(new { success = true, crId, clientCode, alreadyLinked });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("points/{id:int}/tester/verify-close")]
    public async Task<IActionResult> TesterVerifyClose(int id, [FromBody] PointActionRequest r)
    {
        if (!await _workflow.VerifyAndCloseAsync(id, r.UserId, r.Remark))
            return BadRequest(new { message = "Point is not in Testing-Completed state." });
        await _notify.NotifyPointEventAsync(id, "closed");
        return Ok(new { ok = true });
    }

    [HttpPost("points/{id:int}/tester/reopen")]
    public async Task<IActionResult> TesterReopen(int id, [FromBody] PointActionRequest r)
    { await _workflow.ReopenForDeveloperAsync(id, r.UserId, r.Remark); await _notify.NotifyPointEventAsync(id, "reopened"); return Ok(new { ok = true }); }

    // ---------------- Support ----------------
    [HttpPost("points/{id:int}/support/start")]
    public async Task<IActionResult> SupportStart(int id, [FromBody] PointActionRequest r)
    { await _workflow.StartSupportAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/support/complete")]
    public async Task<IActionResult> SupportComplete(int id, [FromBody] PointActionRequest r)
    { await _workflow.CompleteSupportAsync(id, r.UserId); return Ok(new { ok = true }); }

    [HttpPost("points/{id:int}/support/verify")]
    public async Task<IActionResult> SupportVerify(int id, [FromBody] PointActionRequest r)
        => await _workflow.MarkSupportVerifiedAsync(id, r.UserId, r.Remark) ? Ok(new { ok = true }) : BadRequest(new { message = "Point is not in PendingSupport state." });

    [HttpPost("points/{id:int}/support/send-to-qc")]
    public async Task<IActionResult> SupportSendToQc(int id, [FromBody] PointActionRequest r)
        => await _workflow.SupportSendToQcAsync(id, r.UserId, r.Remark) ? Ok(new { ok = true }) : BadRequest(new { message = "Point is not eligible." });

    [HttpPost("points/{id:int}/support/reopen")]
    public async Task<IActionResult> SupportReopen(int id, [FromBody] PointActionRequest r)
        => await _workflow.ReopenFromSupportAsync(id, r.UserId, r.Remark) ? Ok(new { ok = true }) : BadRequest(new { message = "Point is not in PendingSupport state." });

    // ---------------- Merge ----------------
    [HttpPost("points/{id:int}/merge/approve")]
    public async Task<IActionResult> MergeApprove(int id, [FromBody] PointActionRequest r)
        => await _workflow.MergeApproveAsync(id, r.UserId, r.Remark) ? Ok(new { ok = true }) : BadRequest(new { message = "Point is not in PendingMerge state." });

    [HttpPost("points/{id:int}/merge/reopen")]
    public async Task<IActionResult> MergeReopen(int id, [FromBody] PointActionRequest r)
        => await _workflow.MergeReopenAsync(id, r.UserId, r.Remark) ? Ok(new { ok = true }) : BadRequest(new { message = "Point is not in PendingMerge state." });

    // ---------------- Tickets (Assign / Manage Assignments) ----------------
    [HttpPost("tickets/assign")]
    public async Task<IActionResult> Assign([FromBody] AssignRequest req)
    {
        if (req.DevId <= 0 || req.PointIds.Count == 0)
            return BadRequest(new { message = "Select at least one point and a developer." });
        if (req.CreatedById <= 0)
            return BadRequest(new { message = "Your account is not linked to a Point Management (TMS) user. Ask an admin to add you in Manage Users." });
        var ticketId = await _tickets.CreateAndAssignAsync(req);
        await _notify.NotifyUserAsync(req.DevId, $"{req.PointIds.Count} point(s) assigned to you", "/point-management/developer");
        return Ok(new { ticketId });
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> GetTickets() => Ok(await _tickets.GetTicketsAsync());

    /// <summary>Point-level rows for the Manage Assignments grid (one row per assigned point).</summary>
    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments() => Ok(await _tickets.GetAssignmentRowsAsync());

    public sealed class ReassignRequest { public int NewDevId { get; set; } public int AssignedByUserId { get; set; } }

    [HttpPost("tickets/{id:int}/reassign")]
    public async Task<IActionResult> ReassignTicket(int id, [FromBody] ReassignRequest r)
        => await _tickets.ReassignAsync(id, r.NewDevId, r.AssignedByUserId) ? Ok(new { ok = true }) : NotFound();

    [HttpPost("tickets/{id:int}/close")]
    public async Task<IActionResult> CloseTicket(int id)
        => await _tickets.CloseAsync(id) ? Ok(new { ok = true }) : NotFound();

    // ---------------- Reports ----------------
    [HttpGet("reports/time")]
    public async Task<IActionResult> TimeReport(
        [FromQuery] int? custId, [FromQuery] int? devId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _reports.GetReportAsync(custId, devId, from, to));

    // ---------------- Admin: Users ----------------
    public sealed class ActiveRequest { public bool Active { get; set; } }

    [HttpGet("admin/users")]
    public async Task<IActionResult> AdminUsers() => Ok(await _admin.GetAllUsersAsync());

    [HttpPost("admin/users")]
    public async Task<IActionResult> SaveUser([FromBody] TmsUserSave u)
    {
        if (string.IsNullOrWhiteSpace(u.FullName) || string.IsNullOrWhiteSpace(u.Email))
            return BadRequest(new { message = "Name and Email are required." });
        return Ok(new { userId = await _admin.UpsertUserAsync(u) });
    }

    [HttpPost("admin/users/{id:int}/active")]
    public async Task<IActionResult> SetUserActive(int id, [FromBody] ActiveRequest r)
    { await _admin.SetUserActiveAsync(id, r.Active); return Ok(new { ok = true }); }

    // ---------------- Admin: Customers ----------------
    [HttpGet("admin/customers")]
    public async Task<IActionResult> AdminCustomers() => Ok(await _admin.GetAllCustomersAsync());

    [HttpPost("admin/customers")]
    public async Task<IActionResult> SaveCustomer([FromBody] TmsCustomerSave c)
    {
        if (string.IsNullOrWhiteSpace(c.CompanyName))
            return BadRequest(new { message = "Company Name is required." });
        return Ok(new { customerId = await _admin.UpsertCustomerAsync(c) });
    }

    [HttpPost("admin/customers/{id:int}/active")]
    public async Task<IActionResult> SetCustomerActive(int id, [FromBody] ActiveRequest r)
    { await _admin.SetCustomerActiveAsync(id, r.Active); return Ok(new { ok = true }); }

    // ---------------- Admin: Permissions ----------------
    [HttpGet("admin/app-users")]
    public async Task<IActionResult> AppUsers() => Ok(await _admin.GetAppUsersAsync());

    [HttpGet("admin/pm-modules")]
    public async Task<IActionResult> PmModules() => Ok(await _admin.GetPmModulesAsync());

    [HttpGet("admin/user-modules")]
    public async Task<IActionResult> UserModules([FromQuery] long userId) => Ok(await _admin.GetUserPmModuleIdsAsync(userId));

    [HttpPost("admin/user-modules")]
    public async Task<IActionResult> SaveUserModules([FromBody] SavePermissionsRequest req)
    { await _admin.SaveUserPmModulesAsync(req.UserId, req.ModuleIds); return Ok(new { ok = true }); }

    // ---------------- Notifications ----------------
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] string email) => Ok(await _notify.GetForEmailAsync(email ?? ""));

    [HttpGet("notifications/unread")]
    public async Task<IActionResult> NotificationsUnread([FromQuery] string email) => Ok(new { count = await _notify.GetUnreadCountForEmailAsync(email ?? "") });

    [HttpPost("notifications/{id:int}/read")]
    public async Task<IActionResult> MarkNotificationRead(int id) { await _notify.MarkReadAsync(id); return Ok(new { ok = true }); }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead([FromQuery] string email) { await _notify.MarkAllReadForEmailAsync(email ?? ""); return Ok(new { ok = true }); }

    [HttpDelete("notifications/{id:int}")]
    public async Task<IActionResult> DeleteNotification(int id) { await _notify.DeleteAsync(id); return Ok(new { ok = true }); }
}
