namespace Indus360.Api.Models;

/// <summary>Standard audit columns (populated from the acting user's UserID header + DB clock).</summary>
public abstract class AuditFields
{
    public int? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public int? DeletedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
}

public class Client : AuditFields
{
    public string ClientCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public string? Gstin { get; set; }
    public string? Contact { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Segment { get; set; }
    public string? Application { get; set; }
    public string? Product { get; set; }
    public string? Consultant { get; set; }
    public string Status { get; set; } = "Pending Kick-Off";
    public int Progress { get; set; }
    public string? CompanyLogin { get; set; }
    public string? PasswordMasked { get; set; }
    public string? UserLogin { get; set; }
    public string? Url { get; set; }
    public bool KickoffDone { get; set; }
    public string? KickoffDate { get; set; }
    public bool MastersSent { get; set; }
    public bool SignoffDone { get; set; }
    public string? Source { get; set; }
}

public class ClientModule
{
    public int Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public bool IsOn { get; set; }
}

public class Milestone : AuditFields
{
    public int Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string? MilestoneGroup { get; set; }            // Roadmap to Success (Milestone-1…)
    public string Name { get; set; } = "";                 // Phases
    public string? TaskTimeline { get; set; }              // Task Timeline
    public string? PlannedDate { get; set; }               // Estimated Start Date
    public string? ActualDate { get; set; }                // Actual Start Date
    public string? EndDate { get; set; }                   // End date
    public string? ResPerson { get; set; }                 // Res. Person Indus
    public string Status { get; set; } = "Pending";
    public string? StartDateVariance { get; set; }         // Start Date Variance (Days)
    public string? ScheduledStartStatus { get; set; }      // Scheduled Start Status
    public string? RemarkStartDelay { get; set; }          // Remark-1 (Start Delay)
    public string? TimelineVariance { get; set; }          // Timeline Variance (Days)
    public string? TimelineVarianceStatus { get; set; }    // Timeline Variance Status
    public string? RemarkDuration { get; set; }            // Remark-2 (Duration)
    public int SortOrder { get; set; }
    public bool Emailed { get; set; }
    public bool Tasked { get; set; }
}

public class TrainingUpdate : AuditFields
{
    public int Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string? ModuleName { get; set; }                // Main Module
    public string? SubModule { get; set; }
    public string? TimelineDays { get; set; }              // Timeline in Days
    public string? LogDate { get; set; }                   // Schedule Date
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Trainee { get; set; }                   // Trainee Name
    public string? Trainer { get; set; }                   // Trainer from Indas
    public string Status { get; set; } = "Running";
    public string? Details { get; set; }                   // Details of Covered Modules in Training
    public string? Remark { get; set; }
    public string? VideoUrl { get; set; }
    public bool Emailed { get; set; }
    public bool Tasked { get; set; }
}

public class ChangeRequest : AuditFields
{
    public int Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string? ModuleName { get; set; }                // Module Name (keyline ModuleHeadDisplayName)
    public string? SubModule { get; set; }                 // Sub Module Name (keyline ModuleDisplayName)
    public string? Description { get; set; }               // Point Description
    public string? RaisedBy { get; set; }
    public string? RaisedDate { get; set; }
    public string? ReportedBy { get; set; }
    public string QueryType { get; set; } = "Improvement";
    public string Status { get; set; } = "Open";
    public string? CompletionDate { get; set; }
    public string? CompletionDays { get; set; }
    public string? Remark { get; set; }
    public bool InBugTool { get; set; }
    public bool Emailed { get; set; }
    public bool Tasked { get; set; }
    public bool Pointed { get; set; }
    public int? PointID { get; set; }               // Point Management ticket id created from this CR
}

public class SupportLog : AuditFields
{
    public int Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string? LogDate { get; set; }
    public string? ModuleName { get; set; }
    public string? SubModule { get; set; }
    public string? Problem { get; set; }
    public string? Solution { get; set; }
    public string Status { get; set; } = "Complete";
    public bool Emailed { get; set; }
    public bool Tasked { get; set; }
}

public class OnsiteVisit : AuditFields
{
    public int Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string Person { get; set; } = "";               // Person Name
    public string? Age { get; set; }
    public string? MobileNo { get; set; }
    public string? Location { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? Days { get; set; }
    public string? TicketCharge { get; set; }              // Tickets
    public string? HotelCharge { get; set; }
    public string? FoodCharge { get; set; }
    public string? SiteCharge { get; set; }
    public string Status { get; set; } = "Planned";
    public bool Emailed { get; set; }
}

/// <summary>Row for the clients grid.</summary>
public class ClientListItem
{
    public string ClientCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public string? Application { get; set; }
    public string Status { get; set; } = "";
    public int Progress { get; set; }
    public string? Consultant { get; set; }
    public int OpenCrCount { get; set; }
}

/// <summary>Full client 360 with nested collections.</summary>
public class ClientDetail : Client
{
    public List<ClientModule> Modules { get; set; } = new();
    public List<Milestone> Milestones { get; set; } = new();
    public List<TrainingUpdate> Training { get; set; } = new();
    public List<ChangeRequest> ChangeRequests { get; set; } = new();
    public List<SupportLog> Support { get; set; } = new();
    public List<OnsiteVisit> Onsite { get; set; } = new();
}
