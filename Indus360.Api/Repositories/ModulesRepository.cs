using System.Data;
using Dapper;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Module-authority logic cloned from BulkImport CompanySubscriptionService:
/// per-client module settings (get/save), copy modules, module groups.
/// Reads use the app master catalog in the Indus DB + the client's own ModuleMaster.
/// Destructive writes (save/copy/apply) operate on the client DB in transactions.
/// NOTE: the remote hard-coded template DB (IndusEnterpriseNewInstallation) is replaced
/// natively by the app's master catalog table in the Indus control DB.
/// </summary>
public sealed class ModulesRepository
{
    private readonly string _controlCs;
    private readonly string _localCs;
    private readonly string _keylineCs;
    private readonly string? _printudeCatalogCs;
    public ModulesRepository(IConfiguration cfg)
    {
        // IndusControl is a flat, mode-independent connection string.
        _controlCs = cfg.GetConnectionString("IndusControl")
           ?? throw new InvalidOperationException("ConnectionStrings:IndusControl not configured.");
        // IndusKeyline is the shared enterprise module catalog (also flat / mode-independent).
        _keylineCs = cfg.GetConnectionString("IndusKeyline")
           ?? throw new InvalidOperationException("ConnectionStrings:IndusKeyline not configured.");
        // PrintudeERP's module catalog lives in its OWN DB (IndusPrintudeDemo, 15.206.241.195). Optional —
        // only used when a PrintudeERP client's Module Settings tab is opened / saved.
        _printudeCatalogCs = cfg.GetConnectionString("IndusPrintudeCatalog");
        // "Default" is resolved mode-aware (same as Db.cs): the app DB connection strings live under
        // ConnectionStrings:{Local|Server}:Default, NOT a flat ConnectionStrings:Default. DatabaseMode
        // (default "Local") picks the section; fall back to a flat Default for older configs.
        var mode = cfg["DatabaseMode"]?.Trim();
        if (string.IsNullOrWhiteSpace(mode)) mode = "Local";
        _localCs = cfg[$"ConnectionStrings:{mode}:Default"] ?? cfg.GetConnectionString("Default")
           ?? throw new InvalidOperationException($"ConnectionStrings:{mode}:Default (or ConnectionStrings:Default) not configured.");
    }

    private SqlConnection Control() => new(_controlCs);
    private SqlConnection Local() => new(_localCs);  // app DB (app.Users, audit log)
    private SqlConnection Keyline() => new(_keylineCs);  // shared enterprise module catalog (IndusEnterpriseKeyline)
    private SqlConnection PrintudeCatalog() => new(_printudeCatalogCs
        ?? throw new InvalidOperationException("ConnectionStrings:IndusPrintudeCatalog not configured (needed for the PrintudeERP module catalog)."));

    // The module catalog shown in "Module Settings" comes from a per-application authoritative source:
    //   Estimoprime → IndusEnterpriseKeyline (Keyline),  PrintudeERP → IndusPrintudeDemo (PrintudeCatalog).
    // Any other app keeps the legacy per-app master table in IndusControl.
    private (SqlConnection conn, string from) DisplayCatalog(string? app) => (app ?? "").ToLowerInvariant() switch
    {
        "estimoprime" or "desktop" => (Keyline(), "dbo.ModuleMaster"),
        "printudeerp" => (PrintudeCatalog(), "dbo.ModuleMaster"),
        _ => (Control(), $"[{MasterTable(app!)}]"),
    };

    // The row copied INTO the client DB when a module is ENABLED — must match what DisplayCatalog lists so
    // a shown module can actually be enabled. Only PrintudeERP diverges; the rest use the shared Keyline
    // catalog exactly as before (Estimoprime already did).
    private (SqlConnection conn, string from) EnableSource(string? app) => (app ?? "").ToLowerInvariant() switch
    {
        "printudeerp" => (PrintudeCatalog(), "dbo.ModuleMaster"),
        _ => (Keyline(), "dbo.ModuleMaster"),
    };

    private sealed class AppUserAuth { public long UserId { get; set; } public string? FullName { get; set; } public string? Email { get; set; } }
    private static SqlConnection Client(string cs)
        => new(new SqlConnectionStringBuilder(cs) { TrustServerCertificate = true, ConnectTimeout = 30 }.ConnectionString);

    private static string MasterTable(string app) => (app ?? "").ToLowerInvariant() switch
    {
        "estimoprime" or "desktop" => "EstimoprimeModuleMaster",
        "printudeerp" => "PrintudeERPModuleMaster",
        "multiunit" => "MultiUnitModuleMaster",
        _ => throw new Exception($"Unknown application for module table: {app}"),
    };

    private const string AuthCols = "UserID, ModuleID, ModuleName, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, IsHomePage, CompanyID, IsLocked, CreatedBy, IsDeletedTransaction";
    private const string AuthVals = "@UserID, @ModuleID, @ModuleName, 1,1,1,1,1,1,1, 0, @CompanyID, 0, @UserID, 0";

    // ── get module settings (catalog + client status) ────────────
    public async Task<List<ModuleSettingsRow>> GetModuleSettingsAsync(string app, string connStr)
    {
        var (catConn, catFrom) = DisplayCatalog(app);
        List<ModuleGroupModuleRow> catalog;
        using (var c = catConn)
        {
            await c.OpenAsync();
            catalog = (await c.QueryAsync<ModuleGroupModuleRow>(
                $"SELECT ModuleHeadName, ModuleDisplayName, ModuleName FROM {catFrom} ORDER BY ModuleHeadName, ModuleDisplayName")).ToList();
        }
        // Dedupe by ModuleName: a module is enabled/disabled per ModuleName (the client's ModuleMaster keys
        // on it), so it must appear ONCE in the settings list. Some per-app master tables carry a stray
        // duplicate (e.g. SalesOrderGang.aspx under both "Order Booking" and a "Sales Order Gang" head) —
        // without this, both rows share the same ModuleName key, so toggling one flips both in the UI.
        catalog = catalog
            .GroupBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()) // catalog is ordered by head → keeps the alphabetically-first head
            .ToList();
        var clientMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var cc = Client(connStr))
        {
            await cc.OpenAsync();
            foreach (var r in await cc.QueryAsync("SELECT ModuleName, CAST(ISNULL(IsDeletedTransaction,0) AS int) AS IsDeletedTransaction FROM ModuleMaster"))
            { string? n = r.ModuleName; if (n != null) clientMap[n] = (int)r.IsDeletedTransaction; }
        }
        return catalog.Select(m => new ModuleSettingsRow
        {
            ModuleHeadName = m.ModuleHeadName, ModuleDisplayName = m.ModuleDisplayName, ModuleName = m.ModuleName,
            Status = clientMap.TryGetValue(m.ModuleName, out var d) && d == 0,
        }).ToList();
    }

    // ── check modules exist ──────────────────────────────────────
    public async Task<(bool has, int count)> CheckModulesExistAsync(string connStr)
    {
        using var cc = Client(connStr);
        await cc.OpenAsync();
        var n = await cc.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ModuleMaster");
        return (n > 0, n);
    }

    // ── save module settings (diff apply to client DB) ───────────
    public async Task<(int inserted, int deleted)> SaveModuleSettingsAsync(SaveModuleSettingsRequest req)
    {
        // Enabling a module copies its full row from the app's authoritative catalog — the SAME source the
        // Module Settings tab lists (Estimoprime/others → IndusEnterpriseKeyline, PrintudeERP →
        // IndusPrintudeDemo). A module with no row in that catalog simply has nothing to copy (no-op).
        var (srcConn, srcFrom) = EnableSource(req.ApplicationName);
        var masterLookup = new Dictionary<string, IDictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        using (var c = srcConn)
        {
            await c.OpenAsync();
            foreach (var row in await c.QueryAsync($"SELECT * FROM {srcFrom} WHERE ISNULL(IsDeletedTransaction,0)=0"))
            {
                var dict = (IDictionary<string, object>)row;
                var name = dict.TryGetValue("ModuleName", out var mn) ? mn?.ToString() : null;
                if (name != null) masterLookup[name] = dict;
            }
        }

        int inserted = 0, deleted = 0;
        await using var cc = Client(req.ConnectionString);
        await cc.OpenAsync();
        // Keyline has a few columns older client DBs don't (e.g. ReactRoutePath) — copy only the columns
        // that actually exist in THIS client's ModuleMaster, else the INSERT would fail.
        var clientCols = new HashSet<string>(
            await cc.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('ModuleMaster')"),
            StringComparer.OrdinalIgnoreCase);
        // NOT-NULL columns of THIS client's ModuleMaster (e.g. PrintDocumentName) that the Keyline source
        // row may leave NULL/absent. We substitute an empty default ('' for text, 0 for numeric) so the
        // INSERT never sends NULL to a NOT NULL column ("Cannot insert the value NULL into column ...").
        var notNullCols = (await cc.QueryAsync(
            @"SELECT c.name AS Name, TYPE_NAME(c.system_type_id) AS TypeName
              FROM sys.columns c
              WHERE c.object_id = OBJECT_ID('ModuleMaster')
                AND c.is_nullable = 0 AND c.is_identity = 0 AND c.is_computed = 0"))
            .Select(r => (Name: (string)r.Name, TypeName: (string?)r.TypeName)).ToList();
        var adminUserId = await cc.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='admin'");
        var companyId = await cc.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;
        var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in await cc.QueryAsync("SELECT ModuleName, CAST(ISNULL(IsDeletedTransaction,0) AS int) AS IsDeletedTransaction FROM ModuleMaster"))
        { string? n = r.ModuleName; if (n != null) existing[n] = (int)r.IsDeletedTransaction; }

        foreach (var mod in req.Modules)
        {
            if (mod.Status)
            {
                if (existing.TryGetValue(mod.ModuleName, out var d) && d == 1)
                {
                    // restore a soft-deleted module: un-delete the module + its (previously soft-deleted) authority
                    await cc.ExecuteAsync("UPDATE ModuleMaster SET IsDeletedTransaction=0 WHERE ModuleName=@n", new { n = mod.ModuleName });
                    await cc.ExecuteAsync("UPDATE UserModuleAuthentication SET IsDeletedTransaction=0 WHERE ModuleName=@n", new { n = mod.ModuleName });
                    if (adminUserId.HasValue)
                    {
                        var mid = await cc.ExecuteScalarAsync<int?>("SELECT ModuleID FROM ModuleMaster WHERE ModuleName=@n", new { n = mod.ModuleName });
                        if (mid.HasValue)
                            await cc.ExecuteAsync(
                                $"IF NOT EXISTS (SELECT 1 FROM UserModuleAuthentication WHERE ModuleID=@ModuleID AND UserID=@UserID) INSERT INTO UserModuleAuthentication ({AuthCols}) VALUES ({AuthVals})",
                                new { UserID = adminUserId.Value, ModuleID = mid.Value, ModuleName = mod.ModuleName, CompanyID = companyId });
                    }
                    inserted++;
                }
                else if (!existing.ContainsKey(mod.ModuleName) && masterLookup.TryGetValue(mod.ModuleName, out var mrow))
                {
                    // SetGroupIndex must stay consistent PER HEAD in the DESTINATION client — do NOT copy Keyline's value.
                    // If this module's ModuleHeadName already exists in the client → reuse that head's SetGroupIndex.
                    // If the head is brand-new to the client → start a new group at (client's MAX SetGroupIndex) + 1.
                    var headName = mrow.TryGetValue("ModuleHeadName", out var hn) ? hn?.ToString() : null;
                    double? headSgi = string.IsNullOrWhiteSpace(headName) ? null
                        : await cc.ExecuteScalarAsync<double?>(
                            "SELECT MIN(SetGroupIndex) FROM ModuleMaster WHERE ModuleHeadName = @h", new { h = headName });
                    var sgi = headSgi ?? await cc.ExecuteScalarAsync<double?>(
                        "SELECT ISNULL(MAX(SetGroupIndex), 0) + 1 FROM ModuleMaster") ?? 1;

                    // copy the Keyline row but override SetGroupIndex with the destination-computed value
                    var toInsert = new Dictionary<string, object>(mrow, StringComparer.OrdinalIgnoreCase) { ["SetGroupIndex"] = sgi };
                    // Any NOT-NULL client column the source left NULL/absent gets an empty default ('' / 0),
                    // so a NULL never reaches a NOT NULL column (e.g. PrintDocumentName -> '').
                    foreach (var (colName, colType) in notNullCols)
                    {
                        if (colName.Equals("ModuleID", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!toInsert.TryGetValue(colName, out var cur) || cur is null || cur is DBNull)
                        {
                            var def = NonNullDefaultFor(colType);
                            if (def is not null) toInsert[colName] = def;
                        }
                    }
                    var newId = await InsertDynamicAsync(cc, "ModuleMaster", toInsert, exclude: new[] { "ModuleID" }, onlyColumns: clientCols);
                    if (adminUserId.HasValue)
                        await cc.ExecuteAsync($"INSERT INTO UserModuleAuthentication ({AuthCols}) VALUES ({AuthVals})",
                            new { UserID = adminUserId.Value, ModuleID = newId, ModuleName = mod.ModuleName, CompanyID = companyId });
                    inserted++;
                }
            }
            else if (existing.TryGetValue(mod.ModuleName, out var d) && d == 0)
            {
                // SOFT delete (never hard delete) — mark the module + its authority as deleted so re-enabling restores it.
                await cc.ExecuteAsync("UPDATE ModuleMaster SET IsDeletedTransaction=1 WHERE ModuleName=@n", new { n = mod.ModuleName });
                await cc.ExecuteAsync("UPDATE UserModuleAuthentication SET IsDeletedTransaction=1 WHERE ModuleName=@n", new { n = mod.ModuleName });
                deleted++;
            }
        }
        return (inserted, deleted);
    }

    // ── copy modules (source client → target client, transactional) ──
    public async Task<int> CopyModulesAsync(CopyModulesRequest req)
    {
        string? targetConn;
        using (var c = Control())
        {
            await c.OpenAsync();
            targetConn = await c.ExecuteScalarAsync<string>(
                "SELECT Conn_String FROM dbo.Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID=@id",
                new { id = req.TargetCompanyUserID });
        }
        if (string.IsNullOrWhiteSpace(targetConn)) throw new Exception("Target client connection string not found.");

        List<IDictionary<string, object>> source;
        await using (var sc = Client(req.SourceConnectionString))
        {
            await sc.OpenAsync();
            source = (await sc.QueryAsync("SELECT * FROM ModuleMaster")).Select(r => (IDictionary<string, object>)r).ToList();
        }
        if (source.Count == 0) throw new Exception("No modules found in source database.");

        await using var tc = Client(targetConn);
        await tc.OpenAsync();
        await using var tx = (SqlTransaction)await tc.BeginTransactionAsync();
        var adminUserId = await tc.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='admin'", transaction: tx);
        var companyId = await tc.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0", transaction: tx) ?? 2;

        await tc.ExecuteAsync("DELETE FROM UserModuleAuthentication", transaction: tx);
        await tc.ExecuteAsync("DELETE FROM ModuleMaster", transaction: tx);

        var cols = source[0].Keys.Where(k => !k.Equals("ModuleID", StringComparison.OrdinalIgnoreCase)).ToList();
        var colList = string.Join(", ", cols.Select(c => $"[{c}]"));
        var total = 0;
        const int batchSize = 50;
        for (var i = 0; i < source.Count; i += batchSize)
        {
            var batch = source.Skip(i).Take(batchSize).ToList();
            var dp = new DynamicParameters();
            var valueGroups = new List<string>();
            for (var b = 0; b < batch.Count; b++)
            {
                var names = cols.Select(col => $"@r{b}_{col}");
                valueGroups.Add("(" + string.Join(", ", names) + ")");
                foreach (var col in cols) dp.Add($"@r{b}_{col}", batch[b].TryGetValue(col, out var v) ? v : null);
            }
            await tc.ExecuteAsync($"INSERT INTO ModuleMaster ({colList}) VALUES {string.Join(", ", valueGroups)}", dp, tx);
            total += batch.Count;
        }

        if (adminUserId.HasValue)
            await tc.ExecuteAsync(
                $"INSERT INTO UserModuleAuthentication ({AuthCols}) SELECT @UserID, ModuleID, ModuleName, 1,1,1,1,1,1,1, 0, @CompanyID, 0, @UserID, 0 FROM ModuleMaster",
                new { UserID = adminUserId.Value, CompanyID = companyId }, tx);

        await tx.CommitAsync();
        return total;
    }

    // ── module groups (Indus DB) ─────────────────────────────────
    public async Task<List<string>> GetModuleGroupsAsync(string app)
    {
        using var c = Control();
        await c.OpenAsync();
        return (await c.QueryAsync<string>(
            "SELECT DISTINCT ModuleGroupName FROM dbo.ModuleGroupMaster WHERE ApplicationName=@app ORDER BY ModuleGroupName",
            new { app })).ToList();
    }

    public async Task<List<ModuleGroupModuleRow>> GetModuleGroupModulesAsync(string app, string group)
    {
        using var c = Control();
        await c.OpenAsync();
        return (await c.QueryAsync<ModuleGroupModuleRow>(
            "SELECT ModuleHeadName, ModuleDisplayName, ModuleName FROM dbo.ModuleGroupMaster WHERE ApplicationName=@app AND ModuleGroupName=@group ORDER BY ModuleHeadName, ModuleDisplayName",
            new { app, group })).ToList();
    }

    /// <summary>Available modules to build a group — natively read from the app master catalog.</summary>
    public async Task<List<ModuleGroupModuleRow>> GetAvailableModulesAsync(string app)
    {
        var table = MasterTable(app);
        using var c = Control();
        await c.OpenAsync();
        return (await c.QueryAsync<ModuleGroupModuleRow>(
            $"SELECT ModuleHeadName, ModuleDisplayName, ModuleName FROM [{table}] ORDER BY ModuleHeadName, ModuleDisplayName")).ToList();
    }

    public async Task CreateModuleGroupAsync(CreateModuleGroupRequest req)
    {
        var table = MasterTable(req.ApplicationName);
        using var c = Control();
        await c.OpenAsync();
        var dup = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g",
            new { a = req.ApplicationName, g = req.ModuleGroupName });
        if (dup > 0) throw new Exception($"Module group '{req.ModuleGroupName}' already exists.");

        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        foreach (var name in req.SelectedModuleNames)
        {
            var row = (IDictionary<string, object>?)(await c.QuerySingleOrDefaultAsync(
                $"SELECT * FROM [{table}] WHERE ModuleName=@n", new { n = name }, tx));
            if (row is null) continue;
            var meta = new Dictionary<string, object?>
            {
                ["ApplicationName"] = req.ApplicationName, ["ModuleGroupName"] = req.ModuleGroupName,
                ["CompanyID"] = 2, ["UserID"] = 1, ["FYear"] = "2026-2027", ["IsLocked"] = false,
                ["CreatedBy"] = 1, ["CreatedDate"] = DateTime.Now, ["ModifiedBy"] = 0, ["ModifiedDate"] = null,
                ["DeletedBy"] = 0, ["DeletedDate"] = null, ["IsDeletedTransaction"] = false, ["ProductionUnitID"] = 0,
            };
            var exclude = new HashSet<string>(meta.Keys.Concat(new[] { "ModuleID", "IsIntegratedModule" }), StringComparer.OrdinalIgnoreCase);
            await InsertGroupRowAsync(c, row, meta, exclude, tx);
        }
        await tx.CommitAsync();
    }

    // ── edit a module group (diff-based add/remove of its modules) ─────
    public async Task<(int inserted, int deleted)> UpdateModuleGroupAsync(UpdateModuleGroupRequest req)
    {
        var table = MasterTable(req.ApplicationName);
        using var c = Control();
        await c.OpenAsync();

        var exists = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g",
            new { a = req.ApplicationName, g = req.ModuleGroupName });
        if (exists == 0) throw new Exception($"Module group '{req.ModuleGroupName}' does not exist.");

        var current = (await c.QueryAsync<string>(
            "SELECT ModuleName FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g",
            new { a = req.ApplicationName, g = req.ModuleGroupName })).ToList();

        var selected = new HashSet<string>(req.SelectedModuleNames ?? new(), StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var toDelete = current.Where(m => !selected.Contains(m)).ToList();
        var toInsert = (req.SelectedModuleNames ?? new()).Where(m => !currentSet.Contains(m)).ToList();

        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        int deleted = 0, inserted = 0;
        foreach (var name in toDelete)
            deleted += await c.ExecuteAsync(
                "DELETE FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g AND ModuleName=@n",
                new { a = req.ApplicationName, g = req.ModuleGroupName, n = name }, tx);

        foreach (var name in toInsert)
        {
            var row = (IDictionary<string, object>?)(await c.QuerySingleOrDefaultAsync(
                $"SELECT * FROM [{table}] WHERE ModuleName=@n", new { n = name }, tx));
            if (row is null) continue;
            var meta = new Dictionary<string, object?>
            {
                ["ApplicationName"] = req.ApplicationName, ["ModuleGroupName"] = req.ModuleGroupName,
                ["CompanyID"] = 2, ["UserID"] = 1, ["FYear"] = "2026-2027", ["IsLocked"] = false,
                ["CreatedBy"] = 1, ["CreatedDate"] = DateTime.Now, ["ModifiedBy"] = 0, ["ModifiedDate"] = null,
                ["DeletedBy"] = 0, ["DeletedDate"] = null, ["IsDeletedTransaction"] = false, ["ProductionUnitID"] = 0,
            };
            var exclude = new HashSet<string>(meta.Keys.Concat(new[] { "ModuleID", "IsIntegratedModule" }), StringComparer.OrdinalIgnoreCase);
            await InsertGroupRowAsync(c, row, meta, exclude, tx);
            inserted++;
        }
        await tx.CommitAsync();
        return (inserted, deleted);
    }

    // ── delete a module group (re-auth against app.Users login + reason, hard delete + audit) ─────
    public async Task<int> DeleteModuleGroupAsync(DeleteModuleGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new Exception("A reason for deletion is required.");

        // 1) Authenticate against the app's own login users (app.Users) — accepts email OR full name.
        AppUserAuth? actor;
        using (var app = Local())
        {
            await app.OpenAsync();
            actor = await app.QuerySingleOrDefaultAsync<AppUserAuth>(@"
                SELECT TOP 1 UserId, FullName, Email
                FROM app.Users
                WHERE (Email = @u OR FullName = @u)
                  AND PasswordHash = @p
                  AND IsActive = 1
                  AND ISNULL(IsDeletedTransaction,0) = 0
                ORDER BY UserId", new { u = req.UserName, p = req.Password });
        }
        if (actor is null) throw new Exception("Invalid Username or Password.");

        // 2) Delete the group from the control DB (ModuleGroupMaster).
        int deleted;
        using (var c = Control())
        {
            await c.OpenAsync();
            var exists = await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g",
                new { a = req.ApplicationName, g = req.ModuleGroupName });
            if (exists == 0) throw new Exception($"Module group '{req.ModuleGroupName}' does not exist.");

            deleted = await c.ExecuteAsync(
                "DELETE FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g",
                new { a = req.ApplicationName, g = req.ModuleGroupName });
        }

        // 3) Audit — who deleted what, and why (best-effort; never fail the delete on a log error).
        try
        {
            using var app = Local();
            await app.OpenAsync();
            await app.ExecuteAsync(@"
                INSERT INTO app.ModuleGroupDeletionLog
                  (ApplicationName, ModuleGroupName, ModulesDeleted, Reason, DeletedByUserId, DeletedByName, DeletedByEmail, DeletedAt)
                VALUES (@ApplicationName, @ModuleGroupName, @ModulesDeleted, @Reason, @UserId, @Name, @Email, SYSDATETIME())",
                new { req.ApplicationName, req.ModuleGroupName, ModulesDeleted = deleted, req.Reason,
                      UserId = actor.UserId, Name = actor.FullName, Email = actor.Email });
        }
        catch { /* audit is best-effort */ }

        return deleted;
    }

    // ── apply a module group to a client (wipe + repopulate) ─────
    public async Task<int> ApplyModuleGroupToClientAsync(ApplyModuleGroupRequest req)
    {
        List<IDictionary<string, object>> groupRows;
        using (var c = Control())
        {
            await c.OpenAsync();
            groupRows = (await c.QueryAsync(
                "SELECT * FROM dbo.ModuleGroupMaster WHERE ApplicationName=@a AND ModuleGroupName=@g AND ISNULL(IsDeletedTransaction,0)=0",
                new { a = req.ApplicationName, g = req.ModuleGroupName })).Select(r => (IDictionary<string, object>)r).ToList();
        }
        if (groupRows.Count == 0) throw new Exception("Module group has no modules.");

        await using var cc = Client(req.ConnectionString);
        await cc.OpenAsync();
        var adminUserId = await cc.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='admin'");
        if (!adminUserId.HasValue) throw new Exception("Client admin user not found.");
        var companyId = await cc.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;

        await using var tx = (SqlTransaction)await cc.BeginTransactionAsync();
        await cc.ExecuteAsync("DELETE FROM UserModuleAuthentication", transaction: tx);
        await cc.ExecuteAsync("DELETE FROM ModuleMaster", transaction: tx);

        var exclude = new[] { "ModuleID", "ModuleGroupID", "ModuleGroupName", "ApplicationName" };
        var inserted = 0;
        foreach (var row in groupRows)
        {
            var newId = await InsertDynamicAsync(cc, "ModuleMaster", row, exclude, tx);
            var name = row.TryGetValue("ModuleName", out var mn) ? mn?.ToString() ?? "" : "";
            await cc.ExecuteAsync($"INSERT INTO UserModuleAuthentication ({AuthCols}) VALUES ({AuthVals})",
                new { UserID = adminUserId.Value, ModuleID = newId, ModuleName = name, CompanyID = companyId }, tx);
            inserted++;
        }
        await tx.CommitAsync();
        return inserted;
    }

    // ── New Module Addition (client DB ModuleMaster CRUD) ────────
    public async Task<List<ClientModuleDto>> GetClientModulesAsync(string connStr)
    {
        using var cc = Client(connStr);
        await cc.OpenAsync();
        return (await cc.QueryAsync<ClientModuleDto>(@"
            SELECT ModuleId, ModuleName, ModuleHeadName, ModuleDisplayName, ModuleHeadDisplayName,
                   ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex
            FROM ModuleMaster WHERE ISNULL(IsDeletedTransaction,0)=0
            ORDER BY ModuleHeadName, ModuleDisplayName")).ToList();
    }

    /// <summary>Catalog for the "from catalog" picker — natively read from the app master catalog.</summary>
    public Task<List<ModuleGroupModuleRow>> GetCatalogModulesAsync(string app) => GetAvailableModulesAsync(app);

    /// <summary>
    /// Rich catalog (adds ModuleHeadDisplayName, SetGroupIndex, display orders) for the New Module
    /// auto-fill — mirrors BulkImport's GetIndusModules but sourced from the app master catalog.
    /// </summary>
    public async Task<List<ClientModuleDto>> GetCatalogRichAsync(string app)
    {
        // Sourced from the shared Keyline enterprise catalog (IndusEnterpriseKeyline.dbo.ModuleMaster) —
        // the same master the client Change-Request Module / Sub-Module dropdowns use. Enterprise-wide
        // (not per-app); the `app` argument is kept for API compatibility. Soft-deleted rows excluded.
        _ = app;
        using var c = Keyline();
        await c.OpenAsync();
        return (await c.QueryAsync<ClientModuleDto>(
            @"SELECT CAST(ModuleID AS int) AS ModuleId, ModuleName, ModuleHeadName, ModuleDisplayName, ModuleHeadDisplayName,
                     ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex
              FROM dbo.ModuleMaster
              WHERE ISNULL(IsDeletedTransaction,0)=0
                AND NULLIF(LTRIM(RTRIM(ModuleHeadName)),'') IS NOT NULL
              ORDER BY ModuleHeadName, ModuleDisplayName")).ToList();
    }

    public async Task<int> CreateClientModuleAsync(ClientModuleRequest req)
    {
        var m = req.Module;
        await using var cc = Client(req.ConnectionString);
        await cc.OpenAsync();

        // Free up the chosen head-display-order slot within the group (mirror BulkImport:
        // if the order is already taken, shift every module at/after it by +1).
        if (m.ModuleHeadDisplayOrder.HasValue && m.SetGroupIndex.HasValue)
        {
            var taken = await cc.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ModuleMaster WHERE ModuleHeadDisplayOrder=@o AND SetGroupIndex=@g",
                new { o = m.ModuleHeadDisplayOrder.Value, g = m.SetGroupIndex.Value });
            if (taken > 0)
                await cc.ExecuteAsync(
                    @"UPDATE ModuleMaster SET ModuleHeadDisplayOrder = ModuleHeadDisplayOrder + 1,
                             ModuleDisplayOrder = ModuleDisplayOrder + 1
                      WHERE ModuleHeadDisplayOrder >= @o AND SetGroupIndex = @g AND ISNULL(IsDeletedTransaction,0)=0",
                    new { o = m.ModuleHeadDisplayOrder.Value, g = m.SetGroupIndex.Value });
        }
        m.ModuleDisplayOrder = m.ModuleHeadDisplayOrder; // ModuleDisplayOrder mirrors ModuleHeadDisplayOrder

        var newId = await cc.ExecuteScalarAsync<int>(@"
            INSERT INTO ModuleMaster (ModuleName, ModuleHeadName, ModuleDisplayName, ModuleHeadDisplayName,
                ModuleHeadDisplayOrder, ModuleDisplayOrder, SetGroupIndex, CompanyID)
            VALUES (@ModuleName, @ModuleHeadName, @ModuleDisplayName, @ModuleHeadDisplayName,
                @ModuleHeadDisplayOrder, @ModuleDisplayOrder, @SetGroupIndex, 2);
            SELECT CAST(SCOPE_IDENTITY() AS int);", m);
        var adminUserId = await cc.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='admin' AND ISNULL(IsBlocked,0)=0");
        if (adminUserId.HasValue)
            await cc.ExecuteAsync($"INSERT INTO UserModuleAuthentication ({AuthCols}) VALUES ({AuthVals})",
                new { UserID = adminUserId.Value, ModuleID = newId, m.ModuleName, CompanyID = 2 });
        return newId;
    }

    public async Task UpdateClientModuleAsync(ClientModuleRequest req)
    {
        var m = req.Module;
        using var cc = Client(req.ConnectionString);
        await cc.OpenAsync();
        await cc.ExecuteAsync(@"
            UPDATE ModuleMaster SET ModuleName=@ModuleName, ModuleHeadName=@ModuleHeadName, ModuleDisplayName=@ModuleDisplayName,
                ModuleHeadDisplayName=@ModuleHeadDisplayName, ModuleHeadDisplayOrder=@ModuleHeadDisplayOrder,
                ModuleDisplayOrder=@ModuleDisplayOrder, SetGroupIndex=@SetGroupIndex
            WHERE ModuleID=@ModuleId", m);
    }

    public async Task SoftDeleteClientModuleAsync(string connStr, int moduleId)
    {
        using var cc = Client(connStr);
        await cc.OpenAsync();
        await cc.ExecuteAsync("UPDATE ModuleMaster SET IsDeletedTransaction=1 WHERE ModuleID=@moduleId", new { moduleId });
    }

    // ── Indus Tool Authority (central Indus DB: IndusToolModuleMaster + CompanyModuleAuthority) ──
    public async Task<List<IndusToolModuleDto>> GetModulesForCompanyAsync(string companyUserId)
    {
        using var c = Control();
        await c.OpenAsync();
        return (await c.QueryAsync<IndusToolModuleDto>(@"
            SELECT m.ModuleID, m.ModuleName, m.ModulePath, m.ModuleIcon, m.DisplayOrder,
                   CASE WHEN a.ModuleID IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsEnabled
            FROM dbo.IndusToolModuleMaster m
            LEFT JOIN dbo.CompanyModuleAuthority a ON a.ModuleID = m.ModuleID AND a.CompanyUserID = @CompanyUserID
            ORDER BY m.DisplayOrder", new { CompanyUserID = companyUserId })).ToList();
    }

    public async Task<int> SaveCompanyModuleAuthorityAsync(string companyUserId, List<int> enabledIds)
    {
        await using var c = Control();
        await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("DELETE FROM dbo.CompanyModuleAuthority WHERE CompanyUserID=@id", new { id = companyUserId }, tx);
        foreach (var mid in enabledIds)
            await c.ExecuteAsync("INSERT INTO dbo.CompanyModuleAuthority (CompanyUserID, ModuleID) VALUES (@c, @m)",
                new { c = companyUserId, m = mid }, tx);
        await tx.CommitAsync();
        return enabledIds.Count;
    }

    // ── dynamic insert helpers ───────────────────────────────────
    // Empty default for a NOT NULL column whose value would otherwise be NULL: '' for text types,
    // 0 for numeric/bit. Returns null for types we can't safely default (datetime, uniqueidentifier, …)
    // so the caller leaves those untouched.
    private static object? NonNullDefaultFor(string? sqlType)
    {
        var t = (sqlType ?? "").ToLowerInvariant();
        if (t is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml") return "";
        if (t is "bit" or "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real") return 0;
        return null;
    }

    private static async Task<int> InsertDynamicAsync(SqlConnection cc, string tableInto, IDictionary<string, object> row, IEnumerable<string> exclude, IDbTransaction? tx = null, IReadOnlySet<string>? onlyColumns = null)
    {
        var ex = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
        // onlyColumns (when given) restricts the insert to columns that exist in the target table — used when
        // the source row (e.g. Keyline) has extra columns the target ModuleMaster doesn't.
        var cols = row.Keys.Where(k => !ex.Contains(k) && (onlyColumns is null || onlyColumns.Contains(k))).ToList();
        var colList = string.Join(", ", cols.Select(c => $"[{c}]"));
        var parList = string.Join(", ", cols.Select(c => $"@{c}"));
        var dp = new DynamicParameters();
        foreach (var col in cols) dp.Add("@" + col, row[col]);
        return await cc.ExecuteScalarAsync<int>($"INSERT INTO {tableInto} ({colList}) OUTPUT INSERTED.ModuleID VALUES ({parList})", dp, tx);
    }

    private static async Task InsertGroupRowAsync(SqlConnection c, IDictionary<string, object> templateRow, Dictionary<string, object?> meta, HashSet<string> excludeFromTemplate, IDbTransaction tx)
    {
        var cols = templateRow.Keys.Where(k => !excludeFromTemplate.Contains(k)).ToList();
        var dp = new DynamicParameters();
        var colNames = new List<string>();
        foreach (var col in cols) { colNames.Add($"[{col}]"); dp.Add("@" + col, templateRow[col]); }
        foreach (var kv in meta) { colNames.Add($"[{kv.Key}]"); dp.Add("@" + kv.Key, kv.Value); }
        var parNames = colNames.Select(n => "@" + n.Trim('[', ']'));
        await c.ExecuteAsync($"INSERT INTO dbo.ModuleGroupMaster ({string.Join(", ", colNames)}) VALUES ({string.Join(", ", parNames)})", dp, tx);
    }
}
