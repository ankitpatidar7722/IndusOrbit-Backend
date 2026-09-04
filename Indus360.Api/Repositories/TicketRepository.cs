using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Ticket (assignment container) data access — create/assign, list, reassign,
/// close. Ports the ticket methods from DataAccess.vb (CreateTicketAndAssignPoints,
/// GetTicketsForMainGrid, ReassignTicket, CloseTicket).
/// </summary>
public sealed class TicketRepository
{
    private readonly Db _db;
    public TicketRepository(Db db) => _db = db;

    /// <summary>Create a ticket and move the selected verified-queue points to Assigned.</summary>
    public async Task<int> CreateAndAssignAsync(AssignRequest req)
    {
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @out TABLE (id int);
            INSERT INTO dbo.Tickets (TicketNo, AssignedToID, CreatedByID, Department, DateCreated, Status)
            OUTPUT INSERTED.TicketID INTO @out
            VALUES (FORMAT(GETDATE(),'yyyyMMddHHmmssfff'), @DevId, @CreatedById, 'Software Development', GETDATE(), 'Open');
            DECLARE @ticketId int = (SELECT TOP 1 id FROM @out);
            UPDATE dbo.Points
              SET TicketID = @ticketId, AssignedToID = @DevId, Status = 'Assigned',
                  ExpectedDate = @ExpectedDate, ExpectedMinutes = @ExpectedMinutes
              WHERE PointID IN @PointIds AND Status = 'Queue' AND IsVerified = 1;
            COMMIT;
            SELECT @ticketId;";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteScalarAsync<int>(sql, new
        {
            req.DevId, req.CreatedById, req.ExpectedDate, req.ExpectedMinutes, PointIds = req.PointIds
        });
    }

    /// <summary>All tickets for the manage-assignments grid.</summary>
    public async Task<IEnumerable<TicketRow>> GetTicketsAsync()
    {
        const string sql = @"
            SELECT t.TicketID, t.TicketNo, t.AssignedToID, u.FullName AS AssignedToName, cu.FullName AS CreatedByName,
                   t.Department, t.Status, t.DateCreated,
                   (SELECT COUNT(*) FROM dbo.Points p WHERE p.TicketID = t.TicketID) AS PointCount,
                   (SELECT COUNT(*) FROM dbo.Points p WHERE p.TicketID = t.TicketID AND p.Status NOT IN ('Closed','Reject')) AS OpenCount
            FROM dbo.Tickets t
            LEFT JOIN app.Users u  ON u.UserID = t.AssignedToID
            LEFT JOIN app.Users cu ON cu.UserID = t.CreatedByID
            ORDER BY t.DateCreated DESC, t.TicketID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<TicketRow>(sql);
    }

    /// <summary>Reassign a ticket (and its open points) to another developer. Also records who
    /// performed the reassignment (Tickets.CreatedByID) and when, so "Assign By"/"Assign Date"
    /// on the Manage Assignments grid always reflect the latest assignment action.</summary>
    public async Task<bool> ReassignAsync(int ticketId, int newDevId, int assignedByUserId)
    {
        const string sql = @"
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE dbo.Tickets SET AssignedToID = @newDevId, CreatedByID = @assignedByUserId, DateCreated = GETDATE() WHERE TicketID = @ticketId;
            UPDATE dbo.Points  SET AssignedToID = @newDevId WHERE TicketID = @ticketId AND Status NOT IN ('Closed','Reject');
            COMMIT;";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteAsync(sql, new { ticketId, newDevId, assignedByUserId }) > 0;
    }

    /// <summary>One row per assigned Point (has a Ticket) for the Manage Assignments grid.</summary>
    public async Task<IEnumerable<AssignmentRow>> GetAssignmentRowsAsync()
    {
        const string sql = @"
            SELECT p.PointID, p.Title, p.TicketID,
                   p.AssignedToID, au.FullName AS AssignedToName,
                   t.CreatedByID AS AssignedByID, ab.FullName AS AssignedByName, t.DateCreated AS AssignDate,
                   ru.FullName AS ReportedByName,
                   p.Status, p.Priority, p.Description,
                   c.CompanyName AS CustomerName, pr.ProductName,
                   -- Legacy TMS stored Module in `Summary`; fall back to it when the Module column is blank.
                   ISNULL(NULLIF(LTRIM(RTRIM(p.Module)), ''), p.Summary) AS Module, p.SubModule
            FROM dbo.Points p
            JOIN dbo.Tickets t ON t.TicketID = p.TicketID
            LEFT JOIN app.Users au ON au.UserID = p.AssignedToID
            LEFT JOIN app.Users ab ON ab.UserID = t.CreatedByID
            LEFT JOIN app.Users ru ON ru.UserID = p.ReportedByID
            LEFT JOIN dbo.Customers c ON c.CustomerID = p.CustomerID
            LEFT JOIN dbo.Products  pr ON pr.ProductID = p.ProductID
            WHERE p.TicketID IS NOT NULL
            ORDER BY t.DateCreated DESC, p.PointID DESC";
        await using var db = await _db.OpenTmsAsync();
        return await db.QueryAsync<AssignmentRow>(sql);
    }

    /// <summary>Admin override: close all points on a ticket.</summary>
    public async Task<bool> CloseAsync(int ticketId)
    {
        const string sql = @"
            UPDATE dbo.Points SET Status = 'Closed', DateClosed = GETDATE()
            WHERE TicketID = @ticketId AND Status NOT IN ('Closed','Reject');
            UPDATE dbo.Tickets SET Status = 'Closed' WHERE TicketID = @ticketId;";
        await using var db = await _db.OpenTmsAsync();
        return await db.ExecuteAsync(sql, new { ticketId }) > 0;
    }
}
