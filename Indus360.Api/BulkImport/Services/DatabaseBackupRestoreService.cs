using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace Backend.Services
{
    public class DatabaseBackupRestoreService : IDatabaseBackupRestoreService
    {
        private readonly BackupRestoreConfig _config;
        private readonly IActivityLogService _activityLog;
        private static readonly ConcurrentDictionary<string, OperationStatusResponse> _operationStatuses = new();
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        public DatabaseBackupRestoreService(
            IOptions<BackupRestoreConfig> config,
            IActivityLogService activityLog)
        {
            _config = config.Value;
            _activityLog = activityLog;
        }

        #region Public Methods

        public async Task<BackupAndTransferResponse> BackupAndTransferAsync(
            BackupAndTransferRequest request,
            CancellationToken cancellationToken = default)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
                return new BackupAndTransferResponse { Success = false, Message = "Source connection string is required." };
            if (string.IsNullOrWhiteSpace(request.DestinationConnectionString))
                return new BackupAndTransferResponse { Success = false, Message = "Destination connection string is required." };
            if (string.IsNullOrWhiteSpace(request.DatabaseName))
                return new BackupAndTransferResponse { Success = false, Message = "Database name is required." };
            if (string.IsNullOrWhiteSpace(request.BackupDatabaseName))
                return new BackupAndTransferResponse { Success = false, Message = "Backup database name is required." };

            var operationId = Guid.NewGuid().ToString();

            // Initialize operation status
            var status = new OperationStatusResponse
            {
                OperationId = operationId,
                Stage = "Initializing",
                PercentComplete = 0,
                Message = "Starting backup and transfer operation",
                IsComplete = false,
                Success = false,
                StartedAt = DateTime.Now
            };
            _operationStatuses[operationId] = status;

            // Start background task
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteBackupAndTransferAsync(request, operationId, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BackupRestore] Operation {operationId} failed: {ex.Message}");
                    UpdateOperationStatus(operationId, "Failed", 0, $"Error: {ex.Message}", true, false, ex.Message);
                }
            }, cancellationToken);

            return new BackupAndTransferResponse
            {
                Success = true,
                Message = "Backup and transfer operation started. Use the operation ID to check status.",
                OperationId = operationId
            };
        }

        public async Task<string> ReceiveBackupAsync(
            Stream fileStream,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure RestoreStoragePath directory exists
                if (!Directory.Exists(_config.RestoreStoragePath))
                    Directory.CreateDirectory(_config.RestoreStoragePath);

                var savePath = Path.Combine(_config.RestoreStoragePath, fileName);

                Console.WriteLine($"[BackupRestore] Receiving backup file: {fileName} → {savePath}");

                // Write stream to disk
                using (var fileWriteStream = File.Create(savePath))
                {
                    await fileStream.CopyToAsync(fileWriteStream, _config.ChunkSizeBytes, cancellationToken);
                }

                var fileInfo = new FileInfo(savePath);
                Console.WriteLine($"[BackupRestore] File received successfully. Size: {fileInfo.Length / 1024 / 1024} MB");

                return savePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestore] Receive backup error: {ex.Message}");
                throw;
            }
        }

        public async Task<RestoreResponse> RestoreAsync(
            RestoreRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(request.ConnectionString))
                    return new RestoreResponse { Success = false, Message = "Connection string is required." };
                if (string.IsNullOrWhiteSpace(request.BackupFilePath))
                    return new RestoreResponse { Success = false, Message = "Backup file path is required." };
                if (string.IsNullOrWhiteSpace(request.DatabaseName))
                    return new RestoreResponse { Success = false, Message = "Database name is required." };

                // Validate backup file exists
                if (!File.Exists(request.BackupFilePath))
                    return new RestoreResponse { Success = false, Message = $"Backup file not found: {request.BackupFilePath}" };

                Console.WriteLine($"[BackupRestore] Starting restore: {request.DatabaseName} from {request.BackupFilePath}");

                using var conn = new SqlConnection(request.ConnectionString);
                await conn.OpenAsync(cancellationToken);

                // Check if database already exists
                var exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sys.databases WHERE name = @Name",
                    new { Name = request.DatabaseName });

                if (exists > 0 && !request.AllowOverwrite)
                {
                    return new RestoreResponse
                    {
                        Success = false,
                        Message = $"Database '{request.DatabaseName}' already exists. Set AllowOverwrite=true to replace it."
                    };
                }

                // Get logical file names from backup
                var files = (await conn.QueryAsync(
                    $"RESTORE FILELISTONLY FROM DISK = N'{request.BackupFilePath}'",
                    commandTimeout: 120)).ToList();

                if (!files.Any())
                {
                    return new RestoreResponse
                    {
                        Success = false,
                        Message = "Backup file is invalid or empty. Cannot retrieve file list."
                    };
                }

                // Get default data/log paths
                var dataPath = await conn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))") ?? @"C:\SQLData\";
                var logPath = await conn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(500))") ?? dataPath;

                if (!dataPath.EndsWith(@"\")) dataPath += @"\";
                if (!logPath.EndsWith(@"\")) logPath += @"\";

                // Build MOVE clauses (reuse logic from CompanySubscriptionService.cs)
                var moveClause = new StringBuilder();
                int dataFileIndex = 0;
                foreach (var file in files)
                {
                    var logicalName = file.LogicalName?.ToString();
                    var type = file.Type?.ToString();

                    if (string.IsNullOrEmpty(logicalName)) continue;

                    string physicalPath;
                    if (type == "L") // Log file
                    {
                        physicalPath = $"{logPath}{request.DatabaseName}_log.ldf";
                    }
                    else // Data file
                    {
                        var suffix = dataFileIndex == 0 ? ".mdf" : $"_{dataFileIndex}.ndf";
                        physicalPath = $"{dataPath}{request.DatabaseName}{suffix}";
                        dataFileIndex++;
                    }

                    moveClause.Append($", MOVE N'{logicalName}' TO N'{physicalPath}'");
                }

                // Execute RESTORE DATABASE
                var restoreSql = $"RESTORE DATABASE [{request.DatabaseName}] FROM DISK = N'{request.BackupFilePath}' " +
                                 $"WITH REPLACE{moveClause}, STATS = 10, RECOVERY";

                Console.WriteLine($"[BackupRestore] Executing restore command for database '{request.DatabaseName}'");

                await conn.ExecuteAsync(restoreSql, commandTimeout: 600);

                Console.WriteLine($"[BackupRestore] Restore complete: {request.DatabaseName}");

                // Log activity
                await _activityLog.LogActivityAsync(new CreateActivityLogRequest
                {
                    ActionType = "Restore Database",
                    ActionDescription = $"Restored database '{request.DatabaseName}' from backup file '{Path.GetFileName(request.BackupFilePath)}'",
                    EntityName = "Database",
                    NewValue = request.DatabaseName
                });

                return new RestoreResponse
                {
                    Success = true,
                    Message = $"Database '{request.DatabaseName}' restored successfully.",
                    DatabaseName = request.DatabaseName
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestore] Restore error: {ex.Message}");
                return new RestoreResponse
                {
                    Success = false,
                    Message = $"Restore failed: {ex.Message}"
                };
            }
        }

        public Task<OperationStatusResponse?> GetOperationStatusAsync(string operationId)
        {
            if (_operationStatuses.TryGetValue(operationId, out var status))
                return Task.FromResult<OperationStatusResponse?>(status);

            return Task.FromResult<OperationStatusResponse?>(null);
        }

        public async Task<string> CreateCompressedBackupAsync(
            string connectionString,
            string databaseName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Console.WriteLine($"[BackupRestore] Creating compressed backup for database '{databaseName}'");

                // Ensure backup directory exists
                if (!Directory.Exists(_config.BackupStoragePath))
                {
                    Console.WriteLine($"[BackupRestore] Creating backup directory: {_config.BackupStoragePath}");
                    Directory.CreateDirectory(_config.BackupStoragePath);
                }
                else
                {
                    Console.WriteLine($"[BackupRestore] Backup directory exists: {_config.BackupStoragePath}");
                }

                // Create backup file
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(cancellationToken);

                Console.WriteLine($"[BackupRestore] Connected to SQL Server");

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var backupFileName = $"{databaseName}_{timestamp}.bak";

                // Use local backup path (should be UNC path for remote servers)
                var backupPath = Path.Combine(_config.BackupStoragePath, backupFileName);

                Console.WriteLine($"[BackupRestore] Backup path: {backupPath}");

                Console.WriteLine($"[BackupRestore] Creating backup: {backupPath}");
                Console.WriteLine($"[BackupRestore] Executing BACKUP DATABASE command...");

                var backupSql = $@"
                    BACKUP DATABASE [{databaseName}]
                    TO DISK = N'{backupPath}'
                    WITH
                        COPY_ONLY,
                        FORMAT,
                        INIT,
                        SKIP,
                        NOUNLOAD,
                        STATS = 10,
                        CHECKSUM";

                await conn.ExecuteAsync(backupSql, commandTimeout: 600);

                Console.WriteLine($"[BackupRestore] Backup created successfully");

                // Verify backup file exists and get its size
                if (!File.Exists(backupPath))
                {
                    throw new Exception($"Backup file was not created: {backupPath}");
                }

                var backupFileInfo = new FileInfo(backupPath);
                Console.WriteLine($"[BackupRestore] Backup file size: {backupFileInfo.Length / 1024 / 1024} MB");

                if (backupFileInfo.Length == 0)
                {
                    throw new Exception($"Backup file is empty: {backupPath}");
                }

                // Compress to ZIP
                var zipFileName = $"{databaseName}_{timestamp}.zip";
                var zipPath = Path.Combine(_config.BackupStoragePath, zipFileName);

                Console.WriteLine($"[BackupRestore] Compressing backup to: {zipPath}");

                using (var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zipArchive.CreateEntryFromFile(backupPath, backupFileName, CompressionLevel.Optimal);
                }

                // Verify ZIP file was created and contains the entry
                if (!File.Exists(zipPath))
                {
                    throw new Exception($"ZIP file was not created: {zipPath}");
                }

                var zipFileInfo = new FileInfo(zipPath);
                Console.WriteLine($"[BackupRestore] Compression complete. ZIP size: {zipFileInfo.Length / 1024 / 1024} MB");

                // Delete original .bak file to save space
                try
                {
                    File.Delete(backupPath);
                    Console.WriteLine($"[BackupRestore] Deleted temporary .bak file");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BackupRestore] Warning: Could not delete .bak file: {ex.Message}");
                }

                // Log activity
                await _activityLog.LogActivityAsync(new CreateActivityLogRequest
                {
                    ActionType = "Download Compressed Backup",
                    ActionDescription = $"Created compressed backup for database '{databaseName}' ({Path.GetFileName(zipPath)})",
                    EntityName = "Database",
                    NewValue = databaseName
                });

                return zipPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestore] CreateCompressedBackup error: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Private Methods

        private async Task ExecuteBackupAndTransferAsync(
            BackupAndTransferRequest request,
            string operationId,
            CancellationToken cancellationToken)
        {
            string? backupFilePath = null;
            string? receivedFilePath = null;

            try
            {
                // Step 1: Backup source database
                UpdateOperationStatus(operationId, "Backing up database", 10, $"Creating backup of '{request.BackupDatabaseName}'");

                backupFilePath = await ExecuteBackupAsync(
                    request.SourceConnectionString,
                    request.BackupDatabaseName,
                    request.DatabaseName,
                    cancellationToken);

                var fileInfo = new FileInfo(backupFilePath);
                var fileSizeBytes = fileInfo.Length;
                var fileSizeMB = fileSizeBytes / 1024.0 / 1024.0;

                UpdateOperationStatus(operationId, "Backup complete", 30,
                    $"Backup created: {Path.GetFileName(backupFilePath)} ({fileSizeMB:F2} MB)",
                    totalBytes: fileSizeBytes);

                // Step 2: Transfer file (if cross-server)
                var isCrossServer = !string.IsNullOrWhiteSpace(request.DestinationServerUrl);

                if (isCrossServer)
                {
                    UpdateOperationStatus(operationId, "Transferring file", 35,
                        $"Transferring {fileSizeMB:F2} MB to destination server");

                    receivedFilePath = await StreamToDestinationAsync(
                        backupFilePath,
                        request.DestinationServerUrl!,
                        request.ApiKey ?? _config.ApiKey,
                        operationId,
                        cancellationToken);

                    UpdateOperationStatus(operationId, "Transfer complete", 65,
                        $"File transferred successfully ({fileSizeMB:F2} MB)");
                }
                else
                {
                    // Same server - use local backup file path
                    receivedFilePath = backupFilePath;
                    UpdateOperationStatus(operationId, "Using local backup", 65,
                        "Same-server restore, using local backup file");
                }

                // Step 3: Restore on destination
                UpdateOperationStatus(operationId, "Restoring database", 70,
                    $"Restoring database '{request.DatabaseName}'");

                var restoreRequest = new RestoreRequest
                {
                    ConnectionString = request.DestinationConnectionString,
                    BackupFilePath = receivedFilePath,
                    DatabaseName = request.DatabaseName,
                    AllowOverwrite = request.AllowOverwrite
                };

                RestoreResponse? restoreResponse;
                if (isCrossServer)
                {
                    // Call destination server's restore endpoint
                    restoreResponse = await CallRemoteRestoreAsync(
                        request.DestinationServerUrl!,
                        restoreRequest,
                        request.ApiKey ?? _config.ApiKey,
                        cancellationToken);
                }
                else
                {
                    // Local restore
                    restoreResponse = await RestoreAsync(restoreRequest, cancellationToken);
                }

                if (restoreResponse == null || !restoreResponse.Success)
                {
                    throw new Exception(restoreResponse?.Message ?? "Restore failed with no error message");
                }

                UpdateOperationStatus(operationId, "Restore complete", 90, restoreResponse.Message);

                // Step 4: Cleanup backup files
                UpdateOperationStatus(operationId, "Cleaning up", 95, "Removing temporary backup files");

                await CleanupBackupFileAsync(backupFilePath, request.SourceConnectionString);

                if (isCrossServer && !string.IsNullOrEmpty(receivedFilePath))
                {
                    // Cleanup on destination server (call cleanup endpoint or delete directly if accessible)
                    try
                    {
                        await CallRemoteCleanupAsync(
                            request.DestinationServerUrl!,
                            receivedFilePath,
                            request.ApiKey ?? _config.ApiKey,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BackupRestore] Cleanup warning (non-critical): {ex.Message}");
                    }
                }

                // Complete
                UpdateOperationStatus(operationId, "Complete", 100,
                    $"Database '{request.DatabaseName}' successfully created and restored",
                    isComplete: true, success: true);

                // Log activity
                await _activityLog.LogActivityAsync(new CreateActivityLogRequest
                {
                    ActionType = "Backup and Restore Database",
                    ActionDescription = $"Completed backup and restore operation for database '{request.DatabaseName}' " +
                                       (isCrossServer ? $"from source to destination server via HTTP" : "on same server"),
                    EntityName = "Database",
                    NewValue = $"{request.BackupDatabaseName} → {request.DatabaseName}"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestore] Operation {operationId} error: {ex.Message}\n{ex.StackTrace}");

                // Cleanup on error
                if (!string.IsNullOrEmpty(backupFilePath) && File.Exists(backupFilePath))
                {
                    try
                    {
                        await CleanupBackupFileAsync(backupFilePath, request.SourceConnectionString);
                    }
                    catch { /* Ignore cleanup errors */ }
                }

                UpdateOperationStatus(operationId, "Failed", 0,
                    $"Operation failed: {ex.Message}", isComplete: true, success: false, error: ex.Message);
            }
        }

        private async Task<string> ExecuteBackupAsync(
            string connectionString,
            string sourceDatabaseName,
            string newDatabaseName,
            CancellationToken cancellationToken)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            // Ensure BackupStoragePath directory exists
            if (!Directory.Exists(_config.BackupStoragePath))
                Directory.CreateDirectory(_config.BackupStoragePath);

            // Get default backup directory (fallback to config path)
            var defaultBackupPath = await conn.ExecuteScalarAsync<string>(
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(500))");

            if (string.IsNullOrEmpty(defaultBackupPath))
            {
                var defaultDataPath = await conn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))");
                defaultBackupPath = defaultDataPath ?? _config.BackupStoragePath;
            }

            if (!defaultBackupPath.EndsWith(@"\")) defaultBackupPath += @"\";

            var backupFileName = $"{newDatabaseName}_{DateTime.Now:yyyyMMddHHmmss}.bak";
            var backupPath = Path.Combine(defaultBackupPath, backupFileName);

            Console.WriteLine($"[BackupRestore] Backing up database '{sourceDatabaseName}' to {backupPath}");

            // Execute BACKUP DATABASE command
            // Note: COMPRESSION removed for compatibility with Web Edition
            var backupSql = $@"
                BACKUP DATABASE [{sourceDatabaseName}]
                TO DISK = N'{backupPath}'
                WITH
                    COPY_ONLY,
                    FORMAT,
                    INIT,
                    SKIP,
                    NOUNLOAD,
                    STATS = 10,
                    CHECKSUM";

            await conn.ExecuteAsync(backupSql, commandTimeout: 600);

            Console.WriteLine($"[BackupRestore] Backup complete: {backupPath}");

            return backupPath;
        }

        private async Task<string> StreamToDestinationAsync(
            string backupFilePath,
            string destinationServerUrl,
            string apiKey,
            string operationId,
            CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(backupFilePath);
            var fileInfo = new FileInfo(backupFilePath);
            var totalBytes = fileInfo.Length;

            Console.WriteLine($"[BackupRestore] Streaming file to destination: {destinationServerUrl}");
            Console.WriteLine($"[BackupRestore] File: {fileName}, Size: {totalBytes / 1024.0 / 1024.0:F2} MB");

            using var fileStream = File.OpenRead(backupFilePath);

            // Create progress-tracking stream wrapper
            var progressStream = new ProgressStream(fileStream, totalBytes, (bytesRead) =>
            {
                var percent = 35 + (int)((bytesRead / (double)totalBytes) * 30); // 35-65%
                var mbRead = bytesRead / 1024.0 / 1024.0;
                var mbTotal = totalBytes / 1024.0 / 1024.0;
                UpdateOperationStatus(operationId, "Transferring file", percent,
                    $"Transferred {mbRead:F2} MB / {mbTotal:F2} MB",
                    bytesTransferred: bytesRead, totalBytes: totalBytes);
            });

            using var content = new StreamContent(progressStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var url = $"{destinationServerUrl.TrimEnd('/')}/api/DatabaseBackupRestore/receive-backup?fileName={Uri.EscapeDataString(fileName)}";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            request.Headers.Add("X-Backup-Restore-ApiKey", apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to transfer backup file. Status: {response.StatusCode}, Error: {errorContent}");
            }

            var resultJson = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[BackupRestore] Transfer complete: {fileName}");

            // The destination server returns the saved file path
            // For simplicity, we'll return the expected path
            return Path.Combine(_config.RestoreStoragePath, fileName);
        }

        private async Task<RestoreResponse?> CallRemoteRestoreAsync(
            string destinationServerUrl,
            RestoreRequest restoreRequest,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var url = $"{destinationServerUrl.TrimEnd('/')}/api/DatabaseBackupRestore/restore";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(restoreRequest)
            };
            request.Headers.Add("X-Backup-Restore-ApiKey", apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Remote restore failed. Status: {response.StatusCode}, Error: {errorContent}");
            }

            return await response.Content.ReadFromJsonAsync<RestoreResponse>();
        }

        private async Task CallRemoteCleanupAsync(
            string destinationServerUrl,
            string backupFilePath,
            string apiKey,
            CancellationToken cancellationToken)
        {
            // In a full implementation, we'd have a cleanup endpoint on the destination
            // For now, we'll skip this as the destination can clean up its own temp files
            await Task.CompletedTask;
        }

        private async Task CleanupBackupFileAsync(string backupFilePath, string connectionString)
        {
            try
            {
                Console.WriteLine($"[BackupRestore] Cleaning up backup file: {backupFilePath}");

                // Try direct file delete first
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                    Console.WriteLine($"[BackupRestore] Backup file deleted: {backupFilePath}");
                }
                else
                {
                    // Fallback: use xp_cmdshell (matches CompanySubscriptionService pattern)
                    using var conn = new SqlConnection(connectionString);
                    await conn.OpenAsync();
                    await conn.ExecuteAsync(
                        $"EXEC master.dbo.xp_cmdshell 'del \"{backupFilePath}\"'",
                        commandTimeout: 30);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestore] Cleanup warning (non-critical): {ex.Message}");
            }
        }

        private void UpdateOperationStatus(
            string operationId,
            string stage,
            int percent,
            string message,
            bool isComplete = false,
            bool success = false,
            string? error = null,
            long bytesTransferred = 0,
            long totalBytes = 0)
        {
            if (_operationStatuses.TryGetValue(operationId, out var status))
            {
                status.Stage = stage;
                status.PercentComplete = percent;
                status.Message = message;
                status.IsComplete = isComplete;
                status.Success = success;
                status.Error = error;
                status.BytesTransferred = bytesTransferred > 0 ? bytesTransferred : status.BytesTransferred;
                status.TotalBytes = totalBytes > 0 ? totalBytes : status.TotalBytes;

                if (isComplete)
                    status.CompletedAt = DateTime.Now;

                Console.WriteLine($"[BackupRestore] [{operationId}] {stage} - {percent}% - {message}");
            }
        }

        #endregion
    }

    #region Helper Class

    /// <summary>
    /// Wrapper stream that reports read progress for file transfers
    /// </summary>
    internal class ProgressStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private readonly Action<long> _onProgress;
        private long _bytesRead;

        public ProgressStream(Stream baseStream, long totalBytes, Action<long> onProgress)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _onProgress = onProgress;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _baseStream.Read(buffer, offset, count);
            _bytesRead += bytesRead;
            _onProgress(_bytesRead);
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesRead += bytesRead;
            _onProgress(_bytesRead);
            return bytesRead;
        }

        public override void Flush() => _baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
