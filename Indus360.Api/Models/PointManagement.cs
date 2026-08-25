namespace Indus360.Api.Models;

// ---------------------------------------------------------------------------
// DTOs for the Point Management module (migrated from the IndusTaskManagement
// "TMS" app). Property names mirror the IndusTaskManagement table columns so
// Dapper maps them directly; ASP.NET Core serializes them to camelCase JSON,
// which the frontend lib/tms.ts consumes.
// ---------------------------------------------------------------------------

// ----- Lookups -----
public sealed class PmUser
{
    public int UserID { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public string? WhatsAppNumber { get; set; }
}

public sealed class PmCustomer
{
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? ContactPerson { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; }
    public DateTime? DateCreated { get; set; }
}

public sealed class PmProduct
{
    public int ProductID { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductVersion { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PmCategory
{
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = "";
    public bool IsActive { get; set; }
}

// ----- Dashboard -----
/// <summary>Status counters for the admin dashboard stat cards (mirrors DashboardAdmin.LoadDashboardStats).</summary>
public sealed class AdminDashboardStats
{
    public int Total { get; set; }
    public int Queue { get; set; }
    public int Assigned { get; set; }
    public int InProgress { get; set; }
    public int DevCompleted { get; set; }
    public int PendingSupport { get; set; }
    public int SupportVerified { get; set; }
    public int PendingMerge { get; set; }
    public int PendingQC { get; set; }
    public int InTesting { get; set; }
    public int TestingCompleted { get; set; }
    public int ReOpened { get; set; }
    public int Hold { get; set; }
    public int Reject { get; set; }
    public int Closed { get; set; }
    public int Delayed { get; set; }
    public int Open { get; set; }
}

// ----- Attachments -----
public sealed class AttachmentRow
{
    public int AttachmentID { get; set; }
    public int PointID { get; set; }
    public string FileName { get; set; } = "";
    public string? OriginalFileName { get; set; }
    public DateTime UploadTimestamp { get; set; }
    public int UploadedByID { get; set; }
    public string? UploadedByName { get; set; }
}

public sealed class AttachmentFile
{
    public string FilePath { get; set; } = "";
    public string? OriginalFileName { get; set; }
}

// ----- Notifications -----
public sealed class NotificationRow
{
    public int NotificationID { get; set; }
    public string? MessageText { get; set; }
    public string? NavigateURL { get; set; }
    public bool IsRead { get; set; }
    public DateTime DateCreated { get; set; }
}

// ----- Admin (users / customers / permissions) -----
public sealed class TmsUserSave
{
    public int UserID { get; set; }               // 0 = create
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Password { get; set; }          // optional; only set/reset when provided
    public string Role { get; set; } = "developer";
    public string? WhatsAppNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TmsCustomerSave
{
    public int CustomerID { get; set; }            // 0 = create
    public string CustomerName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? ContactPerson { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>An Indus360App user that Point-Management permissions can be assigned to.</summary>
public sealed class AppUserInfo
{
    public long UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Role { get; set; }
}

/// <summary>A Point-Management submodule that can be granted.</summary>
public sealed class PmModuleInfo
{
    public long ModuleID { get; set; }
    public string ModuleName { get; set; } = "";
    public string ModuleDisplayName { get; set; } = "";
}

public sealed class SavePermissionsRequest
{
    public long UserId { get; set; }
    public List<long> ModuleIds { get; set; } = new();
}

// ----- Reports -----
/// <summary>One row of the Time / Customer-Progress report (mirrors GetTimeReportData).</summary>
public sealed class TimeReportRow
{
    public int PointID { get; set; }
    public string? Customer { get; set; }
    public string? Product { get; set; }
    public string? Module { get; set; }
    public string? ReportedBy { get; set; }
    public string? AssignedTo { get; set; }
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public int? ExpectedMinutes { get; set; }
    public int? TotalTimeSpent { get; set; }
    public int? PauseTimeMinutes { get; set; }
    public int DelayMinutes { get; set; }
    public int? SupportTimeSpent { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? DateClosed { get; set; }
}

// ----- Tickets (assignment containers) -----
public sealed class TicketRow
{
    public int TicketID { get; set; }
    public string TicketNo { get; set; } = "";
    public int AssignedToID { get; set; }
    public string? AssignedToName { get; set; }
    public string? CreatedByName { get; set; }
    public string? Department { get; set; }
    public string Status { get; set; } = "";
    public DateTime? DateCreated { get; set; }
    public int PointCount { get; set; }
    public int OpenCount { get; set; }
}

/// <summary>One row for the Manage Assignments grid — a Point that has been assigned via a Ticket,
/// with who performed the assignment (Ticket.CreatedByID) and when.</summary>
public sealed class AssignmentRow
{
    public int PointID { get; set; }
    public string Title { get; set; } = "";
    public int? TicketID { get; set; }
    public int? AssignedToID { get; set; }
    public string? AssignedToName { get; set; }
    public int? AssignedByID { get; set; }
    public string? AssignedByName { get; set; }
    public DateTime? AssignDate { get; set; }
    public string? ReportedByName { get; set; }
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public string? Description { get; set; }
    public string? CustomerName { get; set; }
    public string? ProductName { get; set; }
    public string? Module { get; set; }
    public string? SubModule { get; set; }
}

/// <summary>Payload to assign queue points to a developer (mirrors CreateTicketAndAssignPoints).</summary>
public sealed class AssignRequest
{
    public int DevId { get; set; }
    public int CreatedById { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public int? ExpectedMinutes { get; set; }
    public List<int> PointIds { get; set; } = new();
}

// ----- Write payloads -----
/// <summary>Payload to create a new Point (mirrors AddPoint.aspx → InsertPoint).</summary>
public sealed class NewPointRequest
{
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Module { get; set; }       // keyline ModuleHeadDisplayName
    public string? SubModule { get; set; }    // keyline ModuleDisplayName
    public string Description { get; set; } = "";
    public int CustomerID { get; set; }
    public string? CustomerName { get; set; }   // /clients company name → resolved to CustomerID (find-or-create)
    public int ProductID { get; set; }
    public int ReportedByID { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Category { get; set; } = "Bug";
    public string? Complexity { get; set; }
}

/// <summary>Action payload for point timer/transition endpoints.</summary>
public sealed class PointActionRequest
{
    public int UserId { get; set; }
    public string? Remark { get; set; }
    public string? Status { get; set; }   // for Hold/Reject
}

/// <summary>Full point detail for the action drawer (developer/tester/support).</summary>
public sealed class PointDetail
{
    public int PointID { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Complexity { get; set; }
    public string? Module { get; set; }
    public string? SubModule { get; set; }
    public string? CustomerName { get; set; }
    public string? ProductName { get; set; }
    public string? TicketNo { get; set; }
    public string? AssignedToName { get; set; }
    public string? ReportedByName { get; set; }
    public int? ExpectedMinutes { get; set; }
    public int? TotalTimeSpent { get; set; }
    public int? PauseTimeMinutes { get; set; }
    public bool IsDeveloperPaused { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DateCompleted { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? DeveloperRemark { get; set; }
    public string? TesterRemark { get; set; }
    public string? SupportRemark { get; set; }
    public string? AdminRemark { get; set; }
}

/// <summary>One QC cycle row (PointHistory) for the point timeline.</summary>
public sealed class PointHistoryRow
{
    public int HistoryID { get; set; }
    public string? DeveloperName { get; set; }
    public string? TesterName { get; set; }
    public string? SupportName { get; set; }
    public string CycleStatus { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? CompleteDate { get; set; }
    public int? TimeSpentMinutes { get; set; }
    public int? ExtraTimeMinutes { get; set; }
    public string? DeveloperRemark { get; set; }
    public string? TesterRemark { get; set; }
    public string? SupportRemark { get; set; }
}

// ----- Point (task) grid row -----
/// <summary>A row for the various point grids (admin/dev/tester/manage/reports).</summary>
public sealed class PointGridRow
{
    public int PointID { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? Module { get; set; }
    public string? SubModule { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Complexity { get; set; }
    public int CustomerID { get; set; }
    public string? CustomerName { get; set; }
    public int ProductID { get; set; }
    public string? ProductName { get; set; }
    public int? TicketID { get; set; }
    public string? TicketNo { get; set; }
    public string? AssignedByName { get; set; }     // TMS "Assigned By" = the Ticket's CreatedByID
    public int ReportedByID { get; set; }
    public string? ReportedByName { get; set; }
    public int? AssignedToID { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DateCompleted { get; set; }
    public DateTime? DateClosed { get; set; }
    public DateTime? ToDoDate { get; set; }
    public int? ExpectedMinutes { get; set; }
    public int? TotalTimeSpent { get; set; }
    public int? PauseTimeMinutes { get; set; }
    public bool IsVerified { get; set; }
    public int VerificationStatus { get; set; }
    public bool IsDeveloperPaused { get; set; }
    public int? SortOrder { get; set; }
    public string? AudioFilePath { get; set; }
    public int? TrackerChangeRequestId { get; set; }   // set once this point has been sent to a client Tracker
}
