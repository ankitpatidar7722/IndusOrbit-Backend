using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class ItemStockService : IItemStockService
{
    private readonly SqlConnection _connection;

    public ItemStockService(SqlConnection connection)
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

    // â”€â”€â”€ Enrich: validate ItemCodes, fill ItemID/BatchNo/StockUnit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<ItemStockEnrichResult> EnrichStockRowsAsync(List<ItemStockEnrichRowDto> rows, int itemGroupId)
    {
        await EnsureOpenAsync();

        var enrichResult = new ItemStockEnrichResult();

        // Fetch all items for this group
        var itemLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT ItemID, ItemCode, ItemName, ItemGroupID, ISNULL(StockUnit, '') AS StockUnit,
                     Quality, GSM, Manufecturer, Finish, SizeW, SizeL
              FROM ItemMaster
              WHERE ItemGroupID = @GroupId
                AND ISNULL(IsDeletedTransaction, 0) = 0",
            new { GroupId = itemGroupId });

        var byCode = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in itemLookup)
        {
            string code = item.ItemCode?.ToString() ?? "";
            if (!string.IsNullOrEmpty(code) && !byCode.ContainsKey(code))
                byCode[code] = item;
        }

        var dateStr = DateTime.Now.ToString("dd-MM-yy");
        
        // â”€â”€â”€ Step: Calculate Frequencies for BatchNo Logic â”€â”€â”€
        var itemFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            string code = (row.ItemCode ?? "Unknown").Trim();
            if (!itemFrequencies.ContainsKey(code)) itemFrequencies[code] = 1;
            else itemFrequencies[code]++;
        }

        var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var enriched = new ItemStockEnrichedRow
            {
                ItemCode = row.ItemCode?.Trim(),
                ReceiptQuantity = row.ReceiptQuantity,
                LandedRate = row.LandedRate,
                WarehouseName = row.WarehouseName?.Trim(),
                BinName = row.BinName?.Trim(),
                SupplierBatchNo = row.SupplierBatchNo?.Trim(),
                SizeL = row.SizeL,
                SizeW = row.SizeW
            };

            if (string.IsNullOrWhiteSpace(row.ItemCode))
            {
                enriched.IsValid = false;
                enriched.Error = "ItemCode is empty";
                enrichResult.Rows.Add(enriched);
                continue;
            }

            if (!byCode.TryGetValue(row.ItemCode.Trim(), out var matched))
            {
                enriched.IsValid = false;
                enriched.Error = "ItemCode not found";
                enrichResult.InvalidItemCodes.Add(row.ItemCode.Trim());
                enrichResult.Rows.Add(enriched);
                continue;
            }

            enriched.ItemID = (int)matched.ItemID;
            enriched.StockUnit = !string.IsNullOrWhiteSpace(row.StockUnit)
                ? row.StockUnit
                : matched.StockUnit?.ToString() ?? "";

            // Populate view fields from master (override row values if master has data)
            enriched.ItemName = matched.ItemName?.ToString();
            enriched.Quality = matched.Quality?.ToString();
            enriched.GSM = matched.GSM is decimal g ? g : (matched.GSM is double d ? (decimal)d : (matched.GSM is int i ? (decimal)i : null));
            enriched.Manufecturer = matched.Manufecturer?.ToString();
            enriched.Finish = matched.Finish?.ToString();
            
            var mSizeL = matched.SizeL is decimal sl ? (decimal?)sl : (matched.SizeL is double dl ? (decimal?)dl : (matched.SizeL is int il ? (decimal?)il : (decimal?)null));
            if (mSizeL != null && mSizeL != 0) enriched.SizeL = mSizeL;

            var mSizeW = matched.SizeW is decimal sw ? (decimal?)sw : (matched.SizeW is double dw ? (decimal?)dw : (matched.SizeW is int iw ? (decimal?)iw : (decimal?)null));
            if (mSizeW != null && mSizeW != 0) enriched.SizeW = mSizeW;

            // â”€â”€â”€ BatchNo Generation Logic â”€â”€â”€
            if (!string.IsNullOrWhiteSpace(row.BatchNo))
            {
                enriched.BatchNo = row.BatchNo.Trim();
            }
            else
            {
                string itemCode = (matched.ItemCode?.ToString() ?? row.ItemCode ?? "NA").Trim();
                int itemId = (int)matched.ItemID;
                string freqKey = itemCode;
                int totalFreq = itemFrequencies.TryGetValue(freqKey, out int t) ? t : 1;

                if (!itemCounts.ContainsKey(freqKey)) itemCounts[freqKey] = 1;
                else itemCounts[freqKey]++;

                int currentSeq = itemCounts[freqKey];

                if (totalFreq > 1)
                    enriched.BatchNo = $"PHY_{dateStr}_{itemCode}_{itemId}_{currentSeq}";
                else
                    enriched.BatchNo = $"PHY_{dateStr}_{itemCode}_{itemId}";
            }
            
            enriched.IsValid = true;
            enrichResult.Rows.Add(enriched);
        }

        return enrichResult;
    }

    // â”€â”€â”€ Import: final save to database â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<ItemStockImportResult> ImportItemStockAsync(List<ItemStockRowDto> rows, int itemGroupId)
    {
        var result = new ItemStockImportResult { TotalRows = rows.Count };

        if (rows.Count == 0)
        {
            result.Message = "No rows to import.";
            return result;
        }

        try
        {
            await EnsureOpenAsync();

            // â”€â”€â”€ 1. Fetch items for ItemID resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var itemLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT ItemID, ItemCode, ItemGroupID, ISNULL(StockUnit, '') AS StockUnit
                  FROM ItemMaster
                  WHERE ItemGroupID = @GroupId
                    AND ISNULL(IsDeletedTransaction, 0) = 0",
                new { GroupId = itemGroupId });

            var byCode = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in itemLookup)
            {
                string code = item.ItemCode?.ToString() ?? "";
                if (!string.IsNullOrEmpty(code) && !byCode.ContainsKey(code))
                    byCode[code] = item;
            }

            // â”€â”€â”€ 2. Fetch warehouse lookup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var warehouseLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT WarehouseID, WarehouseName, BinName
                  FROM WarehouseMaster
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0");

            // Build lookup: "WarehouseName|BinName" â†’ WarehouseID
            // Trim DB values so spaces/case never cause a mismatch with trimmed input
            var whMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var wh in warehouseLookup)
            {
                string whN = (wh.WarehouseName?.ToString() ?? "").Trim();
                string bnN = (wh.BinName?.ToString() ?? "").Trim();
                string key = $"{whN}|{bnN}";
                if (!whMap.ContainsKey(key))
                    whMap[key] = (int)wh.WarehouseID;
            }

            // â”€â”€â”€ 3. Validate and resolve each row â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var validRows = new List<ItemStockRowDto>();
            var dateStr = DateTime.Now.ToString("dd-MM-yy");

            // Calculate frequencies for BatchNo logic
            var itemFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string code = (row.ItemCode ?? "Unknown").Trim();
                if (!itemFrequencies.ContainsKey(code)) itemFrequencies[code] = 1;
                else itemFrequencies[code]++;
            }
            var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.RowIndex = i + 1;

                // Validate ItemCode
                if (string.IsNullOrWhiteSpace(row.ItemCode))
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ ItemCode is empty");
                    continue;
                }

                if (!byCode.TryGetValue(row.ItemCode.Trim(), out var matched))
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ ItemCode not found: {row.ItemCode}");
                    continue;
                }

                // Validate quantity
                if (row.ReceiptQuantity <= 0)
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ Invalid ReceiptQuantity ({row.ReceiptQuantity})");
                    continue;
                }

                row.ItemID = (int)matched.ItemID;
                row.ItemGroupID = (int)matched.ItemGroupID;

                // Default StockUnit from master if not provided
                if (string.IsNullOrWhiteSpace(row.StockUnit))
                    row.StockUnit = matched.StockUnit?.ToString() ?? "";

                // Default LandedRate to 0
                if (row.LandedRate < 0) row.LandedRate = 0;

                // â”€â”€â”€ BatchNo Generation Logic (Consistent with Enrich) â”€â”€â”€
                if (string.IsNullOrWhiteSpace(row.BatchNo))
                {
                    string itemCode = (matched.ItemCode?.ToString() ?? row.ItemCode ?? "NA").Trim();
                    int itemId = (int)matched.ItemID;
                    string freqKey = itemCode;
                    int totalFreq = itemFrequencies.TryGetValue(freqKey, out int t) ? t : 1;

                    if (!itemCounts.ContainsKey(freqKey)) itemCounts[freqKey] = 1;
                    else itemCounts[freqKey]++;

                    int currentSeq = itemCounts[freqKey];

                    if (totalFreq > 1)
                        row.BatchNo = $"PHY_{dateStr}_{itemCode}_{itemId}_{currentSeq}";
                    else
                        row.BatchNo = $"PHY_{dateStr}_{itemCode}_{itemId}";
                }

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

            // â”€â”€â”€ 4. Generate Voucher Number â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            const string prefix = "PHY";
            const int voucherId = -16;
            var companyId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;
            var userId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2;
            var fYear = await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026";

            var maxVoucherNo = await _connection.ExecuteScalarAsync<long?>(
                @"SELECT ISNULL(MAX(MaxVoucherNo), 0) FROM ItemTransactionMain
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

            // â”€â”€â”€ 6. INSERT ItemTransactionMain â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var transactionId = await _connection.ExecuteScalarAsync<int>(
                @"INSERT INTO ItemTransactionMain
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

            // â”€â”€â”€ 7. Bulk INSERT ItemTransactionDetail â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var detailTable = new System.Data.DataTable();
            detailTable.Columns.Add("TransID", typeof(int));
            detailTable.Columns.Add("ItemID", typeof(int));
            detailTable.Columns.Add("ItemGroupID", typeof(int));
            detailTable.Columns.Add("ReceiptQuantity", typeof(decimal));
            detailTable.Columns.Add("NewStockQuantity", typeof(decimal));
            detailTable.Columns.Add("OldStockQuantity", typeof(decimal));
            detailTable.Columns.Add("LandedRate", typeof(decimal));
            detailTable.Columns.Add("BatchNo", typeof(string));
            detailTable.Columns.Add("SupplierBatchNo", typeof(string));
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
                    i + 1, r.ItemID, r.ItemGroupID,
                    qty, qty, qty, rate,
                    r.BatchNo ?? "", r.SupplierBatchNo ?? "", r.StockUnit ?? "", r.WarehouseID,
                    transactionId, transactionId,
                    now, now, userId, companyId, fYear, userId, userId
                );
            }

            using var bulkCopy = new SqlBulkCopy(_connection)
            {
                DestinationTableName = "ItemTransactionDetail",
                BatchSize = 1000,
                BulkCopyTimeout = 300
            };
            foreach (System.Data.DataColumn col in detailTable.Columns)
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

            await bulkCopy.WriteToServerAsync(detailTable);

            // â”€â”€â”€ 8. Update BatchID = TransactionDetailID â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            await _connection.ExecuteAsync(
                "UPDATE ItemTransactionDetail SET BatchID = TransactionDetailID WHERE TransactionID = @TxnId",
                new { TxnId = transactionId });

            // â”€â”€â”€ 9. INSERT into ItemTransactionBatchDetail â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            await _connection.ExecuteAsync(
                @"INSERT INTO ItemTransactionBatchDetail
                    (BatchID, BatchNo, SupplierBatchNo, MfgDate, ExpiryDate, CompanyID, FYear, CreatedBy, CreatedDate)
                  SELECT BatchID, BatchNo, SupplierBatchNo, MfgDate, ExpiryDate, CompanyID, FYear, CreatedBy, CreatedDate
                  FROM ItemTransactionDetail
                  WHERE CompanyID = @CompanyId
                    AND TransactionID = @TxnId
                    AND ParentTransactionID = TransactionID",
                new { CompanyId = companyId, TxnId = transactionId });

            // â”€â”€â”€ 10. Call UPDATE_ITEM_STOCK_VALUES â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            await _connection.ExecuteAsync(
                "EXEC UPDATE_ITEM_STOCK_VALUES @CompanyId, @TxnId, 0",
                new { CompanyId = companyId, TxnId = transactionId },
                commandTimeout: 120);

            // â”€â”€â”€ 11. Build result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            result.Success = true;
            result.ImportedRows = validRows.Count;
            result.Message = result.FailedRows > 0
                ? $"Stock Upload Completed. {validRows.Count} imported, {result.FailedRows} failed."
                : $"Stock Upload Completed Successfully. {validRows.Count} row(s) imported.";
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemStock Import Error: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            result.Success = false;
            result.Message = $"Stock import failed: {ex.Message}";
        }

        return result;
    }

    // â”€â”€â”€ Validate: structured validation matching Import Master pattern â”€â”€â”€â”€â”€â”€â”€
    public async Task<ItemStockValidationResult> ValidateStockRowsAsync(List<ItemStockEnrichedRow> rows, int itemGroupId)
    {
        await EnsureOpenAsync();

        var validationResult = new ItemStockValidationResult();
        validationResult.Summary.TotalRows = rows.Count;

        if (rows.Count == 0)
        {
            validationResult.IsValid = true;
            return validationResult;
        }

        // â”€â”€â”€ 1. Fetch lookup data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Fetch ItemGroup name for group-specific validations
        var itemGroupName = await _connection.QueryFirstOrDefaultAsync<string>(
            @"SELECT ItemGroupName FROM ItemGroupMaster WHERE ItemGroupID = @GroupId",
            new { GroupId = itemGroupId }) ?? "";
        var isPaperGroup = itemGroupName.Equals("PAPER", StringComparison.OrdinalIgnoreCase);

        var itemLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT ItemID, ItemCode, ItemGroupID, ISNULL(StockUnit, '') AS StockUnit
              FROM ItemMaster
              WHERE ItemGroupID = @GroupId
                AND ISNULL(IsDeletedTransaction, 0) = 0",
            new { GroupId = itemGroupId });

        var validItemCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in itemLookup)
        {
            string code = item.ItemCode?.ToString() ?? "";
            if (!string.IsNullOrEmpty(code))
                validItemCodes.Add(code);
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

        // â”€â”€â”€ 2. Duplicate detection by (ItemID, BatchNo, WarehouseName, BinName) â”€
        var compositeKeyCounts = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var itemId = r.ItemID.ToString();
            var batchNo = r.BatchNo?.Trim() ?? "";
            var whName = r.WarehouseName?.Trim() ?? "";
            var binNm = r.BinName?.Trim() ?? "";
            var compositeKey = $"{itemId}|{batchNo}|{whName}|{binNm}";
            if (!compositeKeyCounts.ContainsKey(compositeKey))
                compositeKeyCounts[compositeKey] = new List<int>();
            compositeKeyCounts[compositeKey].Add(i);
        }
        // Mark only extras as duplicate (preserve the FIRST occurrence in each group)
        var duplicateRowIndices = new HashSet<int>(
            compositeKeyCounts.Where(kv => kv.Value.Count > 1).SelectMany(kv => kv.Value.Skip(1)));

        // â”€â”€â”€ 3. Per-row validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowValidation = new ItemStockRowValidation { RowIndex = i };
            var cellIssues = new List<ItemStockCellValidation>();

            var code = row.ItemCode?.Trim() ?? "";
            var isDuplicate = duplicateRowIndices.Contains(i);

            // Mark duplicate (row also continues through Missing/Mismatch/Invalid checks)
            if (isDuplicate)
            {
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "ItemCode",
                    Status = "Duplicate",
                    ValidationMessage = $"Duplicate row: same ItemID, BatchNo, WarehouseName, BinName"
                });
            }

            // Missing: ItemCode
            if (string.IsNullOrEmpty(code))
            {
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "ItemCode",
                    Status = "MissingData",
                    ValidationMessage = "ItemCode is required"
                });
            }
            // Mismatch: ItemCode not in ItemMaster
            else if (!validItemCodes.Contains(code))
            {
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "ItemCode",
                    Status = "Mismatch",
                    ValidationMessage = $"ItemCode '{code}' not found in ItemMaster"
                });
            }

            // Missing/Invalid: ReceiptQuantity
            if (row.ReceiptQuantity <= 0)
            {
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "ReceiptQuantity",
                    Status = "MissingData",
                    ValidationMessage = "ReceiptQuantity must be greater than 0"
                });
            }
            // PAPER group: ReceiptQuantity must be a whole number (no decimals).
            // Use tolerance (< 0.001) to ignore floating-point noise from SQL SUM arithmetic
            // e.g. 110 â†’ 110.0000000000001 after SUM â€” should not be flagged as decimal.
            else if (isPaperGroup && Math.Abs(row.ReceiptQuantity - Math.Round(row.ReceiptQuantity)) >= 0.001m)
            {
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "ReceiptQuantity",
                    Status = "InvalidContent",
                    ValidationMessage = "For PAPER group, ReceiptQuantity must be a whole number (no decimal values)"
                });
            }

            // PAPER group: StockUnit must be "Sheet"
            if (isPaperGroup)
            {
                var stockUnit = row.StockUnit?.Trim() ?? "";
                if (!stockUnit.Equals("Sheet", StringComparison.OrdinalIgnoreCase))
                {
                    cellIssues.Add(new ItemStockCellValidation
                    {
                        ColumnName = "StockUnit",
                        Status = "InvalidContent",
                        ValidationMessage = "For PAPER group, StockUnit should be Sheet"
                    });
                }
            }

            // Missing: WarehouseName
            var whName = row.WarehouseName?.Trim() ?? "";
            if (string.IsNullOrEmpty(whName))
            {
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "WarehouseName",
                    Status = "MissingData",
                    ValidationMessage = "WarehouseName is required"
                });
            }
            // Mismatch: WarehouseName not in DB
            else if (!validWarehouses.Contains(whName))
            {
                cellIssues.Add(new ItemStockCellValidation
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
                cellIssues.Add(new ItemStockCellValidation
                {
                    ColumnName = "BinName",
                    Status = "MissingData",
                    ValidationMessage = "BinName is required"
                });
            }
            // Mismatch: BinName not under that Warehouse
            else if (!string.IsNullOrEmpty(whName) && validWarehouses.Contains(whName))
            {
                if (!binsByWarehouse.ContainsKey(whName) || !binsByWarehouse[whName].Contains(binName))
                {
                    cellIssues.Add(new ItemStockCellValidation
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

                // Count per-row (each category counted once per row, not per cell)
                bool hasDuplicate = cellIssues.Any(c => c.Status == "Duplicate");
                bool hasMissing = cellIssues.Any(c => c.Status == "MissingData");
                bool hasMismatch = cellIssues.Any(c => c.Status == "Mismatch");
                bool hasInvalid = cellIssues.Any(c => c.Status == "InvalidContent");

                if (hasDuplicate) validationResult.Summary.DuplicateCount++;
                if (hasMissing) validationResult.Summary.MissingDataCount++;
                if (hasMismatch) validationResult.Summary.MismatchCount++;
                if (hasInvalid) validationResult.Summary.InvalidContentCount++;

                // Set row status to the most severe issue (Duplicate > MissingData > Mismatch > Invalid)
                if (hasDuplicate)
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

    // â”€â”€â”€ Reset Item Stock: create counter-transaction issuing out all batch stock â”€
    // Matches VB.NET WebService_ItemStockReset.SaveStockResetVoucher logic:
    // 1. Query all batch-wise closing stock for the ItemGroup
    // 2. Create a new PHY transaction with IssueQuantity = ClosingQty (zeroes out stock)
    // 3. Update BatchID and call UPDATE_ITEM_STOCK_VALUES
    public async Task<ItemStockImportResult> ResetItemStockAsync(int itemGroupId, string username, string password, string reason, List<int>? itemIds = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var result = new ItemStockImportResult();

        try
        {
            await EnsureOpenAsync();

            // â”€â”€â”€ Validate credentials - Use same password encoding as login â”€â”€â”€â”€â”€â”€
            var encodedPassword = PasswordEncoder.ChangePassword(password ?? string.Empty);

            var userCheckQuery = @"
                SELECT COUNT(1)
                FROM UserMaster
                WHERE UserName = @Username
                  AND ISNULL(Password, '') = @Password
                  AND ISNULL(IsBlocked, 0) = 0";

            var isValidUser = await _connection.ExecuteScalarAsync<bool>(
                userCheckQuery,
                new { Username = username, Password = encodedPassword }
            );

            if (!isValidUser)
            {
                try
                {
                    await System.IO.File.AppendAllTextAsync(
                        "debug_log.txt",
                        $"[{DateTime.Now}] ResetItemStock Failed: Invalid credentials for user '{username}'. Reason: {reason}\n"
                    );
                }
                catch { }
                throw new UnauthorizedAccessException("Invalid username or password.");
            }

            try
            {
                var dateRangeLog = (fromDate.HasValue || toDate.HasValue)
                    ? $" DateRange: {(fromDate?.ToString("yyyy-MM-dd") ?? "ALL")} to {(toDate?.ToString("yyyy-MM-dd") ?? "ALL")}."
                    : " DateRange: ALL.";
                await System.IO.File.AppendAllTextAsync(
                    "debug_log.txt",
                    $"[{DateTime.Now}] ResetItemStock Authorized: user '{username}', ItemGroupId={itemGroupId}.{dateRangeLog} Reason: {reason}\n"
                );
            }
            catch { }

            // â”€â”€â”€ 1. Fetch all batch-wise closing stock (same query as VB.NET GetAllBatchStock) â”€
            var hasItemFilter = itemIds != null && itemIds.Count > 0;
            var itemIdFilter = hasItemFilter ? "AND IM.ItemID IN @ItemIds" : "";

            // Optional date-range filter on the transaction VoucherDate.
            // Both null => reset ALL item stock (legacy behaviour). ToDate is inclusive of the whole day.
            var dateFilter =
                "AND (@FromDate IS NULL OR ITM.VoucherDate >= @FromDate) " +
                "AND (@ToDate   IS NULL OR ITM.VoucherDate <  DATEADD(DAY, 1, @ToDate))";

            var batchStock = (await _connection.QueryAsync<dynamic>(
                $@"SELECT
                    ISNULL(IM.ItemID, 0) AS ItemID,
                    ISNULL(IM.ItemGroupID, 0) AS ItemGroupID,
                    ISNULL(ITD.WarehouseID, 0) AS WarehouseID,
                    ISNULL(ITD.ParentTransactionID, 0) AS ParentTransactionID,
                    ROUND(
                        ISNULL(SUM(ISNULL(ITD.ReceiptQuantity, 0)), 0)
                      - ISNULL(SUM(ISNULL(ITD.IssueQuantity, 0)), 0)
                      - ISNULL(SUM(ISNULL(ITD.RejectedQuantity, 0)), 0), 2
                    ) AS ClosingQty,
                    ISNULL(ITD.BatchID, 0) AS BatchID,
                    NULLIF(ITD.BatchNo, '') AS BatchNo,
                    NULLIF(IBD.SupplierBatchNo, '') AS SupplierBatchNo,
                    IBD.MfgDate,
                    IBD.ExpiryDate,
                    NULLIF(IM.StockUnit, '') AS StockUnit
                  FROM ItemMaster AS IM
                  INNER JOIN ItemTransactionDetail AS ITD
                    ON ITD.ItemID = IM.ItemID AND ITD.CompanyID = IM.CompanyID
                    AND ISNULL(ITD.IsDeletedTransaction, 0) = 0
                    AND (ISNULL(ITD.ReceiptQuantity, 0) > 0 OR ISNULL(ITD.IssueQuantity, 0) > 0)
                  INNER JOIN ItemTransactionMain AS ITM
                    ON ITM.TransactionID = ITD.TransactionID AND ITM.CompanyID = ITD.CompanyID
                    AND ITM.VoucherID NOT IN (-8, -9, -11)
                  INNER JOIN WarehouseMaster AS WM
                    ON WM.WarehouseID = ITD.WarehouseID AND WM.CompanyID = ITD.CompanyID
                  INNER JOIN ItemTransactionBatchDetail AS IBD
                    ON IBD.BatchID = ITD.BatchID AND IBD.CompanyID = ITD.CompanyID
                  WHERE ITD.CompanyID = 2
                    AND IM.ItemGroupID = @GroupId
                    {itemIdFilter}
                    {dateFilter}
                    AND ISNULL(IM.IsDeletedTransaction, 0) = 0
                  GROUP BY
                    ISNULL(IM.ItemID, 0), ISNULL(ITD.ParentTransactionID, 0),
                    ISNULL(ITD.BatchID, 0), NULLIF(ITD.BatchNo, ''),
                    NULLIF(IBD.SupplierBatchNo, ''), IBD.MfgDate, IBD.ExpiryDate,
                    ISNULL(ITD.WarehouseID, 0), ISNULL(IM.CompanyID, 0),
                    ISNULL(IM.ItemGroupID, 0), NULLIF(IM.StockUnit, '')
                  HAVING ROUND(
                    ISNULL(SUM(ISNULL(ITD.ReceiptQuantity, 0)), 0)
                  - ISNULL(SUM(ISNULL(ITD.IssueQuantity, 0)), 0)
                  - ISNULL(SUM(ISNULL(ITD.RejectedQuantity, 0)), 0), 2) > 0",
                hasItemFilter
                    ? (object)new { GroupId = itemGroupId, ItemIds = itemIds, FromDate = fromDate, ToDate = toDate }
                    : new { GroupId = itemGroupId, FromDate = fromDate, ToDate = toDate },
                commandTimeout: 120)).ToList();

            var rangeSuffix = (fromDate.HasValue || toDate.HasValue)
                ? $" for the selected date range ({(fromDate?.ToString("dd-MMM-yyyy") ?? "start")} to {(toDate?.ToString("dd-MMM-yyyy") ?? "today")})"
                : "";

            if (batchStock.Count == 0)
            {
                result.Success = true;
                result.Message = $"No item stock records found to reset{rangeSuffix}.";
                return result;
            }

            // â”€â”€â”€ 2. Generate Voucher Number â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            const string prefix = "PHY";
            const int voucherId = -16;
            var companyId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;
            var userId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin'") ?? 2;
            var fYear = await _connection.ExecuteScalarAsync<string>("SELECT TOP 1 FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026";

            var maxVoucherNo = await _connection.ExecuteScalarAsync<long?>(
                @"SELECT ISNULL(MAX(MaxVoucherNo), 0) FROM ItemTransactionMain
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0
                    AND VoucherPrefix = @Prefix AND VoucherID = @VoucherId
                    AND CompanyID = @CompanyId AND FYear = @FYear",
                new { Prefix = prefix, VoucherId = voucherId, CompanyId = companyId, FYear = fYear }) ?? 0;
            maxVoucherNo++;
            var voucherNo = $"{prefix}{maxVoucherNo:00000}";

            // â”€â”€â”€ 3. INSERT ItemTransactionMain (Particular = "Stock Reset") â”€â”€â”€â”€â”€â”€â”€
            var transactionId = await _connection.ExecuteScalarAsync<int>(
                @"INSERT INTO ItemTransactionMain
                    (TotalQuantity, Particular, Narration,
                     VoucherDate, CreatedDate, UserID, CompanyID, FYear, CreatedBy,
                     VoucherID, VoucherPrefix, MaxVoucherNo, VoucherNo)
                  VALUES
                    (0, 'Stock Reset', '',
                     GETDATE(), GETDATE(), @UserId, @CompanyId, @FYear, @UserId,
                     @VoucherId, @Prefix, @MaxNo, @VoucherNo);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    UserId = userId, CompanyId = companyId, FYear = fYear,
                    VoucherId = voucherId, Prefix = prefix,
                    MaxNo = maxVoucherNo, VoucherNo = voucherNo
                });

            // â”€â”€â”€ 4. Bulk INSERT ItemTransactionDetail with IssueQuantity = ClosingQty â”€
            var detailTable = new System.Data.DataTable();
            detailTable.Columns.Add("TransID", typeof(int));
            detailTable.Columns.Add("ItemID", typeof(int));
            detailTable.Columns.Add("ItemGroupID", typeof(int));
            detailTable.Columns.Add("IssueQuantity", typeof(decimal));
            detailTable.Columns.Add("BatchNo", typeof(string));
            detailTable.Columns.Add("BatchID", typeof(int));
            detailTable.Columns.Add("StockUnit", typeof(string));
            detailTable.Columns.Add("WarehouseID", typeof(int));
            detailTable.Columns.Add("ParentTransactionID", typeof(int));
            detailTable.Columns.Add("TransactionID", typeof(int));
            detailTable.Columns.Add("ModifiedDate", typeof(DateTime));
            detailTable.Columns.Add("CreatedDate", typeof(DateTime));
            detailTable.Columns.Add("UserID", typeof(int));
            detailTable.Columns.Add("CompanyID", typeof(int));
            detailTable.Columns.Add("FYear", typeof(string));
            detailTable.Columns.Add("CreatedBy", typeof(int));
            detailTable.Columns.Add("ModifiedBy", typeof(int));

            var now = DateTime.Now;
            for (int i = 0; i < batchStock.Count; i++)
            {
                var row = batchStock[i];
                detailTable.Rows.Add(
                    i + 1,
                    (int)row.ItemID,
                    (int)row.ItemGroupID,
                    (decimal)row.ClosingQty,         // IssueQuantity = ClosingQty (issues out all stock)
                    (string?)row.BatchNo ?? "",
                    (int)row.BatchID,
                    (string?)row.StockUnit ?? "",
                    (int)row.WarehouseID,
                    (int)row.ParentTransactionID,     // Points to original transaction
                    transactionId,
                    now, now, userId, companyId, fYear, userId, userId
                );
            }

            using var bulkCopy = new SqlBulkCopy(_connection)
            {
                DestinationTableName = "ItemTransactionDetail",
                BatchSize = 1000,
                BulkCopyTimeout = 300
            };
            foreach (System.Data.DataColumn col in detailTable.Columns)
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulkCopy.WriteToServerAsync(detailTable);

            // â”€â”€â”€ 5. Update BatchID = TransactionDetailID where BatchID = 0 â”€â”€â”€â”€â”€â”€â”€â”€
            await _connection.ExecuteAsync(
                @"UPDATE ItemTransactionDetail
                  SET BatchID = TransactionDetailID
                  WHERE TransactionID = @TxnId AND CompanyID = @CompanyId
                    AND ISNULL(BatchID, 0) = 0 AND ParentTransactionID = TransactionID",
                new { TxnId = transactionId, CompanyId = companyId });

            // â”€â”€â”€ 6. Call UPDATE_ITEM_STOCK_VALUES to recalculate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            await _connection.ExecuteAsync(
                "EXEC UPDATE_ITEM_STOCK_VALUES @CompanyId, @TxnId, 0",
                new { CompanyId = companyId, TxnId = transactionId },
                commandTimeout: 120);

            result.Success = true;
            result.ImportedRows = batchStock.Count;
            result.TotalRows = batchStock.Count;
            result.Message = $"Item Stock Reset Successful. {batchStock.Count} batch(es) zeroed out{rangeSuffix}.";
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ResetItemStock Error: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            result.Success = false;
            result.Message = $"Reset item stock failed: {ex.Message}";
        }

        return result;
    }

    // â”€â”€â”€ Reset Floor Stock: consumes all remaining floor stock â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Matches VB.NET WebService_FloorStockReset.SaveStockResetVoucher logic:
    // Floor stock = items issued to floor (VoucherID=-19) minus consumed (ItemConsumptionDetail)
    // Reset creates consumption records in ItemConsumptionMain/Detail (NOT ItemTransactionMain!)
    // VoucherPrefix='RTS', VoucherID=-25
    public async Task<ItemStockImportResult> ResetFloorStockAsync(int itemGroupId, string username, string password, string reason, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var result = new ItemStockImportResult();

        try
        {
            await EnsureOpenAsync();

            // â”€â”€â”€ Validate credentials - Use same password encoding as login â”€â”€â”€â”€â”€â”€
            var encodedPassword = PasswordEncoder.ChangePassword(password ?? string.Empty);

            var userCheckQuery = @"
                SELECT COUNT(1)
                FROM UserMaster
                WHERE UserName = @Username
                  AND ISNULL(Password, '') = @Password
                  AND ISNULL(IsBlocked, 0) = 0";

            var isValidUser = await _connection.ExecuteScalarAsync<bool>(
                userCheckQuery,
                new { Username = username, Password = encodedPassword }
            );

            if (!isValidUser)
            {
                try
                {
                    await System.IO.File.AppendAllTextAsync(
                        "debug_log.txt",
                        $"[{DateTime.Now}] ResetFloorStock Failed: Invalid credentials for user '{username}'. Reason: {reason}\n"
                    );
                }
                catch { }
                throw new UnauthorizedAccessException("Invalid username or password.");
            }

            try
            {
                var dateRangeLog = (fromDate.HasValue || toDate.HasValue)
                    ? $" DateRange: {(fromDate?.ToString("yyyy-MM-dd") ?? "ALL")} to {(toDate?.ToString("yyyy-MM-dd") ?? "ALL")}."
                    : " DateRange: ALL.";
                await System.IO.File.AppendAllTextAsync(
                    "debug_log.txt",
                    $"[{DateTime.Now}] ResetFloorStock Authorized: user '{username}', ItemGroupId={itemGroupId}.{dateRangeLog} Reason: {reason}\n"
                );
            }
            catch { }

            // â”€â”€â”€ 1. Fetch all floor stock (same query as VB.NET GetAllFloorStock) â”€
            // Floor stock = IssueQuantity (from VoucherID=-19 transactions) minus consumed
            // Consumed = SUM(ConsumeQuantity + ReturnQuantity) from ItemConsumptionDetail
            var itemGroupFilter = itemGroupId > 0
                ? "AND IM.ItemGroupID = @GroupId"
                : "";

            // Optional date-range filter on the floor-issue VoucherDate.
            // Both null => reset ALL floor stock (legacy behaviour). ToDate is inclusive of the whole day.
            var dateFilter =
                "AND (@FromDate IS NULL OR ITM.VoucherDate >= @FromDate) " +
                "AND (@ToDate   IS NULL OR ITM.VoucherDate <  DATEADD(DAY, 1, @ToDate))";

            var floorStock = (await _connection.QueryAsync<dynamic>(
                $@"SELECT
                    ISNULL(ITD.JobBookingID, 0) AS JobBookingID,
                    ISNULL(ITD.ParentTransactionID, 0) AS ParentTransactionID,
                    ISNULL(ITM.TransactionID, 0) AS TransactionID,
                    ISNULL(ITM.DepartmentID, 0) AS DepartmentID,
                    ISNULL(ITD.FloorWarehouseID, 0) AS FloorWarehouseID,
                    ISNULL(ITD.JobBookingJobCardContentsID, 0) AS JobBookingJobCardContentsID,
                    ISNULL(ITD.MachineID, 0) AS MachineID,
                    ISNULL(ITD.ItemID, 0) AS ItemID,
                    ISNULL(IM.ItemGroupID, 0) AS ItemGroupID,
                    NULLIF(IM.StockUnit, '') AS StockUnit,
                    ISNULL(ITD.BatchID, 0) AS BatchID,
                    NULLIF(ITD.BatchNo, '') AS BatchNo,
                    ISNULL(ITD.IssueQuantity, 0) AS IssueQuantity,
                    ISNULL(CS.ConsumedStock, 0) AS ConsumeQuantity,
                    ROUND((ISNULL(ITD.IssueQuantity, 0) - ISNULL(CS.ConsumedStock, 0)), 3) AS FloorStock
                  FROM ItemTransactionMain AS ITM
                  INNER JOIN ItemTransactionDetail AS ITD
                    ON ITD.TransactionID = ITM.TransactionID AND ITD.CompanyID = ITM.CompanyID
                  INNER JOIN ItemMaster AS IM
                    ON IM.ItemID = ITD.ItemID AND IM.CompanyID = ITD.CompanyID
                  INNER JOIN ItemTransactionBatchDetail AS IBD
                    ON IBD.BatchID = ITD.BatchID AND IBD.CompanyID = ITD.CompanyID
                  LEFT JOIN (
                    SELECT
                        ISNULL(ICD.IssueTransactionID, 0) AS IssueTransactionID,
                        ISNULL(ICD.CompanyID, 0) AS CompanyID,
                        ISNULL(ICD.ItemID, 0) AS ItemID,
                        ISNULL(ICD.ParentTransactionID, 0) AS ParentTransactionID,
                        ISNULL(ICD.DepartmentID, 0) AS DepartmentID,
                        ISNULL(ICD.JobBookingJobCardContentsID, 0) AS JobBookingJobCardContentsID,
                        ISNULL(ICD.BatchID, 0) AS BatchID,
                        NULLIF(ICD.BatchNo, '') AS BatchNo,
                        ROUND(SUM(ISNULL(ICD.ConsumeQuantity, 0)) + SUM(ISNULL(ICD.ReturnQuantity, 0)), 3) AS ConsumedStock
                      FROM ItemConsumptionMain AS ICM
                      INNER JOIN ItemConsumptionDetail AS ICD
                        ON ICM.ConsumptionTransactionID = ICD.ConsumptionTransactionID
                        AND ICM.CompanyID = ICD.CompanyID
                      WHERE ISNULL(ICD.IsDeletedTransaction, 0) = 0
                        AND ICD.CompanyID = 2
                      GROUP BY
                        ISNULL(ICD.IssueTransactionID, 0), ISNULL(ICD.CompanyID, 0),
                        ISNULL(ICD.ItemID, 0), ISNULL(ICD.ParentTransactionID, 0),
                        ISNULL(ICD.DepartmentID, 0), ISNULL(ICD.JobBookingJobCardContentsID, 0),
                        ISNULL(ICD.BatchID, 0), NULLIF(ICD.BatchNo, '')
                      HAVING ROUND(SUM(ISNULL(ICD.ConsumeQuantity, 0)) + SUM(ISNULL(ICD.ReturnQuantity, 0)), 3) > 0
                  ) AS CS
                    ON CS.IssueTransactionID = ITM.TransactionID
                    AND CS.ItemID = ITD.ItemID
                    AND CS.ParentTransactionID = ITD.ParentTransactionID
                    AND CS.BatchID = ITD.BatchID
                    AND CS.CompanyID = ITD.CompanyID
                  WHERE ITM.VoucherID = -19
                    {itemGroupFilter}
                    {dateFilter}
                    AND ISNULL(ITD.IsDeletedTransaction, 0) <> 1
                    AND ITD.CompanyID = 2
                    AND ROUND((ISNULL(ITD.IssueQuantity, 0) - ISNULL(CS.ConsumedStock, 0)), 3) > 0
                  ORDER BY ITM.TransactionID DESC",
                new { GroupId = itemGroupId, FromDate = fromDate, ToDate = toDate },
                commandTimeout: 120)).ToList();

            var rangeSuffix = (fromDate.HasValue || toDate.HasValue)
                ? $" for the selected date range ({(fromDate?.ToString("dd-MMM-yyyy") ?? "start")} to {(toDate?.ToString("dd-MMM-yyyy") ?? "today")})"
                : "";

            if (floorStock.Count == 0)
            {
                result.Success = true;
                result.Message = $"No floor stock records found to reset{rangeSuffix}.";
                return result;
            }

            // â”€â”€â”€ 2. Generate Voucher Number from ItemConsumptionMain â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            const string prefix = "RTS";
            const int voucherIdForMax = -16;  // Used for MaxVoucherNo lookup (matches VB.NET)
            const int voucherIdForInsert = -25;  // Actual VoucherID in the record (matches JS)
            var companyId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;
            var userId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin'") ?? 2;
            var fYear = await _connection.ExecuteScalarAsync<string>("SELECT TOP 1 FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026";

            var maxVoucherNo = await _connection.ExecuteScalarAsync<long?>(
                @"SELECT ISNULL(MAX(MaxVoucherNo), 0) FROM ItemConsumptionMain
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0
                    AND VoucherPrefix = @Prefix AND VoucherID = @VoucherId
                    AND CompanyID = @CompanyId AND FYear = @FYear",
                new { Prefix = prefix, VoucherId = voucherIdForMax, CompanyId = companyId, FYear = fYear }) ?? 0;
            maxVoucherNo++;
            var voucherNo = $"{prefix}{maxVoucherNo:00000}";

            // â”€â”€â”€ 3. INSERT into ItemConsumptionMain â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var consumptionTxnId = await _connection.ExecuteScalarAsync<int>(
                @"INSERT INTO ItemConsumptionMain
                    (VoucherID, TotalQuantity, Particular,
                     VoucherDate, CreatedDate, UserID, CompanyID, FYear, CreatedBy,
                     VoucherPrefix, MaxVoucherNo, VoucherNo)
                  VALUES
                    (@VoucherId, 0, 'Stock Reset',
                     GETDATE(), GETDATE(), @UserId, @CompanyId, @FYear, @UserId,
                     @Prefix, @MaxNo, @VoucherNo);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    VoucherId = voucherIdForInsert,
                    UserId = userId, CompanyId = companyId, FYear = fYear,
                    Prefix = prefix, MaxNo = maxVoucherNo, VoucherNo = voucherNo
                });

            // â”€â”€â”€ 4. Bulk INSERT into ItemConsumptionDetail â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // ConsumeQuantity = FloorStock, IssueQuantity = FloorStock (consumes all remaining)
            var detailTable = new System.Data.DataTable();
            detailTable.Columns.Add("TransID", typeof(int));
            detailTable.Columns.Add("ParentTransactionID", typeof(int));
            detailTable.Columns.Add("IssueTransactionID", typeof(int));
            detailTable.Columns.Add("DepartmentID", typeof(int));
            detailTable.Columns.Add("ItemID", typeof(int));
            detailTable.Columns.Add("ItemGroupID", typeof(int));
            detailTable.Columns.Add("JobBookingID", typeof(int));
            detailTable.Columns.Add("JobBookingJobCardContentsID", typeof(int));
            detailTable.Columns.Add("MachineID", typeof(int));
            detailTable.Columns.Add("ConsumeQuantity", typeof(decimal));
            detailTable.Columns.Add("IssueQuantity", typeof(decimal));
            detailTable.Columns.Add("BatchNo", typeof(string));
            detailTable.Columns.Add("BatchID", typeof(int));
            detailTable.Columns.Add("FloorWarehouseID", typeof(int));
            detailTable.Columns.Add("StockUnit", typeof(string));
            detailTable.Columns.Add("ConsumptionTransactionID", typeof(int));
            detailTable.Columns.Add("CreatedDate", typeof(DateTime));
            detailTable.Columns.Add("UserID", typeof(int));
            detailTable.Columns.Add("CompanyID", typeof(int));
            detailTable.Columns.Add("FYear", typeof(string));
            detailTable.Columns.Add("CreatedBy", typeof(int));
            detailTable.Columns.Add("IsDeletedTransaction", typeof(int));

            var now = DateTime.Now;
            for (int i = 0; i < floorStock.Count; i++)
            {
                var row = floorStock[i];
                var floorQty = Math.Round((decimal)row.FloorStock, 3);

                detailTable.Rows.Add(
                    i + 1,                                    // TransID
                    (int)row.ParentTransactionID,             // ParentTransactionID
                    (int)row.TransactionID,                   // IssueTransactionID (the floor issue txn)
                    (int)row.DepartmentID,                    // DepartmentID from original floor issue
                    (int)row.ItemID,                          // ItemID
                    (int)row.ItemGroupID,                     // ItemGroupID
                    (int)row.JobBookingID,                    // JobBookingID
                    (int)row.JobBookingJobCardContentsID,     // JobBookingJobCardContentsID
                    (int)row.MachineID,                       // MachineID
                    floorQty,                                 // ConsumeQuantity = FloorStock
                    floorQty,                                 // IssueQuantity = FloorStock
                    (string?)row.BatchNo ?? "",               // BatchNo
                    (int)row.BatchID,                         // BatchID
                    (int)row.FloorWarehouseID,                // FloorWarehouseID
                    (string?)row.StockUnit ?? "",             // StockUnit
                    consumptionTxnId,                         // ConsumptionTransactionID
                    now, userId, companyId, fYear, userId,
                    0                                         // IsDeletedTransaction = 0 (active record)
                );
            }

            using var bulkCopy = new SqlBulkCopy(_connection)
            {
                DestinationTableName = "ItemConsumptionDetail",
                BatchSize = 1000,
                BulkCopyTimeout = 300
            };
            foreach (System.Data.DataColumn col in detailTable.Columns)
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulkCopy.WriteToServerAsync(detailTable);

            // â”€â”€â”€ 5. Update BatchID = ConsumptionTransactionDetailID where BatchID = 0 â”€
            await _connection.ExecuteAsync(
                @"UPDATE ItemConsumptionDetail
                  SET BatchID = ConsumptionTransactionDetailID
                  WHERE ConsumptionTransactionID = @TxnId AND CompanyID = @CompanyId
                    AND ISNULL(BatchID, 0) = 0 AND ParentTransactionID = ConsumptionTransactionID",
                new { TxnId = consumptionTxnId, CompanyId = companyId });

            // â”€â”€â”€ 6. Call UPDATE_ITEM_STOCK_VALUES to recalculate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            await _connection.ExecuteAsync(
                "EXEC UPDATE_ITEM_STOCK_VALUES @CompanyId, @TxnId, 0",
                new { CompanyId = companyId, TxnId = consumptionTxnId },
                commandTimeout: 120);

            result.Success = true;
            result.ImportedRows = floorStock.Count;
            result.TotalRows = floorStock.Count;
            result.Message = $"Floor Stock Reset Successful. {floorStock.Count} floor stock record(s) consumed{rangeSuffix}.";
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ResetFloorStock Error: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            result.Success = false;
            result.Message = $"Reset floor stock failed: {ex.Message}";
        }

        return result;
    }

    // â”€â”€â”€ Load Stock: fetch existing stock data from DB â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Returns actual batch-wise closing stock (ReceiptQuantity - IssueQuantity - RejectedQuantity > 0)
    public async Task<List<ItemStockEnrichedRow>> GetStockDataAsync(int itemGroupId)
    {
        await EnsureOpenAsync();

        var rows = await _connection.QueryAsync<ItemStockEnrichedRow>(
            @"SELECT
                IM.ItemCode,
                MAX(NULLIF(IM.ItemName, '')) AS ItemName,
                ISNULL(IM.ItemID, 0) AS ItemID,
                ROUND(
                    ISNULL(SUM(ISNULL(ITD.ReceiptQuantity, 0)), 0)
                  - ISNULL(SUM(ISNULL(ITD.IssueQuantity, 0)), 0)
                  - ISNULL(SUM(ISNULL(ITD.RejectedQuantity, 0)), 0), 0
                ) AS ReceiptQuantity,
                MAX(ISNULL(ITD.LandedRate, 0)) AS LandedRate,
                MAX(NULLIF(ITD.BatchNo, '')) AS BatchNo,
                MAX(NULLIF(IBD.SupplierBatchNo, '')) AS SupplierBatchNo,
                MAX(NULLIF(IM.StockUnit, '')) AS StockUnit,
                MAX(NULLIF(WM.WarehouseName, '')) AS WarehouseName,
                MAX(NULLIF(WM.BinName, '')) AS BinName,
                MAX(NULLIF(IM.Quality, '')) AS Quality,
                MAX(ISNULL(IM.GSM, 0)) AS GSM,
                MAX(NULLIF(IM.Manufecturer, '')) AS Manufecturer,
                MAX(NULLIF(IM.Finish, '')) AS Finish,
                MAX(ISNULL(IM.SizeL, 0)) AS SizeL,
                MAX(ISNULL(IM.SizeW, 0)) AS SizeW,
                CAST(1 AS BIT) AS IsValid
              FROM ItemMaster AS IM
              INNER JOIN ItemTransactionDetail AS ITD
                ON ITD.ItemID = IM.ItemID
                AND ITD.CompanyID = IM.CompanyID
                AND ISNULL(ITD.IsDeletedTransaction, 0) = 0
                AND (ISNULL(ITD.ReceiptQuantity, 0) > 0 OR ISNULL(ITD.IssueQuantity, 0) > 0)
              INNER JOIN ItemTransactionMain AS ITM
                ON ITM.TransactionID = ITD.TransactionID
                AND ITM.CompanyID = ITD.CompanyID
              LEFT JOIN ItemTransactionBatchDetail AS IBD
                ON IBD.BatchID = ITD.BatchID
                AND IBD.CompanyID = ITD.CompanyID
                AND ITM.VoucherID NOT IN (-8, -9, -11)
              INNER JOIN WarehouseMaster AS WM
                ON WM.WarehouseID = ITD.WarehouseID
                AND WM.CompanyID = ITD.CompanyID
              WHERE ITD.CompanyID = 2
                AND IM.ItemGroupID = @GroupId
                AND ISNULL(IM.IsDeletedTransaction, 0) = 0
              GROUP BY
                ISNULL(IM.ItemID, 0),
                IM.ItemCode,
                ISNULL(ITD.ParentTransactionID, 0),
                ISNULL(ITD.BatchID, 0),
                ISNULL(ITD.WarehouseID, 0)
              HAVING ROUND(
                ISNULL(SUM(ISNULL(ITD.ReceiptQuantity, 0)), 0)
              - ISNULL(SUM(ISNULL(ITD.IssueQuantity, 0)), 0)
              - ISNULL(SUM(ISNULL(ITD.RejectedQuantity, 0)), 0), 2) > 0
              ORDER BY ISNULL(ITD.ParentTransactionID, 0)",
            new { GroupId = itemGroupId },
            commandTimeout: 120);

        return rows.ToList();
    }

    // â”€â”€â”€ Load Master Data: fetch all items for a group to use as template â”€â”€â”€
    public async Task<List<ItemStockEnrichedRow>> GetMasterDataAsync(int itemGroupId)
    {
        await EnsureOpenAsync();

        var query = @"
            SELECT 
                ItemCode,
                ItemID,
                ItemName,
                Quality,
                GSM,
                Manufecturer,
                Finish,
                SizeW,
                SizeL,
                ISNULL(StockUnit, '') AS StockUnit,
                CAST(1 AS BIT) AS IsValid
            FROM ItemMaster
            WHERE ItemGroupID = @GroupId
              AND ISNULL(IsDeletedTransaction, 0) = 0
            ORDER BY ItemCode";

        var result = await _connection.QueryAsync<ItemStockEnrichedRow>(query, new { GroupId = itemGroupId });
        return result.ToList();
    }
}


