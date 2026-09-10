using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class SparePartService : ISparePartService
{
    private readonly SqlConnection _connection;

    public SparePartService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<SparePartMasterDto>> GetAllSparePartsAsync()
    {
        // Optimized query: simplified WHERE clause, added NOLOCK hint, changed sort to ID
        var query = @"
            SELECT
                sp.SparePartID,
                sp.SparePartName,
                sp.SparePartGroup,
                ph.DisplayName as HSNGroup,
                sp.Unit,
                sp.Rate,
                sp.SparePartType,
                sp.MinimumStockQty,
                sp.PurchaseOrderQuantity,
                sp.StockRefCode,
                sp.SupplierReference,
                sp.Narration,
                ISNULL(sp.IsDeletedTransaction, 0) as IsDeletedTransaction
            FROM SparePartMaster sp WITH (NOLOCK)
            LEFT JOIN ProductHSNMaster ph ON sp.ProductHSNID = ph.ProductHSNID
            WHERE ISNULL(sp.IsDeletedTransaction, 0) = 0
            ORDER BY sp.SparePartID";

        var spareParts = await _connection.QueryAsync<SparePartMasterDto>(query);
        return spareParts.ToList();
    }

    public async Task<bool> SoftDeleteSparePartAsync(int sparePartId)
    {
        var query = @"
            UPDATE SparePartMaster 
            SET IsDeletedTransaction = 1
            WHERE SparePartID = @SparePartId";

        var rowsAffected = await _connection.ExecuteAsync(query, new { SparePartId = sparePartId });
        return rowsAffected > 0;
    }

    public async Task<SparePartValidationResultDto> ValidateSparePartsAsync(List<SparePartMasterDto> spareParts)
    {
        var result = new SparePartValidationResultDto
        {
            Summary = new ValidationSummary
            {
                TotalRows = spareParts.Count
            }
        };

        // Get existing spare parts from database for duplicate check
        var existingSpareParts = await GetAllSparePartsAsync();

        // Get valid HSN Groups
        var validHSNGroups = await GetHSNGroupsAsync();
        var hsnGroupLookup = new HashSet<string>(
            validHSNGroups.Select(h => h.DisplayName.Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        // Get valid Units
        var validUnits = await GetUnitsAsync();
        var unitLookup = new HashSet<string>(
            validUnits.Select(u => u.UnitSymbol.Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        // Required fields
        var requiredFields = new[] { "SparePartName", "SparePartGroup", "HSNGroup", "Unit", "SparePartType" };

        for (int i = 0; i < spareParts.Count; i++)
        {
            var sparePart = spareParts[i];
            var rowValidation = new SparePartRowValidation
            {
                RowIndex = i,
                Data = sparePart,
                RowStatus = ValidationStatus.Valid
            };

            // Track if row has validation issues
            bool hasMissingData = false;
            bool hasMismatch = false;
            bool hasInvalidContent = false;

            // 1. Check for missing data (BLUE)
            foreach (var field in requiredFields)
            {
                var value = GetPropertyValue(sparePart, field);
                if (string.IsNullOrWhiteSpace(value?.ToString()))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = field,
                        ValidationMessage = $"{field} is required",
                        Status = ValidationStatus.MissingData
                    });

                    if (rowValidation.RowStatus != ValidationStatus.Duplicate)
                        rowValidation.RowStatus = ValidationStatus.MissingData;

                    hasMissingData = true;
                }
            }

            // Check Rate field (numeric field)
            if (!sparePart.Rate.HasValue || sparePart.Rate.Value <= 0)
            {
                rowValidation.CellValidations.Add(new CellValidation
                {
                    ColumnName = "Rate",
                    ValidationMessage = "Rate is required and must be greater than 0",
                    Status = ValidationStatus.MissingData
                });

                if (rowValidation.RowStatus != ValidationStatus.Duplicate)
                    rowValidation.RowStatus = ValidationStatus.MissingData;

                hasMissingData = true;
            }

            // 2. Check for duplicates (RED)
            // Duplicate logic: SparePartName + SparePartGroup + SparePartType
            bool IsDuplicate(SparePartMasterDto a, SparePartMasterDto b)
            {
                var nameA = a.SparePartName?.Trim() ?? "";
                var groupA = a.SparePartGroup?.Trim() ?? "";
                var typeA = a.SparePartType?.Trim() ?? "";

                var nameB = b.SparePartName?.Trim() ?? "";
                var groupB = b.SparePartGroup?.Trim() ?? "";
                var typeB = b.SparePartType?.Trim() ?? "";

                return string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(groupA, groupB, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(typeA, typeB, StringComparison.OrdinalIgnoreCase);
            }

            var isDuplicate = existingSpareParts.Any(e => IsDuplicate(e, sparePart));

            // Also check within the current batch
            var duplicateInBatch = spareParts.Take(i).Any(s => IsDuplicate(s, sparePart));

            if (isDuplicate || duplicateInBatch)
            {
                rowValidation.RowStatus = ValidationStatus.Duplicate;
                rowValidation.ErrorMessage = "Duplicate record found";
            }

            // 3. Check HSNGroup mismatch (YELLOW)
            if (!string.IsNullOrWhiteSpace(sparePart.HSNGroup))
            {
                // Exact match check
                var isValidHSNGroup = hsnGroupLookup.Contains(sparePart.HSNGroup.Trim());

                if (!isValidHSNGroup)
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "HSNGroup",
                        ValidationMessage = "Invalid",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 4. Check Unit mismatch (YELLOW)
            if (!string.IsNullOrWhiteSpace(sparePart.Unit))
            {
                // Exact match check
                var isValidUnit = unitLookup.Contains(sparePart.Unit.Trim());

                if (!isValidUnit)
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "Unit",
                        ValidationMessage = "Invalid",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 5. Check for Special Characters (InvalidContent)
            var stringProperties = typeof(SparePartMasterDto)
                .GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.Name != "Narration");

            foreach (var prop in stringProperties)
            {
                var val = prop.GetValue(sparePart) as string;
                if (!string.IsNullOrEmpty(val) && (val.Contains('\'') || val.Contains('\"')))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = prop.Name,
                        ValidationMessage = "Special characters are found in this row and column.",
                        Status = ValidationStatus.InvalidContent
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.InvalidContent;

                    hasInvalidContent = true;
                }
            }

            // Count each category independently — a single row can appear in multiple categories
            if (rowValidation.RowStatus == ValidationStatus.Duplicate)
                result.Summary.DuplicateCount++;
            if (hasMissingData)
                result.Summary.MissingDataCount++;
            if (hasMismatch)
                result.Summary.MismatchCount++;
            if (hasInvalidContent)
                result.Summary.InvalidContentCount++;

            result.Rows.Add(rowValidation);
        }

        result.Summary.ValidRows = result.Rows.Count(r => r.RowStatus == ValidationStatus.Valid);
        result.IsValid = result.Summary.DuplicateCount == 0 &&
                        result.Summary.MissingDataCount == 0 &&
                        result.Summary.MismatchCount == 0 &&
                        result.Summary.InvalidContentCount == 0;

        return result;
    }

    public async Task<ImportResultDto> ImportSparePartsAsync(List<SparePartMasterDto> spareParts)
    {
        var result = new ImportResultDto();
        result.TotalRows = spareParts.Count;

        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        // ─── 0. Ensure string columns are wide enough ────────────────────────
        var colsToWiden = new[] { "SparePartName", "SparePartGroup", "SparePartType", "HSNGroup", "SupplierReference", "StockRefCode", "Narration" };
        foreach (var col in colsToWiden)
        {
            var widenSql = $@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE Name = '{col}'
                    AND Object_ID = Object_ID(N'SparePartMaster')
                    AND max_length != -1
                    AND max_length / 2 < 500
                )
                BEGIN
                    ALTER TABLE SparePartMaster ALTER COLUMN {col} NVARCHAR(500) NULL;
                END";
            await _connection.ExecuteAsync(widenSql);
        }

        // ─── 1. Fetch lookup data ────────────────────────────────────────────
        var hsnGroups = await GetHSNGroupsAsync();
        var hsnGroupMapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var hsn in hsnGroups)
        {
            if (!string.IsNullOrWhiteSpace(hsn.DisplayName) && !hsnGroupMapping.ContainsKey(hsn.DisplayName))
            {
                hsnGroupMapping[hsn.DisplayName.Trim()] = hsn.ProductHSNID;
            }
        }

        var maxSparePartCode = await _connection.ExecuteScalarAsync<int?>(
            "SELECT ISNULL(MAX(MaxSparePartCode), 0) FROM SparePartMaster WHERE IsDeletedTransaction = 0"
        ) ?? 0;

        // ─── 2. Pre-process rows: validate and prepare ───────────────────────
        var validRows = new List<(int RowIndex, int MaxCode, string SparePartCode,
                                   SparePartMasterDto SparePart, int ProductHSNID)>();

        for (int i = 0; i < spareParts.Count; i++)
        {
            var sparePart = spareParts[i];
            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(sparePart.SparePartName))
                    throw new Exception("SparePartName is required");

                maxSparePartCode++;
                string sparePartCode = $"SPM{maxSparePartCode.ToString().PadLeft(5, '0')}";

                int productHSNID = 0;
                if (!string.IsNullOrWhiteSpace(sparePart.HSNGroup) &&
                    hsnGroupMapping.TryGetValue(sparePart.HSNGroup.Trim(), out int hsnId))
                {
                    productHSNID = hsnId;
                }

                validRows.Add((i, maxSparePartCode, sparePartCode, sparePart, productHSNID));
            }
            catch (Exception ex)
            {
                maxSparePartCode--;
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {i + 1} ({sparePart.SparePartName}): Pre-validation failed – {ex.Message}");
            }
        }

        if (validRows.Count == 0)
        {
            result.Success = false;
            result.Message = "All rows failed validation. Nothing was imported.";
            return result;
        }

        // ─── 3. Build DataTable for SqlBulkCopy ──────────────────────────────
        var dataTable = new System.Data.DataTable();
        dataTable.Columns.Add("SparePartName",         typeof(string));
        dataTable.Columns.Add("SparePartCode",         typeof(string));
        dataTable.Columns.Add("MaxSparePartCode",      typeof(int));
        dataTable.Columns.Add("ProductHSNID",          typeof(int));
        dataTable.Columns.Add("SparePartGroup",        typeof(string));
        dataTable.Columns.Add("SparePartType",         typeof(string));
        dataTable.Columns.Add("Unit",                  typeof(string));
        dataTable.Columns.Add("Rate",                  typeof(object));
        dataTable.Columns.Add("HSNGroup",              typeof(string));
        dataTable.Columns.Add("SupplierReference",     typeof(string));
        dataTable.Columns.Add("StockRefCode",          typeof(string));
        dataTable.Columns.Add("PurchaseOrderQuantity", typeof(object));
        dataTable.Columns.Add("MinimumStockQty",       typeof(object));
        dataTable.Columns.Add("Narration",             typeof(string));
        dataTable.Columns.Add("VoucherPrefix",         typeof(string));
        dataTable.Columns.Add("CompanyID",             typeof(int));
        dataTable.Columns.Add("UserID",                typeof(int));
        dataTable.Columns.Add("VoucherDate",           typeof(DateTime));
        dataTable.Columns.Add("CreatedBy",             typeof(int));
        dataTable.Columns.Add("CreatedDate",           typeof(DateTime));
        dataTable.Columns.Add("IsDeletedTransaction",  typeof(bool));

        object N(object? v) => v ?? DBNull.Value;
        var now = DateTime.Now;

        foreach (var (rowIndex, maxCode, sparePartCode, sp, productHSNID) in validRows)
        {
            try
            {
                dataTable.Rows.Add(
                    N(sp.SparePartName),
                    sparePartCode,
                    maxCode,
                    productHSNID,
                    N(sp.SparePartGroup),
                    N(sp.SparePartType),
                    N(sp.Unit),
                    N(sp.Rate),
                    N(sp.HSNGroup),
                    N(sp.SupplierReference),
                    N(sp.StockRefCode),
                    N(sp.PurchaseOrderQuantity),
                    N(sp.MinimumStockQty),
                    N(sp.Narration),
                    "SPM",
                    2,  // CompanyID
                    2,  // UserID
                    now,
                    2,  // CreatedBy
                    now,
                    false
                );
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {rowIndex + 1} ({sp.SparePartName}): DataTable build failed – {ex.Message}");
            }
        }

        // ─── 4. SqlBulkCopy insert ───────────────────────────────────────────
        try
        {
            using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(_connection)
            {
                DestinationTableName = "SparePartMaster",
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
                result.Message = $"Imported {result.ImportedRows} of {spareParts.Count} spare part(s). {result.ErrorRows} row(s) failed.";
            }
            else
            {
                result.Message = $"Successfully imported {result.ImportedRows} spare part(s).";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Bulk insert failed: {ex.Message}";
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] SparePart Bulk Insert Error: {ex.Message}\nStack: {ex.StackTrace}\n"); } catch { }
        }

        return result;
    }

    public async Task<List<HSNGroupDto>> GetHSNGroupsAsync()
    {
        var query = @"
            SELECT ProductHSNID, LTRIM(RTRIM(DisplayName)) as DisplayName, ProductHSNName
            FROM ProductHSNMaster
            WHERE IsDeletedTransaction = 0 
            AND DisplayName IS NOT NULL 
            AND DisplayName <> ''
            AND ProductCategory = 'Spare Parts'
            ORDER BY DisplayName";

        var results = await _connection.QueryAsync<HSNGroupDto>(query);
        return results.ToList();
    }

    public async Task<List<UnitDto>> GetUnitsAsync()
    {
        var query = @"
            SELECT UnitID, UnitSymbol
            FROM UnitMaster
            WHERE UnitSymbol IS NOT NULL 
            AND UnitSymbol <> ''
            ORDER BY UnitSymbol";

        var results = await _connection.QueryAsync<UnitDto>(query);
        return results.ToList();
    }

    public async Task<int> ClearAllSparePartDataAsync(string username, string password, string reason)
    {
        if (_connection.State != System.Data.ConnectionState.Open) await _connection.OpenAsync();

        // 1. Validate Credentials - Use same password encoding as login
        var encodedPassword = PasswordEncoder.ChangePassword(password ?? string.Empty);

        var userCheckQuery = @"
            SELECT COUNT(1)
            FROM UserMaster
            WHERE UserName = @Username
              AND ISNULL(Password, '') = @Password
              AND ISNULL(IsBlocked, 0) = 0";

        var isValidUser = await _connection.ExecuteScalarAsync<bool>(userCheckQuery, new { Username = username, Password = encodedPassword });

        if (!isValidUser)
        {
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllSparePartData Failed: Invalid credentials for user '{username}'. Reason: {reason}\n"); } catch { }
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        // 2. Perform Transactional Delete
        int deletedCount = 0;
        var transaction = await _connection.BeginTransactionAsync();
        try
        {
            // First, get the count of ALL records to be deleted (TRUNCATE deletes all, so we count all)
            var countQuery = "SELECT COUNT(*) FROM SparePartMaster";
            deletedCount = await _connection.ExecuteScalarAsync<int>(countQuery, transaction: transaction);

            // Delete ALL records from Master (matching TRUNCATE TABLE behavior)
            var deleteMasterQuery = "DELETE FROM SparePartMaster";
            await _connection.ExecuteAsync(deleteMasterQuery, transaction: transaction);

            await transaction.CommitAsync();

            // 3. Log Audit
            try
            {
                var logMessage = $"[{DateTime.Now}] AUDIT: User '{username}' cleared {deletedCount} Spare Part records. Reason: {reason}\n";
                await System.IO.File.AppendAllTextAsync("debug_log.txt", logMessage);
            }
            catch { }

            return deletedCount;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllSparePartData Exception: {ex.Message}\n"); } catch { }
            throw;
        }
    }

    private object? GetPropertyValue(object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName)?.GetValue(obj);
    }
}
