using System.Text.RegularExpressions;
using Backend.Services;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Repositories;
using Indus360.Api.Services;
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
    private readonly BackupUploadService _upload;

    public DatabaseBackupController(Db db, IDatabaseBackupRestoreService backup, SavedDbServerRepository saved, BackupUploadService upload)
    {
        _db = db;
        _backup = backup;
        _saved = saved;
        _upload = upload;
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
        /// <summary>Upload: original .bak file name + size in bytes.</summary>
        public string? FileName { get; set; }
        public long Size { get; set; }
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

    /// <summary>"By Server": read-only health snapshot — drives (total/free), CPU/RAM, SQL version and
    /// per-database data/log sizes + last full backup. Each section is optional: a login without
    /// VIEW SERVER STATE / sysadmin just gets fewer details instead of an error.</summary>
    [HttpPost("server-info")]
    public async Task<IActionResult> ServerInfo([FromBody] ServerRequest? req)
    {
        // Opt-in feature permission (Users → User Profile → "Can See Server Detail").
        if (!await HasPermissionAsync(ServerDetailPermission))
            return StatusCode(403, new { error = "You do not have permission to see server details." });

        var (connStr, bad) = await ResolveServerConnAsync(req);
        if (connStr == null) return BadRequest(new { error = bad });
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var warnings = new List<string>();

            var basic = await conn.QueryFirstAsync(
                @"SELECT @@SERVERNAME AS ServerName,
                         CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) AS ProductVersion,
                         CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(64)) AS ProductLevel,
                         CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition");

            int? cpu = null; long? ramMB = null, ramFreeMB = null, sqlMemMB = null; DateTime? startTime = null;
            try
            {
                var sys = await conn.QueryFirstAsync(
                    "SELECT cpu_count AS Cpu, physical_memory_kb / 1024 AS RamMB, sqlserver_start_time AS StartTime FROM sys.dm_os_sys_info");
                cpu = (int)sys.Cpu; ramMB = (long)sys.RamMB; startTime = (DateTime)sys.StartTime;
                var mem = await conn.QueryFirstAsync(
                    @"SELECT (SELECT available_physical_memory_kb / 1024 FROM sys.dm_os_sys_memory) AS FreeMB,
                             (SELECT physical_memory_in_use_kb / 1024 FROM sys.dm_os_process_memory) AS SqlMB");
                ramFreeMB = (long?)mem.FreeMB; sqlMemMB = (long?)mem.SqlMB;
            }
            catch (SqlException) { warnings.Add("CPU / memory details need the VIEW SERVER STATE permission."); }

            // Drives: total + free for every drive holding a database file; xp_fixeddrives adds free
            // space for the remaining fixed drives (no total available for those).
            var drives = new Dictionary<string, DriveInfoDto>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var vols = await conn.QueryAsync(
                    @"SELECT DISTINCT vs.volume_mount_point AS Mount, vs.logical_volume_name AS Label,
                             vs.total_bytes AS Total, vs.available_bytes AS Free
                      FROM sys.master_files f CROSS APPLY sys.dm_os_volume_stats(f.database_id, f.file_id) vs");
                foreach (var v in vols)
                {
                    var key = ((string)v.Mount).TrimEnd('\\');
                    drives[key] = new DriveInfoDto(key, (string?)v.Label, (long)v.Total / 1048576, (long)v.Free / 1048576);
                }
            }
            catch (SqlException) { warnings.Add("Drive size details need the VIEW SERVER STATE permission."); }
            try
            {
                var fixedDrives = await conn.QueryAsync("EXEC master.dbo.xp_fixeddrives");
                foreach (IDictionary<string, object> row in fixedDrives)
                {
                    var vals = row.Values.ToArray();
                    var key = $"{vals[0]}:";
                    if (!drives.ContainsKey(key)) drives[key] = new DriveInfoDto(key, null, null, Convert.ToInt64(vals[1]));
                }
            }
            catch (SqlException) { /* needs sysadmin — the volume list above is enough */ }

            // Databases: data/log size, file drives, last full backup.
            var files = await conn.QueryAsync(
                @"SELECT d.name AS Name, d.state_desc AS State, d.recovery_model_desc AS Recovery, d.create_date AS Created,
                         f.type AS FileType, CAST(f.size AS bigint) * 8 / 1024 AS SizeMB, f.physical_name AS Path
                  FROM sys.databases d JOIN sys.master_files f ON f.database_id = d.database_id
                  WHERE d.database_id > 4 AND d.source_database_id IS NULL");
            var lastBackups = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var bk = await conn.QueryAsync(
                    "SELECT database_name AS Name, MAX(backup_finish_date) AS LastFull FROM msdb.dbo.backupset WHERE type = 'D' GROUP BY database_name");
                foreach (var b in bk) if (b.LastFull != null) lastBackups[(string)b.Name] = (DateTime)b.LastFull;
            }
            catch (SqlException) { /* no msdb access */ }

            var databases = files
                .GroupBy(f => (string)f.Name)
                .Select(g =>
                {
                    var first = g.First();
                    var driveSet = g.Select(f => DriveOf((string)f.Path)).Where(d => d != null).Distinct(StringComparer.OrdinalIgnoreCase);
                    return new
                    {
                        name = g.Key,
                        state = (string)first.State,
                        recovery = (string)first.Recovery,
                        created = (DateTime)first.Created,
                        dataMB = g.Where(f => (byte)f.FileType != 1).Sum(f => (long)f.SizeMB),
                        logMB = g.Where(f => (byte)f.FileType == 1).Sum(f => (long)f.SizeMB),
                        drives = string.Join(", ", driveSet),
                        lastBackup = lastBackups.TryGetValue(g.Key, out var lb) ? lb : (DateTime?)null,
                    };
                })
                .OrderByDescending(d => d.dataMB + d.logMB)
                .ToList();

            return Ok(new
            {
                serverName = (string?)basic.ServerName,
                version = SqlVersionName((string?)basic.ProductVersion),
                productVersion = (string?)basic.ProductVersion,
                productLevel = (string?)basic.ProductLevel,
                edition = (string?)basic.Edition,
                cpuCount = cpu,
                ramMB,
                ramFreeMB,
                sqlMemoryMB = sqlMemMB,
                startTime,
                drives = drives.Values.OrderBy(d => d.Drive).Select(d => new
                {
                    drive = d.Drive, label = d.Label, totalMB = d.TotalMB, freeMB = d.FreeMB,
                    dbCount = databases.Count(x => x.drives.Split(", ").Contains(d.Drive, StringComparer.OrdinalIgnoreCase)),
                }),
                databases,
                warnings,
            });
        }
        catch (SqlException ex)
        {
            return BadRequest(new { error = ConnectError(ex) });
        }
    }

    private const string ServerDetailPermission = "databaseBackup.serverDetail";
    private const string UploadRestorePermission = "databaseBackup.uploadRestore";

    private async Task<bool> HasPermissionAsync(string key)
    {
        var uid = this.CurrentUserId();
        if (uid is null) return false;
        await using var app = await _db.OpenAsync();
        return await app.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM app.UserPermissions WHERE UserId = @uid AND PermissionKey = @key",
            new { uid = uid.Value, key }) > 0;
    }

    private IActionResult NoUploadPermission()
        => StatusCode(403, new { error = "You do not have permission to upload or restore backups." });

    // ── Upload a .bak from the user's machine to the server's backup folder (+ optional restore) ──

    /// <summary>Where an uploaded .bak will land on this server + free space on that drive.</summary>
    [HttpPost("upload/target")]
    public async Task<IActionResult> UploadTarget([FromBody] ServerRequest? req)
    {
        if (!await HasPermissionAsync(UploadRestorePermission)) return NoUploadPermission();
        var (connStr, bad) = await ResolveServerConnAsync(req);
        if (connStr == null) return BadRequest(new { error = bad });
        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            var (folder, freeMB) = await _upload.GetTargetAsync(conn, NormalizeServer(req!.Server)!);
            return Ok(new { folder, freeMB, chunkBytes = BackupUploadService.ChunkBytes });
        }
        catch (SqlException ex) { return BadRequest(new { error = ConnectError(ex) }); }
    }

    [HttpPost("upload/start")]
    public async Task<IActionResult> UploadStart([FromBody] ServerRequest? req)
    {
        if (!await HasPermissionAsync(UploadRestorePermission)) return NoUploadPermission();
        if (string.IsNullOrWhiteSpace(req?.FileName) || !req.FileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Please choose a .bak file." });
        if (req.Size <= 0) return BadRequest(new { error = "The file is empty." });
        var (connStr, bad) = await ResolveServerConnAsync(req);
        if (connStr == null) return BadRequest(new { error = bad });
        try
        {
            var s = await _upload.StartAsync(this.CurrentUserId()!.Value, connStr, NormalizeServer(req.Server)!, req.FileName, req.Size);
            return Ok(new { uploadId = s.Id, fileName = s.FileName, folder = s.Folder, parts = s.Parts, chunkBytes = BackupUploadService.ChunkBytes });
        }
        catch (SqlException ex) { return BadRequest(new { error = ConnectError(ex) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>One chunk as the raw request body (gzip when gz=1). Not audited (see AuditActionFilter).</summary>
    [HttpPost("upload-chunk/{uploadId}")]
    [RequestSizeLimit(BackupUploadService.ChunkBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadChunk(string uploadId, [FromQuery] int seq, [FromQuery] int gz, CancellationToken ct)
    {
        var uid = this.CurrentUserId();
        var s = uid is null ? null : _upload.GetSession(uploadId, uid.Value);
        if (s == null) return NotFound(new { error = "Upload not found or expired — please start again." });
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        try
        {
            await _upload.WriteChunkAsync(s, seq, ms.ToArray(), gz == 1, ct);
            return Ok(new { ok = true });
        }
        catch (SqlException ex) { return StatusCode(500, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("upload/{uploadId}/complete")]
    public IActionResult UploadComplete(string uploadId)
    {
        var uid = this.CurrentUserId();
        var s = uid is null ? null : _upload.GetSession(uploadId, uid.Value);
        if (s == null) return NotFound(new { error = "Upload not found or expired — please start again." });
        try { return Ok(new { operationId = _upload.StartComplete(s) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("upload/{uploadId}")]
    public async Task<IActionResult> UploadCancel(string uploadId)
    {
        var uid = this.CurrentUserId();
        var s = uid is null ? null : _upload.GetSession(uploadId, uid.Value);
        if (s != null) { try { await _upload.CancelAsync(s); } catch { /* best-effort */ } }
        return Ok(new { ok = true });
    }

    public sealed class RestoreRequest
    {
        public string? DatabaseName { get; set; }
        /// <summary>Overwrite the existing database of that name (otherwise the name must be free).</summary>
        public bool Replace { get; set; }
        /// <summary>Replace only: back up the current database first (default on).</summary>
        public bool SafetyBackup { get; set; } = true;
    }

    /// <summary>Restore the uploaded .bak as a new database, or over an existing one (with an optional safety backup first).</summary>
    [HttpPost("upload/{uploadId}/restore")]
    public async Task<IActionResult> UploadRestore(string uploadId, [FromBody] RestoreRequest? req)
    {
        if (!await HasPermissionAsync(UploadRestorePermission)) return NoUploadPermission();
        var uid = this.CurrentUserId();
        var s = uid is null ? null : _upload.GetSession(uploadId, uid.Value);
        if (s == null || !s.Completed) return NotFound(new { error = "Uploaded file not found — please upload it again." });
        var name = req?.DatabaseName?.Trim();
        if (!BackupUploadService.IsValidDbName(name))
            return BadRequest(new { error = "Database name can only contain letters, numbers, _ and - (max 100)." });
        return Ok(new { operationId = _upload.StartRestore(s, name!, req!.Replace, req.SafetyBackup) });
    }

    /// <summary>Poll an upload-finish or restore operation.</summary>
    [HttpGet("upload/op/{operationId}")]
    public IActionResult UploadOp(string operationId)
    {
        var op = _upload.GetOp(operationId);
        if (op == null) return NotFound(new { error = "Unknown or expired operation." });
        return Ok(new { stage = op.Stage, percent = op.Percent, isComplete = op.IsComplete, success = op.Success, error = op.Error, result = op.Result });
    }

    private sealed record DriveInfoDto(string Drive, string? Label, long? TotalMB, long FreeMB);

    // "D:\Data\x.mdf" -> "D:"; mount points / UNC paths -> null.
    private static string? DriveOf(string? path)
        => path != null && path.Length >= 2 && path[1] == ':' ? char.ToUpperInvariant(path[0]) + ":" : null;

    private static string SqlVersionName(string? productVersion)
    {
        var parts = (productVersion ?? "").Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major)) return "SQL Server";
        return major switch
        {
            17 => "SQL Server 2025",
            16 => "SQL Server 2022",
            15 => "SQL Server 2019",
            14 => "SQL Server 2017",
            13 => "SQL Server 2016",
            12 => "SQL Server 2014",
            11 => "SQL Server 2012",
            10 => parts[1] == "50" ? "SQL Server 2008 R2" : "SQL Server 2008",
            _ => $"SQL Server ({productVersion})",
        };
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
