using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Subscription "Exceed Days". CURRENT values live directly on the subscription row in the shared
/// control DB — columns ERPExceedDays/ERPExceedDate + CloudExceedDays/CloudExceedDate on
/// dbo.Indus_Company_Authentication_For_Web_Modules. The ExceedDate columns are always kept in sync
/// as PaymentDueDate + ExceedDays (Payment Due itself is never touched). The CHANGE HISTORY stays in
/// OUR app DB (app.ClientSubscriptionExceedHistory), keyed by CompanyUserID — the same row key the
/// main subscription update uses.
/// </summary>
public sealed class ClientExceedRepository
{
    private readonly Db _db;
    public ClientExceedRepository(Db db) => _db = db;

    private const string Table = "dbo.Indus_Company_Authentication_For_Web_Modules";

    private sealed class ExceedRow { public int? ERPExceedDays { get; set; } public int? CloudExceedDays { get; set; } }

    /// <summary>Current ERP + Cloud exceed days (from the control row) + change history (from the app DB).</summary>
    public async Task<ClientExceedResponse> GetAsync(string clientCode)
    {
        var resp = new ClientExceedResponse();

        using (var ctrl = await _db.OpenControlAsync())
        {
            var row = await ctrl.QueryFirstOrDefaultAsync<ExceedRow>(
                $"SELECT ERPExceedDays, CloudExceedDays FROM {Table} WHERE CompanyUserID=@clientCode",
                new { clientCode });
            var erpDays = row?.ERPExceedDays ?? 0;
            var cloudDays = row?.CloudExceedDays ?? 0;
            resp.Erp = new ExceedEntry { Days = erpDays, Active = erpDays > 0 };
            resp.Cloud = new ExceedEntry { Days = cloudDays, Active = cloudDays > 0 };
        }

        await using (var app = await _db.OpenAsync())
        {
            var history = (await app.QueryAsync<ExceedHistoryRow>(
                @"SELECT h.Kind, h.OldDays, h.NewDays, h.ChangedBy,
                         u.FullName AS ChangedByName, h.ChangedDate
                  FROM app.ClientSubscriptionExceedHistory h
                  LEFT JOIN app.Users u ON u.UserId = h.ChangedBy
                  WHERE h.ClientCode=@clientCode
                  ORDER BY h.ChangedDate DESC",
                new { clientCode })).ToList();
            // ChangedDate is stored in UTC — tag the Kind so JSON emits a trailing 'Z' and the
            // browser renders it in the viewer's local timezone (else a UTC value shows as-is).
            foreach (var h in history) h.ChangedDate = DateTime.SpecifyKind(h.ChangedDate, DateTimeKind.Utc);
            resp.History = history;
        }
        return resp;
    }

    /// <summary>Write ERP/Cloud exceed days onto the control row (recomputing ExceedDate = PaymentDue + days),
    /// and log an app-DB history row whenever the day count changes.</summary>
    public async Task SaveAsync(string clientCode, ClientExceedSaveRequest req, int? userId)
    {
        var histLog = new List<(string Kind, int Old, int New)>();

        using (var ctrl = await _db.OpenControlAsync())
        {
            if (req.Erp is not null) await UpsertKindAsync(ctrl, clientCode, "ERP", req.Erp, histLog);
            if (req.Cloud is not null) await UpsertKindAsync(ctrl, clientCode, "Cloud", req.Cloud, histLog);
        }

        if (histLog.Count > 0)
        {
            await using var app = await _db.OpenAsync();
            foreach (var h in histLog)
                await app.ExecuteAsync(
                    @"INSERT INTO app.ClientSubscriptionExceedHistory (ClientCode, Kind, OldDays, NewDays, ChangedBy, ChangedDate)
                      VALUES (@clientCode, @kind, @oldDays, @newDays, @userId, SYSUTCDATETIME())",
                    new { clientCode, kind = h.Kind, oldDays = h.Old, newDays = h.New, userId });
        }
    }

    private static async Task UpsertKindAsync(SqlConnection ctrl, string clientCode, string kind, ExceedEntry e, List<(string Kind, int Old, int New)> histLog)
    {
        // Column names are fixed constants (never user input) → safe to interpolate.
        var daysCol = kind == "Cloud" ? "CloudExceedDays" : "ERPExceedDays";
        var dateCol = kind == "Cloud" ? "CloudExceedDate" : "ERPExceedDate";
        var dueCol = kind == "Cloud" ? "CloudPaymentDueDate" : "PaymentDueDate";

        var old = await ctrl.ExecuteScalarAsync<int?>(
            $"SELECT {daysCol} FROM {Table} WHERE CompanyUserID=@clientCode", new { clientCode }) ?? 0;
        var newDays = e.Active ? e.Days : 0;   // "off" → 0 days → NULL exceed date

        await ctrl.ExecuteAsync(
            $@"UPDATE {Table}
                  SET {daysCol}=@newDays,
                      {dateCol}=CASE WHEN @newDays > 0 THEN DATEADD(DAY, @newDays, {dueCol}) ELSE NULL END
                WHERE CompanyUserID=@clientCode",
            new { clientCode, newDays });

        if (old != newDays) histLog.Add((kind, old, newDays));
    }
}
