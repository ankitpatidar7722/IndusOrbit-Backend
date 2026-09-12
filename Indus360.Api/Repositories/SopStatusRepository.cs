using Dapper;
using Indus360.Api.Data;

namespace Indus360.Api.Repositories;

/// <summary>One saved SOP-status row for a (client, web module).</summary>
public sealed class SopStatusRow
{
    public string ModuleName { get; set; } = "";
    public string? YoutubeLink { get; set; }
    public string? SopDocument { get; set; }
    public bool Status { get; set; }
}

/// <summary>A SOP-bearing web module (for the Estimoprime default seed).</summary>
public sealed record SopModuleInfo(string ModuleName, string ModuleHeadName, string ModuleDisplayName, string SopSlug);

/// <summary>
/// Per-client SOP data for web modules (app.SopModuleStatus). Keyed by (ClientCode = CompanyUniqueCode,
/// ModuleName = the Keyline ModuleName). Holds the module's Youtube link, SOP document and a Status
/// tick — edited on the "SOP of Web Modules" grid. Table is created on first use (publish-only deploy
/// needs no manual SQL).
/// </summary>
public sealed class SopStatusRepository
{
    private readonly Db _db;
    public SopStatusRepository(Db db) => _db = db;

    private static bool _ready;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private async Task EnsureAsync(Microsoft.Data.SqlClient.SqlConnection c)
    {
        if (_ready) return;
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            await c.ExecuteAsync(@"
IF OBJECT_ID('app.SopModuleStatus','U') IS NULL
BEGIN
    CREATE TABLE app.SopModuleStatus (
        Id                BIGINT IDENTITY(1,1) CONSTRAINT PK_SopModuleStatus PRIMARY KEY,
        ClientCode        NVARCHAR(100) NOT NULL,
        ModuleName        NVARCHAR(300) NOT NULL,
        ModuleHeadName    NVARCHAR(200) NULL,
        ModuleDisplayName NVARCHAR(300) NULL,
        SopSlug           NVARCHAR(200) NULL,
        YoutubeLink       NVARCHAR(1000) NULL,
        SopDocument       NVARCHAR(MAX) NULL,
        Status            BIT NOT NULL CONSTRAINT DF_SopModuleStatus_Status DEFAULT 0,
        ModifiedBy        INT NULL,
        ModifiedAt        DATETIME2 NOT NULL CONSTRAINT DF_SopModuleStatus_Mod DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_SopModuleStatus_Key ON app.SopModuleStatus(ClientCode, ModuleName);
END
ELSE
BEGIN
    -- Older table: add the denormalized columns the ERP SOP tab reads (idempotent).
    IF COL_LENGTH('app.SopModuleStatus','ModuleHeadName')    IS NULL ALTER TABLE app.SopModuleStatus ADD ModuleHeadName    NVARCHAR(200) NULL;
    IF COL_LENGTH('app.SopModuleStatus','ModuleDisplayName') IS NULL ALTER TABLE app.SopModuleStatus ADD ModuleDisplayName NVARCHAR(300) NULL;
    IF COL_LENGTH('app.SopModuleStatus','SopSlug')           IS NULL ALTER TABLE app.SopModuleStatus ADD SopSlug           NVARCHAR(200) NULL;
END");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>All saved SOP rows for one client.</summary>
    public async Task<List<SopStatusRow>> GetForClientAsync(string clientCode)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var rows = await c.QueryAsync<SopStatusRow>(
            "SELECT ModuleName, YoutubeLink, SopDocument, Status FROM app.SopModuleStatus WHERE ClientCode = @clientCode",
            new { clientCode });
        return rows.AsList();
    }

    /// <summary>Insert-or-update one module's SOP data for a client (with denormalized head/display/slug).</summary>
    public async Task UpsertAsync(string clientCode, string moduleName, string? head, string? display, string? slug,
        string? youtube, string? sopDoc, bool status, int? userId)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        await c.ExecuteAsync(@"
UPDATE app.SopModuleStatus
   SET ModuleHeadName=@head, ModuleDisplayName=@display, SopSlug=@slug,
       YoutubeLink=@youtube, SopDocument=@sopDoc, Status=@status, ModifiedBy=@userId, ModifiedAt=SYSUTCDATETIME()
 WHERE ClientCode=@clientCode AND ModuleName=@moduleName;
IF @@ROWCOUNT = 0
   INSERT INTO app.SopModuleStatus (ClientCode, ModuleName, ModuleHeadName, ModuleDisplayName, SopSlug, YoutubeLink, SopDocument, Status, ModifiedBy)
   VALUES (@clientCode, @moduleName, @head, @display, @slug, @youtube, @sopDoc, @status, @userId);",
            new { clientCode, moduleName, head, display, slug, youtube, sopDoc, status, userId });
    }

    /// <summary>Bulk-tick Status=true for the given (client × SOP module) pairs — the Estimoprime default
    /// seed. Existing rows are set to true (and head/display/slug filled); missing rows are inserted.
    /// Returns rows affected.</summary>
    public async Task<int> SeedTrueAsync(IReadOnlyList<string> clientCodes, IReadOnlyList<SopModuleInfo> modules)
    {
        if (clientCodes.Count == 0 || modules.Count == 0) return 0;
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var valuesSql = string.Join(",", modules.Select((_, i) => $"(@n{i},@h{i},@d{i},@g{i})"));
        int total = 0;
        foreach (var code in clientCodes)
        {
            var dp = new DynamicParameters();
            dp.Add("code", code);
            for (int i = 0; i < modules.Count; i++)
            {
                dp.Add($"n{i}", modules[i].ModuleName); dp.Add($"h{i}", modules[i].ModuleHeadName);
                dp.Add($"d{i}", modules[i].ModuleDisplayName); dp.Add($"g{i}", modules[i].SopSlug);
            }
            total += await c.ExecuteAsync($@"
MERGE app.SopModuleStatus AS t
USING (SELECT @code AS ClientCode, v.n AS ModuleName, v.h AS Head, v.d AS Display, v.g AS Slug
       FROM (VALUES {valuesSql}) v(n,h,d,g)) AS s
  ON t.ClientCode = s.ClientCode AND t.ModuleName = s.ModuleName
WHEN MATCHED THEN UPDATE SET Status = 1, ModuleHeadName = s.Head, ModuleDisplayName = s.Display, SopSlug = s.Slug, ModifiedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (ClientCode, ModuleName, ModuleHeadName, ModuleDisplayName, SopSlug, Status, ModifiedAt)
       VALUES (s.ClientCode, s.ModuleName, s.Head, s.Display, s.Slug, 1, SYSUTCDATETIME());", dp);
        }
        return total;
    }
}
