using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Backend.Services;

public class ContentAuthorityService : IContentAuthorityService
{
    private const string SourceConnStr =
        "Data Source=13.200.122.70,1433;" +
        "Initial Catalog=IndusEnterpriseKeyline;" +
        "User ID=Indus;Password=Param@99811;" +
        "Persist Security Info=True;TrustServerCertificate=True";

    private readonly SqlConnection _clientConn;

    public ContentAuthorityService(SqlConnection clientConn)
    {
        _clientConn = clientConn;
    }

    public async Task<List<ContentAuthorityRowDto>> GetContentAuthorityDataAsync()
    {
        await using var sourceConn = new SqlConnection(SourceConnStr);
        var sourceRows = (await sourceConn.QueryAsync<dynamic>(
            "SELECT DISTINCT ContentName, ISNULL(ContentCaption,'') as ContentCaption, ISNULL(ContentOpenHref,'') as OpenHref, ISNULL(ContentClosedHref,'') as ClosedHref FROM ContentMaster WHERE ISNULL(ContentName,'') <> '' and Isactive=1 ORDER BY ContentName"
        )).ToList();

        var clientLookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var clientExists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var wasOpen = _clientConn.State == ConnectionState.Open;
            if (!wasOpen) await _clientConn.OpenAsync();

            var tableExists = await _clientConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ContentMaster]') AND type = 'U'"
            );

            if (tableExists > 0)
            {
                await _clientConn.ExecuteAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'IsActive' AND Object_ID = Object_ID(N'ContentMaster'))
                    BEGIN
                        ALTER TABLE ContentMaster ADD IsActive BIT DEFAULT 1;
                        EXEC('UPDATE ContentMaster SET IsActive = 1');
                    END");

                var rows = await _clientConn.QueryAsync<dynamic>("SELECT ContentName, ISNULL(IsActive, 1) as IsActive FROM ContentMaster");
                foreach (var r in rows)
                {
                    string name = (string)r.ContentName;
                    bool active = (bool)r.IsActive;
                    clientExists.Add(name);
                    if (!clientLookup.ContainsKey(name) || active) clientLookup[name] = active;
                }
            }
            if (!wasOpen) await _clientConn.CloseAsync();
        }
        catch (Exception ex) { Console.WriteLine($"[ContentAuthority] GET Error: {ex.Message}"); }

        return sourceRows.Select(r => new ContentAuthorityRowDto
        {
            ContentName = (string)r.ContentName,
            ContentCaption = (string)r.ContentCaption,
            ContentOpenHref = (string)r.OpenHref,
            ContentClosedHref = (string)r.ClosedHref,
            IsSelected = clientLookup.TryGetValue((string)r.ContentName, out var active) && active,
            ExistsInClientDb = clientExists.Contains((string)r.ContentName)
        }).ToList();
    }

    // ── SAVE ACCESS (Authority Logic) ───────────────────
    /* 
       Flow requested by user:
       1. If Content NOT exists in Client DB -> INSERT into ContentMaster AND sync SHEETS + COORDINATES (Full Sync for New).
       2. If Content EXISTS but IsActive=0 -> UPDATE IsActive=1 (Fast Activation).
       3. If Content checked to be Deselected -> UPDATE IsActive=0 (Fast Deactivation).
    */
    public async Task<ContentAuthoritySaveResult> SaveContentAuthorityAsync(ContentAuthoritySaveRequest request)
    {
        var result = new ContentAuthoritySaveResult();
        if (request == null) return result;

        await using var sourceConn = new SqlConnection(SourceConnStr);
        await sourceConn.OpenAsync();

        var clientWasOpen = _clientConn.State == ConnectionState.Open;
        if (!clientWasOpen) await _clientConn.OpenAsync();

        var existingClientIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rows = await _clientConn.QueryAsync<(int Id, string Name)>(
                "SELECT ContentId AS Id, ContentName AS Name FROM ContentMaster");
            foreach (var r in rows) existingClientIds[r.Name] = r.Id;
        }
        catch (Exception ex) { Console.WriteLine($"[ContentAuthority] existingClientIds load error: {ex.Message}"); }

        // Prep for potential bulk inserts (only if we have new rows)
        var sheetIdCols = await GetIdentityColumnsAsync(_clientConn, null, "ContentWiseKeylineSheetPlanning");
        var coordIdCols = await GetIdentityColumnsAsync(_clientConn, null, "ContentWiseKeyLineCoordinates");

        await using var transaction = (SqlTransaction)await _clientConn.BeginTransactionAsync();
        try
        {
            DataTable? dtSheet = null; DataTable? dtCoord = null;

            // 1. Handle Deselected (Fast Deactivate)
            foreach (var name in request.DeselectedContents)
            {
                if (existingClientIds.TryGetValue(name, out var id))
                {
                    await _clientConn.ExecuteAsync("UPDATE ContentMaster SET IsActive = 0 WHERE ContentId = @Id", new { Id = id }, transaction);
                    result.Deactivated++;
                    result.Processed++;
                }
            }

            // 2. Handle Selected (Activation or New Insertion)
            foreach (var name in request.SelectedContents)
            {
                if (existingClientIds.TryGetValue(name, out var id))
                {
                    // Case: ALREADY EXISTS -> Just ensure IsActive = 1 (Fast)
                    await _clientConn.ExecuteAsync("UPDATE ContentMaster SET IsActive = 1 WHERE ContentId = @Id", new { Id = id }, transaction);
                    result.Updated++;
                }
                else
                {
                    // Double-check directly in DB — existingClientIds may have failed to load
                    var existingId = await _clientConn.ExecuteScalarAsync<int?>(
                        "SELECT ContentId FROM ContentMaster WHERE ContentName = @N", new { N = name }, transaction);

                    if (existingId.HasValue)
                    {
                        // Content exists in DB but wasn't in our cache -> just activate, NO keyline sync
                        await _clientConn.ExecuteAsync(
                            "UPDATE ContentMaster SET IsActive = 1 WHERE ContentId = @Id",
                            new { Id = existingId.Value }, transaction);
                        result.Updated++;
                    }
                    else
                    {
                        // Case: TRULY NEW CONTENT -> INSERT + FULL DATA SYNC (keyline only for first time)
                        var sourceRow = await sourceConn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ContentMaster WHERE ContentName = @N", new { N = name });
                        if (sourceRow != null)
                        {
                            var srcDict = (IDictionary<string, object>)sourceRow;
                            srcDict["IsActive"] = 1;
                            var cols = srcDict.Keys.Where(k => !k.Equals("ContentId", StringComparison.OrdinalIgnoreCase)).ToList();
                            var sql = $"INSERT INTO ContentMaster ({string.Join(",", cols.Select(c => $"[{c}]"))}) VALUES ({string.Join(",", cols.Select(c => "@" + c))})";
                            var p = new DynamicParameters();
                            foreach (var c in cols) p.Add("@" + c, srcDict[c]);
                            await _clientConn.ExecuteAsync(sql, p, transaction);
                            result.Inserted++;

                            // SYNC CHILD TABLES — only for first-time content
                            var srcSheets = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @N", new { N = name })).ToList();
                            var srcCoords = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ContentWiseKeyLineCoordinates WHERE ContentType = @N", new { N = name })).ToList();

                            if (srcSheets.Count > 0)
                            {
                                dtSheet ??= BuildDataTable((IDictionary<string, object>)srcSheets[0], sheetIdCols);
                                foreach (var r in srcSheets) { AppendRow(dtSheet, (IDictionary<string, object>)r, sheetIdCols); result.ChildRowsInserted++; }
                            }
                            if (srcCoords.Count > 0)
                            {
                                dtCoord ??= BuildDataTable((IDictionary<string, object>)srcCoords[0], coordIdCols);
                                foreach (var r in srcCoords) { AppendRow(dtCoord, (IDictionary<string, object>)r, coordIdCols); result.ChildRowsInserted++; }
                            }
                        }
                    }
                }
                result.Processed++;
            }

            // Execute bulk inserts for children (if any new contents had data)
            if (dtSheet is { Rows.Count: > 0 }) await BulkInsertAsync(_clientConn, transaction, "ContentWiseKeylineSheetPlanning", dtSheet);
            if (dtCoord is { Rows.Count: > 0 }) await BulkInsertAsync(_clientConn, transaction, "ContentWiseKeyLineCoordinates", dtCoord);

            await transaction.CommitAsync();
            result.Message = $"Access updated. New content synced: {result.Inserted}, Existing updated/activated: {result.Updated}, Deactivated: {result.Deactivated}.";
            return result;
        }
        catch { await transaction.RollbackAsync(); throw; }
        finally { if (!clientWasOpen) await _clientConn.CloseAsync(); }
    }

    // ── UPDATE TECH DETAILS (Refresh Logic) ─────────────────────
    public async Task<ContentAuthoritySaveResult> UpdateContentDetailsAsync(List<string> contentNames)
    {
        var result = new ContentAuthoritySaveResult();
        if (contentNames == null || contentNames.Count == 0) return result;

        await using var sourceConn = new SqlConnection(SourceConnStr);
        await sourceConn.OpenAsync();

        var clientWasOpen = _clientConn.State == ConnectionState.Open;
        if (!clientWasOpen) await _clientConn.OpenAsync();

        var sheetIdCols = await GetIdentityColumnsAsync(_clientConn, null, "ContentWiseKeylineSheetPlanning");
        var coordIdCols = await GetIdentityColumnsAsync(_clientConn, null, "ContentWiseKeyLineCoordinates");

        var clientIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rows = await _clientConn.QueryAsync<dynamic>("SELECT ContentId, ContentName FROM ContentMaster");
            foreach (var r in rows) clientIds[(string)r.ContentName] = (int)r.ContentId;
        }
        catch { }

        await using var transaction = (SqlTransaction)await _clientConn.BeginTransactionAsync();
        try
        {
            DataTable? dtSheet = null; DataTable? dtCoord = null;

            foreach (var name in contentNames)
            {
                if (!clientIds.TryGetValue(name, out var cid)) continue;

                var srcRow = await sourceConn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ContentMaster WHERE ContentName = @N", new { N = name });
                if (srcRow != null)
                {
                    var srcD = (IDictionary<string, object>)srcRow;
                    var cols = srcD.Keys.Where(k => !k.Equals("ContentId", StringComparison.OrdinalIgnoreCase) && !k.Equals("ContentName", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (cols.Count > 0)
                    {
                        var sql = $"UPDATE ContentMaster SET {string.Join(",", cols.Select(c => $"[{c}] = @{c}"))} WHERE ContentId = @Id";
                        var p = new DynamicParameters();
                        p.Add("@Id", cid);
                        foreach (var c in cols) p.Add("@" + c, srcD[c]);
                        await _clientConn.ExecuteAsync(sql, p, transaction);
                    }
                }

                result.ChildRowsDeleted += await _clientConn.ExecuteAsync("DELETE FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @N", new { N = name }, transaction);
                result.ChildRowsDeleted += await _clientConn.ExecuteAsync("DELETE FROM ContentWiseKeyLineCoordinates WHERE ContentType = @N", new { N = name }, transaction);

                var srcSheets = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @N", new { N = name })).ToList();
                var srcCoords = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ContentWiseKeyLineCoordinates WHERE ContentType = @N", new { N = name })).ToList();

                if (srcSheets.Count > 0)
                {
                    dtSheet ??= BuildDataTable((IDictionary<string, object>)srcSheets[0], sheetIdCols);
                    foreach (var r in srcSheets) { AppendRow(dtSheet, (IDictionary<string, object>)r, sheetIdCols); result.ChildRowsInserted++; }
                }
                if (srcCoords.Count > 0)
                {
                    dtCoord ??= BuildDataTable((IDictionary<string, object>)srcCoords[0], coordIdCols);
                    foreach (var r in srcCoords) { AppendRow(dtCoord, (IDictionary<string, object>)r, coordIdCols); result.ChildRowsInserted++; }
                }
                result.Processed++;
            }

            if (dtSheet is { Rows.Count: > 0 }) await BulkInsertAsync(_clientConn, transaction, "ContentWiseKeylineSheetPlanning", dtSheet);
            if (dtCoord is { Rows.Count: > 0 }) await BulkInsertAsync(_clientConn, transaction, "ContentWiseKeyLineCoordinates", dtCoord);

            await transaction.CommitAsync();
            result.Message = $"Tech details refreshed for {result.Processed} contents. Child rows inserted: {result.ChildRowsInserted}.";
            return result;
        }
        catch { await transaction.RollbackAsync(); throw; }
        finally { if (!clientWasOpen) await _clientConn.CloseAsync(); }
    }

    // ── UPDATE KEYLINE DETAILS (Only ContentWiseKeylineCoordinates + ContentWiseKeylineSheetPlanning) ──
    public async Task<ContentAuthoritySaveResult> UpdateKeylineDetailsAsync(List<string> contentNames)
    {
        var result = new ContentAuthoritySaveResult();
        if (contentNames == null || contentNames.Count == 0) return result;

        await using var sourceConn = new SqlConnection(SourceConnStr);
        await sourceConn.OpenAsync();

        var clientWasOpen = _clientConn.State == ConnectionState.Open;
        if (!clientWasOpen) await _clientConn.OpenAsync();

        var sheetIdCols = await GetIdentityColumnsAsync(_clientConn, null, "ContentWiseKeylineSheetPlanning");
        var coordIdCols = await GetIdentityColumnsAsync(_clientConn, null, "ContentWiseKeyLineCoordinates");

        await using var transaction = (SqlTransaction)await _clientConn.BeginTransactionAsync();
        try
        {
            DataTable? dtSheet = null; DataTable? dtCoord = null;

            foreach (var name in contentNames)
            {
                result.ChildRowsDeleted += await _clientConn.ExecuteAsync("DELETE FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @N", new { N = name }, transaction);
                result.ChildRowsDeleted += await _clientConn.ExecuteAsync("DELETE FROM ContentWiseKeyLineCoordinates WHERE ContentType = @N", new { N = name }, transaction);

                var srcSheets = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @N", new { N = name })).ToList();
                var srcCoords = (await sourceConn.QueryAsync<dynamic>("SELECT * FROM ContentWiseKeyLineCoordinates WHERE ContentType = @N", new { N = name })).ToList();

                if (srcSheets.Count > 0)
                {
                    dtSheet ??= BuildDataTable((IDictionary<string, object>)srcSheets[0], sheetIdCols);
                    foreach (var r in srcSheets) { AppendRow(dtSheet, (IDictionary<string, object>)r, sheetIdCols); result.ChildRowsInserted++; }
                }
                if (srcCoords.Count > 0)
                {
                    dtCoord ??= BuildDataTable((IDictionary<string, object>)srcCoords[0], coordIdCols);
                    foreach (var r in srcCoords) { AppendRow(dtCoord, (IDictionary<string, object>)r, coordIdCols); result.ChildRowsInserted++; }
                }
                result.Processed++;
            }

            if (dtSheet is { Rows.Count: > 0 }) await BulkInsertAsync(_clientConn, transaction, "ContentWiseKeylineSheetPlanning", dtSheet);
            if (dtCoord is { Rows.Count: > 0 }) await BulkInsertAsync(_clientConn, transaction, "ContentWiseKeyLineCoordinates", dtCoord);

            await transaction.CommitAsync();
            result.Message = $"Keyline details refreshed for {result.Processed} contents. Child rows inserted: {result.ChildRowsInserted}.";
            return result;
        }
        catch { await transaction.RollbackAsync(); throw; }
        finally { if (!clientWasOpen) await _clientConn.CloseAsync(); }
    }

    private static async Task BulkInsertAsync(SqlConnection conn, SqlTransaction tx, string table, DataTable dt)
    {
        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = table, BatchSize = 5000, BulkCopyTimeout = 120 };
        foreach (DataColumn c in dt.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(dt);
    }

    private static DataTable BuildDataTable(IDictionary<string, object> row, HashSet<string> skip)
    {
        var dt = new DataTable();
        foreach (var k in row.Keys) if (!skip.Contains(k)) dt.Columns.Add(k, row[k]?.GetType() ?? typeof(object));
        return dt;
    }

    private static void AppendRow(DataTable dt, IDictionary<string, object> d, HashSet<string> skip)
    {
        var r = dt.NewRow();
        foreach (DataColumn c in dt.Columns) r[c.ColumnName] = d.TryGetValue(c.ColumnName, out var v) ? (v ?? DBNull.Value) : DBNull.Value;
        dt.Rows.Add(r);
    }

    private static async Task<HashSet<string>> GetIdentityColumnsAsync(SqlConnection c, SqlTransaction? tx, string t)
    {
        var cols = await c.QueryAsync<string>("SELECT c.name FROM sys.columns c JOIN sys.objects o ON c.object_id = o.object_id WHERE o.name = @T AND o.type = 'U' AND c.is_identity = 1", new { T = t }, tx);
        return new HashSet<string>(cols, StringComparer.OrdinalIgnoreCase);
    }
}
