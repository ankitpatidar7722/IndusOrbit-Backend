using Backend.Attributes;
using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseBackupRestoreController : ControllerBase
    {
        private readonly IDatabaseBackupRestoreService _service;

        public DatabaseBackupRestoreController(IDatabaseBackupRestoreService service)
        {
            _service = service;
        }

        /// <summary>
        /// Initiates a backup and transfer operation (potentially cross-server)
        /// Returns immediately with an operation ID for status polling
        /// Requires user authentication
        /// </summary>
        [HttpPost("backup-and-transfer")]
        [Authorize]
        public async Task<IActionResult> BackupAndTransfer([FromBody] BackupAndTransferRequest request)
        {
            try
            {
                var result = await _service.BackupAndTransferAsync(request);

                if (!result.Success)
                    return BadRequest(new { error = result.Message });

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestoreController] BackupAndTransfer error: {ex.Message}");
                return StatusCode(500, new { error = $"Operation failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Receives a backup file stream from another server (server-to-server endpoint)
        /// Requires API key authentication (not user JWT)
        /// </summary>
        [HttpPost("receive-backup")]
        [ValidateBackupApiKey]
        public async Task<IActionResult> ReceiveBackup([FromQuery] string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return BadRequest(new { error = "fileName query parameter is required" });

                // Validate file extension
                if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Only .bak files are supported" });

                var savedFilePath = await _service.ReceiveBackupAsync(Request.Body, fileName);

                return Ok(new
                {
                    Success = true,
                    Message = "Backup file received successfully",
                    FilePath = savedFilePath
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestoreController] ReceiveBackup error: {ex.Message}");
                return StatusCode(500, new { error = $"Failed to receive backup file: {ex.Message}" });
            }
        }

        /// <summary>
        /// Restores a database from a backup file
        /// Can be called by source server (cross-server) or directly by frontend (same-server)
        /// Requires API key authentication for server-to-server calls
        /// </summary>
        [HttpPost("restore")]
        [ValidateBackupApiKey] // Allows both API key and JWT authentication
        public async Task<IActionResult> Restore([FromBody] RestoreRequest request)
        {
            try
            {
                var result = await _service.RestoreAsync(request);

                if (!result.Success)
                    return BadRequest(new { error = result.Message });

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestoreController] Restore error: {ex.Message}");
                return StatusCode(500, new { error = $"Restore failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Gets the current status of a long-running operation
        /// Used for polling by the frontend
        /// Requires user authentication
        /// </summary>
        [HttpGet("status/{operationId}")]
        [Authorize]
        public async Task<IActionResult> GetStatus(string operationId)
        {
            try
            {
                var status = await _service.GetOperationStatusAsync(operationId);

                if (status == null)
                    return NotFound(new { message = $"Operation '{operationId}' not found" });

                return Ok(status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestoreController] GetStatus error: {ex.Message}");
                return StatusCode(500, new { error = $"Failed to get status: {ex.Message}" });
            }
        }

        /// <summary>
        /// Creates a compressed backup and downloads it to the browser
        /// Creates .bak file, compresses to .zip, streams to browser, then cleans up
        /// Requires user authentication
        /// </summary>
        [HttpGet("download-backup")]
        [Authorize]
        public async Task<IActionResult> DownloadBackup(
            [FromQuery] string server,
            [FromQuery] string databaseName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(server))
                    return BadRequest(new { error = "Server parameter is required" });

                if (string.IsNullOrWhiteSpace(databaseName))
                    return BadRequest(new { error = "Database name parameter is required" });

                // Build connection string
                var connectionString = $"Data Source={server};Initial Catalog=master;User ID=indus;Password=Param@99811;TrustServerCertificate=True";

                Console.WriteLine($"[BackupRestoreController] Download backup requested: {databaseName} from {server}");

                // Create compressed backup
                var zipPath = await _service.CreateCompressedBackupAsync(connectionString, databaseName);

                // Stream file to browser
                var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var fileName = Path.GetFileName(zipPath);

                // After streaming completes, delete the file
                Response.RegisterForDispose(fileStream);
                Response.OnCompleted(async () =>
                {
                    try
                    {
                        await Task.Delay(1000); // Small delay to ensure stream is closed
                        if (System.IO.File.Exists(zipPath))
                        {
                            System.IO.File.Delete(zipPath);
                            Console.WriteLine($"[BackupRestoreController] Cleaned up: {zipPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BackupRestoreController] Cleanup warning: {ex.Message}");
                    }
                });

                return File(fileStream, "application/zip", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupRestoreController] DownloadBackup error: {ex.Message}");
                return StatusCode(500, new { error = $"Failed to create backup: {ex.Message}" });
            }
        }

        /// <summary>
        /// Health check endpoint for testing connectivity
        /// No authentication required
        /// </summary>
        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult Health()
        {
            return Ok(new
            {
                Status = "Healthy",
                Service = "Database Backup & Restore",
                Timestamp = DateTime.Now
            });
        }
    }
}
