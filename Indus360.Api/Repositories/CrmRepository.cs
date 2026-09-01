using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Read-only bridge into the internal CRM app's (IndusInternalApp) "Clients" tab — lets
/// /clients' "Create Client Project" wizard pick an already-known CRM client instead of
/// retyping their details. Same physical DB (IndusAppDB) as the CRM app's own API, reached
/// via the already-configured IndusApp connection — see Models/Crm.cs for details.
/// "Provisioned" tracking (which CRM clients already have a database) lives in Indus360's
/// OWN app schema (app.CrmProvisionedClients) — we only ever READ the CRM's DB, never write it.
/// </summary>
public sealed class CrmRepository
{
    private readonly Db _db;
    private readonly IConfiguration _cfg;
    public CrmRepository(Db db, IConfiguration cfg) { _db = db; _cfg = cfg; }

    // Base URL of the internal CRM app (IndusInternalApp) API — used only to build the
    // proposal-document open link (that app serves the S3 PDF via a presigned redirect).
    // Mode-aware (Local vs Server) like the connection strings, so the link is right in both
    // environments without editing config on deploy; falls back to a flat key, then localhost.
    private string CrmApiBaseUrl =>
        (_cfg[$"CrmApiBaseUrl:{_db.Mode}"] ?? _cfg["CrmApiBaseUrl"] ?? "http://localhost:5153").TrimEnd('/');

    /// <summary>Active CRM clients (mirrors IndusInternalApp CustomersController.GetAllCustomers,
    /// minus the multi-contact join — one row per company, which is all the wizard needs), with
    /// DbStatus="Created" merged in for any already provisioned through this wizard, and the
    /// latest proposal document (matched CompanyName ↔ the proposal's lead name via OriginalLeadID).</summary>
    public async Task<IEnumerable<CrmClientRow>> GetClientsAsync()
    {
        const string sql = @"
            SELECT c.CustomerID, c.CompanyName, c.ContactPersonName, c.Email, c.PhoneNumber, c.Address,
                   c.City, c.State, c.Country, c.Status, c.Segment, c.CompanySize, c.LeadType,
                   c.IndasProduct, emp.FullName AS AssignedToName,
                   c.Website, c.GST, c.CompanyPAN,
                   prop.ProposalID AS ProposalId, prop.DocumentName AS ProposalDocumentName
            FROM dbo.Customers c
            LEFT JOIN dbo.Employees emp ON c.AssignedToEmployeeID = emp.EmployeeID
            OUTER APPLY (
                -- The latest CRM proposal for this client that actually has a document.
                -- 'Lead Name' in the CRM grid = CRM_Leads.CompanyName (via ProposalSent.OriginalLeadID);
                -- match it to this client's CompanyName, as the user asked (name-based match).
                SELECT TOP 1 ps.ProposalID, ps.DocumentName
                FROM dbo.CRM_ProposalSent ps
                JOIN dbo.CRM_Leads l ON l.LeadID = ps.OriginalLeadID
                WHERE ISNULL(ps.IsDeleted,0) = 0
                  AND NULLIF(LTRIM(RTRIM(ps.DocumentPath)),'') IS NOT NULL
                  AND l.CompanyName = c.CompanyName
                ORDER BY ps.ProposalID DESC
            ) prop
            WHERE c.IsActive = 1
            ORDER BY c.CompanyName";
        await using var appDb = await _db.OpenAppDbAsync();
        var rows = (await appDb.QueryAsync<CrmClientRow>(sql)).ToList();

        // Build the open-in-new-tab URL for any client that has a proposal document.
        var baseUrl = CrmApiBaseUrl;
        foreach (var r in rows)
            if (r.ProposalId is int pid && pid > 0)
                r.ProposalDocumentUrl = $"{baseUrl}/api/crm/proposal/{pid}/document?inline=true";

        await using var db = await _db.OpenAsync();
        var provisioned = (await db.QueryAsync<int>("SELECT CrmCustomerID FROM app.CrmProvisionedClients")).ToHashSet();
        foreach (var r in rows)
            if (provisioned.Contains(r.CustomerID)) r.DbStatus = "Created";

        return rows;
    }

    /// <summary>Record that a CRM client's database was just created via the wizard (upsert —
    /// re-running the wizard for the same CRM client updates the stamp, doesn't duplicate).</summary>
    public async Task MarkProvisionedAsync(MarkCrmProvisionedRequest r, int? actingUserId)
    {
        await using var db = await _db.OpenAsync();
        await db.ExecuteAsync(@"
            IF EXISTS (SELECT 1 FROM app.CrmProvisionedClients WHERE CrmCustomerID=@CrmCustomerId)
                UPDATE app.CrmProvisionedClients SET ClientName=@ClientName, DatabaseName=@DatabaseName, CreatedAt=SYSDATETIME(), CreatedBy=@actingUserId
                WHERE CrmCustomerID=@CrmCustomerId;
            ELSE
                INSERT INTO app.CrmProvisionedClients (CrmCustomerID, ClientName, DatabaseName, CreatedAt, CreatedBy)
                VALUES (@CrmCustomerId, @ClientName, @DatabaseName, SYSDATETIME(), @actingUserId);",
            new { r.CrmCustomerId, r.ClientName, r.DatabaseName, actingUserId });
    }
}
