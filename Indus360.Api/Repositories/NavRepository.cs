using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>Login + per-user dynamic navigation (module authority).</summary>
public sealed class NavRepository
{
    private readonly Db _db;
    public NavRepository(Db db) => _db = db;

    /// <summary>Validate credentials and return the session identity (null if invalid).</summary>
    public async Task<SessionUser?> AuthenticateAsync(string email, string password)
    {
        using var c = await _db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<SessionUser>(@"
            SELECT  UserId            AS UserID,
                    FullName,
                    Email,
                    Role,
                    CompanyId          AS CompanyID,
                    ProductionUnitId   AS ProductionUnitID,
                    FYear,
                    CompanyUsername,
                    CompanyPassword
            FROM app.Users
            WHERE Email = @email AND PasswordHash = @password AND IsActive = 1 AND ISNULL(IsDeletedTransaction,0) = 0",
            new { email, password });
    }

    /// <summary>Modules the user is allowed to view, ordered for the sidebar.</summary>
    public async Task<IEnumerable<NavModule>> GetModulesAsync(long userId)
    {
        using var c = await _db.OpenAsync();
        return await c.QueryAsync<NavModule>(@"
            SELECT  m.ModuleName, m.ModuleDisplayName, m.ModuleHeadName, m.ModuleHeadDisplayName,
                    m.ModuleDisplayOrder, m.SetGroupIndex, m.ModuleIcon, m.ParentModuleName,
                    a.CanView, a.CanSave, a.CanEdit, a.CanDelete, a.CanPrint, a.CanExport, a.CanCancel
            FROM app.UserModuleAuthentication a
            JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID
            WHERE a.UserID = @userId AND a.CanView = 1
              AND ISNULL(a.IsDeletedTransaction,0) = 0
              AND ISNULL(m.IsDeletedTransaction,0) = 0
              AND ISNULL(m.IsSidebarItem,1) = 1   -- non-sidebar permission-only modules (client tabs) never show in the menu
            ORDER BY m.SetGroupIndex, m.ModuleDisplayOrder",
            new { userId });
    }
}
