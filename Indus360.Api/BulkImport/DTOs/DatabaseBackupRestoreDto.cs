namespace Backend.DTOs
{
    // ─── Configuration (maps to appsettings.json) ───
    public class BackupRestoreConfig
    {
        public string BackupStoragePath { get; set; } = @"C:\SQLBackups\";
        public string RestoreStoragePath { get; set; } = @"C:\SQLBackups\Incoming\";
        public string ApiKey { get; set; } = string.Empty;
        public int ChunkSizeBytes { get; set; } = 1048576; // 1MB default
        public int MaxBackupFileSizeGB { get; set; } = 100;
    }

    // ─── Backup and Transfer Requests/Responses ───
    public class BackupAndTransferRequest
    {
        public string SourceConnectionString { get; set; } = string.Empty;
        public string DestinationConnectionString { get; set; } = string.Empty;
        public string? DestinationServerUrl { get; set; } // null = same server
        public string DatabaseName { get; set; } = string.Empty;
        public string BackupDatabaseName { get; set; } = string.Empty;
        public bool AllowOverwrite { get; set; } = false;
        public string? ApiKey { get; set; } // For cross-server auth
    }

    public class BackupAndTransferResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
    }

    // ─── Restore Requests/Responses ───
    public class RestoreRequest
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string BackupFilePath { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public bool AllowOverwrite { get; set; } = false;
    }

    public class RestoreResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? DatabaseName { get; set; }
    }

    // ─── Operation Status (for polling) ───
    public class OperationStatusResponse
    {
        public string OperationId { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty; // "Backing up", "Transferring", "Restoring"
        public int PercentComplete { get; set; } // 0-100
        public string Message { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
