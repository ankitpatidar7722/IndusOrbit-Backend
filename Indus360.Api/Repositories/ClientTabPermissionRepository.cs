using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Per-user permission for the 7 client-detail tabs (registered as non-sidebar modules
/// under ModuleHeadName='Client Detail Tabs' in app.ModuleMaster; managed via the normal
/// User Management → Module Authentication matrix). Effective permission rules:
///   • Admin role            → full (view + edit) on every tab.
///   • No tab rows at all     → DEFAULT: view every tab, edit none.
///   • Any tab row configured → EXPLICIT: only View-granted tabs are visible; Edit follows CanEdit.
/// </summary>
public sealed class ClientTabPermissionRepository
{
    private readonly Db _db;
    public ClientTabPermissionRepository(Db db) => _db = db;

    private sealed class Row
    {
        public string ModuleName { get; set; } = "";
        public bool CanView { get; set; }
        public bool CanEdit { get; set; }
        public int HasRow { get; set; }
    }

    public async Task<IEnumerable<ClientTabPermissionDto>> GetForUserAsync(long userId)
    {
        await using var c = await _db.OpenAsync();

        var role = (await c.ExecuteScalarAsync<string?>("SELECT Role FROM app.Users WHERE UserId=@userId", new { userId }))?.Trim() ?? "";
        var isAdmin = role.Equals("admin", StringComparison.OrdinalIgnoreCase)
                   || role.Equals("administrator", StringComparison.OrdinalIgnoreCase);

        var rows = (await c.QueryAsync<Row>(@"
            SELECT m.ModuleName,
                   CAST(ISNULL(a.CanView,0) AS bit) AS CanView,
                   CAST(ISNULL(a.CanEdit,0) AS bit) AS CanEdit,
                   CASE WHEN a.ModuleID IS NULL THEN 0 ELSE 1 END AS HasRow
            FROM app.ModuleMaster m
            LEFT JOIN app.UserModuleAuthentication a
              ON a.ModuleID = m.ModuleID AND a.UserID = @userId AND ISNULL(a.IsDeletedTransaction,0)=0
            WHERE m.ModuleHeadName = 'Client Detail Tabs' AND ISNULL(m.IsDeletedTransaction,0)=0
            ORDER BY m.ModuleDisplayOrder", new { userId })).ToList();

        var anyConfigured = rows.Any(r => r.HasRow == 1);

        return rows.Select(r =>
        {
            var key = r.ModuleName.StartsWith("clienttab-", StringComparison.OrdinalIgnoreCase)
                ? r.ModuleName.Substring("clienttab-".Length) : r.ModuleName;
            bool canView, canEdit;
            if (isAdmin) { canView = true; canEdit = true; }
            else if (!anyConfigured) { canView = true; canEdit = false; }   // default
            else { canEdit = r.CanEdit; canView = r.CanView || r.CanEdit; } // explicit; edit implies view
            return new ClientTabPermissionDto { Key = key, CanView = canView, CanEdit = canEdit };
        }).ToList();
    }
}
