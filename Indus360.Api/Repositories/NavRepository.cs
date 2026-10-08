using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Indus360.Api.Services;

namespace Indus360.Api.Repositories;

/// <summary>Login + per-user dynamic navigation (module authority).</summary>
public sealed class NavRepository
{
    private readonly Db _db;
    public NavRepository(Db db) => _db = db;

    /// <summary>AuthenticateAsync projection — the session identity plus the stored password hash
    /// (used only to verify, then discarded — never returned to the caller / session).</summary>
    private sealed class AuthRow
    {
        public long UserID { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "User";
        public int CompanyID { get; set; }
        public long ProductionUnitID { get; set; }
        public string FYear { get; set; } = "";
        public string CompanyUsername { get; set; } = "";
        public string CompanyPassword { get; set; } = "";
        public string? PasswordHash { get; set; }
    }

    /// <summary>Validate credentials and return the session identity (null if invalid).</summary>
    public async Task<SessionUser?> AuthenticateAsync(string email, string password)
    {
        using var c = await _db.OpenAsync();
        // Fetch by email only, then verify the password in C#. We can no longer match the hash in SQL:
        // stored values are now BCrypt hashes (salted — the same password hashes differently every time).
        var row = await c.QuerySingleOrDefaultAsync<AuthRow>(@"
            SELECT  UserId            AS UserID,
                    FullName,
                    Email,
                    Role,
                    CompanyId          AS CompanyID,
                    ProductionUnitId   AS ProductionUnitID,
                    FYear,
                    CompanyUsername,
                    CompanyPassword,
                    PasswordHash
            FROM app.Users
            WHERE Email = @email AND IsActive = 1 AND ISNULL(IsDeletedTransaction,0) = 0",
            new { email });
        if (row is null || !PasswordHasher.Verify(password, row.PasswordHash)) return null;

        // Lazy migration: a legacy plain-text password is upgraded to a BCrypt hash on first successful
        // login — so existing accounts keep working and silently become hashed over time (no bulk reset).
        if (!PasswordHasher.IsHashed(row.PasswordHash))
            await c.ExecuteAsync("UPDATE app.Users SET PasswordHash = @h WHERE UserId = @id",
                new { h = PasswordHasher.Hash(password), id = row.UserID });

        // Return a clean SessionUser (the hash never leaves this method / reaches the next-auth session).
        return new SessionUser
        {
            UserID = row.UserID, FullName = row.FullName, Email = row.Email, Role = row.Role,
            CompanyID = row.CompanyID, ProductionUnitID = row.ProductionUnitID, FYear = row.FYear,
            CompanyUsername = row.CompanyUsername, CompanyPassword = row.CompanyPassword,
        };
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
