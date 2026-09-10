using System.Text.RegularExpressions;
using Backend.Services;
using Dapper;
using Indus360.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Indus360-native "Database Backup" (Admin -> Database Backup): pick a client and download a fresh
/// compressed backup of that client's DB with live % progress. Reuses the folded-in BulkImport
/// <see cref="IDatabaseBackupRestoreService"/> for the actual BACKUP + server-side zip + cross-server
/// transfer (see also the /bulk/api/DatabaseBackupRestore endpoints). Indus360 auth model (no JWT).
/// </summary>
[ApiController]
[Route("api/database-backup")]
public sealed class DatabaseBackupController : ControllerBase
{
    private readonly Db _db;
    private readonly IDatabaseBackupRestoreService _backup;

    public DatabaseBackupController(Db db, IDatabaseBackupRestoreService backup)
    {
        _db = db;
        _backup = backup;
    }

    public sealed class StartRequest
    {
        public string? CompanyUserId { get; set; }
    }

    /// <summary>Resolve the client's DB (from the control DB) and kick off the backup. Returns operationId.</summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartRequest? req)
    {
        var cuid = req?.CompanyUserId?.Trim();
        if (string.IsNullOrWhiteSpace(cuid))
            return BadRequest(new { error = "companyUserId is required." });

        string? connStr;
        await using (var ctrl = await _db.OpenControlAsync())
        {
            connStr = await ctrl.ExecuteScalarAsync<string?>(
                "SELECT Conn_String FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @cuid",
                new { cuid });
        }
        if (string.IsNullOrWhiteSpace(connStr))
            return NotFound(new { error = "No database connection found for this client." });

        var server = Extract(connStr, @"Data Source=([^;]+)");
        var database = Extract(connStr, @"Initial Catalog=([^;]+)");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            return BadRequest(new { error = "Client connection string is missing a server or database name." });

        // Admin login for BACKUP + OPENROWSET read-back (needs sysadmin/bulkadmin). Packet Size=32767
        // streams the .bak back ~2.4x faster.
        var backupConn = $"Data Source={server};Initial Catalog=master;User ID=indus;Password=Param@99811;" +
                         "TrustServerCertificate=True;Packet Size=32767";

        var operationId = _backup.StartTrackedBackup(backupConn, database);
        return Ok(new { operationId, databaseName = database });
    }

    /// <summary>Poll for progress: { percentComplete, stage, message, isComplete, success, error }.</summary>
    [HttpGet("status/{operationId}")]
    public async Task<IActionResult> Status(string operationId)
    {
        var st = await _backup.GetOperationStatusAsync(operationId);
        if (st == null) return NotFound(new { error = "Unknown or expired operation." });
        return Ok(st);
    }

    /// <summary>Stream the finished .zip once, then clean it up.</summary>
    [HttpGet("download/{operationId}")]
    public IActionResult Download(string operationId)
    {
        if (!_backup.TryTakeResultZip(operationId, out var zipPath) || !System.IO.File.Exists(zipPath))
            return NotFound(new { error = "Backup is not ready yet or was already downloaded." });

        var fileName = Path.GetFileName(zipPath);
        var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.RegisterForDispose(fs);
        Response.OnCompleted(async () =>
        {
            try
            {
                await Task.Delay(1000);
                if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
            }
            catch { /* best-effort cleanup */ }
        });
        return File(fs, "application/zip", fileName);
    }

    private static string? Extract(string input, string pattern)
    {
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
