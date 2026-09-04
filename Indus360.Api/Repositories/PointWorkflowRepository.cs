using System.Data;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Write-side / workflow operations on Points (create, triage, status
/// transitions, timers, QC). Ports the corresponding DataAccess.vb methods.
/// This first slice covers creation + admin triage; timer/QC transitions are
/// added as their pages are migrated.
/// </summary>
public sealed class PointWorkflowRepository
{
    private readonly Db _db;
    public PointWorkflowRepository(Db db) => _db = db;

    // ---------------- Create (AddPoint) ----------------
    public async Task<int> InsertPointAsync(NewPointRequest p)
    {
        await using var db = await _db.OpenTmsAsync();

        // The Add Point form sources the customer from /clients (by company name). Resolve it to a
        // TMS dbo.Customers CustomerID (find-or-create by name) so the point + its workflow stay valid.
        if (p.CustomerID <= 0 && !string.IsNullOrWhiteSpace(p.CustomerName))
        {
            var name = p.CustomerName.Trim();
            p.CustomerID = await db.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 CustomerID FROM dbo.Customers WHERE CompanyName=@n ORDER BY CustomerID", new { n = name }) ?? 0;
            if (p.CustomerID <= 0)
                p.CustomerID = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO dbo.Customers (CustomerName, CompanyName, IsActive, DateCreated) OUTPUT INSERTED.CustomerID VALUES (@n, @n, 1, GETDATE())",
                    new { n = name });
        }

        // Title is OPTIONAL — keep it exactly as entered; a blank Title stays blank (never copied
        // from the Description). Normalise null→"" so the NOT NULL column is satisfied.
        p.Title = (p.Title ?? "").Trim();

        const string sql = @"
            INSERT INTO dbo.Points
                (Title, Summary, Module, SubModule, Description, CustomerID, ProductID, ReportedByID,
                 Status, Priority, Category, Complexity, DateCreated,
                 VerificationStatus, IsVerified, IsDeveloperPaused, PauseTimeMinutes)
            OUTPUT INSERTED.PointID
            VALUES
                (@Title, @Summary, @Module, @SubModule, @Description, @CustomerID, @ProductID, @ReportedByID,
                 'Queue', @Priority, @Category, @Complexity, GETDATE(),
                 0, 0, 0, 0);";
        return await db.ExecuteScalarAsync<int>(sql, p);
    }

    // ---------------- Edit / Delete a Queue point (Manage Points) ----------------
    /// <summary>Update an editable (Queue-status) point. Only Queue points can be edited — once a point
    /// is assigned / in-flight, edits are blocked. Resolves CustomerName→CustomerID like create.</summary>
    public async Task<(bool ok, string? message)> UpdateQueuePointAsync(int pointId, NewPointRequest p)
    {
        await using var db = await _db.OpenTmsAsync();

        var status = await db.ExecuteScalarAsync<string?>(
            "SELECT Status FROM dbo.Points WHERE PointID=@pointId AND ISNULL(IsDeletedTransaction,0)=0", new { pointId });
        if (status is null) return (false, "Point not found.");
        if (!string.Equals(status.Trim(), "Queue", StringComparison.OrdinalIgnoreCase))
            return (false, $"Only Queue points can be edited (this point is '{status}').");

        // Resolve the customer the same way create does (find-or-create by company name).
        if (p.CustomerID <= 0 && !string.IsNullOrWhiteSpace(p.CustomerName))
        {
            var name = p.CustomerName.Trim();
            p.CustomerID = await db.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 CustomerID FROM dbo.Customers WHERE CompanyName=@n ORDER BY CustomerID", new { n = name }) ?? 0;
            if (p.CustomerID <= 0)
                p.CustomerID = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO dbo.Customers (CustomerName, CompanyName, IsActive, DateCreated) OUTPUT INSERTED.CustomerID VALUES (@n, @n, 1, GETDATE())",
                    new { n = name });
        }

        p.Title = (p.Title ?? "").Trim(); // optional — blank stays blank, never derived from Description

        var rows = await db.ExecuteAsync(@"
            UPDATE dbo.Points SET
                Title=@Title, Module=@Module, SubModule=@SubModule, Description=@Description,
                CustomerID = CASE WHEN @CustomerID > 0 THEN @CustomerID ELSE CustomerID END,
                ProductID  = CASE WHEN @ProductID  > 0 THEN @ProductID  ELSE ProductID  END,
                Priority=@Priority, Category=@Category, Complexity=@Complexity
            WHERE PointID=@pointId AND Status='Queue' AND ISNULL(IsDeletedTransaction,0)=0",
            new { pointId, p.Title, p.Module, p.SubModule, p.Description, p.CustomerID, p.ProductID, p.Priority, p.Category, p.Complexity });
        return rows > 0 ? (true, null) : (false, "Point could not be updated.");
    }

    /// <summary>Soft-delete a Queue point (IsDeletedTransaction=1). Blocked once the point leaves Queue.</summary>
    public async Task<(bool ok, string? message)> DeleteQueuePointAsync(int pointId)
    {
        await using var db = await _db.OpenTmsAsync();
        var status = await db.ExecuteScalarAsync<string?>(
            "SELECT Status FROM dbo.Points WHERE PointID=@pointId AND ISNULL(IsDeletedTransaction,0)=0", new { pointId });
        if (status is null) return (false, "Point not found or already deleted.");
        if (!string.Equals(status.Trim(), "Queue", StringComparison.OrdinalIgnoreCase))
            return (false, $"Only Queue points can be deleted (this point is '{status}').");
        var rows = await db.ExecuteAsync(
            "UPDATE dbo.Points SET IsDeletedTransaction=1 WHERE PointID=@pointId AND Status='Queue'", new { pointId });
        return rows > 0 ? (true, null) : (false, "Point could not be deleted.");
    }

    // ---------------- Triage (VerifyTicket) ----------------
    /// <summary>Approve a queue point for assignment (mirrors MarkPointVerified).</summary>
    public async Task<bool> MarkVerifiedAsync(int pointId)
    {
        const string sql = @"
            UPDATE dbo.Points
            SET IsVerified = 1, VerificationStatus = 1, DateTicketVerified = GETDATE()
            WHERE PointID = @pointId;";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteAsync(sql, new { pointId }) > 0;
    }

    /// <summary>Close a point (admin action from Manage Points) — set Closed + close/complete dates.</summary>
    public async Task<bool> ClosePointAsync(int pointId)
    {
        const string sql = @"
            UPDATE dbo.Points
            SET Status = 'Closed', DateClosed = GETDATE(), DateCompleted = ISNULL(DateCompleted, GETDATE())
            WHERE PointID = @pointId AND Status <> 'Closed';";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteAsync(sql, new { pointId }) > 0;
    }

    /// <summary>Reject a queue point during triage (mirrors UpdatePointAsUnActive).</summary>
    public async Task<bool> MarkUnActiveAsync(int pointId, string? adminRemark)
    {
        const string sql = @"
            UPDATE dbo.Points
            SET VerificationStatus = 2, AdminRemark = @adminRemark
            WHERE PointID = @pointId;";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteAsync(sql, new { pointId, adminRemark }) > 0;
    }

    /// <summary>Re-activate an un-active (rejected) point — put it back in the pending verification
    /// queue (VerificationStatus 2 → 0). Lets an accidental "Un-Active" be undone from Verify Tickets.</summary>
    public async Task<bool> ReactivateAsync(int pointId)
    {
        const string sql = @"
            UPDATE dbo.Points
            SET VerificationStatus = 0, AdminRemark = NULL
            WHERE PointID = @pointId AND VerificationStatus = 2;";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteAsync(sql, new { pointId }) > 0;
    }

    // ---------------- Developer timer (mirrors DataAccess Time Tracking region) ----------------
    private static Task LogAsync(SqlConnection db, int pointId, string action, int userId, string? remarks = null)
        => db.ExecuteAsync(
            "INSERT INTO dbo.PointTimeLog (PointID, Action, ActionTime, PerformedByID, Remarks) VALUES (@pointId,@action,GETDATE(),@userId,@remarks)",
            new { pointId, action, userId, remarks });

    private static Task CalcTotalAsync(SqlConnection db, int pointId)
        => db.ExecuteAsync("sp_CalculatePointTotalTime", new { PointID = pointId }, commandType: CommandType.StoredProcedure);

    /// <summary>Start (or resume a fresh cycle of) the developer timer.</summary>
    public async Task StartTimerAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync(
            "UPDATE dbo.Points SET StartDate = ISNULL(StartDate, GETDATE()), Status = 'In Progress', IsDeveloperPaused = 0, LastActionTime = GETDATE() WHERE PointID = @pointId",
            new { pointId });
        await LogAsync(db, pointId, "Start", userId);
    }

    public async Task PauseTimerAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Points SET IsDeveloperPaused = 1, LastActionTime = GETDATE() WHERE PointID = @pointId", new { pointId });
        await LogAsync(db, pointId, "Pause", userId);
        await CalcTotalAsync(db, pointId);
    }

    public async Task ResumeTimerAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Points SET IsDeveloperPaused = 0, LastActionTime = GETDATE() WHERE PointID = @pointId", new { pointId });
        await LogAsync(db, pointId, "Resume", userId);
    }

    /// <summary>Complete the developer round (mirrors CompletePointTimer: auto-resume if paused,
    /// mark DevCompleted, recompute total, log ExtraTime if over the estimate).</summary>
    public async Task CompleteTimerAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        var paused = await db.ExecuteScalarAsync<bool>("SELECT ISNULL(IsDeveloperPaused,0) FROM dbo.Points WHERE PointID = @pointId", new { pointId });
        if (paused) await LogAsync(db, pointId, "Resume", userId); // keep Start/Resume→Complete pairs balanced for the SP

        await db.ExecuteAsync(
            "UPDATE dbo.Points SET DateCompleted = GETDATE(), Status = 'DevCompleted', IsDeveloperPaused = 0, LastActionTime = GETDATE() WHERE PointID = @pointId",
            new { pointId });
        await LogAsync(db, pointId, "Complete", userId);
        await CalcTotalAsync(db, pointId);

        var total = await db.ExecuteScalarAsync<int>("SELECT ISNULL(TotalTimeSpent,0) FROM dbo.Points WHERE PointID = @pointId", new { pointId });
        var expected = await db.ExecuteScalarAsync<int>("SELECT ISNULL(ExpectedMinutes,0) FROM dbo.Points WHERE PointID = @pointId", new { pointId });
        if (expected > 0 && total > expected)
            await LogAsync(db, pointId, "ExtraTime", userId, $"{total - expected} min over estimate");
    }

    // ---------------- Developer transitions ----------------
    /// <summary>Send a completed point to Support — records the QC cycle and routes it
    /// (mirrors DashboardDeveloper.SendPointToQC + CreatePointHistoryCycle).</summary>
    public async Task<bool> SendToSupportAsync(int pointId, int userId, string? remark)
    {
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRAN;
            EXEC sp_CalculatePointTotalTime @PointID = @pointId;
            DECLARE @total int = (SELECT ISNULL(TotalTimeSpent,0)  FROM dbo.Points WHERE PointID = @pointId);
            DECLARE @prev  int = (SELECT ISNULL(SUM(TimeSpentMinutes),0) FROM dbo.PointHistory WHERE PointID = @pointId);
            DECLARE @exp   int = (SELECT ISNULL(ExpectedMinutes,0) FROM dbo.Points WHERE PointID = @pointId);
            DECLARE @round int = @total - @prev; IF @round < 0 SET @round = 0;
            INSERT INTO dbo.PointHistory
                (PointID, DeveloperID, StartDate, CompleteDate, SentToQCDate, DeveloperRemark, CycleStatus, TimeSpentMinutes, ExpectedMinutes, ExtraTimeMinutes)
            SELECT @pointId, @userId, StartDate, DateCompleted, GETDATE(), @remark, 'SentToQC', @round, @exp,
                   CASE WHEN @exp > 0 AND @round > @exp THEN @round - @exp ELSE 0 END
            FROM dbo.Points WHERE PointID = @pointId;
            UPDATE dbo.Points
              SET Status = 'PendingSupport', DateSentToQC = GETDATE(), DeveloperRemark = @remark,
                  StartDate = NULL, DateCompleted = NULL
              WHERE PointID = @pointId;
            INSERT INTO dbo.PointTimeLog (PointID, Action, ActionTime, PerformedByID, Remarks)
              VALUES (@pointId, 'SentToSupport', GETDATE(), @userId, @remark);
            COMMIT;";
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync(sql, new { pointId, userId, remark });
        return true;
    }

    /// <summary>Send a support-verified point for merge (guarded; mirrors SendPointToMerge).</summary>
    public async Task<bool> SendToMergeAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'PendingMerge', MergeAssignedToID = NULL, DateSentToMerge = GETDATE() WHERE PointID = @pointId AND Status = 'SupportVerified'",
            new { pointId });
        if (n > 0) await LogAsync(db, pointId, "SentToMerge", userId);
        return n > 0;
    }

    /// <summary>Hold / Reject (mirrors UpdatePointStatus). Only 'Hold' and 'Reject' are accepted.</summary>
    public async Task<bool> SetStatusAsync(int pointId, int userId, string status, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = @status, DeveloperRemark = COALESCE(@remark, DeveloperRemark) WHERE PointID = @pointId",
            new { pointId, status, remark });
        if (n > 0) await LogAsync(db, pointId, status, userId, remark);
        return n > 0;
    }

    // ---------------- Tester / QC (mirrors DashboardTester) ----------------
    private static Task UpdateLatestCycleAsync(SqlConnection db, int pointId, int? testerId, int? supportId, string cycleStatus, string? testerRemark, string? supportRemark)
        => db.ExecuteAsync(@"
            UPDATE dbo.PointHistory
              SET TesterID     = COALESCE(@testerId, TesterID),
                  SupportID    = COALESCE(@supportId, SupportID),
                  TesterRemark = COALESCE(@testerRemark, TesterRemark),
                  SupportRemark= COALESCE(@supportRemark, SupportRemark),
                  CycleStatus  = @cycleStatus,
                  FinalStatusDate = GETDATE()
            WHERE HistoryID = (SELECT MAX(HistoryID) FROM dbo.PointHistory WHERE PointID = @pointId)",
            new { pointId, testerId, supportId, cycleStatus, testerRemark, supportRemark });

    public async Task StartTesterAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Points SET Status = 'In-Testing', TesterStartDate = GETDATE() WHERE PointID = @pointId", new { pointId });
        await LogAsync(db, pointId, "TesterStart", userId);
    }

    public async Task CompleteTestingAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Points SET Status = 'Testing-Completed', TesterCompleteDate = GETDATE() WHERE PointID = @pointId", new { pointId });
        await LogAsync(db, pointId, "TesterComplete", userId);
    }

    /// <summary>Verify &amp; close (mirrors VerifyAndClose) — only from Testing-Completed.</summary>
    public async Task<bool> VerifyAndCloseAsync(int pointId, int testerId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'Closed', DateClosed = GETDATE(), TesterRemark = @remark WHERE PointID = @pointId AND Status = 'Testing-Completed'",
            new { pointId, remark });
        if (n > 0)
        {
            await UpdateLatestCycleAsync(db, pointId, testerId, null, "Verified", remark, null);
            await LogAsync(db, pointId, "VerifiedClosed", testerId, remark);
        }
        return n > 0;
    }

    public async Task ReopenForDeveloperAsync(int pointId, int testerId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        await UpdateLatestCycleAsync(db, pointId, testerId, null, "ReOpened", remark, null);
        await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'ReOpened', TesterRemark = @remark, StartDate = NULL, DateCompleted = NULL WHERE PointID = @pointId",
            new { pointId, remark });
        await LogAsync(db, pointId, "ReOpened", testerId, remark);
    }

    // ---------------- Support (mirrors SupportActionCenter) ----------------
    public async Task StartSupportAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        await db.ExecuteAsync("UPDATE dbo.Points SET SupportStartDate = ISNULL(SupportStartDate, GETDATE()), IsSupportPaused = 0, SupportLastActionTime = GETDATE() WHERE PointID = @pointId", new { pointId });
        await LogAsync(db, pointId, "Support-Start", userId);
    }

    public async Task CompleteSupportAsync(int pointId, int userId)
    {
        await using var db = await _db.OpenTmsAsync();
        // Approximate support time from the start marker (DataAccess derived this from the Support-* logs).
        await db.ExecuteAsync(@"
            UPDATE dbo.Points
              SET SupportCompleteDate = GETDATE(),
                  SupportTimeSpent = ISNULL(SupportTimeSpent,0) + CASE WHEN SupportStartDate IS NOT NULL THEN DATEDIFF(MINUTE, SupportStartDate, GETDATE()) ELSE 0 END,
                  SupportLastActionTime = GETDATE()
            WHERE PointID = @pointId", new { pointId });
        await LogAsync(db, pointId, "Support-Complete", userId);
    }

    public async Task<bool> MarkSupportVerifiedAsync(int pointId, int userId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'SupportVerified', SupportRemark = @remark, DateSupportVerified = GETDATE() WHERE PointID = @pointId AND Status = 'PendingSupport'",
            new { pointId, remark });
        if (n > 0)
        {
            await UpdateLatestCycleAsync(db, pointId, null, userId, "SupportVerified", null, remark);
            await LogAsync(db, pointId, "SupportVerified", userId, remark);
        }
        return n > 0;
    }

    public async Task<bool> SupportSendToQcAsync(int pointId, int userId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'PendingQC', SupportRemark = @remark WHERE PointID = @pointId AND Status IN ('PendingSupport','SupportVerified')",
            new { pointId, remark });
        if (n > 0) await LogAsync(db, pointId, "SupportSentToQC", userId, remark);
        return n > 0;
    }

    public async Task<bool> ReopenFromSupportAsync(int pointId, int userId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'ReOpened', SupportRemark = @remark, StartDate = NULL, DateCompleted = NULL, SupportStartDate = NULL, SupportCompleteDate = NULL WHERE PointID = @pointId AND Status = 'PendingSupport'",
            new { pointId, remark });
        if (n > 0) await LogAsync(db, pointId, "ReopenedFromSupport", userId, remark);
        return n > 0;
    }

    // ---------------- Merge (mirrors MergeCode) ----------------
    public async Task<bool> MergeApproveAsync(int pointId, int userId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'PendingQC', DateTicketVerified = GETDATE() WHERE PointID = @pointId AND Status = 'PendingMerge'",
            new { pointId });
        if (n > 0) await LogAsync(db, pointId, "MergeApproved", userId, remark);
        return n > 0;
    }

    public async Task<bool> MergeReopenAsync(int pointId, int userId, string? remark)
    {
        await using var db = await _db.OpenTmsAsync();
        var n = await db.ExecuteAsync(
            "UPDATE dbo.Points SET Status = 'ReOpened', StartDate = NULL, DateCompleted = NULL WHERE PointID = @pointId AND Status = 'PendingMerge'",
            new { pointId });
        if (n > 0) await LogAsync(db, pointId, "MergeReopened", userId, remark);
        return n > 0;
    }
}
