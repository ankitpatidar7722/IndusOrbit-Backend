using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Point (task) data access for the Point Management module — reads/writes the
/// IndusTaskManagement database. Ports the Points-related methods from
/// DataAccess.vb (dashboard stats, filtered grids). Write/workflow methods are
/// added per page in later phases.
/// </summary>
public sealed class PointRepository
{
    private readonly Db _db;
    public PointRepository(Db db) => _db = db;

    // Shared SELECT for point grid rows — joins the lookup names in one shot.
    // AssignedByName/ToDoDate/AudioFilePath mirror TMS's DashboardDeveloper grid
    // (CreatedByFullName = who created the Ticket i.e. "assigned by"; AudioFilePath = the
    // point's most recent .webm voice-note attachment, if any).
    private const string PointSelect = @"
        SELECT  p.PointID, p.Title, p.Summary, p.Module, p.SubModule, p.Description, p.Status, p.Priority, p.Category, p.Complexity,
                p.CustomerID, c.CompanyName AS CustomerName,
                p.ProductID, pr.ProductName,
                p.TicketID, t.TicketNo, tb.FullName AS AssignedByName,
                p.ReportedByID, ru.FullName AS ReportedByName,
                p.AssignedToID, au.FullName AS AssignedToName,
                p.DateCreated, p.ExpectedDate, p.StartDate, p.DateCompleted, p.DateClosed, p.ToDoDate,
                p.ExpectedMinutes, p.TotalTimeSpent, p.PauseTimeMinutes,
                p.IsVerified, p.VerificationStatus, p.IsDeveloperPaused, p.SortOrder,
                aud.FilePath AS AudioFilePath, cr.Id AS TrackerChangeRequestId
        FROM dbo.Points p
        LEFT JOIN dbo.Customers c ON c.CustomerID = p.CustomerID
        LEFT JOIN dbo.Products  pr ON pr.ProductID = p.ProductID
        LEFT JOIN dbo.Tickets   t ON t.TicketID = p.TicketID
        LEFT JOIN dbo.Users     tb ON tb.UserID = t.CreatedByID
        LEFT JOIN dbo.Users     ru ON ru.UserID = p.ReportedByID
        LEFT JOIN dbo.Users     au ON au.UserID = p.AssignedToID
        OUTER APPLY (SELECT TOP 1 FilePath FROM dbo.PointAttachments pa WHERE pa.PointID = p.PointID AND pa.FilePath LIKE '%.webm' AND ISNULL(pa.IsDeletedTransaction,0) = 0 ORDER BY pa.AttachmentID DESC) aud
        OUTER APPLY (SELECT TOP 1 Id FROM app.ChangeRequests x WHERE x.PointID = p.PointID AND ISNULL(x.IsDeletedTransaction,0) = 0 ORDER BY x.Id DESC) cr ";

    /// <summary>Admin dashboard status counters (mirrors DashboardAdmin.LoadDashboardStats).</summary>
    public async Task<AdminDashboardStats> GetAdminStatsAsync(DateTime? from, DateTime? to, int? devId, int? custId)
    {
        const string sql = @"
            SELECT
              COUNT(*) AS Total,
              SUM(CASE WHEN Status='Queue' THEN 1 ELSE 0 END) AS Queue,
              SUM(CASE WHEN Status='Assigned' THEN 1 ELSE 0 END) AS Assigned,
              SUM(CASE WHEN Status='In Progress' THEN 1 ELSE 0 END) AS InProgress,
              SUM(CASE WHEN Status='DevCompleted' THEN 1 ELSE 0 END) AS DevCompleted,
              SUM(CASE WHEN Status='PendingSupport' THEN 1 ELSE 0 END) AS PendingSupport,
              SUM(CASE WHEN Status='SupportVerified' THEN 1 ELSE 0 END) AS SupportVerified,
              SUM(CASE WHEN Status='PendingMerge' THEN 1 ELSE 0 END) AS PendingMerge,
              SUM(CASE WHEN Status='PendingQC' THEN 1 ELSE 0 END) AS PendingQC,
              SUM(CASE WHEN Status='In-Testing' THEN 1 ELSE 0 END) AS InTesting,
              SUM(CASE WHEN Status='Testing-Completed' THEN 1 ELSE 0 END) AS TestingCompleted,
              SUM(CASE WHEN Status='ReOpened' THEN 1 ELSE 0 END) AS ReOpened,
              SUM(CASE WHEN Status='Hold' THEN 1 ELSE 0 END) AS Hold,
              SUM(CASE WHEN Status='Reject' THEN 1 ELSE 0 END) AS Reject,
              SUM(CASE WHEN Status='Closed' THEN 1 ELSE 0 END) AS Closed,
              SUM(CASE WHEN ISNULL(ExpectedMinutes,0) > 0 AND ISNULL(TotalTimeSpent,0) > ExpectedMinutes THEN 1 ELSE 0 END) AS Delayed,
              SUM(CASE WHEN Status NOT IN ('PendingQC','In-Testing','Testing-Completed','Closed') THEN 1 ELSE 0 END) AS [Open]
            FROM dbo.Points
            WHERE (@from IS NULL OR DateCreated >= @from)
              AND (@to   IS NULL OR DateCreated < DATEADD(DAY,1,@to))
              AND (@devId  IS NULL OR AssignedToID = @devId)
              AND (@custId IS NULL OR CustomerID = @custId)";
        await using var db = await _db.OpenTmsAsync();
        return await db.QuerySingleAsync<AdminDashboardStats>(sql, new { from, to, devId, custId });
    }

    /// <summary>Filtered points for a grid (mirrors DataAccess.GetFilteredPointsForGrid).
    /// When <paramref name="status"/> is null/"All" no status filter is applied.
    /// <paramref name="reportedBy"/> scopes to a support user's own reported points.</summary>
    public async Task<IEnumerable<PointGridRow>> GetFilteredAsync(
        string? status, DateTime? from, DateTime? to, int? devId, int? custId, int? reportedBy = null)
    {
        var sql = PointSelect + @"
            WHERE (@status IS NULL OR @status = 'All' OR p.Status = @status)
              AND (@from IS NULL OR p.DateCreated >= @from)
              AND (@to   IS NULL OR p.DateCreated < DATEADD(DAY,1,@to))
              AND (@devId  IS NULL OR p.AssignedToID = @devId)
              AND (@custId IS NULL OR p.CustomerID = @custId)
              AND (@reportedBy IS NULL OR p.ReportedByID = @reportedBy)
            ORDER BY p.DateCreated DESC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql, new { status, from, to, devId, custId, reportedBy });
    }

    /// <summary>Verified, unassigned queue points (mirrors GetQueuePointsForGrid).</summary>
    public async Task<IEnumerable<PointGridRow>> GetQueueAsync()
    {
        var sql = PointSelect + @"
            WHERE p.Status = 'Queue' AND p.IsVerified = 1 AND p.TicketID IS NULL
            ORDER BY p.Priority DESC, p.DateCreated";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql);
    }

    /// <summary>Triage queue by verification state (mirrors GetAllPointsForVerification:
    /// 0 = pending/active, 2 = un-active). Verified (1) points move to the Assign page.</summary>
    public async Task<IEnumerable<PointGridRow>> GetVerificationQueueAsync(int verificationStatus)
    {
        var sql = PointSelect + @"
            WHERE p.VerificationStatus = @verificationStatus AND p.Status = 'Queue'
            ORDER BY p.DateCreated DESC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql, new { verificationStatus });
    }

    /// <summary>A developer's board — every point ever assigned to them, ANY status (mirrors TMS
    /// DashboardDeveloper's "STATUS FILTER REMOVED TO SHOW ALL POINTS" — the All/Assigned/In Progress
    /// buttons on the page are a client-side filter over this same full set, not separate queries).
    /// <paramref name="devId"/> = null means no assignee filter (admin sees every developer's board).</summary>
    public async Task<IEnumerable<PointGridRow>> GetDeveloperBoardAsync(int? devId)
    {
        var sql = PointSelect + @"
            WHERE (@devId IS NULL OR p.AssignedToID = @devId)
            ORDER BY CASE p.Status WHEN 'SupportVerified' THEN 0 WHEN 'PendingMerge' THEN 1 ELSE 2 END,
                     p.SortOrder ASC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql, new { devId });
    }

    /// <summary>A tester's QC queue (mirrors GetPointsForTesterDashboard).</summary>
    public async Task<IEnumerable<PointGridRow>> GetTesterQueueAsync()
    {
        var sql = PointSelect + @"
            WHERE p.Status IN ('PendingQC','In-Testing','Testing-Completed','ReOpened')
            ORDER BY CASE p.Status WHEN 'PendingQC' THEN 0 WHEN 'In-Testing' THEN 1 ELSE 2 END, p.DateSentToQC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql);
    }

    /// <summary>A support user's queue — their reported points awaiting support verification.
    /// <paramref name="supportUserId"/> = null means no reporter filter (admin sees everyone's queue).</summary>
    public async Task<IEnumerable<PointGridRow>> GetSupportQueueAsync(int? supportUserId)
    {
        var sql = PointSelect + @"
            WHERE p.Status = 'PendingSupport' AND (@supportUserId IS NULL OR p.ReportedByID = @supportUserId)
            ORDER BY p.DateSentToQC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql, new { supportUserId });
    }

    /// <summary>Points awaiting merge review (mirrors GetPointsForMerge).</summary>
    public async Task<IEnumerable<PointGridRow>> GetMergeQueueAsync()
    {
        var sql = PointSelect + @"
            WHERE p.Status = 'PendingMerge'
            ORDER BY p.DateSentToMerge, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointGridRow>(sql);
    }

    /// <summary>Full detail for the action drawer.</summary>
    public async Task<PointDetail?> GetDetailAsync(int pointId)
    {
        const string sql = @"
            SELECT  p.PointID, p.Title, p.Summary, p.Description, p.Status, p.Priority, p.Category, p.Complexity,
                    p.Module, p.SubModule,
                    c.CompanyName AS CustomerName, pr.ProductName, t.TicketNo,
                    au.FullName AS AssignedToName, ru.FullName AS ReportedByName,
                    p.ExpectedMinutes, p.TotalTimeSpent, p.PauseTimeMinutes, p.IsDeveloperPaused,
                    p.DateCreated, p.StartDate, p.DateCompleted, p.ExpectedDate,
                    p.DeveloperRemark, p.TesterRemark, p.SupportRemark, p.AdminRemark
            FROM dbo.Points p
            LEFT JOIN dbo.Customers c ON c.CustomerID = p.CustomerID
            LEFT JOIN dbo.Products  pr ON pr.ProductID = p.ProductID
            LEFT JOIN dbo.Tickets   t ON t.TicketID = p.TicketID
            LEFT JOIN dbo.Users     au ON au.UserID = p.AssignedToID
            LEFT JOIN dbo.Users     ru ON ru.UserID = p.ReportedByID
            WHERE p.PointID = @pointId";
        await using var db = await _db.OpenTmsAsync();
        return await db.QuerySingleOrDefaultAsync<PointDetail>(sql, new { pointId });
    }

    /// <summary>The QC-cycle timeline for a point (mirrors GetPointHistory).</summary>
    public async Task<IEnumerable<PointHistoryRow>> GetHistoryAsync(int pointId)
    {
        const string sql = @"
            SELECT  h.HistoryID, d.FullName AS DeveloperName, te.FullName AS TesterName, s.FullName AS SupportName,
                    h.CycleStatus, h.StartDate, h.CompleteDate, h.TimeSpentMinutes, h.ExtraTimeMinutes,
                    h.DeveloperRemark, h.TesterRemark, h.SupportRemark
            FROM dbo.PointHistory h
            LEFT JOIN dbo.Users d  ON d.UserID  = h.DeveloperID
            LEFT JOIN dbo.Users te ON te.UserID = h.TesterID
            LEFT JOIN dbo.Users s  ON s.UserID  = h.SupportID
            WHERE h.PointID = @pointId
            ORDER BY h.HistoryID";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<PointHistoryRow>(sql, new { pointId });
    }
}
