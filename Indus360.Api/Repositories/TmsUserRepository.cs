using Dapper;
using Indus360.Api.Data;

namespace Indus360.Api.Repositories;

/// <summary>Result of resolving the logged-in Indus360 user against the TMS world.</summary>
public sealed class PmContext
{
    /// <summary>The matching IndusTaskManagement UserID (0 if the email isn't a TMS user).</summary>
    public int TmsUserId { get; set; }
    public string TmsRole { get; set; } = "";
    public string FullName { get; set; } = "";
    /// <summary>Point-Management submodule routes this user may view (from UserModuleAuthentication).</summary>
    public List<string> Modules { get; set; } = new();
}

/// <summary>
/// Bridges the Indus360 identity (login/permissions in Indus360App) to the TMS
/// identity (Points/Tickets keyed by IndusTaskManagement UserID). The two are
/// linked by email. Used by every Point Management page for authorization + the
/// "my work" scoping (developer/tester/support see their own rows).
/// </summary>
public sealed class TmsUserRepository
{
    private readonly Db _db;
    public TmsUserRepository(Db db) => _db = db;

    public async Task<PmContext> GetContextAsync(long indusUserId, string email)
    {
        var ctx = new PmContext();

        // 1) Allowed Point-Management modules — from Indus360App (Default DB).
        await using (var app = await _db.OpenAsync())
        {
            ctx.Modules = (await app.QueryAsync<string>(@"
                SELECT m.ModuleName
                FROM app.UserModuleAuthentication a
                JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID
                WHERE a.UserID = @indusUserId
                  AND a.CanView = 1
                  AND ISNULL(a.IsDeletedTransaction,0) = 0
                  AND m.ModuleName LIKE '/point-management%'",
                new { indusUserId })).ToList();
        }

        // 2) Matching TMS user identity — from IndusTaskManagement.
        if (!string.IsNullOrWhiteSpace(email))
        {
            await using var tms = await _db.OpenTmsAsync();
            var row = await tms.QuerySingleOrDefaultAsync(@"
                SELECT TOP 1 UserID, ISNULL(Role,'') AS Role, ISNULL(FullName,'') AS FullName
                FROM dbo.Users WHERE Email = @email",
                new { email });
            if (row is not null)
            {
                ctx.TmsUserId = (int)row.UserID;
                ctx.TmsRole = (string)row.Role;
                ctx.FullName = (string)row.FullName;
            }
        }

        return ctx;
    }

    /// <summary>True if the user may view the given Point-Management route (server-side guard).</summary>
    public async Task<bool> CanViewAsync(long indusUserId, string moduleRoute)
    {
        await using var app = await _db.OpenAsync();
        var n = await app.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM app.UserModuleAuthentication a
            JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID
            WHERE a.UserID = @indusUserId AND a.CanView = 1
              AND ISNULL(a.IsDeletedTransaction,0) = 0
              AND m.ModuleName = @moduleRoute",
            new { indusUserId, moduleRoute });
        return n > 0;
    }
}
