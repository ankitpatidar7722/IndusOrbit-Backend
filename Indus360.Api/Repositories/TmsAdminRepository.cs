using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Admin data access for the Point Management module: TMS users (with Indus360App
/// login sync), TMS customers, and native permission grants (UserModuleAuthentication).
/// Ports ManageUsers / ManageCustomers / UserPermissions.
/// </summary>
public sealed class TmsAdminRepository
{
    private readonly Db _db;
    public TmsAdminRepository(Db db) => _db = db;

    // ---------------- Users ----------------
    public async Task<IEnumerable<PmUser>> GetAllUsersAsync()
    {
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PmUser>(
            "SELECT UserId AS UserID, FullName, Email, Role, IsActive, Mobile AS WhatsAppNumber FROM app.Users ORDER BY FullName");
    }

    /// <summary>Create/update a user — unified on app.Users (Point Management + the app login are the same table now).</summary>
    public async Task<int> UpsertUserAsync(TmsUserSave u)
    {
        await using var db = await _db.OpenTmsAsync();
        if (u.UserID > 0)
        {
            await db.ExecuteAsync(@"
                UPDATE app.Users SET FullName=@FullName, Email=@Email, Role=@Role, IsActive=@IsActive, Mobile=@WhatsAppNumber,
                    PasswordHash = CASE WHEN @Password IS NOT NULL AND @Password <> '' THEN @Password ELSE PasswordHash END
                WHERE UserId=@UserID", u);
            return u.UserID;
        }
        return await db.ExecuteScalarAsync<int>(@"
            INSERT INTO app.Users (FullName, Email, PasswordHash, Role, IsActive, Mobile, CompanyId, ProductionUnitId, FYear, CreatedAt)
            OUTPUT INSERTED.UserId
            VALUES (@FullName, @Email, COALESCE(NULLIF(@Password,''),'indus@123'), @Role, @IsActive, @WhatsAppNumber, 1, 1, '2026-2027', SYSDATETIME())", u);
    }

    public async Task SetUserActiveAsync(int userId, bool active)
    {
        // TMS side
        await using (var tms = await _db.OpenTmsAsync())
        {
            var email = await tms.ExecuteScalarAsync<string?>("SELECT Email FROM app.Users WHERE UserID=@userId", new { userId });
            await tms.ExecuteAsync("UPDATE app.Users SET IsActive=@active WHERE UserID=@userId", new { userId, active });
            if (!string.IsNullOrWhiteSpace(email))
            {
                await using var app = await _db.OpenAsync();
                await app.ExecuteAsync("UPDATE app.Users SET IsActive=@active WHERE Email=@email", new { active, email });
            }
        }
    }

    // ---------------- Customers ----------------
    public async Task<IEnumerable<PmCustomer>> GetAllCustomersAsync()
    {
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PmCustomer>(
            "SELECT CustomerID, CustomerName, CompanyName, ContactPerson, ContactEmail, ContactPhone, IsActive, DateCreated FROM dbo.Customers ORDER BY CompanyName");
    }

    public async Task<int> UpsertCustomerAsync(TmsCustomerSave c)
    {
        await using var db = await _db.OpenTmsAsync();
        if (c.CustomerID > 0)
        {
            await db.ExecuteAsync(@"
                UPDATE dbo.Customers SET CustomerName=@CustomerName, CompanyName=@CompanyName,
                    ContactPerson=@ContactPerson, ContactEmail=@ContactEmail, ContactPhone=@ContactPhone, IsActive=@IsActive
                WHERE CustomerID=@CustomerID", c);
            return c.CustomerID;
        }
        return await db.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.Customers (CustomerName, CompanyName, ContactPerson, ContactEmail, ContactPhone, IsActive, DateCreated)
            OUTPUT INSERTED.CustomerID
            VALUES (@CustomerName, @CompanyName, @ContactPerson, @ContactEmail, @ContactPhone, @IsActive, GETDATE())", c);
    }

    public async Task SetCustomerActiveAsync(int customerId, bool active)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Customers SET IsActive=@active WHERE CustomerID=@customerId", new { customerId, active });
    }

    // ---------------- Permissions (UserModuleAuthentication) ----------------
    public async Task<IEnumerable<AppUserInfo>> GetAppUsersAsync()
    {
        await using var app = await _db.OpenAsync();
        return await app.QueryAsync<AppUserInfo>(
            "SELECT UserId, FullName, Email, Role FROM app.Users WHERE IsActive = 1 AND ISNULL(IsDeletedTransaction,0) = 0 ORDER BY FullName");
    }

    public async Task<IEnumerable<PmModuleInfo>> GetPmModulesAsync()
    {
        await using var app = await _db.OpenAsync();
        return await app.QueryAsync<PmModuleInfo>(
            "SELECT ModuleID, ModuleName, ModuleDisplayName FROM app.ModuleMaster WHERE ModuleName LIKE '/point-management/%' ORDER BY ModuleDisplayOrder");
    }

    public async Task<IEnumerable<long>> GetUserPmModuleIdsAsync(long userId)
    {
        await using var app = await _db.OpenAsync();
        return await app.QueryAsync<long>(@"
            SELECT a.ModuleID FROM app.UserModuleAuthentication a
            JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID
            WHERE a.UserID=@userId AND a.CanView=1 AND m.ModuleName LIKE '/point-management/%'", new { userId });
    }

    /// <summary>Replace a user's Point-Management grants; always keeps the parent module if any submodule is granted.</summary>
    public async Task SaveUserPmModulesAsync(long userId, List<long> moduleIds)
    {
        await using var app = await _db.OpenAsync();
        await app.ExecuteAsync(@"
            DELETE a FROM app.UserModuleAuthentication a
            JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID
            WHERE a.UserID=@userId AND m.ModuleName LIKE '/point-management%'", new { userId });

        if (moduleIds.Count == 0) return;

        await app.ExecuteAsync(@"
            INSERT INTO app.UserModuleAuthentication (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
            SELECT @userId, m.ModuleID, 1,1,1,1,1,1,1, 1, 0
            FROM app.ModuleMaster m
            WHERE (m.ModuleID IN @moduleIds OR m.ModuleName = '/point-management')
              AND m.ModuleName LIKE '/point-management%'", new { userId, moduleIds });
    }
}
