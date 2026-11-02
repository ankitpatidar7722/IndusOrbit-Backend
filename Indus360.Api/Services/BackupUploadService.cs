using System.Collections.Concurrent;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Indus360.Api.Repositories;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Services;

/// <summary>
/// "Database Backup → By Server → Upload .bak": put a .bak from the user's machine into a SQL server's
/// backup folder, optionally RESTORE it as a new database. Registered as a singleton (sessions + ops
/// live in memory; an app-pool recycle simply abandons in-flight uploads).
///
/// The backend is not on the SQL box, so the file travels through the SQL connection (same idea as the
/// provisioning cross-server transfer): the browser sends gzip chunks → each chunk is written as its own
/// part file on the server via OLE Automation (DECOMPRESS + ADODB.Stream, one small batch per chunk, so
/// chunks can arrive in parallel and memory stays small) → on completion the parts are joined in order
/// (PowerShell via xp_cmdshell when enabled, else ADODB.Stream) and the result is verified with
/// RESTORE HEADERONLY / FILELISTONLY.
/// </summary>
public sealed class BackupUploadService
{
    public const int ChunkBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan SessionIdle = TimeSpan.FromHours(2);

    private readonly IConfiguration _cfg;
    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new();
    private readonly ConcurrentDictionary<string, OpStatus> _ops = new();

    public BackupUploadService(IConfiguration cfg) => _cfg = cfg;

    public sealed class UploadSession
    {
        public required string Id { get; init; }
        public required int UserId { get; init; }
        public required string ConnStr { get; init; }
        public required string Server { get; init; }
        public required string Folder { get; init; }
        public required string FileName { get; init; }
        public required long Size { get; init; }
        public required int Parts { get; init; }
        public required bool CanDecompress { get; init; }
        public ConcurrentDictionary<int, bool> Received { get; } = new();
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public bool Completed { get; set; }
        public string FullPath => Folder + FileName;
        public string PartPath(int i) => $"{FullPath}.up{i:D5}";
    }

    public sealed class OpStatus
    {
        public string Stage { get; set; } = "Starting";
        public int Percent { get; set; }
        public bool IsComplete { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public object? Result { get; set; }
    }

    public OpStatus? GetOp(string id) => _ops.TryGetValue(id, out var s) ? s : null;

    public UploadSession? GetSession(string id, int userId)
    {
        PurgeIdle();
        return _sessions.TryGetValue(id, out var s) && s.UserId == userId ? s : null;
    }

    // ── 1. Target folder + free space (shown before the user picks a file) ─────────────
    public async Task<(string Folder, long? FreeMB)> GetTargetAsync(SqlConnection conn, string server)
    {
        var folder = await ResolveBackupFolderAsync(conn, server);
        return (folder, await FreeMBAsync(conn, folder));
    }

    // ── 2. Start an upload ────────────────────────────────────────────────────────────
    public async Task<UploadSession> StartAsync(int userId, string connStr, string server, string fileName, long size)
    {
        var safe = SafeBakName(fileName);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var folder = await ResolveBackupFolderAsync(conn, server);
        var free = await FreeMBAsync(conn, folder);
        var needMB = size / 1048576 + 512;   // the file + a little headroom
        if (free is not null && free < needMB)
            throw new InvalidOperationException(
                $"Not enough space on {folder[..2]} — {Mb(size / 1048576)} needed, only {Mb(free.Value)} free.");

        await EnsureOleAsync(conn);

        // Never overwrite an existing file: add a timestamp when the name is taken.
        if (await FileExistsAsync(conn, folder + safe))
            safe = $"{Path.GetFileNameWithoutExtension(safe)}_{DateTime.Now:yyyyMMddHHmmss}.bak";

        var major = await conn.ExecuteScalarAsync<string>("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64))");
        var canDecompress = int.TryParse((major ?? "").Split('.')[0], out var m) && m >= 13;   // DECOMPRESS = SQL 2016+

        var s = new UploadSession
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ConnStr = connStr,
            Server = server,
            Folder = folder,
            FileName = safe,
            Size = size,
            Parts = (int)Math.Max(1, (size + ChunkBytes - 1) / ChunkBytes),
            CanDecompress = canDecompress,
        };
        _sessions[s.Id] = s;
        return s;
    }

    // ── 3. One chunk (gzip or raw) → its own part file on the server ───────────────────
    public async Task WriteChunkAsync(UploadSession s, int seq, byte[] body, bool gzipped, CancellationToken ct)
    {
        if (seq < 0 || seq >= s.Parts) throw new InvalidOperationException("Invalid chunk number.");
        if (s.Completed) throw new InvalidOperationException("This upload is already finished.");
        s.LastSeen = DateTime.UtcNow;

        // SQL 2014 and older have no DECOMPRESS → inflate here and send raw.
        if (gzipped && !s.CanDecompress)
        {
            using var src = new MemoryStream(body);
            using var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress);
            using var dst = new MemoryStream();
            await gz.CopyToAsync(dst, ct);
            body = dst.ToArray();
            gzipped = false;
        }

        var expected = seq < s.Parts - 1 ? ChunkBytes : s.Size - (long)ChunkBytes * (s.Parts - 1);
        await using var conn = new SqlConnection(s.ConnStr);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand($@"
            DECLARE @hr int, @o int, @b varbinary(max) = {(gzipped ? "DECOMPRESS(@d)" : "@d")};
            IF @b IS NULL OR DATALENGTH(@b) <> @len BEGIN RAISERROR('Chunk %d arrived damaged — it will be resent.', 16, 1, @seq); RETURN; END
            EXEC @hr = sp_OACreate 'ADODB.Stream', @o OUT;
            IF @hr <> 0 BEGIN RAISERROR('OLE create failed (0x%08X)', 16, 1, @hr); RETURN; END
            EXEC sp_OASetProperty @o, 'Type', 1;
            EXEC sp_OAMethod @o, 'Open';
            EXEC @hr = sp_OAMethod @o, 'Write', NULL, @b;
            IF @hr = 0 EXEC @hr = sp_OAMethod @o, 'SaveToFile', NULL, @path, 2;
            EXEC sp_OAMethod @o, 'Close';
            EXEC sp_OADestroy @o;
            IF @hr <> 0 RAISERROR('Could not write the file on the server (0x%08X)', 16, 1, @hr);", conn) { CommandTimeout = 300 };
        cmd.Parameters.Add("@d", SqlDbType.VarBinary, -1).Value = body;
        cmd.Parameters.Add("@len", SqlDbType.BigInt).Value = expected;
        cmd.Parameters.Add("@seq", SqlDbType.Int).Value = seq;
        cmd.Parameters.Add("@path", SqlDbType.NVarChar, 600).Value = s.PartPath(seq);
        await cmd.ExecuteNonQueryAsync(ct);
        s.Received[seq] = true;
    }

    // ── 4. Join the parts + verify (background op) ─────────────────────────────────────
    public string StartComplete(UploadSession s)
    {
        var missing = Enumerable.Range(0, s.Parts).Where(i => !s.Received.ContainsKey(i)).Take(5).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Upload is not complete yet (missing part {missing[0] + 1} of {s.Parts}).");
        s.Completed = true;

        var opId = Guid.NewGuid().ToString("N");
        var op = _ops[opId] = new OpStatus { Stage = "Joining file on the server", Percent = 5 };
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new SqlConnection(s.ConnStr);
                await conn.OpenAsync();
                var len = await JoinPartsAsync(conn, s);
                if (len != s.Size)
                    throw new InvalidOperationException($"The file on the server is incomplete ({len:N0} of {s.Size:N0} bytes).");

                op.Stage = "Checking the backup"; op.Percent = 80;
                op.Result = await ReadBackupInfoAsync(conn, s.FullPath, s.Server);
                op.Stage = "Complete"; op.Percent = 100; op.Success = true;
            }
            catch (Exception ex)
            {
                op.Error = ex is SqlException se && se.Message.Contains("RESTORE HEADERONLY", StringComparison.OrdinalIgnoreCase)
                    ? $"The file was uploaded but is not a valid SQL Server backup: {se.Message}"
                    : ex.Message;
                try { await DeletePartsAsync(s); } catch { /* best-effort */ }
            }
            finally { op.IsComplete = true; }
        });
        return opId;
    }

    // ── 5. Cancel: remove the parts written so far ────────────────────────────────────
    public async Task CancelAsync(UploadSession s)
    {
        _sessions.TryRemove(s.Id, out _);
        if (!s.Completed) await DeletePartsAsync(s);
    }

    // ── 6. RESTORE the uploaded .bak — as a NEW database, or over an EXISTING one (background op) ──
    private static readonly string[] SystemDbs = ["master", "model", "msdb", "tempdb", "distribution"];

    /// <param name="replace">false = new database (name must be free); true = overwrite the existing database of that name.</param>
    /// <param name="safetyBackup">replace only: take a COPY_ONLY backup of the existing database first.</param>
    public string StartRestore(UploadSession s, string dbName, bool replace, bool safetyBackup)
    {
        var opId = Guid.NewGuid().ToString("N");
        var op = _ops[opId] = new OpStatus { Stage = "Preparing restore", Percent = 2 };
        _ = Task.Run(async () =>
        {
            string? safetyPath = null;
            var singleUser = false;
            await using var conn = new SqlConnection(s.ConnStr);
            try
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("USE master");

                var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name = @n", new { n = dbName }) > 0;
                if (!replace && exists)
                    throw new InvalidOperationException($"A database named '{dbName}' already exists on this server — choose another name, or pick \"Replace existing database\".");
                if (replace && !exists)
                    throw new InvalidOperationException($"There is no database named '{dbName}' on this server to replace.");
                if (replace && SystemDbs.Contains(dbName, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"'{dbName}' is a system database and cannot be replaced.");

                var bak = s.FullPath.Replace("'", "''");
                var files = (await conn.QueryAsync($"RESTORE FILELISTONLY FROM DISK = N'{bak}'", commandTimeout: 300)).ToList();

                // Where the restored files go: the existing database's own files (replace) or the server's data folders (new).
                var existingFiles = replace
                    ? (await conn.QueryAsync<(string Type, string Path, long SizeMB)>(
                        "SELECT CASE WHEN type = 1 THEN 'L' ELSE 'D' END AS Type, physical_name AS Path, CAST(size AS bigint) * 8 / 1024 AS SizeMB " +
                        "FROM sys.master_files WHERE database_id = DB_ID(@n) AND type IN (0, 1) ORDER BY type, file_id", new { n = dbName })).ToList()
                    : [];
                string dataPath, logPath;
                if (replace && existingFiles.Count > 0)
                {
                    dataPath = FolderOf(existingFiles.FirstOrDefault(f => f.Type == "D").Path ?? existingFiles[0].Path);
                    logPath = FolderOf(existingFiles.FirstOrDefault(f => f.Type == "L").Path ?? dataPath);
                }
                else
                {
                    var defData = await conn.ExecuteScalarAsync<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(500))") ?? @"C:\SQLData\";
                    var defLog = await conn.ExecuteScalarAsync<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(500))") ?? defData;
                    var mapped = ProvisioningRepository.MappedFolder(_cfg, s.Server, backup: false);
                    dataPath = await ProvisioningRepository.ResolveFolderAsync(conn, mapped, defData);
                    logPath = await ProvisioningRepository.ResolveFolderAsync(conn, mapped, defLog);
                }

                // MOVE clause: when replacing, reuse the existing physical files in order; extra files get new names beside them.
                var move = new StringBuilder();
                var existingData = existingFiles.Where(f => f.Type == "D").Select(f => f.Path).ToList();
                var existingLog = existingFiles.Where(f => f.Type == "L").Select(f => f.Path).ToList();
                int dataIdx = 0, logIdx = 0;
                long dataBytes = 0, logBytes = 0;
                foreach (IDictionary<string, object> f in files)
                {
                    var logical = f["LogicalName"]?.ToString()?.Replace("'", "''");
                    var isLog = f["Type"]?.ToString() == "L";
                    var sz = Convert.ToInt64(f["Size"]);
                    string physical;
                    if (isLog)
                    {
                        physical = logIdx < existingLog.Count ? existingLog[logIdx] : $"{logPath}{dbName}_log{(logIdx == 0 ? "" : $"_{logIdx}")}.ldf";
                        logIdx++; logBytes += sz;
                    }
                    else
                    {
                        physical = dataIdx < existingData.Count ? existingData[dataIdx] : $"{dataPath}{dbName}{(dataIdx == 0 ? ".mdf" : $"_{dataIdx}.ndf")}";
                        dataIdx++; dataBytes += sz;
                    }
                    move.Append($", MOVE N'{logical}' TO N'{physical.Replace("'", "''")}'");
                }

                // Space check: restored files take their full allocated size; when replacing, the old files' space is reused.
                var drives = await FixedDrivesAsync(conn);
                var need = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                void Add(string path, long mb) { var d = path[..1]; need[d] = need.GetValueOrDefault(d) + mb; }
                Add(dataPath, dataBytes / 1048576); Add(logPath, logBytes / 1048576);
                foreach (var f in existingFiles) Add(f.Path, -f.SizeMB);
                var backupFolder = await ResolveBackupFolderAsync(conn, s.Server);
                if (replace && safetyBackup)
                    Add(backupFolder, existingFiles.Sum(f => f.SizeMB));   // worst case: an uncompressed full copy
                foreach (var (d, mb) in need)
                    if (mb > 0 && drives.TryGetValue(d, out var free) && free < mb + 512)
                        throw new InvalidOperationException($"Not enough space on {d}: — {Mb(mb)} needed, only {Mb(free)} free.");

                if (replace && safetyBackup)
                {
                    op.Stage = $"Safety backup of the current {dbName}"; op.Percent = 3;
                    safetyPath = $"{backupFolder}{dbName}_BeforeRestore_{DateTime.Now:yyyyMMddHHmmss}.bak";
                    await ExecWithProgressAsync(conn, s.ConnStr,
                        $"BACKUP DATABASE [{dbName}] TO DISK = N'{safetyPath.Replace("'", "''")}' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 5", op, 3, 40);
                }

                var from = replace && safetyBackup ? 42 : 5;
                if (replace)
                {
                    op.Stage = $"Closing connections to {dbName}"; op.Percent = from;
                    await conn.ExecuteAsync($"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", commandTimeout: 120);
                    singleUser = true;
                }

                op.Stage = replace ? $"Replacing {dbName}" : "Restoring database"; op.Percent = from;
                await ExecWithProgressAsync(conn, s.ConnStr,
                    $"RESTORE DATABASE [{dbName}] FROM DISK = N'{bak}' WITH {(replace ? "REPLACE, " : "")}RECOVERY, STATS = 5{move}", op, from, 98);

                if (singleUser)
                {
                    await conn.ExecuteAsync($"ALTER DATABASE [{dbName}] SET MULTI_USER", commandTimeout: 120);
                    singleUser = false;
                }

                var sizeMB = await conn.ExecuteScalarAsync<long>(
                    "SELECT ISNULL(SUM(CAST(size AS bigint)) * 8 / 1024, 0) FROM sys.master_files WHERE database_id = DB_ID(@n)", new { n = dbName });
                op.Result = new { databaseName = dbName, sizeMB, dataFolder = dataPath, logFolder = logPath, replaced = replace, safetyBackup = safetyPath };
                op.Stage = "Complete"; op.Percent = 100; op.Success = true;
            }
            catch (Exception ex)
            {
                op.Error = ex.Message + (safetyPath != null
                    ? $" The database as it was before is saved in {safetyPath}."
                    : "");
            }
            finally
            {
                // A failed replace must not leave the database locked to one connection.
                if (singleUser)
                    try { await conn.ExecuteAsync($"IF DB_ID(@n) IS NOT NULL ALTER DATABASE [{dbName}] SET MULTI_USER", new { n = dbName }, commandTimeout: 120); }
                    catch { /* database may be left in RESTORING state — the error message names the safety backup */ }
                op.IsComplete = true;
            }
        });
        return opId;
    }

    private static string FolderOf(string path) => path[..(path.LastIndexOf('\\') + 1)];

    public static bool IsValidDbName(string? name) => name != null && Regex.IsMatch(name, @"^[A-Za-z0-9_\-]{1,100}$");

    // ── helpers ───────────────────────────────────────────────────────────────────────

    private async Task<string> ResolveBackupFolderAsync(SqlConnection conn, string server)
    {
        var def = await conn.ExecuteScalarAsync<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(500))")
                  ?? await conn.ExecuteScalarAsync<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(500))")
                  ?? @"C:\Temp\";
        return await ProvisioningRepository.ResolveFolderAsync(conn, ProvisioningRepository.MappedFolder(_cfg, server, backup: true), def);
    }

    private static async Task<Dictionary<string, long>> FixedDrivesAsync(SqlConnection conn)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (IDictionary<string, object> row in await conn.QueryAsync("EXEC master.dbo.xp_fixeddrives"))
            {
                var v = row.Values.ToArray();
                map[v[0]?.ToString() ?? ""] = Convert.ToInt64(v[1]);
            }
        }
        catch (SqlException) { /* no permission → skip the space check */ }
        return map;
    }

    private static async Task<long?> FreeMBAsync(SqlConnection conn, string folder)
        => (await FixedDrivesAsync(conn)).TryGetValue(folder[..1], out var mb) ? mb : null;

    private static async Task EnsureOleAsync(SqlConnection conn)
    {
        var on = await conn.ExecuteScalarAsync<int>(
            "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'Ole Automation Procedures'");
        if (on == 1) return;
        try
        {
            // Same switch the client provisioning (Create Database) already turns on for cross-server copies.
            await conn.ExecuteAsync("EXEC sp_configure 'show advanced options', 1; RECONFIGURE; " +
                                    "EXEC sp_configure 'Ole Automation Procedures', 1; RECONFIGURE;", commandTimeout: 30);
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "This server does not allow writing files through SQL (OLE Automation is off and this login cannot turn it on). " +
                $"Please use a sysadmin login. Details: {ex.Message}");
        }
    }

    private static async Task<bool> FileExistsAsync(SqlConnection conn, string path)
        => await conn.ExecuteScalarAsync<int>("DECLARE @e int; EXEC master.dbo.xp_fileexist @p, @e OUTPUT; SELECT @e;", new { p = path }) == 1;

    private static async Task<bool> CmdShellOnAsync(SqlConnection conn)
        => await conn.ExecuteScalarAsync<int>("SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'xp_cmdshell'") == 1;

    /// <summary>Join part files in order into the final .bak and delete them. Returns the final length.</summary>
    private static async Task<long> JoinPartsAsync(SqlConnection conn, UploadSession s)
    {
        if (s.Parts == 1)
        {
            // Single part: just rename it.
            await RunOleFsoAsync(conn, "MoveFile", s.PartPath(0), s.FullPath);
            return await FileLengthAsync(conn, s.FullPath);
        }

        if (await CmdShellOnAsync(conn))
        {
            // Streamed join (no size limit, constant memory) via PowerShell.
            static string Q(string v) => "'" + v.Replace("'", "''") + "'";
            var script = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" +
                         $"$out=[IO.File]::Create({Q(s.FullPath)});" +
                         $"try{{for($i=0;$i -lt {s.Parts};$i++){{$p={Q(s.FullPath + ".up")}+$i.ToString('D5');" +
                         "$in=[IO.File]::OpenRead($p);try{$in.CopyTo($out,4MB)}finally{$in.Dispose()};Remove-Item -LiteralPath $p}}" +
                         "finally{$len=$out.Length;$out.Dispose()};'LEN='+$len";
            var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var lines = (await conn.QueryAsync<string?>(
                "EXEC master.dbo.xp_cmdshell @c", new { c = $"powershell -NoProfile -NonInteractive -EncodedCommand {enc}" },
                commandTimeout: 1800)).ToList();
            var m = lines.Select(l => Regex.Match(l ?? "", @"LEN=(\d+)")).FirstOrDefault(x => x.Success);
            if (m is null)
                throw new InvalidOperationException("Could not join the uploaded file on the server: " +
                                                    string.Join(" ", lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(5)));
            return long.Parse(m.Groups[1].Value);
        }

        // No xp_cmdshell → ADODB.Stream join in one batch (holds the file in the SQL process; fine for
        // normal sizes, may fail on very large files — the error says so).
        await using var cmd = new SqlCommand(@"
            DECLARE @hr int, @o int, @i int = 0, @len bigint, @part nvarchar(600), @sql nvarchar(max), @b varbinary(max);
            EXEC @hr = sp_OACreate 'ADODB.Stream', @o OUT;
            IF @hr <> 0 BEGIN RAISERROR('OLE create failed (0x%08X)', 16, 1, @hr); RETURN; END
            EXEC sp_OASetProperty @o, 'Type', 1;
            EXEC sp_OAMethod @o, 'Open';
            WHILE @i < @parts AND @hr = 0
            BEGIN
                SET @part = @base + RIGHT('0000' + CAST(@i AS varchar(10)), 5);
                SET @sql = N'SELECT @b = BulkColumn FROM OPENROWSET(BULK N''' + REPLACE(@part, '''', '''''') + N''', SINGLE_BLOB) AS x';
                EXEC sp_executesql @sql, N'@b varbinary(max) OUTPUT', @b OUTPUT;
                EXEC @hr = sp_OAMethod @o, 'Write', NULL, @b;
                SET @i += 1;
            END
            IF @hr = 0 EXEC @hr = sp_OAMethod @o, 'SaveToFile', NULL, @path, 2;
            EXEC sp_OAGetProperty @o, 'Size', @len OUT;
            EXEC sp_OAMethod @o, 'Close';
            EXEC sp_OADestroy @o;
            IF @hr <> 0 BEGIN RAISERROR('Could not join the uploaded file on the server (0x%08X). For very large files enable xp_cmdshell on this server.', 16, 1, @hr); RETURN; END
            SELECT @len;", conn) { CommandTimeout = 1800 };
        cmd.Parameters.AddWithValue("@parts", s.Parts);
        cmd.Parameters.AddWithValue("@base", s.FullPath + ".up");
        cmd.Parameters.AddWithValue("@path", s.FullPath);
        var len = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        await DeletePartsAsync(conn, s);
        return len;
    }

    private static async Task<long> FileLengthAsync(SqlConnection conn, string path)
    {
        await using var cmd = new SqlCommand(@"
            DECLARE @hr int, @fso int, @f int, @len bigint;
            EXEC @hr = sp_OACreate 'Scripting.FileSystemObject', @fso OUT;
            EXEC @hr = sp_OAMethod @fso, 'GetFile', @f OUT, @path;
            IF @hr = 0 EXEC sp_OAGetProperty @f, 'Size', @len OUT;
            IF @f IS NOT NULL EXEC sp_OADestroy @f;
            EXEC sp_OADestroy @fso;
            SELECT @len;", conn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@path", path);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? -1 : Convert.ToInt64(v);
    }

    private static async Task RunOleFsoAsync(SqlConnection conn, string method, string a, string? b = null)
    {
        await using var cmd = new SqlCommand($@"
            DECLARE @hr int, @fso int;
            EXEC @hr = sp_OACreate 'Scripting.FileSystemObject', @fso OUT;
            IF @hr <> 0 BEGIN RAISERROR('OLE create failed (0x%08X)', 16, 1, @hr); RETURN; END
            EXEC @hr = sp_OAMethod @fso, '{method}', NULL, @a{(b is null ? "" : ", @b")};
            EXEC sp_OADestroy @fso;
            IF @hr <> 0 RAISERROR('File operation failed on the server (0x%08X)', 16, 1, @hr);", conn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@a", a);
        if (b is not null) cmd.Parameters.AddWithValue("@b", b);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DeletePartsAsync(UploadSession s)
    {
        await using var conn = new SqlConnection(s.ConnStr);
        await conn.OpenAsync();
        await DeletePartsAsync(conn, s);
    }

    private static async Task DeletePartsAsync(SqlConnection conn, UploadSession s)
    {
        // FileSystemObject.DeleteFile accepts a wildcard in the file name; "not found" is fine.
        try { await RunOleFsoAsync(conn, "DeleteFile", s.FullPath + ".up*"); } catch { /* nothing left to delete */ }
    }

    private static async Task<object> ReadBackupInfoAsync(SqlConnection conn, string path, string server)
    {
        var p = path.Replace("'", "''");
        var head = (IDictionary<string, object>)(await conn.QueryAsync($"RESTORE HEADERONLY FROM DISK = N'{p}'", commandTimeout: 300)).First();
        var files = (await conn.QueryAsync($"RESTORE FILELISTONLY FROM DISK = N'{p}'", commandTimeout: 300)).ToList();
        long restoredBytes = files.Sum(f => Convert.ToInt64(((IDictionary<string, object>)f)["Size"]));
        object? Get(string k) => head.TryGetValue(k, out var v) && v is not DBNull ? v : null;
        return new
        {
            filePath = path,
            fileName = Path.GetFileName(path),
            databaseName = Get("DatabaseName")?.ToString(),
            sourceServer = Get("ServerName")?.ToString(),
            backupDate = Get("BackupFinishDate"),
            backupSizeMB = Convert.ToInt64(Get("BackupSize") ?? 0L) / 1048576,
            restoredSizeMB = restoredBytes / 1048576,
            compressed = Convert.ToInt32(Get("Compressed") ?? 0) == 1,
            server,
        };
    }

    /// <summary>Run RESTORE while polling percent_complete on a second connection.</summary>
    private static async Task ExecWithProgressAsync(SqlConnection conn, string connStr, string sql, OpStatus op, int from = 5, int to = 98)
    {
        var spid = await conn.ExecuteScalarAsync<int>("SELECT @@SPID");
        var exec = conn.ExecuteAsync(sql, commandTimeout: 0);
        try
        {
            await using var mon = new SqlConnection(connStr);
            await mon.OpenAsync();
            while (await Task.WhenAny(exec, Task.Delay(1500)) != exec)
            {
                try
                {
                    var pc = await mon.ExecuteScalarAsync<double?>(
                        "SELECT percent_complete FROM sys.dm_exec_requests WHERE session_id = @s", new { s = spid });
                    if (pc is > 0) op.Percent = Math.Clamp(from + (int)(pc.Value * (to - from) / 100), from, to);
                }
                catch { /* best-effort */ }
            }
        }
        catch { /* monitor unavailable — just wait */ }
        await exec;
    }

    private static string SafeBakName(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? ""));
        name = Regex.Replace(name, @"[^A-Za-z0-9_\-\.]", "_").Trim('.', '_');
        if (name.Length == 0) name = "upload";
        if (name.Length > 120) name = name[..120];
        return name + ".bak";
    }

    private static string Mb(long mb) => mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb} MB";

    private void PurgeIdle()
    {
        foreach (var (id, s) in _sessions)
            if (DateTime.UtcNow - s.LastSeen > SessionIdle && _sessions.TryRemove(id, out _) && !s.Completed)
                _ = Task.Run(async () => { try { await DeletePartsAsync(s); } catch { } });
        foreach (var (id, o) in _ops)
            if (o.IsComplete && _ops.Count > 200) _ops.TryRemove(id, out _);
    }
}
