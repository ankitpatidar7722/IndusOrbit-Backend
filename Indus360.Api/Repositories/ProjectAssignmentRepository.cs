using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Admin → Project Assignment. Projects (client companies) are read LIVE from the prod
/// Indus control DB (dbo.Indus_Company_Authentication_For_Web_Modules, via OpenControlAsync).
/// The per-user assignment mapping is stored LOCALLY in app.UserProjectAssignment
/// (IndusTaskManagement). One row per (UserID, ProjectCode), toggled via IsDeletedTransaction.
/// </summary>
public sealed class ProjectAssignmentRepository
{
    private readonly Db _db;
    public ProjectAssignmentRepository(Db db) => _db = db;

    private const string ProdTable = "dbo.Indus_Company_Authentication_For_Web_Modules";

    /// <summary>All assignable projects (client companies) from prod, newest names first.</summary>
    public async Task<IEnumerable<ProjectDto>> GetProjectsAsync()
    {
        using var c = await _db.OpenControlAsync();
        // Prod can hold the same CompanyUniqueCode more than once — return one row per code
        // (assignment is keyed by code) to avoid duplicate React keys on the client.
        return await c.QueryAsync<ProjectDto>($@"
            SELECT Code, Name, Application, Status FROM (
                SELECT CompanyUniqueCode AS Code, CompanyName AS Name,
                       ApplicationName AS Application, SubscriptionStatus AS Status,
                       ROW_NUMBER() OVER (PARTITION BY CompanyUniqueCode
                                          ORDER BY CASE WHEN LTRIM(RTRIM(ISNULL(CompanyName,''))) = '' THEN 1 ELSE 0 END, CompanyName) AS rn
                FROM {ProdTable}
                WHERE ISNULL(IsActive,1) = 1 AND ISNULL(CompanyUniqueCode,'') <> ''
            ) x
            WHERE x.rn = 1
            ORDER BY x.Name");
    }

    /// <summary>The project codes currently assigned to a user (active rows only).</summary>
    public async Task<IEnumerable<string>> GetAssignedCodesAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return await c.QueryAsync<string>(
            "SELECT ProjectCode FROM app.UserProjectAssignment WHERE UserID=@userId AND ISNULL(IsDeletedTransaction,0)=0",
            new { userId });
    }

    /// <summary>
    /// Replace a user's assignments with <paramref name="codes"/>. Deactivates unselected rows,
    /// re-activates or inserts selected ones (one row per user+code). Name/App snapshots are
    /// filled best-effort from the prod list so assigned projects still render if prod is down.
    /// </summary>
    public async Task SaveAsync(long userId, List<string> codes, long? assignedBy)
    {
        var selected = codes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

        // Best-effort name/app snapshot for the selected codes.
        Dictionary<string, ProjectDto> lookup = new();
        try { lookup = (await GetProjectsAsync()).ToDictionary(p => p.Code, p => p, StringComparer.OrdinalIgnoreCase); }
        catch { /* prod unavailable — save codes without snapshot */ }

        await using var c = await _db.OpenAsync();

        // 1) Deactivate currently-active rows the user should no longer have.
        if (selected.Count == 0)
            await c.ExecuteAsync("UPDATE app.UserProjectAssignment SET IsDeletedTransaction=1 WHERE UserID=@userId AND ISNULL(IsDeletedTransaction,0)=0", new { userId });
        else
            await c.ExecuteAsync("UPDATE app.UserProjectAssignment SET IsDeletedTransaction=1 WHERE UserID=@userId AND ISNULL(IsDeletedTransaction,0)=0 AND ProjectCode NOT IN @selected", new { userId, selected });

        // 2) Re-activate or insert each selected code (keeps one row per user+code).
        foreach (var code in selected)
        {
            lookup.TryGetValue(code, out var p);
            var updated = await c.ExecuteAsync(@"
                UPDATE app.UserProjectAssignment
                SET IsDeletedTransaction=0, ProjectName=@Name, ApplicationName=@App, AssignedAt=SYSUTCDATETIME(), AssignedBy=@assignedBy
                WHERE UserID=@userId AND ProjectCode=@code",
                new { userId, code, Name = p?.Name, App = p?.Application, assignedBy });
            if (updated == 0)
                await c.ExecuteAsync(@"
                    INSERT INTO app.UserProjectAssignment (UserID, ProjectCode, ProjectName, ApplicationName, AssignedBy, AssignedAt, IsDeletedTransaction)
                    VALUES (@userId, @code, @Name, @App, @assignedBy, SYSUTCDATETIME(), 0)",
                    new { userId, code, Name = p?.Name, App = p?.Application, assignedBy });
        }
    }
}
