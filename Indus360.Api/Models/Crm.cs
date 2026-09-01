namespace Indus360.Api.Models;

/// <summary>
/// A client row from the internal CRM app's "Clients" tab (IndusInternalApp, `/crm/leads?tab=clients`).
/// Read directly from `IndusAppDB.dbo.Customers` — the SAME physical database Indus360 already
/// connects to via `Db.OpenAppDbAsync()` (IndusApp connection, used for Employees/HR) and the exact
/// database IndusInternalApp's own `CustomersController` (`GET /api/customers`) queries — so no HTTP
/// call to the other app is needed, just a direct SQL read.
/// </summary>
public sealed class CrmClientRow
{
    public int CustomerID { get; set; }
    public string CompanyName { get; set; } = "";
    public string? ContactPersonName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Status { get; set; }
    public string? Segment { get; set; }
    public string? CompanySize { get; set; }
    public string? LeadType { get; set; }
    public string? IndasProduct { get; set; }
    public string? AssignedToName { get; set; }
    public string? Website { get; set; }
    public string? GST { get; set; }
    public string? CompanyPAN { get; set; }
    /// <summary>"Created" once this client has had a database provisioned via the wizard's CRM
    /// picker (looked up from Indus360's OWN app.CrmProvisionedClients — never written to the CRM's DB).</summary>
    public string? DbStatus { get; set; }
    /// <summary>The latest CRM proposal document for this client, matched by CompanyName ↔ the
    /// proposal's lead name. Files live in S3; the URL points at IndusInternalApp's own
    /// presigned-redirect endpoint (opening it 302-redirects to the actual PDF).</summary>
    public int? ProposalId { get; set; }
    public string? ProposalDocumentName { get; set; }
    public string? ProposalDocumentUrl { get; set; }
}

/// <summary>Body to record that a CRM client's database has been created via the wizard.</summary>
public sealed class MarkCrmProvisionedRequest
{
    public int CrmCustomerId { get; set; }
    public string? ClientName { get; set; }
    public string? DatabaseName { get; set; }
}
