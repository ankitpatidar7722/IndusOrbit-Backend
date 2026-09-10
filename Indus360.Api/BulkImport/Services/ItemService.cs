using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace Backend.Services;

public class ItemService : IItemService
{
    private readonly SqlConnection _connection;

    public ItemService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<ItemMasterDto>> GetAllItemsAsync(int itemGroupId)
    {
        // ── Use stored procedure dbo.GetData for fast, optimised loading ──────
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        try
        {
            var rows = await _connection.QueryAsync<dynamic>(
                "EXEC dbo.GetData @TblName, @CompanyID, @ItemGroupID",
                new { TblName = "ItemMaster", CompanyID = 2, ItemGroupID = itemGroupId });

            var itemsList = new List<ItemMasterDto>();
            foreach (var row in rows)
            {
                var dict = (IDictionary<string, object>)row;

                T? Val<T>(string key)
                {
                    if (!dict.TryGetValue(key, out var v) || v == null || v is DBNull) return default;
                    try { return (T)Convert.ChangeType(v, typeof(T)); } catch { return default; }
                }
                string? Str(string key) => dict.TryGetValue(key, out var v) && v != null && v is not DBNull ? v.ToString() : null;
                decimal? Dec(string key) { var s = Str(key); return decimal.TryParse(s, out var d) ? d : null; }
                int? Int32(string key)  { var s = Str(key); return int.TryParse(s, out var i)  ? i : null; }
                bool? Bool(string key)  { var s = Str(key); return bool.TryParse(s, out var b)  ? b : null; }

                itemsList.Add(new ItemMasterDto
                {
                    ItemID              = Int32("ItemID"),
                    ItemName            = Str("ItemName"),
                    ItemCode            = Str("ItemCode"),
                    ItemGroupID         = Int32("ItemGroupID"),
                    ItemGroupName       = Str("ItemGroupName"),
                    ProductHSNID        = Int32("ProductHSNID"),
                    HSNGroup            = Str("DisplayName") ?? Str("HSNGroup") ?? Str("ProductHSNName"),
                    ProductHSNName      = Str("DisplayName") ?? Str("ProductHSNName") ?? Str("HSNGroup"),
                    HSNCode             = Str("HSNCode"),
                    StockUnit           = Str("StockUnit"),
                    PurchaseUnit        = Str("PurchaseUnit"),
                    EstimationUnit      = Str("EstimationUnit"),
                    EstimationRate      = Dec("EstimationRate"),
                    UnitPerPacking      = Dec("UnitPerPacking"),
                    WtPerPacking        = Dec("WtPerPacking"),
                    ConversionFactor    = Dec("ConversionFactor"),
                    ItemSubGroupID      = Int32("ItemSubGroupID"),
                    ItemSubGroupName    = Str("ItemSubGroupName"),
                    ItemType            = Str("ItemType"),
                    StockType           = Str("StockType"),
                    StockCategory       = Str("StockCategory"),
                    SizeW               = Dec("SizeW"),
                    SizeL               = Dec("SizeL"),
                    ItemSize            = Str("ItemSize"),
                    PurchaseRate        = Dec("PurchaseRate"),
                    StockRefCode        = Str("StockRefCode"),
                    ItemDescription     = Str("ItemDescription"),
                    Quality             = Str("Quality"),
                    GSM                 = Dec("GSM"),
                    Manufecturer        = Str("Manufecturer"),
                    Finish              = Str("Finish"),
                    ManufecturerItemCode= Str("ManufecturerItemCode"),
                    Caliper             = Dec("Caliper"),
                    ShelfLife           = Int32("ShelfLife"),
                    MinimumStockQty     = Dec("MinimumStockQty"),
                    IsStandardItem      = Bool("IsStandardItem"),
                    IsRegularItem       = Bool("IsRegularItem"),
                    PackingType         = Str("PackingType"),
                    BF                  = Str("BF"),
                    InkColour           = Str("InkColour"),
                    PantoneCode         = Str("PantoneCode"),
                    PurchaseOrderQuantity = Dec("PurchaseOrderQuantity"),
                    Thickness           = Dec("Thickness"),
                    Density             = Dec("Density"),
                    ReleaseGSM          = Dec("ReleaseGSM"),
                    AdhesiveGSM         = Dec("AdhesiveGSM"),
                    TotalGSM            = Dec("TotalGSM"),
                    CertificationType   = Str("CertificationType"),
                    PaperGroup          = Str("PaperGroup"),
                    SizeH               = Dec("SizeH"),
                    NoOfPly             = Int32("NoOfPly"),
                    EmptyCartonWt       = Dec("EmptyCartonWt"),
                    Capacity            = Dec("Capacity"),
                    CBF                 = Dec("CBF"),
                    CBM                 = Dec("CBM"),
                    IsDeletedTransaction = Bool("IsDeletedTransaction") ?? false,
                });
            }
            return itemsList;
        }
        catch (Exception ex)
        {
            // SP may not exist yet — fall back to direct query so app still works
            try { System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] GetData SP failed, falling back: {ex.Message}\n"); } catch { }
            return await GetAllItemsAsyncFallback(itemGroupId);
        }
    }

    // Fallback: original JOIN query used when SP is unavailable
    private async Task<List<ItemMasterDto>> GetAllItemsAsyncFallback(int itemGroupId)
    {
        var query = @"
            SELECT
                i.ItemID, i.ItemName, i.ItemCode, i.ItemGroupID,
                ig.ItemGroupName,
                i.ProductHSNID,
                hsn.DisplayName as HSNGroup, hsn.DisplayName as ProductHSNName, hsn.HSNCode,
                i.StockUnit, i.PurchaseUnit, i.EstimationUnit, i.EstimationRate,
                i.UnitPerPacking, i.WtPerPacking, i.ConversionFactor,
                i.ItemSubGroupID, isg.ItemSubGroupName,
                i.ItemType, i.StockType, i.StockCategory,
                i.SizeW, i.SizeL, i.SizeH, i.ItemSize, i.PurchaseRate, i.StockRefCode, i.ItemDescription,
                i.Quality, i.GSM, i.Manufecturer, i.Finish, i.ManufecturerItemCode, i.Caliper,
                i.ShelfLife, i.MinimumStockQty, i.IsStandardItem, i.IsRegularItem,
                i.PackingType, i.BF, i.InkColour, i.PantoneCode, i.PurchaseOrderQuantity,
                i.Thickness, i.Density, i.ReleaseGSM, i.AdhesiveGSM, i.TotalGSM,
                i.NoOfPly, i.EmptyCartonWt, i.Capacity, i.CBF, i.CBM,
                CAST(ISNULL(i.IsDeletedTransaction, 0) AS BIT) as IsDeletedTransaction
            FROM ItemMaster i
            LEFT JOIN ItemGroupMaster ig ON i.ItemGroupID = ig.ItemGroupID
            LEFT JOIN ProductHSNMaster hsn ON i.ProductHSNID = hsn.ProductHSNID
            LEFT JOIN ItemSubGroupMaster isg ON i.ItemSubGroupID = isg.ItemSubGroupID
            WHERE i.ItemGroupID = @ItemGroupID
              AND (i.IsDeletedTransaction IS NULL OR i.IsDeletedTransaction = 0)
            ORDER BY i.ItemName";

        var items = (await _connection.QueryAsync<ItemMasterDto>(query, new { ItemGroupID = itemGroupId }))
            .GroupBy(i => i.ItemID).Select(g => g.First()).ToList();

        var itemIds = items.Where(i => i.ItemID.HasValue).Select(i => i.ItemID!.Value).ToList();
        if (itemIds.Count > 0)
        {
            const int batch = 1000;
            var allDetails = new List<dynamic>();
            for (int b = 0; b < itemIds.Count; b += batch)
            {
                var chunk = itemIds.Skip(b).Take(batch).ToList();
                var details = await _connection.QueryAsync<dynamic>(
                    "SELECT ItemID, FieldName, FieldValue FROM ItemMasterDetails WHERE ItemID IN @IDs AND ISNULL(IsDeletedTransaction,0)=0",
                    new { IDs = chunk });
                allDetails.AddRange(details);
            }
            var detailMap = allDetails.GroupBy(d => (int)d.ItemID)
                                      .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var item in items)
            {
                if (!item.ItemID.HasValue || !detailMap.TryGetValue(item.ItemID.Value, out var details)) continue;
                foreach (var d in details)
                {
                    string fn = d.FieldName; string fv = d.FieldValue ?? "";
                    switch (fn)
                    {
                        case "Quality": item.Quality = fv; break;
                        case "GSM": if (decimal.TryParse(fv, out var g)) item.GSM = g; break;
                        case "Manufecturer": item.Manufecturer = fv; break;
                        case "Finish": item.Finish = fv; break;
                        case "ManufecturerItemCode": item.ManufecturerItemCode = fv; break;
                        case "Caliper": if (decimal.TryParse(fv, out var c)) item.Caliper = c; break;
                        case "ShelfLife": if (int.TryParse(fv, out var sl)) item.ShelfLife = sl; break;
                        case "EstimationRate": if (decimal.TryParse(fv, out var er)) item.EstimationRate = er; break;
                        case "MinimumStockQty": if (decimal.TryParse(fv, out var ms)) item.MinimumStockQty = ms; break;
                        case "IsStandardItem": if (bool.TryParse(fv, out var ist)) item.IsStandardItem = ist; break;
                        case "IsRegularItem": if (bool.TryParse(fv, out var ir)) item.IsRegularItem = ir; break;
                        case "PackingType": item.PackingType = fv; break;
                        case "CertificationType": item.CertificationType = fv; break;
                        case "PaperGroup": item.PaperGroup = fv; break;
                        case "BF": item.BF = fv; break;
                        case "InkColour": item.InkColour = fv; break;
                        case "PantoneCode": item.PantoneCode = fv; break;
                        case "ItemType": item.ItemType = fv; break;
                        case "PurchaseOrderQuantity": if (decimal.TryParse(fv, out var po)) item.PurchaseOrderQuantity = po; break;
                        case "Thickness": if (decimal.TryParse(fv, out var th)) item.Thickness = th; break;
                        case "Density": if (decimal.TryParse(fv, out var dn)) item.Density = dn; break;
                        case "ReleaseGSM": if (decimal.TryParse(fv, out var rg)) item.ReleaseGSM = rg; break;
                        case "AdhesiveGSM": if (decimal.TryParse(fv, out var ag)) item.AdhesiveGSM = ag; break;
                        case "TotalGSM": if (decimal.TryParse(fv, out var tg)) item.TotalGSM = tg; break;
                        case "SizeH": if (decimal.TryParse(fv, out var sh)) item.SizeH = sh; break;
                        case "NoOfPly": if (int.TryParse(fv, out var np)) item.NoOfPly = np; break;
                        case "EmptyCartonWt": if (decimal.TryParse(fv, out var ec)) item.EmptyCartonWt = ec; break;
                        case "Capacity": if (decimal.TryParse(fv, out var cap)) item.Capacity = cap; break;
                        case "CBF": if (decimal.TryParse(fv, out var cbf)) item.CBF = cbf; break;
                        case "CBM": if (decimal.TryParse(fv, out var cbm)) item.CBM = cbm; break;
                    }
                }
            }
        }
        return items;
    }

    public async Task<bool> SoftDeleteItemAsync(int itemId)
    {
        try 
        {
            var query = @"
                UPDATE ItemMaster 
                SET IsDeletedTransaction = 1
                WHERE ItemID = @ItemId;

                UPDATE ItemMasterDetails
                SET IsDeletedTransaction = 1
                WHERE ItemID = @ItemId;";

            var rowsAffected = await _connection.ExecuteAsync(query, new { ItemId = itemId });
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemService] Error in SoftDeleteItemAsync: {ex.Message}");
            throw; // Re-throw to ensure controller catches it and returns 500
        }
    }

    public async Task<ItemValidationResultDto> ValidateItemsAsync(List<ItemMasterDto> items, int itemGroupId)
    {
        var result = new ItemValidationResultDto
        {
            Summary = new ValidationSummary
            {
                TotalRows = items.Count
            }
        };

        // Get existing items from database for duplicate check
        var existingItems = await GetAllItemsAsync(itemGroupId);

        // Get valid HSN Groups
        var validHSNGroups = await GetHSNGroupsAsync();
        var hsnGroupLookup = new HashSet<string>(
            validHSNGroups.Select(h => h.DisplayName.Trim())
        );

        // Get valid Units for fields dynamically from ItemGroupFieldMaster
        var fieldUnits = await GetFieldUnitsAsync(itemGroupId);

        // Get valid Packing Types
        var validPackingTypes = new HashSet<string>(
            new[] { "Ream", "Packet", "Sheet" , "Gross"}
        );

        // Get valid Item Sub Groups (dynamic from ItemGroupFieldMaster)
        var validItemSubGroups = await GetItemSubGroupsAsync(itemGroupId);
        var itemSubGroupLookup = new HashSet<string>(
            validItemSubGroups.Select(sg => sg.ItemSubGroupName.Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        // Get field metadata from ItemGroupFieldMaster for dynamic datatype validation
        var fieldMetadataQuery = @"SELECT FieldName, FieldDataType
            FROM ItemGroupFieldMaster
            WHERE ItemGroupID = @ItemGroupID
              AND ISNULL(IsDeletedTransaction, 0) <> 1
              AND FieldName IS NOT NULL
              AND FieldDataType IS NOT NULL";
        var fieldMetadata = (await _connection.QueryAsync<dynamic>(fieldMetadataQuery, new { ItemGroupID = itemGroupId }))
            .ToDictionary(
                f => (string)f.FieldName,
                f => ((string)f.FieldDataType).Trim().ToLower(),
                StringComparer.OrdinalIgnoreCase
            );

        // Item group-specific required fields
        string[] requiredFields;

        if (itemGroupId == 2) // REEL
        {
            requiredFields = new[] {
                "Quality", "SizeW", "GSM", "Manufecturer", "Finish",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 3) // INK & ADDITIVES
        {
            requiredFields = new[] {
                "ItemSubGroupName", "ItemType", "Manufecturer",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 4) // VARNISHES & COATINGS
        {
            requiredFields = new[] {
                "ItemType", "Quality", "ItemSubGroupName",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 5) // LAMINATION FILM
        {
            requiredFields = new[] {
                "Quality", "ItemSubGroupName",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 6) // FOIL
        {
            requiredFields = new[] {
                "Quality", "ItemSubGroupName",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 7) // SHIPPER CARTON
        {
            requiredFields = new[] {
                "Quality", "ItemSubGroupName", "NoOfPly",
                "SizeL", "SizeW", "SizeH",
                "PurchaseUnit", "EstimationUnit", "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 8) // OTHER MATERIAL
        {
            requiredFields = new[] {
                "ItemSubGroupName", "Quality",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else if (itemGroupId == 13) // ROLL
        {
            requiredFields = new[] {
                "ItemType", "Quality", "Manufecturer", "GSM",
                "PurchaseUnit", "PurchaseRate", "EstimationUnit", "EstimationRate",
                "StockUnit", "ProductHSNName"
            };
        }
        else // PAPER (default) and other item groups
        {
            requiredFields = new[] {
                "Quality", "GSM", "Manufecturer", "Finish", "SizeL", "SizeW",
                "PurchaseRate", "StockUnit", "PurchaseUnit", "EstimationRate",
                "EstimationUnit", "PackingType", "UnitPerPacking"
            };
        }

        // Pre-compute PAPER group flag (everything that is NOT a named item group ID)
        bool isPaperGroup = !(itemGroupId == 2 || itemGroupId == 3 || itemGroupId == 4 ||
                              itemGroupId == 5 || itemGroupId == 6 || itemGroupId == 7 ||
                              itemGroupId == 8 || itemGroupId == 13);

        // Look up the exact 'SHEET' unit symbol from UnitMaster once (preserve DB casing)
        string? sheetUnitSymbol = null;
        if (isPaperGroup)
        {
            sheetUnitSymbol = fieldUnits.StockUnit
                .FirstOrDefault(u => string.Equals(u.Trim(), "SHEET", StringComparison.OrdinalIgnoreCase))
                ?.Trim();
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var rowValidation = new ItemRowValidation
            {
                RowIndex = i,
                Data = item,
                RowStatus = ValidationStatus.Valid
            };

            // Track if row has validation issues
            bool hasMissingData = false;
            bool hasMismatch = false;
            bool hasInvalidContent = false;

            // 1. Check for missing data (BLUE)
            // Note: If a field has an entry in RawValues, it means the frontend
            // couldn't parse it as the expected type (e.g., "10A" for a decimal field).
            // That field is NOT missing — it has invalid content, handled in step 6b.
            foreach (var field in requiredFields)
            {
                // Skip missing check if RawValues has this field (it's invalid, not missing)
                if (item.RawValues != null && item.RawValues.ContainsKey(field))
                    continue;

                var value = GetPropertyValue(item, field);
                bool isMissing = false;

                // Handle numeric fields
                var numericFields = new[] { "GSM", "SizeL", "SizeW", "SizeH", "NoOfPly", "PurchaseRate", "EstimationRate",
                                           "UnitPerPacking", "MinimumStockQty", "ShelfLife" };

                // Fields where 0 is a valid value — only check presence, not positivity
                var allowZeroFields = new[] { "SizeL", "SizeW", "SizeH", "NoOfPly" };

                if (numericFields.Contains(field))
                {
                    if (allowZeroFields.Contains(field))
                        isMissing = value == null || string.IsNullOrWhiteSpace(value.ToString());
                    else
                        isMissing = value == null || string.IsNullOrWhiteSpace(value.ToString()) ||
                                    Convert.ToDecimal(value) <= 0;
                }
                else
                {
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

            // 2. Check for duplicates (RED) - Item group specific logic
            bool IsDuplicate(ItemMasterDto a, ItemMasterDto b)
            {
                if (itemGroupId == 2) // REEL: BF + Quality + GSM + Manufacturer + Finish + SizeW
                {
                    var bfA = a.BF?.Trim() ?? "";
                    var bfB = b.BF?.Trim() ?? "";

                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    var gsmA = a.GSM?.ToString() ?? "";
                    var gsmB = b.GSM?.ToString() ?? "";

                    var manufA = a.Manufecturer?.Trim() ?? "";
                    var manufB = b.Manufecturer?.Trim() ?? "";

                    var finishA = a.Finish?.Trim() ?? "";
                    var finishB = b.Finish?.Trim() ?? "";

                    var sizeWA = a.SizeW?.ToString() ?? "";
                    var sizeWB = b.SizeW?.ToString() ?? "";

                    return string.Equals(bfA, bfB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(gsmA, gsmB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manufA, manufB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(finishA, finishB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(sizeWA, sizeWB, StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 3) // INK & ADDITIVES: ItemType + InkColour + PantoneCode + ManufacturerItemCode + Manufacturer
                {
                    var itemTypeA = a.ItemType?.Trim() ?? "";
                    var itemTypeB = b.ItemType?.Trim() ?? "";

                    var inkColourA = a.InkColour?.Trim() ?? "";
                    var inkColourB = b.InkColour?.Trim() ?? "";

                    var pantoneCodeA = a.PantoneCode?.Trim() ?? "";
                    var pantoneCodeB = b.PantoneCode?.Trim() ?? "";

                    var mfgItemCodeA = a.ManufecturerItemCode?.Trim() ?? "";
                    var mfgItemCodeB = b.ManufecturerItemCode?.Trim() ?? "";

                    var manufacturerA = a.Manufecturer?.Trim() ?? "";
                    var manufacturerB = b.Manufecturer?.Trim() ?? "";

                    return string.Equals(itemTypeA, itemTypeB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(inkColourA, inkColourB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(pantoneCodeA, pantoneCodeB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(mfgItemCodeA, mfgItemCodeB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manufacturerA, manufacturerB, StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 4) // VARNISHES & COATINGS: ItemType + Quality
                {
                    var itemTypeA = a.ItemType?.Trim() ?? "";
                    var itemTypeB = b.ItemType?.Trim() ?? "";

                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    return string.Equals(itemTypeA, itemTypeB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 5) // LAMINATION FILM: Quality + SizeW + Thickness + StockRefCode
                {
                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    var sizeWA = a.SizeW?.ToString() ?? "";
                    var sizeWB = b.SizeW?.ToString() ?? "";

                    var thicknessA = a.Thickness?.ToString() ?? "";
                    var thicknessB = b.Thickness?.ToString() ?? "";

                    var stockRefA = a.StockRefCode?.Trim() ?? "";
                    var stockRefB = b.StockRefCode?.Trim() ?? "";

                    return string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(sizeWA, sizeWB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(thicknessA, thicknessB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(stockRefA, stockRefB, StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 6) // FOIL: Quality + SizeW + Thickness
                {
                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    var sizeWA = a.SizeW?.ToString() ?? "";
                    var sizeWB = b.SizeW?.ToString() ?? "";

                    var thicknessA = a.Thickness?.ToString() ?? "";
                    var thicknessB = b.Thickness?.ToString() ?? "";

                    return string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(sizeWA, sizeWB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(thicknessA, thicknessB, StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 7) // SHIPPER CARTON: Quality + ItemSubGroupName + NoOfPly + SizeL + SizeW + SizeH
                {
                    return string.Equals(a.Quality?.Trim(), b.Quality?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(a.ItemSubGroupName?.Trim(), b.ItemSubGroupName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(a.NoOfPly?.ToString(), b.NoOfPly?.ToString(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(a.SizeL?.ToString(), b.SizeL?.ToString(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(a.SizeW?.ToString(), b.SizeW?.ToString(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(a.SizeH?.ToString(), b.SizeH?.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 8) // OTHER MATERIAL: ItemSubGroupName + Quality + Manufacturer
                {
                    var subGroupA = a.ItemSubGroupName?.Trim() ?? "";
                    var subGroupB = b.ItemSubGroupName?.Trim() ?? "";

                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    var manufA = a.Manufecturer?.Trim() ?? "";
                    var manufB = b.Manufecturer?.Trim() ?? "";

                    return string.Equals(subGroupA, subGroupB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manufA, manufB, StringComparison.OrdinalIgnoreCase);
                }
                else if (itemGroupId == 13) // ROLL: ItemType + Quality + Manufacturer + ManufacturerItemCode + SizeW + GSM + ReleaseGSM + AdhesiveGSM
                {
                    var itemTypeA = a.ItemType?.Trim() ?? "";
                    var itemTypeB = b.ItemType?.Trim() ?? "";

                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    var manufA = a.Manufecturer?.Trim() ?? "";
                    var manufB = b.Manufecturer?.Trim() ?? "";

                    var manufItemCodeA = a.ManufecturerItemCode?.Trim() ?? "";
                    var manufItemCodeB = b.ManufecturerItemCode?.Trim() ?? "";

                    var sizeWA = a.SizeW?.ToString() ?? "";
                    var sizeWB = b.SizeW?.ToString() ?? "";

                    var gsmA = a.GSM?.ToString() ?? "";
                    var gsmB = b.GSM?.ToString() ?? "";

                    var releaseGSMA = a.ReleaseGSM?.ToString() ?? "";
                    var releaseGSMB = b.ReleaseGSM?.ToString() ?? "";

                    var adhesiveGSMA = a.AdhesiveGSM?.ToString() ?? "";
                    var adhesiveGSMB = b.AdhesiveGSM?.ToString() ?? "";

                    return string.Equals(itemTypeA, itemTypeB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manufA, manufB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manufItemCodeA, manufItemCodeB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(sizeWA, sizeWB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(gsmA, gsmB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(releaseGSMA, releaseGSMB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(adhesiveGSMA, adhesiveGSMB, StringComparison.OrdinalIgnoreCase);
                }
                else // PAPER (default): Quality + GSM + Manufacturer + Finish + ItemSize
                {
                    var qualityA = a.Quality?.Trim() ?? "";
                    var qualityB = b.Quality?.Trim() ?? "";

                    var gsmA = a.GSM?.ToString() ?? "";
                    var gsmB = b.GSM?.ToString() ?? "";

                    var manufA = a.Manufecturer?.Trim() ?? "";
                    var manufB = b.Manufecturer?.Trim() ?? "";

                    var finishA = a.Finish?.Trim() ?? "";
                    var finishB = b.Finish?.Trim() ?? "";

                    // Calculate ItemSize for comparison
                    var itemSizeA = (a.SizeW.HasValue && a.SizeL.HasValue) ? $"{a.SizeW} X {a.SizeL}" : "";
                    var itemSizeB = (b.SizeW.HasValue && b.SizeL.HasValue) ? $"{b.SizeW} X {b.SizeL}" : "";

                    return string.Equals(qualityA, qualityB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(gsmA, gsmB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manufA, manufB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(finishA, finishB, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(itemSizeA, itemSizeB, StringComparison.OrdinalIgnoreCase);
                }
            }

            var isDuplicate = existingItems.Any(e => IsDuplicate(e, item));

            // Also check within the current batch
            var duplicateInBatch = items.Take(i).Any(s => IsDuplicate(s, item));

            if (isDuplicate || duplicateInBatch)
            {
                rowValidation.RowStatus = ValidationStatus.Duplicate;
                rowValidation.ErrorMessage = "Duplicate record found";
            }

            // 3. Check HSNGroup mismatch (YELLOW)
            if (!string.IsNullOrWhiteSpace(item.HSNGroup))
            {
                var isValidHSNGroup = hsnGroupLookup.Contains(item.HSNGroup.Trim());

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

            // 4. Check Unit mismatches (YELLOW)
            var unitFields = new[] { "PurchaseUnit", "StockUnit", "EstimationUnit" };
            foreach (var unitField in unitFields)
            {
                // PAPER group: StockUnit has a dedicated SHEET-only check below — skip generic mismatch here
                if (isPaperGroup && unitField == "StockUnit") continue;

                var unitValue = GetPropertyValue(item, unitField)?.ToString();
                if (!string.IsNullOrWhiteSpace(unitValue))
                {
                    List<string> validUnitsForField = new List<string>();
                    if (string.Equals(unitField, "PurchaseUnit", StringComparison.OrdinalIgnoreCase))
                        validUnitsForField = fieldUnits.PurchaseUnit;
                    else if (string.Equals(unitField, "EstimationUnit", StringComparison.OrdinalIgnoreCase))
                        validUnitsForField = fieldUnits.EstimationUnit;
                    else if (string.Equals(unitField, "StockUnit", StringComparison.OrdinalIgnoreCase))
                        validUnitsForField = fieldUnits.StockUnit;

                    var isValidUnit = validUnitsForField.Contains(unitValue.Trim(), StringComparer.OrdinalIgnoreCase);

                    if (!isValidUnit)
                    {
                        rowValidation.CellValidations.Add(new CellValidation
                        {
                            ColumnName = unitField,
                            //ValidationMessage = $"{unitField} does not match UnitMaster UnitSymbol",
                            Status = ValidationStatus.Mismatch
                        });

                        if (rowValidation.RowStatus == ValidationStatus.Valid)
                            rowValidation.RowStatus = ValidationStatus.Mismatch;

                        hasMismatch = true;
                    }
                }
            }

            // 4a. PAPER-specific: StockUnit must be exactly the 'SHEET' symbol from UnitMaster (PURPLE – InvalidContent)
            if (isPaperGroup && sheetUnitSymbol != null && !string.IsNullOrWhiteSpace(item.StockUnit))
            {
                if (!string.Equals(item.StockUnit.Trim(), sheetUnitSymbol, StringComparison.Ordinal))
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "StockUnit",
                        ValidationMessage = $"StockUnit must be '{sheetUnitSymbol}' for Item Group PAPER.",
                        Status = ValidationStatus.InvalidContent
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.InvalidContent;

                    hasInvalidContent = true;
                }
            }

            // 4b. Check PackingType mismatch (YELLOW)
            if (!string.IsNullOrWhiteSpace(item.PackingType))
            {
                var isValidPackingType = validPackingTypes.Contains(item.PackingType.Trim());

                if (!isValidPackingType)
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "PackingType",
                        //ValidationMessage = "PackingType does not match valid values (Ream, Box, Bundle, Carton, Pallet, Roll, Sheet)",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 4c. Check ItemSubGroupName mismatch (YELLOW) - for groups with ItemSubGroupName
            if ((itemGroupId == 3 || itemGroupId == 4 || itemGroupId == 5 || itemGroupId == 6 || itemGroupId == 7 || itemGroupId == 8) && !string.IsNullOrWhiteSpace(item.ItemSubGroupName))
            {
                var isValidSubGroup = itemSubGroupLookup.Contains(item.ItemSubGroupName.Trim());

                if (!isValidSubGroup)
                {
                    rowValidation.CellValidations.Add(new CellValidation
                    {
                        ColumnName = "ItemSubGroupName",
                        ValidationMessage = "Invalid",
                        Status = ValidationStatus.Mismatch
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.Mismatch;

                    hasMismatch = true;
                }
            }

            // 4d. Check ProductHSNName (HSNGroup) mismatch - rename for clarity
            if (!string.IsNullOrWhiteSpace(item.ProductHSNName))
            {
                var isValidProductHSN = hsnGroupLookup.Contains(item.ProductHSNName.Trim());

                if (!isValidProductHSN)
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

            // 5. Check for Special Characters (InvalidContent)
            var stringProperties = typeof(ItemMasterDto)
                .GetProperties()
                .Where(p => p.PropertyType == typeof(string) && 
                           p.Name != "ItemDescription" && 
                           p.Name != "StockRefCode");

            foreach (var prop in stringProperties)
            {
                var val = prop.GetValue(item) as string;
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

            // 6. Dynamic datatype validation from ItemGroupFieldMaster (InvalidContent - PURPLE)
            foreach (var fieldEntry in fieldMetadata)
            {
                var fieldName = fieldEntry.Key;
                var fieldDataType = fieldEntry.Value;

                var rawValue = GetPropertyValue(item, fieldName);
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
                       // ValidationMessage = validationMsg,
                        Status = ValidationStatus.InvalidContent
                    });

                    if (rowValidation.RowStatus == ValidationStatus.Valid)
                        rowValidation.RowStatus = ValidationStatus.InvalidContent;

                    hasInvalidContent = true;
                }
            }

            // 6b. Validate RawValues — fields where frontend couldn't parse the value
            // These are original string values like "10A" for a decimal field
            if (item.RawValues != null && item.RawValues.Count > 0)
            {
                foreach (var rawEntry in item.RawValues)
                {
                    var rawFieldName = rawEntry.Key;
                    var rawValue = rawEntry.Value;

                    // Look up expected data type from field metadata
                    string expectedType = "unknown";
                    if (fieldMetadata.TryGetValue(rawFieldName, out var fdt))
                    {
                        expectedType = fdt;
                    }
                    else
                    {
                        // Infer type from the DTO property type
                        var prop = typeof(ItemMasterDto).GetProperty(rawFieldName);
                        if (prop != null)
                        {
                            var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                            if (underlyingType == typeof(decimal) || underlyingType == typeof(double) || underlyingType == typeof(float))
                                expectedType = "decimal";
                            else if (underlyingType == typeof(int) || underlyingType == typeof(long))
                                expectedType = "integer";
                            else if (underlyingType == typeof(bool))
                                expectedType = "boolean";
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

    public async Task<ImportResultDto> ImportItemsAsync(List<ItemMasterDto> items, int itemGroupId)
    {
        var result = new ImportResultDto();
        result.TotalRows = items.Count;

        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();

        // ─── 1. Fetch lookup tables ───────────────────────────────────────────
        var hsnGroups = await GetHSNGroupsAsync();
        var hsnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hsnGroups)
            if (!string.IsNullOrWhiteSpace(h.DisplayName) && !hsnMap.ContainsKey(h.DisplayName))
                hsnMap[h.DisplayName.Trim()] = h.ProductHSNID;

        var subGroups = await GetItemSubGroupsAsync(itemGroupId);
        var subGroupMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sg in subGroups)
            if (!string.IsNullOrWhiteSpace(sg.ItemSubGroupName) && !subGroupMap.ContainsKey(sg.ItemSubGroupName))
                subGroupMap[sg.ItemSubGroupName.Trim()] = sg.ItemSubGroupID;

        var itemGroup = await _connection.QueryFirstOrDefaultAsync<ItemGroupDto>(@"
            SELECT ItemGroupID, ItemGroupName, ItemGroupPrefix, ItemNameFormula, ItemDescriptionFormula
            FROM ItemGroupMaster
            WHERE ItemGroupID = @ItemGroupID AND CompanyID = 2 AND IsDeletedTransaction = 0",
            new { ItemGroupID = itemGroupId });

        if (itemGroup == null)
        {
            result.Success = false;
            result.Message = $"ItemGroup with ID {itemGroupId} not found.";
            return result;
        }

        var maxItemNo = await _connection.ExecuteScalarAsync<int?>(
            "SELECT ISNULL(MAX(MaxItemNo), 0) FROM ItemMaster WHERE ItemGroupID = @ItemGroupID AND IsDeletedTransaction = 0",
            new { ItemGroupID = itemGroupId }) ?? 0;

        // ─── 2. Pre-process rows: build prepared rows + collect errors ────────
        string defaultItemType = itemGroupId == 4 ? "Varnish"
            : itemGroupId == 5 ? "LAMINATION FILM"
            : itemGroupId == 6 ? "FOIL"
            : itemGroupId == 7 ? "E"
            : itemGroupId == 8 ? "OTHER MATERIAL"
            : itemGroupId == 13 ? "Paper" : "PAPER";

        var masterRows = new List<(int RowIndex, int MaxItemNo, string ItemCode, ItemMasterDto Item,
                                   int ProductHSNID, int ItemSubGroupID, string ItemType,
                                   string? ItemSize, string ItemDescription)>();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            try
            {
                maxItemNo++;
                string itemCode = $"{itemGroup.ItemGroupPrefix}{maxItemNo.ToString().PadLeft(5, '0')}";

                string? itemSize = item.ItemSize;
                if (string.IsNullOrEmpty(itemSize) && item.SizeW.HasValue && item.SizeL.HasValue)
                    itemSize = $"{item.SizeW} X {item.SizeL}";

                var descParts = new List<string>();
                if (!string.IsNullOrEmpty(item.ItemType))     descParts.Add($"ItemType:{item.ItemType}");
                if (!string.IsNullOrEmpty(item.InkColour))    descParts.Add($"InkColour:{item.InkColour}");
                if (!string.IsNullOrEmpty(item.PantoneCode))  descParts.Add($"PantoneCode:{item.PantoneCode}");
                if (!string.IsNullOrEmpty(item.Quality))      descParts.Add($"Quality:{item.Quality}");
                if (item.GSM.HasValue)                        descParts.Add($"GSM:{item.GSM}");
                if (item.ReleaseGSM.HasValue)                 descParts.Add($"ReleaseGSM:{item.ReleaseGSM}");
                if (item.AdhesiveGSM.HasValue)                descParts.Add($"AdhesiveGSM:{item.AdhesiveGSM}");
                if (!string.IsNullOrEmpty(item.Manufecturer)) descParts.Add($"Manufecturer:{item.Manufecturer}");
                if (!string.IsNullOrEmpty(item.Finish))       descParts.Add($"Finish:{item.Finish}");
                if (item.SizeW.HasValue)                      descParts.Add($"SizeW:{item.SizeW}");
                if (item.SizeL.HasValue)                      descParts.Add($"SizeL:{item.SizeL}");
                if (item.SizeH.HasValue)                      descParts.Add($"SizeH:{item.SizeH}");
                if (item.NoOfPly.HasValue)                    descParts.Add($"NoOfPly:{item.NoOfPly}");
                if (item.Caliper.HasValue)                    descParts.Add($"Caliper:{item.Caliper}");

                int productHSNID = 0;
                if (!string.IsNullOrWhiteSpace(item.HSNGroup) && hsnMap.TryGetValue(item.HSNGroup.Trim(), out int hId))
                    productHSNID = hId;
                else if (!string.IsNullOrWhiteSpace(item.ProductHSNName) && hsnMap.TryGetValue(item.ProductHSNName.Trim(), out int hId2))
                    productHSNID = hId2;

                int subGroupID = 0;
                if (!string.IsNullOrWhiteSpace(item.ItemSubGroupName) && subGroupMap.TryGetValue(item.ItemSubGroupName.Trim(), out int sgId))
                    subGroupID = sgId;

                string resolvedItemType = item.ItemType ?? item.StockCategory?.ToUpper() ?? defaultItemType;

                masterRows.Add((i, maxItemNo, itemCode, item, productHSNID, subGroupID,
                                resolvedItemType, itemSize, string.Join(", ", descParts)));
            }
            catch (Exception ex)
            {
                maxItemNo--;
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {i + 1} ({item.ItemName}): Pre-process error – {ex.Message}");
            }
        }

        if (masterRows.Count == 0)
        {
            result.Success = false;
            result.Message = "All rows failed pre-processing. Nothing was imported.";
            return result;
        }

        // ─── 3. SqlBulkCopy → ItemMaster ─────────────────────────────────────
        var masterTable = new System.Data.DataTable();
        masterTable.Columns.Add("ItemName",              typeof(string));
        masterTable.Columns.Add("ItemCode",              typeof(string));
        masterTable.Columns.Add("MaxItemNo",             typeof(int));
        masterTable.Columns.Add("ItemCodePrefix",        typeof(string));
        masterTable.Columns.Add("ItemGroupID",           typeof(int));
        masterTable.Columns.Add("ProductHSNID",          typeof(object));
        masterTable.Columns.Add("StockUnit",             typeof(string));
        masterTable.Columns.Add("PurchaseUnit",          typeof(string));
        masterTable.Columns.Add("EstimationUnit",        typeof(string));
        masterTable.Columns.Add("EstimationRate",        typeof(object));
        masterTable.Columns.Add("UnitPerPacking",        typeof(object));
        masterTable.Columns.Add("WtPerPacking",          typeof(object));
        masterTable.Columns.Add("ConversionFactor",      typeof(object));
        masterTable.Columns.Add("ItemSubGroupID",        typeof(object));
        masterTable.Columns.Add("StockType",             typeof(string));
        masterTable.Columns.Add("StockCategory",         typeof(string));
        masterTable.Columns.Add("ItemType",              typeof(string));
        masterTable.Columns.Add("SizeW",                 typeof(object));
        masterTable.Columns.Add("SizeL",                 typeof(object));
        masterTable.Columns.Add("ItemSize",              typeof(string));
        masterTable.Columns.Add("PurchaseRate",          typeof(object));
        masterTable.Columns.Add("StockRefCode",          typeof(string));
        masterTable.Columns.Add("ItemDescription",       typeof(string));
        masterTable.Columns.Add("Quality",               typeof(string));
        masterTable.Columns.Add("GSM",                   typeof(object));
        masterTable.Columns.Add("Manufecturer",          typeof(string));
        masterTable.Columns.Add("Finish",                typeof(string));
        masterTable.Columns.Add("ManufecturerItemCode",  typeof(string));
        masterTable.Columns.Add("Caliper",               typeof(object));
        masterTable.Columns.Add("ShelfLife",             typeof(object));
        masterTable.Columns.Add("MinimumStockQty",       typeof(object));
        masterTable.Columns.Add("IsStandardItem",        typeof(object));
        masterTable.Columns.Add("IsRegularItem",         typeof(object));
        masterTable.Columns.Add("PackingType",           typeof(string));
        masterTable.Columns.Add("BF",                    typeof(string));
        masterTable.Columns.Add("InkColour",             typeof(string));
        masterTable.Columns.Add("PantoneCode",           typeof(string));
        masterTable.Columns.Add("PurchaseOrderQuantity", typeof(object));
        masterTable.Columns.Add("Thickness",             typeof(object));
        masterTable.Columns.Add("Density",               typeof(object));
        masterTable.Columns.Add("ReleaseGSM",            typeof(object));
        masterTable.Columns.Add("AdhesiveGSM",           typeof(object));
        masterTable.Columns.Add("TotalGSM",              typeof(object));
        masterTable.Columns.Add("SizeH",                 typeof(object));
        masterTable.Columns.Add("NoOfPly",               typeof(object));
        masterTable.Columns.Add("EmptyCartonWt",         typeof(object));
        masterTable.Columns.Add("Capacity",              typeof(object));
        masterTable.Columns.Add("CBF",                   typeof(object));
        masterTable.Columns.Add("CBM",                   typeof(object));
        masterTable.Columns.Add("ISItemActive",          typeof(int));
        masterTable.Columns.Add("CompanyID",             typeof(int));
        masterTable.Columns.Add("UserID",                typeof(int));
        masterTable.Columns.Add("FYear",                 typeof(string));
        masterTable.Columns.Add("CreatedBy",             typeof(int));
        masterTable.Columns.Add("CreatedDate",           typeof(DateTime));
        masterTable.Columns.Add("IsDeletedTransaction",  typeof(bool));

        object N(object? v) => v ?? DBNull.Value;

        foreach (var r in masterRows)
        {
            var it = r.Item;
            masterTable.Rows.Add(
                N(it.ItemName), r.ItemCode, r.MaxItemNo,
                N(itemGroup.ItemGroupPrefix),
                itemGroupId,
                r.ProductHSNID > 0 ? (object)r.ProductHSNID : DBNull.Value,
                N(it.StockUnit), N(it.PurchaseUnit), N(it.EstimationUnit),
                N(it.EstimationRate), N(it.UnitPerPacking), N(it.WtPerPacking), N(it.ConversionFactor),
                r.ItemSubGroupID != 0 ? (object)r.ItemSubGroupID : DBNull.Value,
                N(it.StockType), N(it.StockCategory), r.ItemType,
                N(it.SizeW), N(it.SizeL), N(r.ItemSize),
                N(it.PurchaseRate), N(it.StockRefCode), N(r.ItemDescription),
                N(it.Quality), N(it.GSM), N(it.Manufecturer), N(it.Finish),
                N(it.ManufecturerItemCode), N(it.Caliper), N(it.ShelfLife),
                N(it.MinimumStockQty), N(it.IsStandardItem), N(it.IsRegularItem),
                N(it.PackingType), N(it.BF), N(it.InkColour), N(it.PantoneCode),
                N(it.PurchaseOrderQuantity), N(it.Thickness), N(it.Density),
                N(it.ReleaseGSM), N(it.AdhesiveGSM), N(it.TotalGSM),
                N(it.SizeH), N(it.NoOfPly), N(it.EmptyCartonWt), N(it.Capacity), N(it.CBF), N(it.CBM),
                1, 2, 2, "2025-2026", 2, DateTime.Now, false
            );
        }

        // Bulk copy master rows
        using var bulkCopy = new SqlBulkCopy(_connection)
        {
            DestinationTableName = "ItemMaster",
            BatchSize = 500,
            BulkCopyTimeout = 300
        };
        foreach (System.Data.DataColumn col in masterTable.Columns)
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulkCopy.WriteToServerAsync(masterTable);

        // ─── 4. Retrieve newly inserted ItemIDs (match by ItemCode) ──────────
        // ItemCode is unique per group (prefix + incrementing number), so no timestamp filter needed.
        // NOTE: The old timestamp filter failed in production because the IIS app server clock
        // and SQL Server clock were out of sync (different machines).
        var itemCodes = masterRows.Select(r => r.ItemCode).ToList();
        var insertedItems = new Dictionary<string, int>();

        const int batchSize = 2000;
        for (int i = 0; i < itemCodes.Count; i += batchSize)
        {
            var batch = itemCodes.Skip(i).Take(batchSize).ToList();
            var batchResults = await _connection.QueryAsync<(string ItemCode, int ItemID)>(
                @"SELECT ItemCode, ItemID FROM ItemMaster
                  WHERE ItemCode IN @Codes
                    AND IsDeletedTransaction = 0",
                new { Codes = batch });

            foreach (var item in batchResults)
                insertedItems[item.ItemCode] = item.ItemID;
        }

        int successCount = 0;

        // ─── 5. Build ItemMasterDetails DataTable for SqlBulkCopy ───────────
        var detailTable = new System.Data.DataTable();
        detailTable.Columns.Add("ItemID",            typeof(int));
        detailTable.Columns.Add("ParentFieldName",   typeof(string));
        detailTable.Columns.Add("ParentFieldValue",  typeof(string));
        detailTable.Columns.Add("FieldName",         typeof(string));
        detailTable.Columns.Add("FieldValue",        typeof(string));
        detailTable.Columns.Add("SequenceNo",        typeof(int));
        detailTable.Columns.Add("ItemGroupID",       typeof(int));
        detailTable.Columns.Add("CompanyID",         typeof(int));
        detailTable.Columns.Add("UserID",            typeof(int));
        detailTable.Columns.Add("FYear",             typeof(string));
        detailTable.Columns.Add("CreatedBy",         typeof(int));
        detailTable.Columns.Add("CreatedDate",       typeof(DateTime));
        detailTable.Columns.Add("IsDeletedTransaction", typeof(bool));

        foreach (var r in masterRows)
        {
            if (!insertedItems.TryGetValue(r.ItemCode, out int newItemId))
            {
                result.ErrorRows++;
                result.ErrorMessages.Add($"Row {r.RowIndex + 1} ({r.Item.ItemName}): ItemID not found after bulk insert.");
                continue;
            }

            successCount++;
            var it = r.Item;
            int seq = 1;
            var now = DateTime.Now;

            void AddDetail(string fn, string? fv)
            {
                if (string.IsNullOrEmpty(fv)) return;
                detailTable.Rows.Add(newItemId, fn, fv, fn, fv, seq++,
                    itemGroupId, 2, 2, "2025-2026", 2, now, false);
            }

            AddDetail("BF",                    it.BF);
            AddDetail("InkColour",             it.InkColour);
            AddDetail("PantoneCode",           it.PantoneCode);
            AddDetail("ItemType",              r.ItemType);
            if (r.ItemSubGroupID != 0) AddDetail("ItemSubGroupID", r.ItemSubGroupID.ToString());
            AddDetail("StockType",             it.StockType);
            if (it.PurchaseOrderQuantity.HasValue) AddDetail("PurchaseOrderQuantity", it.PurchaseOrderQuantity.ToString());
            AddDetail("Quality",               it.Quality);
            if (it.GSM.HasValue)               AddDetail("GSM",              it.GSM.ToString());
            AddDetail("Manufecturer",          it.Manufecturer);
            AddDetail("Finish",                it.Finish);
            AddDetail("ManufecturerItemCode",  it.ManufecturerItemCode);
            if (it.Caliper.HasValue)           AddDetail("Caliper",          it.Caliper.ToString());
            if (it.SizeW.HasValue)             AddDetail("SizeW",            it.SizeW.ToString());
            if (it.SizeL.HasValue)             AddDetail("SizeL",            it.SizeL.ToString());
            if (it.SizeH.HasValue)             AddDetail("SizeH",            it.SizeH.ToString());
            if (it.EmptyCartonWt.HasValue)     AddDetail("EmptyCartonWt",    it.EmptyCartonWt.ToString());
            AddDetail("PurchaseUnit",          it.PurchaseUnit);
            if (it.PurchaseRate.HasValue)      AddDetail("PurchaseRate",     it.PurchaseRate.ToString());
            if (it.ShelfLife.HasValue)         AddDetail("ShelfLife",        it.ShelfLife.ToString());
            AddDetail("EstimationUnit",        it.EstimationUnit);
            if (it.EstimationRate.HasValue)    AddDetail("EstimationRate",   it.EstimationRate.ToString());
            AddDetail("StockUnit",             it.StockUnit);
            if (it.MinimumStockQty.HasValue)   AddDetail("MinimumStockQty",  it.MinimumStockQty.ToString());
            if (it.IsStandardItem.HasValue)    AddDetail("IsStandardItem",   it.IsStandardItem.ToString());
            if (it.IsRegularItem.HasValue)     AddDetail("IsRegularItem",    it.IsRegularItem.ToString());
            AddDetail("PackingType",           it.PackingType);
            if (it.UnitPerPacking.HasValue)    AddDetail("UnitPerPacking",   it.UnitPerPacking.ToString());
            if (it.WtPerPacking.HasValue)      AddDetail("WtPerPacking",     it.WtPerPacking.ToString());
            AddDetail("ItemSize",              r.ItemSize);
            if (r.ProductHSNID > 0)
            {
                AddDetail("ProductHSNID",      r.ProductHSNID.ToString());
                AddDetail("ProductHSNName",    r.ProductHSNID.ToString());
            }
            if (it.Thickness.HasValue)         AddDetail("Thickness",        it.Thickness.ToString());
            if (it.Density.HasValue)           AddDetail("Density",          it.Density.ToString());
            if (it.ReleaseGSM.HasValue)        AddDetail("ReleaseGSM",       it.ReleaseGSM.ToString());
            if (it.AdhesiveGSM.HasValue)       AddDetail("AdhesiveGSM",      it.AdhesiveGSM.ToString());
            if (it.TotalGSM.HasValue)          AddDetail("TotalGSM",         it.TotalGSM.ToString());
            AddDetail("StockRefCode",          it.StockRefCode);
            AddDetail("CertificationType",     it.CertificationType);
            AddDetail("PaperGroup",            it.PaperGroup);
            if (it.NoOfPly.HasValue)           AddDetail("NoOfPly",          it.NoOfPly.ToString());
            if (it.Capacity.HasValue)          AddDetail("Capacity",         it.Capacity.ToString());
            if (it.CBF.HasValue)               AddDetail("CBF",              it.CBF.ToString());
            if (it.CBM.HasValue)               AddDetail("CBM",              it.CBM.ToString());
            AddDetail("ISItemActive",          "True");
        }

        // Bulk-copy all detail rows in a single operation
        using var detailBulkCopy = new SqlBulkCopy(_connection)
        {
            DestinationTableName = "ItemMasterDetails",
            BatchSize = 1000,
            BulkCopyTimeout = 300
        };
        foreach (System.Data.DataColumn col in detailTable.Columns)
            detailBulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        if (detailTable.Rows.Count > 0)
            await detailBulkCopy.WriteToServerAsync(detailTable);

        // ─── 6. Build result ──────────────────────────────────────────────────
        result.ImportedRows = successCount;
        if (result.ErrorRows > 0)
        {
            result.Success = successCount > 0;
            result.Message = $"Imported {successCount} of {items.Count} item(s). {result.ErrorRows} row(s) failed.";
        }
        else
        {
            result.Success = true;
            result.Message = $"Successfully imported {successCount} item(s).";
        }

        return result;
    }

    public async Task<List<ItemGroupDto>> GetItemGroupsAsync()
    {
        var query = @"
            SELECT ItemGroupID, ItemGroupName, ItemGroupPrefix,
                   ItemNameFormula, ItemDescriptionFormula
            FROM ItemGroupMaster
            WHERE CompanyID = 2 
              AND IsDeletedTransaction = 0
            ORDER BY ItemGroupName";

        var results = await _connection.QueryAsync<ItemGroupDto>(query);
        return results.ToList();
    }

    public async Task<List<HSNGroupDto>> GetHSNGroupsAsync()
    {
        var query = @"
            SELECT ProductHSNID, DisplayName, HSNCode
            FROM ProductHSNMaster
            WHERE IsDeletedTransaction = 0 
            AND DisplayName IS NOT NULL 
            AND DisplayName <> ''
            ORDER BY DisplayName";

        var results = await _connection.QueryAsync<HSNGroupDto>(query);
        return results.ToList();
    }

    public async Task<FieldUnitsDto> GetFieldUnitsAsync(int itemGroupId)
    {
        // 1. ItemGroupFieldMaster: comma-separated preferred values per field
        var fieldQuery = @"
            SELECT FieldName, SelectBoxDefault
            FROM ItemGroupFieldMaster
            WHERE ItemGroupID = @ItemGroupID
            AND FieldName IN ('PurchaseUnit', 'EstimationUnit', 'StockUnit')";

        var fieldResults = await _connection.QueryAsync<(string FieldName, string SelectBoxDefault)>(
            fieldQuery, new { ItemGroupID = itemGroupId });

        // 2. UnitMaster: all DISTINCT unit symbols (fallback/supplement)
        var unitMasterQuery = @"
            SELECT DISTINCT LTRIM(RTRIM(UnitSymbol)) AS UnitSymbol
            FROM UnitMaster
            WHERE UnitSymbol IS NOT NULL AND LTRIM(RTRIM(UnitSymbol)) <> ''
            ORDER BY UnitSymbol";

        var unitMasterValues = (await _connection.QueryAsync<string>(unitMasterQuery)).ToList();

        // Parse ItemGroupFieldMaster values into a per-field map
        var fieldMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in fieldResults)
        {
            if (!string.IsNullOrWhiteSpace(r.SelectBoxDefault))
            {
                fieldMap[r.FieldName] = r.SelectBoxDefault
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }

        // Merge: ItemGroupFieldMaster first (preserves preferred casing), then UnitMaster for extras
        static List<string> Merge(List<string> primary, List<string> secondary)
        {
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var v in primary)
                if (seen.Add(v)) result.Add(v);
            foreach (var v in secondary)
                if (seen.Add(v)) result.Add(v);
            return result;
        }

        var empty = new List<string>();
        var dto = new FieldUnitsDto
        {
            PurchaseUnit   = Merge(fieldMap.GetValueOrDefault("PurchaseUnit",   empty), unitMasterValues),
            EstimationUnit = Merge(fieldMap.GetValueOrDefault("EstimationUnit", empty), unitMasterValues),
            StockUnit      = Merge(fieldMap.GetValueOrDefault("StockUnit",      empty), unitMasterValues)
        };
        return dto;
    }

    public async Task<int> ClearAllItemDataAsync(string username, string password, string reason, int itemGroupId)
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
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllItemData Failed: Invalid credentials for user '{username}'. Reason: {reason}\n"); } catch { }
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        // 2. Perform Transactional Delete
        int deletedCount = 0;
        var transaction = await _connection.BeginTransactionAsync();
        try
        {
            // Delete from Details first (foreign key constraint)
            var deleteDetailQuery = @"
                DELETE imd 
                FROM ItemMasterDetails imd
                INNER JOIN ItemMaster im ON imd.ItemID = im.ItemID
                WHERE im.ItemGroupID = @ItemGroupID";
            
            await _connection.ExecuteAsync(deleteDetailQuery, new { ItemGroupID = itemGroupId }, transaction: transaction);

            // Delete from Master
            var deleteMasterQuery = "DELETE FROM ItemMaster WHERE ItemGroupID = @ItemGroupID";
            deletedCount = await _connection.ExecuteAsync(deleteMasterQuery, new { ItemGroupID = itemGroupId }, transaction: transaction);

            await transaction.CommitAsync();

            // 3. Log Audit
            try
            {
                var logMessage = $"[{DateTime.Now}] AUDIT: User '{username}' cleared {deletedCount} Item records for ItemGroupID {itemGroupId}. Reason: {reason}\n";
                await System.IO.File.AppendAllTextAsync("debug_log.txt", logMessage);
            }
            catch { }

            return deletedCount;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            try { await System.IO.File.AppendAllTextAsync("debug_log.txt", $"[{DateTime.Now}] ClearAllItemData Exception: {ex.Message}\n"); } catch { }
            throw;
        }
    }

    public async Task<List<ItemSubGroupDto>> GetItemSubGroupsAsync(int itemGroupId)
    {
        // Step 1: Fetch the dynamic query from ItemGroupFieldMaster
        var fieldQuery = @"
            SELECT SelectBoxQueryDB
            FROM ItemGroupFieldMaster
            WHERE ItemGroupID = @ItemGroupID
            AND FieldName = 'ItemSubGroupID'
            AND ISNULL(IsDeletedTransaction, 0) <> 1";

        var selectBoxQuery = await _connection.QueryFirstOrDefaultAsync<string>(
            fieldQuery, new { ItemGroupID = itemGroupId });

        if (string.IsNullOrWhiteSpace(selectBoxQuery))
        {
            return new List<ItemSubGroupDto>();
        }

        // Step 2: Replace # placeholders with single quotes (legacy DB pattern)
        var dynamicQuery = selectBoxQuery.Replace("#", "'");

        // Step 3: Execute the dynamic query and map results
        var results = await _connection.QueryAsync<ItemSubGroupDto>(dynamicQuery);
        return results.ToList();
    }

    private object? GetPropertyValue(object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName)?.GetValue(obj);
    }
}
