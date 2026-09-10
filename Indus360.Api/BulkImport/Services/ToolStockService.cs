using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class ToolStockService : IToolStockService
{
    private readonly SqlConnection _connection;

    public ToolStockService(SqlConnection connection)
    {
        _connection = connection;
    }

    private async Task EnsureOpenAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();
    }

    // â”€â”€â”€ Warehouse List â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<List<WarehouseDto>> GetWarehousesAsync()
    {
        await EnsureOpenAsync();
        var result = await _connection.QueryAsync<WarehouseDto>(
            @"SELECT DISTINCT WarehouseName
              FROM WarehouseMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0
              ORDER BY WarehouseName");
        return result.ToList();
    }

    // â”€â”€â”€ Bins by Warehouse â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<List<WarehouseDto>> GetBinsByWarehouseAsync(string warehouseName)
    {
        await EnsureOpenAsync();
        var result = await _connection.QueryAsync<WarehouseDto>(
            @"SELECT WarehouseID, WarehouseName, BinName
              FROM WarehouseMaster
              WHERE UPPER(LTRIM(RTRIM(WarehouseName))) = UPPER(LTRIM(RTRIM(@Name)))
                AND ISNULL(IsDeletedTransaction, 0) = 0
              ORDER BY BinName",
            new { Name = warehouseName?.Trim() });
        return result.ToList();
    }

    // â”€â”€â”€ Enrich: validate ToolNames/ToolGroupNames, fill ToolID/ToolGroupID/BatchNo/StockUnit â”€
    public async Task<ToolStockEnrichResult> EnrichStockRowsAsync(List<ToolStockEnrichRowDto> rows)
    {
        await EnsureOpenAsync();

        var enrichResult = new ToolStockEnrichResult();

        // Fetch all active tools with their group info
        var toolLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT t.ToolID, t.ToolName, t.ToolCode, t.ToolGroupID,
                     tg.ToolGroupName,
                     ISNULL(t.StockUnit, '') AS StockUnit,
                     ISNULL(t.PurchaseRate, 0) AS PurchaseRate
              FROM ToolMaster t
              LEFT JOIN ToolGroupMaster tg ON t.ToolGroupID = tg.ToolGroupID
              WHERE ISNULL(t.IsDeletedTransaction, 0) = 0");

        // Build lookups
        var byGroupAndName = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
        var byGroupAndCode = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in toolLookup)
        {
            string groupName = tool.ToolGroupName?.ToString() ?? "";
            string toolName = tool.ToolName?.ToString() ?? "";
            string toolCode = tool.ToolCode?.ToString() ?? "";

            if (!string.IsNullOrEmpty(toolName))
            {
                string key = $"{groupName}|{toolName}";
                if (!byGroupAndName.ContainsKey(key))
                    byGroupAndName[key] = tool;
            }

            if (!string.IsNullOrEmpty(toolCode))
            {
                string key = $"{groupName}|{toolCode}";
                if (!byGroupAndCode.ContainsKey(key))
                    byGroupAndCode[key] = tool;
            }
        }

        // Build tool group lookup
        var toolGroupLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT ToolGroupID, ToolGroupName
              FROM ToolGroupMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");
        var groupByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in toolGroupLookup)
        {
            string gName = g.ToolGroupName?.ToString() ?? "";
            if (!string.IsNullOrEmpty(gName) && !groupByName.ContainsKey(gName))
                groupByName[gName] = (int)g.ToolGroupID;
        }
        // Build warehouse lookup
        var warehouseLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT WarehouseID, WarehouseName, BinName
              FROM WarehouseMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");
        var whMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var wh in warehouseLookup)
        {
            string whName = (wh.WarehouseName?.ToString() ?? "").Trim();
            string binName = (wh.BinName?.ToString() ?? "").Trim();
            string key = $"{whName}|{binName}";
            if (!whMap.ContainsKey(key))
                whMap[key] = (int)wh.WarehouseID;
        }

        var dateStr = DateTime.Now.ToString("dd-MM-yy");

        foreach (var row in rows)
        {
            var enriched = new ToolStockEnrichedRow
            {
                ToolGroupName = row.ToolGroupName?.Trim(),
                ToolCode = row.ToolCode?.Trim(),
                ToolName = row.ToolName?.Trim(),
                ReceiptQuantity = row.ReceiptQuantity,
                LandedRate = row.LandedRate,
                WarehouseName = row.WarehouseName?.Trim(),
                BinName = row.BinName?.Trim(),
                SupplierBatchNo = row.SupplierBatchNo?.Trim()
            };

            // Validation: ToolCode is now mandatory
            if (string.IsNullOrWhiteSpace(row.ToolCode))
            {
                enriched.IsValid = false;
                enriched.Error = "ToolCode is missing";
                enrichResult.Rows.Add(enriched);
                continue;
            }

            // Validate ToolGroup
            if (!groupByName.TryGetValue(row.ToolGroupName.Trim(), out int toolGroupId))
            {
                enriched.IsValid = false;
                enriched.Error = "ToolGroupName not found";
                enrichResult.InvalidToolGroupNames.Add(row.ToolGroupName.Trim());
                enrichResult.Rows.Add(enriched);
                continue;
            }

            // Validate Tool (Exclusively by Code)
            var matched = (dynamic?)null;
            string codeKey = $"{row.ToolGroupName.Trim()}|{row.ToolCode.Trim()}";
            byGroupAndCode.TryGetValue(codeKey, out matched);

            if (matched == null)
            {
                enriched.IsValid = false;
                enriched.Error = "ToolCode are not Exist in database";
                enrichResult.InvalidToolNames.Add(row.ToolCode.Trim());
                enrichResult.Rows.Add(enriched);
                continue;
            }

            enriched.ToolID = (int)matched.ToolID;
            enriched.ToolGroupID = toolGroupId;
            enriched.ToolCode = matched.ToolCode?.ToString() ?? row.ToolCode;
            enriched.ToolName = matched.ToolName?.ToString();
            enriched.StockUnit = !string.IsNullOrWhiteSpace(row.StockUnit)
                ? row.StockUnit
                : matched.StockUnit?.ToString() ?? "";

            // Use rate from Excel as is, no fallback to master

            if (!string.IsNullOrWhiteSpace(row.WarehouseName))
            {
                string whName = row.WarehouseName.Trim();
                string binName = row.BinName?.Trim() ?? "";
                string whKey = $"{whName}|{binName}";
                
                if (whMap.TryGetValue(whKey, out int whId))
                {
                    enriched.WarehouseID = whId;
                }
                else if (!string.IsNullOrEmpty(binName))
                {
                    // Fallback to warehouse name only if specific bin not found
                    if (whMap.TryGetValue($"{whName}|", out int whIdFallback))
                        enriched.WarehouseID = whIdFallback;
                }
            }

            enriched.IsValid = true;
            enrichResult.Rows.Add(enriched);
        }

        // â”€â”€â”€ Step: Calculate Frequencies for BatchNo Logic â”€â”€â”€
        var frequencies = enrichResult.Rows
            .Where(r => r.IsValid)
            .GroupBy(r => r.ToolID)
            .ToDictionary(g => g.Key, g => g.Count());

        var toolCounts = new Dictionary<int, int>();
        foreach (var enriched in enrichResult.Rows.Where(r => r.IsValid))
        {
            if (string.IsNullOrWhiteSpace(enriched.BatchNo))
            {
                int toolId = enriched.ToolID;
                string toolCode = (enriched.ToolCode ?? "").Trim();
                if (string.IsNullOrEmpty(toolCode)) toolCode = "NA";

                if (!toolCounts.ContainsKey(toolId)) toolCounts[toolId] = 1;
                else toolCounts[toolId]++;

                int seq = toolCounts[toolId];
                int total = frequencies[toolId];

                if (total > 1)
                    enriched.BatchNo = $"PPH_{dateStr}_{toolCode}_{seq}";
                else
                    enriched.BatchNo = $"PPH_{dateStr}_{toolCode}";
            }
        }

        return enrichResult;
    }

    // â”€â”€â”€ Import: final save to database â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<ToolStockImportResult> ImportToolStockAsync(List<ToolStockRowDto> rows)
    {
        var result = new ToolStockImportResult { TotalRows = rows.Count };

        if (rows.Count == 0)
        {
            result.Message = "No rows to import.";
            return result;
        }

        try
        {
            await EnsureOpenAsync();

            // â”€â”€â”€ 1. Fetch tools for ToolID resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var toolLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT t.ToolID, t.ToolName, t.ToolCode, t.ToolGroupID,
                         tg.ToolGroupName,
                         ISNULL(t.StockUnit, '') AS StockUnit,
                         ISNULL(t.PurchaseRate, 0) AS PurchaseRate
                  FROM ToolMaster t
                  LEFT JOIN ToolGroupMaster tg ON t.ToolGroupID = tg.ToolGroupID
                  WHERE ISNULL(t.IsDeletedTransaction, 0) = 0");

            // Build lookups
            var byGroupAndName = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            var byGroupAndCode = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in toolLookup)
            {
                string groupName = tool.ToolGroupName?.ToString() ?? "";
                string toolName = tool.ToolName?.ToString() ?? "";
                string toolCode = tool.ToolCode?.ToString() ?? "";

                if (!string.IsNullOrEmpty(toolName))
                {
                    string key = $"{groupName}|{toolName}";
                    if (!byGroupAndName.ContainsKey(key))
                        byGroupAndName[key] = tool;
                }

                if (!string.IsNullOrEmpty(toolCode))
                {
                    string key = $"{groupName}|{toolCode}";
                    if (!byGroupAndCode.ContainsKey(key))
                        byGroupAndCode[key] = tool;
                }
            }

            // Tool group lookup
            var toolGroupLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT ToolGroupID, ToolGroupName
                  FROM ToolGroupMaster
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0");
            var groupByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in toolGroupLookup)
            {
                string gName = g.ToolGroupName?.ToString() ?? "";
                if (!string.IsNullOrEmpty(gName) && !groupByName.ContainsKey(gName))
                    groupByName[gName] = (int)g.ToolGroupID;
            }

            // â”€â”€â”€ 2. Fetch warehouse lookup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var warehouseLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT WarehouseID, WarehouseName, BinName
                  FROM WarehouseMaster
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0");

            var whMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var wh in warehouseLookup)
            {
                string whName = (wh.WarehouseName?.ToString() ?? "").Trim();
                string binName = (wh.BinName?.ToString() ?? "").Trim();
                string key = $"{whName}|{binName}";
                if (!whMap.ContainsKey(key))
                    whMap[key] = (int)wh.WarehouseID;
            }

            // â”€â”€â”€ 3. Validate and resolve each row â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var validRows = new List<ToolStockRowDto>();
            var dateStr = DateTime.Now.ToString("dd-MM-yy");

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.RowIndex = i + 1;

                // Validation: ToolCode is mandatory
                if (string.IsNullOrWhiteSpace(row.ToolCode))
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ ToolCode is missing");
                    continue;
                }

                if (!groupByName.TryGetValue(row.ToolGroupName.Trim(), out int toolGroupId))
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ ToolGroupName not found: {row.ToolGroupName}");
                    continue;
                }

                // Validate Tool (Exclusively by Code)
                var matched = (dynamic?)null;
                string codeKey = $"{row.ToolGroupName.Trim()}|{row.ToolCode.Trim()}";
                byGroupAndCode.TryGetValue(codeKey, out matched);

                if (matched == null)
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ ToolCode are not Exist in database: {row.ToolCode}");
                    continue;
                }

                if (row.ReceiptQuantity <= 0)
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ Invalid ReceiptQuantity ({row.ReceiptQuantity})");
                    continue;
                }

                row.ToolID = (int)matched.ToolID;
                row.ToolGroupID = toolGroupId;

                if (string.IsNullOrWhiteSpace(row.StockUnit))
                    row.StockUnit = matched.StockUnit?.ToString() ?? "";

                // Use rate from Excel as is, no fallback to master

                // Resolve WarehouseID â€” must find a valid match; never insert 0
                string whInputName = row.WarehouseName?.Trim() ?? "";
                string whInputBin  = row.BinName?.Trim() ?? "";
                if (string.IsNullOrEmpty(whInputName))
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ WarehouseName is required.");
                    continue;
                }

                string whKey = $"{whInputName}|{whInputBin}";
                if (whMap.TryGetValue(whKey, out int whId))
                    row.WarehouseID = whId;
                else if (whMap.TryGetValue($"{whInputName}|", out int whIdFallback))
                    row.WarehouseID = whIdFallback;   // warehouse has no bins
                else
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ Warehouse '{row.WarehouseName}' / Bin '{row.BinName}' not found in WarehouseMaster. Check for extra spaces or spelling.");
                    continue;
                }

                validRows.Add(row);
            }

            if (validRows.Count == 0)
            {
                result.Message = $"No valid rows to import. {result.FailedRows} row(s) failed.";
                return result;
            }

            // â”€â”€â”€ Step: Calculate Frequencies for BatchNo Logic â”€â”€â”€
            var frequencies = validRows
                .GroupBy(r => r.ToolID)
                .ToDictionary(g => g.Key, g => g.Count());

            var toolCounts = new Dictionary<int, int>();
            foreach (var row in validRows)
            {
                if (string.IsNullOrWhiteSpace(row.BatchNo))
                {
                    int toolId = row.ToolID;
                    string toolCode = (row.ToolCode ?? "").Trim();
                    if (string.IsNullOrEmpty(toolCode)) toolCode = "NA";

                    if (!toolCounts.ContainsKey(toolId)) toolCounts[toolId] = 1;
                    else toolCounts[toolId]++;

                    int seq = toolCounts[toolId];
                    int total = frequencies[toolId];

                    if (total > 1)
                        row.BatchNo = $"PPH_{dateStr}_{toolCode}_{seq}";
                    else
                        row.BatchNo = $"PPH_{dateStr}_{toolCode}";
                }
            }

            // â”€â”€â”€ 4. Generate Voucher Number â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            const string prefix = "PPH";
            const int voucherId = -41;
            var companyId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;
            var userId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2;
            var fYear = await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026";

            var maxVoucherNo = await _connection.ExecuteScalarAsync<long?>(
                @"SELECT ISNULL(MAX(MaxVoucherNo), 0) FROM ToolTransactionMain
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0
                    AND VoucherPrefix = @Prefix
                    AND VoucherID = @VoucherId
                    AND CompanyID = @CompanyId
                    AND FYear = @FYear",
                new { Prefix = prefix, VoucherId = voucherId, CompanyId = companyId, FYear = fYear }) ?? 0;

            maxVoucherNo++;
            var voucherNo = $"{prefix}{maxVoucherNo:00000}";

            // â”€â”€â”€ 5. Calculate totals â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            decimal totalQuantity = validRows.Sum(r => Math.Round(r.ReceiptQuantity, 2));

            // â”€â”€â”€ 6. INSERT ToolTransactionMain â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var transactionId = await _connection.ExecuteScalarAsync<int>(
                @"INSERT INTO ToolTransactionMain
                    (TotalQuantity, Particular, Narration,
                     VoucherDate, CreatedDate, UserID, CompanyID, FYear, CreatedBy,
                     VoucherID, VoucherPrefix, MaxVoucherNo, VoucherNo)
                  VALUES
                    (@TotalQty, 'Stock Verification', '',
                     GETDATE(), GETDATE(), @UserId, @CompanyId, @FYear, @UserId,
                     @VoucherId, @Prefix, @MaxNo, @VoucherNo);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    TotalQty = totalQuantity,
                    UserId = userId,
                    CompanyId = companyId,
                    FYear = fYear,
                    VoucherId = voucherId,
                    Prefix = prefix,
                    MaxNo = maxVoucherNo,
                    VoucherNo = voucherNo
                });

            // â”€â”€â”€ 7. Bulk INSERT ToolTransactionDetail â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var detailTable = new System.Data.DataTable();
            detailTable.Columns.Add("TransID", typeof(int));
            detailTable.Columns.Add("ToolID", typeof(int));
            detailTable.Columns.Add("ToolGroupID", typeof(int));
            detailTable.Columns.Add("RequiredQuantity", typeof(decimal));
            detailTable.Columns.Add("PurchaseOrderQuantity", typeof(decimal));
            detailTable.Columns.Add("ReceiptQuantity", typeof(decimal));
            detailTable.Columns.Add("IssueQuantity", typeof(decimal));
            detailTable.Columns.Add("NewStockQuantity", typeof(decimal));
            detailTable.Columns.Add("LandedRate", typeof(decimal));
            detailTable.Columns.Add("BatchNo", typeof(string));
            detailTable.Columns.Add("StockUnit", typeof(string));
            detailTable.Columns.Add("WarehouseID", typeof(int));
            detailTable.Columns.Add("TransactionID", typeof(int));
            detailTable.Columns.Add("ParentTransactionID", typeof(int));
            detailTable.Columns.Add("ModifiedDate", typeof(DateTime));
            detailTable.Columns.Add("CreatedDate", typeof(DateTime));
            detailTable.Columns.Add("UserID", typeof(int));
            detailTable.Columns.Add("CompanyID", typeof(int));
            detailTable.Columns.Add("FYear", typeof(string));
            detailTable.Columns.Add("CreatedBy", typeof(int));
            detailTable.Columns.Add("ModifiedBy", typeof(int));

            var now = DateTime.Now;
            for (int i = 0; i < validRows.Count; i++)
            {
                var r = validRows[i];
                var qty = Math.Round(r.ReceiptQuantity, 2);
                var rate = Math.Round(r.LandedRate, 3);

                detailTable.Rows.Add(
                    i + 1,              // TransID
                    r.ToolID,           // ToolID
                    r.ToolGroupID,      // ToolGroupID
                    qty,                // RequiredQuantity
                    0m,                 // PurchaseOrderQuantity
                    qty,                // ReceiptQuantity
                    0m,                 // IssueQuantity
                    qty,                // NewStockQuantity
                    rate,               // LandedRate
                    r.BatchNo ?? "",    // BatchNo
                    r.StockUnit ?? "",  // StockUnit
                    r.WarehouseID,      // WarehouseID
                    transactionId,      // TransactionID
                    transactionId,      // ParentTransactionID
                    now, now,           // ModifiedDate, CreatedDate
                    userId, companyId, fYear, userId, userId
                );
            }

            using var bulkCopy = new SqlBulkCopy(_connection)
            {
                DestinationTableName = "ToolTransactionDetail",
                BatchSize = 1000,
                BulkCopyTimeout = 300
            };
            foreach (System.Data.DataColumn col in detailTable.Columns)
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

            await bulkCopy.WriteToServerAsync(detailTable);

            // â”€â”€â”€ 8. Build result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            result.Success = true;
            result.ImportedRows = validRows.Count;
            result.Message = result.FailedRows > 0
                ? $"Stock Upload Completed. {validRows.Count} imported, {result.FailedRows} failed."
                : $"Stock Upload Completed Successfully. {validRows.Count} row(s) imported.";
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ToolStock Import Error: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            result.Success = false;
            result.Message = $"Stock import failed: {ex.Message}";
        }

        return result;
    }

    // â”€â”€â”€ Validate: structured validation matching Import Master pattern â”€â”€â”€â”€â”€â”€â”€
    public async Task<ToolStockValidationResult> ValidateStockRowsAsync(List<ToolStockEnrichedRow> rows)
    {
        await EnsureOpenAsync();

        var validationResult = new ToolStockValidationResult();
        validationResult.Summary.TotalRows = rows.Count;

        if (rows.Count == 0)
        {
            validationResult.IsValid = true;
            return validationResult;
        }

        // â”€â”€â”€ 1. Fetch lookup data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var toolLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT t.ToolID, t.ToolName, t.ToolGroupID, tg.ToolGroupName
              FROM ToolMaster t
              LEFT JOIN ToolGroupMaster tg ON t.ToolGroupID = tg.ToolGroupID
              WHERE ISNULL(t.IsDeletedTransaction, 0) = 0");

        var validToolCodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in toolLookup)
        {
            string groupName = tool.ToolGroupName?.ToString() ?? "";
            string toolCode = tool.ToolCode?.ToString() ?? "";
            if (!string.IsNullOrEmpty(toolCode))
                validToolCodeKeys.Add($"{groupName}|{toolCode}");
        }

        // Tool group names
        var toolGroupLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT ToolGroupID, ToolGroupName
              FROM ToolGroupMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");
        var validGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in toolGroupLookup)
        {
            string gName = g.ToolGroupName?.ToString() ?? "";
            if (!string.IsNullOrEmpty(gName))
                validGroupNames.Add(gName);
        }

        // Fetch distinct warehouse names
        var warehouseNames = await _connection.QueryAsync<string>(
            @"SELECT DISTINCT LTRIM(RTRIM(WarehouseName)) AS WarehouseName
              FROM WarehouseMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");
        var validWarehouses = new HashSet<string>(warehouseNames, StringComparer.OrdinalIgnoreCase);

        // Fetch warehouse â†’ bin mapping
        var warehouseBins = await _connection.QueryAsync<dynamic>(
            @"SELECT LTRIM(RTRIM(WarehouseName)) AS WarehouseName, LTRIM(RTRIM(BinName)) AS BinName
              FROM WarehouseMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");
        var binsByWarehouse = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var wb in warehouseBins)
        {
            string wName = wb.WarehouseName?.ToString()?.Trim() ?? "";
            string bName = wb.BinName?.ToString()?.Trim() ?? "";
            if (!string.IsNullOrEmpty(wName))
            {
                if (!binsByWarehouse.ContainsKey(wName))
                    binsByWarehouse[wName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(bName))
                    binsByWarehouse[wName].Add(bName);
            }
        }

        // â”€â”€â”€ 2. Duplicate detection by (ToolID, BatchNo, WarehouseName, BinName) â”€
        var compositeKeyCounts = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var toolId = r.ToolID.ToString();
            var batchNo = r.BatchNo?.Trim() ?? "";
            var whName = r.WarehouseName?.Trim() ?? "";
            var binNm = r.BinName?.Trim() ?? "";
            var compositeKey = $"{toolId}|{batchNo}|{whName}|{binNm}";
            if (!compositeKeyCounts.ContainsKey(compositeKey))
                compositeKeyCounts[compositeKey] = new List<int>();
            compositeKeyCounts[compositeKey].Add(i);
        }
        var duplicateRowIndices = new HashSet<int>(
            compositeKeyCounts.Where(kv => kv.Value.Count > 1).SelectMany(kv => kv.Value.Skip(1)));

        // â”€â”€â”€ 3. Per-row validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowValidation = new ToolStockRowValidation { RowIndex = i };
            var cellIssues = new List<ToolStockCellValidation>();

            var toolGroupName = row.ToolGroupName?.Trim() ?? "";
            var toolName = row.ToolName?.Trim() ?? "";
            var isDuplicate = duplicateRowIndices.Contains(i);

            if (isDuplicate)
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "ToolCode",
                    Status = "Duplicate",
                    ValidationMessage = "Duplicate row: same ToolID, BatchNo, WarehouseName, BinName"
                });
            }

            // Missing/Invalid: ToolGroupName
            if (string.IsNullOrEmpty(toolGroupName))
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "ToolGroupName",
                    Status = "MissingData",
                    ValidationMessage = "ToolGroupName is required"
                });
            }
            else if (!validGroupNames.Contains(toolGroupName))
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "ToolGroupName",
                    Status = "Mismatch",
                    ValidationMessage = $"ToolGroupName '{toolGroupName}' not found in ToolGroupMaster"
                });
            }

            // Missing/Invalid: ToolCode
            var toolCode = row.ToolCode?.Trim() ?? "";
            if (string.IsNullOrEmpty(toolCode))
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "ToolCode",
                    Status = "MissingData",
                    ValidationMessage = "ToolCode is mandatory"
                });
            }
            else if (!string.IsNullOrEmpty(toolGroupName) && validGroupNames.Contains(toolGroupName))
            {
                string codeKey = $"{toolGroupName}|{toolCode}";
                if (!validToolCodeKeys.Contains(codeKey))
                {
                    cellIssues.Add(new ToolStockCellValidation
                    {
                        ColumnName = "ToolCode",
                        Status = "Mismatch",
                        ValidationMessage = "ToolCode are not Exist in database"
                    });
                }
            }

            // Missing/Invalid: ReceiptQuantity
            if (row.ReceiptQuantity <= 0)
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "ReceiptQuantity",
                    Status = "MissingData",
                    ValidationMessage = "ReceiptQuantity must be greater than 0"
                });
            }

            // Missing: WarehouseName
            var whName = row.WarehouseName?.Trim() ?? "";
            if (string.IsNullOrEmpty(whName))
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "WarehouseName",
                    Status = "MissingData",
                    ValidationMessage = "WarehouseName is required"
                });
            }
            else if (!validWarehouses.Contains(whName))
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "WarehouseName",
                    Status = "Mismatch",
                    ValidationMessage = $"WarehouseName '{whName}' not found in WarehouseMaster"
                });
            }

            // Missing: BinName
            var binName = row.BinName?.Trim() ?? "";
            if (string.IsNullOrEmpty(binName))
            {
                cellIssues.Add(new ToolStockCellValidation
                {
                    ColumnName = "BinName",
                    Status = "MissingData",
                    ValidationMessage = "BinName is required"
                });
            }
            else if (!string.IsNullOrEmpty(whName) && validWarehouses.Contains(whName))
            {
                if (!binsByWarehouse.ContainsKey(whName) || !binsByWarehouse[whName].Contains(binName))
                {
                    cellIssues.Add(new ToolStockCellValidation
                    {
                        ColumnName = "BinName",
                        Status = "Mismatch",
                        ValidationMessage = $"BinName '{binName}' not found under Warehouse '{whName}'"
                    });
                }
            }

            // Set row status based on cell issues
            if (cellIssues.Count > 0)
            {
                rowValidation.CellValidations = cellIssues;

                bool hasDuplicate2 = cellIssues.Any(c => c.Status == "Duplicate");
                bool hasMissing = cellIssues.Any(c => c.Status == "MissingData");
                bool hasMismatch = cellIssues.Any(c => c.Status == "Mismatch");
                bool hasInvalid = cellIssues.Any(c => c.Status == "InvalidContent");

                if (hasDuplicate2) validationResult.Summary.DuplicateCount++;
                if (hasMissing) validationResult.Summary.MissingDataCount++;
                if (hasMismatch) validationResult.Summary.MismatchCount++;
                if (hasInvalid) validationResult.Summary.InvalidContentCount++;

                if (hasDuplicate2)
                    rowValidation.RowStatus = "Duplicate";
                else if (hasMissing)
                    rowValidation.RowStatus = "MissingData";
                else if (hasMismatch)
                    rowValidation.RowStatus = "Mismatch";
                else if (hasInvalid)
                    rowValidation.RowStatus = "InvalidContent";
            }
            else
            {
                rowValidation.RowStatus = "Valid";
                validationResult.Summary.ValidRows++;
            }

            validationResult.Rows.Add(rowValidation);
        }

        // â”€â”€â”€ 4. Final summary â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        validationResult.IsValid = validationResult.Summary.DuplicateCount == 0
            && validationResult.Summary.MissingDataCount == 0
            && validationResult.Summary.MismatchCount == 0
            && validationResult.Summary.InvalidContentCount == 0;

        return validationResult;
    }

    // â”€â”€â”€ Load Stock: fetch existing tool stock data from DB â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Returns actual batch-wise closing stock (ReceiptQuantity - IssueQuantity > 0)
    public async Task<List<ToolStockEnrichedRow>> GetStockDataAsync(int toolGroupId)
    {
        await EnsureOpenAsync();

        var rows = await _connection.QueryAsync<ToolStockEnrichedRow>(
            @"SELECT
                MAX(tg.ToolGroupName) AS ToolGroupName,
                MAX(TM.ToolCode) AS ToolCode,
                MAX(TM.ToolName) AS ToolName,
                ISNULL(TM.ToolID, 0) AS ToolID,
                ISNULL(TM.ToolGroupID, 0) AS ToolGroupID,
                ROUND(
                    ISNULL(SUM(ISNULL(TTD.ReceiptQuantity, 0)), 0)
                  - ISNULL(SUM(ISNULL(TTD.IssueQuantity, 0)), 0), 2
                ) AS ReceiptQuantity,
                MAX(ISNULL(TTD.LandedRate, 0)) AS LandedRate,
                MAX(NULLIF(TTD.BatchNo, '')) AS BatchNo,
                MAX(NULLIF(TM.StockUnit, '')) AS StockUnit,
                MAX(NULLIF(WM.WarehouseName, '')) AS WarehouseName,
                MAX(NULLIF(WM.BinName, '')) AS BinName,
                CAST(1 AS BIT) AS IsValid
              FROM ToolMaster AS TM
              INNER JOIN ToolTransactionDetail AS TTD
                ON TTD.ToolID = TM.ToolID
                AND TTD.CompanyID = TM.CompanyID
                AND ISNULL(TTD.IsDeletedTransaction, 0) = 0
                AND (ISNULL(TTD.ReceiptQuantity, 0) > 0 OR ISNULL(TTD.IssueQuantity, 0) > 0)
              INNER JOIN ToolTransactionMain AS TTM
                ON TTM.TransactionID = TTD.TransactionID
                AND TTM.CompanyID = TTD.CompanyID
                AND TTM.VoucherID NOT IN (-8, -9, -11)
              INNER JOIN WarehouseMaster AS WM
                ON WM.WarehouseID = TTD.WarehouseID
                AND WM.CompanyID = TTD.CompanyID
              LEFT JOIN ToolGroupMaster AS tg
                ON tg.ToolGroupID = TM.ToolGroupID
              WHERE TTD.CompanyID = 2
                AND TM.ToolGroupID = @ToolGroupId
                AND ISNULL(TM.IsDeletedTransaction, 0) = 0
              GROUP BY
                ISNULL(TM.ToolID, 0),
                ISNULL(TM.ToolGroupID, 0),
                ISNULL(TTD.ParentTransactionID, 0),
                ISNULL(TTD.WarehouseID, 0)
              HAVING ROUND(
                ISNULL(SUM(ISNULL(TTD.ReceiptQuantity, 0)), 0)
              - ISNULL(SUM(ISNULL(TTD.IssueQuantity, 0)), 0), 2) > 0
              ORDER BY ISNULL(TTD.ParentTransactionID, 0)",
            new { ToolGroupId = toolGroupId },
            commandTimeout: 120);

        return rows.ToList();
    }

    // â”€â”€â”€ Load Master Data: fetch all tools for a group to use as template â”€â”€â”€
    public async Task<List<ToolStockEnrichedRow>> GetMasterDataAsync(int toolGroupId)
    {
        await EnsureOpenAsync();

        var query = @"
            SELECT 
                tg.ToolGroupName,
                t.ToolCode,
                t.ToolName,
                t.ToolID,
                t.ToolGroupID,
                0 AS LandedRate,
                ISNULL(t.StockUnit, '') AS StockUnit,
                CAST(1 AS BIT) AS IsValid
            FROM ToolMaster t
            LEFT JOIN ToolGroupMaster tg ON t.ToolGroupID = tg.ToolGroupID
            WHERE t.ToolGroupID = @GroupId
              AND ISNULL(t.IsDeletedTransaction, 0) = 0
            ORDER BY t.ToolName";

        var result = await _connection.QueryAsync<ToolStockEnrichedRow>(query, new { GroupId = toolGroupId });
        return result.ToList();
    }
}


