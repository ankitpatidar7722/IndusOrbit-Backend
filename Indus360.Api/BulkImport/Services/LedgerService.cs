using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class LedgerService : ILedgerService
{
    private readonly SqlConnection _connection;

    public LedgerService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<LedgerMasterDto>> GetLedgersByGroupAsync(int ledgerGroupId)
    {
        // ── Main load: call the existing stored procedure GetLedgerMasterData ──────────────
        // SP branches:
        //   LedgerGroupID = 1  → Clients    : PIVOT on GSTRegistrationType, PartyType, CustomerCategory, CreditDays
        //   LedgerGroupID = 4  → Consignees : specific columns + ClientName self-join
        //   All others         : LM.* + DepartmentMaster JOIN

        // Query as dynamic first to handle malformed column names from SP's PIVOT
        var dynamicResults = (await _connection.QueryAsync<dynamic>(
            "GetLedgerMasterData",
            new
            {
                TblName       = "LedgerMaster",
                CompanyID     = "2",
                LedgerGroupID = ledgerGroupId.ToString()
            },
            commandType: System.Data.CommandType.StoredProcedure
        )).ToList();

        if (dynamicResults.Count == 0)
            return new List<LedgerMasterDto>();

        // Manually map dynamic results to LedgerMasterDto with robust error handling
        var ledgerList = new List<LedgerMasterDto>();

        foreach (var row in dynamicResults)
        {
            var ledger = new LedgerMasterDto();
            var dict = (IDictionary<string, object>)row;

            // Helper to safely get and parse values
            object? GetValue(string key) => dict.ContainsKey(key) ? dict[key] : null;

            string? GetString(string key)
            {
                var val = GetValue(key);
                return val == null || val == DBNull.Value ? null : val.ToString();
            }

            int? GetInt(string key)
            {
                var val = GetString(key);
                if (string.IsNullOrWhiteSpace(val)) return null;
                return int.TryParse(val, out var result) ? result : null;
            }

            decimal? GetDecimal(string key)
            {
                var val = GetString(key);
                if (string.IsNullOrWhiteSpace(val)) return null;
                return decimal.TryParse(val, out var result) ? result : null;
            }

            bool GetBool(string key)
            {
                var val = GetValue(key);
                if (val == null || val == DBNull.Value) return false;
                if (val is bool b) return b;
                var str = val.ToString()?.Trim().ToLower();
                return str == "1" || str == "true";
            }

            bool? GetNullableBool(string key)
            {
                var val = GetValue(key);
                if (val == null || val == DBNull.Value) return null;
                if (val is bool b) return b;
                var str = val.ToString()?.Trim().ToLower();
                if (string.IsNullOrEmpty(str)) return null;
                return str == "1" || str == "true";
            }

            // Map all properties, handling malformed column names
            ledger.LedgerID = GetInt("LedgerID") ?? 0;
            ledger.LedgerName = GetString("LedgerName");
            ledger.MailingName = GetString("MailingName");
            ledger.Address1 = GetString("Address1");
            ledger.Address2 = GetString("Address2");
            ledger.Address3 = GetString("Address3");
            ledger.City = GetString("City");
            ledger.State = GetString("State");
            ledger.Country = GetString("Country");
            ledger.Pincode = GetString("Pincode");
            ledger.MobileNo = GetString("MobileNo");
            ledger.TelephoneNo = GetString("TelephoneNo") ?? GetString("PhoneNo");
            ledger.Email = GetString("Email") ?? GetString("EmailID");
            ledger.Website = GetString("Website");
            ledger.LedgerGroupID = GetInt("LedgerGroupID") ?? 0;
            ledger.SalesRepresentative = GetString("SalesRepresentative") ?? GetString("ContactPerson");
            ledger.Designation = GetString("Designation");
            ledger.GSTNo = GetString("GSTNo") ?? GetString("GSTIN");
            ledger.PANNo = GetString("PANNo") ?? GetString("PAN");
            ledger.DeliveredQtyTolerance = GetDecimal("DeliveredQtyTolerance");
            ledger.SupplyTypeCode = GetString("SupplyTypeCode");
            ledger.Distance = GetDecimal("Distance");
            ledger.LegalName = GetString("LegalName");
            ledger.MailingAddress = GetString("MailingAddress");
            ledger.IsDeletedTransaction = GetBool("IsDeletedTransaction");

            // Handle CreditDays with both "CreditDays" and "CreditDays=" column names (database is nvarchar)
            ledger.CreditDays = GetString("CreditDays") ?? GetString("CreditDays=");

            // Handle other PIVOT columns that might have malformed names
            ledger.GSTRegistrationType = GetString("GSTRegistrationType") ?? GetString("GSTRegistrationType=");
            ledger.RefCode = GetString("RefCode") ?? GetString("RefCode=");
            ledger.CurrencyCode = GetString("CurrencyCode") ?? GetString("CurrencyCode=");
            ledger.GSTApplicable = GetNullableBool("GSTApplicable") ?? GetNullableBool("GSTApplicable=");

            // Additional fields from stored procedure
            ledger.DepartmentName = GetString("DepartmentName");
            ledger.ClientName = GetString("ClientName");
            ledger.DepartmentID = GetInt("DepartmentID");
            ledger.RefClientID = GetInt("RefClientID");

            ledgerList.Add(ledger);
        }

        if (ledgerList.Count == 0)
            return ledgerList;

        // ── Supplemental batch: fetch detail fields the SP doesn't return ──────────────────
        // Uses a JOIN to LedgerMaster (filtered by LedgerGroupID) instead of IN @LedgerIDs
        // to avoid SQL Server's 2100-parameter limit when there are many records.
        string fieldFilter = ledgerGroupId == 1
            ? "'RefCode','CurrencyCode','GSTApplicable'"
            : "'GSTRegistrationType','RefCode','CreditDays','CurrencyCode','GSTApplicable'";

        var detailsQuery = $@"
            SELECT LD.LedgerID, LD.FieldName, LD.FieldValue
            FROM   LedgerMasterDetails LD
            INNER JOIN LedgerMaster LM ON LM.LedgerID = LD.LedgerID
            WHERE  LM.LedgerGroupID = @LedgerGroupId
              AND  ISNULL(LM.IsDeletedTransaction, 0) = 0
              AND  ISNULL(LD.IsDeletedTransaction, 0) = 0
              AND  LD.FieldValue IS NOT NULL
              AND  LD.FieldName IN ({fieldFilter})";

        var allDetails = await _connection.QueryAsync<dynamic>(detailsQuery, new { LedgerGroupId = ledgerGroupId });

        // Group by LedgerID → latest value per field (equivalent to TOP 1 ORDER BY DESC)
        var detailsByLedger = allDetails
            .GroupBy(d => (int)d.LedgerID)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(d => (string)d.FieldName)
                       .ToDictionary(
                           fg => fg.Key,
                           fg => (string)fg.Last().FieldValue,
                           StringComparer.OrdinalIgnoreCase));

        // Map supplemental fields back onto each ledger DTO
        foreach (var ledger in ledgerList)
        {
            if (ledger.LedgerID <= 0) continue;
            if (!detailsByLedger.TryGetValue(ledger.LedgerID, out var fields)) continue;

            if (fields.TryGetValue("RefCode", out var refCode))
                ledger.RefCode = refCode;

            if (fields.TryGetValue("CurrencyCode", out var currencyCode))
                ledger.CurrencyCode = currencyCode;

            if (fields.TryGetValue("GSTApplicable", out var gstApplicableStr))
            {
                var str = gstApplicableStr?.Trim().ToLower();
                if (!string.IsNullOrEmpty(str))
                    ledger.GSTApplicable = (str == "1" || str == "true");
            }

            // Only needed for non-Clients groups (SP PIVOT already covers Group 1)
            if (ledgerGroupId != 1)
            {
                if (fields.TryGetValue("GSTRegistrationType", out var gstRegType))
                    ledger.GSTRegistrationType = gstRegType;

                if (fields.TryGetValue("CreditDays", out var creditDaysStr))
                    ledger.CreditDays = creditDaysStr;
            }
        }

        return ledgerList;
    }

    public async Task<bool> SoftDeleteLedgerAsync(int ledgerId)
    {
        var query = @"
            UPDATE LedgerMaster 
            SET IsDeletedTransaction = 1
            WHERE LedgerID = @LedgerId";

        var rowsAffected = await _connection.ExecuteAsync(query, new { LedgerId = ledgerId });
        return rowsAffected > 0;
    }

    public async Task<LedgerValidationResultDto> ValidateLedgersAsync(List<LedgerMasterDto> ledgers, int ledgerGroupId)
    {
        var result = new LedgerValidationResultDto
        {
            Summary = new ValidationSummary
            {
                TotalRows = ledgers.Count
            }
        };

        // Get Ledger Group Name to check for Supplier
        var ledgerGroupName = await _connection.ExecuteScalarAsync<string>(
            "SELECT LedgerGroupName FROM LedgerGroupMaster WHERE LedgerGroupID = @LedgerGroupId",
            new { LedgerGroupId = ledgerGroupId });
        
        bool isSupplier = ledgerGroupName?.ToLower().Contains("supplier") == true;
        bool isEmployee = ledgerGroupName?.ToLower().Contains("employee") == true;
        bool isConsignee = ledgerGroupName?.ToLower().Contains("consignee") == true;
        bool isVendor = ledgerGroupName?.ToLower().Contains("vendors") == true;
        bool isTransporter = ledgerGroupName?.ToLower().Contains("transporters") == true;

        // Get existing ledgers from database for duplicate check
        var existingLedgers = await GetLedgersByGroupAsync(ledgerGroupId);

        // Get valid Country/State combinations
        var validCountryStates = await GetCountryStatesAsync();

        // Get Valid Clients if needed
        var validClients = new List<ClientDto>();
        if (ledgers.Any(l => !string.IsNullOrWhiteSpace(l.ClientName)))
        {
            validClients = await GetClientsAsync();
        }

        // Get valid Departments if this is an Employee group
        var validDepartmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (isEmployee)
        {
            var deptRows = await _connection.QueryAsync<string>(
                "SELECT LTRIM(RTRIM(DepartmentName)) FROM DepartmentMaster WHERE DepartmentName IS NOT NULL");
            foreach (var d in deptRows)
                if (!string.IsNullOrWhiteSpace(d)) validDepartmentNames.Add(d);
        }

        // Get field metadata from LedgerGroupFieldMaster for dynamic datatype validation
        var fieldMetadataQuery = @"SELECT FieldName, FieldDataType
            FROM LedgerGroupFieldMaster
            WHERE LedgerGroupID = @LedgerGroupID
              AND ISNULL(IsDeletedTransaction, 0) <> 1
              AND FieldName IS NOT NULL
              AND FieldDataType IS NOT NULL";
        var fieldMetadata = (await _connection.QueryAsync<dynamic>(fieldMetadataQuery, new { LedgerGroupID = ledgerGroupId }))
            .ToDictionary(
                f => (string)f.FieldName,
                f => ((string)f.FieldDataType).Trim().ToLower(),
                StringComparer.OrdinalIgnoreCase
            );

        // Required fields
        var requiredList = new List<string> { "LedgerName", "MailingName", "Address1", "Country", "State", "City", "MobileNo" };
        
        if (isEmployee)
        {
            requiredList.Add("DepartmentName");
            requiredList.Add("Designation");
            // GSTNo is not mandatory for employees
        }
        else
        {
            requiredList.Add("GSTNo");
        }

        if (isSupplier || isVendor)
        {
            requiredList.Add("CurrencyCode");
        }
        var requiredFields = requiredList.ToArray();

        for (int i = 0; i < ledgers.Count; i++)
        {
            var ledger = ledgers[i];
            var rowValidation = new LedgerRowValidation
            {
                RowIndex = i,
                Data = ledger,
                RowStatus = ValidationStatus.Valid
            };

            // Track if row has validation issues
            bool hasMissingData = false;
            bool hasMismatch = false;

            // 1. Check for missing data (BLUE)
            // Note: Skip fields in RawValues — they have invalid content, not missing data
            foreach (var field in requiredFields)
            {
                if (ledger.RawValues != null && ledger.RawValues.ContainsKey(field))
                    continue;

                var value = GetPropertyValue(ledger, field);
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

            // 2. Check for duplicates (RED)
            // Helper for composite comparison
            bool IsDuplicate(LedgerMasterDto a, LedgerMasterDto b)
            {
                var nameA = a.LedgerName?.Trim() ?? "";
                var addrA = a.Address1?.Trim() ?? "";
                
                var nameB = b.LedgerName?.Trim() ?? "";
                var addrB = b.Address1?.Trim() ?? "";

                if (isEmployee)
                {
                    // For Employees: LedgerName + MailingName + Address1 + DepartmentName + Designation
                    var mailA = a.MailingName?.Trim() ?? "";
                    var mailB = b.MailingName?.Trim() ?? "";
                    
                    var deptA = a.DepartmentName?.Trim() ?? "";
                    var deptB = b.DepartmentName?.Trim() ?? "";
                    
                    var desigA = a.Designation?.Trim() ?? "";
                    var desigB = b.Designation?.Trim() ?? "";

                    return string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(mailA, mailB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(addrA, addrB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(deptA, deptB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(desigA, desigB, StringComparison.OrdinalIgnoreCase);
                }
                else if (isConsignee)
                {
                    // For Consignee: LedgerName + ClientName + Address1 + GSTNo
                    var gstA = a.GSTNo?.Trim() ?? "";
                    var gstB = b.GSTNo?.Trim() ?? "";

                    var clientA = a.ClientName?.Trim() ?? "";
                    var clientB = b.ClientName?.Trim() ?? "";

                    return string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(clientA, clientB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(addrA, addrB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(gstA, gstB, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // For Others: LedgerName + Address1 + GSTNo
                    var gstA = a.GSTNo?.Trim() ?? "";
                    var gstB = b.GSTNo?.Trim() ?? "";

                    return string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(addrA, addrB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(gstA, gstB, StringComparison.OrdinalIgnoreCase);
                }
            }

            var isDuplicate = existingLedgers.Any(e => IsDuplicate(e, ledger));

            // Also check within the current batch
            var duplicateInBatch = ledgers.Take(i).Any(l => IsDuplicate(l, ledger));

            if (isDuplicate || duplicateInBatch)
            {
                rowValidation.RowStatus = ValidationStatus.Duplicate;
                rowValidation.ErrorMessage = "Duplicate record found";
            }

             // 3. Check Country/State mismatch (YELLOW)
            if (!string.IsNullOrWhiteSpace(ledger.Country) && !string.IsNullOrWhiteSpace(ledger.State))
            {
                var isValidCountryState = validCountryStates.Any(cs =>
                    string.Equals(cs.Country, ledger.Country, StringComparison.Ordinal) &&
                    string.Equals(cs.State, ledger.State, StringComparison.Ordinal));

                if (!isValidCountryState)
                {
                    // Add validation for both Country and State columns individually
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "Country",
                        ValidationMessage = "Invalid Country/State combination",
                        Status = ValidationStatus.Mismatch
                    });

                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "State",
                        ValidationMessage = "Invalid Country/State combination",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // Check ClientName mismatch (Consignee)
            if (!string.IsNullOrWhiteSpace(ledger.ClientName))
            {
                var isValidClient = validClients.Any(c => string.Equals(c.LedgerName, ledger.ClientName, StringComparison.Ordinal));

                if (!isValidClient)
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "ClientName",
                        ValidationMessage = "Client not found in system",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                         rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // Check DepartmentName mismatch (Employees only)
            if (isEmployee && !string.IsNullOrWhiteSpace(ledger.DepartmentName))
            {
                var trimmedDept = ledger.DepartmentName.Trim();
                if (!validDepartmentNames.Contains(trimmedDept))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "DepartmentName",
                        ValidationMessage = $"Department '{trimmedDept}' not found in DepartmentMaster",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 4. Check for Special Characters (InvalidContent)
            bool hasInvalidContent = false;
            var stringProperties = typeof(LedgerMasterDto)
                .GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.Name != "LegalName" && p.Name != "MailingAddress");

            foreach (var prop in stringProperties)
            {
                var val = prop.GetValue(ledger) as string;
                if (!string.IsNullOrEmpty(val) && (val.Contains('\'') || val.Contains('\"') ))
                {
                     // Skip specific fields if necessary, currently checking all string fields
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = prop.Name,
                        ValidationMessage = "Single quote (') and double quote (\") are not allowed.",
                        Status = ValidationStatus.InvalidContent
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.InvalidContent;
                    
                    hasInvalidContent = true;
                }
            }

            // 5. Dynamic datatype validation from LedgerGroupFieldMaster (InvalidContent - PURPLE)
            foreach (var fieldEntry in fieldMetadata)
            {
                var fieldName = fieldEntry.Key;
                var fieldDataType = fieldEntry.Value;

                var rawValue = GetPropertyValue(ledger, fieldName);
                if (rawValue == null) continue;

                var strValue = rawValue.ToString()?.Trim();
                if (string.IsNullOrEmpty(strValue)) continue;

                bool isValid = true;
                string validationMsg = "";

                switch (fieldDataType)
                {
                    case "integer":
                    case "int":
                        if (!long.TryParse(strValue, out _))
                        {
                            isValid = false;
                            validationMsg = $"Invalid Content in {fieldName} — Only Integer values allowed.";
                        }
                        break;

                    case "real":
                    case "decimal":
                    case "float":
                    case "numeric":
                    case "money":
                        if (!decimal.TryParse(strValue, out _))
                        {
                            isValid = false;
                            validationMsg = $"Invalid Content in {fieldName} — Only Numeric values allowed.";
                        }
                        break;

                    case "bit":
                    case "boolean":
                        var boolValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { "true", "false", "0", "1", "yes", "no" };
                        if (!boolValues.Contains(strValue))
                        {
                            isValid = false;
                            validationMsg = $"Invalid Content in {fieldName} — Only True/False values allowed.";
                        }
                        break;

                    case "date":
                    case "datetime":
                        if (!DateTime.TryParse(strValue, out _))
                        {
                            isValid = false;
                            validationMsg = $"Invalid Content in {fieldName} — Only valid Date values allowed.";
                        }
                        break;

                    // varchar, string, nvarchar, text — no numeric validation needed
                    default:
                        break;
                }

                if (!isValid)
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = fieldName,
                        ValidationMessage = validationMsg,
                        Status = ValidationStatus.InvalidContent
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.InvalidContent;

                    hasInvalidContent = true;
                }
            }

            // 5b. Validate RawValues — fields where frontend couldn't parse the value
            if (ledger.RawValues != null && ledger.RawValues.Count > 0)
            {
                foreach (var rawEntry in ledger.RawValues)
                {
                    var rawFieldName = rawEntry.Key;
                    var rawValue = rawEntry.Value;

                    string expectedType = "unknown";
                    if (fieldMetadata.TryGetValue(rawFieldName, out var fdt))
                    {
                        expectedType = fdt;
                    }
                    else
                    {
                        var prop = typeof(LedgerMasterDto).GetProperty(rawFieldName);
                        if (prop != null)
                        {
                            var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                            if (underlyingType == typeof(decimal) || underlyingType == typeof(double) || underlyingType == typeof(float))
                                expectedType = "decimal";
                            else if (underlyingType == typeof(int) || underlyingType == typeof(long))
                                expectedType = "integer";
                            else if (underlyingType == typeof(bool))
                                expectedType = "boolean";
                            else if (underlyingType == typeof(string))
                                expectedType = "varchar";
                        }
                    }

                    string validationMsg = expectedType switch
                    {
                        "integer" or "int" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid Integer.",
                        "real" or "decimal" or "float" or "numeric" or "money" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid Number.",
                        "bit" or "boolean" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid True/False value.",
                        "date" or "datetime" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid Date.",
                        _ => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid value."
                    };

                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = rawFieldName,
                        ValidationMessage = validationMsg,
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

    public async Task<ImportResultDto> ImportLedgersAsync(List<LedgerMasterDto> ledgers, int ledgerGroupId)
    {
        var result = new ImportResultDto();
        result.TotalRows = ledgers.Count;
        if (_connection.State != System.Data.ConnectionState.Open) await _connection.OpenAsync();

        // ── 1. Group metadata ─────────────────────────────────────────────────────────────
        string prefix = "LGR";
        string ledgerType = "Suppliers";
        try
        {
            var groupData = await _connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT LedgerGroupPrefix, LedgerGroupName FROM LedgerGroupMaster WHERE LedgerGroupID = @GID",
                new { GID = ledgerGroupId });
            if (groupData != null)
            {
                if (!string.IsNullOrEmpty(groupData.LedgerGroupPrefix)) prefix = groupData.LedgerGroupPrefix;
                if (!string.IsNullOrEmpty(groupData.LedgerGroupName)) ledgerType = groupData.LedgerGroupName;
            }
        }
        catch { }

        bool isEmployee  = ledgerType.ToLower().Contains("employee");
        bool isConsignee = ledgerType.ToLower().Contains("consignee");
        bool isClient    = ledgerGroupId == 1; // LedgerGroupID 1 = Clients (DB name is "Sundry Debtors")

        // ── 2. Max ledger number ──────────────────────────────────────────────────────────
        var maxLedgerNo = await _connection.ExecuteScalarAsync<int?>(
            "SELECT MAX(MaxLedgerNo) FROM LedgerMaster WHERE LedgerGroupID = @GID AND IsDeletedTransaction = 0",
            new { GID = ledgerGroupId }) ?? 0;

        // ── 3. Batch pre-resolve all lookups (3 queries instead of N×3) ───────────────────

        // 3a. SalesRep name → ID
        var salesRepNames = ledgers
            .Where(l => !string.IsNullOrWhiteSpace(l.SalesRepresentative))
            .Select(l => l.SalesRepresentative!.Trim()).Distinct().ToList();
        var salesRepMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (salesRepNames.Count > 0)
        {
            var rows = await _connection.QueryAsync<(string Name, int Id)>(
                @"SELECT LM.LedgerName AS Name, LM.LedgerID AS Id
                  FROM LedgerMaster LM
                  INNER JOIN LedgerGroupMaster LG ON LG.LedgerGroupID = LM.LedgerGroupID AND LG.CompanyID = LM.CompanyID
                  WHERE LG.LedgerGroupNameID = 27 AND LM.DepartmentID = -50
                    AND ISNULL(LM.IsDeletedTransaction, 0) <> 1 AND LM.CompanyID = 2
                    AND LM.LedgerName IN @Names",
                new { Names = salesRepNames });
            foreach (var r in rows) salesRepMap[r.Name] = r.Id;
        }

        // 3b. Department name → ID
        var deptNames = ledgers
            .Where(l => !string.IsNullOrWhiteSpace(l.DepartmentName))
            .Select(l => l.DepartmentName!.Trim()).Distinct().ToList();
        var deptMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (deptNames.Count > 0)
        {
            var rows = await _connection.QueryAsync<(string Name, int Id)>(
                "SELECT LTRIM(RTRIM(DepartmentName)) AS Name, DepartmentID AS Id FROM DepartmentMaster WHERE LTRIM(RTRIM(DepartmentName)) IN @Names",
                new { Names = deptNames });
            foreach (var r in rows) deptMap[r.Name.Trim()] = r.Id;
        }

        // 3c. Client name → ID (Consignee only)
        var clientMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (isConsignee)
        {
            var clientNames = ledgers
                .Where(l => !string.IsNullOrWhiteSpace(l.ClientName))
                .Select(l => l.ClientName!.Trim()).Distinct().ToList();
            if (clientNames.Count > 0)
            {
                var rows = await _connection.QueryAsync<(string Name, int Id)>(
                    "SELECT LedgerName AS Name, LedgerID AS Id FROM LedgerMaster WHERE LedgerName IN @Names AND ISNULL(IsDeletedTransaction, 0) = 0",
                    new { Names = clientNames });
                foreach (var r in rows) clientMap[r.Name] = r.Id;
            }
        }

        // ── 4. Build DataTable for LedgerMaster ──────────────────────────────────────────
        var masterTable = new System.Data.DataTable("LedgerMaster");
        masterTable.Columns.Add("LedgerCode",             typeof(string));
        masterTable.Columns.Add("MaxLedgerNo",            typeof(int));
        masterTable.Columns.Add("LedgerCodePrefix",       typeof(string));
        masterTable.Columns.Add("LedgerGroupID",          typeof(int));
        masterTable.Columns.Add("LedgerName",             typeof(string));
        masterTable.Columns.Add("MailingName",            typeof(string));
        masterTable.Columns.Add("Address1",               typeof(string));
        masterTable.Columns.Add("Address2",               typeof(string));
        masterTable.Columns.Add("Address3",               typeof(string));
        masterTable.Columns.Add("Country",                typeof(string));
        masterTable.Columns.Add("State",                  typeof(string));
        masterTable.Columns.Add("City",                   typeof(string));
        masterTable.Columns.Add("Pincode",                typeof(string));
        masterTable.Columns.Add("TelephoneNo",            typeof(string));
        masterTable.Columns.Add("Email",                  typeof(string));
        masterTable.Columns.Add("MobileNo",               typeof(string));
        masterTable.Columns.Add("Website",                typeof(string));
        masterTable.Columns.Add("PANNo",                  typeof(string));
        masterTable.Columns.Add("GSTNo",                  typeof(string));
        masterTable.Columns.Add("RefSalesRepresentativeID", typeof(int));
        masterTable.Columns.Add("SupplyTypeCode",         typeof(string));
        masterTable.Columns.Add("GSTApplicable",          typeof(bool));
        masterTable.Columns.Add("Distance",               typeof(decimal));
        masterTable.Columns.Add("DeliveredQtyTolerance",  typeof(decimal));
        masterTable.Columns.Add("IsDeletedTransaction",   typeof(bool));
        masterTable.Columns.Add("CompanyID",              typeof(int));
        masterTable.Columns.Add("UserID",                 typeof(int));
        masterTable.Columns.Add("FYear",                  typeof(string));
        masterTable.Columns.Add("CreatedDate",            typeof(DateTime));
        masterTable.Columns.Add("CreatedBy",              typeof(int));
        masterTable.Columns.Add("ISLedgerActive",         typeof(bool));
        masterTable.Columns.Add("LegalName",              typeof(string));
        masterTable.Columns.Add("MailingAddress",         typeof(string));
        masterTable.Columns.Add("CurrencyCode",           typeof(string));
        masterTable.Columns.Add("DepartmentID",           typeof(int));
        masterTable.Columns.Add("LedgerRefCode",          typeof(string));
        masterTable.Columns.Add("InventoryEffect",        typeof(bool));
        masterTable.Columns.Add("MaintainBillWise",       typeof(bool));
        masterTable.Columns.Add("IsTaxType",              typeof(bool));
        masterTable.Columns.Add("LedgerType",             typeof(string));
        masterTable.Columns.Add("DateOfBirth",            typeof(DateTime));
        masterTable.Columns.Add("Designation",            typeof(string));
        masterTable.Columns.Add("RefClientID",            typeof(int));
        masterTable.Columns.Add("IsClientApproval",       typeof(bool));

        // ── 4a. Query ACTUAL DB column sizes so truncation never occurs ──────────────────────
        var colSizeQuery = @"
            SELECT c.name AS ColName,
                   CASE WHEN c.max_length = -1 THEN 4000
                        WHEN t.name LIKE 'n%'  THEN c.max_length / 2
                        ELSE c.max_length
                   END AS MaxChars
            FROM sys.columns c
            JOIN sys.types   t ON c.user_type_id = t.user_type_id
            WHERE c.object_id = OBJECT_ID('LedgerMaster')
              AND t.name IN ('nvarchar','varchar','char','nchar')";
        var schemaRows = await _connection.QueryAsync<(string ColName, int MaxChars)>(colSizeQuery);
        var colSizes   = schemaRows.ToDictionary(r => r.ColName, r => r.MaxChars, StringComparer.OrdinalIgnoreCase);
        // Helper: get actual DB max for a column, fallback to safeDefault if not found
        int ColMax(string colName, int safeDefault = 250) =>
            colSizes.TryGetValue(colName, out int sz) ? sz : safeDefault;

        // Per-row metadata for the details phase
        var rowMeta = new List<(string LedgerCode, LedgerMasterDto Ledger, int? SalesRepId, int? DeptId, int? ClientId, string SupplyType, bool GstApplicable, string LegalName)>();

        // ── Helper: safely truncate a string to maxLen, returns DBNull if null/empty ────────
        static object T(string? value, int maxLen)
        {
            if (value == null) return DBNull.Value;
            var v = value.Trim();
            return v.Length == 0 ? DBNull.Value : (object)(v.Length > maxLen ? v[..maxLen] : v);
        }
        // Same but always returns a non-null string (for required fields like LedgerName)
        static string TS(string? value, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var v = value.Trim();
            return v.Length > maxLen ? v[..maxLen] : v;
        }
        // Smart email cleaner: extracts actual email address from messy strings
        // e.g. "Harit Mathur/Ho <Harit.Mathur@Bajajallianz.Co.In>" → "Harit.Mathur@Bajajallianz.Co.In"
        // e.g. "a@x.com ][ b@x.com ][ c@x.com"   → "a@x.com"  (takes first only)
        static object CleanEmail(string? value, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(value)) return DBNull.Value;
            var raw = value.Trim();

            // 1. Try to extract from angle-brackets first: <email@domain>
            var angleMatch = System.Text.RegularExpressions.Regex.Match(raw, @"<([^<>\s]+@[^<>\s]+)>");
            if (angleMatch.Success)
            {
                var extracted = angleMatch.Groups[1].Value.Trim();
                return extracted.Length > maxLen ? extracted[..maxLen] : extracted;
            }

            // 2. Extract first valid-looking email token from the string
            var emailMatch = System.Text.RegularExpressions.Regex.Match(raw, @"[\w.+\-]+@[\w.\-]+\.[a-zA-Z]{2,}");
            if (emailMatch.Success)
            {
                var extracted = emailMatch.Value.Trim();
                return extracted.Length > maxLen ? extracted[..maxLen] : extracted;
            }

            // 3. Fallback: just truncate whatever is there
            return raw.Length > maxLen ? raw[..maxLen] : raw;
        }
        // Smart phone cleaner: takes the first number from multi-value cells
        // e.g. "04994-232324-04994-232325-0-9895751799" → split by ][, /, ,, ;, space + take first
        // e.g. "01234-567890 ][ 09876-543210"           → "01234-567890"
        static object CleanPhone(string? value, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(value)) return DBNull.Value;
            var raw = value.Trim();

            // Split on common multi-value separators used in Excel exports
            var separators = new[] { "][", " / ", "/", ", ", ";", " ; ",  " " };
            foreach (var sep in separators)
            {
                if (raw.Contains(sep))
                {
                    var first = raw.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (first.Length > 0)
                        return first.Length > maxLen ? first[..maxLen] : first;
                }
            }

            // No separator found: just truncate
            return raw.Length > maxLen ? raw[..maxLen] : raw;
        }

        for (int i = 0; i < ledgers.Count; i++)
        {
            var ledger = ledgers[i];
            maxLedgerNo++;
            string ledgerCode = $"{prefix}{maxLedgerNo.ToString().PadLeft(5, '0')}";

            int? salesRepId = salesRepMap.TryGetValue(ledger.SalesRepresentative?.Trim() ?? "", out int srId) ? srId : null;
            int? deptId     = deptMap.TryGetValue(ledger.DepartmentName?.Trim() ?? "", out int dId) ? dId : (int?)null;
            int? clientId   = isConsignee && clientMap.TryGetValue(ledger.ClientName?.Trim() ?? "", out int cId) ? cId : null;

            // For employee rows: DepartmentID is mandatory — skip the row if not resolved
            if (isEmployee && !deptId.HasValue)
            {
                string rowLabel = $"Row {i + 1}" + (!string.IsNullOrWhiteSpace(ledger.LedgerName) ? $" ({ledger.LedgerName})" : "");
                result.ErrorMessages.Add($"{rowLabel} – DepartmentName '{ledger.DepartmentName?.Trim()}' not found in DepartmentMaster. Row skipped.");
                result.ErrorRows++;
                continue;
            }

            string supplyType  = !string.IsNullOrWhiteSpace(ledger.SupplyTypeCode) ? ledger.SupplyTypeCode : "B2B";
            bool   gstApp      = ledger.GSTApplicable ?? true;
            string legalName   = !string.IsNullOrWhiteSpace(ledger.MailingName) ? ledger.MailingName : (ledger.LedgerName ?? "");

            // ── Per-row try-catch: build DataTable row, skip on any error ──────────────────
            try
            {
                object DBN = DBNull.Value;
                masterTable.Rows.Add(
                    TS(ledgerCode,                                  ColMax("LedgerCode", 20)),
                    maxLedgerNo,
                    TS(prefix,                                      ColMax("LedgerCodePrefix", 10)),
                    ledgerGroupId,
                    TS(ledger.LedgerName,                           ColMax("LedgerName", 250)),
                    TS(ledger.MailingName ?? ledger.LedgerName,     ColMax("MailingName", 250)),
                    T(ledger.Address1,                              ColMax("Address1", 500)),
                    T(ledger.Address2,                              ColMax("Address2", 500)),
                    T(ledger.Address3,                              ColMax("Address3", 500)),
                    T(ledger.Country,                               ColMax("Country", 100)),
                    T(ledger.State,                                 ColMax("State", 100)),
                    T(ledger.City,                                  ColMax("City", 100)),
                    T(ledger.Pincode,                               ColMax("Pincode", 20)),
                    CleanPhone(ledger.TelephoneNo,                  ColMax("TelephoneNo", 30)),
                    CleanEmail(ledger.Email,                        ColMax("Email", 100)),
                    CleanPhone(ledger.MobileNo,                     ColMax("MobileNo", 30)),
                    T(ledger.Website,                               ColMax("Website", 250)),
                    T(ledger.PANNo,                                 ColMax("PANNo", 10)),
                    T(ledger.GSTNo,                                 ColMax("GSTNo", 15)),
                    salesRepId.HasValue ? (object)salesRepId.Value : DBN,
                    TS(supplyType,                                  ColMax("SupplyTypeCode", 10)),
                    gstApp,
                    ledger.Distance.HasValue              ? (object)ledger.Distance.Value              : DBN,
                    ledger.DeliveredQtyTolerance.HasValue ? (object)ledger.DeliveredQtyTolerance.Value : DBN,
                    false, 2, 2, "2025-2026", DateTime.Now, 2, true,
                    TS(legalName,                                   ColMax("LegalName", 250)),
                    T(ledger.MailingAddress,                        ColMax("MailingAddress", 1000)),
                    T(ledger.CurrencyCode,                          ColMax("CurrencyCode", 10)),
                    deptId ?? 0,
                    T(ledger.RefCode,                               ColMax("LedgerRefCode", 50)),
                    false, false, false,
                    TS(ledgerType,                                  ColMax("LedgerType", 100)),
                    ledger.DateOfBirth.HasValue ? (object)ledger.DateOfBirth.Value : DBN,
                    T(ledger.Designation,                          ColMax("Designation", 100)),
                    clientId.HasValue ? (object)clientId.Value : DBN,
                    isClient   // IsClientApproval = 1 for Clients group
                );
                rowMeta.Add((ledgerCode, ledger, salesRepId, deptId, clientId, supplyType, gstApp, legalName));
            }
            catch (Exception rowEx)
            {
                // This row had unresolvable data — skip it, log clear reason
                maxLedgerNo--; // revert counter so next row gets correct code
                string rowLabel = $"Row {i + 1}" + (!string.IsNullOrWhiteSpace(ledger.LedgerName) ? $" ({ledger.LedgerName})" : "");
                result.ErrorMessages.Add($"{rowLabel} – {rowEx.Message}");
                result.ErrorRows++;
            }
        }



        // ── 5. Phase 1: SqlBulkCopy → LedgerMaster ───────────────────────────────────────
        // Values are already capped to exact DB column sizes via ColMax(), so no truncation errors.
        if (masterTable.Rows.Count > 0)
        {
            using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(_connection);
            bulkCopy.DestinationTableName = "LedgerMaster";
            bulkCopy.BatchSize            = 500;
            bulkCopy.BulkCopyTimeout      = 300;

            var cols = new[] {
                "LedgerCode","MaxLedgerNo","LedgerCodePrefix","LedgerGroupID",
                "LedgerName","MailingName","Address1","Address2","Address3",
                "Country","State","City","Pincode","TelephoneNo","Email","MobileNo","Website",
                "PANNo","GSTNo","RefSalesRepresentativeID","SupplyTypeCode","GSTApplicable",
                "Distance","DeliveredQtyTolerance","IsDeletedTransaction","CompanyID","UserID","FYear",
                "CreatedDate","CreatedBy","ISLedgerActive","LegalName","MailingAddress",
                "CurrencyCode","DepartmentID","LedgerRefCode","InventoryEffect","MaintainBillWise",
                "IsTaxType","LedgerType","DateOfBirth","Designation","RefClientID","IsClientApproval"
            };
            foreach (var col in cols)
                bulkCopy.ColumnMappings.Add(col, col);

            await bulkCopy.WriteToServerAsync(masterTable);
        }

        // ── 5b. For Clients group: set IsClient = 1 and IsClientApproval = 1 via UPDATE ────
        if (isClient && rowMeta.Count > 0)
        {
            var insertedCodes = rowMeta.Select(r => r.LedgerCode).ToList();
            try
            {
                const int updateBatch = 1000;
                for (int b = 0; b < insertedCodes.Count; b += updateBatch)
                {
                    var batch = insertedCodes.Skip(b).Take(updateBatch).ToList();
                    await _connection.ExecuteAsync(
                        "UPDATE LedgerMaster SET IsClient = 1, IsClientApproval = 1 WHERE LedgerCode IN @Codes AND CompanyID = 2",
                        new { Codes = batch });
                }
            }
            catch (Exception ex)
            {
                // IsClient or IsClientApproval column may not exist or have different names — log to debug_log.txt
                try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] IsClient Update Error: {ex.Message}\n"); } catch { }
            }
        }

        // ── 6. Retrieve the generated LedgerIDs by LedgerCode ────────────────────────────
        // SQL Server allows max 2100 parameters per query; batch to 1000 to stay safe.
        var ledgerCodes = rowMeta.Select(r => r.LedgerCode).ToList();
        var codeToId    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        const int idBatchSize = 1000;
        for (int b = 0; b < ledgerCodes.Count; b += idBatchSize)
        {
            var batch = ledgerCodes.Skip(b).Take(idBatchSize).ToList();
            var batchIds = await _connection.QueryAsync<(string LedgerCode, int LedgerID)>(
                "SELECT LedgerCode, LedgerID FROM LedgerMaster WHERE LedgerCode IN @Codes AND CompanyID = 2",
                new { Codes = batch });
            foreach (var (code, id) in batchIds)
                codeToId[code] = id;
        }

        // ── 7. Build DataTable for LedgerMasterDetails ───────────────────────────────────
        var detailsTable = new System.Data.DataTable("LedgerMasterDetails");
        detailsTable.Columns.Add("LedgerID",         typeof(int));
        detailsTable.Columns.Add("LedgerGroupID",    typeof(int));
        detailsTable.Columns.Add("CompanyID",        typeof(int));
        detailsTable.Columns.Add("UserID",           typeof(int));
        detailsTable.Columns.Add("FYear",            typeof(string));
        detailsTable.Columns.Add("FieldName",        typeof(string));
        detailsTable.Columns.Add("FieldValue",       typeof(string));
        detailsTable.Columns.Add("ParentFieldName",  typeof(string));
        detailsTable.Columns.Add("ParentFieldValue", typeof(string));
        detailsTable.Columns.Add("CreatedDate",      typeof(DateTime));
        detailsTable.Columns.Add("CreatedBy",        typeof(int));
        detailsTable.Columns.Add("ModifiedDate",     typeof(DateTime));
        detailsTable.Columns.Add("ModifiedBy",       typeof(int));
        detailsTable.Columns.Add("SequenceNo",       typeof(int));
        detailsTable.Columns.Add("FieldID",          typeof(int));

        int insertedCount = 0;
        foreach (var (ledgerCode, ledger, salesRepId, deptId, clientId, supplyType, gstApp, legalName) in rowMeta)
        {
            if (!codeToId.TryGetValue(ledgerCode, out int newLedgerId))
            {
                // ID wasn't returned — row was filtered or duplicate LedgerCode
                string rowLabel = !string.IsNullOrWhiteSpace(ledger.LedgerName) ? $"({ledger.LedgerName})" : ledgerCode;
                result.ErrorMessages.Add($"{rowLabel} – Insert succeeded but ID could not be retrieved (possible duplicate LedgerCode)");
                result.ErrorRows++;
                continue;
            }

            insertedCount++;

            var details = isEmployee
                ? new List<(string Name, string? Value, int Seq)>
                {
                    ("LedgerName",    ledger.LedgerName,  1),
                    ("MailingName",   ledger.MailingName, 2),
                    ("Address1",      ledger.Address1,    3),
                    ("Address2",      ledger.Address2,    4),
                    ("Address3",      ledger.Address3,    5),
                    ("Country",       ledger.Country,     6),
                    ("State",         ledger.State,       7),
                    ("City",          ledger.City,        8),
                    ("Pincode",       ledger.Pincode,     9),
                    ("MailingAddress",ledger.MailingAddress, 10),
                    ("DateOfBirth",   ledger.DateOfBirth?.ToString("yyyy-MM-dd"), 11),
                    ("TelephoneNo",   ledger.TelephoneNo, 12),
                    ("MobileNo",      ledger.MobileNo,    13),
                    ("Email",         ledger.Email,       14),
                    ("PANNo",         ledger.PANNo,       15),
                    ("DepartmentID",  deptId?.ToString(), 16),
                    ("Designation",   ledger.Designation, 17),
                    ("ISLedgerActive","True",             0)
                }
                : new List<(string Name, string? Value, int Seq)>
                {
                    ("LedgerName",            ledger.LedgerName,                   1),
                    ("MailingName",           ledger.MailingName,                  2),
                    ("Address1",              ledger.Address1,                     3),
                    ("Address2",              ledger.Address2,                     4),
                    ("Address3",              ledger.Address3,                     5),
                    ("Country",               ledger.Country,                      6),
                    ("State",                 ledger.State,                        7),
                    ("City",                  ledger.City,                         8),
                    ("Pincode",               ledger.Pincode,                      9),
                    ("MailingAddress",        ledger.MailingAddress,               10),
                    ("TelephoneNo",           ledger.TelephoneNo,                  11),
                    ("MobileNo",              ledger.MobileNo,                     12),
                    ("Email",                 ledger.Email,                        13),
                    ("Website",               ledger.Website,                      14),
                    ("PANNo",                 ledger.PANNo,                        15),
                    ("GSTNo",                 ledger.GSTNo,                        16),
                    ("CurrencyCode",          ledger.CurrencyCode,                 17),
                    ("GSTApplicable",         gstApp.ToString(),                   18),
                    ("LegalName",             legalName,                           19),
                    ("SupplyTypeCode",        supplyType,                          20),
                    ("RefCode",               ledger.RefCode,                      21),
                    ("DeliveredQtyTolerance", ledger.DeliveredQtyTolerance?.ToString(), 22),
                    ("RefSalesRepresentativeID", salesRepId?.ToString(),          25),
                    ("GSTRegistrationType",   ledger.GSTRegistrationType,          24),
                    ("CreditDays",            ledger.CreditDays,                   26),
                    ("ISLedgerActive",        "True",                              0)
                };

            if (isConsignee && clientId.HasValue)
                details.Add(("RefClientID", clientId.Value.ToString(), 23));

            var now = DateTime.Now;
            foreach (var (name, value, seq) in details)
            {
                if (value == null) continue;
                detailsTable.Rows.Add(
                    newLedgerId, ledgerGroupId, 2, 2, "2025-2026",
                    name, value, name, value,
                    now, 2, now, 2, seq, 0);
            }
        }

        // ── 8. Phase 2: SqlBulkCopy → LedgerMasterDetails ────────────────────────────────
        if (detailsTable.Rows.Count > 0)
        {
            using var detailsBulk = new Microsoft.Data.SqlClient.SqlBulkCopy(_connection);
            detailsBulk.DestinationTableName = "LedgerMasterDetails";
            detailsBulk.BatchSize            = 1000;
            detailsBulk.BulkCopyTimeout      = 300;

            var detailCols = new[] {
                "LedgerID","LedgerGroupID","CompanyID","UserID","FYear",
                "FieldName","FieldValue","ParentFieldName","ParentFieldValue",
                "CreatedDate","CreatedBy","ModifiedDate","ModifiedBy","SequenceNo","FieldID"
            };
            foreach (var col in detailCols)
                detailsBulk.ColumnMappings.Add(col, col);

            await detailsBulk.WriteToServerAsync(detailsTable);
        }

        // ── 9. Build final result ─────────────────────────────────────────────────────────
        result.ImportedRows = insertedCount;
        result.Success      = insertedCount > 0;
        result.Message      = result.ErrorRows == 0
            ? $"Successfully imported {insertedCount} of {ledgers.Count} ledger(s)"
            : $"Import completed. Inserted: {insertedCount}, Failed: {result.ErrorRows}, Total: {ledgers.Count}";

        return result;
    }




    public async Task<List<CountryStateDto>> GetCountryStatesAsync()
    {
        var query = @"
            SELECT DISTINCT Country, State
            FROM CountryStateMaster
            WHERE Country IS NOT NULL AND State IS NOT NULL
            ORDER BY Country, State";

        var results = await _connection.QueryAsync<CountryStateDto>(query);
        return results.ToList();
    }

    public async Task<int> ClearAllLedgerDataAsync(int ledgerGroupId, string username, string password, string reason)
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
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllLedgerData Failed: Invalid credentials for user '{username}'. Reason: {reason}\n"); } catch {}
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        // 2. Perform Transactional Delete
        int deletedCount = 0;
        var transaction = await _connection.BeginTransactionAsync();
        try
        {
            // Delete from Details first (FK constraint usually requires this)
            var deleteDetailsQuery = "DELETE FROM LedgerMasterDetails WHERE LedgerGroupID = @LedgerGroupId";
            await _connection.ExecuteAsync(deleteDetailsQuery, new { LedgerGroupId = ledgerGroupId }, transaction: transaction);

            // Delete from Master
            var deleteMasterQuery = "DELETE FROM LedgerMaster WHERE LedgerGroupID = @LedgerGroupId";
            deletedCount = await _connection.ExecuteAsync(deleteMasterQuery, new { LedgerGroupId = ledgerGroupId }, transaction: transaction);

            await transaction.CommitAsync();

            // 3. Log Audit
            try 
            { 
                var logMessage = $"[{DateTime.Now}] AUDIT: User '{username}' cleared {deletedCount} records for LedgerGroupID {ledgerGroupId}. Reason: {reason}\n";
                await System.IO.File.AppendAllTextAsync("debug_log.txt", logMessage); 
            } 
            catch {}

            return deletedCount;
        }

        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllLedgerData Exception: {ex.Message}\n"); } catch {}
            throw;
        }
    }

    public async Task<List<ClientDto>> GetClientsAsync()
    {
        try
        {
            var query = @"
                SELECT LedgerID, LedgerName 
                FROM (
                    SELECT LedgerID, FieldName, NULLIF(FieldValue, '') as FieldValue 
                    FROM LedgerMasterDetails 
                    WHERE CompanyID = 2 
                    AND LedgerGroupID IN (
                        SELECT DISTINCT LedgerGroupID 
                        FROM LedgerGroupMaster 
                        WHERE LedgerGroupNameID = 24 AND CompanyID = 2
                    ) 
                    AND ISNULL(IsDeletedTransaction, 0) <> 1 
                ) x 
                PIVOT (
                    MAX(FieldValue) FOR FieldName IN ([LedgerName])
                ) p
                ORDER BY LedgerName";

            var results = await _connection.QueryAsync<ClientDto>(query);
            return results.ToList();
        }
        catch (Exception ex)
        {
             try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] GetClientsAsync Error: {ex.Message}\nStack: {ex.StackTrace}\n"); } catch { }
             throw;
        }
    }

    public async Task<List<SalesRepresentativeDto>> GetSalesRepresentativesAsync()
    {
        var query = @"
            SELECT LM.[LedgerID] AS EmployeeID, LM.[LedgerName] AS EmployeeName 
            FROM LedgerMaster AS LM 
            INNER JOIN LedgerGroupMaster AS LG ON LG.LedgerGroupID=LM.LedgerGroupID AND LG.CompanyID=LM.CompanyID 
            Where LG.LedgerGroupNameID=27 AND LM.DepartmentID=-50  
            And ISNULL(LM.IsDeletedTransaction, 0) <> 1 AND LM.CompanyID=2 
            Order By LM.[LedgerName]";

        var results = await _connection.QueryAsync<SalesRepresentativeDto>(query);
        return results.ToList();
    }

    public async Task<List<DepartmentDto>> GetDepartmentsAsync()
    {
        try 
        {
            var query = @"
                SELECT DepartmentID, DepartmentName 
                FROM DepartmentMaster 
                ORDER BY DepartmentName";

            var results = await _connection.QueryAsync<DepartmentDto>(query);
            return results.ToList();
        }
        catch (Exception ex)
        {
             try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] GetDepartmentsAsync Error: {ex.Message}\nStack: {ex.StackTrace}\n"); } catch { }
             throw;
        }
    }

    private object? GetPropertyValue(object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName)?.GetValue(obj);
    }
}


