using System.Diagnostics;
using Indus360.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Controllers;

/// <summary>
/// Deployment / liveness diagnostics.
///   GET /api/health     -> the API process is up (no DB touched — fast, safe for uptime pings / IIS warmup)
///   GET /api/health/db  -> opens EVERY configured DB connection and reports which connect and which fail
/// NOTE: /api/health/db reveals DB server hostnames + catalog names (never passwords). Handy while bringing
/// the deployment up; consider removing or protecting it once the server is verified live.
/// </summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly Db _db;
    public HealthController(Db db) => _db = db;

    /// <summary>Liveness — proves the API host is running. Does not touch any database.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "Indus 360 API",
        mode = _db.Mode,
        utc = DateTime.UtcNow,
    });

    /// <summary>Readiness — tests connectivity to every configured database.</summary>
    [HttpGet("db")]
    public async Task<IActionResult> DbHealth()
    {
        var databases = new List<object>();
        var allOk = true;

        foreach (var (name, cs) in _db.ConfiguredConnections)
        {
            if (string.IsNullOrWhiteSpace(cs))
            {
                databases.Add(new { name, configured = false });
                continue;
            }

            // Force a short connect timeout so an unreachable DB fails fast — the Server-mode
            // strings carry "Connection Timeout=3600", which would otherwise hang the health check.
            string server = "?", catalog = "?", conn;
            try
            {
                var b = new SqlConnectionStringBuilder(cs) { ConnectTimeout = 5 };
                server = b.DataSource;
                catalog = b.InitialCatalog;
                conn = b.ConnectionString;
            }
            catch
            {
                conn = cs;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                await using var c = new SqlConnection(conn);
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync();
                sw.Stop();
                databases.Add(new { name, ok = true, server, catalog, ms = sw.ElapsedMilliseconds });
            }
            catch (Exception ex)
            {
                sw.Stop();
                allOk = false;
                databases.Add(new { name, ok = false, server, catalog, ms = sw.ElapsedMilliseconds, error = ex.Message });
            }
        }

        var payload = new
        {
            status = allOk ? "healthy" : "degraded",
            mode = _db.Mode,
            utc = DateTime.UtcNow,
            databases,
        };
        return allOk ? Ok(payload) : StatusCode(503, payload);
    }
}
