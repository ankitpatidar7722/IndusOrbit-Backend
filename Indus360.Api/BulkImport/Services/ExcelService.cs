using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;
using OfficeOpenXml;

namespace Backend.Services;

public class ExcelService : IExcelService
{
    private readonly SqlConnection _connection;

    public ExcelService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<ExcelPreviewDto> PreviewExcelAsync(Stream fileStream)
    {
        try
        {
            // EPPlus requires a seekable stream. Copy to MemoryStream to ensure it's seekable and at position 0
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0; // Reset position to beginning
            
            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0]; // Get first worksheet

            if (worksheet.Dimension == null)
            {
                throw new Exception("The Excel file is empty.");
            }

            var preview = new ExcelPreviewDto
            {
                TotalRows = worksheet.Dimension.Rows,
                TotalColumns = worksheet.Dimension.Columns
            };

            // Read headers from first row
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var headerValue = worksheet.Cells[1, col].Value?.ToString() ?? $"Column{col}";
                preview.Headers.Add(headerValue);
            }

            // Read all data rows
            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                var rowData = new List<object>();
                for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                {
                    var cellValue = worksheet.Cells[row, col].Value ?? "";
                    rowData.Add(cellValue);
                }
                preview.Rows.Add(rowData);
            }

            // AUTO-CALCULATE fields if this appears to be Item Master
            // Detect by checking for key columns: GSM, Quality, Width/SizeW, Length/SizeL
            var hasGSM = preview.Headers.Any(h => h.Equals("GSM", StringComparison.OrdinalIgnoreCase));
            var hasQuality = preview.Headers.Any(h => h.Equals("Quality", StringComparison.OrdinalIgnoreCase));
            var hasWidth = preview.Headers.Any(h => h.Equals("Width", StringComparison.OrdinalIgnoreCase) || 
                                                      h.Equals("SizeW", StringComparison.OrdinalIgnoreCase));
            var hasLength = preview.Headers.Any(h => h.Equals("Length", StringComparison.OrdinalIgnoreCase) || 
                                                       h.Equals("SizeL", StringComparison.OrdinalIgnoreCase));

            if (hasGSM || hasQuality || hasWidth || hasLength)
            {
                // This looks like Item Master - perform calculations
                var gsmIndex = preview.Headers.FindIndex(h => h.Equals("GSM", StringComparison.OrdinalIgnoreCase));
                var qualityIndex = preview.Headers.FindIndex(h => h.Equals("Quality", StringComparison.OrdinalIgnoreCase));
                var manufacturerIndex = preview.Headers.FindIndex(h => h.Equals("Manufacturer", StringComparison.OrdinalIgnoreCase) ||
                                                                        h.Equals("Manufecturer", StringComparison.OrdinalIgnoreCase));
                var finishIndex = preview.Headers.FindIndex(h => h.Equals("Finish", StringComparison.OrdinalIgnoreCase));
                var widthIndex = preview.Headers.FindIndex(h => h.Equals("Width", StringComparison.OrdinalIgnoreCase) || 
                                                                 h.Equals("SizeW", StringComparison.OrdinalIgnoreCase));
                var lengthIndex = preview.Headers.FindIndex(h => h.Equals("Length", StringComparison.OrdinalIgnoreCase) || 
                                                                  h.Equals("SizeL", StringComparison.OrdinalIgnoreCase));
                var unitPerPackingIndex = preview.Headers.FindIndex(h => h.Equals("UnitPerPacking", StringComparison.OrdinalIgnoreCase));
                
                var caliperIndex = preview.Headers.FindIndex(h => h.Equals("Caliper", StringComparison.OrdinalIgnoreCase));
                var itemSizeIndex = preview.Headers.FindIndex(h => h.Equals("ItemSize", StringComparison.OrdinalIgnoreCase));
                var wtPerPackingIndex = preview.Headers.FindIndex(h => h.Equals("WtPerPacking", StringComparison.OrdinalIgnoreCase));
                var itemNameIndex = preview.Headers.FindIndex(h => h.Equals("ItemName", StringComparison.OrdinalIgnoreCase));

                foreach (var row in preview.Rows)
                {
                    // Calculate Caliper = GSM / 1000
                    if (gsmIndex >= 0 && caliperIndex >= 0 && gsmIndex < row.Count)
                    {
                        if (decimal.TryParse(row[gsmIndex]?.ToString(), out var gsm) && gsm > 0)
                        {
                            if (caliperIndex >= row.Count)
                            {
                                // Extend row if Caliper column doesn't exist yet
                                while (row.Count <= caliperIndex) row.Add("");
                            }
                            row[caliperIndex] = (gsm / 1000).ToString("0.00");
                        }
                    }

                    // Calculate ItemSize = Width X Length MM
                    if (widthIndex >= 0 && lengthIndex >= 0 && itemSizeIndex >= 0 &&
                        widthIndex < row.Count && lengthIndex < row.Count)
                    {
                        var width = row[widthIndex]?.ToString();
                        var length = row[lengthIndex]?.ToString();
                        if (!string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(length))
                        {
                            if (itemSizeIndex >= row.Count)
                            {
                                while (row.Count <= itemSizeIndex) row.Add("");
                            }
                            row[itemSizeIndex] = $"{width} X {length} MM";
                        }
                    }

                    // Calculate WtPerPacking
                    if (lengthIndex >= 0 && widthIndex >= 0 && gsmIndex >= 0 && 
                        unitPerPackingIndex >= 0 && wtPerPackingIndex >= 0 &&
                        lengthIndex < row.Count && widthIndex < row.Count && 
                        gsmIndex < row.Count && unitPerPackingIndex < row.Count)
                    {
                        if (decimal.TryParse(row[lengthIndex]?.ToString(), out var len) &&
                            decimal.TryParse(row[widthIndex]?.ToString(), out var wid) &&
                            decimal.TryParse(row[gsmIndex]?.ToString(), out var gsm2) &&
                            decimal.TryParse(row[unitPerPackingIndex]?.ToString(), out var unit) &&
                            len > 0 && wid > 0 && gsm2 > 0 && unit > 0)
                        {
                            if (wtPerPackingIndex >= row.Count)
                            {
                                while (row.Count <= wtPerPackingIndex) row.Add("");
                            }
                            var wtPerPacking = (len * wid * gsm2 * unit) / 1000000000m;
                            row[wtPerPackingIndex] = wtPerPacking.ToString("0.0000");
                        }
                    }

                    // Calculate ItemName = Quality, GSM, Manufacturer, Finish, ItemSize
                    if (itemNameIndex >= 0)
                    {
                        var parts = new List<string>();
                        
                        if (qualityIndex >= 0 && qualityIndex < row.Count)
                        {
                            var quality = row[qualityIndex]?.ToString();
                            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
                        }
                        
                        if (gsmIndex >= 0 && gsmIndex < row.Count)
                        {
                            var gsmStr = row[gsmIndex]?.ToString();
                            if (!string.IsNullOrEmpty(gsmStr)) parts.Add($"{gsmStr} GSM");
                        }
                        
                        if (manufacturerIndex >= 0 && manufacturerIndex < row.Count)
                        {
                            var manufacturer = row[manufacturerIndex]?.ToString();
                            if (!string.IsNullOrEmpty(manufacturer)) parts.Add(manufacturer);
                        }
                        
                        if (finishIndex >= 0 && finishIndex < row.Count)
                        {
                            var finish = row[finishIndex]?.ToString();
                            if (!string.IsNullOrEmpty(finish)) parts.Add(finish);
                        }
                        
                        if (itemSizeIndex >= 0 && itemSizeIndex < row.Count)
                        {
                            var itemSize = row[itemSizeIndex]?.ToString();
                            if (!string.IsNullOrEmpty(itemSize)) parts.Add(itemSize);
                        }
                        
                        if (parts.Any())
                        {
                            if (itemNameIndex >= row.Count)
                            {
                                while (row.Count <= itemNameIndex) row.Add("");
                            }
                            row[itemNameIndex] = string.Join(", ", parts);
                        }
                    }
                }
            }

            return await Task.FromResult(preview);
        }
        catch (Exception ex)
        {
            // Check if this is likely an old .xls format issue
            if (ex.Message.Contains("not a valid Package file") || 
                ex.Message.Contains("BIFF") || 
                ex.Message.Contains("encrypted"))
            {
                throw new Exception("This file appears to be in the old Excel format (.xls). Please convert it to the newer .xlsx format and try again. EPPlus library only supports .xlsx files.");
            }
            
            throw new Exception($"Error reading Excel file: {ex.Message}", ex);
        }
    }

    public async Task<ImportResultDto> ImportExcelAsync(Stream fileStream, string tableName, int? subModuleId = null)
    {
        // Debug: Log the incoming table name
        try 
        {
            File.AppendAllText("import_debug.log", $"{DateTime.Now}: Starting Import for TableName: '{tableName}'{Environment.NewLine}");
        }
        catch {}

        // Special handling for Product Group Master
        // Loosen check to catch variations
        if (tableName.Equals("Product Group Master", StringComparison.OrdinalIgnoreCase) || 
            tableName.Equals("ProductGroupMaster", StringComparison.OrdinalIgnoreCase) ||
            tableName.Contains("Product Group", StringComparison.OrdinalIgnoreCase) ||
            tableName.Equals("ProductGroupMasterForGST.aspx", StringComparison.OrdinalIgnoreCase) ||
            tableName.Equals("ProductGroupMasterForGST", StringComparison.OrdinalIgnoreCase))
        {
            return await ImportProductGroupMasterAsync(fileStream);
        }

        if (tableName.Equals("Spare Part Master", StringComparison.OrdinalIgnoreCase) ||
            tableName.Equals("SparePartMaster", StringComparison.OrdinalIgnoreCase) ||
            tableName.Equals("SparePartMaster.aspx", StringComparison.OrdinalIgnoreCase) ||
            tableName.Contains("Spare Part", StringComparison.OrdinalIgnoreCase))
        {
            return await ImportSparePartMasterAsync(fileStream, 2); // Default user 2 as per existing code
        }

        // New Logic for Ledger Master (Clients/Suppliers)
        if (tableName.Equals("Clients", StringComparison.OrdinalIgnoreCase) || 
            tableName.Equals("Suppliers", StringComparison.OrdinalIgnoreCase) ||
            tableName.Equals("LedgerMaster", StringComparison.OrdinalIgnoreCase))
        {
            // Determine Group ID (Hardcoded for now as per requirement, or lookup)
            int groupId = 0; // Default
            if (tableName.Equals("Clients", StringComparison.OrdinalIgnoreCase)) groupId = 1;
            else if (tableName.Equals("Suppliers", StringComparison.OrdinalIgnoreCase)) groupId = 2;
            else groupId = 1; // Fallback? Or maybe throw error? Let's assume 1.

            return await ImportLedgerMasterAsync(fileStream, tableName, groupId);
        }

        // Tool Master routing - now with subModuleId support
        if (tableName.Equals("Tool Master", StringComparison.OrdinalIgnoreCase) || 
            tableName.Equals("ToolMaster", StringComparison.OrdinalIgnoreCase) ||
            tableName.Contains("Tool", StringComparison.OrdinalIgnoreCase))
        {
            if (!subModuleId.HasValue || subModuleId.Value <= 0)
            {
                return new ImportResultDto
                {
                    Success = false,
                    ErrorMessages = new List<string> { "Tool Master import requires a valid Tool Group ID. Please select a sub-module." }
                };
            }

            return await ImportToolMasterAsync(fileStream, "Tool Master", subModuleId.Value);
        }

        var result = new ImportResultDto();
        
        try
        {
            // EPPlus requires a seekable stream. Copy to MemoryStream to ensure it's seekable and at position 0
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            // Read headers
            var headers = new List<string>();
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                headers.Add(worksheet.Cells[1, col].Value?.ToString() ?? $"Column{col}");
            }

            result.TotalRows = worksheet.Dimension.Rows - 1; // Exclude header row

            // Get existing records to check for duplicates
            var existingRecords = await GetExistingRecordsAsync(tableName, headers);

            int importedCount = 0;
            int duplicateCount = 0;
            int errorCount = 0;

            // Process each row
            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                try
                {
                    var rowData = new Dictionary<string, object>();
                    
                    for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                    {
                        var value = worksheet.Cells[row, col].Value;
                        rowData[headers[col - 1]] = value ?? DBNull.Value;
                    }

                    // Check for duplicates
                    if (IsDuplicate(rowData, existingRecords))
                    {
                        duplicateCount++;
                        continue;
                    }

                    // Insert record
                    await InsertRecordAsync(tableName, rowData);
                    importedCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    result.ErrorMessages.Add($"Row {row}: {ex.Message}");
                }
            }

            result.Success = errorCount == 0;
            result.ImportedRows = importedCount;
            result.DuplicateRows = duplicateCount;
            result.ErrorRows = errorCount;
            result.ErrorMessages = result.ErrorMessages; // Ensure these are set

            if (result.Success)
                result.Message = $"Import completed. Imported: {importedCount}, Duplicates: {duplicateCount}, Errors: {errorCount}";
            else 
            {
                var firstError = result.ErrorMessages.FirstOrDefault() ?? "Unknown";
                result.Message = $"Generic Import Failed. Errors: {errorCount}. First Error: {firstError}";
                 try 
                 {
                    File.AppendAllText("import_debug.log", $"{DateTime.Now}: Generic Import Failed. Table: {tableName}. First Error: {firstError}{Environment.NewLine}");
                 }
                 catch {}
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"Import failed: {ex.Message}");
            return result;
        }
    }

    private async Task<ImportResultDto> ImportProductGroupMasterAsync(Stream fileStream)
    {
        var result = new ImportResultDto();
        try
        {
            // EPPlus requires a seekable stream. Copy to MemoryStream to ensure it's seekable and at position 0
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            result.TotalRows = worksheet.Dimension.Rows - 1;
            int errorCount = 0;
            var errorMessages = new List<string>();

            // Get Headers mapping
            var headerMap = new Dictionary<string, int>();
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(header))
                {
                    headerMap[header] = col;
                }
            }

            // Helper to get value securely
            string? GetValue(string colName, int rowIdx)
            {
                var colIndex = headerMap.ContainsKey(colName) ? headerMap[colName] : 
                               headerMap.FirstOrDefault(k => k.Key.Equals(colName, StringComparison.OrdinalIgnoreCase)).Value;
                
                if (colIndex == 0) return null;
                
                // Get the cell value and handle DBNull explicitly
                var cellValue = worksheet.Cells[rowIdx, colIndex].Value;
                
                // Convert DBNull to null
                if (cellValue == null || cellValue == DBNull.Value)
                    return null;
                
                var stringValue = cellValue.ToString()?.Trim();
                return string.IsNullOrEmpty(stringValue) ? null : stringValue;
            }

            // ==========================================
            // PHASE 1: Validation
            // ==========================================
            var insertList = new List<DynamicParameters>();
            var rowsToProcess = new List<int>();

            // To track unique names within the file itself
            var fileDisplayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Fetch existing DisplayNames from database for duplicate check
            await EnsureConnectionOpenAsync();
            string fetchExistingQuery = @"
                SELECT LTRIM(RTRIM(DisplayName)) AS DisplayName
                FROM ProductHSNMaster
                WHERE IsDeletedTransaction = 0
                    AND DisplayName IS NOT NULL
                    AND DisplayName <> ''";

            var existingDisplayNames = await _connection.QueryAsync<string>(fetchExistingQuery);
            var dbDisplayNames = new HashSet<string>(
                existingDisplayNames.Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                try
                {
                    // 0. Check for Empty Row
                    bool isRowEmpty = true;
                    for (int c = 1; c <= worksheet.Dimension.Columns; c++)
                    {
                        if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, c].Value?.ToString()))
                        {
                            isRowEmpty = false;
                            break;
                        }
                    }

                    if (isRowEmpty) continue;

                    // 1. Read Excel Values
                    var groupName = GetValue("Group Name", row);
                    var hsnCode = GetValue("HSN Code", row);
                    var displayName = GetValue("Display Name", row);
                    var productType = GetValue("ProductType", row);
                    var gst = GetValue("GST %", row);
                    var cgst = GetValue("CGST %", row);
                    var sgst = GetValue("SGST %", row);
                    var igst = GetValue("IGST %", row);
                    var itemGroupName = GetValue("ItemGroupName", row) ?? GetValue("Item Group Name", row);

                    // 2. DUPLICATE VALIDATION: Check DisplayName
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        var displayNameTrimmed = displayName.Trim();

                        // Check for duplicate within Excel file
                        if (fileDisplayNames.Contains(displayNameTrimmed))
                        {
                            errorCount++;
                            result.DuplicateRows++;
                            errorMessages.Add($"Row {row}: Duplicate DisplayName '{displayNameTrimmed}' found within Excel file.");
                            continue;
                        }

                        // Check for duplicate in database
                        if (dbDisplayNames.Contains(displayNameTrimmed))
                        {
                            errorCount++;
                            result.DuplicateRows++;
                            errorMessages.Add($"Row {row}: Duplicate DisplayName '{displayNameTrimmed}' already exists in database.");
                            continue;
                        }

                        // Add to Excel tracking set
                        fileDisplayNames.Add(displayNameTrimmed);
                    }

                    // Default to empty strings to avoid null issues
                    displayName = displayName ?? "";
                    hsnCode = hsnCode ?? "";
                    productType = productType ?? "";

                    // 3. Dynamic Lookup for ItemGroupID
                    int? itemGroupId = null;
                    if (string.Equals(productType, "Raw Material", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(itemGroupName))
                        {
                            await EnsureConnectionOpenAsync();
                            itemGroupId = await _connection.QueryFirstOrDefaultAsync<int?>(
                                "SELECT ItemGroupID FROM ItemGroupMaster WHERE ItemGroupName = @ItemGroupName",
                                new { ItemGroupName = itemGroupName });
                            
                            if (itemGroupId == null)
                                throw new Exception($"ItemGroupName '{itemGroupName}' not found in ItemGroupMaster.");
                        }
                    }

                    // 4. Prepare Data for Later Insert
                    var insertParams = new DynamicParameters();
                    insertParams.Add("@ProductHSNName", groupName);
                    insertParams.Add("@HSNCode", hsnCode ?? "");
                    insertParams.Add("@UnderProductHSNID", 0);
                    insertParams.Add("@GroupLevel", 0);
                    insertParams.Add("@CompanyID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2);
                    insertParams.Add("@DisplayName", displayName);
                    insertParams.Add("@TariffNo", "");
                    insertParams.Add("@ProductCategory", productType ?? "");
                    insertParams.Add("@GSTTaxPercentage", decimal.TryParse(gst?.Replace("%",""), out var dGst) ? dGst : 0);
                    insertParams.Add("@CGSTTaxPercentage", decimal.TryParse(cgst?.Replace("%",""), out var dCgst) ? dCgst : 0);
                    insertParams.Add("@SGSTTaxPercentage", decimal.TryParse(sgst?.Replace("%",""), out var dSgst) ? dSgst : 0);
                    insertParams.Add("@IGSTTaxPercentage", decimal.TryParse(igst?.Replace("%",""), out var dIgst) ? dIgst : 0);
                    insertParams.Add("@TallyProductHSNName", "");
                    insertParams.Add("@TallyGUID", 0);
                    insertParams.Add("@ItemGroupID", itemGroupId);
                    insertParams.Add("@UserID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);
                    insertParams.Add("@ModifiedDate", DateTime.Now);
                    insertParams.Add("@CreatedBy", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);
                    insertParams.Add("@CreatedDate", DateTime.Now);
                    insertParams.Add("@ModifiedBy", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);
                    insertParams.Add("@DeletedBy", 0);
                    insertParams.Add("@DeletedDate", null);
                    insertParams.Add("@IsDeletedTransaction", 0);
                    insertParams.Add("@FYear", (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"));

                    insertList.Add(insertParams);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    var errorMsg = $"Row {row}: {ex.Message}";
                    errorMessages.Add(errorMsg);
                }
            }

            // ==========================================
            // PHASE 2: Execution (Only if no errors)
            // ==========================================
            result.ErrorRows = errorCount;
            result.ErrorMessages = errorMessages;

            if (errorCount == 0 && insertList.Any())
            {
                await EnsureConnectionOpenAsync();

                using var transaction = _connection.BeginTransaction();
                try
                {
                     string insertQuery = @"
                        INSERT INTO ProductHSNMaster (
                            ProductHSNName, HSNCode, UnderProductHSNID, GroupLevel, CompanyID, 
                            DisplayName, TariffNo, ProductCategory, GSTTaxPercentage, 
                            CGSTTaxPercentage, SGSTTaxPercentage, IGSTTaxPercentage, 
                            TallyProductHSNName, TallyGUID, ItemGroupID, UserID, 
                            ModifiedDate, CreatedBy, CreatedDate, ModifiedBy, 
                            DeletedBy, DeletedDate, IsDeletedTransaction, FYear
                        ) VALUES (
                            @ProductHSNName, @HSNCode, @UnderProductHSNID, @GroupLevel, @CompanyID, 
                            @DisplayName, @TariffNo, @ProductCategory, @GSTTaxPercentage, 
                            @CGSTTaxPercentage, @SGSTTaxPercentage, @IGSTTaxPercentage, 
                            @TallyProductHSNName, @TallyGUID, @ItemGroupID, @UserID, 
                            @ModifiedDate, @CreatedBy, @CreatedDate, @ModifiedBy, 
                            @DeletedBy, @DeletedDate, @IsDeletedTransaction, @FYear
                        )";

                    foreach (var paramsObj in insertList)
                    {
                        await _connection.ExecuteAsync(insertQuery, paramsObj, transaction: transaction);
                    }

                    transaction.Commit();

                    result.Success = true;
                    result.ImportedRows = insertList.Count;

                    // Build success message with duplicate/error info
                    var messageBuilder = new System.Text.StringBuilder();
                    messageBuilder.Append($"Successfully imported {insertList.Count} record(s) into Product Group Master.");

                    if (result.DuplicateRows > 0)
                    {
                        messageBuilder.Append($" Skipped: {result.DuplicateRows} duplicate(s).");
                    }

                    result.Message = messageBuilder.ToString();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    result.Success = false;
                    result.Message = $"Transaction Failed: {ex.Message}";
                    result.ErrorMessages.Add($"Critical Transaction Error: {ex.Message}");

                    try { File.AppendAllText("import_debug.log", $"{DateTime.Now}: Transaction Rolled back. {ex.Message}{Environment.NewLine}"); } catch {}
                }
            }
            else if (errorCount > 0)
            {
                result.Success = false;
                result.ImportedRows = 0;
                result.ErrorRows = errorCount - result.DuplicateRows;

                result.Message = result.DuplicateRows > 0
                    ? $"Import failed. {result.DuplicateRows} duplicate(s) found, {result.ErrorRows} validation error(s). No data inserted."
                    : $"Validation failed with {errorCount} error(s). No data inserted.";

                try { File.AppendAllText("import_debug.log", $"{DateTime.Now}: Validation Failed. {errorCount} errors.{Environment.NewLine}"); } catch {}
            }
            else
            {
                // No errors but no data (maybe all empty rows?)
                result.Success = true;
                result.ImportedRows = 0;
                result.Message = "No valid data found to import.";
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"Critical Import Error: {ex.Message}");
            return result;
        }
    }

    private async Task<ImportResultDto> ImportSparePartMasterAsync(Stream fileStream, int createdBy)
    {
        var result = new ImportResultDto();
        try
        {
            // EPPlus requires a seekable stream. Copy to MemoryStream to ensure it's seekable and at position 0
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            result.TotalRows = worksheet.Dimension.Rows - 1;
            int errorCount = 0;
            int duplicateCount = 0;
            var errorMessages = new List<string>();

            // Get Headers mapping
            var headerMap = new Dictionary<string, int>();
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(header))
                {
                    headerMap[header] = col;
                }
            }

            string? GetValue(string colName, int rowIdx)
            {
                var colIndex = headerMap.ContainsKey(colName) ? headerMap[colName] : 
                               headerMap.FirstOrDefault(k => k.Key.Equals(colName, StringComparison.OrdinalIgnoreCase)).Value;
                
                if (colIndex == 0) return null;
                
                // Get the cell value and handle DBNull explicitly
                var cellValue = worksheet.Cells[rowIdx, colIndex].Value;
                
                // Convert DBNull to null
                if (cellValue == null || cellValue == DBNull.Value)
                    return null;
                
                var stringValue = cellValue.ToString()?.Trim();
                return string.IsNullOrEmpty(stringValue) ? null : stringValue;
            }

            // ==========================================
            // PHASE 1: Validation
            // ==========================================
            var validRows = new List<Dictionary<string, object>>();
            // Track duplicates in file: Combination of Name + Group + Type
            var fileDuplicateCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Fetch Lookups
            // ProductHSNID Lookup (HSNGroup -> ID) mapping to ProductHSNName in DB
            var hsnLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                await EnsureConnectionOpenAsync();
                var hsnList = await _connection.QueryAsync<(int Id, string Name)>("SELECT ProductHSNID, DisplayName FROM ProductHSNMaster WHERE IsDeletedTransaction = 0");
                foreach (var hsn in hsnList)
                {
                    if (hsn.Name != null && !hsnLookup.ContainsKey(hsn.Name))
                        hsnLookup.Add(hsn.Name, hsn.Id);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessages.Add("Failed to fetch Product HSN Master data: " + ex.Message);
                return result;
            }

            // Fetch Existing Spare Parts for Duplicate Check (Name + Group + Type)
            var existingSpareParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var spList = await _connection.QueryAsync<(string Name, string Group, string Type)>(
                    "SELECT SparePartName, SparePartGroup, SparePartType FROM SparePartMaster WHERE IsDeletedTransaction = 0");
                foreach (var sp in spList)
                {
                    if (!string.IsNullOrEmpty(sp.Name) && !string.IsNullOrEmpty(sp.Group) && !string.IsNullOrEmpty(sp.Type))
                    {
                        string compositeKey = $"{sp.Name.Trim()}|{sp.Group.Trim()}|{sp.Type.Trim()}";
                        existingSpareParts.Add(compositeKey);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessages.Add("Failed to fetch existing Spare Parts: " + ex.Message);
                return result;
            }

            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                // Check empty row
                bool isRowEmpty = true;
                for (int c = 1; c <= worksheet.Dimension.Columns; c++)
                {
                    if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, c].Value?.ToString()))
                    {
                        isRowEmpty = false;
                        break;
                    }
                }
                if (isRowEmpty) continue;

                var sparePartName = GetValue("SparePartName", row) ?? GetValue("Spare Part Name", row);
                var sparePartGroup = GetValue("SparePartGroup", row) ?? GetValue("Spare Part Group", row);
                var sparePartType = GetValue("SparePartType", row) ?? GetValue("Spare Part Type", row);
                var hsnCode = GetValue("HSNCode", row) ?? GetValue("HSN Code", row);
                var unit = GetValue("Unit", row);
                var rateStr = GetValue("Rate", row);
                
                // New Columns Mapping
                var hsnGroup = GetValue("HSNGroup", row) ?? GetValue("HSN Group", row);
                var supplierReference = GetValue("SupplierReference", row) ?? GetValue("Supplier Reference", row);
                var stockRefCode = GetValue("StockRefCode", row) ?? GetValue("Stock Ref Code", row);
                var poQtyStr = GetValue("PurchaseOrderQuantity", row) ?? GetValue("Purchase Order Quantity", row);

                // Safe parsing
                decimal rate = 0;
                if (!string.IsNullOrEmpty(rateStr) && decimal.TryParse(rateStr, out var r)) rate = r;
                
                decimal purchaseOrderQuantity = 0;
                if (!string.IsNullOrEmpty(poQtyStr) && decimal.TryParse(poQtyStr, out var poq)) purchaseOrderQuantity = poq;

                var rowErrors = new List<string>();
                bool isDuplicate = false;

                // ==========================================
                // REQUIRED FIELD VALIDATION
                // ==========================================
                if (string.IsNullOrEmpty(sparePartName))
                    rowErrors.Add($"Row {row} [SparePartName={sparePartName ?? "NULL"}, SparePartGroup={sparePartGroup ?? "NULL"}]: SparePartName is required.");

                if (string.IsNullOrEmpty(sparePartGroup))
                    rowErrors.Add($"Row {row} [SparePartName={sparePartName ?? "NULL"}, SparePartGroup={sparePartGroup ?? "NULL"}]: SparePartGroup is required.");

                if (string.IsNullOrEmpty(sparePartType))
                    rowErrors.Add($"Row {row} [SparePartName={sparePartName ?? "NULL"}, SparePartGroup={sparePartGroup ?? "NULL"}]: SparePartType is required.");

                if (string.IsNullOrEmpty(hsnCode))
                    rowErrors.Add($"Row {row} [SparePartName={sparePartName ?? "NULL"}, SparePartGroup={sparePartGroup ?? "NULL"}]: HSNCode is required.");

                if (string.IsNullOrEmpty(unit))
                    rowErrors.Add($"Row {row} [SparePartName={sparePartName ?? "NULL"}, SparePartGroup={sparePartGroup ?? "NULL"}]: Unit is required.");

                // ==========================================
                // COMBINATION DUPLICATE VALIDATION
                // ==========================================
                // Validate: (SparePartName + SparePartGroup + SparePartType)
                if (!string.IsNullOrEmpty(sparePartName) && !string.IsNullOrEmpty(sparePartGroup) && !string.IsNullOrEmpty(sparePartType))
                {
                    string compositeKey = $"{sparePartName.Trim()}|{sparePartGroup.Trim()}|{sparePartType.Trim()}";

                    // Check in database
                    if (existingSpareParts.Contains(compositeKey))
                    {
                        isDuplicate = true;
                        duplicateCount++;
                        rowErrors.Add($"Row {row} [SparePartName={sparePartName}, SparePartGroup={sparePartGroup}]: Duplicate SparePartName + SparePartGroup + SparePartType already exists in database.");
                    }

                    // Check inside Excel batch
                    if (fileDuplicateCheck.Contains(compositeKey))
                    {
                        isDuplicate = true;
                        if (!rowErrors.Any(e => e.Contains("Duplicate"))) // Don't double-count duplicates
                            duplicateCount++;
                        rowErrors.Add($"Row {row} [SparePartName={sparePartName}, SparePartGroup={sparePartGroup}]: Duplicate SparePartName + SparePartGroup + SparePartType found within Excel file.");
                    }
                    else
                    {
                        fileDuplicateCheck.Add(compositeKey);
                    }
                }

                // ProductHSNID Lookup using HSNGroup (Optional - for ProductHSNID foreign key)
                int productHSNID = 0;
                if (!string.IsNullOrEmpty(hsnGroup))
                {
                    if (hsnLookup.TryGetValue(hsnGroup, out int id))
                        productHSNID = id;
                    // Don't error if HSNGroup not found - it's optional for lookup
                }

                // ==========================================
                // VALIDATION RESULT HANDLING
                // ==========================================
                if (rowErrors.Any())
                {
                    if (!isDuplicate) // Only increment errorCount for non-duplicate errors
                        errorCount++;
                    errorMessages.AddRange(rowErrors);
                    continue; // Skip this row - do not add to validRows
                }

                // Row is valid - add to import batch
                var validRow = new Dictionary<string, object>();
                validRow["SparePartName"] = sparePartName ?? "";
                validRow["SparePartGroup"] = sparePartGroup ?? "";
                validRow["SparePartType"] = sparePartType ?? "";
                validRow["HSNCode"] = hsnCode ?? "";
                validRow["Unit"] = unit ?? "";
                validRow["ProductHSNID"] = productHSNID;
                validRow["Rate"] = rate;
                validRow["HSNGroup"] = hsnGroup ?? "";
                validRow["SupplierReference"] = supplierReference ?? "";
                validRow["StockRefCode"] = stockRefCode ?? "";
                validRow["PurchaseOrderQuantity"] = purchaseOrderQuantity;
                validRows.Add(validRow);
            }

            // Check if there are any validation errors or duplicates
            if (errorMessages.Any())
            {
                result.Success = false;
                result.ErrorRows = errorCount;
                result.DuplicateRows = duplicateCount;
                result.ErrorMessages = errorMessages;

                var messageParts = new List<string>();
                if (duplicateCount > 0)
                    messageParts.Add($"{duplicateCount} duplicate(s)");
                if (errorCount > 0)
                    messageParts.Add($"{errorCount} validation error(s)");

                result.Message = $"Validation failed: {string.Join(", ", messageParts)}. No data imported.";
                return result;
            }

            if (!validRows.Any())
            {
                result.Success = true;
                result.Message = "No valid data found to import.";
                return result;
            }

            // ==========================================
            // PHASE 2: Execution
            // ==========================================
            await EnsureConnectionOpenAsync();
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Get Max Code
                int currentMaxCode = await _connection.ExecuteScalarAsync<int?>("SELECT MAX(MaxSparePartCode) FROM SparePartMaster WHERE IsDeletedTransaction = 0", transaction: transaction) ?? 0;

                string insertSql = @"
                    INSERT INTO SparePartMaster
                    (SparePartName, SparePartCode, MaxSparePartCode, ProductHSNID, SparePartGroup, SparePartType, HSNCode, Unit, Rate,
                     HSNGroup, SupplierReference, StockRefCode, PurchaseOrderQuantity,
                     VoucherPrefix, CompanyID, UserID,
                     VoucherDate, CreatedBy, CreatedDate, IsDeletedTransaction)
                    VALUES
                    (@SparePartName, @SparePartCode, @MaxSparePartCode, @ProductHSNID, @SparePartGroup, @SparePartType, @HSNCode, @Unit, @Rate,
                     @HSNGroup, @SupplierReference, @StockRefCode, @PurchaseOrderQuantity,
                     @VoucherPrefix, @CompanyID, @UserID,
                     @VoucherDate, @CreatedBy, @CreatedDate, 0)";

                foreach (var row in validRows)
                {
                    currentMaxCode++;
                    string sparePartCode = "SPM" + currentMaxCode.ToString().PadLeft(5, '0');

                    await _connection.ExecuteAsync(insertSql, new {
                        SparePartName = row["SparePartName"],
                        SparePartCode = sparePartCode,
                        MaxSparePartCode = currentMaxCode,
                        ProductHSNID = row["ProductHSNID"],
                        SparePartGroup = row["SparePartGroup"],
                        SparePartType = row["SparePartType"],
                        HSNCode = row["HSNCode"],
                        Unit = row["Unit"],
                        Rate = row["Rate"],
                        HSNGroup = row["HSNGroup"],
                        SupplierReference = row["SupplierReference"],
                        StockRefCode = row["StockRefCode"],
                        PurchaseOrderQuantity = row["PurchaseOrderQuantity"],
                        VoucherPrefix = "SPM",
                        CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                        UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                        VoucherDate = DateTime.Now,
                        CreatedBy = createdBy,
                        CreatedDate = DateTime.Now
                    }, transaction: transaction);
                }

                transaction.Commit();
                result.Success = true;
                result.ImportedRows = validRows.Count;
                result.Message = $"Successfully imported {validRows.Count} Spare Parts.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                result.Success = false;
                result.ErrorMessages.Add("Database Error: " + ex.Message);
                result.Message = "Database error during import: " + ex.Message;
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"Critical Error: {ex.Message}");
            return result;
        }
    }

    private async Task EnsureConnectionOpenAsync()
    {
        if (_connection.State == System.Data.ConnectionState.Broken)
        {
            _connection.Close();
        }
        
        if (_connection.State != System.Data.ConnectionState.Open)
        {
             await _connection.OpenAsync();
        }
    }

    private async Task<List<Dictionary<string, object>>> GetExistingRecordsAsync(string tableName, List<string> columns)
    {
        try
        {
            var columnList = string.Join(", ", columns.Select(c => $"[{c}]"));
            var query = $"SELECT {columnList} FROM [{tableName}]";
            
            var records = await _connection.QueryAsync(query);
            
            return records.Select(r => ((IDictionary<string, object>)r).ToDictionary(k => k.Key, v => v.Value)).ToList();
        }
        catch
        {
            // If table doesn't exist or error, return empty list
            return new List<Dictionary<string, object>>();
        }
    }

    private bool IsDuplicate(Dictionary<string, object> newRecord, List<Dictionary<string, object>> existingRecords)
    {
        foreach (var existing in existingRecords)
        {
            bool isDuplicate = true;
            
            foreach (var key in newRecord.Keys)
            {
                if (!existing.ContainsKey(key))
                {
                    isDuplicate = false;
                    break;
                }

                var newValue = newRecord[key]?.ToString() ?? "";
                var existingValue = existing[key]?.ToString() ?? "";

                if (newValue != existingValue)
                {
                    isDuplicate = false;
                    break;
                }
            }

            if (isDuplicate)
                return true;
        }

        return false;
    }

    private async Task InsertRecordAsync(string tableName, Dictionary<string, object> data)
    {
        var columns = string.Join(", ", data.Keys.Select(k => $"[{k}]"));
        // Sanitize parameter names: remove spaces and special chars
        var paramDict = new Dictionary<string, object>();
        var paramNames = new List<string>();

        foreach(var kvp in data)
        {
            var cleanParamName = "@" + System.Text.RegularExpressions.Regex.Replace(kvp.Key, "[^a-zA-Z0-9]", "");
            paramNames.Add(cleanParamName);
            paramDict[cleanParamName.Substring(1)] = kvp.Value; // Dapper expects name without @ in dictionary keys? No, DynamicParameters handles it, but dictionary keys usually don't need @ if passed as object. 
            // Wait, Dapper's ExecuteAsync(sql, paramObject). If paramObject is Dictionary<string, object>, keys should match @ParamName in sql? 
            // Dapper treats Dictionary<string,object> as a bag of parameters where Key = ParamName.
        }
        
        // Actually, simpler way with Dapper and Dictionary:
        // If we use DynamicParameters, it's safer.
        var parameters = new DynamicParameters();
        var paramList = new List<string>();
        
        foreach(var kvp in data)
        {
            var cleanName = System.Text.RegularExpressions.Regex.Replace(kvp.Key, "[^a-zA-Z0-9_]", "_");
            var paramName = $"@{cleanName}";
            
            paramList.Add(paramName);
            parameters.Add(cleanName, kvp.Value);
        }

        var paramString = string.Join(", ", paramList);
        var query = $"INSERT INTO [{tableName}] ({columns}) VALUES ({paramString})";
        
        await _connection.ExecuteAsync(query, parameters);
    }

    private async Task<ImportResultDto> ImportLedgerMasterAsync(Stream fileStream, string moduleName, int ledgerGroupId)
    {
        var result = new ImportResultDto();
        try
        {
            // EPPlus requires a seekable stream. Copy to MemoryStream to ensure it's seekable and at position 0
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            result.TotalRows = worksheet.Dimension.Rows - 1;

            // 1. Map Headers
            var headerMap = new Dictionary<string, int>();
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(header))
                {
                    headerMap[header] = col;
                }
            }

            string? GetValue(string colName, int rowIdx)
            {
                var colIndex = headerMap.ContainsKey(colName) ? headerMap[colName] : 
                               headerMap.FirstOrDefault(k => k.Key.Equals(colName, StringComparison.OrdinalIgnoreCase)).Value;
                
                if (colIndex == 0) return null;
                
                // Get the cell value and handle DBNull explicitly
                var cellValue = worksheet.Cells[rowIdx, colIndex].Value;
                
                // Convert DBNull to null
                if (cellValue == null || cellValue == DBNull.Value)
                    return null;
                
                var stringValue = cellValue.ToString()?.Trim();
                return string.IsNullOrEmpty(stringValue) ? null : stringValue;
            }

            // 2. Prepare Data
            var validRows = new List<Dictionary<string, object?>>();

            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                bool isRowEmpty = true;
                for (int c = 1; c <= worksheet.Dimension.Columns; c++)
                {
                    if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, c].Value?.ToString()))
                    {
                        isRowEmpty = false;
                        break;
                    }
                }
                if (isRowEmpty) continue;

                // Get LedgerName - NO VALIDATION, accept any value including null
                var ledgerName = GetValue("LedgerName", row) ?? GetValue("Ledger Name", row) ?? GetValue("Name", row) ?? "";

                // Construct Row Data
                var rowData = new Dictionary<string, object?>();
                
                // Add all columns dynamic mapping
                foreach(var kvp in headerMap)
                {
                    var cellValue = GetValue(kvp.Key, row);
                    // Convert null or empty string to DBNull for database, 
                    // but store as null in dictionary to avoid DBNull parameter errors
                    rowData[kvp.Key] = cellValue; // GetValue already returns null for empty cells
                }
                // Ensure LedgerName is standard
                rowData["LedgerName"] = ledgerName;

                validRows.Add(rowData);
            }

            // Skip error check - process all rows
            // if (errorCount > 0 && !validRows.Any())
            // {
            //     result.Success = false;
            //     result.ErrorMessages = errorMessages;
            //     result.ErrorRows = errorCount;
            //     result.Message = "Validation failed. No valid rows found.";
            //     return result;
            // }

            // 3. Execute Import (Transactional)
            await EnsureConnectionOpenAsync();
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Get Prefix and Max No logic
                // For now, simple logic: Get Max Ledger Code
                // Ideally we should lookup LedgerGroupMaster for Prefix (e.g., 'CLT' or 'SUP')
                string prefix = "LGR";
                try 
                {
                    var groupPrefix = await _connection.ExecuteScalarAsync<string>(
                        "SELECT LedgerGroupPrefix FROM LedgerGroupMaster WHERE LedgerGroupID = @GID", 
                        new { GID = ledgerGroupId }, transaction: transaction);
                    if (!string.IsNullOrEmpty(groupPrefix)) prefix = groupPrefix;
                }
                catch {} // Fallback to LGR if table/column missing

                // Get current Max Number
                var maxLedgerNoObj = await _connection.ExecuteScalarAsync<object>(
                    "SELECT MAX(MaxLedgerNo) FROM LedgerMaster WHERE LedgerGroupID = @GID AND IsDeletedTransaction = 0",
                    new { GID = ledgerGroupId }, transaction: transaction);
                
                long currentMaxNo = 0;
                if (maxLedgerNoObj != null && long.TryParse(maxLedgerNoObj.ToString(), out var parsedMax))
                {
                    currentMaxNo = parsedMax;
                }

                foreach (var rowData in validRows)
                {
                    currentMaxNo++;
                    var ledgerCode = $"{prefix}{currentMaxNo.ToString().PadLeft(5, '0')}";
                    var ledgerName = rowData["LedgerName"]?.ToString() ?? "";

                    // Insert Header (LedgerMaster)
                    string insertMasterSql = @"
                        INSERT INTO LedgerMaster (
                            LedgerCode, MaxLedgerNo, LedgerCodePrefix, LedgerName, 
                            LedgerGroupID, CompanyID, UserID, FYear, 
                            ISLedgerActive, IsDeletedTransaction, CreatedDate, CreatedBy
                        ) VALUES (
                            @LedgerCode, @MaxLedgerNo, @LedgerCodePrefix, @LedgerName,
                            @LedgerGroupID, @CompanyID, @UserID, @FYear,
                            @ISLedgerActive, 0, @CreatedDate, @CreatedBy
                        );
                        SELECT SCOPE_IDENTITY();";

                    var masterParams = new {
                        LedgerCode = ledgerCode,
                        MaxLedgerNo = currentMaxNo,
                        LedgerCodePrefix = prefix,
                        LedgerName = ledgerName,
                        LedgerGroupID = ledgerGroupId,
                        CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2, // Hardcoded
                        UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,    // Hardcoded
                        FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                        ISLedgerActive = true,
                        CreatedDate = DateTime.Now,
                        CreatedBy = 2
                    };

                    var ledgerIdObj = await _connection.ExecuteScalarAsync<object>(insertMasterSql, masterParams, transaction: transaction);
                    int newLedgerId = Convert.ToInt32(ledgerIdObj);

                    // Insert Details (LedgerMasterDetails)
                    // We iterate over all keys in rowData. 
                    // Any key that is NOT "LedgerName" is treated as a generic field.
                    // (You might want to exclude strictly system columns if they appear in Excel, but usually Excel only has data columns)
                    
                    string insertDetailSql = @"
                        INSERT INTO LedgerMasterDetails (
                            LedgerID, LedgerGroupID, CompanyID, UserID, FYear,
                            FieldName, FieldValue, ParentFieldName, ParentFieldValue,
                            CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
                        ) VALUES (
                            @LedgerID, @LedgerGroupID, @CompanyID, @UserID, @FYear,
                            @FieldName, @FieldValue, @ParentFieldName, @ParentFieldValue,
                            @CreatedDate, @CreatedBy, @CreatedDate, @CreatedBy
                        )";

                    foreach (var key in rowData.Keys)
                    {
                        var val = rowData[key]?.ToString(); // Can be null
                        
                        // Insert every column as a detail, including Name if desired, or skip Name? 
                        // The VB code seems to insert everything in details too (objIMDRecord loop).
                        // Let's insert everything.
                        
                        await _connection.ExecuteAsync(insertDetailSql, new {
                            LedgerID = newLedgerId,
                            LedgerGroupID = ledgerGroupId,
                            CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                            UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                            FieldName = key,             // The Excel Header Name
                            FieldValue = val ?? (object)DBNull.Value,  // Use DBNull.Value for null values
                            ParentFieldName = key,       // Legacy often maps ParentFieldName = FieldName
                            ParentFieldValue = val ?? (object)DBNull.Value,
                            CreatedDate = DateTime.Now,
                            CreatedBy = 2
                        }, transaction: transaction);
                    }
                    
                    // Explicit Insert for ISLedgerActive in Details (as per legacy code)
                    await _connection.ExecuteAsync(insertDetailSql, new {
                        LedgerID = newLedgerId,
                        LedgerGroupID = ledgerGroupId,
                        CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                        UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                        FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                        FieldName = "ISLedgerActive",
                        FieldValue = "True",
                        ParentFieldName = "ISLedgerActive",
                        ParentFieldValue = "True",
                        CreatedDate = DateTime.Now,
                        CreatedBy = 2
                    }, transaction: transaction);
                }

                transaction.Commit();
                result.Success = true;
                result.ImportedRows = validRows.Count;
                result.Message = $"Successfully imported {validRows.Count} Ledgers into Group {ledgerGroupId} ({moduleName}).";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                result.Success = false;
                result.Message = $"Database Error: {ex.Message}";
                result.ErrorMessages.Add(ex.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"System Error: {ex.Message}");
            return result;
        }
    }

    public async Task<List<LedgerGroupDto>> GetLedgerGroupsAsync()
    {
        try
        {
            await EnsureConnectionOpenAsync();

            // Query based on the old VB code - fetches ledger groups
            string query = @"
                SELECT DISTINCT
                    LGM.LedgerGroupID,
                    LGM.LedgerGroupName,
                    LGM.LedgerGroupNameDisplay,
                    LGM.LedgerGroupNameID
                FROM LedgerGroupMaster AS LGM
                WHERE LGM.IsDeletedTransaction = 0
                    AND LGM.CompanyID = 2
                    AND LGM.LedgerGroupID IN (1,2,3,4,7,8)
                ORDER BY LGM.LedgerGroupID";

            var result = await _connection.QueryAsync<LedgerGroupDto>(query);
            return result.ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to fetch ledger groups: {ex.Message}", ex);
        }
    }

    public async Task<List<MasterColumnDto>> GetMasterColumnsAsync(int ledgerGroupId)
    {
        try
        {
            await EnsureConnectionOpenAsync();

            // STEP 1: Try to get LedgerGroupName and use default columns
            try
            {
                var groupName = await _connection.ExecuteScalarAsync<string>(
                    "SELECT LedgerGroupName FROM LedgerGroupMaster WHERE LedgerGroupID = @Id AND IsDeletedTransaction = 0",
                    new { Id = ledgerGroupId });

                if (!string.IsNullOrEmpty(groupName))
                {
                    var defaultColumns = GetDefaultLedgerColumns(groupName);
                    if (defaultColumns != null && defaultColumns.Any())
                    {
                        return defaultColumns;
                    }
                }
            }
            catch
            {
                // If getting default columns fails, continue to fallback logic
            }

            // STEP 2: Fallback to complex logic
            // First, check what columns exist in LedgerGroupMaster
            string checkColumnsQuery = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'LedgerGroupMaster'
                AND COLUMN_NAME IN ('SelectQuery', 'TableName', 'LedgerGroupName')";

            var availableColumns = await _connection.QueryAsync<string>(checkColumnsQuery);
            var columnsList = availableColumns.ToList();

            // Build dynamic query based on available columns
            var selectParts = new List<string> { "LedgerGroupID" };
            if (columnsList.Contains("SelectQuery")) selectParts.Add("SelectQuery");
            if (columnsList.Contains("TableName")) selectParts.Add("TableName");
            if (columnsList.Contains("LedgerGroupName")) selectParts.Add("LedgerGroupName");

            string queryForLedgerGroup = $@"
                SELECT {string.Join(", ", selectParts)}
                FROM LedgerGroupMaster
                WHERE CompanyID = 2
                    AND LedgerGroupID = @LedgerGroupId
                    AND ISNULL(IsDeletedTransaction, 0) <> 1";

            var ledgerGroupData = await _connection.QueryFirstOrDefaultAsync<dynamic>(
                queryForLedgerGroup,
                new { LedgerGroupId = ledgerGroupId });

            if (ledgerGroupData == null)
            {
                throw new Exception($"Ledger Group with ID {ledgerGroupId} not found");
            }

            var dict = ledgerGroupData as IDictionary<string, object>;
            string? selectQuery = dict != null && dict.ContainsKey("SelectQuery") ? dict["SelectQuery"]?.ToString() : null;
            string tableName = dict != null && dict.ContainsKey("TableName") ? dict["TableName"]?.ToString() ?? "LedgerMaster" : "LedgerMaster";
            string ledgerGroupName = dict != null && dict.ContainsKey("LedgerGroupName") ? dict["LedgerGroupName"]?.ToString() ?? "" : "";

            Console.WriteLine($"[DEBUG] LedgerGroupID={ledgerGroupId}, LedgerGroupName='{ledgerGroupName}', SelectQuery={selectQuery ?? "NULL"}");

            // If SelectQuery is null or empty, use default schema
            if (string.IsNullOrEmpty(selectQuery))
            {
                // DEBUG: Log the ledgerGroupName
                Console.WriteLine($"[DEBUG] GetMasterColumns - LedgerGroupID: {ledgerGroupId}, LedgerGroupName: '{ledgerGroupName}'");

                // ALWAYS try to get default schema based on Ledger Group Name first
                var defaultColumns = GetDefaultLedgerColumns(ledgerGroupName);
                Console.WriteLine($"[DEBUG] GetDefaultLedgerColumns returned {defaultColumns?.Count ?? 0} columns");

                if (defaultColumns != null && defaultColumns.Any())
                {
                    // Return default columns immediately - don't fall back to database
                    Console.WriteLine($"[DEBUG] Returning {defaultColumns.Count} default columns");
                    return defaultColumns;
                }
                Console.WriteLine("[DEBUG] No default columns found, falling back to database query");

                // Fallback: Get distinct field names from existing LedgerMasterDetails for this group (only if no defaults)
                string fallbackQuery = @"
                    SELECT DISTINCT
                        FieldName,
                        'string' as DataType,
                        CAST(0 AS BIT) as IsRequired
                    FROM LedgerMasterDetails
                    WHERE LedgerGroupID = @LedgerGroupId
                        AND CompanyID = 2
                    ORDER BY FieldName";

                var fallbackColumns = await _connection.QueryAsync<dynamic>(fallbackQuery, new { LedgerGroupId = ledgerGroupId });

                if (fallbackColumns != null && fallbackColumns.Any())
                {
                    var fallbackResult = new List<MasterColumnDto>();
                    int fallbackSequence = 1;

                    foreach (var col in fallbackColumns)
                    {
                        var colDict = col as IDictionary<string, object>;
                        if (colDict != null && colDict.ContainsKey("FieldName"))
                        {
                            fallbackResult.Add(new MasterColumnDto
                            {
                                FieldName = colDict["FieldName"]?.ToString() ?? "",
                                DataType = "string",
                                IsRequired = false,
                                SequenceNo = fallbackSequence++
                            });
                        }
                    }

                    // If we found columns from existing data, return them
                    if (fallbackResult.Any())
                    {
                        return fallbackResult;
                    }
                }

                // If still no columns, check if stored procedure exists
                string checkProcQuery = @"
                    SELECT COUNT(*)
                    FROM sys.objects
                    WHERE type = 'P' AND name = 'GetLedgerMasterData'";

                var procExists = await _connection.ExecuteScalarAsync<int>(checkProcQuery);

                if (procExists > 0)
                {
                    selectQuery = "GetLedgerMasterData";
                }
                else
                {
                    // No stored procedure and no SelectQuery - return empty list
                    return new List<MasterColumnDto>();
                }
            }

            // Prepare parameters for the stored procedure or query
            var parameters = new DynamicParameters();
            parameters.Add("@TblName", tableName);
            parameters.Add("@CompanyID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2);
            parameters.Add("@LedgerGroupID", ledgerGroupId);

            // Execute the query/stored procedure
            IEnumerable<dynamic> columns;
            try
            {
                // Check if it's a stored procedure call
                bool isStoredProc = !selectQuery!.Trim().ToUpper().StartsWith("SELECT");

                if (isStoredProc)
                {
                    columns = await _connection.QueryAsync<dynamic>(
                        selectQuery,
                        parameters,
                        commandType: System.Data.CommandType.StoredProcedure);
                }
                else
                {
                    columns = await _connection.QueryAsync<dynamic>(selectQuery, parameters);
                }

                // If no columns returned, return empty list
                if (columns == null || !columns.Any())
                {
                    return new List<MasterColumnDto>();
                }
            }
            catch (Exception ex)
            {
                // If execution fails, log and return empty list instead of throwing
                Console.WriteLine($"[ERROR] GetMasterColumnsAsync failed: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                return new List<MasterColumnDto>();
            }

            var result = new List<MasterColumnDto>();
            int sequence = 1;

            foreach (var col in columns)
            {
                var colDict = col as IDictionary<string, object>;
                if (colDict != null && colDict.ContainsKey("FieldName"))
                {
                    result.Add(new MasterColumnDto
                    {
                        FieldName = colDict["FieldName"]?.ToString() ?? "",
                        DataType = colDict.ContainsKey("DataType") ? colDict["DataType"]?.ToString() ?? "string" : "string",
                        IsRequired = colDict.ContainsKey("IsRequired") && Convert.ToBoolean(colDict["IsRequired"]),
                        SequenceNo = sequence++
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Log detailed error
            var errorMsg = $"Failed to fetch master columns for LedgerGroupID {ledgerGroupId}: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMsg += $" Inner: {ex.InnerException.Message}";
            }
            throw new Exception(errorMsg, ex);
        }
    }

    private List<MasterColumnDto> GetDefaultLedgerColumns(string ledgerGroupName)
    {
        // Provide default column schemas for common ledger groups
        var columns = new List<MasterColumnDto>();

        // Normalize the ledger group name for comparison
        var normalizedName = ledgerGroupName?.Trim().ToLower() ?? "";

        // Common columns for all ledger types
        var commonColumns = new[]
        {
            new { Name = "LedgerName", Required = true, Seq = 1 },
            new { Name = "LedgerDescription", Required = false, Seq = 2 },
            new { Name = "Address", Required = false, Seq = 3 },
            new { Name = "City", Required = false, Seq = 4 },
            new { Name = "State", Required = false, Seq = 5 },
            new { Name = "Country", Required = false, Seq = 6 },
            new { Name = "PinCode", Required = false, Seq = 7 },
            new { Name = "Phone", Required = false, Seq = 8 },
            new { Name = "Email", Required = false, Seq = 9 },
            new { Name = "GSTNo", Required = false, Seq = 10 },
            new { Name = "PANNO", Required = false, Seq = 11 },
            new { Name = "MailingName", Required = false, Seq = 12 },
            new { Name = "Address1", Required = false, Seq = 13 },
            new { Name = "Address2", Required = false, Seq = 14 },
            new { Name = "MobileNo", Required = false, Seq = 15 },
            new { Name = "GSTApplicable", Required = false, Seq = 16 },
            new { Name = "TelephoneNo", Required = false, Seq = 17 },
            new { Name = "Website", Required = false, Seq = 18 },
            new { Name = "LegalName", Required = false, Seq = 19 },
            new { Name = "SupplyTypeCode", Required = false, Seq = 20 }
        };

        // Consignee specific
        if (normalizedName.Contains("consignee"))
        {
            int seq = 1;
            columns.Add(new MasterColumnDto { FieldName = "LedgerName", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ClientName", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ConsigneeCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ContactPerson", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "City", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "State", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Country", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PinCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Phone", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Mobile", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Email", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSTNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PANNO", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MailingName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address1", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address2", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MobileNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSTApplicable", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "TelephoneNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Website", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "LegalName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "SupplyTypeCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            return columns;
        }

        // Clients/Customers specific (includes "Sundry Debtors" accounting term)
        if (normalizedName.Contains("debtor") || normalizedName.Contains("customer") || normalizedName.Contains("client"))
        {
            int seq = 1;
            columns.Add(new MasterColumnDto { FieldName = "LedgerName", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "CustomerCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ContactPerson", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "City", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "State", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Country", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PinCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Phone", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Mobile", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Email", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSTNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PANNO", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "CreditLimit", DataType = "number", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "CreditDays", DataType = "number", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MailingName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address1", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address2", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MobileNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSTApplicable", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "TelephoneNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Website", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "LegalName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "SupplyTypeCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            return columns;
        }

        // Suppliers/Vendors specific (includes "Sundry Creditors" accounting term)
        if (normalizedName.Contains("creditor") || normalizedName.Contains("supplier") || normalizedName.Contains("vendor"))
        {
            int seq = 1;
            columns.Add(new MasterColumnDto { FieldName = "LedgerName", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "SupplierCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ContactPerson", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "City", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "State", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Country", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PinCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Phone", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Mobile", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Email", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSTNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PANNO", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PaymentTerms", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "CreditDays", DataType = "number", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "BankName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "AccountNumber", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "IFSCCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MailingName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address1", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address2", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MobileNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSTApplicable", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "TelephoneNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Website", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "LegalName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "SupplyTypeCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            return columns;
        }

        // Employee specific
        if (normalizedName.Contains("employee"))
        {
            int seq = 1;
            columns.Add(new MasterColumnDto { FieldName = "LedgerName", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "EmployeeCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Designation", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "DepartmentName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "DepartmentID", DataType = "bigint", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "JoiningDate", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "City", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "State", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Country", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PinCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Phone", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Mobile", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Email", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PANNO", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            // Add other common columns if needed
            columns.Add(new MasterColumnDto { FieldName = "Address1", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Address2", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MobileNo", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            return columns;
        }

        // Default generic ledger columns for any other type
        if (!string.IsNullOrEmpty(normalizedName))
        {
            foreach (var col in commonColumns)
            {
                columns.Add(new MasterColumnDto
                {
                    FieldName = col.Name,
                    DataType = "string",
                    IsRequired = col.Required,
                    SequenceNo = col.Seq
                });
            }
            return columns;
        }

        // Return empty if no ledger group name provided
        return new List<MasterColumnDto>();
    }

    public async Task<ImportResultDto> ImportLedgerMasterWithValidationAsync(Stream fileStream, int ledgerGroupId)
    {
        var result = new ImportResultDto();
        try
        {
            // 1. Get Master Columns for validation
            var masterColumns = await GetMasterColumnsAsync(ledgerGroupId);

            // 2. Get Ledger Group Details
            await EnsureConnectionOpenAsync();
            var ledgerGroup = await _connection.QueryFirstOrDefaultAsync<LedgerGroupDto>(
                "SELECT LedgerGroupID, LedgerGroupName FROM LedgerGroupMaster WHERE LedgerGroupID = @Id AND IsDeletedTransaction = 0",
                new { Id = ledgerGroupId });

            if (ledgerGroup == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("Invalid Ledger Group ID.");
                return result;
            }

            // 3. Read and Validate Excel
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            result.TotalRows = worksheet.Dimension.Rows - 1;

            // 4. Map Headers
            var headerMap = new Dictionary<string, int>();
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(header))
                {
                    headerMap[header] = col;
                }
            }

            // Helper to get value
            string? GetValue(string colName, int rowIdx)
            {
                // direct match
                if (headerMap.ContainsKey(colName)) 
                    return GetCellValue(rowIdx, headerMap[colName]);

                // case-insensitive match
                var key = headerMap.Keys.FirstOrDefault(k => k.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (key != null) return GetCellValue(rowIdx, headerMap[key]);

                // fuzzy match (ignore spaces) e.g. "Mobile No" matches "MobileNo"
                var fuzzyKey = headerMap.Keys.FirstOrDefault(k => k.Replace(" ", "").Equals(colName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                if (fuzzyKey != null) return GetCellValue(rowIdx, headerMap[fuzzyKey]);

                return null;
            }

            string? GetCellValue(int rowIdx, int colIndex)
            {
                var cellValue = worksheet.Cells[rowIdx, colIndex].Value;
                if (cellValue == null || cellValue == DBNull.Value) return null;
                var stringValue = cellValue.ToString()?.Trim();
                return string.IsNullOrEmpty(stringValue) ? null : stringValue;
            }

            string? SanitizeNumeric(string? input)
            {
                if (string.IsNullOrEmpty(input)) return null;
                // Keep only digits
                var numeric = new string(input.Where(char.IsDigit).ToArray());
                return string.IsNullOrEmpty(numeric) ? null : numeric;
            }

            // 5. Validate Required Columns (only if masterColumns is defined)
            // ==========================================
            // EMPLOYEE-SPECIFIC: Build Department Lookup Cache
            // ==========================================
            // Check if it's an Employee import (LedgerGroup usually named "Employee" or similar)
            bool isEmployeeImport = ledgerGroup?.LedgerGroupName?.IndexOf("Employee", StringComparison.OrdinalIgnoreCase) >= 0;
            Dictionary<string, int> departmentLookupCache = null;

            if (isEmployeeImport)
            {
                try 
                {
                    var departments = await _connection.QueryAsync<(int DepartmentID, string DepartmentName)>(
                        "SELECT DepartmentID, DepartmentName FROM DepartmentMaster WHERE IsDeletedTransaction = 0");
                    
                    departmentLookupCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var dep in departments)
                    {
                        if (!string.IsNullOrEmpty(dep.DepartmentName))
                        {
                            departmentLookupCache[dep.DepartmentName.Trim()] = dep.DepartmentID;
                        }
                    }
                }
                catch 
                {
                    // Ignore if DepartmentMaster doesn't exist or error
                }
            }

            var missingColumns = new List<string>();
            if (masterColumns.Any())
            {
                foreach (var masterCol in masterColumns.Where(c => c.IsRequired))
                {
                    if (!headerMap.ContainsKey(masterCol.FieldName))
                    {
                        missingColumns.Add(masterCol.FieldName);
                    }
                }

                if (missingColumns.Any())
                {
                    result.Success = false;
                    result.ErrorMessages.Add($"Missing required columns: {string.Join(", ", missingColumns)}");
                    return result;
                }
            }

            // 6. Process Rows with Duplicate Validation
            var validRows = new List<Dictionary<string, object?>>();
            var errorMessages = new List<string>();
            var excelCompositeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ==========================================
            // CONSIGNEE-SPECIFIC: Build Client Lookup Cache
            // ==========================================
            // LedgerGroupID = 4 for Consignee
            // LedgerGroupID = 1 for Clients
            bool isConsigneeImport = (ledgerGroupId == 4);
            Dictionary<string, int> clientLookupCache = null;

            if (isConsigneeImport)
            {
                // Fetch all clients from LedgerMaster (LedgerGroupID = 1) for lookup
                string clientLookupQuery = @"
                    SELECT LedgerID, LedgerName
                    FROM LedgerMaster
                    WHERE LedgerGroupID = 1
                        AND CompanyID = 2
                        AND IsDeletedTransaction = 0
                        AND LedgerName IS NOT NULL";

                var clients = await _connection.QueryAsync<(int LedgerID, string LedgerName)>(clientLookupQuery);
                clientLookupCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var client in clients)
                {
                    if (!string.IsNullOrEmpty(client.LedgerName))
                    {
                        // Use trimmed LedgerName as key for case-insensitive lookup
                        clientLookupCache[client.LedgerName.Trim()] = client.LedgerID;
                    }
                }
            }

            // Fetch existing keys from database for duplicate check
            string duplicateCheckQuery;

            if (isConsigneeImport)
            {
                // CONSIGNEE: Use LedgerName + ClientName (from RefClientID lookup)
                duplicateCheckQuery = @"
                    SELECT
                        LTRIM(RTRIM(ISNULL(L.LedgerName, ''))) + '|' + LTRIM(RTRIM(ISNULL(C.LedgerName, ''))) AS CompositeKey
                    FROM LedgerMaster L
                    LEFT JOIN LedgerMaster C ON L.RefClientID = C.LedgerID
                    WHERE L.LedgerGroupID = @LedgerGroupId
                        AND L.CompanyID = 2
                        AND L.IsDeletedTransaction = 0
                        AND L.LedgerName IS NOT NULL
                        AND LTRIM(RTRIM(L.LedgerName)) <> ''";
            }
            else
            {
                // OTHER LEDGER GROUPS: Use LedgerName + GSTNo + PANNO (or just LedgerName if GST/PAN empty)
                duplicateCheckQuery = @"
                    SELECT
                        CASE
                            WHEN LTRIM(RTRIM(ISNULL(GSTNo, ''))) <> '' OR LTRIM(RTRIM(ISNULL(PANNO, ''))) <> ''
                            THEN LTRIM(RTRIM(ISNULL(LedgerName, ''))) + '|' + LTRIM(RTRIM(ISNULL(GSTNo, ''))) + '|' + LTRIM(RTRIM(ISNULL(PANNO, '')))
                            ELSE LTRIM(RTRIM(ISNULL(LedgerName, '')))
                        END AS CompositeKey
                    FROM LedgerMaster
                    WHERE LedgerGroupID = @LedgerGroupId
                        AND CompanyID = 2
                        AND IsDeletedTransaction = 0
                        AND LedgerName IS NOT NULL
                        AND LTRIM(RTRIM(LedgerName)) <> ''";
            }

            var existingCompositeKeys = await _connection.QueryAsync<string>(
                duplicateCheckQuery,
                new { LedgerGroupId = ledgerGroupId });
            var dbCompositeKeys = new HashSet<string>(
                existingCompositeKeys.Where(k => !string.IsNullOrWhiteSpace(k)),
                StringComparer.OrdinalIgnoreCase);

            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                // Check empty row
                bool isRowEmpty = true;
                for (int c = 1; c <= worksheet.Dimension.Columns; c++)
                {
                    if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, c].Value?.ToString()))
                    {
                        isRowEmpty = false;
                        break;
                    }
                }
                if (isRowEmpty) continue;

                // Construct Row Data
                var rowData = new Dictionary<string, object?>();
                var rowErrors = new List<string>();

                // CRITICAL FIX: Always read ALL Excel columns first
                // This ensures custom fields like RefCode are captured
                foreach (var header in headerMap.Keys)
                {
                    var value = GetValue(header, row);
                    rowData[header] = value;
                }

                // Then perform validation if masterColumns are defined
                if (masterColumns.Any())
                {
                    // Validate required fields from master column definitions
                    foreach (var masterCol in masterColumns)
                    {
                        var value = GetValue(masterCol.FieldName, row);

                        // Validate required fields
                        if (masterCol.IsRequired && string.IsNullOrEmpty(value))
                        {
                            rowErrors.Add($"Row {row}: {masterCol.FieldName} is required.");
                        }
                        
                        // Update rowData with validated value (in case header name differs)
                        rowData[masterCol.FieldName] = value;
                    }
                }

                // ==========================================
                // CONSIGNEE-SPECIFIC: ClientName Validation & Lookup
                // ==========================================
                if (isConsigneeImport)
                {
                    // Get ClientName from Excel
                    var clientName = GetFieldValue(rowData, "ClientName")?.Trim();

                    // Validation: ClientName is required for Consignee
                    if (string.IsNullOrEmpty(clientName))
                    {
                        rowErrors.Add($"Row {row}: ClientName is required for Consignee import.");
                    }
                    else
                    {
                        // Lookup: Find ClientName in Client Master (LedgerGroupID = 1)
                        if (clientLookupCache != null && clientLookupCache.TryGetValue(clientName, out int refClientId))
                        {
                            // SUCCESS: Store RefClientID for later use in INSERT
                            rowData["RefClientID"] = refClientId;
                        }
                        else
                        {
                            // FAILURE: ClientName not found in Client Master
                            rowErrors.Add($"Row {row}: ClientName '{clientName}' not found in Client Master (LedgerGroupID = 1).");
                        }
                    }
                }

                // ==========================================
                // EMPLOYEE-SPECIFIC: Department Name -> ID Lookup
                // ==========================================
                if (isEmployeeImport)
                {
                    // 1. Check 'DepartmentName' column first
                    var depName = GetFieldValue(rowData, "DepartmentName")?.Trim();
                    
                    // 2. If empty, check if 'DepartmentID' column has text (User mistakenly put Name in ID column)
                    if (string.IsNullOrEmpty(depName))
                    {
                        var rawId = GetFieldValue(rowData, "DepartmentID")?.Trim();
                        // If rawId is NOT numeric, treat it as Name
                        if (!string.IsNullOrEmpty(rawId) && !long.TryParse(rawId, out _))
                        {
                            depName = rawId;
                        }
                    }

                    if (!string.IsNullOrEmpty(depName))
                    {
                        if (departmentLookupCache != null && departmentLookupCache.TryGetValue(depName, out int depId))
                        {
                            // Found! Store as DepartmentID (for Details table)
                            rowData["DepartmentID"] = depId;
                        }
                        else
                        {
                            // Not found in cache. 
                            // If we tried to use ID column as Name, but it failed lookup, 
                            // we must clear it or set it to 0 so SQL doesn't crash trying to insert text into BigInt.
                            // But usually, we just let it fail? No, user wants it fixed.
                            // Better: Validate it.
                             var rawId = GetFieldValue(rowData, "DepartmentID")?.Trim();
                             if (!string.IsNullOrEmpty(rawId) && !long.TryParse(rawId, out _))
                             {
                                 // It's text, and lookup failed. We CANNOT send this text to DB.
                                 // Set to null or 0.
                                 rowErrors.Add($"Row {row}: Department '{depName}' not found in Department Master.");
                                 rowData["DepartmentID"] = null; // Prevent SQL error
                             }
                        }
                    }
                }

                // If there are row-specific errors, add them and skip this row
                if (rowErrors.Any())
                {
                    errorMessages.AddRange(rowErrors);
                    continue;
                }

                // ==========================================
                // DUPLICATE VALIDATION
                // ==========================================
                var ledgerName = GetFieldValue(rowData, "LedgerName")?.Trim() ?? "";

                // Skip duplicate validation if LedgerName is empty
                if (!string.IsNullOrEmpty(ledgerName))
                {
                    string compositeKey;
                    string duplicateContext;

                    if (isConsigneeImport)
                    {
                        // CONSIGNEE: Use LedgerName + ClientName as composite key
                        var clientName = GetFieldValue(rowData, "ClientName")?.Trim() ?? "";
                        compositeKey = $"{ledgerName}|{clientName}";
                        duplicateContext = $"LedgerName='{ledgerName}', ClientName='{clientName}'";
                    }
                    else
                    {
                        // OTHER LEDGER GROUPS: Use LedgerName + GSTNo + PANNO
                        var gstNo = GetFieldValue(rowData, "GSTNo")?.Trim() ?? "";
                        var panNo = GetFieldValue(rowData, "PANNO")?.Trim() ?? "";
                        bool hasGstOrPan = !string.IsNullOrEmpty(gstNo) || !string.IsNullOrEmpty(panNo);

                        if (hasGstOrPan)
                        {
                            // Use full composite key when GST or PAN is available
                            compositeKey = $"{ledgerName}|{gstNo}|{panNo}";
                            duplicateContext = $"LedgerName='{ledgerName}', GSTNo='{gstNo}', PANNO='{panNo}'";
                        }
                        else
                        {
                            // When both GST and PAN are empty, only check LedgerName
                            compositeKey = ledgerName;
                            duplicateContext = $"LedgerName='{ledgerName}'";
                        }
                    }

                    // Check for duplicate within Excel file
                    if (excelCompositeKeys.Contains(compositeKey))
                    {
                        errorMessages.Add($"Row {row}: Duplicate found within Excel file. {duplicateContext} combination already exists.");
                        result.DuplicateRows++;
                        continue;
                    }

                    // Check for duplicate in database
                    if (dbCompositeKeys.Contains(compositeKey))
                    {
                        errorMessages.Add($"Row {row}: Duplicate found in database. {duplicateContext} combination already exists.");
                        result.DuplicateRows++;
                        continue;
                    }

                    // Add to Excel composite keys set
                    excelCompositeKeys.Add(compositeKey);
                }

                validRows.Add(rowData);
            }

            // Helper function to get field value with case-insensitive key matching
            string? GetFieldValue(Dictionary<string, object?> data, string fieldName)
            {
                var key = data.Keys.FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                return key != null ? data[key]?.ToString() : null;
            }

            // ==========================================
            // VALIDATION RESULT CHECK
            // ==========================================
            // CRITICAL: If ANY validation errors exist, FAIL the entire import (all-or-nothing)
            if (errorMessages.Any())
            {
                result.Success = false;
                result.ErrorMessages = errorMessages;
                result.ErrorRows = errorMessages.Count - result.DuplicateRows;

                var messageParts = new List<string>();
                if (result.DuplicateRows > 0)
                    messageParts.Add($"{result.DuplicateRows} duplicate(s)");
                if (result.ErrorRows > 0)
                    messageParts.Add($"{result.ErrorRows} validation error(s)");

                result.Message = $"Import failed: {string.Join(", ", messageParts)}. No data imported (all-or-nothing validation).";
                return result;
            }

            if (!validRows.Any())
            {
                result.Success = true;
                result.Message = "No valid data found to import.";
                return result;
            }

            // 7. Execute Import (Transactional) - Similar to old VB code
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Get all LedgerMaster table columns to dynamically build INSERT
                string columnsQuery = @"
                    SELECT COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'LedgerMaster'
                    AND COLUMN_NAME NOT IN ('LedgerID')
                    ORDER BY ORDINAL_POSITION";

                var ledgerMasterColumns = await _connection.QueryAsync<string>(columnsQuery, transaction: transaction);
                var ledgerMasterColumnsList = ledgerMasterColumns.Select(c => c.ToLower()).ToList();

                // Get Prefix
                string prefix = "LGR";
                try
                {
                    var groupPrefix = await _connection.ExecuteScalarAsync<string>(
                        "SELECT LedgerGroupPrefix FROM LedgerGroupMaster WHERE LedgerGroupID = @GID",
                        new { GID = ledgerGroupId }, transaction: transaction);
                    if (!string.IsNullOrEmpty(groupPrefix)) prefix = groupPrefix;
                }
                catch {}

                // Get current Max Number
                var maxLedgerNoObj = await _connection.ExecuteScalarAsync<object>(
                    "SELECT MAX(MaxLedgerNo) FROM LedgerMaster WHERE LedgerGroupID = @GID AND IsDeletedTransaction = 0",
                    new { GID = ledgerGroupId }, transaction: transaction);

                long currentMaxNo = 0;
                if (maxLedgerNoObj != null && long.TryParse(maxLedgerNoObj.ToString(), out var parsedMax))
                {
                    currentMaxNo = parsedMax;
                }

                foreach (var rowData in validRows)
                {
                    currentMaxNo++;
                    var ledgerCode = $"{prefix}{currentMaxNo.ToString().PadLeft(5, '0')}";

                    // Get LedgerName from the row data
                    var ledgerName = rowData.ContainsKey("LedgerName") ? rowData["LedgerName"]?.ToString() : "";
                    if (string.IsNullOrEmpty(ledgerName))
                    {
                        // Fallback: use first field value as name
                        ledgerName = rowData.Values.FirstOrDefault()?.ToString() ?? $"Ledger_{currentMaxNo}";
                    }

                    // Build dynamic INSERT for LedgerMaster with all matching columns
                    var masterParams = new DynamicParameters();
                    var insertColumns = new List<string>();
                    var insertValues = new List<string>();

                    // Add system required columns
                    insertColumns.Add("LedgerCode");
                    insertValues.Add("@LedgerCode");
                    masterParams.Add("@LedgerCode", ledgerCode);

                    insertColumns.Add("MaxLedgerNo");
                    insertValues.Add("@MaxLedgerNo");
                    masterParams.Add("@MaxLedgerNo", currentMaxNo);

                    insertColumns.Add("LedgerCodePrefix");
                    insertValues.Add("@LedgerCodePrefix");
                    masterParams.Add("@LedgerCodePrefix", prefix);

                    insertColumns.Add("LedgerGroupID");
                    insertValues.Add("@LedgerGroupID");
                    masterParams.Add("@LedgerGroupID", ledgerGroupId);

                    insertColumns.Add("CompanyID");
                    insertValues.Add("@CompanyID");
                    masterParams.Add("@CompanyID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2);

                    insertColumns.Add("UserID");
                    insertValues.Add("@UserID");
                    masterParams.Add("@UserID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);

                    insertColumns.Add("FYear");
                    insertValues.Add("@FYear");
                    masterParams.Add("@FYear", (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"));

                    insertColumns.Add("CreatedDate");
                    insertValues.Add("@CreatedDate");
                    masterParams.Add("@CreatedDate", DateTime.Now);

                    insertColumns.Add("CreatedBy");
                    insertValues.Add("@CreatedBy");
                    masterParams.Add("@CreatedBy", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);

                    insertColumns.Add("ModifiedDate");
                    insertValues.Add("@ModifiedDate");
                    masterParams.Add("@ModifiedDate", DateTime.Now);

                    insertColumns.Add("ModifiedBy");
                    insertValues.Add("@ModifiedBy");
                    masterParams.Add("@ModifiedBy", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);

                    insertColumns.Add("IsDeletedTransaction");
                    insertValues.Add("@IsDeletedTransaction");
                    masterParams.Add("@IsDeletedTransaction", 0);

                    // Map Excel columns to LedgerMaster columns dynamically
                    foreach (var field in rowData)
                    {
                        var fieldNameLower = field.Key.ToLower().Replace(" ", "");

                        // Find matching column in LedgerMaster (case-insensitive, ignore spaces)
                        var matchingColumn = ledgerMasterColumnsList.FirstOrDefault(c =>
                            c.Replace(" ", "").Equals(fieldNameLower, StringComparison.OrdinalIgnoreCase));

                        if (matchingColumn != null && !insertColumns.Any(ic => ic.Equals(matchingColumn, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Get the actual column name with proper casing
                            var actualColumnName = ledgerMasterColumns.First(c =>
                                c.ToLower() == matchingColumn);

                            var value = field.Value?.ToString();

                            // Sanitize known numeric columns
                            if (new[] { "MobileNo", "Phone", "Mobile", "CreditLimit", "CreditDays", "DepartmentID" }.Contains(actualColumnName, StringComparer.OrdinalIgnoreCase))
                            {
                                // For CreditLimit/Days allow decimals? Bigint implies integer.
                                // If error is nvarchar -> bigint, likely MobileNo/Phone.
                                // If CreditLimit is decimal, stripping non-digits is risky (removes decimal point).
                                // Let's simplify: simple sanitization for Phone/Mobile
                                if (actualColumnName.Contains("Mobile") || actualColumnName.Contains("Phone"))
                                {
                                    value = SanitizeNumeric(value);
                                }
                            }

                            insertColumns.Add(actualColumnName);
                            insertValues.Add($"@{actualColumnName}");
                            masterParams.Add($"@{actualColumnName}", value);
                        }
                    }

                    // Special handling for required fields if not already added
                    if (!insertColumns.Any(c => c.Equals("LedgerName", StringComparison.OrdinalIgnoreCase)))
                    {
                        insertColumns.Add("LedgerName");
                        insertValues.Add("@LedgerName");
                        masterParams.Add("@LedgerName", ledgerName);
                    }

                    if (!insertColumns.Any(c => c.Equals("LedgerDescription", StringComparison.OrdinalIgnoreCase)))
                    {
                        insertColumns.Add("LedgerDescription");
                        insertValues.Add("@LedgerDescription");
                        masterParams.Add("@LedgerDescription", $"{ledgerGroup.LedgerGroupName}: {ledgerName}");
                    }

                    if (!insertColumns.Any(c => c.Equals("LedgerType", StringComparison.OrdinalIgnoreCase)))
                    {
                        insertColumns.Add("LedgerType");
                        insertValues.Add("@LedgerType");
                        masterParams.Add("@LedgerType", ledgerGroup.LedgerGroupName);
                    }

                    if (!insertColumns.Any(c => c.Equals("ISLedgerActive", StringComparison.OrdinalIgnoreCase)))
                    {
                        insertColumns.Add("ISLedgerActive");
                        insertValues.Add("@ISLedgerActive");
                        masterParams.Add("@ISLedgerActive", true);
                    }

                    // CONSIGNEE-SPECIFIC: Ensure RefClientID is added for Consignee imports
                    if (isConsigneeImport && rowData.ContainsKey("RefClientID"))
                    {
                        if (!insertColumns.Any(c => c.Equals("RefClientID", StringComparison.OrdinalIgnoreCase)))
                        {
                            // Check if RefClientID column exists in LedgerMaster
                            if (ledgerMasterColumnsList.Any(c => c.Equals("refclientid", StringComparison.OrdinalIgnoreCase)))
                            {
                                insertColumns.Add("RefClientID");
                                insertValues.Add("@RefClientID");
                                masterParams.Add("@RefClientID", rowData["RefClientID"]);
                            }
                        }
                    }

                    // Build and execute dynamic INSERT
                    string insertMasterSql = $@"
                        INSERT INTO LedgerMaster ({string.Join(", ", insertColumns)})
                        VALUES ({string.Join(", ", insertValues)});
                        SELECT SCOPE_IDENTITY();";

                    var ledgerIdObj = await _connection.ExecuteScalarAsync<object>(insertMasterSql, masterParams, transaction: transaction);
                    int newLedgerId = Convert.ToInt32(ledgerIdObj);

                    // Insert Details (LedgerMasterDetails) - One row per field
                    string insertDetailSql = @"
                        INSERT INTO LedgerMasterDetails (
                            LedgerID, LedgerGroupID, CompanyID, UserID, FYear,
                            FieldName, FieldValue, ParentFieldName, ParentFieldValue, ParentLedgerID,
                            SequenceNo, CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
                        ) VALUES (
                            @LedgerID, @LedgerGroupID, @CompanyID, @UserID, @FYear,
                            @FieldName, @FieldValue, @ParentFieldName, @ParentFieldValue, @ParentLedgerID,
                            @SequenceNo, @CreatedDate, @CreatedBy, @CreatedDate, @CreatedBy
                        )";

                    int sequenceNo = 1;

                    if (masterColumns.Any())
                    {
                        // STEP 1: Use master column definitions first
                        var insertedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        
                        foreach (var masterCol in masterColumns)
                        {
                            var fieldValue = rowData.ContainsKey(masterCol.FieldName) ? rowData[masterCol.FieldName]?.ToString() : "";

                            // Skip empty non-required fields
                            if (string.IsNullOrEmpty(fieldValue) && fieldValue == null)
                            {
                                continue;
                            }

                            await _connection.ExecuteAsync(insertDetailSql, new {
                                LedgerID = newLedgerId,
                                LedgerGroupID = ledgerGroupId,
                                CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                                UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                                FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                                FieldName = masterCol.FieldName,
                                FieldValue = fieldValue ?? "",
                                ParentFieldName = masterCol.FieldName,
                                ParentFieldValue = fieldValue ?? "",
                                ParentLedgerID = 0,
                                SequenceNo = sequenceNo++,
                                CreatedDate = DateTime.Now,
                                CreatedBy = 2
                            }, transaction: transaction);
                            
                            // Track inserted fields
                            insertedFields.Add(masterCol.FieldName);
                        }
                        
                        // STEP 2: Insert any additional fields from Excel that weren't in masterColumns
                        // This ensures fields like RefCode, custom columns, etc. are saved
                        Console.WriteLine($"[DEBUG] Starting STEP 2 - Processing additional Excel fields for LedgerID={newLedgerId}");
                        Console.WriteLine($"[DEBUG] Total fields in rowData: {rowData.Count}");
                        Console.WriteLine($"[DEBUG] Already inserted via masterColumns: {string.Join(", ", insertedFields)}");
                        
                        foreach (var field in rowData)
                        {
                            Console.WriteLine($"[DEBUG] Checking field: '{field.Key}' with value: '{field.Value}'");
                            
                            // Skip if already inserted via masterColumns
                            if (insertedFields.Contains(field.Key))
                            {
                                Console.WriteLine($"[DEBUG] >>> Skipped '{field.Key}' - already inserted via masterColumns");
                                continue;
                            }
                            
                            var fieldValue = field.Value?.ToString();

                            // Skip empty fields
                            if (string.IsNullOrEmpty(fieldValue))
                            {
                                Console.WriteLine($"[DEBUG] >>> Skipped '{field.Key}' - value is null or empty");
                                continue;
                            }

                            Console.WriteLine($"[DEBUG] >>> Inserting '{field.Key}' = '{fieldValue}' into LedgerMasterDetails");
                            
                            await _connection.ExecuteAsync(insertDetailSql, new {
                                LedgerID = newLedgerId,
                                LedgerGroupID = ledgerGroupId,
                                CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                                UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                                FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                                FieldName = field.Key,
                                FieldValue = fieldValue ?? "",
                                ParentFieldName = field.Key,
                                ParentFieldValue = fieldValue ?? "",
                                ParentLedgerID = 0,
                                SequenceNo = sequenceNo++,
                                CreatedDate = DateTime.Now,
                                CreatedBy = 2
                            }, transaction: transaction);
                            
                            Console.WriteLine($"[DEBUG] >>> Successfully inserted '{field.Key}'");
                        }
                        
                        Console.WriteLine($"[DEBUG] Completed STEP 2 - Additional field processing");
                    }
                    else
                    {
                        // No master columns - use all rowData fields
                        foreach (var field in rowData)
                        {
                            var fieldValue = field.Value?.ToString();

                            // Skip empty fields
                            if (string.IsNullOrEmpty(fieldValue))
                            {
                                continue;
                            }

                            await _connection.ExecuteAsync(insertDetailSql, new {
                                LedgerID = newLedgerId,
                                LedgerGroupID = ledgerGroupId,
                                CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                                UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                                FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                                FieldName = field.Key,
                                FieldValue = fieldValue ?? "",
                                ParentFieldName = field.Key,
                                ParentFieldValue = fieldValue ?? "",
                                ParentLedgerID = 0,
                                SequenceNo = sequenceNo++,
                                CreatedDate = DateTime.Now,
                                CreatedBy = 2
                            }, transaction: transaction);
                        }
                    }

                    // CRITICAL FIX: Explicitly insert ISLedgerActive into LedgerMasterDetails
                    // This ensures ISLedgerActive is saved for ALL Ledger Groups (Clients, Suppliers, Employees, Consignees, etc.)
                    // Legacy VB code always inserted this as a separate row with both FieldName and ParentFieldName = "ISLedgerActive"
                    await _connection.ExecuteAsync(insertDetailSql, new {
                        LedgerID = newLedgerId,
                        LedgerGroupID = ledgerGroupId,
                        CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                        UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                        FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                        FieldName = "ISLedgerActive",
                        FieldValue = "True",
                        ParentFieldName = "ISLedgerActive",
                        ParentFieldValue = "True",
                        ParentLedgerID = 0,
                        SequenceNo = sequenceNo++,
                        CreatedDate = DateTime.Now,
                        CreatedBy = 2
                    }, transaction: transaction);

                    // CLIENTS-SPECIFIC: Insert default fields for Clients Ledger Group (LedgerGroupID = 1)
                    if (ledgerGroupId == 1) // Clients
                    {
                        // 1. GSTRegistrationType: Use user-provided value if present, otherwise default to 'Unknown'
                        var gstRegistrationType = GetFieldValue(rowData, "GSTRegistrationType")?.Trim();
                        if (string.IsNullOrEmpty(gstRegistrationType))
                        {
                            gstRegistrationType = "Unknown";
                        }

                        await _connection.ExecuteAsync(insertDetailSql, new {
                            LedgerID = newLedgerId,
                            LedgerGroupID = ledgerGroupId,
                            CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                            UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                            FieldName = "GSTRegistrationType",
                            FieldValue = gstRegistrationType,
                            ParentFieldName = "GSTRegistrationType",
                            ParentFieldValue = gstRegistrationType,
                            ParentLedgerID = 0,
                            SequenceNo = sequenceNo++,
                            CreatedDate = DateTime.Now,
                            CreatedBy = 2
                        }, transaction: transaction);

                        // 2. PartyType: Always default to 'Not Applicable' for Clients
                        await _connection.ExecuteAsync(insertDetailSql, new {
                            LedgerID = newLedgerId,
                            LedgerGroupID = ledgerGroupId,
                            CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                            UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                            FieldName = "PartyType",
                            FieldValue = "Not Applicable",
                            ParentFieldName = "PartyType",
                            ParentFieldValue = "Not Applicable",
                            ParentLedgerID = 0,
                            SequenceNo = sequenceNo++,
                            CreatedDate = DateTime.Now,
                            CreatedBy = 2
                        }, transaction: transaction);
                    }
                }

                transaction.Commit();
                result.Success = true;
                result.ImportedRows = validRows.Count;

                // Build success message with duplicate/error info
                var messageBuilder = new System.Text.StringBuilder();
                messageBuilder.Append($"Successfully imported {validRows.Count} record(s) into {ledgerGroup.LedgerGroupName}.");

                if (result.DuplicateRows > 0 || result.ErrorRows > 0)
                {
                    messageBuilder.Append(" ");
                    var skippedParts = new List<string>();
                    if (result.DuplicateRows > 0)
                        skippedParts.Add($"{result.DuplicateRows} duplicate(s)");
                    if (result.ErrorRows > 0)
                        skippedParts.Add($"{result.ErrorRows} error(s)");

                    messageBuilder.Append($"Skipped: {string.Join(", ", skippedParts)}.");
                }

                result.Message = messageBuilder.ToString();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                result.Success = false;
                result.Message = $"Database Error: {ex.Message}";
                result.ErrorMessages.Add(ex.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"System Error: {ex.Message}");
            return result;
        }
    }

    // ==========================================
    // TOOL MASTER IMPORT METHODS
    // ==========================================

    public async Task<List<ToolGroupDto>> GetToolGroupsAsync()
    {
        await EnsureConnectionOpenAsync();
        
        var query = @"
            SELECT 
                ToolGroupID,
                ToolGroupName,
                ToolGroupNameDisplay
            FROM ToolGroupMaster
            WHERE CompanyID = 2
            AND ISNULL(IsDeletedTransaction, 0) = 0
            AND ToolGroupID < 10
            ORDER BY ToolGroupID";
        
        var toolGroups = await _connection.QueryAsync<ToolGroupDto>(query);
        return toolGroups.ToList();
    }

    private async Task<(string toolGroupPrefix, string toolGroupName, string toolNameFormula, string toolDescriptionFormula)?> GetToolGroupDetailsAsync(int toolGroupId)
    {
        await EnsureConnectionOpenAsync();
        
        var query = @"
            SELECT 
                ISNULL(ToolGroupPrefix, '') as ToolGroupPrefix,
                ISNULL(ToolGroupName, '') as ToolGroupName,
                ISNULL(ToolNameFormula, '') as ToolNameFormula,
                ISNULL(ToolDescriptionFormula, '') as ToolDescriptionFormula
            FROM ToolGroupMaster
            WHERE ToolGroupID = @ToolGroupId
            AND CompanyID = 2
            AND ISNULL(IsDeletedTransaction, 0) = 0";
        
        var result = await _connection.QueryFirstOrDefaultAsync<dynamic>(query, new { ToolGroupId = toolGroupId });
        
        if (result == null)
            return null;
            
        return (result.ToolGroupPrefix, result.ToolGroupName, result.ToolNameFormula, result.ToolDescriptionFormula);
    }

    private async Task<List<MasterColumnDto>> GetToolGroupColumnsAsync(int toolGroupId)
    {
        await EnsureConnectionOpenAsync();
        
        var query = @"
            SELECT 
                FieldName,
                ISNULL(FieldDataType, 'string') as DataType,
                CAST(ISNULL(IsRequiredFieldValidator, 0) AS BIT) as IsRequired,
                ISNULL(FieldDrawSequence, 0) as SequenceNo,
                ISNULL(UnitMeasurement, '') as UnitMeasurement
            FROM ToolGroupFieldMaster
            WHERE ToolGroupID = @ToolGroupId
            AND CompanyID = 2
            AND ISNULL(IsDeletedTransaction, 0) = 0
            ORDER BY FieldDrawSequence";
        
        var columns = await _connection.QueryAsync(query, new { ToolGroupId = toolGroupId });
        
        var result = new List<MasterColumnDto>();
        foreach (var col in columns)
        {
            var dict = col as IDictionary<string, object>;
            if (dict != null)
            {
                result.Add(new MasterColumnDto
                {
                    FieldName = dict["FieldName"]?.ToString() ?? "",
                    DataType = dict["DataType"]?.ToString() ?? "string",
                    IsRequired = dict["IsRequired"] != null && Convert.ToBoolean(dict["IsRequired"]),
                    SequenceNo = dict["SequenceNo"] != null ? Convert.ToInt32(dict["SequenceNo"]) : 0
                });
            }
        }
        
        return result;
    }

    private async Task<int?> GetProductHSNIdAsync(string hsnCode, string productHSNName)
    {
        await EnsureConnectionOpenAsync();
        
        var query = @"
            SELECT ProductHSNID 
            FROM ProductHSNMaster
            WHERE HSNCode = @HSNCode 
            AND ProductHSNName = @ProductHSNName
            AND ISNULL(IsDeletedTransaction, 0) = 0";
        
        var hsnId = await _connection.QueryFirstOrDefaultAsync<int?>(query, new
        {
            HSNCode = hsnCode,
            ProductHSNName = productHSNName
        });
        
        return hsnId;
    }

    private string GenerateDefaultFYear(DateTime createdDate)
    {
        // Financial year logic: April to March
        // If current month is April (4) or later, FYear = CurrentYear-NextYear
        // If current month is before April, FYear = PreviousYear-CurrentYear
        
        int currentYear = createdDate.Year;
        int previousYear = currentYear - 1;
        int nextYear = currentYear + 1;
        
        if (createdDate.Month >= 4) // April or later
        {
            return $"{currentYear}-{nextYear}";
        }
        else // January to March
        {
            return $"{previousYear}-{currentYear}";
        }
    }

    public async Task<ImportResultDto> ImportToolMasterAsync(Stream fileStream, string moduleName, int toolGroupId)
    {
        var result = new ImportResultDto
        {
            Success = false,
            Message = "",
            ImportedRows = 0,
            TotalRows = 0,
            ErrorMessages = new List<string>()
        };

        try
        {
            // 1. Get Tool Group Details (prefix, formulas)
            var toolGroupDetails = await GetToolGroupDetailsAsync(toolGroupId);
            if (toolGroupDetails == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("Invalid Tool Group ID.");
                return result;
            }

            var (toolGroupPrefix, toolGroupName, toolNameFormula, toolDescriptionFormula) = toolGroupDetails.Value;

            // 2. Get Tool Group Field Definitions
            var masterColumns = await GetToolGroupColumnsAsync(toolGroupId);

            if (!masterColumns.Any())
            {
                result.Success = false;
                result.ErrorMessages.Add("No field definitions found for this Tool Group.");
                return result;
            }

            // 3. Read and Validate Excel
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.Success = false;
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            // 4. Read Excel Headers and Data
            var headerMap = new Dictionary<string, int>();
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(headerValue))
                {
                    headerMap[headerValue] = col;
                }
            }

            // Helper function to get cell value
            string? GetValue(string columnName, int row)
            {
                if (headerMap.ContainsKey(columnName))
                {
                    return worksheet.Cells[row, headerMap[columnName]].Value?.ToString()?.Trim();
                }
                return null;
            }

            // 5. Process Rows
            var validRows = new List<Dictionary<string, object?>>();
            
            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                // Check if row is empty
                bool isRowEmpty = true;
                for (int c = 1; c <= worksheet.Dimension.Columns; c++)
                {
                    if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, c].Value?.ToString()))
                    {
                        isRowEmpty = false;
                        break;
                    }
                }
                if (isRowEmpty) continue;

                // Read ALL Excel columns first (to capture custom fields)
                var rowData = new Dictionary<string, object?>();
                foreach (var header in headerMap.Keys)
                {
                    var value = GetValue(header, row);
                    rowData[header] = value;
                }

                // Validate required fields from master columns
                var rowErrors = new List<string>();
                foreach (var masterCol in masterColumns)
                {
                    // Skip fields stored in ToolMaster table OR looked up dynamically
                    if (masterCol.FieldName.Equals("ToolRefCode", StringComparison.OrdinalIgnoreCase) ||
                        masterCol.FieldName.Equals("ToolName", StringComparison.OrdinalIgnoreCase) ||
                        masterCol.FieldName.Equals("ProductHSNID", StringComparison.OrdinalIgnoreCase) ||
                        masterCol.FieldName.Equals("HSNCode", StringComparison.OrdinalIgnoreCase) ||
                        masterCol.FieldName.Equals("ProductHSNName", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    // Normalize field name: "Ledger Name" or "ClientName" -> "LedgerName"
                    var normalizedFieldName = masterCol.FieldName;
                    if (masterCol.FieldName.Equals("Ledger Name", StringComparison.OrdinalIgnoreCase) ||
                        masterCol.FieldName.Equals("ClientName", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedFieldName = "LedgerName";
                    }
                    
                    var value = GetValue(masterCol.FieldName, row);
                    if (masterCol.IsRequired && string.IsNullOrEmpty(value))
                    {
                        rowErrors.Add($"Row {row}: {normalizedFieldName} is required.");
                    }
                    // Update rowData with normalized field name
                    rowData[normalizedFieldName] = value;
                }

                // NEW: Validate HSNCode and ProductHSNName are provided
                var hsnCode = GetValue("HSNCode", row);
                var productHSNName = GetValue("ProductHSNName", row);

                if (string.IsNullOrEmpty(hsnCode))
                {
                    rowErrors.Add($"Row {row}: HSNCode is required.");
                }
                if (string.IsNullOrEmpty(productHSNName))
                {
                    rowErrors.Add($"Row {row}: ProductHSNName is required.");
                }

                // NEW: Lookup ProductHSNId dynamically and store for both ToolMaster and ToolMasterDetails
                int? resolvedProductHSNId = null;
                if (!string.IsNullOrEmpty(hsnCode) && !string.IsNullOrEmpty(productHSNName))
                {
                    var productHSNId = await GetProductHSNIdAsync(hsnCode, productHSNName);
                    if (productHSNId == null)
                    {
                        rowErrors.Add($"Row {row}: No matching HSN found for HSNCode '{hsnCode}' and ProductHSNName '{productHSNName}'.");
                    }
                    else
                    {
                        // Store resolved ProductHSNId for ToolMaster
                        resolvedProductHSNId = productHSNId;
                        rowData["__RESOLVED_PRODUCTHSNID__"] = productHSNId.ToString();
                        
                        // Store ProductHSNID value in both ProductHSNID and ProductHSNName fields for ToolMasterDetails
                        rowData["ProductHSNID"] = productHSNId.ToString();
                        rowData["ProductHSNName"] = productHSNId.ToString(); // ProductHSNName field stores ProductHSNID value
                    }
                }

                if (rowErrors.Any())
                {
                    result.ErrorMessages.AddRange(rowErrors);
                    continue;
                }

                validRows.Add(rowData);
            }

            result.TotalRows = validRows.Count;

            if (!validRows.Any())
            {
                result.Success = false;
                result.ErrorMessages.Add("No valid rows found in the Excel file.");
                return result;
            }

            // 6. Insert into Database
            await EnsureConnectionOpenAsync();
            using var transaction = _connection.BeginTransaction();

            try
            {
                // Get max ToolCode number for this prefix
                var maxToolNoQuery = @"
                    SELECT ISNULL(MAX(MaxToolNo), 0) 
                    FROM ToolMaster 
                    WHERE Prefix = @Prefix 
                    AND CompanyID = 2 
                    AND ISNULL(IsDeletedTransaction, 0) = 0";

                var maxToolNo = await _connection.ExecuteScalarAsync<int>(maxToolNoQuery, new { Prefix = toolGroupPrefix }, transaction: transaction);

                // Generate default FYear if not provided in Excel
                var createdDate = DateTime.Now;
                var defaultFYear = GenerateDefaultFYear(createdDate);

                foreach (var rowData in validRows)
                {
                    // Generate Tool Code
                    maxToolNo++;
                    var toolCode = $"{toolGroupPrefix}{maxToolNo:D6}";

                    // Get ProductHSNID (resolved during validation using special key)
                    var productHSNId = 0;
                    if (rowData.ContainsKey("__RESOLVED_PRODUCTHSNID__") && int.TryParse(rowData["__RESOLVED_PRODUCTHSNID__"]?.ToString(), out var hsnId))
                    {
                        productHSNId = hsnId;
                    }

                    // Build ToolName dynamically based on formula
                    var toolName = "";
                    var toolDescription = "";
                    
                    if (!string.IsNullOrEmpty(toolNameFormula))
                    {
                        var nameBuilders = new List<string>();
                        foreach (var masterCol in masterColumns)
                        {
                            if (toolNameFormula.Contains(masterCol.FieldName))
                            {
                                var fieldValue = rowData.ContainsKey(masterCol.FieldName) ? rowData[masterCol.FieldName]?.ToString() : "";
                                if (!string.IsNullOrEmpty(fieldValue))
                                {
                                    nameBuilders.Add(fieldValue);
                                }
                            }
                        }
                        toolName = string.Join(", ", nameBuilders);
                    }

                    // Check if ToolName is provided directly in Excel
                    if (rowData.ContainsKey("ToolName") && !string.IsNullOrEmpty(rowData["ToolName"]?.ToString()))
                    {
                        toolName = rowData["ToolName"]?.ToString() ?? "";
                    }

                    // Build ToolDescription dynamically based on formula
                    if (!string.IsNullOrEmpty(toolDescriptionFormula))
                    {
                        var descBuilders = new List<string>();
                        foreach (var masterCol in masterColumns)
                        {
                            if (toolDescriptionFormula.Contains(masterCol.FieldName))
                            {
                                var fieldValue = rowData.ContainsKey(masterCol.FieldName) ? rowData[masterCol.FieldName]?.ToString() : "";
                                if (!string.IsNullOrEmpty(fieldValue))
                                {
                                    descBuilders.Add($"{masterCol.FieldName}:{fieldValue}");
                                }
                            }
                        }
                        toolDescription = string.Join(", ", descBuilders);
                    }

                    // Insert into ToolMaster
                    var insertToolMasterSql = @"
                        INSERT INTO ToolMaster (
                            ToolGroupID, ToolCode, MaxToolNo, ToolName, ToolDescription, ToolType, Prefix,
                            ProductHSNID, IsToolActive, CompanyID, UserID, CreatedDate, CreatedBy
                        )
                        VALUES (
                            @ToolGroupID, @ToolCode, @MaxToolNo, @ToolName, @ToolDescription, @ToolType, @Prefix,
                            @ProductHSNID, 1, 2, 2, GETDATE(), 2
                        );
                        SELECT CAST(SCOPE_IDENTITY() as int)";

                    var newToolId = await _connection.ExecuteScalarAsync<int>(insertToolMasterSql, new
                    {
                        ToolGroupID = toolGroupId,
                        ToolCode = toolCode,
                        MaxToolNo = maxToolNo,
                        ToolName = toolName,
                        ToolDescription = toolDescription,
                        ToolType = toolGroupName, // Sub-module name (e.g., "DIE", "EMBOSS")
                        Prefix = toolGroupPrefix,
                        ProductHSNID = productHSNId
                    }, transaction: transaction);

                    // Insert into ToolMasterDetails
                    var insertDetailSql = @"
                        INSERT INTO ToolMasterDetails (
                            ToolID, ToolGroupID, CompanyID, UserID,
                            FieldName, FieldValue, ParentFieldName, ParentFieldValue, ParentToolID,
                            SequenceNo, CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
                        )
                        VALUES (
                            @ToolID, @ToolGroupID, @CompanyID, @UserID,
                            @FieldName, @FieldValue, @ParentFieldName, @ParentFieldValue, @ParentToolID,
                            @SequenceNo, @CreatedDate, @CreatedBy, @ModifiedDate, @ModifiedBy
                        )";

                    int sequenceNo = 1;
                    var insertedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Determine FYear: use from Excel if provided, otherwise use generated default
                    var fYear = defaultFYear;
                    if (rowData.ContainsKey("FYear") && !string.IsNullOrEmpty(rowData["FYear"]?.ToString()))
                    {
                        fYear = rowData["FYear"]?.ToString() ?? defaultFYear;
                    }

                    // STEP 1: Insert master column fields
                    foreach (var masterCol in masterColumns)
                    {
                        // Skip fields stored in ToolMaster table only
                        if (masterCol.FieldName.Equals("HSNCode", StringComparison.OrdinalIgnoreCase) ||
                            masterCol.FieldName.Equals("ToolRefCode", StringComparison.OrdinalIgnoreCase) ||
                            masterCol.FieldName.Equals("ToolName", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fieldValue = rowData.ContainsKey(masterCol.FieldName) ? rowData[masterCol.FieldName]?.ToString() : "";

                        // Skip empty non-required fields
                        if (string.IsNullOrEmpty(fieldValue))
                        {
                            continue;
                        }

                        await _connection.ExecuteAsync(insertDetailSql, new
                        {
                            ToolID = newToolId,
                            ToolGroupID = toolGroupId,
                            CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                            UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            FieldName = masterCol.FieldName,
                            FieldValue = fieldValue ?? "",
                            ParentFieldName = masterCol.FieldName,
                            ParentFieldValue = fieldValue ?? "",
                            ParentToolID = 0,
                            SequenceNo = sequenceNo++,
                            CreatedDate = DateTime.Now,
                            CreatedBy = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            ModifiedDate = DateTime.Now,
                            ModifiedBy = 2
                        }, transaction: transaction);

                        insertedFields.Add(masterCol.FieldName);
                    }

                    // STEP 2: Insert any additional Excel fields not in masterColumns
                    foreach (var field in rowData)
                    {
                        // Skip already inserted fields
                        if (insertedFields.Contains(field.Key))
                        {
                            continue;
                        }

                        // Skip fields stored in ToolMaster table or internal keys
                        if (field.Key.Equals("HSNCode", StringComparison.OrdinalIgnoreCase) ||
                            field.Key.Equals("__RESOLVED_PRODUCTHSNID__", StringComparison.OrdinalIgnoreCase) ||
                            field.Key.Equals("ToolRefCode", StringComparison.OrdinalIgnoreCase) ||
                            field.Key.Equals("ToolName", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fieldValue = field.Value?.ToString();
                        if (string.IsNullOrEmpty(fieldValue))
                        {
                            continue;
                        }

                        await _connection.ExecuteAsync(insertDetailSql, new
                        {
                            ToolID = newToolId,
                            ToolGroupID = toolGroupId,
                            CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                            UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            FieldName = field.Key,
                            FieldValue = fieldValue ?? "",
                            ParentFieldName = field.Key,
                            ParentFieldValue = fieldValue ?? "",
                            ParentToolID = 0,
                            SequenceNo = sequenceNo++,
                            CreatedDate = DateTime.Now,
                            CreatedBy = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            ModifiedDate = DateTime.Now,
                            ModifiedBy = 2
                        }, transaction: transaction);
                    }
                }

                transaction.Commit();
                result.Success = true;
                result.ImportedRows = validRows.Count;
                result.Message = $"Successfully imported {validRows.Count} tool(s).";
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                result.Success = false;
                result.Message = $"Database Error: {ex.Message}";
                result.ErrorMessages.Add(ex.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"System Error: {ex.Message}");
            return result;
        }
    }

    // ==========================================
    // ITEM MASTER IMPORT METHODS
    // ==========================================

    public async Task<List<ItemGroupDto>> GetItemGroupsAsync()
    {
        try
        {
            await EnsureConnectionOpenAsync();

            string query = @"
                SELECT ItemGroupID, ItemGroupName, ItemGroupPrefix, 
                       ItemNameFormula, ItemDescriptionFormula
                FROM ItemGroupMaster
                WHERE CompanyID = 2 
                  AND IsDeletedTransaction = 0
                  AND ItemGroupID IN (2,3,4,5,6,7,8,13,14,16)
                ORDER BY ItemGroupName";

            var itemGroups = await _connection.QueryAsync<ItemGroupDto>(query);
            return itemGroups.ToList();
        }
        catch (Exception)
        {
            return new List<ItemGroupDto>();
        }
    }

    public async Task<List<MasterColumnDto>> GetItemMasterColumnsAsync(int itemGroupId)
    {
        try
        {
            await EnsureConnectionOpenAsync();

            // Query custom columns from ItemGroupMasterColumns (if exists)
            string columnQuery = @"
                SELECT FieldName, DataType, IsRequired, SequenceNo, UnitMeasurement
                FROM ItemGroupMasterColumns
                WHERE ItemGroupID = @ItemGroupID 
                  AND CompanyID = 2
                  AND IsDeletedTransaction = 0
                ORDER BY SequenceNo";

            var columns = await _connection.QueryAsync<MasterColumnDto>(columnQuery, new { ItemGroupID = itemGroupId });
            
            if (columns.Any())
            {
                return columns.ToList();
            }
            
            // If no custom columns defined, return default schema
            return GetDefaultItemColumns(itemGroupId);
        }
        catch (Exception)
        {
            // If table doesn't exist or error, return default columns
            return GetDefaultItemColumns(itemGroupId);
        }
    }

    private List<MasterColumnDto> GetDefaultItemColumns(int itemGroupId)
    {
        var columns = new List<MasterColumnDto>();
        int seq = 1;
        
        // Get ItemGroupName to determine which columns to return
        string? itemGroupName = null;
        try
        {
            var groupQuery = "SELECT ItemGroupName FROM ItemGroupMaster WHERE ItemGroupID = @ItemGroupID AND CompanyID = 2";
            itemGroupName = _connection.QueryFirstOrDefault<string>(groupQuery, new { ItemGroupID = itemGroupId });
        }
        catch
        {
            // If error, return generic columns
        }
        
        // PAPER-specific columns
        if (!string.IsNullOrEmpty(itemGroupName) && itemGroupName.Equals("PAPER", StringComparison.OrdinalIgnoreCase))
        {
            columns.Add(new MasterColumnDto { FieldName = "Quality", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "GSM", DataType = "decimal", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Manufecturer", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Finish", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ManufecturerItemCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "Caliper", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "SizeW", DataType = "decimal", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "SizeL", DataType = "decimal", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PurchaseUnit", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PurchaseRate", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ShelfLife", DataType = "int", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "EstimationUnit", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "EstimationRate", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "StockUnit", DataType = "string", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "MinimumStockQty", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "IsStandardItem", DataType = "bit", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "IsRegularItem", DataType = "bit", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "PackingType", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "UnitPerPacking", DataType = "decimal", IsRequired = true, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "WtPerPacking", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ItemSize", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ItemName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "ProductHSNName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            columns.Add(new MasterColumnDto { FieldName = "CertificationType", DataType = "string", IsRequired = false, SequenceNo = seq++ });
            
            return columns;
        }

        // Default columns for Item Master (similar to Ledger Master pattern)
        // Common columns for all items
        columns.Add(new MasterColumnDto { FieldName = "ItemName", DataType = "string", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "StockUnit", DataType = "string", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "PurchaseUnit", DataType = "string", IsRequired = true, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "EstimationUnit", DataType = "string", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "UnitPerPacking", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "WtPerPacking", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "ConversionFactor", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "ItemSubGroupID", DataType = "int", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "ProductHSNID", DataType = "int", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "StockType", DataType = "string", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "StockCategory", DataType = "string", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "SizeW", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "PurchaseRate", DataType = "decimal", IsRequired = false, SequenceNo = seq++ });
        columns.Add(new MasterColumnDto { FieldName = "StockRefCode", DataType = "string", IsRequired = false, SequenceNo = seq++ });

        return columns;
    }

    public async Task<ImportResultDto> ImportItemMasterWithValidationAsync(Stream fileStream, int itemGroupId)
    {
        var result = new ImportResultDto { Success = false };

        try
        {
            await EnsureConnectionOpenAsync();

            // Load Excel file
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage(fileStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
            {
                result.ErrorMessages.Add("The Excel file is empty.");
                return result;
            }

            // Get ItemGroup info
            var itemGroupQuery = @"
                SELECT ItemGroupID, ItemGroupName, ItemGroupPrefix, 
                       ItemNameFormula, ItemDescriptionFormula
                FROM ItemGroupMaster
                WHERE ItemGroupID = @ItemGroupID AND CompanyID = 2 AND IsDeletedTransaction = 0";
            
            var itemGroup = await _connection.QueryFirstOrDefaultAsync<ItemGroupDto>(itemGroupQuery, new { ItemGroupID = itemGroupId });
            
            if (itemGroup == null)
            {
                result.ErrorMessages.Add($"ItemGroup with ID {itemGroupId} not found.");
                return result;
            }

            // Get master columns
            var masterColumns = await GetItemMasterColumnsAsync(itemGroupId);

            // Read headers
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(header))
                {
                    // Normalize: remove spaces
                    var normalizedHeader = header.Replace(" ", "");
                    headerMap[normalizedHeader] = col;
                }
            }

            // Helper functions
            string? GetValue(string columnName, int rowIdx)
            {
                var normalizedColName = columnName.Replace(" ", "");
                var fuzzyKey = headerMap.Keys.FirstOrDefault(k => k.Replace(" ", "").Equals(normalizedColName, StringComparison.OrdinalIgnoreCase));
                if (fuzzyKey != null) return GetCellValue(rowIdx, headerMap[fuzzyKey]);
                return null;
            }

            string? GetCellValue(int rowIdx, int colIndex)
            {
                var cellValue = worksheet.Cells[rowIdx, colIndex].Value;
                if (cellValue == null || cellValue == DBNull.Value) return null;
                var stringValue = cellValue.ToString()?.Trim();
                return string.IsNullOrEmpty(stringValue) ? null : stringValue;
            }

            // Validate required columns
            var missingColumns = new List<string>();
            if (masterColumns.Any())
            {
                foreach (var masterCol in masterColumns.Where(c => c.IsRequired))
                {
                    if (!headerMap.ContainsKey(masterCol.FieldName))
                    {
                        missingColumns.Add(masterCol.FieldName);
                    }
                }

                if (missingColumns.Any())
                {
                    result.Success = false;
                    result.ErrorMessages.Add($"Missing required columns: {string.Join(", ", missingColumns)}");
                    return result;
                }
            }

            // Process Rows
            var validRows = new List<Dictionary<string, object?>>();
            var errorMessages = new List<string>();
            var excelItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Check duplicates in database
            string duplicateCheckQuery = @"
                SELECT ItemName
                FROM ItemMaster
                WHERE ItemGroupID = @ItemGroupID
                  AND CompanyID = 2
                  AND IsDeletedTransaction = 0
                  AND ItemName IS NOT NULL";

            var existingItems = await _connection.QueryAsync<string>(duplicateCheckQuery, new { ItemGroupID = itemGroupId });
            var dbItemNames = new HashSet<string>(existingItems.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);

            for (int row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                // Check empty row
                bool isRowEmpty = true;
                for (int c = 1; c <= worksheet.Dimension.Columns; c++)
                {
                    if (!string.IsNullOrWhiteSpace(worksheet.Cells[row, c].Value?.ToString()))
                    {
                        isRowEmpty = false;
                        break;
                    }
                }
                if (isRowEmpty) continue;

                var rowData = new Dictionary<string, object?>();
                var rowErrors = new List<string>();

                // ALWAYS load ALL Excel columns into rowData (not just masterColumns)
                foreach (var header in headerMap.Keys)
                {
                    var value = GetValue(header, row);
                    rowData[header] = value;
                }

                // Then validate required columns from masterColumns
                if (masterColumns.Any())
                {
                    foreach (var masterCol in masterColumns)
                    {
                        var value = GetFieldValue(rowData, masterCol.FieldName);
                        if (masterCol.IsRequired && string.IsNullOrEmpty(value))
                        {
                            rowErrors.Add($"Row {row}: {masterCol.FieldName} is required.");
                        }
                    }
                }

                // AUTO-CALCULATE: Caliper, ItemSize, WtPerPacking, ItemName
                rowData = CalculateItemFields(rowData);

                // AUTO-SET ItemType to Item Group Name (e.g., "Paper") ONLY if not already provided in Excel
                var excelItemType = rowData.ContainsKey("ItemType") ? rowData["ItemType"]?.ToString()?.Trim() : null;
                if (string.IsNullOrEmpty(excelItemType) && !string.IsNullOrEmpty(itemGroup.ItemGroupName))
                {
                    rowData["ItemType"] = itemGroup.ItemGroupName;
                }

                // AUTO-SET PaperGroup based on item group
                if (!string.IsNullOrEmpty(itemGroup.ItemGroupName))
                {
                    if (itemGroup.ItemGroupName.Equals("PAPER", StringComparison.OrdinalIgnoreCase))
                    {
                        rowData["PaperGroup"] = "Paper";
                    }
                    else if (itemGroup.ItemGroupName.Equals("REEL", StringComparison.OrdinalIgnoreCase))
                    {
                        rowData["PaperGroup"] = "Reel";
                    }
                }

                // APPLY DEFAULTS for PAPER Item Group
                if (!string.IsNullOrEmpty(itemGroup.ItemGroupName) && itemGroup.ItemGroupName.Equals("PAPER", StringComparison.OrdinalIgnoreCase))
                {
                    // ShelfLife default = 365 days
                    if (!rowData.ContainsKey("ShelfLife") || string.IsNullOrEmpty(rowData["ShelfLife"]?.ToString()))
                    {
                        rowData["ShelfLife"] = 365;
                    }
                    
                    // IsStandardItem default = True
                    if (!rowData.ContainsKey("IsStandardItem") || string.IsNullOrEmpty(rowData["IsStandardItem"]?.ToString()))
                    {
                        rowData["IsStandardItem"] = true;
                    }
                    
                    // IsRegularItem default = True
                    if (!rowData.ContainsKey("IsRegularItem") || string.IsNullOrEmpty(rowData["IsRegularItem"]?.ToString()))
                    {
                        rowData["IsRegularItem"] = true;
                    }
                    
                    // CertificationType default = NONE
                    if (!rowData.ContainsKey("CertificationType") || string.IsNullOrEmpty(rowData["CertificationType"]?.ToString()))
                    {
                        rowData["CertificationType"] = "NONE";
                    }
                }

                // LOOKUP ItemSubGroupID based on ItemSubGroupName (for INK & Additives)
                // Check if ItemSubGroupName exists in rowData (case-insensitive)
                var itemSubGroupNameKey = rowData.Keys.FirstOrDefault(k => k.Equals("ItemSubGroupName", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"[DEBUG] Checking for ItemSubGroupName in rowData: {(itemSubGroupNameKey != null ? $"Found key '{itemSubGroupNameKey}'" : "NOT FOUND")}");
                
                if (itemSubGroupNameKey != null)
                {
                    var itemSubGroupName = rowData[itemSubGroupNameKey]?.ToString()?.Trim();
                    Console.WriteLine($"[DEBUG] ItemSubGroupName value: '{itemSubGroupName}'");
                    
                    if (!string.IsNullOrEmpty(itemSubGroupName))
                    {
                        string subGroupQuery = @"
                            SELECT TOP 1 ItemSubGroupID 
                            FROM ItemSubGroupMaster
                            WHERE LTRIM(RTRIM(ItemSubGroupName)) = LTRIM(RTRIM(@ItemSubGroupName))
                              AND CompanyID = @CompanyID
                              AND (IsDeletedTransaction = 0 OR IsDeletedTransaction IS NULL)";
                        
                        Console.WriteLine($"[DEBUG] Executing SQL: {subGroupQuery}");
                        Console.WriteLine($"[DEBUG] Parameters: ItemSubGroupName='{itemSubGroupName}', CompanyID=2");
                        
                        var itemSubGroupID = await _connection.QueryFirstOrDefaultAsync<int?>(
                            subGroupQuery,
                            new { ItemSubGroupName = itemSubGroupName, CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2 }
                        );
                        
                        Console.WriteLine($"[DEBUG] ItemSubGroupName lookup: '{itemSubGroupName}' => ItemSubGroupID = {(itemSubGroupID.HasValue ? itemSubGroupID.Value.ToString() : "NULL")}");
                        
                        if (itemSubGroupID.HasValue)
                        {
                            rowData["ItemSubGroupID"] = itemSubGroupID.Value;
                            Console.WriteLine($"[DEBUG] Added ItemSubGroupID = {itemSubGroupID.Value} to rowData");
                        }
                        else
                        {
                            Console.WriteLine($"[DEBUG] WARNING: No ItemSubGroupID found for ItemSubGroupName = '{itemSubGroupName}'");
                        }
                    }
                }

                if (rowErrors.Any())
                {
                    errorMessages.AddRange(rowErrors);
                    continue;
                }

                // Get ItemName (already auto-calculated by CalculateItemFields)
                var itemName = GetFieldValue(rowData, "ItemName")?.Trim();

                // Fallback if auto-generation failed or resulted in empty string
                if (string.IsNullOrEmpty(itemName))
                {
                    itemName = $"Item_{DateTime.Now.Ticks}"; // Temporary fallback
                    rowData["ItemName"] = itemName;
                }

                // Check duplicates
                if (excelItemNames.Contains(itemName))
                {
                    errorMessages.Add($"Row {row}: Duplicate ItemName '{itemName}' found within Excel file.");
                    result.DuplicateRows++;
                    continue;
                }

                if (dbItemNames.Contains(itemName))
                {
                    errorMessages.Add($"Row {row}: ItemName '{itemName}' already exists in database.");
                    result.DuplicateRows++;
                    continue;
                }

                excelItemNames.Add(itemName);
                validRows.Add(rowData);
            }

            // Helper function to get field value
            string? GetFieldValue(Dictionary<string, object?> data, string fieldName)
            {
                var key = data.Keys.FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                return key != null ? data[key]?.ToString() : null;
            }

            // Validation result check
            if (errorMessages.Any())
            {
                result.Success = false;
                result.ErrorMessages = errorMessages;
                result.ErrorRows = errorMessages.Count - result.DuplicateRows;

                var messageParts = new List<string>();
                if (result.DuplicateRows > 0)
                    messageParts.Add($"{result.DuplicateRows} duplicate(s)");
                if (result.ErrorRows > 0)
                    messageParts.Add($"{result.ErrorRows} validation error(s)");

                result.Message = $"Import failed: {string.Join(", ", messageParts)}. No data imported.";
                return result;
            }

            if (!validRows.Any())
            {
                result.Success = true;
                result.Message = "No valid data found to import.";
                return result;
            }

            // Execute Import (Transactional)
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Get all ItemMaster table columns
                string columnsQuery = @"
                    SELECT COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'ItemMaster'
                    AND COLUMN_NAME NOT IN ('ItemID')
                    ORDER BY ORDINAL_POSITION";

                var itemMasterColumns = await _connection.QueryAsync<string>(columnsQuery, transaction: transaction);
                var itemMasterColumnsList = itemMasterColumns.Select(c => c.ToLower()).ToList();

                // Get Prefix
                string prefix = itemGroup.ItemGroupPrefix ?? "ITM";

                // Get current Max Number
                var maxItemNoObj = await _connection.ExecuteScalarAsync<object>(
                    "SELECT MAX(MaxItemNo) FROM ItemMaster WHERE ItemGroupID = @GID AND IsDeletedTransaction = 0",
                    new { GID = itemGroupId }, transaction: transaction);

                long currentMaxNo = 0;
                if (maxItemNoObj != null && long.TryParse(maxItemNoObj.ToString(), out var parsedMax))
                {
                    currentMaxNo = parsedMax;
                }

                foreach (var rowData in validRows)
                {
                    currentMaxNo++;
                    var itemCode = $"{prefix}{currentMaxNo.ToString().PadLeft(5, '0')}";

                    var itemName = rowData.ContainsKey("ItemName") ? rowData["ItemName"]?.ToString() : "";
                    if (string.IsNullOrEmpty(itemName))
                    {
                        itemName = $"Item_{currentMaxNo}";
                    }

                    // Generate ItemDescription from formula
                    var itemDescription = "";
                    if (!string.IsNullOrEmpty(itemGroup.ItemDescriptionFormula))
                    {
                        itemDescription = GenerateItemDescription(itemGroup.ItemDescriptionFormula, rowData, masterColumns);
                    }

                    // Build dynamic INSERT for ItemMaster
                    var masterParams = new DynamicParameters();
                    var insertColumns = new List<string>();
                    var insertValues = new List<string>();

                    // LOOKUP ProductHSNID based on HSNCode or ProductHSNName
                    int? productHSNID = null;
                    var hsnCode = rowData.ContainsKey("HSNCode") ? rowData["HSNCode"]?.ToString()?.Trim() : null;
                    var productHSNName = rowData.ContainsKey("ProductHSNName") ? rowData["ProductHSNName"]?.ToString()?.Trim() : null;
                    
                    if (!string.IsNullOrEmpty(hsnCode) || !string.IsNullOrEmpty(productHSNName))
                    {
                        string hsnQuery = @"
                            SELECT TOP 1 ProductHSNID 
                            FROM ProductHSNMaster
                            WHERE (@HSNCode IS NOT NULL AND HSNCode = @HSNCode)
                               OR (@ProductHSNName IS NOT NULL AND ProductHSNName = @ProductHSNName)";
                        
                        productHSNID = await _connection.QueryFirstOrDefaultAsync<int?>(
                            hsnQuery, 
                            new { HSNCode = hsnCode, ProductHSNName = productHSNName },
                            transaction: transaction
                        );
                        
                        if (productHSNID.HasValue)
                        {
                            rowData["ProductHSNID"] = productHSNID.Value;
                        }
                    }

                    // Add system required columns
                    insertColumns.Add("ItemCode");
                    insertValues.Add("@ItemCode");
                    masterParams.Add("@ItemCode", itemCode);

                    insertColumns.Add("MaxItemNo");
                    insertValues.Add("@MaxItemNo");
                    masterParams.Add("@MaxItemNo", currentMaxNo);

                    insertColumns.Add("ItemCodePrefix");
                    insertValues.Add("@ItemCodePrefix");
                    masterParams.Add("@ItemCodePrefix", prefix);

                    insertColumns.Add("ItemGroupID");
                    insertValues.Add("@ItemGroupID");
                    masterParams.Add("@ItemGroupID", itemGroupId);

                    insertColumns.Add("CompanyID");
                    insertValues.Add("@CompanyID");
                    masterParams.Add("@CompanyID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2);

                    insertColumns.Add("UserID");
                    insertValues.Add("@UserID");
                    masterParams.Add("@UserID", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);

                    insertColumns.Add("FYear");
                    insertValues.Add("@FYear");
                    masterParams.Add("@FYear", (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"));

                    insertColumns.Add("CreatedDate");
                    insertValues.Add("@CreatedDate");
                    masterParams.Add("@CreatedDate", DateTime.Now);

                    insertColumns.Add("CreatedBy");
                    insertValues.Add("@CreatedBy");
                    masterParams.Add("@CreatedBy", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);

                    insertColumns.Add("ModifiedDate");
                    insertValues.Add("@ModifiedDate");
                    masterParams.Add("@ModifiedDate", DateTime.Now);

                    insertColumns.Add("ModifiedBy");
                    insertValues.Add("@ModifiedBy");
                    masterParams.Add("@ModifiedBy", await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2);

                    insertColumns.Add("IsDeletedTransaction");
                    insertValues.Add("@IsDeletedTransaction");
                    masterParams.Add("@IsDeletedTransaction", 0);

                    // Map Excel columns to ItemMaster columns dynamically
                    foreach (var field in rowData)
                    {
                        var fieldNameLower = field.Key.ToLower().Replace(" ", "");

                        var matchingColumn = itemMasterColumnsList.FirstOrDefault(c =>
                            c.Replace(" ", "").Equals(fieldNameLower, StringComparison.OrdinalIgnoreCase));

                        if (matchingColumn != null && !insertColumns.Any(ic => ic.Equals(matchingColumn, StringComparison.OrdinalIgnoreCase)))
                        {
                            var actualColumnName = itemMasterColumns.First(c => c.ToLower() == matchingColumn);
                            insertColumns.Add(actualColumnName);
                            insertValues.Add($"@{actualColumnName}");
                            
                            // Preserve numeric types instead of converting to string
                            var fieldValue = field.Value;
                            if (fieldValue != null && !string.IsNullOrEmpty(fieldValue.ToString()))
                            {
                                // Try to convert to double for SQL real/float compatibility
                                if (fieldValue is decimal || fieldValue is int || fieldValue is double || fieldValue is float)
                                {
                                    // Already numeric - convert to double for SQL compatibility
                                    masterParams.Add($"@{actualColumnName}", Convert.ToDouble(fieldValue));
                                }
                                else if (double.TryParse(fieldValue.ToString(), out var numValue))
                                {
                                    // String that looks like a number - convert to double
                                    masterParams.Add($"@{actualColumnName}", numValue);
                                }
                                else
                                {
                                    // Keep as string
                                    masterParams.Add($"@{actualColumnName}", fieldValue.ToString());
                                }
                            }
                            else
                            {
                                masterParams.Add($"@{actualColumnName}", null);
                            }
                        }
                    }

                    // Ensure ItemName is added
                    if (!insertColumns.Any(c => c.Equals("ItemName", StringComparison.OrdinalIgnoreCase)))
                    {
                        insertColumns.Add("ItemName");
                        insertValues.Add("@ItemName");
                        masterParams.Add("@ItemName", itemName);
                    }

                    // Add ItemDescription if not already added
                    if (!insertColumns.Any(c => c.Equals("ItemDescription", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(itemDescription))
                    {
                        insertColumns.Add("ItemDescription");
                        insertValues.Add("@ItemDescription");
                        masterParams.Add("@ItemDescription", itemDescription);
                    }

                    // Explicitly add calculated fields if columns exist in ItemMaster table
                    var calculatedFields = new[] { "Caliper", "ItemSize", "WtPerPacking" };
                    foreach (var calcField in calculatedFields)
                    {
                        if (rowData.ContainsKey(calcField) && 
                            itemMasterColumnsList.Any(c => c.Equals(calcField.ToLower(), StringComparison.OrdinalIgnoreCase)) &&
                            !insertColumns.Any(ic => ic.Equals(calcField, StringComparison.OrdinalIgnoreCase)))
                        {
                            var actualColumnName = itemMasterColumns.First(c => c.Equals(calcField, StringComparison.OrdinalIgnoreCase));
                            insertColumns.Add(actualColumnName);
                            insertValues.Add($"@{actualColumnName}");
                            
                            // Preserve type for calculated fields (they're already double from CalculateItemFields)
                            var calcValue = rowData[calcField];
                            if (calcValue is double || calcValue is decimal || calcValue is int || calcValue is float)
                            {
                                masterParams.Add($"@{actualColumnName}", Convert.ToDouble(calcValue));
                            }
                            else if (calcValue != null && double.TryParse(calcValue.ToString(), out var numVal))
                            {
                                masterParams.Add($"@{actualColumnName}", numVal);
                            }
                            else
                            {
                                masterParams.Add($"@{actualColumnName}", calcValue?.ToString());
                            }
                        }
                    }

                    if (!insertColumns.Any(c => c.Equals("ISItemActive", StringComparison.OrdinalIgnoreCase)))
                    {
                        insertColumns.Add("ISItemActive");
                        insertValues.Add("@ISItemActive");
                        masterParams.Add("@ISItemActive", true);
                    }

                    // Build and execute dynamic INSERT
                    string insertMasterSql = $@"
                        INSERT INTO ItemMaster ({string.Join(", ", insertColumns)})
                        VALUES ({string.Join(", ", insertValues)});
                        SELECT SCOPE_IDENTITY();";

                    var itemIdObj = await _connection.ExecuteScalarAsync<object>(insertMasterSql, masterParams, transaction: transaction);
                    int newItemId = Convert.ToInt32(itemIdObj);

                    // Add ISItemActive to rowData so it gets inserted into ItemMasterDetails
                    if (!rowData.ContainsKey("ISItemActive"))
                    {
                        rowData["ISItemActive"] = "True";
                    }

                    // Insert Details (ItemMasterDetails)
                    // Insert ALL Excel columns into ItemMasterDetails, not just predefined ones
                    string insertDetailSql = @"
                        INSERT INTO ItemMasterDetails (
                            ItemID, ItemGroupID, CompanyID, UserID, FYear,
                            FieldName, FieldValue, ParentFieldName, ParentFieldValue, ParentItemID,
                            SequenceNo, CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
                        ) VALUES (
                            @ItemID, @ItemGroupID, @CompanyID, @UserID, @FYear,
                            @FieldName, @FieldValue, @ParentFieldName, @ParentFieldValue, @ParentItemID,
                            @SequenceNo, @CreatedDate, @CreatedBy, @CreatedDate, @CreatedBy
                        )";

                    int sequenceNo = 1;

                    // DEBUG: Show what's in rowData before inserting
                    Console.WriteLine($"[DEBUG] ===== ItemID {newItemId} - rowData contains {rowData.Count} keys =====");
                    foreach (var debugKey in rowData.Keys)
                    {
                        Console.WriteLine($"[DEBUG]   Key: '{debugKey}' = '{rowData[debugKey]}'");
                    }

                    // Loop through ALL Excel columns (rowData) and insert into ItemMasterDetails
                    // NOTE: We insert ALL columns, even if they're already in ItemMaster table
                    // This is the EAV (Entity-Attribute-Value) pattern for flexible data storage
                    int detailsInserted = 0;
                    foreach (var field in rowData)
                    {
                        // Skip HSNCode, ItemName, ItemSubGroupName, and ItemSubGroupID
                        // ItemSubGroupName is skipped because we insert ItemSubGroupID instead (handled separately below)
                        // ItemSubGroupID is skipped here to avoid duplicates (handled separately below)
                        if (field.Key.Equals("HSNCode", StringComparison.OrdinalIgnoreCase) ||
                            field.Key.Equals("ItemName", StringComparison.OrdinalIgnoreCase) ||
                            field.Key.Equals("ItemSubGroupName", StringComparison.OrdinalIgnoreCase) ||
                            field.Key.Equals("ItemSubGroupID", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[DEBUG] Skipping '{field.Key}' - excluded from ItemMasterDetails");
                            continue;
                        }

                        var fieldValue = field.Value?.ToString() ?? "";

                        // SPECIAL CASE: For ProductHSNName field, insert ProductHSNID value instead of the name
                        if (field.Key.Equals("ProductHSNName", StringComparison.OrdinalIgnoreCase) && productHSNID.HasValue)
                        {
                            fieldValue = productHSNID.Value.ToString();
                            Console.WriteLine($"[DEBUG] Inserting '{field.Key}' = '{fieldValue}' (ProductHSNID) into ItemMasterDetails");
                        }
                        else
                        {
                            // Insert ALL other columns into ItemMasterDetails
                            Console.WriteLine($"[DEBUG] Inserting '{field.Key}' = '{fieldValue}' into ItemMasterDetails");
                        }

                        await _connection.ExecuteAsync(insertDetailSql, new {
                            ItemID = newItemId,
                            ItemGroupID = itemGroupId,
                            CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                            UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                            FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                            FieldName = field.Key,
                            FieldValue = fieldValue,
                            ParentFieldName = field.Key,
                            ParentFieldValue = fieldValue,
                            ParentItemID = 0,
                            SequenceNo = sequenceNo++,
                            CreatedDate = DateTime.Now,
                            CreatedBy = 2
                        }, transaction: transaction);
                        
                        detailsInserted++;
                    }
                    
                    // SPECIAL INSERT: Add ItemSubGroupID if it was looked up from ItemSubGroupName
                    if (rowData.ContainsKey("ItemSubGroupID"))
                    {
                        var itemSubGroupIDValue = rowData["ItemSubGroupID"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(itemSubGroupIDValue))
                        {
                            await _connection.ExecuteAsync(insertDetailSql, new {
                                ItemID = newItemId,
                                ItemGroupID = itemGroupId,
                                CompanyID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster WHERE IsDeletedTransaction=0") ?? 2,
                                UserID = await _connection.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='Admin' AND IsDeletedUser=0") ?? 2,
                                FYear = (await _connection.ExecuteScalarAsync<string>("SELECT FYear FROM UserMaster WHERE UserName='Admin'") ?? "2025-2026"),
                                FieldName = "ItemSubGroupID",
                                FieldValue = itemSubGroupIDValue,
                                ParentFieldName = "ItemSubGroupID",
                                ParentFieldValue = itemSubGroupIDValue,
                                ParentItemID = 0,
                                SequenceNo = sequenceNo++,
                                CreatedDate = DateTime.Now,
                                CreatedBy = 2
                            }, transaction: transaction);
                            
                            detailsInserted++;
                            Console.WriteLine($"[DEBUG] Inserted ItemSubGroupID = '{itemSubGroupIDValue}' into ItemMasterDetails");
                        }
                    }
                    
                    Console.WriteLine($"[DEBUG] Inserted {detailsInserted} records into ItemMasterDetails for ItemID {newItemId}");
                }

                transaction.Commit();
                result.Success = true;
                result.ImportedRows = validRows.Count;

                var messageBuilder = new System.Text.StringBuilder();
                messageBuilder.Append($"Successfully imported {validRows.Count} item(s) into {itemGroup.ItemGroupName}.");

                if (result.DuplicateRows > 0 || result.ErrorRows > 0)
                {
                    messageBuilder.Append(" ");
                    var skippedParts = new List<string>();
                    if (result.DuplicateRows > 0)
                        skippedParts.Add($"{result.DuplicateRows} duplicate(s)");
                    if (result.ErrorRows > 0)
                        skippedParts.Add($"{result.ErrorRows} error(s)");
                    messageBuilder.Append($"Skipped: {string.Join(", ", skippedParts)}.");
                }

                result.Message = messageBuilder.ToString();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                result.Success = false;
                result.Message = $"Database Error: {ex.Message}";
                result.ErrorMessages.Add(ex.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessages.Add($"System Error: {ex.Message}");
            return result;
        }
    }

    private string GenerateItemName(string formula, Dictionary<string, object?> rowData, List<MasterColumnDto> masterColumns)
    {
        // If formula is provided, use it
        if (!string.IsNullOrEmpty(formula))
        {
            var result = formula;
            
            // Replace placeholders from rowData directly (not just masterColumns)
            foreach (var field in rowData)
            {
                var fieldValue = field.Value?.ToString();
                if (!string.IsNullOrEmpty(fieldValue))
                {
                    result = result.Replace($"{{{field.Key}}}", fieldValue);
                }
                else
                {
                    result = result.Replace($"{{{field.Key}}}", "");
                }
            }
            
            // Clean up extra commas and spaces
            result = System.Text.RegularExpressions.Regex.Replace(result, @",\s*,", ",");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"^\s*,\s*|\s*,\s*$", "");
            result = result.Trim();
            
            return result;
        }
        
        // Default: Generate from Quality, GSM, Manufacturer, Finish, ItemSize
        // Format: "Art paper, 80 GSM, ITC, coated, 635 X 910 MM"
        var parts = new List<string>();
        
        var quality = GetFieldValueFromDict(rowData, "Quality");
        if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
        
        var gsm = GetFieldValueFromDict(rowData, "GSM");
        if (!string.IsNullOrEmpty(gsm)) parts.Add($"{gsm} GSM");
        
        var manufacturer = GetFieldValueFromDict(rowData, "Manufacturer");
        if (!string.IsNullOrEmpty(manufacturer)) parts.Add(manufacturer);
        
        var finish = GetFieldValueFromDict(rowData, "Finish");
        if (!string.IsNullOrEmpty(finish)) parts.Add(finish);
        
        var itemSize = GetFieldValueFromDict(rowData, "ItemSize");
        if (!string.IsNullOrEmpty(itemSize)) parts.Add($"{itemSize} MM");
        
        return string.Join(", ", parts);
    }

    private string GenerateItemDescription(string formula, Dictionary<string, object?> rowData, List<MasterColumnDto> masterColumns)
    {
        // Formula format: "FieldName1:Value1, FieldName2:Value2"
        
        var result = formula;
        
        foreach (var col in masterColumns)
        {
            var fieldValue = GetFieldValueFromDict(rowData, col.FieldName);
            result = result.Replace($"{{{col.FieldName}}}", fieldValue ?? "");
        }
        
        result = System.Text.RegularExpressions.Regex.Replace(result, @",\s*,", ",");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"^\s*,\s*|\s*,\s*$", "");
        result = result.Trim();
        
        return result;
    }

    private string? GetFieldValueFromDict(Dictionary<string, object?> data, string fieldName)
    {
        var key = data.Keys.FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        return key != null ? data[key]?.ToString() : null;
    }

    private Dictionary<string, object?> CalculateItemFields(Dictionary<string, object?> rowData)
    {
        // Calculate Caliper = GSM / 1000
        if (rowData.TryGetValue("GSM", out var gsmObj) && gsmObj != null)
        {
            if (decimal.TryParse(gsmObj.ToString(), out var gsm) && gsm > 0)
            {
                rowData["Caliper"] = Math.Round((double)(gsm / 1000), 2);
            }
        }
        
        // Calculate ItemSize = SizeW X SizeL (or Width X Length)
        var width = GetFieldValueFromDict(rowData, "SizeW") ?? GetFieldValueFromDict(rowData, "Width");
        var length = GetFieldValueFromDict(rowData, "SizeL") ?? GetFieldValueFromDict(rowData, "Length");
        if (!string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(length))
        {
            rowData["ItemSize"] = $"{width} X {length}";
        }
        
        // Calculate WtPerPacking = (SizeL Ã— SizeW Ã— GSM Ã— UnitPerPacking) / 1000000000
        // Try SizeL/SizeW first, then fallback to Length/Width
        var lenVal = GetFieldValueFromDict(rowData, "SizeL") ?? GetFieldValueFromDict(rowData, "Length");
        var widVal = GetFieldValueFromDict(rowData, "SizeW") ?? GetFieldValueFromDict(rowData, "Width");
        var gsmVal = GetFieldValueFromDict(rowData, "GSM");
        var unitVal = GetFieldValueFromDict(rowData, "UnitPerPacking");
        
        if (!string.IsNullOrEmpty(lenVal) && !string.IsNullOrEmpty(widVal) && 
            !string.IsNullOrEmpty(gsmVal) && !string.IsNullOrEmpty(unitVal))
        {
            if (decimal.TryParse(lenVal, out var len) &&
                decimal.TryParse(widVal, out var wid) &&
                decimal.TryParse(gsmVal, out var gsm2) &&
                decimal.TryParse(unitVal, out var unit) &&
                len > 0 && wid > 0 && gsm2 > 0 && unit > 0)
            {
                var wtPerPacking = (double)((len * wid * gsm2 * unit) / 1000000000m);
                rowData["WtPerPacking"] = Math.Round(wtPerPacking, 4);
            }
        }
        
        // Calculate ItemName based on item group type
        // Check if this is INK & Additives, Reel, or Paper
        var itemType = GetFieldValueFromDict(rowData, "ItemType");
        var bf = GetFieldValueFromDict(rowData, "BF");
        
        // Check if INK & Additives based on ItemType
        bool isInkOrAdditives = itemType != null && 
            (itemType.Equals("INK", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("ADDITIVES", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("INK", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("ADDITIVES", StringComparison.OrdinalIgnoreCase));
        
        // Check if Lamination Film based on ItemType
        bool isLaminationFilm = itemType != null &&
            (itemType.Equals("LAMINATION FILM", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("LAMINATION", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("LAMINATION", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("FILM", StringComparison.OrdinalIgnoreCase));
        
        // Check if Other Material based on ItemType
        bool isOtherMaterial = itemType != null &&
            (itemType.Equals("OTHER MATERIAL", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("OTHER MATERIALS", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("OTHER MATERIAL", StringComparison.OrdinalIgnoreCase));
        
        // Check if Foil based on ItemType
        bool isFoil = itemType != null &&
            (itemType.Equals("FOIL", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("FOIL", StringComparison.OrdinalIgnoreCase));
        
        // Check if VARNISHES & COATINGS based on ItemType
        bool isVarnishesOrCoatings = itemType != null &&
            (itemType.Equals("VARNISHES", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("COATINGS", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("VARNISHES & COATINGS", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("VARNISH", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("COATING", StringComparison.OrdinalIgnoreCase));
        
        // Check if Roll based on ItemType (different from Reel)
        bool isRoll = itemType != null &&
            (itemType.Equals("ROLL", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("ROLL", StringComparison.OrdinalIgnoreCase));
        
        // Check if Shipper Carton based on ItemType
        bool isShipperCarton = itemType != null &&
            (itemType.Equals("SHIPPER CARTON", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("SHIPPER", StringComparison.OrdinalIgnoreCase) ||
             itemType.Equals("CARTON", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("SHIPPER", StringComparison.OrdinalIgnoreCase) ||
             itemType.Contains("CARTON", StringComparison.OrdinalIgnoreCase));
        
        bool isReel = !string.IsNullOrEmpty(bf) || 
                      (itemType != null && itemType.Equals("REEL", StringComparison.OrdinalIgnoreCase));
        
        var parts = new List<string>();
        
        if (isInkOrAdditives)
        {
            // INK & Additives ItemName: ItemType, InkColour, PantoneCode
            var inkColour = GetFieldValueFromDict(rowData, "InkColour");
            var pantoneCode = GetFieldValueFromDict(rowData, "PantoneCode");
            
            if (!string.IsNullOrEmpty(itemType)) parts.Add(itemType);
            if (!string.IsNullOrEmpty(inkColour)) parts.Add(inkColour);
            if (!string.IsNullOrEmpty(pantoneCode)) parts.Add(pantoneCode);
        }
        else if (isLaminationFilm)
        {
            // Lamination Film ItemName: Quality, SizeW, Thickness, Manufacturer
            var quality = GetFieldValueFromDict(rowData, "Quality");
            var sizeW = GetFieldValueFromDict(rowData, "SizeW");
            var thickness = GetFieldValueFromDict(rowData, "Thickness");
            var manufacturer = GetFieldValueFromDict(rowData, "Manufecturer") ?? GetFieldValueFromDict(rowData, "Manufacturer");
            
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
            if (!string.IsNullOrEmpty(sizeW)) parts.Add(sizeW);
            if (!string.IsNullOrEmpty(thickness)) parts.Add(thickness);
            if (!string.IsNullOrEmpty(manufacturer)) parts.Add(manufacturer);
        }
        else if (isOtherMaterial)
        {
            // Other Material ItemName: Quality only
            var quality = GetFieldValueFromDict(rowData, "Quality");
            
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
        }
        else if (isFoil)
        {
            // Foil ItemName: ManufacturerItemCode, Quality, SizeW
            var manufacturerItemCode = GetFieldValueFromDict(rowData, "ManufacturerItemCode");
            var quality = GetFieldValueFromDict(rowData, "Quality");
            var sizeW = GetFieldValueFromDict(rowData, "SizeW");
            
            if (!string.IsNullOrEmpty(manufacturerItemCode)) parts.Add(manufacturerItemCode);
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
            if (!string.IsNullOrEmpty(sizeW)) parts.Add(sizeW);
        }
        else if (isVarnishesOrCoatings)
        {
            // VARNISHES & COATINGS ItemName: ItemType, Quality
            var quality = GetFieldValueFromDict(rowData, "Quality");
            
            if (!string.IsNullOrEmpty(itemType)) parts.Add(itemType);
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
        }
        else if (isRoll)
        {
            // Roll ItemName: Quality, GSM, ReleaseGSM, AdhesiveGSM, Manufacturer, SizeW
            var quality = GetFieldValueFromDict(rowData, "Quality");
            var gsmStr = GetFieldValueFromDict(rowData, "GSM");
            var releaseGSM = GetFieldValueFromDict(rowData, "ReleaseGSM");
            var adhesiveGSM = GetFieldValueFromDict(rowData, "AdhesiveGSM");
            var manufacturer = GetFieldValueFromDict(rowData, "Manufecturer") ?? GetFieldValueFromDict(rowData, "Manufacturer");
            var sizeW = GetFieldValueFromDict(rowData, "SizeW");
            
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
            if (!string.IsNullOrEmpty(gsmStr)) parts.Add(gsmStr);
            if (!string.IsNullOrEmpty(releaseGSM)) parts.Add(releaseGSM);
            if (!string.IsNullOrEmpty(adhesiveGSM)) parts.Add(adhesiveGSM);
            if (!string.IsNullOrEmpty(manufacturer)) parts.Add(manufacturer);
            if (!string.IsNullOrEmpty(sizeW)) parts.Add(sizeW);
        }
        else if (isShipperCarton)
        {
            // Shipper Carton ItemName: Quality, SizeL, SizeW, SizeH, NoOfPly, ManufacturerItemCode
            var quality = GetFieldValueFromDict(rowData, "Quality");
            var sizeL = GetFieldValueFromDict(rowData, "SizeL");
            var sizeW = GetFieldValueFromDict(rowData, "SizeW");
            var sizeH = GetFieldValueFromDict(rowData, "SizeH");
            var noOfPly = GetFieldValueFromDict(rowData, "NoOfPly");
            var manufacturerItemCode = GetFieldValueFromDict(rowData, "ManufacturerItemCode");
            
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
            if (!string.IsNullOrEmpty(sizeL)) parts.Add(sizeL);
            if (!string.IsNullOrEmpty(sizeW)) parts.Add(sizeW);
            if (!string.IsNullOrEmpty(sizeH)) parts.Add(sizeH);
            if (!string.IsNullOrEmpty(noOfPly)) parts.Add(noOfPly);
            if (!string.IsNullOrEmpty(manufacturerItemCode)) parts.Add(manufacturerItemCode);
        }
        else if (isReel)
        {
            // Reel ItemName: BF, Quality, GSM, Manufacturer, Finish, SizeW, Caliper
            var quality = GetFieldValueFromDict(rowData, "Quality");
            var gsmStr = GetFieldValueFromDict(rowData, "GSM");
            var manufacturer = GetFieldValueFromDict(rowData, "Manufecturer") ?? GetFieldValueFromDict(rowData, "Manufacturer");
            var finish = GetFieldValueFromDict(rowData, "Finish");
            
            if (!string.IsNullOrEmpty(bf)) parts.Add(bf);
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
            if (!string.IsNullOrEmpty(gsmStr)) parts.Add($"{gsmStr} GSM");
            if (!string.IsNullOrEmpty(manufacturer)) parts.Add(manufacturer);
            if (!string.IsNullOrEmpty(finish)) parts.Add(finish);
            
            var sizeW = GetFieldValueFromDict(rowData, "SizeW");
            if (!string.IsNullOrEmpty(sizeW)) parts.Add(sizeW);
            
            var caliper = GetFieldValueFromDict(rowData, "Caliper");
            if (!string.IsNullOrEmpty(caliper)) parts.Add(caliper);
        }
        else
        {
            // Paper ItemName: Quality, GSM, Manufacturer, Finish, ItemSize
            var quality = GetFieldValueFromDict(rowData, "Quality");
            var gsmStr = GetFieldValueFromDict(rowData, "GSM");
            var manufacturer = GetFieldValueFromDict(rowData, "Manufecturer") ?? GetFieldValueFromDict(rowData, "Manufacturer");
            var finish = GetFieldValueFromDict(rowData, "Finish");
            
            if (!string.IsNullOrEmpty(quality)) parts.Add(quality);
            if (!string.IsNullOrEmpty(gsmStr)) parts.Add($"{gsmStr} GSM");
            if (!string.IsNullOrEmpty(manufacturer)) parts.Add(manufacturer);
            if (!string.IsNullOrEmpty(finish)) parts.Add(finish);
            
            var itemSize = GetFieldValueFromDict(rowData, "ItemSize");
            if (!string.IsNullOrEmpty(itemSize)) parts.Add($"{itemSize} MM");
        }
        
        if (parts.Any())
        {
            rowData["ItemName"] = string.Join(", ", parts);
        }
        
        return rowData;
    }
}






