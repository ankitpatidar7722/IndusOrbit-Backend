using Microsoft.Data.SqlClient;
using Dapper;
using Backend.DTOs;
using System.Text;

namespace Backend.Services;

public class CompanySubscriptionService : ICompanySubscriptionService
{
    private readonly IConfiguration _config;
    private readonly IActivityLogService _activityLogService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CompanySubscriptionService(
        IConfiguration config,
        IActivityLogService activityLogService,
        IHttpContextAccessor httpContextAccessor)
    {
        _config = config;
        _activityLogService = activityLogService;
        _httpContextAccessor = httpContextAccessor;
    }

    private SqlConnection GetIndusConnection()
    {
        var connString = _config.GetConnectionString("IndusConnection");
        return new SqlConnection(connString);
    }

    /// <summary>
    /// Helper method to log activity
    /// </summary>
    private async Task LogActivityAsync(
        string actionType,
        string actionDescription,
        string? entityName = null,
        int? entityID = null,
        string? oldValue = null,
        string? newValue = null,
        bool isSuccess = true,
        string? errorMessage = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Try to get username from ClaimTypes.Name (standard claim) or "userName" claim
            var userName = httpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                        ?? httpContext?.User?.FindFirst("userName")?.Value
                        ?? "Unknown";

            var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();

            // Get WebUserId from CompanyWebUser table
            int? webUserId = null;
            try
            {
                using var indusConn = GetIndusConnection();
                webUserId = await indusConn.ExecuteScalarAsync<int?>(
                    "SELECT WebUserId FROM CompanyWebUser WHERE WebUserName = @UserName",
                    new { UserName = userName });
            }
            catch
            {
                // If we can't get WebUserId, just log with username
            }

            var request = new CreateActivityLogRequest
            {
                WebUserId = webUserId,
                WebUserName = userName,
                ActionType = actionType,
                EntityName = entityName,
                EntityID = entityID,
                ActionDescription = actionDescription,
                OldValue = oldValue,
                NewValue = newValue,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage
            };

            await _activityLogService.LogActivityAsync(request);
        }
        catch (Exception ex)
        {
            // Log but don't throw - logging failures shouldn't break the main operation
            Console.WriteLine($"[ActivityLog] Failed to log activity: {ex.Message}");
        }
    }

    public async Task<CompanySubscriptionListResponse> GetAllAsync()
    {
        try
        {
            using var conn = GetIndusConnection();
            var query = @"SELECT * FROM Indus_Company_Authentication_For_Web_Modules WHERE ISNULL(IsActive, 1) = 1 ORDER BY CompanyName";

            var data = (await conn.QueryAsync<CompanySubscriptionDto>(query)).ToList();

            Console.WriteLine($"[CompanySubscription] GetAll returned {data.Count} rows");

            // Log activity
            await LogActivityAsync(
                actionType: "View",
                actionDescription: $"Viewed company subscription list ({data.Count} records)"
            );

            return new CompanySubscriptionListResponse { Success = true, Data = data };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanySubscription] GetAll Error: {ex.Message}");
            return new CompanySubscriptionListResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CompanySubscriptionResponse> GetByKeyAsync(string companyUserID)
    {
        try
        {
            using var conn = GetIndusConnection();
            var query = @"SELECT * FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID";

            var data = await conn.QueryFirstOrDefaultAsync<CompanySubscriptionDto>(query, new { CompanyUserID = companyUserID });

            if (data == null)
                return new CompanySubscriptionResponse { Success = false, Message = "Record not found." };

            return new CompanySubscriptionResponse { Success = true, Data = data };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanySubscription] GetByKey Error: {ex.Message}");
            return new CompanySubscriptionResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CompanySubscriptionResponse> CreateAsync(CompanySubscriptionSaveRequest request)
    {
        try
        {
            using var conn = GetIndusConnection();

            // Check if CompanyUserID already exists
            var existsQuery = "SELECT COUNT(*) FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID";
            var exists = await conn.ExecuteScalarAsync<int>(existsQuery, new { request.CompanyUserID });
            if (exists > 0)
                return new CompanySubscriptionResponse { Success = false, Message = $"CompanyUserID '{request.CompanyUserID}' already exists." };

            var query = @"
                INSERT INTO Indus_Company_Authentication_For_Web_Modules
                    (CompanyUserID, Password, Conn_String, CompanyName, ApplicationName, ApplicationVersion,
                     SubscriptionStatus, StatusDescription, SubscriptionStatusMessage,
                     Address, Country, State, City, CompanyCode, CompanyUniqueCode, MaxCompanyUniqueCode,
                     GSTIN, Email, Mobile, LoginAllowed, FromDate, ToDate, PaymentDueDate, FYear,
                     IsMessageActive, MessageDurationValue, MessageDurationType)
                VALUES
                    (@CompanyUserID, @Password, @Conn_String, @CompanyName, @ApplicationName, @ApplicationVersion,
                     @SubscriptionStatus, @StatusDescription, @SubscriptionStatusMessage,
                     @Address, @Country, @State, @City, @CompanyCode, @CompanyUniqueCode, @MaxCompanyUniqueCode,
                     @GSTIN, @Email, @Mobile, @LoginAllowed, @FromDate, @ToDate, @PaymentDueDate, @FYear,
                     @IsMessageActive, @MessageDurationValue, @MessageDurationType)";

            await conn.ExecuteAsync(query, request);

            Console.WriteLine($"[CompanySubscription] Created CompanyUserID={request.CompanyUserID}, Company={request.CompanyName}");

            // Log activity
            await LogActivityAsync(
                actionType: "Create",
                actionDescription: $"Created new company subscription: {request.CompanyName} (ID: {request.CompanyUserID})",
                entityName: "Subscription",
                newValue: System.Text.Json.JsonSerializer.Serialize(new
                {
                    request.CompanyUserID,
                    request.CompanyName,
                    request.ApplicationName,
                    request.SubscriptionStatus
                })
            );

            return await GetByKeyAsync(request.CompanyUserID);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanySubscription] Create Error: {ex.Message}");
            return new CompanySubscriptionResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CompanySubscriptionResponse> UpdateAsync(CompanySubscriptionSaveRequest request)
    {
        try
        {
            using var conn = GetIndusConnection();

            var keyToUpdate = request.OriginalCompanyUserID ?? request.CompanyUserID;

            // Get old values for logging
            var oldDataQuery = "SELECT * FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID";
            var oldData = await conn.QueryFirstOrDefaultAsync<CompanySubscriptionDto>(oldDataQuery, new { CompanyUserID = keyToUpdate });

            var query = @"
                UPDATE Indus_Company_Authentication_For_Web_Modules SET
                    CompanyUserID = @CompanyUserID,
                    Password = @Password,
                    Conn_String = @Conn_String,
                    CompanyName = @CompanyName,
                    ApplicationName = @ApplicationName,
                    ApplicationVersion = @ApplicationVersion,
                    SubscriptionStatus = @SubscriptionStatus,
                    StatusDescription = @StatusDescription,
                    SubscriptionStatusMessage = @SubscriptionStatusMessage,
                    Address = @Address,
                    Country = @Country,
                    State = @State,
                    City = @City,
                    CompanyCode = @CompanyCode,
                    CompanyUniqueCode = @CompanyUniqueCode,
                    GSTIN = @GSTIN,
                    Email = @Email,
                    Mobile = @Mobile,
                    LoginAllowed = @LoginAllowed,
                    FromDate = @FromDate,
                    ToDate = @ToDate,
                    PaymentDueDate = @PaymentDueDate,
                    FYear = @FYear,
                    IsMessageActive = @IsMessageActive,
                    MessageDurationValue = @MessageDurationValue,
                    MessageDurationType = @MessageDurationType,
                    CloudFromDate = @CloudFromDate,
                    CloudToDate = @CloudToDate,
                    CloudPaymentDueDate = @CloudPaymentDueDate,
                    CloudSubscriptionStatus = @CloudSubscriptionStatus
                WHERE CompanyUserID = @OriginalKey";

            Console.WriteLine($"[CompanySubscription] Update Request for {request.CompanyUserID}: MessageActive={request.IsMessageActive}, Duration={request.MessageDurationValue}, Type={request.MessageDurationType}");

            var affected = await conn.ExecuteAsync(query, new
            {
                request.CompanyUserID,
                request.Password,
                request.Conn_String,
                request.CompanyName,
                request.ApplicationName,
                request.ApplicationVersion,
                request.SubscriptionStatus,
                request.StatusDescription,
                request.SubscriptionStatusMessage,
                request.Address,
                request.Country,
                request.State,
                request.City,
                request.CompanyCode,
                request.CompanyUniqueCode,
                request.GSTIN,
                request.Email,
                request.Mobile,
                request.LoginAllowed,
                request.FromDate,
                request.ToDate,
                request.PaymentDueDate,
                request.FYear,
                request.IsMessageActive,
                request.MessageDurationValue,
                request.MessageDurationType,
                request.CloudFromDate,
                request.CloudToDate,
                request.CloudPaymentDueDate,
                request.CloudSubscriptionStatus,
                OriginalKey = keyToUpdate
            });

            if (affected == 0)
                return new CompanySubscriptionResponse { Success = false, Message = "Record not found." };

            Console.WriteLine($"[CompanySubscription] Updated CompanyUserID={request.CompanyUserID}, Company={request.CompanyName}");

            // Detect which fields changed
            var changedFields = new List<string>();
            if (oldData != null)
            {
                if (oldData.CompanyUserID != request.CompanyUserID) changedFields.Add("Company User ID");
                if (oldData.Password != request.Password) changedFields.Add("Password");
                if (oldData.Conn_String != request.Conn_String) changedFields.Add("Connection String");
                if (oldData.CompanyName != request.CompanyName) changedFields.Add("Company Name");
                if (oldData.ApplicationName != request.ApplicationName) changedFields.Add("Application Name");
                if (oldData.ApplicationVersion != request.ApplicationVersion) changedFields.Add("Application Version");
                if (oldData.SubscriptionStatus != request.SubscriptionStatus) changedFields.Add("Subscription Status");
                if (oldData.StatusDescription != request.StatusDescription) changedFields.Add("Status Description");
                if (oldData.SubscriptionStatusMessage != request.SubscriptionStatusMessage) changedFields.Add("Subscription Status Message");
                if (oldData.Address != request.Address) changedFields.Add("Address");
                if (oldData.Country != request.Country) changedFields.Add("Country");
                if (oldData.State != request.State) changedFields.Add("State");
                if (oldData.City != request.City) changedFields.Add("City");
                if (oldData.CompanyCode != request.CompanyCode) changedFields.Add("Company Code");
                if (oldData.CompanyUniqueCode != request.CompanyUniqueCode) changedFields.Add("Company Unique Code");
                if (oldData.GSTIN != request.GSTIN) changedFields.Add("GSTIN");
                if (oldData.Email != request.Email) changedFields.Add("Email");
                if (oldData.Mobile != request.Mobile) changedFields.Add("Mobile");
                if (oldData.LoginAllowed != request.LoginAllowed) changedFields.Add("Login Allowed");
                if (oldData.FromDate != request.FromDate) changedFields.Add("From Date");
                if (oldData.ToDate != request.ToDate) changedFields.Add("To Date");
                if (oldData.PaymentDueDate != request.PaymentDueDate) changedFields.Add("Payment Due Date");
                if (oldData.FYear != request.FYear) changedFields.Add("Financial Year");
                if (oldData.IsMessageActive != request.IsMessageActive) changedFields.Add("Message Active");
                if (oldData.MessageDurationValue != request.MessageDurationValue) changedFields.Add("Message Duration Value");
                if (oldData.MessageDurationType != request.MessageDurationType) changedFields.Add("Message Duration Type");
            }

            // Build description with changed fields
            var fieldsChanged = changedFields.Count > 0
                ? $"Fields updated: {string.Join(", ", changedFields)}"
                : "No changes detected";

            var actionDesc = $"Updated company subscription: {request.CompanyName} (ID: {request.CompanyUserID}) - {fieldsChanged}";

            // Log activity with old and new values
            await LogActivityAsync(
                actionType: "Update",
                actionDescription: actionDesc,
                entityName: "Subscription",
                oldValue: oldData != null ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    oldData.CompanyUserID,
                    oldData.CompanyName,
                    oldData.SubscriptionStatus,
                    oldData.IsMessageActive,
                    oldData.MessageDurationValue,
                    oldData.Email,
                    oldData.Mobile,
                    oldData.Address
                }) : null,
                newValue: System.Text.Json.JsonSerializer.Serialize(new
                {
                    request.CompanyUserID,
                    request.CompanyName,
                    request.SubscriptionStatus,
                    request.IsMessageActive,
                    request.MessageDurationValue,
                    request.Email,
                    request.Mobile,
                    request.Address
                })
            );

            return await GetByKeyAsync(request.CompanyUserID);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanySubscription] Update Error: {ex.Message}");
            return new CompanySubscriptionResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CompanySubscriptionResponse> DeleteAsync(string companyUserID)
    {
        try
        {
            using var conn = GetIndusConnection();

            // Get data before deleting for logging
            var oldDataQuery = "SELECT * FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID";
            var oldData = await conn.QueryFirstOrDefaultAsync<CompanySubscriptionDto>(oldDataQuery, new { CompanyUserID = companyUserID });

            var query = "UPDATE Indus_Company_Authentication_For_Web_Modules SET IsActive = 0 WHERE CompanyUserID = @CompanyUserID";
            var affected = await conn.ExecuteAsync(query, new { CompanyUserID = companyUserID });

            if (affected == 0)
                return new CompanySubscriptionResponse { Success = false, Message = "Record not found." };

            Console.WriteLine($"[CompanySubscription] Soft-deleted (IsActive=0) CompanyUserID={companyUserID}");

            // Log activity
            await LogActivityAsync(
                actionType: "Delete",
                actionDescription: $"Deactivated company subscription: {oldData?.CompanyName ?? companyUserID} (ID: {companyUserID})",
                entityName: "Subscription",
                oldValue: oldData != null ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    oldData.CompanyUserID,
                    oldData.CompanyName,
                    oldData.ApplicationName
                }) : null
            );

            return new CompanySubscriptionResponse { Success = true, Message = "Record deleted successfully." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanySubscription] Delete Error: {ex.Message}");
            return new CompanySubscriptionResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<NextClientCodeResponse> GetNextClientCodeAsync()
    {
        try
        {
            using var conn = GetIndusConnection();
            var nextCode = await conn.ExecuteScalarAsync<int>(
                "SELECT ISNULL(MAX(MaxCompanyUniqueCode), 0) + 1 FROM Indus_Company_Authentication_For_Web_Modules");

            var companyUniqueCode = $"IA{nextCode:D5}";

            Console.WriteLine($"[NextClientCode] Generated: {companyUniqueCode} (MaxCompanyUniqueCode={nextCode})");

            return new NextClientCodeResponse
            {
                Success = true,
                CompanyUniqueCode = companyUniqueCode,
                MaxCompanyUniqueCode = nextCode
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NextClientCode] Error: {ex.Message}");
            return new NextClientCodeResponse { Success = false, Message = ex.Message };
        }
    }

    private static readonly string[] AvailableServers = new[]
    {
        "13.200.122.70,1433",
        "15.206.241.195,1433"
    };

    private const string DbUser = "INDUS";
    private const string DbPassword = "Param@99811";

    public Task<ServerListResponse> GetServersAsync()
    {
        return Task.FromResult(new ServerListResponse { Success = true, Servers = AvailableServers.ToList() });
    }

    public async Task<DynamicBackupDatabaseResponse> GetBackupDatabasesAsync(string applicationName)
    {
        try
        {
            using var conn = GetIndusConnection();
            var query = "SELECT DatabaseName FROM DynamicBackupDatabase WHERE ApplicationName = @ApplicationName";
            var databases = (await conn.QueryAsync<string>(query, new { ApplicationName = applicationName })).ToList();
            return new DynamicBackupDatabaseResponse { Success = true, Databases = databases };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanySubscription] GetBackupDatabases Error: {ex.Message}");
            return new DynamicBackupDatabaseResponse { Success = false, Message = ex.Message };
        }
    }

    private static SqlConnection MasterConnection(string server)
    {
        var cs = $"Data Source={server};Initial Catalog=master;User ID={DbUser};Password={DbPassword};TrustServerCertificate=True;Connect Timeout=30";
        return new SqlConnection(cs);
    }

    public async Task<SetupDatabaseResponse> SetupDatabaseAsync(SetupDatabaseRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                return new SetupDatabaseResponse { Success = false, Message = "Server is required." };
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
                return new SetupDatabaseResponse { Success = false, Message = "Application Name is required." };
            if (string.IsNullOrWhiteSpace(request.DatabaseName))
                return new SetupDatabaseResponse { Success = false, Message = "Database Name is required." };
            if (string.IsNullOrWhiteSpace(request.BackupDatabaseName))
                return new SetupDatabaseResponse { Success = false, Message = "Backup Database Name is required." };

            string sourceServer;
            string sourceDb = request.BackupDatabaseName;

            using (var indusConn = GetIndusConnection())
            {
                var query = "SELECT ServerName FROM DynamicBackupDatabase WHERE ApplicationName = @AppName AND DatabaseName = @DbName";
                sourceServer = await indusConn.ExecuteScalarAsync<string>(query, new { AppName = request.ApplicationName, DbName = sourceDb });

                if (string.IsNullOrEmpty(sourceServer))
                    return new SetupDatabaseResponse { Success = false, Message = "Could not find matching backup database server." };
            }

            var targetServer = request.Server;
            var newDbName = request.DatabaseName.Trim();

            // Smart optimization: If target server also has the backup database, use local restore (no cross-server needed)
            if (sourceServer != targetServer)
            {
                try
                {
                    using var checkConn = MasterConnection(targetServer);
                    await checkConn.OpenAsync();
                    var existsOnTarget = await checkConn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM sys.databases WHERE name = @Name",
                        new { Name = sourceDb });
                    if (existsOnTarget > 0)
                    {
                        Console.WriteLine($"[SetupDB] Backup database '{sourceDb}' found on target server ({targetServer}). Using local restore instead of cross-server.");
                        sourceServer = targetServer;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SetupDB] Could not check target for backup DB: {ex.Message}");
                }
            }

            Console.WriteLine($"[SetupDB] Starting: App={request.ApplicationName}, Source={sourceServer}/{sourceDb}, Target={targetServer}/{newDbName}");

            // Step 1: Backup source DB on source server
            string backupPath;
            using (var sourceConn = MasterConnection(sourceServer))
            {
                await sourceConn.OpenAsync();

                // Get default backup directory
                var defaultBackupPath = await sourceConn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(500))");
                if (string.IsNullOrEmpty(defaultBackupPath))
                {
                    var defaultDataPath = await sourceConn.ExecuteScalarAsync<string>(
                        "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))");
                    defaultBackupPath = defaultDataPath ?? @"C:\Temp\";
                }
                if (!defaultBackupPath.EndsWith(@"\")) defaultBackupPath += @"\";

                backupPath = $@"{defaultBackupPath}{newDbName}_{DateTime.Now:yyyyMMddHHmmss}.bak";

                Console.WriteLine($"[SetupDB] Backing up [{sourceDb}] to {backupPath}");
                await sourceConn.ExecuteAsync(
                    $"BACKUP DATABASE [{sourceDb}] TO DISK = N'{backupPath}' WITH FORMAT, INIT, SKIP, NOUNLOAD, STATS = 10",
                    commandTimeout: 600);
                Console.WriteLine($"[SetupDB] Backup complete");
            }

            // Step 2: Restore on target server
            var restorePath = backupPath;
            bool isCrossServer = sourceServer != targetServer;

            if (isCrossServer)
            {
                // Cross-server: build UNC path to source server's backup file
                var sourceIp = sourceServer.Replace(",1433", "").Replace(",1434", "");
                var driveLetter = backupPath.Substring(0, 1);
                var pathAfterDrive = backupPath.Substring(2);
                restorePath = $@"\\{sourceIp}\{driveLetter}${pathAfterDrive}";
                Console.WriteLine($"[SetupDB] Cross-server restore from UNC: {restorePath}");
            }

            using (var targetConn = MasterConnection(targetServer))
            {
                await targetConn.OpenAsync();

                // Check if DB already exists
                var exists = await targetConn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sys.databases WHERE name = @Name",
                    new { Name = newDbName });
                if (exists > 0)
                    return new SetupDatabaseResponse { Success = false, Message = $"Database '{newDbName}' already exists on {targetServer}." };

                // Pre-check: verify UNC path is accessible from target server
                if (isCrossServer)
                {
                    try
                    {
                        await targetConn.ExecuteAsync(
                            $"EXEC master.dbo.xp_fileexist N'{restorePath}'", commandTimeout: 15);
                    }
                    catch (Exception uncEx)
                    {
                        var sourceIp = sourceServer.Replace(",1433", "").Replace(",1434", "");
                        var targetIp = targetServer.Replace(",1433", "").Replace(",1434", "");
                        Console.WriteLine($"[SetupDB] UNC path not accessible: {uncEx.Message}");
                        return new SetupDatabaseResponse
                        {
                            Success = false,
                            Message = $"Cross-server restore failed: Target server ({targetIp}) cannot access backup file on source server ({sourceIp}). " +
                                      $"Please ensure: (1) Port 445 (SMB) is open between both servers, " +
                                      $"(2) Windows admin shares (C$) are enabled on source server, " +
                                      $"(3) SQL Server service account has network access. " +
                                      $"Or use the same server ({sourceIp}) for both backup and target."
                        };
                    }
                }

                // Get logical file names from backup
                var files = (await targetConn.QueryAsync(
                    $"RESTORE FILELISTONLY FROM DISK = N'{restorePath}'",
                    commandTimeout: 120)).ToList();

                // Get default data/log paths on target
                var dataPath = await targetConn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))") ?? @"C:\SQLData\";
                var logPath = await targetConn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(500))") ?? dataPath;
                if (!dataPath.EndsWith(@"\")) dataPath += @"\";
                if (!logPath.EndsWith(@"\")) logPath += @"\";

                // Build MOVE clauses
                var moveClause = new StringBuilder();
                int dataFileIndex = 0;
                foreach (IDictionary<string, object> file in files)
                {
                    var logicalName = file["LogicalName"]?.ToString();
                    var type = file["Type"]?.ToString();
                    string physicalPath;
                    if (type == "L")
                    {
                        physicalPath = $"{logPath}{newDbName}_log.ldf";
                    }
                    else
                    {
                        var suffix = dataFileIndex == 0 ? ".mdf" : $"_{dataFileIndex}.ndf";
                        physicalPath = $"{dataPath}{newDbName}{suffix}";
                        dataFileIndex++;
                    }
                    moveClause.Append($", MOVE N'{logicalName}' TO N'{physicalPath}'");
                }

                // Restore
                var restoreSql = $"RESTORE DATABASE [{newDbName}] FROM DISK = N'{restorePath}' WITH REPLACE{moveClause}";
                Console.WriteLine($"[SetupDB] Restoring database [{newDbName}]...");
                await targetConn.ExecuteAsync(restoreSql, commandTimeout: 600);
                Console.WriteLine($"[SetupDB] Restore complete: [{newDbName}] on {targetServer}");
            }

            // Cleanup backup file
            try
            {
                using var cleanConn = MasterConnection(sourceServer);
                await cleanConn.OpenAsync();
                await cleanConn.ExecuteAsync(
                    $"EXEC master.dbo.xp_cmdshell 'del \"{backupPath}\"'", commandTimeout: 30);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SetupDB] Cleanup warning (non-critical): {ex.Message}");
            }

            // Build connection string for the new DB
            var connString = $"Password={DbPassword};Persist Security Info=True;User ID=indus;Initial Catalog={newDbName};Data Source={targetServer}";

            // Log activity
            await LogActivityAsync(
                actionType: "Setup Database",
                actionDescription: $"Setup database '{newDbName}' for client '{request.ClientName}' on server '{targetServer}' using backup '{request.BackupDatabaseName}'",
                entityName: "Database",
                newValue: System.Text.Json.JsonSerializer.Serialize(new
                {
                    request.DatabaseName,
                    request.Server,
                    request.ApplicationName,
                    request.ClientName
                })
            );

            return new SetupDatabaseResponse
            {
                Success = true,
                Message = $"Database '{newDbName}' created and restored successfully.",
                ConnectionString = connString,
                DatabaseName = newDbName,
                Server = targetServer,
                ApplicationName = request.ApplicationName,
                ClientName = request.ClientName
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SetupDB] Error: {ex.Message}");
            return new SetupDatabaseResponse { Success = false, Message = $"Database setup failed: {ex.Message}" };
        }
    }

    // â”€â”€â”€ Helper: Connect to client DB using their connection string â”€â”€â”€
    private static SqlConnection ClientConnection(string connString)
    {
        var builder = new SqlConnectionStringBuilder(connString)
        {
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };
        return new SqlConnection(builder.ConnectionString);
    }

    // â”€â”€â”€ Step 3: Company Master â”€â”€â”€
    public async Task<CompanyMasterResponse> SaveCompanyMasterAsync(CompanyMasterRequest request)
    {
        try
        {
            using var conn = ClientConnection(request.ConnectionString);
            await conn.OpenAsync();

            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM CompanyMaster WHERE CompanyID = @CompanyID",
                new { request.CompanyID });

            if (exists > 0)
            {
                var query = @"
                    UPDATE CompanyMaster SET
                        CompanyName = @CompanyName,
                        Address1 = @Address1, Address2 = @Address2, Address3 = @Address3,
                        City = @City, State = @State, Country = @Country, Pincode = @Pincode,
                        ContactNO = @ContactNO, MobileNO = @MobileNO, Email = @Email,
                        Website = @Website, StateTinNo = @StateTinNo, CINNo = @CINNo,
                        ProductionUnitAddress = @ProductionUnitAddress, Address = @Address,
                        GSTIN = @GSTIN, ProductionUnitName = @ProductionUnitName, PAN = @PAN
                    WHERE CompanyID = @CompanyID";

                await conn.ExecuteAsync(query, new
                {
                    request.CompanyID, request.CompanyName,
                    request.Address1, request.Address2, request.Address3,
                    request.City, request.State, request.Country, request.Pincode,
                    request.ContactNO, request.MobileNO, request.Email,
                    request.Website, request.StateTinNo, request.CINNo,
                    request.ProductionUnitAddress, request.Address,
                    request.GSTIN, request.ProductionUnitName, request.PAN
                });

                Console.WriteLine($"[CompanyMaster] Updated CompanyID={request.CompanyID}");
            }
            else
            {
                var query = @"
                    SET IDENTITY_INSERT CompanyMaster ON;
                    INSERT INTO CompanyMaster (CompanyID, CompanyName, Address1, Address2, Address3,
                        City, State, Country, Pincode, ContactNO, MobileNO, Email,
                        Website, StateTinNo, CINNo, ProductionUnitAddress, Address,
                        GSTIN, ProductionUnitName, PAN)
                    VALUES (@CompanyID, @CompanyName, @Address1, @Address2, @Address3,
                        @City, @State, @Country, @Pincode, @ContactNO, @MobileNO, @Email,
                        @Website, @StateTinNo, @CINNo, @ProductionUnitAddress, @Address,
                        @GSTIN, @ProductionUnitName, @PAN);
                    SET IDENTITY_INSERT CompanyMaster OFF;";

                await conn.ExecuteAsync(query, new
                {
                    request.CompanyID, request.CompanyName,
                    request.Address1, request.Address2, request.Address3,
                    request.City, request.State, request.Country, request.Pincode,
                    request.ContactNO, request.MobileNO, request.Email,
                    request.Website, request.StateTinNo, request.CINNo,
                    request.ProductionUnitAddress, request.Address,
                    request.GSTIN, request.ProductionUnitName, request.PAN
                });

                Console.WriteLine($"[CompanyMaster] Inserted CompanyID={request.CompanyID}");
            }

            return new CompanyMasterResponse { Success = true, Message = "Company Master saved.", CompanyID = request.CompanyID };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompanyMaster] Error: {ex.Message}");
            return new CompanyMasterResponse { Success = false, Message = ex.Message };
        }
    }

    // â”€â”€â”€ Step 4: Branch Master â”€â”€â”€
    public async Task<BranchMasterResponse> SaveBranchMasterAsync(BranchMasterRequest request)
    {
        try
        {
            using var conn = ClientConnection(request.ConnectionString);
            await conn.OpenAsync();

            var companyID = request.CompanyID;
            if (companyID == null || companyID == 0)
            {
                companyID = await conn.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 CompanyID FROM CompanyMaster ORDER BY CompanyID DESC");
            }

            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM BranchMaster WHERE BranchID = @BranchID",
                new { request.BranchID });

            if (exists > 0)
            {
                var query = @"
                    UPDATE BranchMaster SET
                        BranchName = @BranchName, MailingName = @MailingName,
                        Address1 = @Address1, Address2 = @Address2, Address3 = @Address3,
                        Address = @Address, City = @City, District = @District,
                        State = @State, Country = @Country, Pincode = @Pincode,
                        MobileNo = @MobileNo, Email = @Email,
                        StateTinNo = @StateTinNo, GSTIN = @GSTIN,
                        CompanyID = @CompanyID
                    WHERE BranchID = @BranchID";

                await conn.ExecuteAsync(query, new
                {
                    request.BranchID, request.BranchName,
                    MailingName = request.MailingName ?? request.BranchName,
                    request.Address1, request.Address2, request.Address3,
                    request.Address, request.City, request.District,
                    request.State, request.Country, request.Pincode,
                    request.MobileNo, request.Email,
                    request.StateTinNo, request.GSTIN,
                    CompanyID = companyID
                });

                Console.WriteLine($"[BranchMaster] Updated BranchID={request.BranchID}");
            }
            else
            {
                var query = @"
                    SET IDENTITY_INSERT BranchMaster ON;
                    INSERT INTO BranchMaster (BranchID, BranchName, MailingName,
                        Address1, Address2, Address3, Address, City, District,
                        State, Country, Pincode, MobileNo, Email,
                        StateTinNo, GSTIN, CompanyID)
                    VALUES (@BranchID, @BranchName, @MailingName,
                        @Address1, @Address2, @Address3, @Address, @City, @District,
                        @State, @Country, @Pincode, @MobileNo, @Email,
                        @StateTinNo, @GSTIN, @CompanyID);
                    SET IDENTITY_INSERT BranchMaster OFF;";

                await conn.ExecuteAsync(query, new
                {
                    request.BranchID, request.BranchName,
                    MailingName = request.MailingName ?? request.BranchName,
                    request.Address1, request.Address2, request.Address3,
                    request.Address, request.City, request.District,
                    request.State, request.Country, request.Pincode,
                    request.MobileNo, request.Email,
                    request.StateTinNo, request.GSTIN,
                    CompanyID = companyID
                });

                Console.WriteLine($"[BranchMaster] Inserted BranchID={request.BranchID}");
            }

            return new BranchMasterResponse { Success = true, Message = "Branch Master saved." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BranchMaster] Error: {ex.Message}");
            return new BranchMasterResponse { Success = false, Message = ex.Message };
        }
    }

    // â”€â”€â”€ Step 5: Production Unit Master â”€â”€â”€
    public async Task<ProductionUnitResponse> SaveProductionUnitAsync(ProductionUnitRequest request)
    {
        try
        {
            using var conn = ClientConnection(request.ConnectionString);
            await conn.OpenAsync();

            var companyID = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 CompanyID FROM CompanyMaster ORDER BY CompanyID DESC") ?? 2;
            var branchID = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 BranchID FROM BranchMaster ORDER BY BranchID DESC") ?? 1;
            var userID = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 UserID FROM UserMaster WHERE UserName = 'admin'") ?? 1;

            var maxNo = await conn.ExecuteScalarAsync<int?>(
                "SELECT ISNULL(MAX(MaxProductionUnitNo), 0) FROM ProductionUnitMaster") ?? 0;
            var newNo = maxNo + 1;
            var productionUnitCode = $"PU{newNo:D5}";

            // DELETE all existing records then INSERT
            await conn.ExecuteAsync("DELETE FROM ProductionUnitMaster");
            Console.WriteLine("[ProductionUnit] Deleted all existing records");

            var query = @"
                INSERT INTO ProductionUnitMaster
                    (ProductionUnitName, Address, City, State, GSTNo, Pincode, Country,
                     CompanyID, BranchID, UserID, IsLocked, IsDeletedTransaction,
                     MaxProductionUnitNo, ProductionUnitCode, PAN)
                VALUES
                    (@ProductionUnitName, @Address, @City, @State, @GSTNo, @Pincode, @Country,
                     @CompanyID, @BranchID, @UserID, 0, 0,
                     @MaxProductionUnitNo, @ProductionUnitCode, @PAN)";

            await conn.ExecuteAsync(query, new
            {
                request.ProductionUnitName, request.Address,
                request.City, request.State,
                GSTNo = request.GSTNo,
                request.Pincode, request.Country,
                CompanyID = companyID, BranchID = branchID, UserID = userID,
                MaxProductionUnitNo = newNo,
                ProductionUnitCode = productionUnitCode,
                request.PAN
            });

            Console.WriteLine($"[ProductionUnit] Inserted: {request.ProductionUnitName}, Code={productionUnitCode}");
            return new ProductionUnitResponse { Success = true, Message = "Production Unit saved." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProductionUnit] Error: {ex.Message}");
            return new ProductionUnitResponse { Success = false, Message = ex.Message };
        }
    }

    // â”€â”€â”€ Module Settings â”€â”€â”€
    private static string GetMasterModuleTable(string applicationName)
    {
        return applicationName.ToLower() switch
        {
            "estimoprime" or "desktop" => "EstimoprimeModuleMaster",
            "printudeerp" => "PrintudeERPModuleMaster",
            "multiunit" => "MultiUnitModuleMaster",
            _ => throw new Exception($"Unknown application for module table: {applicationName}")
        };
    }

    public async Task<ModuleSettingsResponse> GetModuleSettingsAsync(GetModuleSettingsRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
                return new ModuleSettingsResponse { Success = false, Message = "ApplicationName is required." };
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return new ModuleSettingsResponse { Success = false, Message = "ConnectionString is required." };

            var masterTable = GetMasterModuleTable(request.ApplicationName);

            // 1. Fetch all rows from master module table (Indus DB)
            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            var masterRows = (await indusConn.QueryAsync<dynamic>(
                $"SELECT ModuleHeadName, ModuleDisplayName, ModuleName FROM [{masterTable}] ORDER BY ModuleHeadName, ModuleDisplayName"
            )).ToList();

            // 2. Fetch company DB ModuleMaster to compare
            using var clientConn = ClientConnection(request.ConnectionString);
            await clientConn.OpenAsync();

            var clientModules = (await clientConn.QueryAsync<dynamic>(
                "SELECT ModuleName, ISNULL(IsDeletedTransaction, 0) AS IsDeletedTransaction FROM ModuleMaster"
            )).ToList();

            // Build lookup: ModuleName â†’ IsDeletedTransaction
            var clientLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cm in clientModules)
            {
                string modName = cm.ModuleName?.ToString() ?? "";
                int isDel = Convert.ToInt32(cm.IsDeletedTransaction);
                clientLookup[modName] = isDel;
            }

            // 3. Build result with Status
            var result = new List<ModuleSettingsRow>();
            foreach (var row in masterRows)
            {
                string moduleName = row.ModuleName?.ToString() ?? "";
                bool status = false;

                if (clientLookup.TryGetValue(moduleName, out int isDeleted))
                {
                    // Match found: checked only if IsDeletedTransaction = 0
                    status = isDeleted == 0;
                }

                result.Add(new ModuleSettingsRow
                {
                    ModuleHeadName = row.ModuleHeadName?.ToString() ?? "",
                    ModuleDisplayName = row.ModuleDisplayName?.ToString() ?? "",
                    ModuleName = moduleName,
                    Status = status
                });
            }

            Console.WriteLine($"[ModuleSettings] Loaded {result.Count} modules for {request.ApplicationName}, {result.Count(r => r.Status)} active");
            return new ModuleSettingsResponse { Success = true, Data = result };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleSettings] GetModuleSettings Error: {ex.Message}");
            return new ModuleSettingsResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<SaveModuleSettingsResponse> SaveModuleSettingsAsync(SaveModuleSettingsRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
                return new SaveModuleSettingsResponse { Success = false, Message = "ApplicationName is required." };
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return new SaveModuleSettingsResponse { Success = false, Message = "ConnectionString is required." };

            var masterTable = GetMasterModuleTable(request.ApplicationName);

            // 1. Get all master rows from Indus DB
            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();
            var masterRows = (await indusConn.QueryAsync<dynamic>(
                $"SELECT * FROM [{masterTable}]"
            )).ToList();

            // Build master lookup by ModuleName
            var masterLookup = new Dictionary<string, IDictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (IDictionary<string, object> row in masterRows)
            {
                var modName = row["ModuleName"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(modName))
                    masterLookup[modName] = row;
            }

            // 2. Connect to client DB
            using var clientConn = ClientConnection(request.ConnectionString);
            await clientConn.OpenAsync();

            // Fetch admin UserID and CompanyID for UserModuleAuthentication sync
            var adminUserId = await clientConn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 UserID FROM UserMaster WHERE UserName = 'admin'");
            var companyId = await clientConn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0");

            // Get existing client modules
            var existingModules = (await clientConn.QueryAsync<dynamic>(
                "SELECT ModuleName, ISNULL(IsDeletedTransaction, 0) AS IsDeletedTransaction FROM ModuleMaster"
            )).ToList();
            var existingLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var em in existingModules)
            {
                string modName = em.ModuleName?.ToString() ?? "";
                existingLookup[modName] = Convert.ToInt32(em.IsDeletedTransaction);
            }

            int inserted = 0, deleted = 0;

            foreach (var mod in request.Modules)
            {
                bool existsInClient = existingLookup.ContainsKey(mod.ModuleName);

                if (mod.Status)
                {
                    // Should be active
                    if (existsInClient)
                    {
                        // Exists but may be soft-deleted â†’ undelete
                        if (existingLookup[mod.ModuleName] == 1)
                        {
                            await clientConn.ExecuteAsync(
                                "UPDATE ModuleMaster SET IsDeletedTransaction = 0 WHERE ModuleName = @ModuleName",
                                new { mod.ModuleName });

                            // Sync UserModuleAuthentication: insert if not exists
                            if (adminUserId.HasValue)
                            {
                                var moduleId = await clientConn.ExecuteScalarAsync<int?>(
                                    "SELECT ModuleID FROM ModuleMaster WHERE ModuleName = @ModuleName",
                                    new { mod.ModuleName });
                                if (moduleId.HasValue)
                                {
                                    await clientConn.ExecuteAsync(@"
                                        IF NOT EXISTS (SELECT 1 FROM UserModuleAuthentication WHERE ModuleID = @ModuleID AND UserID = @UserID)
                                        INSERT INTO UserModuleAuthentication
                                        (UserID, ModuleID, ModuleName, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, IsHomePage, CompanyID, IsLocked, CreatedBy, IsDeletedTransaction)
                                        VALUES (@UserID, @ModuleID, @ModuleName, 1, 1, 1, 1, 1, 1, 1, 0, @CompanyID, 0, @UserID, 0)",
                                        new { UserID = adminUserId.Value, ModuleID = moduleId.Value, mod.ModuleName, CompanyID = companyId ?? 2 });
                                }
                            }
                            inserted++;
                        }
                        // Already active, skip
                    }
                    else
                    {
                        // INSERT: copy all columns from master
                        if (masterLookup.TryGetValue(mod.ModuleName, out var masterRow))
                        {
                            // Build dynamic INSERT from master columns, excluding identity ModuleID
                            var columns = masterRow.Keys.Where(k => !k.Equals("ModuleID", StringComparison.OrdinalIgnoreCase)).ToList();
                            var colNames = string.Join(", ", columns.Select(c => $"[{c}]"));
                            var paramNames = string.Join(", ", columns.Select(c => $"@{c}"));

                            var paramObj = new DynamicParameters();
                            foreach (var col in columns)
                            {
                                paramObj.Add(col, masterRow[col]);
                            }

                            // Insert into ModuleMaster and get the new ModuleID
                            var newModuleId = await clientConn.ExecuteScalarAsync<int>(
                                $"INSERT INTO ModuleMaster ({colNames}) OUTPUT INSERTED.ModuleID VALUES ({paramNames})", paramObj);

                            // Sync UserModuleAuthentication: insert with full permissions
                            if (adminUserId.HasValue)
                            {
                                await clientConn.ExecuteAsync(@"
                                    INSERT INTO UserModuleAuthentication
                                    (UserID, ModuleID, ModuleName, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, IsHomePage, CompanyID, IsLocked, CreatedBy, IsDeletedTransaction)
                                    VALUES (@UserID, @ModuleID, @ModuleName, 1, 1, 1, 1, 1, 1, 1, 0, @CompanyID, 0, @UserID, 0)",
                                    new { UserID = adminUserId.Value, ModuleID = newModuleId, mod.ModuleName, CompanyID = companyId ?? 2 });
                            }
                            inserted++;
                        }
                    }
                }
                else
                {
                    // Should be inactive
                    if (existsInClient && existingLookup[mod.ModuleName] == 0)
                    {
                        // Get ModuleID before deleting
                        var moduleId = await clientConn.ExecuteScalarAsync<int?>(
                            "SELECT ModuleID FROM ModuleMaster WHERE ModuleName = @ModuleName",
                            new { mod.ModuleName });

                        // Delete from UserModuleAuthentication first
                        if (moduleId.HasValue)
                        {
                            await clientConn.ExecuteAsync(
                                "DELETE FROM UserModuleAuthentication WHERE ModuleID = @ModuleID AND ModuleName = @ModuleName",
                                new { ModuleID = moduleId.Value, mod.ModuleName });
                        }

                        // DELETE from ModuleMaster
                        await clientConn.ExecuteAsync(
                            "DELETE FROM ModuleMaster WHERE ModuleName = @ModuleName",
                            new { mod.ModuleName });
                        deleted++;
                    }
                }
            }

            Console.WriteLine($"[ModuleSettings] Save complete: {inserted} inserted/activated, {deleted} deleted");
            return new SaveModuleSettingsResponse
            {
                Success = true,
                Message = $"Module settings saved. {inserted} activated, {deleted} removed.",
                Inserted = inserted,
                Deleted = deleted
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleSettings] Save Error: {ex.Message}");
            return new SaveModuleSettingsResponse { Success = false, Message = ex.Message };
        }
    }

    // â”€â”€â”€ Copy Modules â”€â”€â”€
    public async Task<ClientDropdownResponse> GetClientDropdownAsync()
    {
        try
        {
            using var conn = GetIndusConnection();
            await conn.OpenAsync();
            var clients = (await conn.QueryAsync<ClientDropdownItem>(
                "SELECT CompanyName, CompanyUserID, ISNULL(ApplicationName,'') AS ApplicationName FROM Indus_Company_Authentication_For_Web_Modules WHERE ISNULL(ApplicationName,'') <> 'Desktop' ORDER BY CompanyName"
            )).ToList();

            return new ClientDropdownResponse { Success = true, Data = clients };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CopyModules] GetClientDropdown Error: {ex.Message}");
            return new ClientDropdownResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CopyModulesResponse> CopyModulesAsync(CopyModulesRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
                return new CopyModulesResponse { Success = false, Message = "Source connection string is required." };
            if (string.IsNullOrWhiteSpace(request.TargetCompanyUserID))
                return new CopyModulesResponse { Success = false, Message = "Target client is required." };

            // 1. Get target Conn_String from Indus DB
            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();
            var targetConnStr = await indusConn.ExecuteScalarAsync<string>(
                "SELECT Conn_String FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID",
                new { CompanyUserID = request.TargetCompanyUserID });

            if (string.IsNullOrWhiteSpace(targetConnStr))
                return new CopyModulesResponse { Success = false, Message = "Target client connection string not found." };

            // 2. Fetch ALL rows from source ModuleMaster
            using var sourceConn = ClientConnection(request.SourceConnectionString);
            await sourceConn.OpenAsync();
            var sourceRows = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ModuleMaster")).ToList();

            if (sourceRows.Count == 0)
                return new CopyModulesResponse { Success = false, Message = "No modules found in source database." };

            // 3. Connect to target and copy in a transaction
            using var targetConn = ClientConnection(targetConnStr);
            await targetConn.OpenAsync();
            using var transaction = targetConn.BeginTransaction();

            try
            {
                // Fetch admin UserID and CompanyID for UserModuleAuthentication sync
                var adminUserId = await targetConn.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 UserID FROM UserMaster WHERE UserName = 'admin'", transaction: transaction);
                var companyId = await targetConn.ExecuteScalarAsync<int?>(
                    "SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0", transaction: transaction);

                // Delete existing data (UserModuleAuthentication first, then ModuleMaster)
                await targetConn.ExecuteAsync("DELETE FROM UserModuleAuthentication", transaction: transaction);
                await targetConn.ExecuteAsync("DELETE FROM ModuleMaster", transaction: transaction);

                // Insert all source rows in batches of 50 to stay under SQL parameter limits (2100)
                var rowsList = sourceRows.Cast<IDictionary<string, object>>().ToList();
                int totalCopied = 0;
                int batchSize = 50;

                for (int i = 0; i < rowsList.Count; i += batchSize)
                {
                    var batch = rowsList.Skip(i).Take(batchSize).ToList();
                    if (!batch.Any()) continue;

                    var firstRow = batch.First();
                    var columns = firstRow.Keys.Where(k => !k.Equals("ModuleID", StringComparison.OrdinalIgnoreCase)).ToList();
                    var colNames = string.Join(", ", columns.Select(c => $"[{c}]"));

                    var sqlBuilder = new StringBuilder();
                    var batchParams = new DynamicParameters();
                    sqlBuilder.Append($"INSERT INTO ModuleMaster ({colNames}) VALUES ");

                    var valueLines = new List<string>();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        var row = batch[j];
                        var rowParamNames = new List<string>();
                        foreach (var col in columns)
                        {
                            var paramName = $"r{i}_{j}_{col}";
                            batchParams.Add(paramName, row[col]);
                            rowParamNames.Add($"@{paramName}");
                        }
                        valueLines.Add($"({string.Join(", ", rowParamNames)})");
                    }
                    sqlBuilder.Append(string.Join(", ", valueLines));
                    sqlBuilder.Append(";");

                    await targetConn.ExecuteAsync(sqlBuilder.ToString(), batchParams, transaction: transaction);
                    totalCopied += batch.Count;
                }

                // Sync UserModuleAuthentication in ONE SHOT for admin user instead of inside the loop
                if (adminUserId.HasValue)
                {
                    await targetConn.ExecuteAsync(@"
                        INSERT INTO UserModuleAuthentication
                        (UserID, ModuleID, ModuleName, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, IsHomePage, CompanyID, IsLocked, CreatedBy, IsDeletedTransaction)
                        SELECT @UserID, ModuleID, ModuleName, 1, 1, 1, 1, 1, 1, 1, 0, @CompanyID, 0, @UserID, 0
                        FROM ModuleMaster",
                        new { UserID = adminUserId.Value, CompanyID = companyId ?? 2 },
                        transaction: transaction);
                }

                transaction.Commit();
                Console.WriteLine($"[CopyModules] Optimized Copy: {totalCopied} modules (+ UserModuleAuthentication) to target {request.TargetCompanyUserID}");

                // Log activity
                await LogActivityAsync(
                    actionType: "Copy Modules",
                    actionDescription: $"Copied {totalCopied} modules to client {request.TargetCompanyUserID}",
                    entityName: "Modules",
                    newValue: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        request.TargetCompanyUserID,
                        ModulesCopied = totalCopied
                    })
                );

                return new CopyModulesResponse
                {
                    Success = true,
                    Message = $"Modules copied successfully to selected client. ({totalCopied} modules)",
                    CopiedCount = totalCopied
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CopyModules] Error: {ex.Message}");
            return new CopyModulesResponse { Success = false, Message = ex.Message };
        }
    }

    // â”€â”€â”€ Final Step: Complete Setup â”€â”€â”€
    public async Task<CompleteSetupResponse> CompleteSetupAsync(CompleteSetupRequest request)
    {
        try
        {
            string adminUserName = "admin";
            string adminPassword = string.Empty;

            // 1. Update UserMaster with City, State, Country on client DB
            using (var conn = ClientConnection(request.ConnectionString))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync(
                    @"UPDATE UserMaster SET City = @City, State = @State, Country = @Country
                      WHERE UserName = 'admin'",
                    new { request.City, request.State, request.Country });
                
                var adminUser = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT UserName, Password FROM UserMaster WHERE UserName = 'admin'");
                if (adminUser != null)
                {
                    adminUserName = adminUser.UserName;
                    adminPassword = adminUser.Password;
                }
                Console.WriteLine("[CompleteSetup] Updated UserMaster for admin");
            }

            // 2. Fetch CompanyUserID and Password from Indus DB
            using var indusConn = GetIndusConnection();
            var sub = await indusConn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT CompanyUserID, Password FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID",
                new { request.CompanyUserID });

            if (sub == null)
                return new CompleteSetupResponse { Success = false, Message = "Subscription record not found." };

            Console.WriteLine($"[CompleteSetup] SUCCESS for CompanyUserID={sub.CompanyUserID}");

            // Log activity
            await LogActivityAsync(
                actionType: "Complete Setup",
                actionDescription: $"Completed setup for CompanyUserID: {sub.CompanyUserID}",
                entityName: "Setup"
            );

            return new CompleteSetupResponse
            {
                Success = true,
                Message = "Setup completed successfully!",
                CompanyUserID = sub.CompanyUserID,
                Password = sub.Password,
                UserName = adminUserName,
                UserPassword = adminPassword
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CompleteSetup] Error: {ex.Message}");
            return new CompleteSetupResponse { Success = false, Message = ex.Message };
        }
    }

    // â”€â”€â”€ Module Group Authority â”€â”€â”€
    public async Task<ModuleGroupDropdownResponse> GetModuleGroupsAsync(string applicationName)
    {
        try
        {
            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            var groups = await indusConn.QueryAsync<string>(
                "SELECT DISTINCT ModuleGroupName FROM ModuleGroupMaster WHERE ApplicationName = @AppName ORDER BY ModuleGroupName",
                new { AppName = applicationName });

            return new ModuleGroupDropdownResponse { Success = true, Data = groups.ToList() };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleGroupAuthority] GetGroups Error: {ex.Message}");
            return new ModuleGroupDropdownResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ModuleGroupModulesResponse> GetModuleGroupModulesAsync(ModuleGroupModulesRequest request)
    {
        try
        {
            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            var modules = await indusConn.QueryAsync<ModuleGroupModuleRow>(
                "SELECT ModuleHeadName, ModuleDisplayName, ModuleName FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName ORDER BY ModuleHeadName, ModuleDisplayName",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName });

            return new ModuleGroupModulesResponse { Success = true, Data = modules.ToList() };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleGroupAuthority] GetModules Error: {ex.Message}");
            return new ModuleGroupModulesResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ModuleGroupModulesResponse> GetAvailableModulesForGroupAsync(string applicationName)
    {
        try
        {
            using var masterConn = new SqlConnection("Data Source=13.200.122.70,1433;Initial Catalog=IndusEnterpriseNewInstallation;User ID=INDUS;Password=Param@99811;TrustServerCertificate=True");
            await masterConn.OpenAsync();

            var modules = await masterConn.QueryAsync<ModuleGroupModuleRow>(
                "SELECT ModuleHeadName, ModuleDisplayName, ModuleName FROM ModuleMaster ORDER BY ModuleHeadName, ModuleDisplayName");

            return new ModuleGroupModulesResponse { Success = true, Data = modules.ToList() };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleGroupAuthority] GetAvailableModules Error: {ex.Message}");
            return new ModuleGroupModulesResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CreateModuleGroupResponse> CreateModuleGroupAsync(CreateModuleGroupRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
                return new CreateModuleGroupResponse { Success = false, Message = "Application Name is required." };
            if (string.IsNullOrWhiteSpace(request.ModuleGroupName))
                return new CreateModuleGroupResponse { Success = false, Message = "Module Group Name is required." };
            if (request.SelectedModuleNames == null || request.SelectedModuleNames.Count == 0)
                return new CreateModuleGroupResponse { Success = false, Message = "Please select at least one module." };

            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            // Check if group already exists (prevent duplicates)
            var exists = await indusConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName });

            if (exists > 0)
                return new CreateModuleGroupResponse { Success = false, Message = $"Module Group '{request.ModuleGroupName}' already exists for {request.ApplicationName}." };

            // Use default metadata values (these tables don't exist in Indus DB)
            var companyID = 2;

            // Get module details from IndusEnterpriseNewInstallation
            using var masterConn = new SqlConnection("Data Source=13.200.122.70,1433;Initial Catalog=IndusEnterpriseNewInstallation;User ID=INDUS;Password=Param@99811;TrustServerCertificate=True");
            await masterConn.OpenAsync();
            var fYear = await masterConn.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='admin'") ?? "2024-25";
            var userID = await masterConn.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 1;

            // Use transaction for atomicity
            using var transaction = indusConn.BeginTransaction();
            try
            {
                int inserted = 0;
                foreach (var moduleName in request.SelectedModuleNames)
                {
                    // Check if module already exists in this group (prevent duplicate module in same group)
                    var moduleExists = await indusConn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName AND ModuleName = @ModuleName",
                        new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName, ModuleName = moduleName },
                        transaction: transaction);

                    if (moduleExists > 0)
                        continue; // Skip duplicate

                    // Get full module details from template database
                    var module = await masterConn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM ModuleMaster WHERE ModuleName = @ModuleName",
                        new { ModuleName = moduleName });

                    if (module != null)
                    {
                        // Build column list (exclude ModuleID and columns we'll override)
                        var moduleDict = (IDictionary<string, object>)module;

                        // Columns to exclude (ModuleID and columns we'll set custom values for)
                        var excludeColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "ModuleID", "ApplicationName", "ModuleGroupName", "CompanyID", "UserID",
                            "FYear", "IsLocked", "CreatedBy", "CreatedDate", "ModifiedBy", "ModifiedDate",
                            "DeletedBy", "DeletedDate", "IsDeletedTransaction", "ProductionUnitID","IsIntegratedModule"
                        };

                        var columns = moduleDict.Keys.Where(k => !excludeColumns.Contains(k)).ToList();

                        // Build insert columns list
                        var insertColumns = new List<string>(columns);
                        insertColumns.Add("ApplicationName");
                        insertColumns.Add("ModuleGroupName");
                        insertColumns.Add("CompanyID");
                        insertColumns.Add("UserID");
                        insertColumns.Add("FYear");
                        insertColumns.Add("IsLocked");
                        insertColumns.Add("CreatedBy");
                        insertColumns.Add("CreatedDate");
                        insertColumns.Add("ModifiedBy");
                        insertColumns.Add("ModifiedDate");
                        insertColumns.Add("DeletedBy");
                        insertColumns.Add("DeletedDate");
                        insertColumns.Add("IsDeletedTransaction");
                        insertColumns.Add("ProductionUnitID");

                        var colNames = string.Join(", ", insertColumns.Select(c => $"[{c}]"));
                        var paramNames = string.Join(", ", insertColumns.Select(c => $"@{c}"));

                        var paramObj = new DynamicParameters();

                        // Map ModuleMaster columns (only the ones not excluded)
                        foreach (var col in columns)
                        {
                            paramObj.Add(col, moduleDict[col]);
                        }

                        // Add metadata columns with custom values
                        paramObj.Add("ApplicationName", request.ApplicationName);
                        paramObj.Add("ModuleGroupName", request.ModuleGroupName);
                        paramObj.Add("CompanyID", companyID);
                        paramObj.Add("UserID", userID);
                        paramObj.Add("FYear", fYear);
                        paramObj.Add("IsLocked", 0);
                        paramObj.Add("CreatedBy", userID);
                        paramObj.Add("CreatedDate", DateTime.Now);
                        paramObj.Add("ModifiedBy", 0);
                        paramObj.Add("ModifiedDate", null);
                        paramObj.Add("DeletedBy", 0);
                        paramObj.Add("DeletedDate", null);
                        paramObj.Add("IsDeletedTransaction", 0);
                        paramObj.Add("ProductionUnitID", 0);

                        await indusConn.ExecuteAsync(
                            $"INSERT INTO ModuleGroupMaster ({colNames}) VALUES ({paramNames})",
                            paramObj,
                            transaction: transaction);
                        inserted++;
                    }
                }

                transaction.Commit();
                Console.WriteLine($"[ModuleGroupAuthority] Created group '{request.ModuleGroupName}' with {inserted} modules");
                return new CreateModuleGroupResponse
                {
                    Success = true,
                    Message = $"Module Group '{request.ModuleGroupName}' created successfully with {inserted} modules."
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleGroupAuthority] CreateGroup Error: {ex.Message}");
            return new CreateModuleGroupResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<UpdateModuleGroupResponse> UpdateModuleGroupAsync(UpdateModuleGroupRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
                return new UpdateModuleGroupResponse { Success = false, Message = "Application Name is required." };
            if (string.IsNullOrWhiteSpace(request.ModuleGroupName))
                return new UpdateModuleGroupResponse { Success = false, Message = "Module Group Name is required." };
            if (request.SelectedModuleNames == null)
                request.SelectedModuleNames = new List<string>();

            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            // Check if group exists
            var exists = await indusConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName });

            if (exists == 0)
                return new UpdateModuleGroupResponse { Success = false, Message = $"Module Group '{request.ModuleGroupName}' does not exist." };

            // Get existing modules for this group
            var existingModules = (await indusConn.QueryAsync<string>(
                "SELECT ModuleName FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName })).ToList();

            var existingSet = new HashSet<string>(existingModules, StringComparer.OrdinalIgnoreCase);
            var newSet = new HashSet<string>(request.SelectedModuleNames, StringComparer.OrdinalIgnoreCase);

            // Calculate differences
            var toDelete = existingSet.Except(newSet).ToList(); // Modules to remove
            var toInsert = newSet.Except(existingSet).ToList(); // Modules to add

            // Use default metadata values
            var companyID = 2;

            // Get module details from IndusEnterpriseNewInstallation
            using var masterConn = new SqlConnection("Data Source=13.200.122.70,1433;Initial Catalog=IndusEnterpriseNewInstallation;User ID=INDUS;Password=Param@99811;TrustServerCertificate=True");
            await masterConn.OpenAsync();
            var fYear = await masterConn.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='admin'") ?? "2024-25";
            var userID = await masterConn.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 1;

            // Use transaction for atomicity
            using var transaction = indusConn.BeginTransaction();
            try
            {
                int deleted = 0;
                int inserted = 0;

                // DELETE removed modules
                foreach (var moduleName in toDelete)
                {
                    await indusConn.ExecuteAsync(
                        "DELETE FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName AND ModuleName = @ModuleName",
                        new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName, ModuleName = moduleName },
                        transaction: transaction);
                    deleted++;
                }

                // INSERT new modules
                foreach (var moduleName in toInsert)
                {
                    // Get full module details from template database
                    var module = await masterConn.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT * FROM ModuleMaster WHERE ModuleName = @ModuleName",
                        new { ModuleName = moduleName });

                    if (module != null)
                    {
                        // Build column list (exclude ModuleID and columns we'll override)
                        var moduleDict = (IDictionary<string, object>)module;

                        // Columns to exclude (ModuleID and columns we'll set custom values for)
                        var excludeColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "ModuleID", "ApplicationName", "ModuleGroupName", "CompanyID", "UserID",
                            "FYear", "IsLocked", "CreatedBy", "CreatedDate", "ModifiedBy", "ModifiedDate",
                            "DeletedBy", "DeletedDate", "IsDeletedTransaction", "ProductionUnitID","IsIntegratedModule"
                        };

                        var columns = moduleDict.Keys.Where(k => !excludeColumns.Contains(k)).ToList();

                        // Build insert columns list
                        var insertColumns = new List<string>(columns);
                        insertColumns.Add("ApplicationName");
                        insertColumns.Add("ModuleGroupName");
                        insertColumns.Add("CompanyID");
                        insertColumns.Add("UserID");
                        insertColumns.Add("FYear");
                        insertColumns.Add("IsLocked");
                        insertColumns.Add("CreatedBy");
                        insertColumns.Add("CreatedDate");
                        insertColumns.Add("ModifiedBy");
                        insertColumns.Add("ModifiedDate");
                        insertColumns.Add("DeletedBy");
                        insertColumns.Add("DeletedDate");
                        insertColumns.Add("IsDeletedTransaction");
                        insertColumns.Add("ProductionUnitID");

                        var colNames = string.Join(", ", insertColumns.Select(c => $"[{c}]"));
                        var paramNames = string.Join(", ", insertColumns.Select(c => $"@{c}"));

                        var paramObj = new DynamicParameters();

                        // Map ModuleMaster columns (only the ones not excluded)
                        foreach (var col in columns)
                        {
                            paramObj.Add(col, moduleDict[col]);
                        }

                        // Add metadata columns with custom values
                        paramObj.Add("ApplicationName", request.ApplicationName);
                        paramObj.Add("ModuleGroupName", request.ModuleGroupName);
                        paramObj.Add("CompanyID", companyID);
                        paramObj.Add("UserID", userID);
                        paramObj.Add("FYear", fYear);
                        paramObj.Add("IsLocked", 0);
                        paramObj.Add("CreatedBy", userID);
                        paramObj.Add("CreatedDate", DateTime.Now);
                        paramObj.Add("ModifiedBy", 0);
                        paramObj.Add("ModifiedDate", null);
                        paramObj.Add("DeletedBy", 0);
                        paramObj.Add("DeletedDate", null);
                        paramObj.Add("IsDeletedTransaction", 0);
                        paramObj.Add("ProductionUnitID", 0);

                        await indusConn.ExecuteAsync(
                            $"INSERT INTO ModuleGroupMaster ({colNames}) VALUES ({paramNames})",
                            paramObj,
                            transaction: transaction);
                        inserted++;
                    }
                }

                transaction.Commit();
                Console.WriteLine($"[ModuleGroupAuthority] Updated group '{request.ModuleGroupName}': +{inserted} modules, -{deleted} modules");
                return new UpdateModuleGroupResponse
                {
                    Success = true,
                    Message = $"Module Group '{request.ModuleGroupName}' updated successfully. Added {inserted} modules, removed {deleted} modules.",
                    Inserted = inserted,
                    Deleted = deleted
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModuleGroupAuthority] UpdateGroup Error: {ex.Message}");
            return new UpdateModuleGroupResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApplyModuleGroupToClientResponse> ApplyModuleGroupToClientAsync(ApplyModuleGroupToClientRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
                return new ApplyModuleGroupToClientResponse { Success = false, Message = "Application Name is required." };
            if (string.IsNullOrWhiteSpace(request.ModuleGroupName))
                return new ApplyModuleGroupToClientResponse { Success = false, Message = "Module Group Name is required." };
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return new ApplyModuleGroupToClientResponse { Success = false, Message = "Connection String is required." };

            // Step 1: Get modules from ModuleGroupMaster (Indus DB)
            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            var groupModules = (await indusConn.QueryAsync<dynamic>(
                "SELECT * FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName AND ISNULL(IsDeletedTransaction, 0) = 0",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName })).ToList();

            if (groupModules.Count == 0)
                return new ApplyModuleGroupToClientResponse { Success = false, Message = $"No modules found for Module Group '{request.ModuleGroupName}'." };

            // Step 2: Connect to Client Database
            using var clientConn = ClientConnection(request.ConnectionString);
            await clientConn.OpenAsync();

            // Get admin UserID and CompanyID
            var adminUserId = await clientConn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 UserID FROM UserMaster WHERE UserName = 'admin'");
            var companyId = await clientConn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0");

            if (!adminUserId.HasValue)
                return new ApplyModuleGroupToClientResponse { Success = false, Message = "Admin user not found in client database." };

            using var transaction = clientConn.BeginTransaction();
            try
            {
                // Step 3: Clear existing data
                await clientConn.ExecuteAsync("DELETE FROM UserModuleAuthentication", transaction: transaction);
                await clientConn.ExecuteAsync("DELETE FROM ModuleMaster", transaction: transaction);

                Console.WriteLine("[ApplyModuleGroup] Cleared existing modules and permissions");

                int inserted = 0;

                // Step 4: Insert modules from ModuleGroupMaster to client's ModuleMaster
                foreach (IDictionary<string, object> sourceRow in groupModules)
                {
                    // Exclude columns specific to ModuleGroupMaster
                    var excludeColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "ModuleID", "ModuleGroupID", "ModuleGroupName", "ApplicationName"
                    };

                    var columns = sourceRow.Keys.Where(k => !excludeColumns.Contains(k)).ToList();
                    var colNames = string.Join(", ", columns.Select(c => $"[{c}]"));
                    var paramNames = string.Join(", ", columns.Select(c => $"@{c}"));

                    var paramObj = new DynamicParameters();
                    foreach (var col in columns)
                    {
                        paramObj.Add(col, sourceRow[col]);
                    }

                    // Insert and get new ModuleID
                    var newModuleId = await clientConn.ExecuteScalarAsync<int>(
                        $"INSERT INTO ModuleMaster ({colNames}) OUTPUT INSERTED.ModuleID VALUES ({paramNames})",
                        paramObj,
                        transaction: transaction);

                    // Step 5: Insert into UserModuleAuthentication for admin
                    string moduleName = sourceRow.ContainsKey("ModuleName") ? sourceRow["ModuleName"]?.ToString() ?? "" : "";

                    await clientConn.ExecuteAsync(@"
                        INSERT INTO UserModuleAuthentication
                        (UserID, ModuleID, ModuleName, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, IsHomePage, CompanyID, IsLocked, CreatedBy, IsDeletedTransaction)
                        VALUES (@UserID, @ModuleID, @ModuleName, 1, 1, 1, 1, 1, 1, 1, 0, @CompanyID, 0, @UserID, 0)",
                        new { UserID = adminUserId.Value, ModuleID = newModuleId, ModuleName = moduleName, CompanyID = companyId ?? 2 },
                        transaction: transaction);

                    inserted++;
                }

                transaction.Commit();
                Console.WriteLine($"[ApplyModuleGroup] Applied {inserted} modules to client database");

                return new ApplyModuleGroupToClientResponse
                {
                    Success = true,
                    Message = $"Successfully applied {inserted} modules to client database.",
                    TotalModules = inserted
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApplyModuleGroup] Error: {ex.Message}");
            return new ApplyModuleGroupToClientResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<CheckModulesExistResponse> CheckModulesExistAsync(string connectionString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return new CheckModulesExistResponse { Success = false, Message = "Connection String is required." };

            using var clientConn = ClientConnection(connectionString);
            await clientConn.OpenAsync();

            var count = await clientConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ModuleMaster");

            return new CheckModulesExistResponse
            {
                Success = true,
                HasModules = count > 0,
                ModuleCount = count
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CheckModulesExist] Error: {ex.Message}");
            return new CheckModulesExistResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<DeleteModuleGroupResponse> DeleteModuleGroupAsync(DeleteModuleGroupRequest request)
    {
        try
        {
            Console.WriteLine($"[DeleteModuleGroup] Starting delete request for group '{request.ModuleGroupName}' by user '{request.UserName}'");

            // Validate inputs
            if (string.IsNullOrWhiteSpace(request.ApplicationName))
            {
                Console.WriteLine("[DeleteModuleGroup] Validation failed: Application Name is required");
                return new DeleteModuleGroupResponse { Success = false, Message = "Application Name is required." };
            }
            if (string.IsNullOrWhiteSpace(request.ModuleGroupName))
            {
                Console.WriteLine("[DeleteModuleGroup] Validation failed: Module Group Name is required");
                return new DeleteModuleGroupResponse { Success = false, Message = "Module Group Name is required." };
            }
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                Console.WriteLine("[DeleteModuleGroup] Validation failed: User Name is required");
                return new DeleteModuleGroupResponse { Success = false, Message = "User Name is required." };
            }
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                Console.WriteLine("[DeleteModuleGroup] Validation failed: Password is required");
                return new DeleteModuleGroupResponse { Success = false, Message = "Password is required." };
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                Console.WriteLine("[DeleteModuleGroup] Validation failed: Reason is required");
                return new DeleteModuleGroupResponse { Success = false, Message = "Reason is required." };
            }

            Console.WriteLine("[DeleteModuleGroup] All validations passed, connecting to database");

            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            Console.WriteLine("[DeleteModuleGroup] Database connection opened, authenticating user");

            // Step 1: Authenticate user
            var authCount = await indusConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM CompanyWebUser WHERE WebUserName = @UserName AND WebUserPassword = @Password",
                new { UserName = request.UserName, Password = request.Password });

            Console.WriteLine($"[DeleteModuleGroup] Authentication query returned count: {authCount}");

            if (authCount == 0)
            {
                Console.WriteLine($"[DeleteModuleGroup] Authentication failed for user: {request.UserName}");
                return new DeleteModuleGroupResponse { Success = false, Message = "Invalid Username or Password" };
            }

            Console.WriteLine($"[DeleteModuleGroup] User '{request.UserName}' authenticated successfully");

            // Step 2: Check if module group exists
            Console.WriteLine($"[DeleteModuleGroup] Checking if module group exists: '{request.ModuleGroupName}' in app '{request.ApplicationName}'");
            var groupCount = await indusConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName });

            Console.WriteLine($"[DeleteModuleGroup] Module group check returned count: {groupCount}");

            if (groupCount == 0)
            {
                Console.WriteLine($"[DeleteModuleGroup] Module group not found");
                return new DeleteModuleGroupResponse { Success = false, Message = $"Module Group '{request.ModuleGroupName}' does not exist." };
            }

            // Step 3: Delete all records for this module group
            Console.WriteLine($"[DeleteModuleGroup] Proceeding with deletion");
            var deletedCount = await indusConn.ExecuteAsync(
                "DELETE FROM ModuleGroupMaster WHERE ApplicationName = @AppName AND ModuleGroupName = @GroupName",
                new { AppName = request.ApplicationName, GroupName = request.ModuleGroupName });

            Console.WriteLine($"[DeleteModuleGroup] SUCCESS - Deleted {deletedCount} modules from group '{request.ModuleGroupName}' by user '{request.UserName}'. Reason: {request.Reason}");

            // Log activity
            await LogActivityAsync(
                actionType: "Delete",
                actionDescription: $"Deleted Module Group: {request.ModuleGroupName} (App: {request.ApplicationName}). Reason: {request.Reason}",
                entityName: "ModuleGroup",
                oldValue: System.Text.Json.JsonSerializer.Serialize(new
                {
                    request.ModuleGroupName,
                    request.ApplicationName,
                    DeletedCount = deletedCount,
                    Reason = request.Reason,
                    ActionBy = request.UserName
                })
            );

            return new DeleteModuleGroupResponse
            {
                Success = true,
                Message = $"Module Group '{request.ModuleGroupName}' deleted successfully. Removed {deletedCount} modules.",
                DeletedCount = deletedCount
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeleteModuleGroup] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[DeleteModuleGroup] Stack Trace: {ex.StackTrace}");
            return new DeleteModuleGroupResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<DeleteCompanySubscriptionResponse> DeleteCompanySubscriptionWithAuthAsync(DeleteCompanySubscriptionRequest request)
    {
        try
        {
            Console.WriteLine($"[DeleteCompanySubscription] Starting delete request for Company User ID '{request.CompanyUserID}' by user '{request.UserName}'");

            // Validate inputs
            if (string.IsNullOrWhiteSpace(request.CompanyUserID))
            {
                Console.WriteLine("[DeleteCompanySubscription] Validation failed: Company User ID is required");
                return new DeleteCompanySubscriptionResponse { Success = false, Message = "Company User ID is required." };
            }
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                Console.WriteLine("[DeleteCompanySubscription] Validation failed: User Name is required");
                return new DeleteCompanySubscriptionResponse { Success = false, Message = "User Name is required." };
            }
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                Console.WriteLine("[DeleteCompanySubscription] Validation failed: Password is required");
                return new DeleteCompanySubscriptionResponse { Success = false, Message = "Password is required." };
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                Console.WriteLine("[DeleteCompanySubscription] Validation failed: Reason is required");
                return new DeleteCompanySubscriptionResponse { Success = false, Message = "Reason is required." };
            }

            Console.WriteLine("[DeleteCompanySubscription] All validations passed, connecting to database");

            using var indusConn = GetIndusConnection();
            await indusConn.OpenAsync();

            Console.WriteLine("[DeleteCompanySubscription] Database connection opened, authenticating user");

            // Step 1: Authenticate user
            var authCount = await indusConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM CompanyWebUser WHERE WebUserName = @UserName AND WebUserPassword = @Password",
                new { UserName = request.UserName, Password = request.Password });

            Console.WriteLine($"[DeleteCompanySubscription] Authentication query returned count: {authCount}");

            if (authCount == 0)
            {
                Console.WriteLine($"[DeleteCompanySubscription] Authentication failed for user: {request.UserName}");
                return new DeleteCompanySubscriptionResponse { Success = false, Message = "Invalid Username or Password" };
            }

            Console.WriteLine($"[DeleteCompanySubscription] User '{request.UserName}' authenticated successfully");

            // Step 2: Check if company subscription exists
            Console.WriteLine($"[DeleteCompanySubscription] Checking if company subscription exists: '{request.CompanyUserID}'");
            var oldData = await indusConn.QueryFirstOrDefaultAsync<CompanySubscriptionDto>(
                "SELECT * FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID = @CompanyUserID",
                new { CompanyUserID = request.CompanyUserID });

            if (oldData == null)
            {
                Console.WriteLine($"[DeleteCompanySubscription] Company subscription not found");
                return new DeleteCompanySubscriptionResponse { Success = false, Message = $"Company subscription '{request.CompanyUserID}' does not exist." };
            }

            // Step 3: Soft-delete — set IsActive = 0 using exact row match (no hard DELETE)
            Console.WriteLine($"[DeleteCompanySubscription] Proceeding with soft-delete (IsActive = 0)");
            var deletedCount = await indusConn.ExecuteAsync(
                @"UPDATE Indus_Company_Authentication_For_Web_Modules
                  SET IsActive = 0
                  WHERE CompanyUserID = @CompanyUserID
                    AND CompanyName = @CompanyName
                    AND ISNULL(CompanyUniqueCode, '') = ISNULL(@CompanyUniqueCode, '')",
                new { request.CompanyUserID, request.CompanyName, request.CompanyUniqueCode });

            Console.WriteLine($"[DeleteCompanySubscription] SUCCESS - Soft-deleted (IsActive=0) company subscription '{request.CompanyUserID}' by user '{request.UserName}'. Reason: {request.Reason}");

            // Log activity
            await LogActivityAsync(
                actionType: "Delete",
                actionDescription: $"Deactivated company subscription (Auth): {oldData.CompanyName} (ID: {request.CompanyUserID}). Reason: {request.Reason}",
                entityName: "Subscription",
                oldValue: System.Text.Json.JsonSerializer.Serialize(new
                {
                    oldData.CompanyUserID,
                    oldData.CompanyName,
                    oldData.ApplicationName,
                    Reason = request.Reason,
                    ActionBy = request.UserName
                })
            );

            return new DeleteCompanySubscriptionResponse
            {
                Success = true,
                Message = $"Company subscription '{request.CompanyUserID}' deleted successfully."
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeleteCompanySubscription] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[DeleteCompanySubscription] Stack Trace: {ex.StackTrace}");
            return new DeleteCompanySubscriptionResponse { Success = false, Message = ex.Message };
        }
    }
}

