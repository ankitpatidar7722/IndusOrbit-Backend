using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Backend.Services;

public class HSNService : IHSNService
{
    private readonly SqlConnection _connection;
    public HSNService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<HSNMasterDto>> GetHSNListAsync(int companyId)
    {
        await EnsureConnectionOpenAsync();
        
        string query = @"
            SELECT 
                PH.ProductHSNID,
                PH.ProductHSNName,
                PH.DisplayName,
                PH.HSNCode,
                PH.GSTTaxPercentage,
                PH.CGSTTaxPercentage,
                PH.SGSTTaxPercentage,
                PH.IGSTTaxPercentage,
                PH.ProductCategory,
                IG.ItemGroupName,
                PH.ItemGroupID,
                PH.CompanyID,
                PH.IsDeletedTransaction,
                PH.CreatedBy,
                PH.CreatedDate
            FROM ProductHSNMaster PH
            LEFT JOIN ItemGroupMaster IG ON PH.ItemGroupID = IG.ItemGroupID
            WHERE PH.CompanyID = @CompanyId AND PH.IsDeletedTransaction = 0";

        var result = await _connection.QueryAsync<HSNMasterDto>(query, new { CompanyId = companyId });
        return result.ToList();
    }

    public async Task<List<string>> GetItemGroupsAsync(int companyId)
    {
        await EnsureConnectionOpenAsync();
        var result = await _connection.QueryAsync<string>(
            "SELECT ItemGroupName FROM ItemGroupMaster WHERE IsDeletedTransaction = 0 AND CompanyID = @CompanyId ORDER BY ItemGroupName", new { CompanyId = companyId });
        return result.ToList();
    }

    public async Task<HSNValidationResultDto> ValidateHSNsAsync(List<HSNMasterDto> hsns)
    {
        var result = new HSNValidationResultDto();
        result.IsValid = true;
        result.Summary.TotalRows = hsns.Count;

        // Fetch existing DisplayNames for duplicate check
        await EnsureConnectionOpenAsync();
        var existingDisplayNames = await _connection.QueryAsync<string>(
            "SELECT DisplayName FROM ProductHSNMaster WHERE IsDeletedTransaction = 0 AND DisplayName IS NOT NULL AND DisplayName <> ''");
        
        var existingNamesSet = new HashSet<string>(existingDisplayNames);
        
        // Fetch valid Item Groups for Lookup Validation
        var existingItemGroups = await _connection.QueryAsync<(int Id, string Name)>(
            "SELECT ItemGroupID, ItemGroupName FROM ItemGroupMaster WHERE IsDeletedTransaction = 0");
        
        var itemGroupsMap = existingItemGroups
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Required columns validation configuration
        // Group Name (ProductHSNName), Display Name, HSN Code, Product Type (ProductCategory), GST % (GSTTaxPercentage)
        var requiredColumns = new List<string> { "ProductHSNName", "DisplayName", "HSNCode", "ProductCategory", "GSTTaxPercentage" };

        for (int i = 0; i < hsns.Count; i++)
        {
            var hsn = hsns[i];
            var rowValidation = new HSNRowValidation { RowIndex = i, Data = hsn, RowStatus = ValidationStatus.Valid };
            
            bool hasMissing = false;
            bool hasDuplicate = false;
            bool hasHSNMismatch = false;
            bool hasHSNInvalidContent = false;
            
            // 1. Missing Fields Check
            foreach (var col in requiredColumns)
            {
                var value = GetPropertyValue(hsn, col)?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = col,
                        ValidationMessage = $"{col} is required",
                        Status = ValidationStatus.MissingData
                    });
                    rowValidation.RowStatus = ValidationStatus.MissingData;
                    hasMissing = true;
                }
            }

            // 1b. ProductCategory Mismatch Check — must be one of the allowed dropdown values
            var validProductCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Raw Material", "Finish Goods", "Spare Parts", "Service", "Tool"
            };
            if (!string.IsNullOrWhiteSpace(hsn.ProductCategory) && !validProductCategories.Contains(hsn.ProductCategory))
            {
                rowValidation.CellValidations.Add(new CellValidation
                {
                    ColumnName = "productCategory",
                    ValidationMessage = $"'{hsn.ProductCategory}' is not a valid Product Type. Must be one of: Raw Material, Finish Goods, Spare Parts, Service, Tool.",
                    Status = ValidationStatus.Mismatch
                });
                if (rowValidation.RowStatus != ValidationStatus.Duplicate && rowValidation.RowStatus != ValidationStatus.MissingData)
                    rowValidation.RowStatus = ValidationStatus.Mismatch;
                hasHSNMismatch = true;
            }

            // Conditional Validation: Item Group Name
            bool isRawMaterial = string.Equals(hsn.ProductCategory, "Raw Material", StringComparison.OrdinalIgnoreCase);
            
            if (isRawMaterial)
            {
                if (string.IsNullOrWhiteSpace(hsn.ItemGroupName))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "itemGroupName",
                        ValidationMessage = "Item Group Name is required for Raw Material",
                        Status = ValidationStatus.MissingData
                    });
                    if(rowValidation.RowStatus != ValidationStatus.MissingData) 
                        rowValidation.RowStatus = ValidationStatus.MissingData;
                    hasMissing = true;
                }
                else
                {
                     // Validate existence - exact match required
                     if (itemGroupsMap.TryGetValue(hsn.ItemGroupName, out int igId))
                    {
                        hsn.ItemGroupID = igId; // Auto-map ID
                        
                        // Debug log for verification
                        try { 
                            await System.IO.File.AppendAllTextAsync("debug_log.txt", 
                                $"[{DateTime.Now}] Row {i}: '{hsn.ItemGroupName}' matched to ID {igId}\n"); 
                        } catch {}
                    }
                    else
                    {
                        // Debug log for mismatch
                        try { 
                            var availableGroups = string.Join(", ", itemGroupsMap.Keys);
                            await System.IO.File.AppendAllTextAsync("debug_log.txt", 
                                $"[{DateTime.Now}] Row {i}: '{hsn.ItemGroupName}' NOT FOUND. Available: {availableGroups}\n"); 
                        } catch {}
                        
                        rowValidation.CellValidations.Add(new CellValidation
                        {
                            ColumnName = "itemGroupName",
                            ValidationMessage = "Item Group not found in Master",
                            Status = ValidationStatus.Mismatch
                        });
                        
                        if (rowValidation.RowStatus != ValidationStatus.Duplicate && rowValidation.RowStatus != ValidationStatus.MissingData)
                        {
                            rowValidation.RowStatus = ValidationStatus.Mismatch;
                        }
                        hasHSNMismatch = true;
                    }
                }
            }
            else
            {
                // If NOT Raw Material, keep Item Group Name empty. 
                // We enforce this by clearing it if present, so it doesn't get imported incorrectly?
                // Or just ignore it. Let's clear it to be safe and consistent with "Keep Item Group Name empty".
                if (!string.IsNullOrEmpty(hsn.ItemGroupName))
                {
                     hsn.ItemGroupName = null;
                     hsn.ItemGroupID = null;
                }
            }

            // 2. Duplicate Check (DisplayName unique)
            if (!string.IsNullOrWhiteSpace(hsn.DisplayName))
            {
                // Check in DB
                if (existingNamesSet.Contains(hsn.DisplayName))
                {
                    rowValidation.RowStatus = ValidationStatus.Duplicate;
                    rowValidation.ErrorMessage = "Duplicate DisplayName in Database";
                    hasDuplicate = true;
                }
                
                // Check in current batch (previous rows)
                if (hsns.Take(i).Any(x => string.Equals(x.DisplayName, hsn.DisplayName, StringComparison.Ordinal)))
                {
                    rowValidation.RowStatus = ValidationStatus.Duplicate;
                    rowValidation.ErrorMessage = "Duplicate DisplayName in File";
                    hasDuplicate = true;
                }
            }

            // 4. Invalid Content (Special Characters) - Check All String Fields
            bool hasInvalidContent = false;
            var stringProperties = typeof(HSNMasterDto)
                .GetProperties()
                .Where(p => p.PropertyType == typeof(string));

            foreach (var prop in stringProperties)
            {
                var val = prop.GetValue(hsn) as string;
                if (!string.IsNullOrEmpty(val) && (val.Contains('\'') || val.Contains('"')))
                {
                    // Convert PascalCase to camelCase for frontend compatibility
                    string camelCaseName = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
                    
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = camelCaseName,
                        ValidationMessage = "Special characters (quotes) are not allowed.",
                        Status = ValidationStatus.InvalidContent
                    });
                    
                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.InvalidContent;

                    hasInvalidContent = true;
                    hasHSNInvalidContent = true;
                }
            }

            // Count each category independently — a single row can appear in multiple categories
            if (hasDuplicate)
                result.Summary.DuplicateCount++;
            if (hasMissing)
                result.Summary.MissingDataCount++;
            if (hasHSNMismatch)
                result.Summary.MismatchCount++;
            if (hasHSNInvalidContent)
                result.Summary.InvalidContentCount++;

            if (rowValidation.RowStatus != ValidationStatus.Valid)
            {
                result.IsValid = false;
            }

            result.Rows.Add(rowValidation);
        }

        result.Summary.ValidRows = result.Rows.Count(r => r.RowStatus == ValidationStatus.Valid);

        return result;
    }

    public async Task<ImportResultDto> ImportHSNsAsync(List<HSNMasterDto> hsns, int userId)
    {
        var result = new ImportResultDto();
        result.TotalRows = hsns.Count;
        await EnsureConnectionOpenAsync();

        // ─── 1. Pre-process rows: validate and prepare for bulk insert ───────
        var validRows = new List<(int RowIndex, HSNMasterDto HSN)>();

        for (int i = 0; i < hsns.Count; i++)
        {
            var hsn = hsns[i];
            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(hsn.ProductHSNName))
                    throw new Exception("ProductHSNName is required");

                if (hsn.CompanyID == 0) hsn.CompanyID = 2;

                validRows.Add((i, hsn));
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {i + 1} ({hsn.DisplayName ?? hsn.ProductHSNName}): Pre-validation failed – {ex.Message}");
            }
        }

        if (validRows.Count == 0)
        {
            result.Success = false;
            result.Message = "All rows failed validation. Nothing was imported.";
            return result;
        }

        // ─── 2. Build DataTable for SqlBulkCopy ──────────────────────────────
        var dataTable = new System.Data.DataTable();
        dataTable.Columns.Add("ProductHSNName",      typeof(string));
        dataTable.Columns.Add("HSNCode",             typeof(string));
        dataTable.Columns.Add("UnderProductHSNID",   typeof(int));
        dataTable.Columns.Add("GroupLevel",          typeof(int));
        dataTable.Columns.Add("CompanyID",           typeof(int));
        dataTable.Columns.Add("DisplayName",         typeof(string));
        dataTable.Columns.Add("TariffNo",            typeof(string));
        dataTable.Columns.Add("ProductCategory",     typeof(string));
        dataTable.Columns.Add("GSTTaxPercentage",    typeof(object));
        dataTable.Columns.Add("CGSTTaxPercentage",   typeof(object));
        dataTable.Columns.Add("SGSTTaxPercentage",   typeof(object));
        dataTable.Columns.Add("IGSTTaxPercentage",   typeof(object));
        dataTable.Columns.Add("TallyProductHSNName", typeof(string));
        dataTable.Columns.Add("TallyGUID",           typeof(int));
        dataTable.Columns.Add("ItemGroupID",         typeof(object));
        dataTable.Columns.Add("UserID",              typeof(int));
        dataTable.Columns.Add("ModifiedDate",        typeof(DateTime));
        dataTable.Columns.Add("CreatedBy",           typeof(int));
        dataTable.Columns.Add("CreatedDate",         typeof(DateTime));
        dataTable.Columns.Add("ModifiedBy",          typeof(int));
        dataTable.Columns.Add("DeletedBy",           typeof(int));
        dataTable.Columns.Add("DeletedDate",         typeof(object));
        dataTable.Columns.Add("IsDeletedTransaction",typeof(bool));
        dataTable.Columns.Add("FYear",               typeof(string));

        object N(object? v) => v ?? DBNull.Value;
        var now = DateTime.Now;

        foreach (var (rowIndex, hsn) in validRows)
        {
            try
            {
                dataTable.Rows.Add(
                    N(hsn.ProductHSNName),
                    N(hsn.HSNCode),
                    0,  // UnderProductHSNID
                    0,  // GroupLevel
                    hsn.CompanyID,
                    N(hsn.DisplayName),
                    "",  // TariffNo
                    N(hsn.ProductCategory),
                    N(hsn.GSTTaxPercentage ?? 0),
                    N(hsn.CGSTTaxPercentage ?? 0),
                    N(hsn.SGSTTaxPercentage ?? 0),
                    N(hsn.IGSTTaxPercentage ?? 0),
                    "",  // TallyProductHSNName
                    0,   // TallyGUID
                    N(hsn.ItemGroupID),
                    userId,
                    now,
                    userId,
                    now,
                    userId,
                    0,   // DeletedBy
                    DBNull.Value,  // DeletedDate
                    false,
                    "2025-2026"
                );
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {rowIndex + 1} ({hsn.DisplayName ?? hsn.ProductHSNName}): DataTable build failed – {ex.Message}");
            }
        }

        // ─── 3. SqlBulkCopy insert ───────────────────────────────────────────
        try
        {
            using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(_connection)
            {
                DestinationTableName = "ProductHSNMaster",
                BatchSize = 1000,
                BulkCopyTimeout = 300
            };

            foreach (System.Data.DataColumn col in dataTable.Columns)
                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

            await bulkCopy.WriteToServerAsync(dataTable);

            result.ImportedRows = dataTable.Rows.Count;
            result.Success = true;

            if (result.ErrorRows > 0)
            {
                result.Message = $"Imported {result.ImportedRows} of {hsns.Count} HSN record(s). {result.ErrorRows} row(s) failed.";
            }
            else
            {
                result.Message = $"Successfully imported {result.ImportedRows} HSN records.";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Bulk insert failed: {ex.Message}";
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] HSN Bulk Insert Error: {ex.Message}\nStack: {ex.StackTrace}\n"); } catch { }
        }

        return result;
    }

    public async Task<ImportResultDto> ClearHSNDataAsync(int companyId, string username, string password, string reason)
    {
        await EnsureConnectionOpenAsync();
        // Use async-compatible transaction handling if Dapper supports it fully, but ADO.NET transaction
        // implies the underlying connection must remain open.
        using var transaction = _connection.BeginTransaction(); 

        try
        {
            // 1. Authenticate User
            var encodedPassword = PasswordEncoder.ChangePassword(password ?? string.Empty);
            var userCheckQuery = @"SELECT COUNT(1) FROM UserMaster
                WHERE UserName = @Username
                  AND ISNULL(Password, '') = @Password
                  AND ISNULL(IsBlocked, 0) = 0";
            var isValidUser = await _connection.ExecuteScalarAsync<bool>(
                userCheckQuery, new { Username = username, Password = encodedPassword }, transaction: transaction);
            if (!isValidUser)
            {
                throw new UnauthorizedAccessException("Invalid username or password.");
            }
            
            // 2. Audit Log (File based as DB structure is unknown)
            try 
            {
                // Appending to shared log file
                string logEntry = $"[{DateTime.Now}] User: {username}, Module: HSN Master, Action: Clear Data, Reason: {reason}{Environment.NewLine}";
                await System.IO.File.AppendAllTextAsync("audit_log.txt", logEntry);
            }
            catch {}

            // 3. Count rows before clearing
            var rowCount = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ProductHSNMaster", transaction: transaction);

            // 4. Clear Data
            await _connection.ExecuteAsync("DELETE FROM ProductHSNMaster", transaction: transaction);

            transaction.Commit();
            return new ImportResultDto { Success = true, Message = $"Successfully deleted {rowCount} rows from HSN Master.", ImportedRows = rowCount };
        }
        catch (Exception ex)
        {
             transaction.Rollback();
             // Log error
             try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] Clear HSN Error: {ex.Message}\n"); } catch {}
             throw; // Controller will handle 500 or Unauthorized
        }
    }


    public async Task<ImportResultDto> SoftDeleteHSNAsync(int hsnId, int userId)
    {
         await EnsureConnectionOpenAsync();
         try
         {
             var result = await _connection.ExecuteAsync(
                 "UPDATE ProductHSNMaster SET IsDeletedTransaction = 1, DeletedBy = @UserId, DeletedDate = GETDATE() WHERE ProductHSNID = @Id",
                 new { Id = hsnId, UserId = userId });
             
             return new ImportResultDto { Success = true, Message = "Record deleted successfully." };
         }
         catch (Exception ex)
         {
             return new ImportResultDto { Success = false, Message = "Failed to delete: " + ex.Message };
         }
    }

    // Helpers
    private async Task EnsureConnectionOpenAsync()
    {
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync();
    }

    private object? GetPropertyValue(object obj, string propName)
    {
        return obj.GetType().GetProperty(propName)?.GetValue(obj, null);
    }

    private void UpdateSummary(ValidationSummary summary, ValidationStatus status)
    {
        switch (status)
        {
            case ValidationStatus.Valid: summary.ValidRows++; break;
            case ValidationStatus.Duplicate: summary.DuplicateCount++; break;
            case ValidationStatus.MissingData: summary.MissingDataCount++; break;
            case ValidationStatus.Mismatch: summary.MismatchCount++; break;
            case ValidationStatus.InvalidContent: summary.InvalidContentCount++; break;
        }
    }
}
