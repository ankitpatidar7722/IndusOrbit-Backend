using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Time / Customer-Progress reporting (mirrors GetTimeReportData &amp;
/// GetCustomerProgressData). One filtered query serves both pages.
/// </summary>
public sealed class TmsReportRepository
{
    private readonly Db _db;
    public TmsReportRepository(Db db) => _db = db;

    public async Task<IEnumerable<TimeReportRow>> GetReportAsync(int? custId, int? devId, DateTime? from, DateTime? to)
    {
        const string sql = @"
            SELECT  p.PointID,
                    c.CompanyName  AS Customer,
                    pr.ProductName AS Product,
                    p.Summary      AS Module,
                    ru.FullName    AS ReportedBy,
                    au.FullName    AS AssignedTo,
                    p.Status, p.Priority,
                    p.ExpectedMinutes, p.TotalTimeSpent, p.PauseTimeMinutes,
                    CASE WHEN ISNULL(p.ExpectedMinutes,0) > 0 AND ISNULL(p.TotalTimeSpent,0) > p.ExpectedMinutes
                         THEN p.TotalTimeSpent - p.ExpectedMinutes ELSE 0 END AS DelayMinutes,
                    p.SupportTimeSpent,
                    p.DateCreated, p.ExpectedDate, p.DateClosed
            FROM dbo.Points p
            LEFT JOIN dbo.Customers c ON c.CustomerID = p.CustomerID
            LEFT JOIN dbo.Products  pr ON pr.ProductID = p.ProductID
            LEFT JOIN dbo.Users     ru ON ru.UserID = p.ReportedByID
            LEFT JOIN dbo.Users     au ON au.UserID = p.AssignedToID
            WHERE p.TicketID IS NOT NULL
              AND (@custId IS NULL OR p.CustomerID = @custId)
              AND (@devId  IS NULL OR p.AssignedToID = @devId)
              AND (@from IS NULL OR p.DateCreated >= @from)
              AND (@to   IS NULL OR p.DateCreated < DATEADD(DAY,1,@to))
            ORDER BY p.DateCreated DESC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<TimeReportRow>(sql, new { custId, devId, from, to });
    }
}
