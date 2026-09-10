using Backend.DTOs;

namespace Backend.Services
{
    public interface IDatabaseBackupRestoreService
    {
        /// <summary>
        /// Initiates a backup and transfer operation (potentially cross-server via HTTP)
        /// Returns immediately with an operation ID for status polling
        /// </summary>
        Task<BackupAndTransferResponse> BackupAndTransferAsync(
            BackupAndTransferRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives a backup file stream from another server (server-to-server endpoint)
        /// Validates API key and saves the file to RestoreStoragePath
        /// </summary>
        Task<string> ReceiveBackupAsync(
            Stream fileStream,
            string fileName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores a database from a backup file
        /// Executes RESTORE FILELISTONLY and RESTORE DATABASE WITH MOVE
        /// </summary>
        Task<RestoreResponse> RestoreAsync(
            RestoreRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current status of a long-running operation
        /// Used for polling by the frontend
        /// </summary>
        Task<OperationStatusResponse?> GetOperationStatusAsync(string operationId);

        /// <summary>
        /// Creates a backup of the database, compresses it to ZIP, and returns the file path
        /// Used for downloading backups to local system
        /// </summary>
        Task<string> CreateCompressedBackupAsync(
            string connectionString,
            string databaseName,
            CancellationToken cancellationToken = default);
    }
}
