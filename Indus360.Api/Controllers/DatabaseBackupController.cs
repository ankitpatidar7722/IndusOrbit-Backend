using System.Text.RegularExpressions;
using Backend.Services;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

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
    private readonly SavedDbServerRepository _saved;

    public DatabaseBackupController(Db db, IDatabaseBackupRestoreService backup, SavedDbServerRepository saved)
    {
        _db = db;
        _backup = backup;
        _saved = saved;
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

    /// <summary>Ad-hoc server credentials typed on the page ("By Server" mode). Never stored.</summary>
    public sealed class ServerRequest
    {
        public string? Server { get; set; }
        public string? UserId { get; set; }
        public string? Password { get; set; }
        public string? Database { get; set; }
        /// <summary>Store the password (encrypted) for next time.</summary>
        public bool Remember { get; set; }
        /// <summary>Password left blank on purpose: use the one saved for this server.</summary>
        public bool UseSaved { get; set; }
    }

    /// <summary>"By Server": this user's previously used servers (newest first). Passwords are never returned.</summary>
    [HttpGet("saved-servers")]
    public async Task<IActionResult> SavedServers()
    {
        var uid = this.CurrentUserId();
        if (uid is null) return Ok(Array.Empty<object>());
        var list = await _saved.ListAsync(uid.Value);
        return Ok(list.Select(s => new { server = s.Server, userId = s.LoginUser, hasPassword = s.HasPassword }));
    }

    /// <summary>"By Server": forget one saved server (and its saved password).</summary>
    [HttpDelete("saved-servers")]
    public async Task<IActionResult> ForgetServer([FromQuery] string server)
    {
        var uid = this.CurrentUserId();
        if (uid is null || string.IsNullOrWhiteSpace(server)) return BadRequest(new { error = "Server name is required." });
        await _saved.DeleteAsync(uid.Value, NormalizeServer(server)!);
        return Ok(new { ok = true });
    }

    /// <summary>"By Server": connect with the typed credentials and list that server's user databases.</summary>
    [HttpPost("server-databases")]
    public async Task<IActionResult> ServerDatabases([FromBody] ServerRequest? req)
    {
        var (connStr, bad) = await ResolveServerConnAsync(req);
        if (connStr == null) return BadRequest(new { error = bad });
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            // Connected → remember the server (and the password only if asked) for next time.
            var uid = this.CurrentUserId();
            if (uid is not null)
            {
                try
                {
                    await _saved.UpsertAsync(uid.Value, NormalizeServer(req!.Server)!, req.UserId!.Trim(),
                        req.UseSaved ? null : req.Password, req.Remember);
                }
                catch (Exception ex) { Console.WriteLine($"[DatabaseBackup] saving server history skipped: {ex.Message}"); }
            }
            var rows = await conn.QueryAsync(
                @"SELECT d.name AS Name,
                         CAST(ISNULL(SUM(CAST(f.size AS bigint)) * 8 / 1024, 0) AS bigint) AS SizeMB
                  FROM sys.databases d
                  LEFT JOIN sys.master_files f ON f.database_id = d.database_id
                  WHERE d.database_id > 4 AND d.state = 0 AND d.source_database_id IS NULL
                  GROUP BY d.name
                  ORDER BY d.name");
            return Ok(rows.Select(r => new { name = (string)r.Name, sizeMB = (long)r.SizeMB }));
        }
        catch (SqlException ex)
        {
            return BadRequest(new { error = ConnectError(ex) });
        }
    }

    /// <summary>"By Server": back up one database from the typed server. Returns operationId (same polling/download).</summary>
    [HttpPost("start-server")]
    public async Task<IActionResult> StartServer([FromBody] ServerRequest? req)
    {
        var (connStr, bad) = await ResolveServerConnAsync(req);
        if (connStr == null) return BadRequest(new { error = bad });
        var database = req!.Database?.Trim();
        if (string.IsNullOrWhiteSpace(database)) return BadRequest(new { error = "Please select a database." });
        try
        {
            // Only accept a name that really exists on that server (it is used inside BACKUP DATABASE [..]).
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.databases WHERE name = @database AND database_id > 4 AND state = 0", new { database });
            if (exists == 0) return NotFound(new { error = $"Database '{database}' was not found on this server." });
        }
        catch (SqlException ex)
        {
            return BadRequest(new { error = ConnectError(ex) });
        }

        var operationId = _backup.StartTrackedBackup(connStr, database);
        return Ok(new { operationId, databaseName = database });
    }

    // "52.66.183.53, 1433" -> "52.66.183.53,1433" (spaces around the port comma).
    private static string? NormalizeServer(string? server)
        => server == null ? null : Regex.Replace(server.Trim(), @"\s*,\s*", ",");

    private async Task<(string? ConnStr, string Error)> ResolveServerConnAsync(ServerRequest? req)
    {
        var server = NormalizeServer(req?.Server);
        var user = req?.UserId?.Trim();
        if (string.IsNullOrWhiteSpace(server)) return (null, "Server name is required.");
        if (string.IsNullOrWhiteSpace(user)) return (null, "User ID is required.");
        var password = req!.Password;
        if (req.UseSaved && string.IsNullOrEmpty(password))
        {
            var uid = this.CurrentUserId();
            var saved = uid is null ? null : await _saved.GetCredentialsAsync(uid.Value, server);
            if (saved is null || !string.Equals(saved.Value.LoginUser, user, StringComparison.OrdinalIgnoreCase))
                return (null, "No saved password for this server — please enter the password.");
            password = saved.Value.Password;
        }
        if (string.IsNullOrEmpty(password)) return (null, "Password is required.");
        // Builder (not string concat) so typed values can't inject extra connection keywords.
        var connStr = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "master",
            UserID = user,
            Password = password,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            PacketSize = 32767,
        }.ConnectionString;
        return (connStr, "");
    }

    private static string ConnectError(SqlException ex) => ex.Number switch
    {
        18456 => "Login failed — please check the User ID and Password.",
        -2 or 53 or 2 or 258 => "Could not reach the server — please check the server name / port.",
        _ => $"Could not connect: {ex.Message}",
    };

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
