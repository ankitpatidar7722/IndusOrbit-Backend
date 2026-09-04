using Dapper;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

public sealed class KeylineModuleRow
{
    public string Head { get; set; } = "";   // ModuleHeadDisplayName → "Module Name"
    public string Name { get; set; } = "";   // ModuleDisplayName     → "Sub Module Name"
}

/// <summary>
/// Read-only lookup from the Keyline enterprise catalog (IndusEnterpriseKeyline on the
/// remote server) — used to populate the cascading Module Name / Sub Module Name dropdowns
/// on the client Change-Request form. Module Name = distinct ModuleHeadDisplayName; the
/// Sub Modules for a Module Name are the ModuleDisplayName rows under that head.
/// </summary>
public sealed class KeylineRepository
{
    private readonly string _cs;
    private readonly Services.CacheService _cache;
    public KeylineRepository(IConfiguration cfg, Services.CacheService cache)
    {
        _cs = cfg.GetConnectionString("IndusKeyline")
           ?? throw new InvalidOperationException("ConnectionStrings:IndusKeyline not configured.");
        _cache = cache;
    }

    /// <summary>Every (head, sub-module) pair, so the client can build both dropdowns + cascade.
    /// This is static reference data on a remote DB — cached 30 min (no writes happen here).</summary>
    public async Task<List<KeylineModuleRow>> GetModulesAsync() =>
        await _cache.GetOrCreateAsync("keyline:modules", TimeSpan.FromMinutes(30), async () =>
        {
            using var c = new SqlConnection(_cs);
            await c.OpenAsync();
            return (await c.QueryAsync<KeylineModuleRow>(@"
            SELECT DISTINCT ModuleHeadDisplayName AS Head, ModuleDisplayName AS Name
            FROM dbo.ModuleMaster
            WHERE NULLIF(LTRIM(RTRIM(ModuleHeadDisplayName)),'') IS NOT NULL
              AND NULLIF(LTRIM(RTRIM(ModuleDisplayName)),'') IS NOT NULL
            ORDER BY ModuleHeadDisplayName, ModuleDisplayName")).ToList();
        });
}
