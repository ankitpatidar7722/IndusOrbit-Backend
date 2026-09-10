using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class SparePartMasterStockService : ISparePartMasterStockService
{
    private readonly SqlConnection _connection;

    public SparePartMasterStockService(SqlConnection connection)
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

    // â”€â”€â”€ Enrich: validate SparePartNames, fill SpareID/BatchNo/StockUnit â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<SparePartStockEnrichResult> EnrichStockRowsAsync(List<SparePartStockEnrichRowDto> rows)
    {
        await EnsureOpenAsync();

        var enrichResult = new SparePartStockEnrichResult();

        // Fetch all active spare parts
        var spareLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT SparePartID, SparePartCode, SparePartName, ISNULL(Unit, '') AS Unit, ISNULL(Rate, 0) AS Rate
              FROM SparePartMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");

        var byName = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

        foreach (var spare in spareLookup)
        {
            string name = spare.SparePartName?.ToString() ?? "";
            if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
                byName[name] = spare;

            string code = spare.SparePartCode?.ToString() ?? "";
            if (!string.IsNullOrEmpty(code) && !byCode.ContainsKey(code))
                byCode[code] = spare;
        }

        var dateStr = DateTime.Now.ToString("dd-MM-yy");
        
        // â”€â”€â”€ Step: Calculate Frequencies for BatchNo Logic â”€â”€â”€
        // We need to know if a spare part appears multiple times to decide if we add _1, _2...
        var spareFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            string codeOrName = (row.SparePartCode ?? row.SparePartName ?? "Unknown").Trim();
            if (!spareFrequencies.ContainsKey(codeOrName)) spareFrequencies[codeOrName] = 1;
            else spareFrequencies[codeOrName]++;
        }

        var spareCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var enriched = new SparePartStockEnrichedRow
            {
                SparePartCode = row.SparePartCode?.Trim(),
                SparePartName = row.SparePartName?.Trim(),
                ReceiptQuantity = row.ReceiptQuantity,
                LandedRate = row.LandedRate,
                WarehouseName = row.WarehouseName?.Trim(),
                BinName = row.BinName?.Trim(),
                SupplierBatchNo = row.SupplierBatchNo?.Trim()
            };

            // Validation: SparePartCode is now mandatory
            if (string.IsNullOrWhiteSpace(row.SparePartCode))
            {
                enriched.IsValid = false;
                enriched.Error = "SparePartCode is missing";
                enrichResult.InvalidSparePartNames.Add("MISSING_CODE");
                enrichResult.Rows.Add(enriched);
                continue;
            }

            dynamic? matched = null;
            byCode.TryGetValue(row.SparePartCode.Trim(), out matched);

            if (matched == null)
            {
                enriched.IsValid = false;
                enriched.Error = "SparePartCode are not Exist in Master";
                enrichResult.InvalidSparePartNames.Add(row.SparePartCode.Trim());
                enrichResult.Rows.Add(enriched);
                continue;
            }

            enriched.SpareID = (int)matched.SparePartID;
            enriched.SparePartCode = matched.SparePartCode?.ToString() ?? row.SparePartCode;
            enriched.SparePartName = matched.SparePartName?.ToString();
            enriched.StockUnit = !string.IsNullOrWhiteSpace(row.StockUnit)
                ? row.StockUnit
                : matched.Unit?.ToString() ?? "";

            // LandedRate will be used as provided in Excel (no fallback to master)

            // â”€â”€â”€ BatchNo Generation Logic â”€â”€â”€
            if (!string.IsNullOrWhiteSpace(row.BatchNo))
            {
                enriched.BatchNo = row.BatchNo.Trim();
            }
            else
            {
                string spareCode = enriched.SparePartCode ?? "NA";
                string freqKey = (row.SparePartCode ?? row.SparePartName ?? "Unknown").Trim();
                int totalFreq = spareFrequencies.TryGetValue(freqKey, out int t) ? t : 1;

                if (!spareCounts.ContainsKey(freqKey)) spareCounts[freqKey] = 1;
                else spareCounts[freqKey]++;

                int currentSeq = spareCounts[freqKey];

                if (totalFreq > 1)
                    enriched.BatchNo = $"PHY_{dateStr}_{spareCode}_{currentSeq}";
                else
                    enriched.BatchNo = $"PHY_{dateStr}_{spareCode}";
            }

            enriched.IsValid = true;

            enrichResult.Rows.Add(enriched);
        }

        return enrichResult;
    }

    // â”€â”€â”€ Import: final save to database â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public async Task<SparePartStockImportResult> ImportSparePartStockAsync(List<SparePartStockRowDto> rows)
    {
        var result = new SparePartStockImportResult { TotalRows = rows.Count };

        if (rows.Count == 0)
        {
            result.Message = "No rows to import.";
            return result;
        }

        try
        {
            await EnsureOpenAsync();

            // â”€â”€â”€ 1. Fetch spare parts for SparePartID resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var spareLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT SparePartID, SparePartCode, SparePartName, ISNULL(Unit, '') AS Unit, ISNULL(Rate, 0) AS Rate
                  FROM SparePartMaster
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0");

            var byName = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            var byCode = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

            foreach (var spare in spareLookup)
            {
                string name = spare.SparePartName?.ToString() ?? "";
                if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
                    byName[name] = spare;

                string code = spare.SparePartCode?.ToString() ?? "";
                if (!string.IsNullOrEmpty(code) && !byCode.ContainsKey(code))
                    byCode[code] = spare;
            }


            // â”€â”€â”€ 2. Fetch warehouse lookup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var warehouseLookup = await _connection.QueryAsync<dynamic>(
                @"SELECT WarehouseID, WarehouseName, BinName
                  FROM WarehouseMaster
                  WHERE ISNULL(IsDeletedTransaction, 0) = 0");

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
            var validRows = new List<SparePartStockRowDto>();
            var dateStr = DateTime.Now.ToString("dd-MM-yy");

            // Calculate frequencies for BatchNo logic
            var spareFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string codeOrName = (row.SparePartCode ?? row.SparePartName ?? "Unknown").Trim();
                if (!spareFrequencies.ContainsKey(codeOrName)) spareFrequencies[codeOrName] = 1;
                else spareFrequencies[codeOrName]++;
            }
            var spareCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.RowIndex = i + 1;

                // Validation: SparePartCode is mandatory
                if (string.IsNullOrWhiteSpace(row.SparePartCode))
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ SparePartCode is missing");
                    continue;
                }

                dynamic? matched = null;
                byCode.TryGetValue(row.SparePartCode.Trim(), out matched);

                if (matched == null)
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ SparePartCode are not Exist in Master: {row.SparePartCode}");
                    continue;
                }

                if (row.ReceiptQuantity <= 0)
                {
                    result.FailedRows++;
                    result.ErrorMessages.Add($"Row {row.RowIndex} â†’ Invalid ReceiptQuantity ({row.ReceiptQuantity})");
                    continue;
                }

                row.SpareID = (int)matched.SparePartID;
                row.SpareGroupID = (int)matched.SparePartID; // SpareGroupID = SparePartID (legacy pattern)

                if (string.IsNullOrWhiteSpace(row.StockUnit))
                    row.StockUnit = matched.Unit?.ToString() ?? "";

                if (row.LandedRate < 0) row.LandedRate = 0;

                // â”€â”€â”€ BatchNo Generation Logic â”€â”€â”€
                if (string.IsNullOrWhiteSpace(row.BatchNo))
                {
                    string spareCode = (matched.SparePartCode?.ToString() ?? row.SparePartCode ?? "NA").Trim();
                    string freqKey = (row.SparePartCode ?? row.SparePartName ?? "Unknown").Trim();
                    int totalFreq = spareFrequencies.TryGetValue(freqKey, out int t) ? t : 1;

                    if (!spareCounts.ContainsKey(freqKey)) spareCounts[freqKey] = 1;
                    else spareCounts[freqKey]++;

                    int currentSeq = spareCounts[freqKey];

                    if (totalFreq > 1)
                        row.BatchNo = $"PHY_{dateStr}_{spareCode}_{currentSeq}";
                    else
                        row.BatchNo = $"PHY_{dateStr}_{spareCode}";
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
            const int voucherId = -118;
            var companyId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2;
            var userId = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2;
            var fYear = await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026";

            var maxVoucherNo = await _connection.ExecuteScalarAsync<long?>(
                @"SELECT ISNULL(MAX(MaxVoucherNo), 0) FROM SpareTransactionMain
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

            // â”€â”€â”€ 6. INSERT SpareTransactionMain â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var transactionId = await _connection.ExecuteScalarAsync<int>(
                @"INSERT INTO SpareTransactionMain
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

            // â”€â”€â”€ 7. Bulk INSERT SpareTransactionDetail â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var detailTable = new System.Data.DataTable();
            detailTable.Columns.Add("TransID", typeof(int));
            detailTable.Columns.Add("SpareID", typeof(int));
            detailTable.Columns.Add("SpareGroupID", typeof(int));
            detailTable.Columns.Add("ReceiptQuantity", typeof(decimal));
            detailTable.Columns.Add("NewStockQuantity", typeof(decimal));
            detailTable.Columns.Add("OldStockQuantity", typeof(decimal));
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
                    i + 1, r.SpareID, r.SpareGroupID,
                    qty, qty, qty, rate,
                    r.BatchNo ?? "", r.StockUnit ?? "", r.WarehouseID,
                    transactionId, transactionId,
                    now, now, userId, companyId, fYear, userId, userId
                );
            }

            using var bulkCopy = new SqlBulkCopy(_connection)
            {
                DestinationTableName = "SpareTransactionDetail",
                BatchSize = 1000,
                BulkCopyTimeout = 300
            };
            foreach (System.Data.DataColumn col in detailTable.Columns)
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

            await bulkCopy.WriteToServerAsync(detailTable);

            // â”€â”€â”€ 8. Build result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // NOTE: No UPDATE_ITEM_STOCK_VALUES call for spare parts
            // NOTE: No ItemTransactionBatchDetail insert for spare parts
            result.Success = true;
            result.ImportedRows = validRows.Count;
            result.Message = result.FailedRows > 0
                ? $"Stock Upload Completed. {validRows.Count} imported, {result.FailedRows} failed."
                : $"Stock Upload Completed Successfully. {validRows.Count} row(s) imported.";
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] SparePartStock Import Error: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            result.Success = false;
            result.Message = $"Stock import failed: {ex.Message}";
        }

        return result;
    }

    // â”€â”€â”€ Validate: structured validation matching Import Master pattern â”€â”€â”€â”€â”€â”€â”€
    public async Task<SparePartStockValidationResult> ValidateStockRowsAsync(List<SparePartStockEnrichedRow> rows)
    {
        await EnsureOpenAsync();

        var validationResult = new SparePartStockValidationResult();
        validationResult.Summary.TotalRows = rows.Count;

        if (rows.Count == 0)
        {
            validationResult.IsValid = true;
            return validationResult;
        }

        // â”€â”€â”€ 1. Fetch lookup data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var spareLookup = await _connection.QueryAsync<dynamic>(
            @"SELECT SparePartCode, ISNULL(Unit, '') AS Unit
              FROM SparePartMaster
              WHERE ISNULL(IsDeletedTransaction, 0) = 0");

        var validSpareCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spare in spareLookup)
        {
            string code = spare.SparePartCode?.ToString() ?? "";
            if (!string.IsNullOrEmpty(code))
                validSpareCodes.Add(code.Trim());
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

        // â”€â”€â”€ 2. Duplicate detection by (SpareID, BatchNo, WarehouseName, BinName) â”€
        var compositeKeyCounts = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var spareId = r.SpareID.ToString();
            var batchNo = r.BatchNo?.Trim() ?? "";
            var whName = r.WarehouseName?.Trim() ?? "";
            var binNm = r.BinName?.Trim() ?? "";
            var compositeKey = $"{spareId}|{batchNo}|{whName}|{binNm}";
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
            var rowValidation = new SparePartStockRowValidation { RowIndex = i };
            var cellIssues = new List<SparePartStockCellValidation>();

            var code = row.SparePartCode?.Trim() ?? "";
            var isDuplicate = duplicateRowIndices.Contains(i);

            if (isDuplicate)
            {
                cellIssues.Add(new SparePartStockCellValidation
                {
                    ColumnName = "SparePartCode",
                    Status = "Duplicate",
                    ValidationMessage = "Duplicate row: same SpareID, BatchNo, WarehouseName, BinName"
                });
            }

            // Missing: SparePartCode
            if (string.IsNullOrEmpty(code))
            {
                cellIssues.Add(new SparePartStockCellValidation
                {
                    ColumnName = "SparePartCode",
                    Status = "MissingData",
                    ValidationMessage = "SparePartCode is mandatory"
                });
            }
            else if (!validSpareCodes.Contains(code))
            {
                cellIssues.Add(new SparePartStockCellValidation
                {
                    ColumnName = "SparePartCode",
                    Status = "Mismatch",
                    ValidationMessage = "SparePartCode are not Exist in Master"
                });
            }

            // Missing/Invalid: ReceiptQuantity
            if (row.ReceiptQuantity <= 0)
            {
                cellIssues.Add(new SparePartStockCellValidation
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
                cellIssues.Add(new SparePartStockCellValidation
                {
                    ColumnName = "WarehouseName",
                    Status = "MissingData",
                    ValidationMessage = "WarehouseName is required"
                });
            }
            else if (!validWarehouses.Contains(whName))
            {
                cellIssues.Add(new SparePartStockCellValidation
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
                cellIssues.Add(new SparePartStockCellValidation
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
                    cellIssues.Add(new SparePartStockCellValidation
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

    // â”€â”€â”€ Load Stock: fetch existing spare part stock data from DB â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Returns actual batch-wise closing stock (ReceiptQuantity - IssueQuantity > 0)
    public async Task<List<SparePartStockEnrichedRow>> GetStockDataAsync()
    {
        await EnsureOpenAsync();

        var rows = await _connection.QueryAsync<SparePartStockEnrichedRow>(
            @"SELECT
                MAX(SPM.SparePartCode) AS SparePartCode,
                MAX(SPM.SparePartName) AS SparePartName,
                ISNULL(SPM.SparePartID, 0) AS SpareID,
                ROUND(
                    ISNULL(SUM(ISNULL(STD.ReceiptQuantity, 0)), 0)
                  - ISNULL(SUM(ISNULL(STD.IssueQuantity, 0)), 0), 2
                ) AS ReceiptQuantity,
                MAX(ISNULL(STD.LandedRate, 0)) AS LandedRate,
                MAX(NULLIF(STD.BatchNo, '')) AS BatchNo,
                MAX(NULLIF(SPM.Unit, '')) AS StockUnit,
                MAX(NULLIF(WM.WarehouseName, '')) AS WarehouseName,
                MAX(NULLIF(WM.BinName, '')) AS BinName,
                CAST(1 AS BIT) AS IsValid
              FROM SparePartMaster AS SPM
              INNER JOIN SpareTransactionDetail AS STD
                ON STD.SpareID = SPM.SparePartID
                AND STD.CompanyID = SPM.CompanyID
                AND ISNULL(STD.IsDeletedTransaction, 0) = 0
                AND (ISNULL(STD.ReceiptQuantity, 0) > 0 OR ISNULL(STD.IssueQuantity, 0) > 0)
              INNER JOIN SpareTransactionMain AS STM
                ON STM.TransactionID = STD.TransactionID
                AND STM.CompanyID = STD.CompanyID
                AND STM.VoucherID NOT IN (-8, -9, -11)
              INNER JOIN WarehouseMaster AS WM
                ON WM.WarehouseID = STD.WarehouseID
                AND WM.CompanyID = STD.CompanyID
              WHERE STD.CompanyID = 2
                AND ISNULL(SPM.IsDeletedTransaction, 0) = 0
              GROUP BY
                ISNULL(SPM.SparePartID, 0),
                ISNULL(STD.ParentTransactionID, 0),
                ISNULL(STD.WarehouseID, 0)
              HAVING ROUND(
                ISNULL(SUM(ISNULL(STD.ReceiptQuantity, 0)), 0)
              - ISNULL(SUM(ISNULL(STD.IssueQuantity, 0)), 0), 2) > 0
              ORDER BY ISNULL(STD.ParentTransactionID, 0)",
            commandTimeout: 120);

        return rows.ToList();
    }

    // â”€â”€â”€ Load Master Data: fetch all spare parts to use as template â”€â”€â”€
    public async Task<List<SparePartStockEnrichedRow>> GetMasterDataAsync()
    {
        await EnsureOpenAsync();

        var query = @"
            SELECT 
                SparePartCode,
                SparePartName,
                SparePartID AS SpareID,
                ISNULL(Unit, '') AS StockUnit,
                CAST(1 AS BIT) AS IsValid
            FROM SparePartMaster
            WHERE ISNULL(IsDeletedTransaction, 0) = 0
            ORDER BY SparePartName";

        var result = await _connection.QueryAsync<SparePartStockEnrichedRow>(query);
        return result.ToList();
    }
}


