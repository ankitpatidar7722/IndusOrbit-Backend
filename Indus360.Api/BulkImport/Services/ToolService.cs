using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class ToolService : IToolService
{
    private readonly SqlConnection _connection;

    public ToolService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<ToolMasterDto>> GetAllToolsAsync(int toolGroupId)
    {
        var query = @"
            SELECT
                t.ToolID,
                t.ToolName,
                t.ToolCode,
                t.ToolGroupID,
                tg.ToolGroupName,
                t.ToolType,
                t.JobName,
                t.LedgerName as ClientName,
                t.ToolRefCode,
                t.ToolLocation as Location,
                t.ProductHSNID,
                hsn.DisplayName as ProductHSNName,
                hsn.HSNCode,
                t.SizeL,
                t.SizeW,
                t.SizeH,
                t.UpsL as UpsAround,
                t.UpsW as UpsAcross,
                t.TotalUps,
                t.PurchaseUnit,
                t.PurchaseRate,
                t.StockUnit,
                CAST(ISNULL(t.IsDeletedTransaction, 0) AS BIT) as IsDeletedTransaction
            FROM ToolMaster t
            LEFT JOIN ToolGroupMaster tg ON t.ToolGroupID = tg.ToolGroupID
            LEFT JOIN ProductHSNMaster hsn ON t.ProductHSNID = hsn.ProductHSNID
            WHERE t.ToolGroupID = @ToolGroupID
              AND (t.IsDeletedTransaction IS NULL OR t.IsDeletedTransaction = 0)
            ORDER BY t.ToolName";

        var tools = await _connection.QueryAsync<ToolMasterDto>(query, new { ToolGroupID = toolGroupId });

        // Deduplicate
        var toolsList = tools.GroupBy(t => t.ToolID).Select(g => g.First()).ToList();

        // Load ALL detail fields in a single batch query
        var toolIds = toolsList.Where(t => t.ToolID.HasValue).Select(t => t.ToolID!.Value).ToList();

        if (toolIds.Count > 0)
        {
            var allDetails = new List<dynamic>();
            const int batchSize = 2000;
            for (int i = 0; i < toolIds.Count; i += batchSize)
            {
                var batch = toolIds.Skip(i).Take(batchSize).ToList();
                var batchDetails = await _connection.QueryAsync<dynamic>(@"
                    SELECT ToolID, FieldName, FieldValue
                    FROM ToolMasterDetails
                    WHERE ToolID IN @ToolIDs AND ISNULL(IsDeletedTransaction, 0) = 0", 
                    new { ToolIDs = batch });
                allDetails.AddRange(batchDetails);
            }

            var detailsByToolId = allDetails
                .GroupBy(d => (int)d.ToolID)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var tool in toolsList)
            {
                if (!tool.ToolID.HasValue || !detailsByToolId.TryGetValue(tool.ToolID.Value, out var details))
                    continue;

                foreach (var detail in details)
                {
                    string fieldName = detail.FieldName;
                    string fieldValue = detail.FieldValue;

                    switch (fieldName)
                    {
                        case "ToolType":
                            tool.ToolType = fieldValue;
                            break;
                        case "JobName":
                            tool.JobName = fieldValue;
                            break;
                        case "ClientName":
                        case "LedgerName":
                            tool.ClientName = fieldValue;
                            break;
                        case "ToolRefCode":
                            tool.ToolRefCode = fieldValue;
                            break;
                        case "Manufacturer":
                            tool.Manufacturer = fieldValue;
                            break;
                        case "NoOfTeeth":
                            if (int.TryParse(fieldValue, out int noOfTeeth)) tool.NoOfTeeth = noOfTeeth;
                            break;
                        case "CircumferenceMM":
                            if (decimal.TryParse(fieldValue, out decimal circumMM)) tool.CircumferenceMM = circumMM;
                            break;
                        case "CircumferenceInch":
                            if (decimal.TryParse(fieldValue, out decimal circumInch)) tool.CircumferenceInch = circumInch;
                            break;
                        case "BCM":
                            if (decimal.TryParse(fieldValue, out decimal bcm)) tool.BCM = bcm;
                            break;
                        case "LPI":
                            if (decimal.TryParse(fieldValue, out decimal lpi)) tool.LPI = lpi;
                            break;
                        case "AroundGap":
                            if (decimal.TryParse(fieldValue, out decimal aroundGap)) tool.AroundGap = aroundGap;
                            break;
                        case "AcrossGap":
                            if (decimal.TryParse(fieldValue, out decimal acrossGap)) tool.AcrossGap = acrossGap;
                            break;
                        case "UnitSymbol":
                            tool.UnitSymbol = fieldValue;
                            break;
                        case "ReferenceToolNo":
                            tool.ReferenceToolNo = fieldValue;
                            break;
                        case "EstimateRate":
                            if (decimal.TryParse(fieldValue, out decimal estimateRate)) tool.EstimateRate = estimateRate;
                            break;
                        case "SizeL":
                            if (decimal.TryParse(fieldValue, out decimal sizeL)) tool.SizeL = sizeL;
                            break;
                        case "SizeW":
                            if (decimal.TryParse(fieldValue, out decimal sizeW)) tool.SizeW = sizeW;
                            break;
                        case "SizeH":
                            if (decimal.TryParse(fieldValue, out decimal sizeH)) tool.SizeH = sizeH;
                            break;
                        case "UpsAround":
                            if (int.TryParse(fieldValue, out int upsAround)) tool.UpsAround = upsAround;
                            break;
                        case "UpsAcross":
                            if (int.TryParse(fieldValue, out int upsAcross)) tool.UpsAcross = upsAcross;
                            break;
                        case "TotalUps":
                            if (int.TryParse(fieldValue, out int totalUps)) tool.TotalUps = totalUps;
                            break;
                        case "PurchaseUnit":
                            tool.PurchaseUnit = fieldValue;
                            break;
                        case "PurchaseRate":
                            if (decimal.TryParse(fieldValue, out decimal purchaseRate)) tool.PurchaseRate = purchaseRate;
                            break;
                        case "StockUnit":
                            tool.StockUnit = fieldValue;
                            break;
                        case "ManufecturerItemCode":
                            tool.ManufecturerItemCode = fieldValue;
                            break;
                        // SIM / SHIM-specific detail fields (ToolGroupID 13)
                        case "Positive":
                            tool.Positive = fieldValue;
                            break;
                        case "Negative":
                            tool.Negative = fieldValue;
                            break;
                        case "Master":
                            tool.Master = fieldValue;
                            break;
                        case "Sim":
                            tool.Sim = fieldValue;
                            break;
                        case "Location":
                            tool.Location = fieldValue;
                            break;
                        case "PurchaseOrderQuantity":
                            if (decimal.TryParse(fieldValue, out decimal poQty)) tool.PurchaseOrderQuantity = poQty;
                            break;
                        case "ShelfLife":
                            if (int.TryParse(fieldValue, out int shelfLife)) tool.ShelfLife = shelfLife;
                            break;
                        case "MinimumStockQty":
                            if (decimal.TryParse(fieldValue, out decimal minStock)) tool.MinimumStockQty = minStock;
                            break;
                        case "IsStandardItem":
                            if (bool.TryParse(fieldValue, out bool isStandard)) tool.IsStandardItem = isStandard;
                            break;
                        case "IsRegularItem":
                            if (bool.TryParse(fieldValue, out bool isRegular)) tool.IsRegularItem = isRegular;
                            break;
                        case "ProductHSNName":
                            // ProductHSNName in details stores the ID, actual name already mapped from JOIN
                            break;
                    }
                }
            }
        }

        return toolsList;
    }

    public async Task<bool> SoftDeleteToolAsync(int toolId)
    {
        var query = @"
            UPDATE ToolMaster
            SET IsDeletedTransaction = 1
            WHERE ToolID = @ToolId;

            UPDATE ToolMasterDetails
            SET IsDeletedTransaction = 1
            WHERE ToolID = @ToolId;";

        var rowsAffected = await _connection.ExecuteAsync(query, new { ToolId = toolId });
        return rowsAffected > 0;
    }

    // Helper method to generate duplicate key based on Tool Group
    private string GenerateDuplicateKey(ToolMasterDto tool, int toolGroupId)
    {
        return toolGroupId switch
        {
            3 => // DIE: JobName + SizeL + SizeW + SizeH + ToolRefCode
                $"{(tool.JobName?.Trim() ?? "").ToLowerInvariant()}|{tool.SizeL?.ToString() ?? ""}|{tool.SizeW?.ToString() ?? ""}|{tool.SizeH?.ToString() ?? ""}|{(tool.ToolRefCode?.Trim() ?? "").ToLowerInvariant()}",

            5 => // PRINTING CYLINDER: ToolName + SizeW + Manufacturer + NoOfTeeth
                $"{(tool.ToolName?.Trim() ?? "").ToLowerInvariant()}|{tool.SizeW?.ToString() ?? ""}|{(tool.Manufacturer?.Trim() ?? "").ToLowerInvariant()}|{tool.NoOfTeeth?.ToString() ?? ""}",

            6 => // ANILOX CYLINDER: SizeW + Manufacturer + BCM + LPI + ToolName
                $"{tool.SizeW?.ToString() ?? ""}|{(tool.Manufacturer?.Trim() ?? "").ToLowerInvariant()}|{tool.BCM?.ToString() ?? ""}|{tool.LPI?.ToString() ?? ""}|{(tool.ToolName?.Trim() ?? "").ToLowerInvariant()}",

            7 => // EMBOSSING CYLINDER: ToolName + SizeW + Manufacturer + NoOfTeeth
                $"{(tool.ToolName?.Trim() ?? "").ToLowerInvariant()}|{tool.SizeW?.ToString() ?? ""}|{(tool.Manufacturer?.Trim() ?? "").ToLowerInvariant()}|{tool.NoOfTeeth?.ToString() ?? ""}",

            8 => // FLEXO DIE: ToolName + SizeL + SizeH + TotalUps
                $"{(tool.ToolName?.Trim() ?? "").ToLowerInvariant()}|{tool.SizeL?.ToString() ?? ""}|{tool.SizeH?.ToString() ?? ""}|{tool.TotalUps?.ToString() ?? ""}",

            13 => // SIM: JobName + SizeL + Positive + Negative + SizeW + UpsAround + UpsAcross + TotalUps + Master + Sim + ReferenceToolNo + Location
                string.Join("|", new[] {
                    (tool.JobName?.Trim() ?? "").ToLowerInvariant(),
                    tool.SizeL?.ToString() ?? "",
                    (tool.Positive?.Trim() ?? "").ToLowerInvariant(),
                    (tool.Negative?.Trim() ?? "").ToLowerInvariant(),
                    tool.SizeW?.ToString() ?? "",
                    tool.UpsAround?.ToString() ?? "",
                    tool.UpsAcross?.ToString() ?? "",
                    tool.TotalUps?.ToString() ?? "",
                    (tool.Master?.Trim() ?? "").ToLowerInvariant(),
                    (tool.Sim?.Trim() ?? "").ToLowerInvariant(),
                    (tool.ReferenceToolNo?.Trim() ?? "").ToLowerInvariant(),
                    (tool.Location?.Trim() ?? "").ToLowerInvariant()
                }),

            _ => // PLATES and default: ToolName + SizeL + SizeW + TotalUps
                $"{(tool.ToolName?.Trim() ?? "").ToLowerInvariant()}|{tool.SizeL?.ToString() ?? ""}|{tool.SizeW?.ToString() ?? ""}|{tool.TotalUps?.ToString() ?? ""}"
        };
    }

    public async Task<ToolValidationResultDto> ValidateToolsAsync(List<ToolMasterDto> tools, int toolGroupId)
    {
        var result = new ToolValidationResultDto
        {
            Summary = new ValidationSummary { TotalRows = tools.Count }
        };

        // Get existing tools from database for duplicate check — build HashSet for O(1) lookup
        var existingTools = await GetAllToolsAsync(toolGroupId);
        var existingToolKeys = new HashSet<string>(
            existingTools.Select(e => GenerateDuplicateKey(e, toolGroupId))
        );
        var batchToolKeys = new HashSet<string>();

        // Get valid HSN Groups (Tool category)
        var validHSNGroups = await GetToolHSNGroupsAsync();
        var hsnGroupLookup = new HashSet<string>(
            validHSNGroups.Select(h => h.DisplayName.Trim())
        );

        // Get valid Units
        var validUnits = await GetToolUnitsAsync();
        var unitLookup = new HashSet<string>(
            validUnits.Select(u => u.UnitSymbol.Trim())
        );

        // For DIE (ToolGroupId == 3), get valid LedgerMaster entries for ClientName validation
        Dictionary<string, int> clientLedgerMap = new();
        if (toolGroupId == 3)
        {
            var clientsQuery = @"
                SELECT LedgerId, LedgerName
                FROM LedgerMaster
                WHERE LedgerGroupId = 1 AND ISNULL(IsDeletedTransaction, 0) = 0";
            var clients = await _connection.QueryAsync<(int LedgerId, string LedgerName)>(clientsQuery);
            foreach (var c in clients)
            {
                if (!string.IsNullOrWhiteSpace(c.LedgerName))
                    clientLedgerMap[c.LedgerName.Trim()] = c.LedgerId;
            }
        }

        // Required fields based on ToolGroupId
        string[] requiredFields;

        if (toolGroupId == 3) // DIE
        {
            requiredFields = new[] {
                "ToolName", "SizeL", "SizeW", "SizeH",
                "UpsAround", "UpsAcross", "TotalUps", "ProductHSNName",
                "PurchaseUnit", "PurchaseRate", "StockUnit"
            };
        }
        else if (toolGroupId == 5) // PRINTING CYLINDER
        {
            requiredFields = new[] {
                "ToolName", "SizeW", "Manufacturer", "NoOfTeeth",
                "CircumferenceMM", "CircumferenceInch", "ProductHSNName",
                "PurchaseUnit", "PurchaseRate", "StockUnit"
            };
        }
        else if (toolGroupId == 6) // ANILOX CYLINDER
        {
            requiredFields = new[] {
                "ToolName", "SizeW", "Manufacturer", "BCM", "LPI",
                "ProductHSNName", "PurchaseUnit", "PurchaseRate", "StockUnit"
            };
        }
        else if (toolGroupId == 7) // EMBOSSING CYLINDER
        {
            requiredFields = new[] {
                "ToolName", "SizeW", "Manufacturer", "NoOfTeeth",
                "CircumferenceMM", "CircumferenceInch", "ProductHSNName",
                "PurchaseUnit", "PurchaseRate", "StockUnit"
            };
        }
        else if (toolGroupId == 8) // FLEXO DIE
        {
            requiredFields = new[] {
                "ToolName", "ToolType", "SizeL", "SizeH",
                "UpsAround", "UpsAcross", "TotalUps", "AroundGap", "AcrossGap",
                "ProductHSNName", "PurchaseUnit", "PurchaseRate", "StockUnit"
            };
        }
        else if (toolGroupId == 13) // SIM — no mandatory fields (per requirement)
        {
            requiredFields = Array.Empty<string>();
        }
        else // PLATES (ToolGroupId == 1) and default for all other tool groups
        {
            requiredFields = new[] {
                "ToolName", "ToolType", "SizeL", "SizeW", "PurchaseUnit", "PurchaseRate",
                "StockUnit", "ProductHSNName", "TotalUps"
            };
        }

        // Cache reflection data outside the loop for performance
        var stringProperties = typeof(ToolMasterDto)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string) &&
                       p.Name != "ToolName" && p.Name != "ToolCode")
            .ToArray();

        var dtoPropertyCache = typeof(ToolMasterDto).GetProperties()
            .ToDictionary(p => p.Name, p => p);

        for (int i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            var rowValidation = new ToolRowValidation
            {
                RowIndex = i,
                Data = tool,
                RowStatus = ValidationStatus.Valid
            };

            bool hasMissingData = false;
            bool hasMismatch = false;
            bool hasInvalidContent = false;

            // 1. Check for missing data (BLUE)
            // Note: If a field has an entry in RawValues, it means the frontend
            // couldn't parse it as the expected type (e.g., "10A" for a decimal field).
            // That field is NOT missing — it has invalid content, handled in step 6.
            foreach (var field in requiredFields)
            {
                // Skip missing check if RawValues has this field (it's invalid, not missing)
                if (tool.RawValues != null && tool.RawValues.ContainsKey(field))
                    continue;

                var value = GetPropertyValue(tool, field);
                bool isMissing = false;

                // Handle numeric fields - 0 is valid, only null/empty is missing
                var numericFields = new[] { "SizeL", "SizeW", "SizeH", "TotalUps", "UpsAround", "UpsAcross",
                    "PurchaseRate", "EstimateRate", "AroundGap", "AcrossGap",
                    "NoOfTeeth", "CircumferenceMM", "CircumferenceInch", "BCM", "LPI",
                    "PurchaseOrderQuantity", "MinimumStockQty", "ShelfLife" };

                if (numericFields.Contains(field))
                {
                    if (field == "PurchaseRate")
                    {
                        // PurchaseRate must be > 0 (0 is not allowed, auto-converted to 1 by frontend)
                        isMissing = value == null || string.IsNullOrWhiteSpace(value.ToString()) ||
                                    Convert.ToDecimal(value) <= 0;
                    }
                    else
                    {
                        // Other numeric fields: 0 is a VALID value, only null/empty is missing
                        isMissing = value == null || string.IsNullOrWhiteSpace(value.ToString());
                    }
                }
                else
                {
                    // String fields: only null/empty is missing
                    isMissing = string.IsNullOrWhiteSpace(value?.ToString());
                }

                if (isMissing)
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

            // 2. Check for duplicates (RED) - Using tool group specific combination (O(1) HashSet lookup)
            var toolKey = GenerateDuplicateKey(tool, toolGroupId);
            var isDuplicate = existingToolKeys.Contains(toolKey);
            var duplicateInBatch = !batchToolKeys.Add(toolKey); // Add returns false if already exists

            if (isDuplicate || duplicateInBatch)
            {
                rowValidation.RowStatus = ValidationStatus.Duplicate;
                rowValidation.ErrorMessage = "Duplicate record found";
            }

            // 3. Check ProductHSNName mismatch (YELLOW)
            if (!string.IsNullOrWhiteSpace(tool.ProductHSNName))
            {
                if (!hsnGroupLookup.Contains(tool.ProductHSNName.Trim()))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "ProductHSNName",
                        ValidationMessage = "Invalid",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 3b. Check ClientName mismatch (YELLOW) - only for DIE (ToolGroupId == 3)
            if (toolGroupId == 3 && !string.IsNullOrWhiteSpace(tool.ClientName))
            {
                if (!clientLedgerMap.ContainsKey(tool.ClientName.Trim()))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "ClientName",
                        ValidationMessage = "Invalid",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 4. Check Unit mismatches (YELLOW)
            var unitFields = new[] { "PurchaseUnit", "StockUnit" };
            foreach (var unitField in unitFields)
            {
                var unitValue = GetPropertyValue(tool, unitField)?.ToString();
                if (!string.IsNullOrWhiteSpace(unitValue))
                {
                    if (!unitLookup.Contains(unitValue.Trim()))
                    {
                        rowValidation.CellValidations.Add(new CellValidation
                        {
                            ColumnName = unitField,
                            ValidationMessage = $"{unitField} does not match UnitMaster UnitSymbol",
                            Status = ValidationStatus.Mismatch
                        });

                        if (rowValidation.RowStatus == ValidationStatus.Valid)
                            rowValidation.RowStatus = ValidationStatus.Mismatch;

                        hasMismatch = true;
                    }
                }
            }

            // 5. Check for Special Characters (InvalidContent)
            foreach (var prop in stringProperties)
            {
                var val = prop.GetValue(tool) as string;
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

            // 6. Validate RawValues
            if (tool.RawValues != null && tool.RawValues.Count > 0)
            {
                foreach (var rawEntry in tool.RawValues)
                {
                    var rawFieldName = rawEntry.Key;
                    var rawValue = rawEntry.Value;

                    // Infer type from the DTO property type (using cached property lookup)
                    string expectedType = "unknown";
                    if (dtoPropertyCache.TryGetValue(rawFieldName, out var prop))
                    {
                        var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                        if (underlyingType == typeof(decimal) || underlyingType == typeof(double) || underlyingType == typeof(float))
                            expectedType = "decimal";
                        else if (underlyingType == typeof(int) || underlyingType == typeof(long))
                            expectedType = "integer";
                        else if (underlyingType == typeof(bool))
                            expectedType = "boolean";
                    }

                    string validationMsg = expectedType switch
                    {
                        "integer" or "int" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid Integer.",
                        "real" or "decimal" or "float" or "numeric" or "money" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid Number.",
                        "bit" or "boolean" => $"Invalid Content in {rawFieldName} — \"{rawValue}\" is not a valid True/False value.",
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

    public async Task<ImportResultDto> ImportToolsAsync(List<ToolMasterDto> tools, int toolGroupId)
    {
        var result = new ImportResultDto();
        result.TotalRows = tools.Count;

        if (_connection.State != System.Data.ConnectionState.Open) await _connection.OpenAsync();

        // ─── 1. Fetch lookup data ───────────────────────────────────────────────
        var hsnGroups = await GetToolHSNGroupsAsync();
        var hsnGroupMapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var hsn in hsnGroups)
        {
            if (!string.IsNullOrWhiteSpace(hsn.DisplayName) && !hsnGroupMapping.ContainsKey(hsn.DisplayName))
                hsnGroupMapping[hsn.DisplayName.Trim()] = hsn.ProductHSNID;
        }

        var toolGroupQuery = @"
            SELECT ToolGroupID, ToolGroupName, ToolGroupPrefix
            FROM ToolGroupMaster
            WHERE ToolGroupID = @ToolGroupID";

        var toolGroup = await _connection.QueryFirstOrDefaultAsync<dynamic>(toolGroupQuery, new { ToolGroupID = toolGroupId });

        if (toolGroup == null)
        {
            result.Success = false;
            result.Message = $"ToolGroup with ID {toolGroupId} not found.";
            return result;
        }

        string toolGroupName = toolGroup.ToolGroupName;
        string prefix = toolGroup.ToolGroupPrefix ?? "TL";

        var maxToolNo = await _connection.ExecuteScalarAsync<int?>(
            "SELECT MAX(MaxToolNo) FROM ToolMaster WHERE ToolGroupID = @ToolGroupID AND ISNULL(IsDeletedTransaction, 0) = 0",
            new { ToolGroupID = toolGroupId }
        ) ?? 0;

        // ─── 2. Pre-process all rows ────────────────────────────────────────────
        var masterRows = new List<(ToolMasterDto Tool, string ToolCode, int MaxToolNo, string ToolName, string ToolType, int? TotalUps, int ProductHSNID, int RowIndex)>();

        for (int i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            maxToolNo++;
            string toolCode = $"{prefix}{maxToolNo.ToString().PadLeft(5, '0')}";

            string toolType = tool.ToolType ?? toolGroupName;

            int? totalUps = tool.TotalUps;
            if (!totalUps.HasValue && tool.UpsAround.HasValue && tool.UpsAcross.HasValue)
                totalUps = tool.UpsAround.Value * tool.UpsAcross.Value;

            var nameParts = new List<string>();
            if (!string.IsNullOrEmpty(toolType)) nameParts.Add(toolType);
            if (tool.SizeL.HasValue) nameParts.Add($"{tool.SizeL}");
            if (tool.SizeW.HasValue) nameParts.Add($"x{tool.SizeW}");
            if (tool.SizeH.HasValue) nameParts.Add($"x{tool.SizeH}");
            string toolName = tool.ToolName ?? string.Join(" ", nameParts);

            int productHSNID = 0;
            if (!string.IsNullOrWhiteSpace(tool.ProductHSNName) &&
                hsnGroupMapping.TryGetValue(tool.ProductHSNName.Trim(), out int hsnId))
            {
                productHSNID = hsnId;
            }

            tool.JobName = tool.JobName?.Trim();
            tool.ClientName = tool.ClientName?.Trim();
            tool.ToolRefCode = tool.ToolRefCode?.Trim();
            tool.ToolName = tool.ToolName?.Trim();
            tool.ToolType = tool.ToolType?.Trim();
            tool.Manufacturer = tool.Manufacturer?.Trim();
            tool.ProductHSNName = tool.ProductHSNName?.Trim();
            tool.PurchaseUnit = tool.PurchaseUnit?.Trim();
            tool.StockUnit = tool.StockUnit?.Trim();
            tool.ManufecturerItemCode = tool.ManufecturerItemCode?.Trim();

            masterRows.Add((tool, toolCode, maxToolNo, toolName, toolType, totalUps, productHSNID, i));
        }

        // ─── 3. Build ToolMaster DataTable & SqlBulkCopy ────────────────────────
        var masterTable = new System.Data.DataTable();
        masterTable.Columns.Add("ToolName",            typeof(string));
        masterTable.Columns.Add("ToolDescription",     typeof(string));
        masterTable.Columns.Add("ToolCode",            typeof(string));
        masterTable.Columns.Add("MaxToolNo",           typeof(int));
        masterTable.Columns.Add("Prefix",              typeof(string));
        masterTable.Columns.Add("ToolGroupID",         typeof(int));
        masterTable.Columns.Add("ToolSubGroupID",      typeof(int));
        masterTable.Columns.Add("ToolType",            typeof(string));
        masterTable.Columns.Add("JobName",             typeof(string));
        masterTable.Columns.Add("LedgerName",          typeof(string));
        masterTable.Columns.Add("ToolRefCode",         typeof(string));
        masterTable.Columns.Add("ProductHSNID",        typeof(object));
        masterTable.Columns.Add("IsToolActive",        typeof(int));
        masterTable.Columns.Add("SizeL",               typeof(object));
        masterTable.Columns.Add("SizeW",               typeof(object));
        masterTable.Columns.Add("SizeH",               typeof(object));
        masterTable.Columns.Add("UpsL",                typeof(object));
        masterTable.Columns.Add("UpsW",                typeof(object));
        masterTable.Columns.Add("TotalUps",            typeof(object));
        masterTable.Columns.Add("Manufecturer",        typeof(string));
        masterTable.Columns.Add("NoOfTeeth",           typeof(object));
        masterTable.Columns.Add("CircumferenceMM",     typeof(object));
        masterTable.Columns.Add("CircumferenceInch",   typeof(object));
        masterTable.Columns.Add("BCM",                 typeof(object));
        masterTable.Columns.Add("LPI",                 typeof(object));
        masterTable.Columns.Add("PurchaseUnit",        typeof(string));
        masterTable.Columns.Add("PurchaseRate",        typeof(object));
        masterTable.Columns.Add("EstimationUnit",      typeof(string));
        masterTable.Columns.Add("EstimationRate",      typeof(object));
        masterTable.Columns.Add("StockUnit",           typeof(string));
        masterTable.Columns.Add("CompanyID",           typeof(int));
        masterTable.Columns.Add("UserID",              typeof(int));
        masterTable.Columns.Add("CreatedBy",           typeof(int));
        masterTable.Columns.Add("CreatedDate",         typeof(DateTime));
        masterTable.Columns.Add("IsDeletedTransaction", typeof(int));
        masterTable.Columns.Add("ToolLocation",         typeof(string));

        object N(object? v) => v ?? DBNull.Value;

        foreach (var r in masterRows)
        {
            var tool = r.Tool;
            masterTable.Rows.Add(
                N(r.ToolName), N(r.ToolName), r.ToolCode, r.MaxToolNo, prefix,
                toolGroupId, 0, N(r.ToolType),
                N(tool.JobName), N(tool.ClientName), N(tool.ToolRefCode),
                r.ProductHSNID > 0 ? (object)r.ProductHSNID : DBNull.Value,
                1,
                N(tool.SizeL), N(tool.SizeW), N(tool.SizeH),
                N(tool.UpsAround), N(tool.UpsAcross), N(r.TotalUps),
                N(tool.Manufacturer), N(tool.NoOfTeeth), N(tool.CircumferenceMM), N(tool.CircumferenceInch),
                N(tool.BCM), N(tool.LPI),
                N(tool.PurchaseUnit), N(tool.PurchaseRate),
                N(tool.PurchaseUnit), N(tool.EstimateRate ?? tool.PurchaseRate),
                N(tool.StockUnit),
                2, 2, 2, DateTime.Now, 0,
                N(tool.Location)
            );
        }

        using var bulkCopy = new SqlBulkCopy(_connection)
        {
            DestinationTableName = "ToolMaster",
            BatchSize = 500,
            BulkCopyTimeout = 300
        };
        foreach (System.Data.DataColumn col in masterTable.Columns)
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulkCopy.WriteToServerAsync(masterTable);

        // ─── 4. Retrieve newly inserted ToolIDs by ToolCode ─────────────────────
        var toolCodes = masterRows.Select(r => r.ToolCode).ToList();
        var insertedTools = new Dictionary<string, int>();

        const int batchSize = 2000;
        for (int i = 0; i < toolCodes.Count; i += batchSize)
        {
            var batch = toolCodes.Skip(i).Take(batchSize).ToList();
            var batchResults = await _connection.QueryAsync<(string ToolCode, int ToolID)>(
                @"SELECT ToolCode, ToolID FROM ToolMaster
                  WHERE ToolCode IN @Codes
                    AND ISNULL(IsDeletedTransaction, 0) = 0",
                new { Codes = batch });

            foreach (var t in batchResults)
                insertedTools[t.ToolCode] = t.ToolID;
        }

        // ─── 5. Build ToolMasterDetails DataTable for SqlBulkCopy ───────────────
        var detailTable = new System.Data.DataTable();
        detailTable.Columns.Add("ParentToolID",         typeof(int));
        detailTable.Columns.Add("ParentFieldName",      typeof(string));
        detailTable.Columns.Add("ParentFieldValue",     typeof(string));
        detailTable.Columns.Add("ToolID",               typeof(int));
        detailTable.Columns.Add("FieldID",              typeof(int));
        detailTable.Columns.Add("FieldName",            typeof(string));
        detailTable.Columns.Add("FieldValue",           typeof(string));
        detailTable.Columns.Add("SequenceNo",           typeof(int));
        detailTable.Columns.Add("ToolGroupID",          typeof(int));
        detailTable.Columns.Add("CompanyID",            typeof(int));
        detailTable.Columns.Add("UserID",               typeof(int));
        detailTable.Columns.Add("IsBlocked",            typeof(int));
        detailTable.Columns.Add("IsLocked",             typeof(int));
        detailTable.Columns.Add("CreatedBy",            typeof(int));
        detailTable.Columns.Add("CreatedDate",          typeof(DateTime));
        detailTable.Columns.Add("ModifiedBy",           typeof(int));
        detailTable.Columns.Add("ModifiedDate",         typeof(DateTime));
        detailTable.Columns.Add("IsActive",             typeof(int));
        detailTable.Columns.Add("IsDeletedTransaction", typeof(int));

        int successCount = 0;

        foreach (var r in masterRows)
        {
            if (!insertedTools.TryGetValue(r.ToolCode, out int newToolId))
            {
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {r.RowIndex + 1} ({r.Tool.ToolName}): ToolID not found after bulk insert.");
                continue;
            }

            successCount++;
            var tool = r.Tool;
            int seq = 1;
            var now = DateTime.Now;

            int? shelfLife = tool.ShelfLife ?? 365;
            bool? isStandardItem = tool.IsStandardItem ?? true;
            bool? isRegularItem = tool.IsRegularItem ?? true;

            void AddDetail(string fieldName, string? fieldValue)
            {
                if (string.IsNullOrEmpty(fieldValue)) return;
                detailTable.Rows.Add(
                    0, fieldName, fieldValue, newToolId, 0,
                    fieldName, fieldValue, seq++, toolGroupId,
                    2, 2, 0, 0,
                    2, now, 2, now,
                    0, 0
                );
            }

            AddDetail("ToolType",              r.ToolType);
            AddDetail("JobName",               tool.JobName);
            AddDetail("LedgerName",            tool.ClientName);
            AddDetail("Manufacturer",          tool.Manufacturer);
            if (tool.NoOfTeeth.HasValue)           AddDetail("NoOfTeeth",           tool.NoOfTeeth.ToString());
            if (tool.CircumferenceMM.HasValue)     AddDetail("CircumferenceMM",     tool.CircumferenceMM.ToString());
            if (tool.CircumferenceInch.HasValue)   AddDetail("CircumferenceInch",   tool.CircumferenceInch.ToString());
            if (tool.BCM.HasValue)                 AddDetail("BCM",                 tool.BCM.ToString());
            if (tool.LPI.HasValue)                 AddDetail("LPI",                 tool.LPI.ToString());
            if (tool.AroundGap.HasValue)           AddDetail("AroundGap",           tool.AroundGap.ToString());
            if (tool.AcrossGap.HasValue)           AddDetail("AcrossGap",           tool.AcrossGap.ToString());
            AddDetail("UnitSymbol",            tool.UnitSymbol);
            AddDetail("ReferenceToolNo",       tool.ReferenceToolNo);
            if (tool.EstimateRate.HasValue)        AddDetail("EstimateRate",        tool.EstimateRate.ToString());
            if (tool.SizeL.HasValue)               AddDetail("SizeL",               tool.SizeL.ToString());
            if (tool.SizeW.HasValue)               AddDetail("SizeW",               tool.SizeW.ToString());
            if (tool.SizeH.HasValue)               AddDetail("SizeH",               tool.SizeH.ToString());
            if (tool.UpsAround.HasValue)           AddDetail("UpsAround",           tool.UpsAround.ToString());
            if (tool.UpsAcross.HasValue)           AddDetail("UpsAcross",           tool.UpsAcross.ToString());
            if (r.TotalUps.HasValue)               AddDetail("TotalUps",            r.TotalUps.ToString());
            AddDetail("PurchaseUnit",          tool.PurchaseUnit);
            if (tool.PurchaseRate.HasValue)        AddDetail("PurchaseRate",        tool.PurchaseRate.ToString());
            AddDetail("StockUnit",             tool.StockUnit);
            AddDetail("ManufecturerItemCode",  tool.ManufecturerItemCode);
            // SIM / SHIM-specific detail fields (ToolGroupID 13; skipped when empty for other groups)
            AddDetail("Positive",              tool.Positive);
            AddDetail("Negative",              tool.Negative);
            AddDetail("Master",                tool.Master);
            AddDetail("Sim",                   tool.Sim);
            // Location goes to ToolMaster.ToolLocation column (above), not details
            if (tool.PurchaseOrderQuantity.HasValue) AddDetail("PurchaseOrderQuantity", tool.PurchaseOrderQuantity.ToString());
            if (shelfLife.HasValue)                AddDetail("ShelfLife",            shelfLife.ToString());
            if (tool.MinimumStockQty.HasValue)     AddDetail("MinimumStockQty",     tool.MinimumStockQty.ToString());
            if (isStandardItem.HasValue)           AddDetail("IsStandardItem",      isStandardItem.ToString());
            if (isRegularItem.HasValue)            AddDetail("IsRegularItem",       isRegularItem.ToString());
            if (r.ProductHSNID > 0)
            {
                AddDetail("ProductHSNID",  r.ProductHSNID.ToString());
                AddDetail("ProductHSNName", r.ProductHSNID.ToString());
            }
        }

        // ─── 6. SqlBulkCopy → ToolMasterDetails ────────────────────────────────
        using var detailBulkCopy = new SqlBulkCopy(_connection)
        {
            DestinationTableName = "ToolMasterDetails",
            BatchSize = 1000,
            BulkCopyTimeout = 300
        };
        foreach (System.Data.DataColumn col in detailTable.Columns)
            detailBulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        if (detailTable.Rows.Count > 0)
            await detailBulkCopy.WriteToServerAsync(detailTable);

        // ─── 7. Build result ────────────────────────────────────────────────────
        result.ImportedRows = successCount;
        if (result.ErrorRows > 0)
        {
            result.Success = successCount > 0;
            result.Message = $"Imported {successCount} of {tools.Count} tool(s). {result.ErrorRows} row(s) failed.";
        }
        else
        {
            result.Success = true;
            result.Message = $"Successfully imported {successCount} tool(s).";
        }

        return result;
    }

    public async Task<List<ToolGroupDto>> GetToolGroupsAsync()
    {
        var query = @"
            SELECT ToolGroupID, ToolGroupName
            FROM ToolGroupMaster
            WHERE ISNULL(IsDeletedTransaction, 0) = 0
            ORDER BY ToolGroupName";

        var results = await _connection.QueryAsync<ToolGroupDto>(query);
        return results.ToList();
    }

    public async Task<List<HSNGroupDto>> GetToolHSNGroupsAsync()
    {
        var query = @"
            SELECT ProductHSNID, DisplayName, HSNCode
            FROM ProductHSNMaster
            WHERE IsDeletedTransaction = 0
              AND ProductCategory = 'Tool'
              AND DisplayName IS NOT NULL
              AND DisplayName <> ''
            ORDER BY DisplayName";

        var results = await _connection.QueryAsync<HSNGroupDto>(query);
        return results.ToList();
    }

    public async Task<List<UnitDto>> GetToolUnitsAsync()
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

    public async Task<int> ClearAllToolDataAsync(string username, string password, string reason, int toolGroupId)
    {
        if (_connection.State != System.Data.ConnectionState.Open) await _connection.OpenAsync();

        // Validate Credentials - Use same password encoding as login
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
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllToolData Failed: Invalid credentials for user '{username}'. Reason: {reason}\n"); } catch { }
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        int deletedCount = 0;
        var transaction = await _connection.BeginTransactionAsync();
        try
        {
            // Delete from Details first
            var deleteDetailQuery = @"
                DELETE tmd
                FROM ToolMasterDetails tmd
                INNER JOIN ToolMaster tm ON tmd.ToolID = tm.ToolID
                WHERE tm.ToolGroupID = @ToolGroupID";

            await _connection.ExecuteAsync(deleteDetailQuery, new { ToolGroupID = toolGroupId }, transaction: transaction);

            // Delete from Master
            var deleteMasterQuery = "DELETE FROM ToolMaster WHERE ToolGroupID = @ToolGroupID";
            deletedCount = await _connection.ExecuteAsync(deleteMasterQuery, new { ToolGroupID = toolGroupId }, transaction: transaction);

            await transaction.CommitAsync();

            try
            {
                var logMessage = $"[{DateTime.Now}] AUDIT: User '{username}' cleared {deletedCount} Tool records for ToolGroupID {toolGroupId}. Reason: {reason}\n";
                await System.IO.File.AppendAllTextAsync("debug_log.txt", logMessage);
            }
            catch { }

            return deletedCount;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllToolData Exception: {ex.Message}\n"); } catch { }
            throw;
        }
    }

    private object? GetPropertyValue(object obj, string propertyName)
    {
        // Case-insensitive property lookup to handle camelCase from frontend
        var prop = obj.GetType().GetProperty(propertyName,
            System.Reflection.BindingFlags.IgnoreCase |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(obj);
    }
}
