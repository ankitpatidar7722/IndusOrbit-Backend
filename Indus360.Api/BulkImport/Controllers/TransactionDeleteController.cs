using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.DTOs;
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Backend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionDeleteController : ControllerBase
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICompanySessionStore _sessionStore;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TransactionDeleteController> _logger;

        public TransactionDeleteController(
            IHttpContextAccessor httpContextAccessor,
            ICompanySessionStore sessionStore,
            IConfiguration configuration,
            ILogger<TransactionDeleteController> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _sessionStore = sessionStore;
            _configuration = configuration;
            _logger = logger;
        }

        private string? GetConnectionString()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            _logger.LogInformation("[GetConnectionString] Starting connection string resolution");

            if (httpContext == null)
            {
                _logger.LogWarning("[GetConnectionString] HttpContext is null");
            }
            else if (httpContext.User == null)
            {
                _logger.LogWarning("[GetConnectionString] HttpContext.User is null");
            }
            else
            {
                _logger.LogInformation("[GetConnectionString] HttpContext and User are available");

                var sessionIdClaim = httpContext.User.FindFirst("sessionId")?.Value;
                _logger.LogInformation($"[GetConnectionString] SessionId claim: {sessionIdClaim ?? "NULL"}");

                if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
                {
                    _logger.LogInformation($"[GetConnectionString] Parsed sessionId: {sessionId}");

                    if (_sessionStore.TryGetSession(sessionId, out var session))
                    {
                        if (session != null && !string.IsNullOrEmpty(session.ConnectionString))
                        {
                            _logger.LogInformation($"[GetConnectionString] Session found with connection string");
                            var connBuilder = new SqlConnectionStringBuilder(session.ConnectionString);
                            connBuilder.TrustServerCertificate = true;
                            return connBuilder.ConnectionString;
                        }
                        else
                        {
                            _logger.LogWarning($"[GetConnectionString] Session found but connection string is empty");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[GetConnectionString] Session not found for sessionId: {sessionId}");
                    }
                }
                else
                {
                    _logger.LogWarning("[GetConnectionString] SessionId claim is empty or invalid");
                }
            }

            // Fallback: Check if IndusConnection exists
            _logger.LogInformation("[GetConnectionString] Attempting fallback to IndusConnection");
            var defaultConn = _configuration.GetConnectionString("IndusConnection");
            if (!string.IsNullOrEmpty(defaultConn))
            {
                _logger.LogInformation("[GetConnectionString] Using IndusConnection");
                return defaultConn;
            }

            _logger.LogError("[GetConnectionString] No connection string found - all methods failed");
            return null;
        }

        private string StandardizeModuleName(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName)) return moduleName;
            
            var lower = moduleName.ToLower().Trim();

            // Item Master
            if (lower.Contains("item") || lower.Contains("masters.aspx"))
                return "Item Master";
            
            // Ledger Master
            if (lower.Contains("ledger") || lower.Contains("ledgermaster.aspx"))
                return "Ledger Master";
            
            // Tool Master
            if (lower.Contains("tool") || lower.Contains("toolmaster.aspx"))
                return "Tool Master";

            // Spare Part Master
            if (lower.Contains("spare") || lower.Contains("sparepartmaster.aspx"))
                return "Spare Part Master";

            // Product Group / HSN Master
            if (lower.Contains("product group") || lower.Contains("hsn") || lower.Contains("productgroupmasterforgst.aspx"))
                return "Product Group Master";

            // Machine Master
            if (lower.Contains("machine") || lower.Contains("machinemaster.aspx"))
                return "Machine Master";

            // Process Master
            if (lower.Contains("process") || lower.Contains("processmaster.aspx"))
                return "Process Master";

            // Category Master
            if (lower.Contains("category") || lower.Contains("categorymaster.aspx"))
                return "Category Master";

            // Department Master
            if (lower.Contains("department") || lower.Contains("departmentmaster.aspx"))
                return "Department Master";

            // Warehouse Master
            if (lower.Contains("warehouse") || lower.Contains("warehousemaster.aspx"))
                return "Warehouse Master";

            // Unit Master
            if (lower.Contains("unit") || lower.Contains("unitmaster.aspx"))
                return "Unit Master";

            return moduleName;
        }

        [HttpPost("clear-all-transactions")]
        public async Task ClearAllTransactions([FromBody] ClearTransactionDataRequestDto request)
        {
            Response.ContentType = "application/json";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
            {
                await WriteProgressAsync(new { type = "error", message = "Username and Reason are required." });
                return;
            }

            try
            {
                // Get connection string
                var connectionString = GetConnectionString();
                if (string.IsNullOrEmpty(connectionString))
                {
                    _logger.LogError("Connection string is not initialized. User may not be logged in properly.");
                    await WriteProgressAsync(new { type = "error", message = "Database connection not available. Please ensure you are logged in." });
                    return;
                }

                _logger.LogInformation($"ClearAllTransactions request from user: {request.Username}");

                // Verify credentials
                var isValidUser = await VerifyCredentialsAsync(request.Username, request.Password);
                if (!isValidUser)
                {
                    _logger.LogWarning($"Invalid credentials for user: {request.Username}");
                    await WriteProgressAsync(new { type = "error", message = "Invalid username or password." });
                    return;
                }

                // Truncate all transaction tables with progress updates
                await TruncateAllTransactionTablesWithProgressAsync(request.Username, request.Reason);
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, $"Database error clearing transactions: {sqlEx.Message}");
                await WriteProgressAsync(new { type = "error", message = "Database error occurred while clearing transactions.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all transaction data");
                await WriteProgressAsync(new { type = "error", message = "An error occurred while clearing transactions.", error = ex.Message });
            }
        }

        private async Task WriteProgressAsync(object data)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            await Response.WriteAsync(json + "\n");
            await Response.Body.FlushAsync();
        }

        private async Task<bool> VerifyCredentialsAsync(string username, string password)
        {
            var connectionString = GetConnectionString();

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Connection string is empty in VerifyCredentialsAsync");
                throw new InvalidOperationException("Database connection string is not available");
            }

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Get password and blocked status
                var getUserQuery = @"
                    SELECT TOP 1 ISNULL(Password, '') as Password, ISNULL(IsBlocked, 0) as IsBlocked
                    FROM UserMaster
                    WHERE UserName = @Username";

                var user = await connection.QueryFirstOrDefaultAsync<dynamic>(getUserQuery, new { Username = username });

                if (user == null)
                {
                    _logger.LogWarning($"ERP User verification failed: User '{username}' not found in UserMaster.");
                    
                    // Fallback: Check if it's the Company Master User
                    var httpContext = _httpContextAccessor.HttpContext;
                    var sessionIdClaim = httpContext?.User.FindFirst("sessionId")?.Value;
                    if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
                    {
                        if (_sessionStore.TryGetSession(sessionId, out var session) && session != null)
                        {
                            if (string.Equals(session.CompanyUserID, username, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation($"Verification successful via Company Master User ID: {username}");
                                return true; // Company master user is always authorized for their own company
                            }
                        }
                    }

                    return false;
                }

                if ((bool)user.IsBlocked)
                {
                    _logger.LogWarning($"Verification failed: User '{username}' is blocked.");
                    return false;
                }

                string dbPassword = (string)user.Password;

                // If database password is empty, allow empty password input
                if (string.IsNullOrEmpty(dbPassword))
                {
                    _logger.LogInformation($"Credential verification successful for user with no password: {username}");
                    return true;
                }

                // If database has password, encode input password and compare
                var encodedPassword = PasswordEncoder.ChangePassword(password ?? string.Empty);

                if (dbPassword == encodedPassword)
                {
                    _logger.LogInformation($"Credential verification successful for user: {username}");
                    return true;
                }

                _logger.LogWarning($"Credential verification failed (password mismatch) for user: {username}");
                return false;
            }
        }

        private async Task TruncateAllTransactionTablesWithProgressAsync(string username, string reason)
        {
            var connectionString = GetConnectionString();

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Connection string is empty in TruncateAllTransactionTablesWithProgressAsync");
                await WriteProgressAsync(new { type = "error", message = "Database connection string is not available" });
                throw new InvalidOperationException("Database connection string is not available");
            }

            // List of all transaction tables to truncate
            var transactionTables = new List<string>
            {
                // Sales Enquiry
                "JobEnquiry", "JobEnquiryContents", "JobEnquiryLayerDetail", "JobEnquiryProcess",
                // Estimation Tables
                "JobBooking", "JobBookingAttachments", "JobBookingColorDetails", "JobBookingContents",
                "JobBookingOnetimeCharges", "JobBookingMaterialCost", "JobBookingContentBookForms",
                "JobBookingCorrugation", "JobBookingContentsLayerDetail", "JobBookingProcess",
                "JobBookingProcessMaterialRequirement", "JobBookingContentsSpecification", "JobBookingCostings",
                "JobBookingProcessMaterialParameterDetail",
                // Price Approval Tables
                "JobApprovedCost",
                // Sales Order Tables
                "JobOrderBookingDetails", "JobOrderBookingOneTimeCharges", "JobOrderBookingBatchDetails",
                "JobOrderBookingDeliveryDetails", "JobOrderBooking",
                // Product Master Tables
                "ProductMasterComplaintRegister", "ProductMasterProcessToolAllocation", "ProductMaster",
                "ProductMasterContents", "ProductMasterContentBookForms", "ProductMasterProcess",
                "ProductMasterProcessMaterialRequirement", "ProductMasterContentsSpecification",
                "ProductMasterOneTimeCharges", "ProductMasterProcessMaterialParameterDetail",
                "ProductMasterContentsLayerDetail", "ProductMasterCorrugation",
                // Job Card Tables
                "JobBookingJobCardProcessToolAllocation", "JobBookingJobCard", "JobBookingJobCardContentBookForms",
                "JobBookingJobCardBatch", "JobBookingJobCardManual", "JobBookingJobCardColorDetails",
                "JobBookingJobCardContents", "JobBookingJobCardFormWiseDetails", "JobBookingJobCardProcessMaterialParameterDetail",
                "JobBookingJobCardGang", "JobBookingJobCardContentsBookedItems", "JobBookingJobCardProcess",
                "JobBookingJobCardProcessMaterialRequirement", "JobBookingJobCardContentsSpecification",
                "JobBookingJobCardCorrugation", "JobBookingJobCardContentsLayerDetail",
                // Job Schedule Tables
                "JobScheduleRelease", "JobScheduleMachineWiseFreeSlot", "JobScheduleReleaseMachineWise",
                "JobScheduleReleaseMachineWiseTemp", "JobScheduleReleaseSequence", "JobScheduleReleaseStatus",
                "JobScheduleReleaseTemp",
                // Production Entry Tables
                "ProductionEntryProcessInspection", "ProductionEntryLineClearance", "ProductionUpdateEntry",
                "ProductionCommentsEntry", "ProductionEntryFormWise", "ProductionEntryProcessInspectionMain",
                "ProductionLineClearanceParametersentry", "ProductionEntryProcessInspectionDetail",
                "ProductionEntry", "ManualProductionDetails", "MachineCurrentStatusEntry",
                // Semi Packing Tables
                "FinishGoodsTransactionSemiPacking", "JobSemiPackingMain", "JobSemiPackingDetail",
                // Finish Goods Tables
                "FinishGoodsQCInspectionPackingDetail", "FinishGoodsTransactionTaxes",
                "FinishGoodsTransactionDetail", "FinishGoodsQCInspectionDetail", "FinishGoodsQCInspectionMain",
                "FinishGoodsTransactionMain", "DespatchFreightEntryTaxes", "DespatchFreightEntry",
                // Invoice Module Tables
                "InvoiceTransactionMain", "InvoiceTransactionTaxes", "InvoiceTransactionDetail",
                // Outsource Module Tables
                "OutsourceProductionDetails", "OutSourceProductionPurchaseInvoiceDetail", "OutsourceChallanDetails",
                "OutsourceChallanMain", "OutsourcePaymentMain", "OutsourceProductionMain",
                "OutSourceProductionPurchaseInvoiceMain", "OutSourceProductionPurchaseInvoiceTaxes",
                "OutsourcePaymentDetails",
                // Work Order Conversion Tables
                "WorkOrderConversionChallanMain", "WorkOrderConversionPurchaseInvoiceDetail",
                "WorkOrderConversionPurchaseInvoiceMain", "WorkOrderConversionPurchaseInvoiceTaxes",
                "WorkOrderConversionChallanDetails",
                // GRS Entry Tables
                "GRSItemTransactionDetail", "GRSOverheadChargesEntry", "GRSPurchaseOverheadChargesDetail",
                "GRSTransactionDetail", "GRSTransactionMain",
                // Inventory Tables
                "ItemTransactionDetail", "ItemTransactionMain", "ItemTransactionBatchDetail",
                "ItemPurchaseInvoiceDetail", "AdditionalItemRequisitionDetail", "AdditionalItemRequisitionMain",
                "ItemConsumptionMain", "ItemConsumptionDetail", "ItemQCInspectionMain", "ItemQCInspectionDetail",
                "ItemPicklistReleaseDetail", "ItemPurchaseDeliverySchedule", "ItemPurchaseInvoiceMain",
                "ItemPurchaseInvoiceTaxes", "ItemPurchaseOrderTaxes", "ItemPurchaseOverheadCharges",
                "ItemPurchaseRequisitionDetail",
                // Other Item Inventory Tables
                "OtherItemPurchaseDeliverySchedule", "OtherItemPurchaseInvoiceMain", "OtherItemPurchaseInvoiceTaxes",
                "OtherItemPurchaseOrderTaxes", "OtherItemPurchaseOverheadCharges", "OtherItemPurchaseInvoiceDetail",
                "OtherItemTransactionDetail", "OtherItemTransactionMain",
                // Tool Inventory Tables
                "ToolPurchaseOrderTaxes", "ToolPurchaseOverheadCharges", "ToolPurchaseRequisitionDetail",
                "ToolTransactionDetail", "ToolTransactionMain", "ToolPurchaseInvoiceDetail",
                "ToolPurchaseInvoiceMain", "ToolPurchaseInvoiceTaxes",
                // Spare Part Inventory Tables
                "SpareConsumptionDetail", "SpareConsumptionMain", "SparePurchaseInvoiceDetail",
                "SparePurchaseInvoiceMain", "SparePurchaseInvoiceTaxes", "SparePurchaseOrderTaxes",
                "SparePurchaseOverheadCharges", "SparePurchaseRequisitionDetail", "SpareTransactionDetail",
                "SpareTransactionMain",
                // Client Inventory Tables
                "ClientConsumptionDetail", "ClientConsumptionMain", "ClientTransactionDetail", "ClientTransactionMain",
                // Maintenance Tables
                "MaintenanceTicketMaster", "MaintenanceTicketProductionEntry", "MaintenanceWorkOrderMain",
                "MaintenanceWorkOrderServiceDetail", "MaintenanceWorkOrderTaxes", "EventScheduleMain"
            };

            int totalTables = transactionTables.Count;
            int processedTables = 0;
            int totalDeleted = 0;

            // Send initial progress
            await WriteProgressAsync(new { type = "start", total = totalTables, message = "Starting deletion process..." });

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Start transaction
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Log the deletion activity
                        try
                        {
                            await System.IO.File.AppendAllTextAsync("debug_log.txt",
                                $"[{DateTime.Now}] ClearAllTransactions: User '{username}' initiated truncation. Reason: {reason}\n");
                        }
                        catch { }

                        // Try to log to ActivityLog if table exists
                        try
                        {
                            var logQuery = @"
                                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ActivityLog')
                                BEGIN
                                    INSERT INTO ActivityLog (ActivityType, Description, PerformedBy, Timestamp)
                                    VALUES ('TRUNCATE_ALL_TRANSACTIONS', @Reason, @Username, GETDATE())
                                END";

                            using (var logCmd = new SqlCommand(logQuery, connection, transaction))
                            {
                                logCmd.Parameters.AddWithValue("@Reason", reason);
                                logCmd.Parameters.AddWithValue("@Username", username);
                                await logCmd.ExecuteNonQueryAsync();
                            }
                        }
                        catch (Exception logEx)
                        {
                            _logger.LogWarning($"Could not log to ActivityLog: {logEx.Message}");
                        }

                        // Truncate each table
                        foreach (var table in transactionTables)
                        {
                            try
                            {
                                // Check if table exists (fast check)
                                var checkTableQuery = "SELECT COUNT(*) FROM sys.tables WHERE name = @TableName";
                                bool tableExists;
                                using (var checkCmd = new SqlCommand(checkTableQuery, connection, transaction))
                                {
                                    checkCmd.Parameters.AddWithValue("@TableName", table);
                                    tableExists = (int)await checkCmd.ExecuteScalarAsync() > 0;
                                }

                                if (!tableExists)
                                {
                                    processedTables++;
                                    continue;
                                }

                                // Try TRUNCATE first (fastest), fallback to DELETE if FK constraints exist
                                try
                                {
                                    var truncateQuery = $"TRUNCATE TABLE [{table}]";
                                    using (var truncateCmd = new SqlCommand(truncateQuery, connection, transaction))
                                    {
                                        truncateCmd.CommandTimeout = 300; // 5 minutes timeout
                                        await truncateCmd.ExecuteNonQueryAsync();
                                    }
                                }
                                catch (SqlException sqlEx) when (sqlEx.Number == 4712) // Cannot truncate table because it is being referenced by FK
                                {
                                    // Use DELETE instead
                                    var deleteQuery = $"DELETE FROM [{table}]";
                                    using (var deleteCmd = new SqlCommand(deleteQuery, connection, transaction))
                                    {
                                        deleteCmd.CommandTimeout = 300;
                                        await deleteCmd.ExecuteNonQueryAsync();
                                    }
                                }

                                processedTables++;
                                int percentage = (int)((double)processedTables / totalTables * 100);

                                // Send progress update
                                await WriteProgressAsync(new
                                {
                                    type = "progress",
                                    current = processedTables,
                                    total = totalTables,
                                    percentage = percentage,
                                    table = table,
                                    message = $"Cleared table: {table}"
                                });

                                _logger.LogInformation($"✓ Cleared table: {table} ({processedTables}/{totalTables})");
                            }
                            catch (Exception ex)
                            {
                                processedTables++;
                                _logger.LogWarning($"Could not clear table {table}: {ex.Message}");

                                // Send error for this table but continue
                                await WriteProgressAsync(new
                                {
                                    type = "table_error",
                                    current = processedTables,
                                    total = totalTables,
                                    percentage = (int)((double)processedTables / totalTables * 100),
                                    table = table,
                                    error = ex.Message
                                });
                            }
                        }

                        // Commit transaction
                        transaction.Commit();

                        // Send completion
                        await WriteProgressAsync(new
                        {
                            type = "complete",
                            message = "All transaction data cleared successfully.",
                            total = totalTables,
                            processed = processedTables
                        });
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        [HttpPost("check-master-usage")]
        public async Task<IActionResult> CheckMasterUsage([FromBody] CheckMasterUsageRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ModuleName))
            {
                return BadRequest(new { message = "Module name is required." });
            }

            var connectionString = GetConnectionString();
            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new { message = "Database connection not available. Please ensure you are logged in." });
            }

            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var result = new MasterUsageResultDto();
                var moduleName = StandardizeModuleName(request.ModuleName);

                if (moduleName.Contains("Item", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckItemMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Ledger", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckLedgerMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckToolMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Machine", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckMachineMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Process", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckProcessMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Category", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckCategoryMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Department", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckDepartmentMasterUsage(connection, result);
                }
                else if (moduleName.Contains("Warehouse", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckWarehouseMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Unit", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckUnitMasterUsage(connection, result);
                }
                else if (moduleName.Contains("Product Group", StringComparison.OrdinalIgnoreCase) || 
                         moduleName.Contains("HSN", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckProductGroupMasterUsage(connection, request.SubModuleId, result);
                }
                else if (moduleName.Contains("Spare", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckSpareMasterUsage(connection, result);
                }
                else
                {
                    return BadRequest(new { message = $"Unknown module type: {moduleName}" });
                }

                result.IsUsed = result.Usages.Any(u => u.Count > 0);
                // The unique count is already calculated inside each CheckXXXMasterUsage method
                // and stored in result.UnusedItemsCount = total - uniqueUsedCount.
                // So uniqueUsedCount = total - UnusedItemsCount.
                result.ItemsUsedInTransactions = result.TotalItemsInGroup - result.UnusedItemsCount;

                if (result.IsUsed)
                {
                    var usedAreas = result.Usages.Where(u => u.Count > 0).Select(u => u.Area).ToList();
                    result.Message = $"{result.ItemsUsedInTransactions} item(s) from this group are currently in use across: {string.Join(", ", usedAreas)}. Please clear these transactions first before deleting the master data.";
                }
                else
                {
                    result.Message = "No items are currently in use in any transactions. You can safely proceed with deletion.";
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking master usage");
                return StatusCode(500, new { message = "Error checking master usage.", error = ex.Message });
            }
        }

        private async Task<bool> TableExistsAsync(SqlConnection connection, string tableName, SqlTransaction? transaction = null)
        {
            var query = "SELECT COUNT(*) FROM sys.tables WHERE name = @TableName";
            using var cmd = transaction != null
                ? new SqlCommand(query, connection, transaction)
                : new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            return (int)await cmd.ExecuteScalarAsync() > 0;
        }

        private async Task<int> SafeCountAsync(SqlConnection connection, string query, SqlParameter[]? parameters = null)
        {
            try
            {
                using var cmd = new SqlCommand(query, connection);
                cmd.CommandTimeout = 60;
                if (parameters != null)
                {
                    cmd.Parameters.AddRange(parameters);
                }
                var result = await cmd.ExecuteScalarAsync();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"SafeCountAsync failed for query: {ex.Message}");
                return 0;
            }
        }

        private async Task<List<int>> SafeQueryIdsAsync(SqlConnection connection, string query, SqlParameter[]? parameters = null, SqlTransaction? transaction = null)
        {
            try
            {
                var ids = new List<int>();
                using var cmd = transaction != null
                    ? new SqlCommand(query, connection, transaction)
                    : new SqlCommand(query, connection);
                cmd.CommandTimeout = 60;
                if (parameters != null) cmd.Parameters.AddRange(parameters);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        var val = reader.GetValue(0);
                        ids.Add(Convert.ToInt32(val));
                    }
                }
                return ids;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"SafeQueryIdsAsync failed: {ex.Message}");
                return new List<int>();
            }
        }

        private async Task<HashSet<int>> GetUsedItemIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var itemSubQuery = "SELECT ItemID FROM ItemMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0";

            if (await TableExistsAsync(connection, "ItemTransactionMain", transaction) && await TableExistsAsync(connection, "ItemTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ITD.ItemID FROM ItemTransactionMain ITM
                       INNER JOIN ItemTransactionDetail ITD ON ITM.TransactionID = ITD.TransactionID
                       WHERE ITD.ItemID IN ({itemSubQuery}) AND ISNULL(ITM.IsDeletedTransaction, 0) = 0 AND ITM.VoucherID <> -8",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "JobBookingContents", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT PaperID FROM JobBookingContents WHERE PaperID IN ({itemSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "JobBookingJobCardContents", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT PaperID FROM JobBookingJobCardContents WHERE PaperID IN ({itemSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ProductMasterContents", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT PaperID FROM ProductMasterContents WHERE PaperID IN ({itemSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ItemQCInspectionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ItemID FROM ItemQCInspectionDetail WHERE ItemID IN ({itemSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ItemPurchaseInvoiceDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ItemID FROM ItemPurchaseInvoiceDetail WHERE ItemID IN ({itemSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "GRSItemTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ItemID FROM GRSItemTransactionDetail WHERE ItemID IN ({itemSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedLedgerIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var ledgerSubQuery = "SELECT LedgerID FROM LedgerMaster WHERE LedgerGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0";

            if (await TableExistsAsync(connection, "InvoiceTransactionMain", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM InvoiceTransactionMain WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "FinishGoodsTransactionMain", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM FinishGoodsTransactionMain WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "JobBooking", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM JobBooking WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ItemPurchaseInvoiceMain", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM ItemPurchaseInvoiceMain WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "JobOrderBooking", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM JobOrderBooking WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // Additional checks from VB Migration code
            if (await TableExistsAsync(connection, "ConcernPersonMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM ConcernPersonMaster WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ClientMachineCostSettings", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM ClientMachineCostSettings WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "EmployeeMachineAllocation", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM EmployeeMachineAllocation WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ItemPurchaseOrderTaxes", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM ItemPurchaseOrderTaxes WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ItemTransactionMain", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM ItemTransactionMain WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "SupplierWisePurchaseSetting", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM SupplierWisePurchaseSetting WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // Hidden constraint: Locked ledgers cannot be deleted
            if (await TableExistsAsync(connection, "LedgerMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT LedgerID FROM LedgerMaster WHERE LedgerGroupID = @GroupId AND ISNULL(IsLocked, 0) = 1 AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedToolIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var toolSubQuery = "SELECT ToolID FROM ToolMaster WHERE ToolGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0";

            if (await TableExistsAsync(connection, "ToolTransactionMain", transaction) && await TableExistsAsync(connection, "ToolTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT TTD.ToolID FROM ToolTransactionMain TTM
                       INNER JOIN ToolTransactionDetail TTD ON TTM.TransactionID = TTD.TransactionID
                       WHERE TTD.ToolID IN ({toolSubQuery}) AND ISNULL(TTM.IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "ToolPurchaseInvoiceMain", transaction) && await TableExistsAsync(connection, "ToolPurchaseInvoiceDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT TPD.ToolID FROM ToolPurchaseInvoiceMain TPM
                       INNER JOIN ToolPurchaseInvoiceDetail TPD ON TPM.TransactionID = TPD.TransactionID
                       WHERE TPD.ToolID IN ({toolSubQuery}) AND ISNULL(TPM.IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            if (await TableExistsAsync(connection, "JobBookingJobCardProcessToolAllocation", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ToolID FROM JobBookingJobCardProcessToolAllocation WHERE ToolID IN ({toolSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // Additional Check from VB code: Check for QC Approved transactions
            if (await TableExistsAsync(connection, "ToolTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ToolID FROM ToolTransactionDetail WHERE ToolID IN ({toolSubQuery}) AND ISNULL(QCApprovalNo,'') <> '' AND (ISNULL(ApprovedQuantity,0) > 0 OR ISNULL(RejectedQuantity,0) > 0) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // Constraint: Locked tools cannot be deleted
            if (await TableExistsAsync(connection, "ToolMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ToolID FROM ToolMaster WHERE ToolGroupID = @GroupId AND ISNULL(IsLocked, 0) = 1 AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", groupId) }, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedMachineIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            // Since MachineMaster might not have a strict GroupID but usually links to DepartmentID
            var machineSubQuery = groupId > 0 
                ? "SELECT MachineID FROM MachineMaster WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT MachineID FROM MachineMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            var parameters = groupId > 0 ? new[] { new SqlParameter("@GroupId", groupId) } : null;

            // 1. Job Booking Contents (Hard constraint from VB)
            if (await TableExistsAsync(connection, "JobBookingContents", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT MachineID FROM JobBookingContents WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 2. Job Booking Process
            if (await TableExistsAsync(connection, "JobBookingProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT MachineID FROM JobBookingProcess WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. Job Card Process
            if (await TableExistsAsync(connection, "JobBookingJobCardProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT MachineID FROM JobBookingJobCardProcess WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 4. Product Master Process
            if (await TableExistsAsync(connection, "ProductMasterProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT MachineID FROM ProductMasterProcess WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 5. Job Schedule Release
            if (await TableExistsAsync(connection, "JobScheduleReleaseMachineWise", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT MachineID FROM JobScheduleReleaseMachineWise WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedProcessIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var processSubQuery = groupId > 0 
                ? "SELECT ProcessID FROM ProcessMaster WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT ProcessID FROM ProcessMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            var parameters = groupId > 0 ? new[] { new SqlParameter("@GroupId", groupId) } : null;

            // 1. Job Booking Process
            if (await TableExistsAsync(connection, "JobBookingProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProcessID FROM JobBookingProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 2. Job Card Process
            if (await TableExistsAsync(connection, "JobBookingJobCardProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProcessID FROM JobBookingJobCardProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. Product Master Process
            if (await TableExistsAsync(connection, "ProductMasterProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProcessID FROM ProductMasterProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 4. Job Enquiry Process
            if (await TableExistsAsync(connection, "JobEnquiryProcess", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProcessID FROM JobEnquiryProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedCategoryIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var categorySubQuery = groupId > 0
                ? "SELECT CategoryID FROM CategoryMaster WHERE SegmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT CategoryID FROM CategoryMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            var parameters = groupId > 0 ? new[] { new SqlParameter("@GroupId", groupId) } : null;

            // 1. Product Master
            if (await TableExistsAsync(connection, "ProductMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT CategoryID FROM ProductMaster WHERE CategoryID IN ({categorySubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 2. Job Booking Contents
            if (await TableExistsAsync(connection, "JobBookingContents", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT CategoryID FROM JobBookingContents WHERE CategoryID IN ({categorySubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. Job Enquiry
            if (await TableExistsAsync(connection, "JobEnquiry", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT CategoryID FROM JobEnquiry WHERE CategoryID IN ({categorySubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedDepartmentIdsAsync(SqlConnection connection, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();

            // 1. Process Master
            if (await TableExistsAsync(connection, "ProcessMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    "SELECT DISTINCT DepartmentID FROM ProcessMaster WHERE ISNULL(DepartmentID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 2. Machine Master
            if (await TableExistsAsync(connection, "MachineMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    "SELECT DISTINCT DepartmentID FROM MachineMaster WHERE ISNULL(DepartmentID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. Ledger Master
            if (await TableExistsAsync(connection, "LedgerMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    "SELECT DISTINCT DepartmentID FROM LedgerMaster WHERE ISNULL(DepartmentID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedWarehouseIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var warehouseSubQuery = groupId > 0
                ? "SELECT WarehouseID FROM WarehouseMaster WHERE ProductionUnitID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT WarehouseID FROM WarehouseMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            var parameters = groupId > 0 ? new[] { new SqlParameter("@GroupId", groupId) } : null;

            // 1. Item Transaction Detail
            if (await TableExistsAsync(connection, "ItemTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT WarehouseID FROM ItemTransactionDetail WHERE WarehouseID IN ({warehouseSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedUnitIdsAsync(SqlConnection connection, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();

            // 1. Item Master
            if (await TableExistsAsync(connection, "ItemMaster", transaction))
            {
                var queries = new[] { "StockUnitID", "PurchaseUnitID", "SalesUnitID", "UnitID" };
                foreach (var col in queries)
                {
                    var ids = await SafeQueryIdsAsync(connection,
                        $"SELECT DISTINCT {col} FROM ItemMaster WHERE ISNULL({col}, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                    foreach (var id in ids) usedIds.Add(id);
                }
            }

            // 2. Product Master
            if (await TableExistsAsync(connection, "ProductMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    "SELECT DISTINCT UnitID FROM ProductMaster WHERE ISNULL(UnitID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. Spare Part Master
            if (await TableExistsAsync(connection, "SparePartMaster", transaction))
            {
                var colName = "UnitID"; // Usually UnitID or SparePartUnitID
                var ids = await SafeQueryIdsAsync(connection,
                    $"SELECT DISTINCT {colName} FROM SparePartMaster WHERE ISNULL({colName}, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedProductGroupIdsAsync(SqlConnection connection, int groupId, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var hsnSubQuery = groupId > 0
                ? "SELECT ProductHSNID FROM ProductHSNMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT ProductHSNID FROM ProductHSNMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            var parameters = groupId > 0 ? new[] { new SqlParameter("@GroupId", groupId) } : null;

            // 1. Item Master
            if (await TableExistsAsync(connection, "ItemMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProductHSNID FROM ItemMaster WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 2. Tool Master
            if (await TableExistsAsync(connection, "ToolMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProductHSNID FROM ToolMaster WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. Item Transactions
            if (await TableExistsAsync(connection, "ItemTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProductHSNID FROM ItemTransactionDetail WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 4. Job Booking
            if (await TableExistsAsync(connection, "JobBooking", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProductHSNID FROM JobBooking WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 5. Product Master
            if (await TableExistsAsync(connection, "ProductMaster", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT ProductHSNID FROM ProductMaster WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters, transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task<HashSet<int>> GetUsedSparePartIdsAsync(SqlConnection connection, SqlTransaction? transaction = null)
        {
            var usedIds = new HashSet<int>();
            var spareSubQuery = "SELECT SparePartID FROM SparePartMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. SpareTransactionDetail — confirmed column: SpareID (from SparePartMasterStockService line 564)
            if (await TableExistsAsync(connection, "SpareTransactionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT SpareID FROM SpareTransactionDetail
                       WHERE SpareID IN ({spareSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    transaction: transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 2. SparePurchaseInvoiceDetail — column: SpareID (consistent naming)
            if (await TableExistsAsync(connection, "SparePurchaseInvoiceDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT SpareID FROM SparePurchaseInvoiceDetail
                       WHERE SpareID IN ({spareSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    transaction: transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            // 3. SpareConsumptionDetail — column: SpareID (consistent naming)
            if (await TableExistsAsync(connection, "SpareConsumptionDetail", transaction))
            {
                var ids = await SafeQueryIdsAsync(connection,
                    $@"SELECT DISTINCT SpareID FROM SpareConsumptionDetail
                       WHERE SpareID IN ({spareSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    transaction: transaction);
                foreach (var id in ids) usedIds.Add(id);
            }

            return usedIds;
        }

        private async Task CheckItemMasterUsage(SqlConnection connection, int itemGroupId, MasterUsageResultDto result)
        {
            // Count total active items in this group
            result.TotalItemsInGroup = await SafeCountAsync(connection,
                "SELECT COUNT(*) FROM ItemMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0",
                new[] { new SqlParameter("@GroupId", itemGroupId) });

            if (result.TotalItemsInGroup == 0)
            {
                result.Message = "No active items found in this group.";
                return;
            }

            var itemSubQuery = "SELECT ItemID FROM ItemMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Stock Transactions (ItemTransactionDetail + ItemTransactionMain)
            if (await TableExistsAsync(connection, "ItemTransactionMain") && await TableExistsAsync(connection, "ItemTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ITD.ItemID)
                       FROM ItemTransactionMain ITM
                       INNER JOIN ItemTransactionDetail ITD ON ITM.TransactionID = ITD.TransactionID
                       WHERE ITD.ItemID IN ({itemSubQuery})
                         AND ISNULL(ITM.IsDeletedTransaction, 0) = 0
                         AND ITM.VoucherID <> -8",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Stock Transactions",
                        TableName = "ItemTransactionDetail",
                        Count = count,
                        Description = $"{count} item(s) are currently used in Stock Transactions. Please clear Stock Transactions first."
                    });
            }

            // 2. Job Booking (JobBookingContents - PaperID = ItemID)
            if (await TableExistsAsync(connection, "JobBookingContents"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT PaperID)
                       FROM JobBookingContents
                       WHERE PaperID IN ({itemSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Estimation / Job Booking",
                        TableName = "JobBookingContents",
                        Count = count,
                        Description = $"{count} item(s) are currently used in Estimation/Job Booking. Please clear Job Booking records first."
                    });
            }

            // 3. Job Cards (JobBookingJobCardContents - PaperID = ItemID)
            if (await TableExistsAsync(connection, "JobBookingJobCardContents"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT PaperID)
                       FROM JobBookingJobCardContents
                       WHERE PaperID IN ({itemSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Job Cards",
                        TableName = "JobBookingJobCardContents",
                        Count = count,
                        Description = $"{count} item(s) are currently used in Job Cards. Please clear Job Card records first."
                    });
            }

            // 4. Product Master (ProductMasterContents - PaperID = ItemID)
            if (await TableExistsAsync(connection, "ProductMasterContents"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT PaperID)
                       FROM ProductMasterContents
                       WHERE PaperID IN ({itemSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Product Master",
                        TableName = "ProductMasterContents",
                        Count = count,
                        Description = $"{count} item(s) are currently used in Product Master. Please clear Product Master records first."
                    });
            }

            // 5. QC Inspections
            if (await TableExistsAsync(connection, "ItemQCInspectionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ItemID)
                       FROM ItemQCInspectionDetail
                       WHERE ItemID IN ({itemSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "QC Inspections",
                        TableName = "ItemQCInspectionDetail",
                        Count = count,
                        Description = $"{count} item(s) are currently used in QC Inspections. Please clear QC data first."
                    });
            }

            // 6. Purchase Orders / Invoices
            if (await TableExistsAsync(connection, "ItemPurchaseInvoiceDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ItemID)
                       FROM ItemPurchaseInvoiceDetail
                       WHERE ItemID IN ({itemSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Purchase Invoices",
                        TableName = "ItemPurchaseInvoiceDetail",
                        Count = count,
                        Description = $"{count} item(s) are currently used in Purchase Invoices. Please clear Purchase Invoices first."
                    });
            }

            // 7. GRS Entry
            if (await TableExistsAsync(connection, "GRSItemTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ItemID)
                       FROM GRSItemTransactionDetail
                       WHERE ItemID IN ({itemSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", itemGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "GRS Entry",
                        TableName = "GRSItemTransactionDetail",
                        Count = count,
                        Description = $"{count} item(s) are currently used in GRS Entry. Please clear GRS data first."
                    });
            }

            // Calculate unused items count
            var usedItemIds = await GetUsedItemIdsAsync(connection, itemGroupId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedItemIds.Count;
        }

        private async Task CheckLedgerMasterUsage(SqlConnection connection, int ledgerGroupId, MasterUsageResultDto result)
        {
            // Count total active ledgers in this group
            if (!await TableExistsAsync(connection, "LedgerMaster")) return;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                "SELECT COUNT(*) FROM LedgerMaster WHERE LedgerGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0",
                new[] { new SqlParameter("@GroupId", ledgerGroupId) });

            if (result.TotalItemsInGroup == 0) return;

            var ledgerSubQuery = "SELECT LedgerID FROM LedgerMaster WHERE LedgerGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Invoice Transactions
            if (await TableExistsAsync(connection, "InvoiceTransactionMain"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID)
                       FROM InvoiceTransactionMain
                       WHERE LedgerID IN ({ledgerSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Invoice Transactions",
                        TableName = "InvoiceTransactionMain",
                        Count = count,
                        Description = $"{count} ledger(s) are currently used in Invoice Transactions. Please clear Invoices first."
                    });
            }

            // 2. Finish Goods
            if (await TableExistsAsync(connection, "FinishGoodsTransactionMain"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID)
                       FROM FinishGoodsTransactionMain
                       WHERE LedgerID IN ({ledgerSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Finish Goods / Despatch",
                        TableName = "FinishGoodsTransactionMain",
                        Count = count,
                        Description = $"{count} ledger(s) are currently used in Finish Goods. Please clear Finish Goods records first."
                    });
            }

            // 3. Job Booking
            if (await TableExistsAsync(connection, "JobBooking"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID)
                       FROM JobBooking
                       WHERE LedgerID IN ({ledgerSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Estimation / Job Booking",
                        TableName = "JobBooking",
                        Count = count,
                        Description = $"{count} ledger(s) are currently used in Job Booking. Please clear Job Booking records first."
                    });
            }

            // 4. Purchase Invoice
            if (await TableExistsAsync(connection, "ItemPurchaseInvoiceMain"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID)
                       FROM ItemPurchaseInvoiceMain
                       WHERE LedgerID IN ({ledgerSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Purchase Invoices",
                        TableName = "ItemPurchaseInvoiceMain",
                        Count = count,
                        Description = $"{count} ledger(s) are currently used in Purchase Invoices. Please clear Purchase Invoices first."
                    });
            }

            // 5. Sales Order
            if (await TableExistsAsync(connection, "JobOrderBooking"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID)
                       FROM JobOrderBooking
                       WHERE LedgerID IN ({ledgerSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Sales Orders / Bookings",
                        TableName = "JobOrderBooking",
                        Count = count,
                        Description = $"{count} ledger(s) are currently used in Sales Orders. Please clear Sales Orders first."
                    });
            }

            // 6. Machine & Allocation Settings
            if (await TableExistsAsync(connection, "EmployeeMachineAllocation"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID) FROM EmployeeMachineAllocation WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Machine Allocation",
                        TableName = "EmployeeMachineAllocation",
                        Count = count,
                        Description = $"{count} ledger(s) are used in Machine Allocation."
                    });
            }

            // 7. Concern Persons
            if (await TableExistsAsync(connection, "ConcernPersonMaster"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID) FROM ConcernPersonMaster WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Concern Persons",
                        TableName = "ConcernPersonMaster",
                        Count = count,
                        Description = $"{count} ledger(s) have Concern Persons linked."
                    });
            }

            // 8. Other Transaction Checks
            if (await TableExistsAsync(connection, "ItemTransactionMain"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT LedgerID) FROM ItemTransactionMain WHERE LedgerID IN ({ledgerSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Item Transactions",
                        TableName = "ItemTransactionMain",
                        Count = count,
                        Description = $"{count} ledger(s) are used in Item Transactions."
                    });
            }

            // 9. Locked Ledgers
            if (await TableExistsAsync(connection, "LedgerMaster"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(*) FROM LedgerMaster WHERE LedgerGroupID = @GroupId AND ISNULL(IsLocked, 0) = 1 AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", ledgerGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Locked Records",
                        TableName = "LedgerMaster",
                        Count = count,
                        Description = $"{count} ledger(s) are locked and cannot be deleted."
                    });
            }

            // Calculate unused ledgers count
            var usedLedgerIds = await GetUsedLedgerIdsAsync(connection, ledgerGroupId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedLedgerIds.Count;
        }

        private async Task CheckToolMasterUsage(SqlConnection connection, int toolGroupId, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "ToolMaster")) return;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                "SELECT COUNT(*) FROM ToolMaster WHERE ToolGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0",
                new[] { new SqlParameter("@GroupId", toolGroupId) });

            if (result.TotalItemsInGroup == 0) return;

            var toolSubQuery = "SELECT ToolID FROM ToolMaster WHERE ToolGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Tool Transactions
            if (await TableExistsAsync(connection, "ToolTransactionMain") && await TableExistsAsync(connection, "ToolTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT TTD.ToolID)
                       FROM ToolTransactionMain TTM
                       INNER JOIN ToolTransactionDetail TTD ON TTM.TransactionID = TTD.TransactionID
                       WHERE TTD.ToolID IN ({toolSubQuery})
                         AND ISNULL(TTM.IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", toolGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Tool Transactions",
                        TableName = "ToolTransactionDetail",
                        Count = count,
                        Description = $"{count} tool(s) are currently used in Tool Transactions. Please clear Tool Transactions first."
                    });
            }

            // 2. Tool Purchase Invoice
            if (await TableExistsAsync(connection, "ToolPurchaseInvoiceMain") && await TableExistsAsync(connection, "ToolPurchaseInvoiceDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT TPD.ToolID)
                       FROM ToolPurchaseInvoiceMain TPM
                       INNER JOIN ToolPurchaseInvoiceDetail TPD ON TPM.TransactionID = TPD.TransactionID
                       WHERE TPD.ToolID IN ({toolSubQuery})
                         AND ISNULL(TPM.IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", toolGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Tool Purchase Invoices",
                        TableName = "ToolPurchaseInvoiceDetail",
                        Count = count,
                        Description = $"{count} tool(s) are currently used in Purchase Invoices. Please clear Tool Purchase Invoices first."
                    });
            }

            // 3. Job Card Tool Allocation
            if (await TableExistsAsync(connection, "JobBookingJobCardProcessToolAllocation"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ToolID)
                       FROM JobBookingJobCardProcessToolAllocation
                       WHERE ToolID IN ({toolSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", toolGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Job Card Tool Allocation",
                        TableName = "JobBookingJobCardProcessToolAllocation",
                        Count = count,
                        Description = $"{count} tool(s) are currently used in Job Card Tool Allocation. Please clear Job Cards first."
                    });
            }

            // 4. QC Approved Transactions (From VB Code)
            if (await TableExistsAsync(connection, "ToolTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ToolID)
                       FROM ToolTransactionDetail
                       WHERE ToolID IN ({toolSubQuery})
                         AND ISNULL(QCApprovalNo, '') <> ''
                         AND (ISNULL(ApprovedQuantity, 0) > 0 OR ISNULL(RejectedQuantity, 0) > 0)
                         AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", toolGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "QC Approved Tools",
                        TableName = "ToolTransactionDetail",
                        Count = count,
                        Description = $"{count} tool(s) have QC Approval/Rejection records and cannot be deleted."
                    });
            }

            // 5. Locked Tools
            if (await TableExistsAsync(connection, "ToolMaster"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(*) FROM ToolMaster WHERE ToolGroupID = @GroupId AND ISNULL(IsLocked, 0) = 1 AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new[] { new SqlParameter("@GroupId", toolGroupId) });

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Locked Records",
                        TableName = "ToolMaster",
                        Count = count,
                        Description = $"{count} tool(s) are locked and cannot be deleted."
                    });
            }

            // Calculate unused tools count
            var usedToolIds = await GetUsedToolIdsAsync(connection, toolGroupId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedToolIds.Count;
        }

        private async Task CheckMachineMasterUsage(SqlConnection connection, int deptId, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "MachineMaster")) return;

            var whereClause = deptId > 0 ? "WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0" : "WHERE ISNULL(IsDeletedTransaction, 0) = 0";
            var parameters = deptId > 0 ? new[] { new SqlParameter("@GroupId", deptId) } : null;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                $"SELECT COUNT(*) FROM MachineMaster {whereClause}",
                parameters);

            if (result.TotalItemsInGroup == 0) return;

            var machineSubQuery = deptId > 0 
                ? "SELECT MachineID FROM MachineMaster WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT MachineID FROM MachineMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Estimation / Job Booking
            if (await TableExistsAsync(connection, "JobBookingContents"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT MachineID) FROM JobBookingContents WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Estimation / Job Booking",
                        TableName = "JobBookingContents",
                        Count = count,
                        Description = $"{count} machine(s) are currently used in Job Booking / Process."
                    });
            }

            // 2. Job Cards
            if (await TableExistsAsync(connection, "JobBookingJobCardProcess"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT MachineID) FROM JobBookingJobCardProcess WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Job Cards",
                        TableName = "JobBookingJobCardProcess",
                        Count = count,
                        Description = $"{count} machine(s) are currently used in active Job Cards."
                    });
            }

            // 3. Product Master
            if (await TableExistsAsync(connection, "ProductMasterProcess"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT MachineID) FROM ProductMasterProcess WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Product Master",
                        TableName = "ProductMasterProcess",
                        Count = count,
                        Description = $"{count} machine(s) are linked to Product Master processes."
                    });
            }

            // 4. Job Schedule
            if (await TableExistsAsync(connection, "JobScheduleReleaseMachineWise"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT MachineID) FROM JobScheduleReleaseMachineWise WHERE MachineID IN ({machineSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Job Schedule",
                        TableName = "JobScheduleReleaseMachineWise",
                        Count = count,
                        Description = $"{count} machine(s) have records in Job Schedule / Planning."
                    });
            }

            // Calculate unused machines count
            var usedMachineIds = await GetUsedMachineIdsAsync(connection, deptId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedMachineIds.Count;
        }

        private async Task CheckProcessMasterUsage(SqlConnection connection, int deptId, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "ProcessMaster")) return;

            var whereClause = deptId > 0 ? "WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0" : "WHERE ISNULL(IsDeletedTransaction, 0) = 0";
            var parameters = deptId > 0 ? new[] { new SqlParameter("@GroupId", deptId) } : null;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                $"SELECT COUNT(*) FROM ProcessMaster {whereClause}",
                parameters);

            if (result.TotalItemsInGroup == 0) return;

            var processSubQuery = deptId > 0 
                ? "SELECT ProcessID FROM ProcessMaster WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT ProcessID FROM ProcessMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Estimation / Job Booking
            if (await TableExistsAsync(connection, "JobBookingProcess"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ProcessID) FROM JobBookingProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Estimation / Job Booking",
                        TableName = "JobBookingProcess",
                        Count = count,
                        Description = $"{count} process(es) are currently used in Job Bookings."
                    });
            }

            // 2. Job Cards
            if (await TableExistsAsync(connection, "JobBookingJobCardProcess"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ProcessID) FROM JobBookingJobCardProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Job Cards",
                        TableName = "JobBookingJobCardProcess",
                        Count = count,
                        Description = $"{count} process(es) are currently used in Job Cards."
                    });
            }

            // 3. Product Master
            if (await TableExistsAsync(connection, "ProductMasterProcess"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ProcessID) FROM ProductMasterProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Product Master",
                        TableName = "ProductMasterProcess",
                        Count = count,
                        Description = $"{count} process(es) are linked to Product Masters."
                    });
            }

            // 4. Job Enquiry
            if (await TableExistsAsync(connection, "JobEnquiryProcess"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ProcessID) FROM JobEnquiryProcess WHERE ProcessID IN ({processSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Enquiry",
                        TableName = "JobEnquiryProcess",
                        Count = count,
                        Description = $"{count} process(es) are linked to Job Enquiries."
                    });
            }

            // Calculate unused processes count
            var usedProcessIds = await GetUsedProcessIdsAsync(connection, deptId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedProcessIds.Count;
        }

        private async Task CheckCategoryMasterUsage(SqlConnection connection, int groupId, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "CategoryMaster")) return;

            var whereClause = groupId > 0 ? "WHERE SegmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0" : "WHERE ISNULL(IsDeletedTransaction, 0) = 0";
            var parameters = groupId > 0 ? new[] { new SqlParameter("@GroupId", groupId) } : null;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                $"SELECT COUNT(*) FROM CategoryMaster {whereClause}",
                parameters);

            if (result.TotalItemsInGroup == 0) return;

            var categorySubQuery = groupId > 0 
                ? "SELECT CategoryID FROM CategoryMaster WHERE SegmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT CategoryID FROM CategoryMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Product Master
            if (await TableExistsAsync(connection, "ProductMaster"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT CategoryID) FROM ProductMaster WHERE CategoryID IN ({categorySubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Product Master",
                        TableName = "ProductMaster",
                        Count = count,
                        Description = $"{count} category(ies) are currently assigned to Product Masters."
                    });
            }

            // 2. Job Booking
            if (await TableExistsAsync(connection, "JobBookingContents"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT CategoryID) FROM JobBookingContents WHERE CategoryID IN ({categorySubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Job Booking",
                        TableName = "JobBookingContents",
                        Count = count,
                        Description = $"{count} category(ies) are used in Job Booking / Estimations."
                    });
            }

            // 3. Enquiry
            if (await TableExistsAsync(connection, "JobEnquiry"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT CategoryID) FROM JobEnquiry WHERE CategoryID IN ({categorySubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Enquiry",
                        TableName = "JobEnquiry",
                        Count = count,
                        Description = $"{count} category(ies) are linked to Job Enquiries."
                    });
            }

            // Calculate unused categories count
            var usedCategoryIds = await GetUsedCategoryIdsAsync(connection, groupId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedCategoryIds.Count;
        }

        private async Task CheckDepartmentMasterUsage(SqlConnection connection, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "DepartmentMaster")) return;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                "SELECT COUNT(*) FROM DepartmentMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0");

            if (result.TotalItemsInGroup == 0) return;

            // 1. Process Master
            if (await TableExistsAsync(connection, "ProcessMaster"))
            {
                var count = await SafeCountAsync(connection,
                    "SELECT COUNT(DISTINCT DepartmentID) FROM ProcessMaster WHERE ISNULL(DepartmentID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Process Master",
                        TableName = "ProcessMaster",
                        Count = count,
                        Description = $"{count} department(s) are currently used in Process Master."
                    });
            }

            // 2. Machine Master
            if (await TableExistsAsync(connection, "MachineMaster"))
            {
                var count = await SafeCountAsync(connection,
                    "SELECT COUNT(DISTINCT DepartmentID) FROM MachineMaster WHERE ISNULL(DepartmentID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Machine Master",
                        TableName = "MachineMaster",
                        Count = count,
                        Description = $"{count} department(s) are currently used in Machine Master."
                    });
            }

            // 3. Ledger Master
            if (await TableExistsAsync(connection, "LedgerMaster"))
            {
                var count = await SafeCountAsync(connection,
                    "SELECT COUNT(DISTINCT DepartmentID) FROM LedgerMaster WHERE ISNULL(DepartmentID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Ledger Master",
                        TableName = "LedgerMaster",
                        Count = count,
                        Description = $"{count} department(s) are currently used in Ledger Master."
                    });
            }

            // Calculate unused departments
            var usedDeptIds = await GetUsedDepartmentIdsAsync(connection);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedDeptIds.Count;
        }

        private async Task CheckWarehouseMasterUsage(SqlConnection connection, int unitId, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "WarehouseMaster")) return;

            var whereClause = unitId > 0 ? "WHERE ProductionUnitID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0" : "WHERE ISNULL(IsDeletedTransaction, 0) = 0";
            var parameters = unitId > 0 ? new[] { new SqlParameter("@GroupId", unitId) } : null;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                $"SELECT COUNT(*) FROM WarehouseMaster {whereClause}",
                parameters);

            if (result.TotalItemsInGroup == 0) return;

            var warehouseSubQuery = unitId > 0 
                ? "SELECT WarehouseID FROM WarehouseMaster WHERE ProductionUnitID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT WarehouseID FROM WarehouseMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Item Transactions
            if (await TableExistsAsync(connection, "ItemTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT WarehouseID) FROM ItemTransactionDetail WHERE WarehouseID IN ({warehouseSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Item Transactions",
                        TableName = "ItemTransactionDetail",
                        Count = count,
                        Description = $"{count} warehouse(s) are currently used in item transactions / stock movements."
                    });
            }

            // Calculate unused warehouses
            var usedWhIds = await GetUsedWarehouseIdsAsync(connection, unitId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedWhIds.Count;
        }

        private async Task CheckUnitMasterUsage(SqlConnection connection, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "UnitMaster")) return;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                "SELECT COUNT(*) FROM UnitMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0");

            if (result.TotalItemsInGroup == 0) return;

            // 1. Item Master
            if (await TableExistsAsync(connection, "ItemMaster"))
            {
                var count = await SafeCountAsync(connection,
                    "SELECT COUNT(DISTINCT UnitID) FROM ItemMaster WHERE ISNULL(UnitID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Item Master",
                        TableName = "ItemMaster",
                        Count = count,
                        Description = $"{count} unit(s) are used in Item Master (Stock/Purchase/Sale)."
                    });
            }

            // 2. Product Master
            if (await TableExistsAsync(connection, "ProductMaster"))
            {
                var count = await SafeCountAsync(connection,
                    "SELECT COUNT(DISTINCT UnitID) FROM ProductMaster WHERE ISNULL(UnitID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Product Master",
                        TableName = "ProductMaster",
                        Count = count,
                        Description = $"{count} unit(s) are used in Product Master."
                    });
            }

            // 3. Spare Master
            if (await TableExistsAsync(connection, "SparePartMaster"))
            {
                var count = await SafeCountAsync(connection,
                    "SELECT COUNT(DISTINCT UnitID) FROM SparePartMaster WHERE ISNULL(UnitID, 0) <> 0 AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Spare Part Master",
                        TableName = "SparePartMaster",
                        Count = count,
                        Description = $"{count} unit(s) are used in Spare Part Master."
                    });
            }

            // Calculate unused units
            var usedUnitIds = await GetUsedUnitIdsAsync(connection);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedUnitIds.Count;
        }

        private async Task CheckProductGroupMasterUsage(SqlConnection connection, int itemGroupId, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "ProductHSNMaster")) return;

            var whereClause = itemGroupId > 0 ? "WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0" : "WHERE ISNULL(IsDeletedTransaction, 0) = 0";
            var parameters = itemGroupId > 0 ? new[] { new SqlParameter("@GroupId", itemGroupId) } : null;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                $"SELECT COUNT(*) FROM ProductHSNMaster {whereClause}",
                parameters);

            if (result.TotalItemsInGroup == 0) return;

            var hsnSubQuery = itemGroupId > 0 
                ? "SELECT ProductHSNID FROM ProductHSNMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                : "SELECT ProductHSNID FROM ProductHSNMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Item/Tool Master
            if (await TableExistsAsync(connection, "ItemMaster") || await TableExistsAsync(connection, "ToolMaster"))
            {
                var count = 0;
                if (await TableExistsAsync(connection, "ItemMaster"))
                    count += await SafeCountAsync(connection, $@"SELECT COUNT(DISTINCT ProductHSNID) FROM ItemMaster WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0", parameters);
                if (await TableExistsAsync(connection, "ToolMaster"))
                    count += await SafeCountAsync(connection, $@"SELECT COUNT(DISTINCT ProductHSNID) FROM ToolMaster WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0", parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Item / Tool Master",
                        TableName = "ProductHSNMaster",
                        Count = count,
                        Description = $"{count} group(s) are used in Item or Tool masters."
                    });
            }

            // 2. Transactions
            if (await TableExistsAsync(connection, "ItemTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ProductHSNID) FROM ItemTransactionDetail WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Transactions",
                        TableName = "ItemTransactionDetail",
                        Count = count,
                        Description = $"{count} group(s) are found in item transaction records."
                    });
            }

            // 3. Product Master
            if (await TableExistsAsync(connection, "ProductMaster"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT ProductHSNID) FROM ProductMaster WHERE ProductHSNID IN ({hsnSubQuery}) AND ISNULL(IsDeletedTransaction, 0) = 0",
                    parameters);

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Product Master",
                        TableName = "ProductMaster",
                        Count = count,
                        Description = $"{count} group(s) are linked to Product Masters."
                    });
            }

            // Calculate unused
            var usedIds = await GetUsedProductGroupIdsAsync(connection, itemGroupId);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedIds.Count;
        }

        private async Task CheckSpareMasterUsage(SqlConnection connection, MasterUsageResultDto result)
        {
            if (!await TableExistsAsync(connection, "SparePartMaster")) return;

            result.TotalItemsInGroup = await SafeCountAsync(connection,
                "SELECT COUNT(*) FROM SparePartMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0");

            if (result.TotalItemsInGroup == 0) return;

            var spareSubQuery = "SELECT SparePartID FROM SparePartMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";

            // 1. Spare Transactions — confirmed column: SpareID
            if (await TableExistsAsync(connection, "SpareTransactionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT SpareID)
                       FROM SpareTransactionDetail
                       WHERE SpareID IN ({spareSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Spare Part Transactions",
                        TableName = "SpareTransactionDetail",
                        Count = count,
                        Description = $"{count} spare part(s) are currently used in Transactions. Please clear Spare Transactions first."
                    });
            }

            // 2. Spare Purchase Invoice — column: SpareID
            if (await TableExistsAsync(connection, "SparePurchaseInvoiceDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT SpareID)
                       FROM SparePurchaseInvoiceDetail
                       WHERE SpareID IN ({spareSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Spare Purchase Invoices",
                        TableName = "SparePurchaseInvoiceDetail",
                        Count = count,
                        Description = $"{count} spare part(s) are currently used in Purchase Invoices. Please clear Purchase Invoices first."
                    });
            }

            // 3. Spare Consumption — column: SpareID
            if (await TableExistsAsync(connection, "SpareConsumptionDetail"))
            {
                var count = await SafeCountAsync(connection,
                    $@"SELECT COUNT(DISTINCT SpareID)
                       FROM SpareConsumptionDetail
                       WHERE SpareID IN ({spareSubQuery})
                         AND ISNULL(IsDeletedTransaction, 0) = 0");

                if (count > 0)
                    result.Usages.Add(new UsageDetail
                    {
                        Area = "Spare Consumption",
                        TableName = "SpareConsumptionDetail",
                        Count = count,
                        Description = $"{count} spare part(s) are currently used in Consumption records. Please clear Consumption data first."
                    });
            }

            // Calculate unused spare parts count
            var usedSpareIds = await GetUsedSparePartIdsAsync(connection);
            result.UnusedItemsCount = result.TotalItemsInGroup - usedSpareIds.Count;
        }

        [HttpPost("delete-master-data")]
        public async Task<IActionResult> DeleteMasterData([FromBody] DeleteMasterDataRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ModuleName))
            {
                return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = "Module name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
            {
                return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = "Username and Reason are required." });
            }

            var connectionString = GetConnectionString();
            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = "Database connection not available." });
            }

            try
            {
                // Verify credentials
                var isValidUser = await VerifyCredentialsAsync(request.Username, request.Password);
                if (!isValidUser)
                {
                    return Unauthorized(new DeleteMasterDataResultDto { Success = false, Message = "Invalid username or password." });
                }

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Re-check usage before deleting
                var usageResult = new MasterUsageResultDto();
                var moduleName = StandardizeModuleName(request.ModuleName);

                if (moduleName.Contains("Item", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckItemMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Ledger", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckLedgerMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckToolMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Machine", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckMachineMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Process", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckProcessMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Category", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckCategoryMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Department", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckDepartmentMasterUsage(connection, usageResult);
                }
                else if (moduleName.Contains("Warehouse", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckWarehouseMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Unit", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckUnitMasterUsage(connection, usageResult);
                }
                else if (moduleName.Contains("Product Group", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckProductGroupMasterUsage(connection, request.SubModuleId, usageResult);
                }
                else if (moduleName.Contains("Spare", StringComparison.OrdinalIgnoreCase))
                {
                    await CheckSpareMasterUsage(connection, usageResult);
                }
                else
                {
                    return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = $"Unknown module type: {moduleName}" });
                }

                if (usageResult.Usages.Any(u => u.Count > 0))
                {
                    return BadRequest(new DeleteMasterDataResultDto
                    {
                        Success = false,
                        Message = "Items are still used in transactions. Clear transactions first before deleting master data."
                    });
                }

                // Perform hard delete (permanently remove records)
                int deletedCount = 0;
                using var transaction = connection.BeginTransaction();

                try
                {


                    if (moduleName.Contains("Item", StringComparison.OrdinalIgnoreCase))
                    {
                        // Delete ItemMasterDetails first (child records)
                        if (await TableExistsAsync(connection, "ItemMasterDetails", transaction))
                        {
                            var detailCmd = new SqlCommand(
                                @"DELETE FROM ItemMasterDetails WHERE ItemGroupID = @GroupId", connection, transaction);
                            detailCmd.Parameters.AddWithValue("@GroupId", request.SubModuleId);
                            await detailCmd.ExecuteNonQueryAsync();
                        }

                        // Delete from ItemMaster
                        var masterCmd = new SqlCommand(
                            @"DELETE FROM ItemMaster WHERE ItemGroupID = @GroupId", connection, transaction);
                        masterCmd.Parameters.AddWithValue("@GroupId", request.SubModuleId);
                        deletedCount = await masterCmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Ledger", StringComparison.OrdinalIgnoreCase))
                    {
                        // Delete LedgerMasterDetails first (child records)
                        if (await TableExistsAsync(connection, "LedgerMasterDetails", transaction))
                        {
                            var detailCmd = new SqlCommand(
                                @"DELETE FROM LedgerMasterDetails WHERE LedgerGroupID = @GroupId", connection, transaction);
                            detailCmd.Parameters.AddWithValue("@GroupId", request.SubModuleId);
                            await detailCmd.ExecuteNonQueryAsync();
                        }

                        var cmd = new SqlCommand(
                            @"DELETE FROM LedgerMaster WHERE LedgerGroupID = @GroupId", connection, transaction);
                        cmd.Parameters.AddWithValue("@GroupId", request.SubModuleId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                    {
                        // Delete ToolMasterDetails first (child records)
                        if (await TableExistsAsync(connection, "ToolMasterDetails", transaction))
                        {
                            var detailCmd = new SqlCommand(
                                @"DELETE FROM ToolMasterDetails WHERE ToolGroupID = @GroupId", connection, transaction);
                            detailCmd.Parameters.AddWithValue("@GroupId", request.SubModuleId);
                            await detailCmd.ExecuteNonQueryAsync();
                        }

                        var cmd = new SqlCommand(
                            @"DELETE FROM ToolMaster WHERE ToolGroupID = @GroupId", connection, transaction);
                        cmd.Parameters.AddWithValue("@GroupId", request.SubModuleId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Machine", StringComparison.OrdinalIgnoreCase))
                    {
                        var groupId = request.SubModuleId;
                        var whereClause = groupId > 0 ? "WHERE DepartmentID = @GroupId" : "WHERE 1=1";
                        var machineSubQuery = groupId > 0 ? "SELECT MachineID FROM MachineMaster WHERE DepartmentID = @GroupId" : "SELECT MachineID FROM MachineMaster";

                        // Delete child tables first
                        string[] childTables = { "MachineSlabMaster", "MachineOnlineCoatingRates", "MachineItemSubGroupAllocationMaster", "MachineToolAllocationMaster" };
                        foreach (var table in childTables)
                        {
                            if (await TableExistsAsync(connection, table, transaction))
                            {
                                var childCmd = new SqlCommand($"DELETE FROM {table} WHERE MachineID IN ({machineSubQuery})", connection, transaction);
                                if (groupId > 0) childCmd.Parameters.AddWithValue("@GroupId", groupId);
                                await childCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var cmd = new SqlCommand($"DELETE FROM MachineMaster {whereClause}", connection, transaction);
                        if (groupId > 0) cmd.Parameters.AddWithValue("@GroupId", groupId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Process", StringComparison.OrdinalIgnoreCase))
                    {
                        var groupId = request.SubModuleId;
                        var whereClause = groupId > 0 ? "WHERE DepartmentID = @GroupId" : "WHERE 1=1";
                        var processSubQuery = groupId > 0 ? "SELECT ProcessID FROM ProcessMaster WHERE DepartmentID = @GroupId" : "SELECT ProcessID FROM ProcessMaster";

                        // Delete child tables first
                        string[] childTables = { "ProcessMasterSlabs", "ProcessAllocatedMachineMaster", "ProcessAllocatedMaterialMaster", 
                                               "ProcessToolGroupAllocationMaster", "ProcessInspectionParameterMaster", "LineClearanceParameterMaster" };
                        foreach (var table in childTables)
                        {
                            if (await TableExistsAsync(connection, table, transaction))
                            {
                                var childCmd = new SqlCommand($"DELETE FROM {table} WHERE ProcessID IN ({processSubQuery})", connection, transaction);
                                if (groupId > 0) childCmd.Parameters.AddWithValue("@GroupId", groupId);
                                await childCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var cmd = new SqlCommand($"DELETE FROM ProcessMaster {whereClause}", connection, transaction);
                        if (groupId > 0) cmd.Parameters.AddWithValue("@GroupId", groupId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Category", StringComparison.OrdinalIgnoreCase))
                    {
                        var groupId = request.SubModuleId;
                        var whereClause = groupId > 0 ? "WHERE SegmentID = @GroupId" : "WHERE 1=1";
                        var categorySubQuery = groupId > 0 ? "SELECT CategoryID FROM CategoryMaster WHERE SegmentID = @GroupId" : "SELECT CategoryID FROM CategoryMaster";

                        // Delete from child tables first
                        string[] childTables = { "CategoryContentAllocationMaster", "CategoryWiseProcessAllocation", "ProcessAllocatedMaterialMaster", 
                                               "CategoryWiseMaterialParameterDetail", "CategoryWiseCOAParameterSetting" };
                        foreach (var table in childTables)
                        {
                            if (await TableExistsAsync(connection, table, transaction))
                            {
                                var childCmd = new SqlCommand($"DELETE FROM {table} WHERE CategoryID IN ({categorySubQuery})", connection, transaction);
                                if (groupId > 0) childCmd.Parameters.AddWithValue("@GroupId", groupId);
                                await childCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var cmd = new SqlCommand($"DELETE FROM CategoryMaster {whereClause}", connection, transaction);
                        if (groupId > 0) cmd.Parameters.AddWithValue("@GroupId", groupId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Department", StringComparison.OrdinalIgnoreCase))
                    {
                        var cmd = new SqlCommand("DELETE FROM DepartmentMaster", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Warehouse", StringComparison.OrdinalIgnoreCase))
                    {
                        var groupId = request.SubModuleId;
                        var whereClause = groupId > 0 ? "WHERE ProductionUnitID = @GroupId" : "WHERE 1=1";
                        var cmd = new SqlCommand($"DELETE FROM WarehouseMaster {whereClause}", connection, transaction);
                        if (groupId > 0) cmd.Parameters.AddWithValue("@GroupId", groupId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Unit", StringComparison.OrdinalIgnoreCase))
                    {
                        var cmd = new SqlCommand("DELETE FROM UnitMaster", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Product Group", StringComparison.OrdinalIgnoreCase))
                    {
                        var groupId = request.SubModuleId;
                        var whereClause = groupId > 0 ? "WHERE ItemGroupID = @GroupId" : "WHERE 1=1";
                        var cmd = new SqlCommand($"DELETE FROM ProductHSNMaster {whereClause}", connection, transaction);
                        if (groupId > 0) cmd.Parameters.AddWithValue("@GroupId", groupId);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Spare", StringComparison.OrdinalIgnoreCase))
                    {
                        // Delete child tables first
                        if (await TableExistsAsync(connection, "SparePartMasterMachineAllocation", transaction))
                        {
                            var childCmd = new SqlCommand("DELETE FROM SparePartMasterMachineAllocation", connection, transaction);
                            await childCmd.ExecuteNonQueryAsync();
                        }

                        var cmd = new SqlCommand("DELETE FROM SparePartMaster", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }


                    // Log to ActivityLog
                    try
                    {
                        if (await TableExistsAsync(connection, "ActivityLog", transaction))
                        {
                            var logCmd = new SqlCommand(
                                @"INSERT INTO ActivityLog (ActivityType, Description, PerformedBy, Timestamp)
                                  VALUES ('HARD_DELETE_MASTER_DATA', @Reason, @Username, GETDATE())", connection, transaction);
                            logCmd.Parameters.AddWithValue("@Reason", $"[HARD DELETE] [{moduleName} - GroupID:{request.SubModuleId}] {request.Reason}");
                            logCmd.Parameters.AddWithValue("@Username", request.Username);
                            await logCmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning($"Could not log to ActivityLog: {logEx.Message}");
                    }

                    transaction.Commit();

                    _logger.LogInformation($"Master data deleted: Module={moduleName}, GroupID={request.SubModuleId}, Count={deletedCount}, User={request.Username}");

                    return Ok(new DeleteMasterDataResultDto
                    {
                        Success = true,
                        Message = $"{deletedCount} record(s) have been successfully deleted.",
                        DeletedCount = deletedCount
                    });
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting master data");
                return StatusCode(500, new DeleteMasterDataResultDto { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost("delete-unused-master-data")]
        public async Task<IActionResult> DeleteUnusedMasterData([FromBody] DeleteMasterDataRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ModuleName))
            {
                return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = "Module name is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Reason))
            {
                return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = "Username and Reason are required." });
            }

            var connectionString = GetConnectionString();
            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = "Database connection not available." });
            }

            try
            {
                var isValidUser = await VerifyCredentialsAsync(request.Username, request.Password);
                if (!isValidUser)
                {
                    return Unauthorized(new DeleteMasterDataResultDto { Success = false, Message = "Invalid username or password." });
                }

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var moduleName = StandardizeModuleName(request.ModuleName);
                int deletedCount = 0;

                using var transaction = connection.BeginTransaction();

                try
                {


                    if (moduleName.Contains("Item", StringComparison.OrdinalIgnoreCase))
                    {
                        // Get all active ItemIDs in this group
                        var allIds = await SafeQueryIdsAsync(connection,
                            "SELECT ItemID FROM ItemMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0",
                            new[] { new SqlParameter("@GroupId", request.SubModuleId) }, transaction);

                        var usedIds = await GetUsedItemIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused items to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        // Delete from ItemMasterDetails first
                        if (await TableExistsAsync(connection, "ItemMasterDetails", transaction))
                        {
                            var detailCmd = new SqlCommand($"DELETE FROM ItemMasterDetails WHERE ItemID IN ({idList})", connection, transaction);
                            await detailCmd.ExecuteNonQueryAsync();
                        }

                        // Delete from ItemMaster
                        var masterCmd = new SqlCommand($"DELETE FROM ItemMaster WHERE ItemID IN ({idList})", connection, transaction);
                        deletedCount = await masterCmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Ledger", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIds = await SafeQueryIdsAsync(connection,
                            "SELECT LedgerID FROM LedgerMaster WHERE LedgerGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0",
                            new[] { new SqlParameter("@GroupId", request.SubModuleId) }, transaction);

                        var usedIds = await GetUsedLedgerIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused ledgers to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        if (await TableExistsAsync(connection, "LedgerMasterDetails", transaction))
                        {
                            var detailCmd = new SqlCommand($"DELETE FROM LedgerMasterDetails WHERE LedgerID IN ({idList})", connection, transaction);
                            await detailCmd.ExecuteNonQueryAsync();
                        }

                        var cmd = new SqlCommand($"DELETE FROM LedgerMaster WHERE LedgerID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Tool", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIds = await SafeQueryIdsAsync(connection,
                            "SELECT ToolID FROM ToolMaster WHERE ToolGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0",
                            new[] { new SqlParameter("@GroupId", request.SubModuleId) }, transaction);

                        var usedIds = await GetUsedToolIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused tools to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        if (await TableExistsAsync(connection, "ToolMasterDetails", transaction))
                        {
                            var detailCmd = new SqlCommand($"DELETE FROM ToolMasterDetails WHERE ToolID IN ({idList})", connection, transaction);
                            await detailCmd.ExecuteNonQueryAsync();
                        }

                        var cmd = new SqlCommand($"DELETE FROM ToolMaster WHERE ToolID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Machine", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIdsQuery = request.SubModuleId > 0 
                            ? "SELECT MachineID FROM MachineMaster WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                            : "SELECT MachineID FROM MachineMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";
                        
                        var idsParams = request.SubModuleId > 0 ? new[] { new SqlParameter("@GroupId", request.SubModuleId) } : null;
                        var allIds = await SafeQueryIdsAsync(connection, allIdsQuery, idsParams, transaction);

                        var usedIds = await GetUsedMachineIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused machines to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        // Delete from child tables first
                        string[] childTables = { "MachineSlabMaster", "MachineOnlineCoatingRates", "MachineItemSubGroupAllocationMaster", "MachineToolAllocationMaster" };
                        foreach (var table in childTables)
                        {
                            if (await TableExistsAsync(connection, table, transaction))
                            {
                                var childCmd = new SqlCommand($"DELETE FROM {table} WHERE MachineID IN ({idList})", connection, transaction);
                                await childCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var cmd = new SqlCommand($"DELETE FROM MachineMaster WHERE MachineID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Process", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIdsQuery = request.SubModuleId > 0 
                            ? "SELECT ProcessID FROM ProcessMaster WHERE DepartmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                            : "SELECT ProcessID FROM ProcessMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";
                        
                        var idsParams = request.SubModuleId > 0 ? new[] { new SqlParameter("@GroupId", request.SubModuleId) } : null;
                        var allIds = await SafeQueryIdsAsync(connection, allIdsQuery, idsParams, transaction);

                        var usedIds = await GetUsedProcessIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused processes to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        // Delete from child tables first
                        string[] childTables = { "ProcessMasterSlabs", "ProcessAllocatedMachineMaster", "ProcessAllocatedMaterialMaster", 
                                               "ProcessToolGroupAllocationMaster", "ProcessInspectionParameterMaster", "LineClearanceParameterMaster" };
                        foreach (var table in childTables)
                        {
                            if (await TableExistsAsync(connection, table, transaction))
                            {
                                var childCmd = new SqlCommand($"DELETE FROM {table} WHERE ProcessID IN ({idList})", connection, transaction);
                                await childCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var cmd = new SqlCommand($"DELETE FROM ProcessMaster WHERE ProcessID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Category", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIdsQuery = request.SubModuleId > 0 
                            ? "SELECT CategoryID FROM CategoryMaster WHERE SegmentID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                            : "SELECT CategoryID FROM CategoryMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";
                        
                        var idsParams = request.SubModuleId > 0 ? new[] { new SqlParameter("@GroupId", request.SubModuleId) } : null;
                        var allIds = await SafeQueryIdsAsync(connection, allIdsQuery, idsParams, transaction);

                        var usedIds = await GetUsedCategoryIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused categories to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        // Delete from child tables first
                        string[] childTables = { "CategoryContentAllocationMaster", "CategoryWiseProcessAllocation", "ProcessAllocatedMaterialMaster", 
                                               "CategoryWiseMaterialParameterDetail", "CategoryWiseCOAParameterSetting" };
                        foreach (var table in childTables)
                        {
                            if (await TableExistsAsync(connection, table, transaction))
                            {
                                var childCmd = new SqlCommand($"DELETE FROM {table} WHERE CategoryID IN ({idList})", connection, transaction);
                                await childCmd.ExecuteNonQueryAsync();
                            }
                        }

                        var cmd = new SqlCommand($"DELETE FROM CategoryMaster WHERE CategoryID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Department", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIds = await SafeQueryIdsAsync(connection, 
                            "SELECT DepartmentID FROM DepartmentMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                        var usedIds = await GetUsedDepartmentIdsAsync(connection, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused departments to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);
                        var cmd = new SqlCommand($"DELETE FROM DepartmentMaster WHERE DepartmentID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Warehouse", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIdsQuery = request.SubModuleId > 0 
                            ? "SELECT WarehouseID FROM WarehouseMaster WHERE ProductionUnitID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                            : "SELECT WarehouseID FROM WarehouseMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";
                        
                        var idsParams = request.SubModuleId > 0 ? new[] { new SqlParameter("@GroupId", request.SubModuleId) } : null;
                        var allIds = await SafeQueryIdsAsync(connection, allIdsQuery, idsParams, transaction);

                        var usedIds = await GetUsedWarehouseIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused warehouses to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);
                        var cmd = new SqlCommand($"DELETE FROM WarehouseMaster WHERE WarehouseID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Unit", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIds = await SafeQueryIdsAsync(connection, 
                            "SELECT UnitID FROM UnitMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                        var usedIds = await GetUsedUnitIdsAsync(connection, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused units to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);
                        var cmd = new SqlCommand($"DELETE FROM UnitMaster WHERE UnitID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Product Group", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIdsQuery = request.SubModuleId > 0 
                            ? "SELECT ProductHSNID FROM ProductHSNMaster WHERE ItemGroupID = @GroupId AND ISNULL(IsDeletedTransaction, 0) = 0"
                            : "SELECT ProductHSNID FROM ProductHSNMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0";
                        
                        var idsParams = request.SubModuleId > 0 ? new[] { new SqlParameter("@GroupId", request.SubModuleId) } : null;
                        var allIds = await SafeQueryIdsAsync(connection, allIdsQuery, idsParams, transaction);

                        var usedIds = await GetUsedProductGroupIdsAsync(connection, request.SubModuleId, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused product groups to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);
                        var cmd = new SqlCommand($"DELETE FROM ProductHSNMaster WHERE ProductHSNID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }
                    else if (moduleName.Contains("Spare", StringComparison.OrdinalIgnoreCase))
                    {
                        var allIds = await SafeQueryIdsAsync(connection, 
                            "SELECT SparePartID FROM SparePartMaster WHERE ISNULL(IsDeletedTransaction, 0) = 0", null, transaction);
                        var usedIds = await GetUsedSparePartIdsAsync(connection, transaction);
                        var unusedIds = allIds.Where(id => !usedIds.Contains(id)).ToList();

                        if (unusedIds.Count == 0)
                        {
                            transaction.Rollback();
                            return Ok(new DeleteMasterDataResultDto { Success = true, Message = "No unused spare parts to delete.", DeletedCount = 0 });
                        }

                        var idList = string.Join(",", unusedIds);

                        // Delete child tables first (Note: SpartPartID typo in db)
                        if (await TableExistsAsync(connection, "SparePartMasterMachineAllocation", transaction))
                        {
                            var childCmd = new SqlCommand($"DELETE FROM SparePartMasterMachineAllocation WHERE SpartPartID IN ({idList})", connection, transaction);
                            await childCmd.ExecuteNonQueryAsync();
                        }

                        var cmd = new SqlCommand($"DELETE FROM SparePartMaster WHERE SparePartID IN ({idList})", connection, transaction);
                        deletedCount = await cmd.ExecuteNonQueryAsync();
                    }

                    else
                    {
                        transaction.Rollback();
                        return BadRequest(new DeleteMasterDataResultDto { Success = false, Message = $"Unknown module type: {moduleName}" });
                    }

                    // Log to ActivityLog
                    try
                    {
                        if (await TableExistsAsync(connection, "ActivityLog", transaction))
                        {
                            var logCmd = new SqlCommand(
                                @"INSERT INTO ActivityLog (ActivityType, Description, PerformedBy, Timestamp)
                                  VALUES ('DELETE_UNUSED_MASTER_DATA', @Reason, @Username, GETDATE())", connection, transaction);
                            logCmd.Parameters.AddWithValue("@Reason", $"[DELETE UNUSED] [{moduleName} - GroupID:{request.SubModuleId}] {request.Reason}");
                            logCmd.Parameters.AddWithValue("@Username", request.Username);
                            await logCmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning($"Could not log to ActivityLog: {logEx.Message}");
                    }

                    transaction.Commit();

                    _logger.LogInformation($"Unused master data deleted: Module={moduleName}, GroupID={request.SubModuleId}, Count={deletedCount}, User={request.Username}");

                    return Ok(new DeleteMasterDataResultDto
                    {
                        Success = true,
                        Message = $"{deletedCount} unused record(s) have been successfully deleted.",
                        DeletedCount = deletedCount
                    });
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting unused master data");
                return StatusCode(500, new DeleteMasterDataResultDto { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        private async Task<int> TruncateAllTransactionTablesAsync(string username, string reason)
        {
            var connectionString = GetConnectionString();

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Connection string is empty in TruncateAllTransactionTablesAsync");
                throw new InvalidOperationException("Database connection string is not available");
            }

            int totalDeleted = 0;

            // List of all transaction tables to truncate
            var transactionTables = new List<string>
            {
                // Sales Enquiry
                "JobEnquiry", "JobEnquiryContents", "JobEnquiryLayerDetail", "JobEnquiryProcess",

                // Estimation Tables
                "JobBooking", "JobBookingAttachments", "JobBookingColorDetails", "JobBookingContents",
                "JobBookingOnetimeCharges", "JobBookingMaterialCost", "JobBookingContentBookForms",
                "JobBookingCorrugation", "JobBookingContentsLayerDetail", "JobBookingProcess",
                "JobBookingProcessMaterialRequirement", "JobBookingContentsSpecification", "JobBookingCostings",
                "JobBookingProcessMaterialParameterDetail",

                // Price Approval Tables
                "JobApprovedCost",

                // Sales Order Tables
                "JobOrderBookingDetails", "JobOrderBookingOneTimeCharges", "JobOrderBookingBatchDetails",
                "JobOrderBookingDeliveryDetails", "JobOrderBooking",

                // Product Master Tables
                "ProductMasterComplaintRegister", "ProductMasterProcessToolAllocation", "ProductMaster",
                "ProductMasterContents", "ProductMasterContentBookForms", "ProductMasterProcess",
                "ProductMasterProcessMaterialRequirement", "ProductMasterContentsSpecification",
                "ProductMasterOneTimeCharges", "ProductMasterProcessMaterialParameterDetail",
                "ProductMasterContentsLayerDetail", "ProductMasterCorrugation",

                // Job Card Tables
                "JobBookingJobCardProcessToolAllocation", "JobBookingJobCard", "JobBookingJobCardContentBookForms",
                "JobBookingJobCardBatch", "JobBookingJobCardManual", "JobBookingJobCardColorDetails",
                "JobBookingJobCardContents", "JobBookingJobCardFormWiseDetails", "JobBookingJobCardProcessMaterialParameterDetail",
                "JobBookingJobCardGang", "JobBookingJobCardContentsBookedItems", "JobBookingJobCardProcess",
                "JobBookingJobCardProcessMaterialRequirement", "JobBookingJobCardContentsSpecification",
                "JobBookingJobCardCorrugation", "JobBookingJobCardContentsLayerDetail",

                // Job Schedule Tables
                "JobScheduleRelease", "JobScheduleMachineWiseFreeSlot", "JobScheduleReleaseMachineWise",
                "JobScheduleReleaseMachineWiseTemp", "JobScheduleReleaseSequence", "JobScheduleReleaseStatus",
                "JobScheduleReleaseTemp",

                // Production Entry Tables
                "ProductionEntryProcessInspection", "ProductionEntryLineClearance", "ProductionUpdateEntry",
                "ProductionCommentsEntry", "ProductionEntryFormWise", "ProductionEntryProcessInspectionMain",
                "ProductionLineClearanceParametersentry", "ProductionEntryProcessInspectionDetail",
                "ProductionEntry", "ManualProductionDetails", "MachineCurrentStatusEntry",

                // Semi Packing Tables
                "FinishGoodsTransactionSemiPacking", "JobSemiPackingMain", "JobSemiPackingDetail",

                // Finish Goods Tables
                "FinishGoodsQCInspectionPackingDetail", "FinishGoodsTransactionTaxes",
                "FinishGoodsTransactionDetail", "FinishGoodsQCInspectionDetail", "FinishGoodsQCInspectionMain",
                "FinishGoodsTransactionMain", "DespatchFreightEntryTaxes", "DespatchFreightEntry",

                // Invoice Module Tables
                "InvoiceTransactionMain", "InvoiceTransactionTaxes", "InvoiceTransactionDetail",

                // Outsource Module Tables
                "OutsourceProductionDetails", "OutSourceProductionPurchaseInvoiceDetail", "OutsourceChallanDetails",
                "OutsourceChallanMain", "OutsourcePaymentMain", "OutsourceProductionMain",
                "OutSourceProductionPurchaseInvoiceMain", "OutSourceProductionPurchaseInvoiceTaxes",
                "OutsourcePaymentDetails",

                // Work Order Conversion Tables
                "WorkOrderConversionChallanMain", "WorkOrderConversionPurchaseInvoiceDetail",
                "WorkOrderConversionPurchaseInvoiceMain", "WorkOrderConversionPurchaseInvoiceTaxes",
                "WorkOrderConversionChallanDetails",

                // GRS Entry Tables
                "GRSItemTransactionDetail", "GRSOverheadChargesEntry", "GRSPurchaseOverheadChargesDetail",
                "GRSTransactionDetail", "GRSTransactionMain",

                // Inventory Tables
                "ItemTransactionDetail", "ItemTransactionMain", "ItemTransactionBatchDetail",
                "ItemPurchaseInvoiceDetail", "AdditionalItemRequisitionDetail", "AdditionalItemRequisitionMain",
                "ItemConsumptionMain", "ItemConsumptionDetail", "ItemQCInspectionMain", "ItemQCInspectionDetail",
                "ItemPicklistReleaseDetail", "ItemPurchaseDeliverySchedule", "ItemPurchaseInvoiceMain",
                "ItemPurchaseInvoiceTaxes", "ItemPurchaseOrderTaxes", "ItemPurchaseOverheadCharges",
                "ItemPurchaseRequisitionDetail",

                // Other Item Inventory Tables
                "OtherItemPurchaseDeliverySchedule", "OtherItemPurchaseInvoiceMain", "OtherItemPurchaseInvoiceTaxes",
                "OtherItemPurchaseOrderTaxes", "OtherItemPurchaseOverheadCharges", "OtherItemPurchaseInvoiceDetail",
                "OtherItemTransactionDetail", "OtherItemTransactionMain",

                // Tool Inventory Tables
                "ToolPurchaseOrderTaxes", "ToolPurchaseOverheadCharges", "ToolPurchaseRequisitionDetail",
                "ToolTransactionDetail", "ToolTransactionMain", "ToolPurchaseInvoiceDetail",
                "ToolPurchaseInvoiceMain", "ToolPurchaseInvoiceTaxes",

                // Spare Part Inventory Tables
                "SpareConsumptionDetail", "SpareConsumptionMain", "SparePurchaseInvoiceDetail",
                "SparePurchaseInvoiceMain", "SparePurchaseInvoiceTaxes", "SparePurchaseOrderTaxes",
                "SparePurchaseOverheadCharges", "SparePurchaseRequisitionDetail", "SpareTransactionDetail",
                "SpareTransactionMain",

                // Client Inventory Tables
                "ClientConsumptionDetail", "ClientConsumptionMain", "ClientTransactionDetail", "ClientTransactionMain",

                // Maintenance Tables
                "MaintenanceTicketMaster", "MaintenanceTicketProductionEntry", "MaintenanceWorkOrderMain",
                "MaintenanceWorkOrderServiceDetail", "MaintenanceWorkOrderTaxes", "EventScheduleMain"
            };

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Start transaction
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Log the deletion activity
                        try
                        {
                            await System.IO.File.AppendAllTextAsync("debug_log.txt",
                                $"[{DateTime.Now}] ClearAllTransactions: User '{username}' initiated truncation. Reason: {reason}\n");
                        }
                        catch { }

                        // Try to log to ActivityLog if table exists
                        try
                        {
                            var logQuery = @"
                                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ActivityLog')
                                BEGIN
                                    INSERT INTO ActivityLog (ActivityType, Description, PerformedBy, Timestamp)
                                    VALUES ('TRUNCATE_ALL_TRANSACTIONS', @Reason, @Username, GETDATE())
                                END";

                            using (var logCmd = new SqlCommand(logQuery, connection, transaction))
                            {
                                logCmd.Parameters.AddWithValue("@Reason", reason);
                                logCmd.Parameters.AddWithValue("@Username", username);
                                await logCmd.ExecuteNonQueryAsync();
                            }
                        }
                        catch (Exception logEx)
                        {
                            _logger.LogWarning($"Could not log to ActivityLog: {logEx.Message}");
                        }

                        // Truncate each table
                        foreach (var table in transactionTables)
                        {
                            try
                            {
                                // Check if table exists
                                var checkTableQuery = "SELECT COUNT(*) FROM sys.tables WHERE name = @TableName";
                                using (var checkCmd = new SqlCommand(checkTableQuery, connection, transaction))
                                {
                                    checkCmd.Parameters.AddWithValue("@TableName", table);
                                    var tableExists = (int)await checkCmd.ExecuteScalarAsync() > 0;

                                    if (!tableExists)
                                    {
                                        _logger.LogWarning($"Table {table} does not exist, skipping...");
                                        continue;
                                    }
                                }

                                // Get count before deleting
                                var countQuery = $"SELECT COUNT(*) FROM [{table}]";
                                using (var countCmd = new SqlCommand(countQuery, connection, transaction))
                                {
                                    var count = (int)await countCmd.ExecuteScalarAsync();
                                    totalDeleted += count;

                                    if (count > 0)
                                    {
                                        _logger.LogInformation($"Table {table} has {count} records");
                                    }
                                }

                                // Try TRUNCATE first (faster), fallback to DELETE if FK constraints exist
                                try
                                {
                                    var truncateQuery = $"TRUNCATE TABLE [{table}]";
                                    using (var truncateCmd = new SqlCommand(truncateQuery, connection, transaction))
                                    {
                                        await truncateCmd.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"✓ Truncated table: {table}");
                                    }
                                }
                                catch (SqlException sqlEx) when (sqlEx.Number == 4712) // Cannot truncate table because it is being referenced by FK
                                {
                                    // Use DELETE instead
                                    var deleteQuery = $"DELETE FROM [{table}]";
                                    using (var deleteCmd = new SqlCommand(deleteQuery, connection, transaction))
                                    {
                                        await deleteCmd.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"✓ Deleted from table: {table}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Could not clear table {table}: {ex.Message}");
                                await System.IO.File.AppendAllTextAsync("debug_log.txt",
                                    $"[{DateTime.Now}] Error clearing {table}: {ex.Message}\n");
                                // Continue with other tables even if one fails
                            }
                        }

                        // Commit transaction
                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            return totalDeleted;
        }
    }
}
