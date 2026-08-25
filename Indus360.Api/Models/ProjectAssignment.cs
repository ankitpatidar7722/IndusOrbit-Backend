namespace Indus360.Api.Models;

// ── Admin → Project Assignment ──
// A "Project" is a client company from the PROD Indus control DB
// (dbo.Indus_Company_Authentication_For_Web_Modules). Admins assign specific
// projects to a user so that user only sees those projects (not all clients).

/// <summary>One assignable project (client company) sourced from the prod Indus DB.</summary>
public sealed class ProjectDto
{
    public string Code { get; set; } = "";       // CompanyUniqueCode e.g. "IA00206"
    public string Name { get; set; } = "";        // CompanyName (Client Name)
    public string? Application { get; set; }       // ApplicationName (Estimoprime / PrintudeERP / …)
    public string? Status { get; set; }            // SubscriptionStatus
}

/// <summary>Save payload: the full set of project codes a user should be assigned.</summary>
public sealed class ProjectAssignmentSaveRequest
{
    public List<string> ProjectCodes { get; set; } = new();
    public long? AssignedBy { get; set; }
}
