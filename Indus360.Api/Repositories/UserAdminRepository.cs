using System.Data;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// User Management (Admin → User Management): CRUD over Indus360App.app.Users
/// (mobile enriched from IndusAppDB.dbo.Employees) + per-user Module Authentication
/// written to app.UserModuleAuthentication (same rows the sidebar reads).
/// </summary>
public sealed class UserAdminRepository
{
    private readonly Db _db;
    public UserAdminRepository(Db db) => _db = db;

    // Mobile + EmployeeCode now live natively on app.Users (Employee HR data merged in).
    private const string MobileSub = "u.Mobile";
    private const string CodeSub = "u.EmployeeCode";

    public async Task<List<UserListRow>> ListAsync()
    {
        using var c = await _db.OpenAsync();
        return (await c.QueryAsync<UserListRow>($@"
            SELECT u.UserId, u.FullName, u.Email, u.Role, u.ReportingManagerId, u.IsActive, u.CompanyId,
                   mgr.FullName AS ReportingManagerName,
                   {MobileSub} AS Mobile,
                   {CodeSub}   AS EmployeeCode
            FROM app.Users u
            LEFT JOIN app.Users mgr ON mgr.UserId = u.ReportingManagerId
            WHERE ISNULL(u.IsDeletedTransaction,0) = 0
            ORDER BY u.FullName")).ToList();
    }

    public async Task<UserDetailDto?> GetAsync(long id)
    {
        using var c = await _db.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<UserDetailDto>($@"
            SELECT u.UserId, u.FullName, u.Email, u.Role, u.ReportingManagerId, u.IsActive, u.CompanyId,
                   u.ProductionUnitId, u.FYear, {MobileSub} AS Mobile, {CodeSub} AS EmployeeCode,
                   u.EmailProvider, u.SmtpUsername, u.SmtpServer, u.SmtpPort, u.SmtpAuthenticate, u.SmtpUseSSL, u.EmailSignature, u.PhotoPath,
                   CAST(CASE WHEN NULLIF(u.SmtpPassword,'') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasSmtpPassword
            FROM app.Users u WHERE u.UserId = @id AND ISNULL(u.IsDeletedTransaction,0) = 0", new { id });
    }

    /// <summary>The stored profile-photo filename for a user (or null).</summary>
    public async Task<string?> GetPhotoAsync(long id)
    {
        using var c = await _db.OpenAsync();
        return await c.ExecuteScalarAsync<string?>("SELECT PhotoPath FROM app.Users WHERE UserId=@id", new { id });
    }

    /// <summary>Set the profile-photo filename; returns the previous one (for cleanup).</summary>
    public async Task<string?> SetPhotoAsync(long id, string? filename)
    {
        using var c = await _db.OpenAsync();
        var old = await c.ExecuteScalarAsync<string?>("SELECT PhotoPath FROM app.Users WHERE UserId=@id", new { id });
        await c.ExecuteAsync("UPDATE app.Users SET PhotoPath=@filename WHERE UserId=@id", new { id, filename });
        return old;
    }

    public async Task<UserLookups> GetLookupsAsync()
    {
        using var c = await _db.OpenAsync();
        using var multi = await c.QueryMultipleAsync(@"
            SELECT DISTINCT Role FROM app.Users WHERE NULLIF(LTRIM(RTRIM(Role)),'') IS NOT NULL AND ISNULL(IsDeletedTransaction,0) = 0 ORDER BY Role;
            SELECT UserId, FullName FROM app.Users WHERE IsActive = 1 AND ISNULL(IsDeletedTransaction,0) = 0 ORDER BY FullName;");
        var roles = (await multi.ReadAsync<string>()).ToList();
        var managers = (await multi.ReadAsync<ManagerOption>()).ToList();
        return new UserLookups { Roles = roles, Managers = managers };
    }

    public async Task<long> CreateAsync(UserSaveRequest r, int? actingUserId)
    {
        using var c = await _db.OpenAsync();
        var newId = await c.ExecuteScalarAsync<long>(@"
            DECLARE @cu nvarchar(200), @cp nvarchar(200), @pu bigint, @fy nvarchar(20);
            SELECT TOP 1 @cu=CompanyUsername, @cp=CompanyPassword, @pu=ProductionUnitId, @fy=FYear
              FROM app.Users WHERE CompanyId=@CompanyId AND IsActive=1 AND ISNULL(IsDeletedTransaction,0)=0 AND NULLIF(CompanyUsername,'') IS NOT NULL ORDER BY UserId;
            INSERT INTO app.Users
              (FullName, Email, PasswordHash, Role, Mobile, ReportingManagerId, IsActive, CreatedAt, CompanyId, ProductionUnitId, FYear, CompanyUsername, CompanyPassword,
               EmailProvider, SmtpUsername, SmtpPassword, SmtpServer, SmtpPort, SmtpAuthenticate, SmtpUseSSL, EmailSignature, CreatedBy, CreatedDate)
            VALUES
              (@FullName, @Email, @Password, @Role, @Mobile, @ReportingManagerId, @IsActive, SYSDATETIME(), @CompanyId, ISNULL(@pu,0), ISNULL(@fy,'2026-2027'), ISNULL(@cu,''), ISNULL(@cp,''),
               @EmailProvider, @SmtpUsername, @SmtpPassword, @SmtpServer, @SmtpPort, @SmtpAuthenticate, @SmtpUseSSL, @EmailSignature, @CreatedBy, SYSDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS bigint);",
            new { r.FullName, r.Email, Password = r.Password ?? "", r.Role, r.Mobile, r.ReportingManagerId, r.IsActive, r.CompanyId,
                  r.EmailProvider, r.SmtpUsername, r.SmtpPassword, r.SmtpServer, r.SmtpPort, r.SmtpAuthenticate, r.SmtpUseSSL, r.EmailSignature, CreatedBy = actingUserId });
        await SyncTmsUserAsync(c, newId);   // mirror into the TMS dbo.Users (Point Management identity)
        return newId;
    }

    /// <summary>
    /// Mirror an app.Users row into the TMS `dbo.Users` table (same DB) by Email, so the
    /// Point Management module — whose Points/Tickets are keyed by dbo.Users.UserID — always
    /// has a matching user with the same data. Upsert: update by email, else insert.
    /// </summary>
    private static async Task SyncTmsUserAsync(SqlConnection c, long appUserId)
    {
        await c.ExecuteAsync(@"
            DECLARE @fn nvarchar(200), @em nvarchar(200), @pw nvarchar(256), @rl nvarchar(50), @act bit, @mob nvarchar(50), @ec nvarchar(50);
            SELECT @fn=FullName, @em=Email, @pw=PasswordHash, @rl=Role, @act=IsActive, @mob=Mobile, @ec=EmployeeCode
            FROM app.Users WHERE UserId=@id;
            IF NULLIF(LTRIM(RTRIM(@em)),'') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.Users WHERE LTRIM(RTRIM(Email))=LTRIM(RTRIM(@em)))
                    UPDATE dbo.Users SET
                        FullName=@fn, Role=ISNULL(NULLIF(@rl,''),Role), IsActive=@act,
                        WhatsAppNumber=@mob, EmployeeCode=@ec,
                        PasswordHash=CASE WHEN NULLIF(@pw,'') IS NOT NULL THEN @pw ELSE PasswordHash END
                    WHERE LTRIM(RTRIM(Email))=LTRIM(RTRIM(@em));
                ELSE
                    INSERT INTO dbo.Users (FullName, Email, PasswordHash, Role, IsActive, DateCreated, EmailVerified, WhatsAppNumber, EmployeeCode)
                    VALUES (@fn, @em, ISNULL(@pw,''), ISNULL(NULLIF(@rl,''),'User'), @act, GETDATE(), 1, @mob, @ec);
            END",
            new { id = appUserId });
    }

    public async Task UpdateAsync(UserSaveRequest r, int? actingUserId)
    {
        var setPwd = !string.IsNullOrWhiteSpace(r.Password);
        var setSmtpPwd = !string.IsNullOrWhiteSpace(r.SmtpPassword);
        using var c = await _db.OpenAsync();
        await c.ExecuteAsync($@"
            UPDATE app.Users SET
                FullName=@FullName, Email=@Email, Role=ISNULL(NULLIF(@Role,''), Role), Mobile=@Mobile,
                ReportingManagerId=@ReportingManagerId, IsActive=@IsActive,
                EmailProvider=@EmailProvider, SmtpUsername=@SmtpUsername, SmtpServer=@SmtpServer,
                SmtpPort=@SmtpPort, SmtpAuthenticate=@SmtpAuthenticate, SmtpUseSSL=@SmtpUseSSL, EmailSignature=@EmailSignature,
                ModifiedBy=@ModifiedBy, ModifiedDate=SYSDATETIME()
                {(setPwd ? ", PasswordHash=@Password" : "")}
                {(setSmtpPwd ? ", SmtpPassword=@SmtpPassword" : "")}
            WHERE UserId=@UserId",
            new { r.UserId, r.FullName, r.Email, r.Role, r.Mobile, r.ReportingManagerId, r.IsActive, r.Password,
                  r.EmailProvider, r.SmtpUsername, r.SmtpPassword, r.SmtpServer, r.SmtpPort, r.SmtpAuthenticate, r.SmtpUseSSL, r.EmailSignature, ModifiedBy = actingUserId });
        await SyncTmsUserAsync(c, r.UserId);   // keep the TMS dbo.Users in sync
    }

    // ---------------- Self-service (logged-in user editing their own account) ----------------

    /// <summary>Update only the caller's own name + email (never role / active / company).</summary>
    public async Task<int> UpdateSelfProfileAsync(long id, string fullName, string email)
    {
        using var c = await _db.OpenAsync();
        return await c.ExecuteAsync(
            "UPDATE app.Users SET FullName=@fullName, Email=@email WHERE UserId=@id",
            new { id, fullName, email });
    }

    /// <summary>Change the caller's own password after verifying the current one.
    /// Returns "Success" | "Current password is incorrect" | "User not found".</summary>
    public async Task<string> ChangeSelfPasswordAsync(long id, string currentPassword, string newPassword)
    {
        using var c = await _db.OpenAsync();
        var stored = await c.QuerySingleOrDefaultAsync<string?>(
            "SELECT PasswordHash FROM app.Users WHERE UserId=@id", new { id });
        if (stored is null) return "User not found";
        if (!string.Equals(stored, currentPassword)) return "Current password is incorrect";
        await c.ExecuteAsync("UPDATE app.Users SET PasswordHash=@newPassword WHERE UserId=@id", new { id, newPassword });
        return "Success";
    }

    /// <summary>Update only the caller's own email/SMTP settings (Settings → Email).</summary>
    public async Task<int> UpdateSelfSmtpAsync(long id, UserSaveRequest r)
    {
        var setSmtpPwd = !string.IsNullOrWhiteSpace(r.SmtpPassword);
        using var c = await _db.OpenAsync();
        return await c.ExecuteAsync($@"
            UPDATE app.Users SET
                EmailProvider=@EmailProvider, SmtpUsername=@SmtpUsername, SmtpServer=@SmtpServer,
                SmtpPort=@SmtpPort, SmtpAuthenticate=@SmtpAuthenticate, SmtpUseSSL=@SmtpUseSSL, EmailSignature=@EmailSignature
                {(setSmtpPwd ? ", SmtpPassword=@SmtpPassword" : "")}
            WHERE UserId=@Id",
            new { Id = id, r.EmailProvider, r.SmtpUsername, r.SmtpPassword, r.SmtpServer, r.SmtpPort, r.SmtpAuthenticate, r.SmtpUseSSL, r.EmailSignature });
    }

    /// <summary>SOFT-delete a user (IsDeletedTransaction=1) and detach anyone reporting to them.
    /// The row + its module authority are kept for recovery; all reads/login filter it out.</summary>
    public async Task<int> DeleteAsync(long id, int? userId)
    {
        using var c = await _db.OpenAsync();
        return await c.ExecuteAsync(@"
            UPDATE app.Users SET ReportingManagerId = NULL WHERE ReportingManagerId = @id;
            UPDATE app.Users SET IsDeletedTransaction = 1, DeletedBy = @userId, DeletedDate = SYSDATETIME() WHERE UserId = @id;", new { id, userId });
    }

    public async Task<List<ModuleAuthRow>> GetModuleAuthAsync(long userId)
    {
        using var c = await _db.OpenAsync();
        return (await c.QueryAsync<ModuleAuthRow>(@"
            SELECT m.ModuleID, m.ModuleName, m.ModuleDisplayName, m.ModuleHeadName, m.ModuleHeadDisplayName,
                   m.SetGroupIndex, m.ModuleDisplayOrder,
                   -- Client-Detail-Tab modules default to view-all when the user has NO clienttab rows yet
                   -- (matches the effective-permission default) so the matrix honestly shows the current state
                   -- and an admin doesn't accidentally deny them by saving an unrelated change.
                   CAST(CASE WHEN m.ModuleHeadName = 'Client Detail Tabs'
                              AND NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication x
                                              JOIN app.ModuleMaster xm ON xm.ModuleID = x.ModuleID
                                              WHERE x.UserID = @userId AND ISNULL(x.IsDeletedTransaction,0)=0
                                                AND xm.ModuleHeadName = 'Client Detail Tabs')
                             THEN 1 ELSE ISNULL(a.CanView,0) END AS bit) AS CanView,
                   CAST(ISNULL(a.CanSave,0)   AS bit) AS CanSave,
                   CAST(ISNULL(a.CanEdit,0)   AS bit) AS CanEdit,
                   CAST(ISNULL(a.CanDelete,0) AS bit) AS CanDelete,
                   CAST(ISNULL(a.CanPrint,0)  AS bit) AS CanPrint,
                   CAST(ISNULL(a.CanExport,0) AS bit) AS CanExport,
                   CAST(ISNULL(a.CanCancel,0) AS bit) AS CanCancel
            FROM app.ModuleMaster m
            LEFT JOIN app.UserModuleAuthentication a
              ON a.ModuleID = m.ModuleID AND a.UserID = @userId AND ISNULL(a.IsDeletedTransaction,0)=0
            WHERE ISNULL(m.IsDeletedTransaction,0)=0
            ORDER BY m.SetGroupIndex, m.ModuleDisplayOrder", new { userId })).ToList();
    }

    /// <summary>Replace the user's module authority (delete + reinsert selected rows).</summary>
    public async Task<int> SaveModuleAuthAsync(SaveModuleAuthRequest req)
    {
        await using var c = await _db.OpenAsync();
        var companyId = await c.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyId FROM app.Users WHERE UserId=@id", new { id = req.UserId }) ?? 1;
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        // Client-Detail-Tab modules must ALWAYS be persisted (even all-zero) so an explicit
        // "unchecked = deny" is recorded. Otherwise a fully-unchecked clienttab row is skipped,
        // leaving the user with NO clienttab rows — indistinguishable from "never configured",
        // which the effective-permission rule treats as view-all, so the tabs can never be hidden.
        var clientTabIds = (await c.QueryAsync<long>(
            "SELECT ModuleID FROM app.ModuleMaster WHERE ModuleHeadName='Client Detail Tabs' AND ISNULL(IsDeletedTransaction,0)=0",
            transaction: tx)).ToHashSet();
        await c.ExecuteAsync("DELETE FROM app.UserModuleAuthentication WHERE UserID=@id", new { id = req.UserId }, tx);
        var saved = 0;
        foreach (var m in req.Modules)
        {
            // keep a row only if at least one permission is granted — EXCEPT client-detail tabs,
            // which are always kept so an explicit deny (all unchecked) is recorded, not lost.
            if (!(m.CanView || m.CanSave || m.CanEdit || m.CanDelete || m.CanPrint || m.CanExport || m.CanCancel)
                && !clientTabIds.Contains(m.ModuleID)) continue;
            await c.ExecuteAsync(@"
                INSERT INTO app.UserModuleAuthentication
                  (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
                VALUES
                  (@UserId, @ModuleID, @CanView, @CanSave, @CanEdit, @CanDelete, @CanPrint, @CanExport, @CanCancel, @CompanyID, 0)",
                new
                {
                    req.UserId, m.ModuleID, m.CanView, m.CanSave, m.CanEdit, m.CanDelete, m.CanPrint, m.CanExport, m.CanCancel,
                    CompanyID = companyId,
                }, tx);
            saved++;
        }
        await tx.CommitAsync();
        return saved;
    }

    /// <summary>Feature-permission keys granted to a user (opt-in — a row's presence = granted).</summary>
    public async Task<List<string>> GetPermissionsAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        return (await c.QueryAsync<string>(
            "SELECT PermissionKey FROM app.UserPermissions WHERE UserId=@userId ORDER BY PermissionKey",
            new { userId })).ToList();
    }

    /// <summary>Replace a user's granted feature permissions (delete-all + reinsert the granted keys).</summary>
    public async Task<int> SavePermissionsAsync(long userId, IEnumerable<string> keys)
    {
        var clean = keys?.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList() ?? new List<string>();
        await using var c = await _db.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("DELETE FROM app.UserPermissions WHERE UserId=@userId", new { userId }, tx);
        foreach (var k in clean)
            await c.ExecuteAsync("INSERT INTO app.UserPermissions (UserId, PermissionKey) VALUES (@userId, @k)", new { userId, k }, tx);
        await tx.CommitAsync();
        return clean.Count;
    }
}
