using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// All data access for the client-360 domain (Dapper / raw SQL).
/// Tables live in the "app" schema of the consolidated IndusTaskManagement DB.
/// </summary>
public sealed class ClientRepository
{
    private readonly Db _db;
    public ClientRepository(Db db) => _db = db;

    // ---------------- Clients ----------------
    public async Task<IEnumerable<ClientListItem>> GetAllAsync()
    {
        const string sql = @"
            SELECT c.ClientCode, c.Name, c.City, c.Application, c.Status, c.Progress, c.Consultant,
                   (SELECT COUNT(*) FROM app.ChangeRequests cr WHERE cr.ClientCode = c.ClientCode AND cr.Status = 'Open' AND ISNULL(cr.IsDeletedTransaction,0)=0) AS OpenCrCount
            FROM app.Clients c
            ORDER BY c.CreatedAt DESC, c.Name;";
        await using var db = await _db.OpenAsync();
        return await db.QueryAsync<ClientListItem>(sql);
    }

    public async Task<ClientDetail?> GetByCodeAsync(string code)
    {
        await using var db = await _db.OpenAsync();
        var c = await db.QuerySingleOrDefaultAsync<ClientDetail>(
            "SELECT * FROM app.Clients WHERE ClientCode = @code", new { code });
        if (c is null) return null;

        var p = new { code };
        c.Modules        = (await db.QueryAsync<ClientModule>("SELECT * FROM app.ClientModules WHERE ClientCode=@code", p)).ToList();
        c.Milestones     = (await db.QueryAsync<Milestone>("SELECT * FROM app.Milestones WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY SortOrder, Id", p)).ToList();
        c.Training       = (await db.QueryAsync<TrainingUpdate>("SELECT * FROM app.TrainingUpdates WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        c.ChangeRequests = (await db.QueryAsync<ChangeRequest>("SELECT * FROM app.ChangeRequests WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        c.Support        = (await db.QueryAsync<SupportLog>("SELECT * FROM app.SupportLogs WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        c.Onsite         = (await db.QueryAsync<OnsiteVisit>("SELECT * FROM app.OnsiteVisits WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        return c;
    }

    public async Task<string> CreateAsync(Client c, IEnumerable<string> modules)
    {
        const string sql = @"
            INSERT INTO app.Clients (ClientCode,Name,City,Gstin,Contact,Email,Mobile,Segment,Application,Product,Consultant,
                                 Status,Progress,CompanyLogin,PasswordMasked,UserLogin,Url,KickoffDone,KickoffDate,MastersSent,SignoffDone,Source)
            VALUES (@ClientCode,@Name,@City,@Gstin,@Contact,@Email,@Mobile,@Segment,@Application,@Product,@Consultant,
                    @Status,@Progress,@CompanyLogin,@PasswordMasked,@UserLogin,@Url,@KickoffDone,@KickoffDate,@MastersSent,@SignoffDone,@Source);";
        await using var db = await _db.OpenAsync();
        await db.ExecuteAsync(sql, c);
        foreach (var m in modules)
            await db.ExecuteAsync("INSERT INTO app.ClientModules (ClientCode,ModuleName,IsOn) VALUES (@ClientCode,@ModuleName,1)",
                new { c.ClientCode, ModuleName = m });
        return c.ClientCode;
    }

    /// <summary>
    /// Implementation-tracker collections for any client code (e.g. a subscription
    /// CompanyUniqueCode) — reads the app tracker tables directly, without
    /// requiring the client to exist in the Clients table.
    /// </summary>
    public async Task<(List<Milestone> milestones, List<TrainingUpdate> training, List<ChangeRequest> changeRequests, List<SupportLog> support, List<OnsiteVisit> onsite)> GetTrackerAsync(string code)
    {
        await using var db = await _db.OpenAsync();
        var p = new { code };
        var milestones = (await db.QueryAsync<Milestone>("SELECT * FROM app.Milestones WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY SortOrder, Id", p)).ToList();
        var training = (await db.QueryAsync<TrainingUpdate>("SELECT * FROM app.TrainingUpdates WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        var cr = (await db.QueryAsync<ChangeRequest>("SELECT * FROM app.ChangeRequests WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        var support = (await db.QueryAsync<SupportLog>("SELECT * FROM app.SupportLogs WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        var onsite = (await db.QueryAsync<OnsiteVisit>("SELECT * FROM app.OnsiteVisits WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        return (milestones, training, cr, support, onsite);
    }

    // ---------------- Milestones ----------------
    public async Task<int> AddMilestoneAsync(Milestone m)
    {
        const string sql = @"INSERT INTO app.Milestones
            (ClientCode,MilestoneGroup,Name,TaskTimeline,PlannedDate,ActualDate,EndDate,ResPerson,Status,
             StartDateVariance,ScheduledStartStatus,RemarkStartDelay,TimelineVariance,TimelineVarianceStatus,RemarkDuration,SortOrder,Emailed,Tasked,CreatedBy)
            OUTPUT INSERTED.Id VALUES
            (@ClientCode,@MilestoneGroup,@Name,@TaskTimeline,@PlannedDate,@ActualDate,@EndDate,@ResPerson,@Status,
             @StartDateVariance,@ScheduledStartStatus,@RemarkStartDelay,@TimelineVariance,@TimelineVarianceStatus,@RemarkDuration,@SortOrder,@Emailed,@Tasked,@CreatedBy);";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, m);
    }
    public async Task<bool> UpdateMilestoneAsync(Milestone m)
    {
        const string sql = @"UPDATE app.Milestones SET MilestoneGroup=@MilestoneGroup,Name=@Name,TaskTimeline=@TaskTimeline,PlannedDate=@PlannedDate,
            ActualDate=@ActualDate,EndDate=@EndDate,ResPerson=@ResPerson,Status=@Status,StartDateVariance=@StartDateVariance,
            ScheduledStartStatus=@ScheduledStartStatus,RemarkStartDelay=@RemarkStartDelay,TimelineVariance=@TimelineVariance,
            TimelineVarianceStatus=@TimelineVarianceStatus,RemarkDuration=@RemarkDuration,Emailed=@Emailed,Tasked=@Tasked,ModifiedBy=@ModifiedBy,ModifiedDate=SYSDATETIME() WHERE Id=@Id;";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteAsync(sql, m) > 0;
    }
    public async Task<bool> DeleteMilestoneAsync(int id, int? userId) => await DeleteAsync("Milestones", id, userId);

    // ---------------- Training ----------------
    public async Task<int> AddTrainingAsync(TrainingUpdate t)
    {
        const string sql = @"INSERT INTO app.TrainingUpdates (ClientCode,ModuleName,SubModule,TimelineDays,LogDate,StartTime,EndTime,Trainee,Trainer,Status,Details,Remark,VideoUrl,Emailed,Tasked,CreatedBy)
            OUTPUT INSERTED.Id VALUES (@ClientCode,@ModuleName,@SubModule,@TimelineDays,@LogDate,@StartTime,@EndTime,@Trainee,@Trainer,@Status,@Details,@Remark,@VideoUrl,@Emailed,@Tasked,@CreatedBy);";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, t);
    }
    public async Task<bool> UpdateTrainingAsync(TrainingUpdate t)
    {
        const string sql = @"UPDATE app.TrainingUpdates SET ModuleName=@ModuleName,SubModule=@SubModule,TimelineDays=@TimelineDays,LogDate=@LogDate,StartTime=@StartTime,EndTime=@EndTime,
            Trainee=@Trainee,Trainer=@Trainer,Status=@Status,Details=@Details,Remark=@Remark,VideoUrl=@VideoUrl,Emailed=@Emailed,Tasked=@Tasked,ModifiedBy=@ModifiedBy,ModifiedDate=SYSDATETIME() WHERE Id=@Id;";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteAsync(sql, t) > 0;
    }
    public async Task<bool> DeleteTrainingAsync(int id, int? userId) => await DeleteAsync("TrainingUpdates", id, userId);

    // ---------------- Change Requests ----------------
    public async Task<int> AddChangeRequestAsync(ChangeRequest x)
    {
        const string sql = @"INSERT INTO app.ChangeRequests (ClientCode,ModuleName,SubModule,Description,RaisedBy,RaisedDate,ReportedBy,QueryType,Status,CompletionDate,CompletionDays,Remark,InBugTool,Emailed,Tasked,Pointed,CreatedBy)
            OUTPUT INSERTED.Id VALUES (@ClientCode,@ModuleName,@SubModule,@Description,@RaisedBy,@RaisedDate,@ReportedBy,@QueryType,@Status,@CompletionDate,@CompletionDays,@Remark,@InBugTool,@Emailed,@Tasked,@Pointed,@CreatedBy);";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, x);
    }
    public async Task<bool> UpdateChangeRequestAsync(ChangeRequest x)
    {
        const string sql = @"UPDATE app.ChangeRequests SET ModuleName=@ModuleName,SubModule=@SubModule,Description=@Description,RaisedBy=@RaisedBy,RaisedDate=@RaisedDate,ReportedBy=@ReportedBy,
            QueryType=@QueryType,Status=@Status,CompletionDate=@CompletionDate,CompletionDays=@CompletionDays,Remark=@Remark,InBugTool=@InBugTool,Emailed=@Emailed,Tasked=@Tasked,Pointed=@Pointed,ModifiedBy=@ModifiedBy,ModifiedDate=SYSDATETIME() WHERE Id=@Id;";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteAsync(sql, x) > 0;
    }
    public async Task<bool> DeleteChangeRequestAsync(int id, int? userId) => await DeleteAsync("ChangeRequests", id, userId);

    // A /clients Application → the matching Point Management product name (same map as Add Point).
    private static string MapAppToProduct(string? app) => (app ?? "").Trim().ToLowerInvariant() switch
    {
        "desktop" => "Indus Print Desktop",
        "estimoprime" or "multiunit" => "Indus Print Web",
        "printudeerp" => "Printude ERP",
        _ => "",
    };

    /// <summary>
    /// "Send To → Point" for a Change Request: create a Point Management point from the CR,
    /// mapping Module/Sub Module/Description across, resolving the customer (find-or-create by the
    /// client's company name) and the Product from the client's Application, then mark the CR as
    /// Pointed. Returns the new Point (Ticket) id + the resolved product name.
    /// </summary>
    public async Task<(int pointId, string product)> SendChangeRequestToPointAsync(
        string clientCode, int crId, int? actingUserId, string? clientName = null, string? clientApplication = null)
    {
        await using var db = await _db.OpenAsync();

        var cr = await db.QuerySingleOrDefaultAsync(@"
            SELECT ModuleName, SubModule, Description, ReportedBy, QueryType
            FROM app.ChangeRequests WHERE Id=@crId AND ClientCode=@clientCode AND ISNULL(IsDeletedTransaction,0)=0",
            new { crId, clientCode });
        if (cr is null) throw new Exception("Change request not found.");

        // Prefer the exact client the user is viewing (name + application passed from the client page);
        // fall back to the /clients prod control DB by code only if those weren't supplied.
        string companyName = string.IsNullOrWhiteSpace(clientName) ? clientCode : clientName.Trim();
        string application = (clientApplication ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(application))
        {
            try
            {
                using var cc = await _db.OpenControlAsync();
                var client = await cc.QuerySingleOrDefaultAsync(@"
                    SELECT TOP 1 CompanyName, ApplicationName
                    FROM dbo.Indus_Company_Authentication_For_Web_Modules
                    WHERE CompanyUniqueCode=@code
                    ORDER BY CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(CompanyName,''))),'') IS NULL THEN 1 ELSE 0 END",
                    new { code = clientCode });
                if (client is not null)
                {
                    if (string.IsNullOrWhiteSpace(clientName) && !string.IsNullOrWhiteSpace((string?)client.CompanyName))
                        companyName = (string)client.CompanyName;
                    if (string.IsNullOrWhiteSpace(application))
                        application = ((string?)client.ApplicationName ?? "").Trim();
                }
            }
            catch { /* control DB unavailable — product resolution below throws a clear message if app is still empty */ }
        }

        // Application → Product.
        var productName = MapAppToProduct(application);
        var productId = string.IsNullOrWhiteSpace(productName) ? 0
            : (await db.ExecuteScalarAsync<int?>("SELECT TOP 1 ProductID FROM dbo.Products WHERE ProductName=@n", new { n = productName }) ?? 0);
        if (productId <= 0)
            throw new Exception($"Could not map the client's application ('{application}') to a Point Management product.");

        // Customer: find-or-create by company name.
        var customerId = await db.ExecuteScalarAsync<int?>("SELECT TOP 1 CustomerID FROM dbo.Customers WHERE CompanyName=@n ORDER BY CustomerID", new { n = companyName }) ?? 0;
        if (customerId <= 0)
            customerId = await db.ExecuteScalarAsync<int>(
                "INSERT INTO dbo.Customers (CustomerName, CompanyName, IsActive, DateCreated) OUTPUT INSERTED.CustomerID VALUES (@n,@n,1,GETDATE())", new { n = companyName });

        // Reported by → the ACTING (logged-in) user who sent it, resolved app.Users → dbo.Users by
        // email (then name). Falls back to the CR's ReportedBy name, then an admin, then first active.
        int reportedById = 0;
        if (actingUserId is int aid && aid > 0)
        {
            var actor = await db.QuerySingleOrDefaultAsync("SELECT Email, FullName FROM app.Users WHERE UserId=@id", new { id = aid });
            var actorEmail = ((string?)actor?.Email ?? "").Trim();
            var actorName = ((string?)actor?.FullName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(actorEmail))
                reportedById = await db.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM dbo.Users WHERE Email=@e AND IsActive=1 ORDER BY UserID", new { e = actorEmail }) ?? 0;
            if (reportedById <= 0 && !string.IsNullOrWhiteSpace(actorName))
                reportedById = await db.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM dbo.Users WHERE FullName=@n AND IsActive=1 ORDER BY UserID", new { n = actorName }) ?? 0;
        }
        var reportedByName = ((string?)cr.ReportedBy ?? "").Trim();
        if (reportedById <= 0 && !string.IsNullOrWhiteSpace(reportedByName))
            reportedById = await db.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM dbo.Users WHERE FullName=@n AND IsActive=1 ORDER BY UserID", new { n = reportedByName }) ?? 0;
        if (reportedById <= 0)
            reportedById = await db.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM dbo.Users WHERE Role LIKE '%admin%' AND IsActive=1 ORDER BY UserID") ?? 0;
        if (reportedById <= 0)
            reportedById = await db.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM dbo.Users WHERE IsActive=1 ORDER BY UserID") ?? 0;
        if (reportedById <= 0) throw new Exception("No Point Management user available to report the point.");

        // Category: use the CR QueryType if it's a valid category, else 'Improvement'.
        var queryType = ((string?)cr.QueryType ?? "").Trim();
        var category = await db.ExecuteScalarAsync<string?>("SELECT TOP 1 CategoryName FROM dbo.Categories WHERE CategoryName=@n AND IsActive=1", new { n = queryType }) ?? "Improvement";

        var moduleName = ((string?)cr.ModuleName ?? "").Trim();
        var title = string.IsNullOrWhiteSpace(moduleName) ? $"Change Request {crId}" : moduleName;

        var pointId = await db.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.Points
                (Title, Module, SubModule, Description, CustomerID, ProductID, ReportedByID,
                 Status, Priority, Category, DateCreated, VerificationStatus, IsVerified, IsDeveloperPaused, PauseTimeMinutes)
            OUTPUT INSERTED.PointID
            VALUES
                (@Title, @Module, @SubModule, @Description, @CustomerID, @ProductID, @ReportedByID,
                 'Queue', 'Medium', @Category, GETDATE(), 0, 0, 0, 0);",
            new { Title = title, Module = moduleName, SubModule = (string?)cr.SubModule, Description = (string?)cr.Description,
                  CustomerID = customerId, ProductID = productId, ReportedByID = reportedById, Category = category });

        // Mark the CR as sent to Point + store the created Point (ticket) id for future verification.
        await db.ExecuteAsync("UPDATE app.ChangeRequests SET Pointed=1, PointID=@pointId, ModifiedBy=@u, ModifiedDate=SYSDATETIME() WHERE Id=@crId",
            new { crId, pointId, u = actingUserId });

        return (pointId, productName);
    }

    /// <summary>
    /// "Send To → Tracker" for a Point Management point: create a Change Request from the point,
    /// mapping Module/Sub Module/Description/Category across, resolving the client's tracker code
    /// from the point's Customer company name. Duplicate-safe: if this point is already linked to a
    /// Change Request (either because it originated FROM one via Send-To-Point, or a previous
    /// Send-To-Tracker already ran), returns that existing row instead of creating a new one.
    /// </summary>
    public async Task<(int crId, string clientCode, bool alreadyLinked)> SendPointToTrackerAsync(int pointId, int? actingUserId)
    {
        await using var db = await _db.OpenAsync();

        // Duplicate guard — one point can only ever have one Change Request.
        var existing = await db.QuerySingleOrDefaultAsync(
            "SELECT TOP 1 Id, ClientCode FROM app.ChangeRequests WHERE PointID=@pointId AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC",
            new { pointId });
        if (existing is not null)
            return ((int)existing.Id, (string)existing.ClientCode, true);

        var point = await db.QuerySingleOrDefaultAsync(@"
            SELECT p.PointID, p.Module, p.SubModule, p.Description, p.Category,
                   c.CompanyName, ru.FullName AS ReportedByName
            FROM dbo.Points p
            LEFT JOIN dbo.Customers c ON c.CustomerID = p.CustomerID
            LEFT JOIN dbo.Users ru ON ru.UserID = p.ReportedByID
            WHERE p.PointID=@pointId", new { pointId });
        if (point is null) throw new Exception("Point not found.");

        var companyName = ((string?)point.CompanyName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(companyName))
            throw new Exception("This point has no customer linked, so it can't be sent to a client tracker.");

        // Resolve the tracker's client code: prefer a live app.Clients row; fall back to the control
        // DB's subscription CompanyUniqueCode (the Tracker pages accept either — see GetTrackerAsync).
        string? clientCode = await db.ExecuteScalarAsync<string?>(
            "SELECT TOP 1 ClientCode FROM app.Clients WHERE Name=@n", new { n = companyName });
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            try
            {
                using var cc = await _db.OpenControlAsync();
                clientCode = await cc.ExecuteScalarAsync<string?>(
                    "SELECT TOP 1 CompanyUniqueCode FROM dbo.Indus_Company_Authentication_For_Web_Modules WHERE CompanyName=@n",
                    new { n = companyName });
            }
            catch { /* control DB unavailable */ }
        }
        if (string.IsNullOrWhiteSpace(clientCode))
            throw new Exception($"Could not find a client/tracker matching customer '{companyName}'.");

        // "Raised By" means the CLIENT-side person who originally raised the issue — we have no such
        // data for a point sent the other way round, so it's left blank rather than guessing an
        // internal Point Management user's name into it. "Reported By" (the internal reporter) is
        // real data on the point, so that one IS carried across.
        var reportedByName = ((string?)point.ReportedByName ?? "").Trim();
        var moduleName = ((string?)point.Module ?? "").Trim();
        var category = ((string?)point.Category ?? "").Trim();

        var cr = new ChangeRequest
        {
            ClientCode = clientCode!,
            ModuleName = string.IsNullOrWhiteSpace(moduleName) ? null : moduleName,
            SubModule = (string?)point.SubModule,
            Description = (string?)point.Description,
            RaisedBy = null,
            RaisedDate = DateTime.Now.ToString("yyyy-MM-dd"),
            ReportedBy = string.IsNullOrWhiteSpace(reportedByName) ? null : reportedByName,
            QueryType = string.IsNullOrWhiteSpace(category) ? "Improvement" : category,
            Status = "Open",
            Pointed = true,
            PointID = pointId,
            CreatedBy = actingUserId,
        };

        var crId = await db.ExecuteScalarAsync<int>(@"
            INSERT INTO app.ChangeRequests (ClientCode,ModuleName,SubModule,Description,RaisedBy,RaisedDate,ReportedBy,QueryType,Status,InBugTool,Emailed,Tasked,Pointed,PointID,CreatedBy)
            OUTPUT INSERTED.Id
            VALUES (@ClientCode,@ModuleName,@SubModule,@Description,@RaisedBy,@RaisedDate,@ReportedBy,@QueryType,@Status,0,0,0,@Pointed,@PointID,@CreatedBy);",
            cr);

        return (crId, clientCode!, false);
    }

    // ---------------- Support ----------------
    public async Task<int> AddSupportAsync(SupportLog s)
    {
        const string sql = @"INSERT INTO app.SupportLogs (ClientCode,LogDate,ModuleName,SubModule,Problem,Solution,Status,Emailed,Tasked,CreatedBy)
            OUTPUT INSERTED.Id VALUES (@ClientCode,@LogDate,@ModuleName,@SubModule,@Problem,@Solution,@Status,@Emailed,@Tasked,@CreatedBy);";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, s);
    }
    public async Task<bool> UpdateSupportAsync(SupportLog s)
    {
        const string sql = @"UPDATE app.SupportLogs SET ModuleName=@ModuleName,SubModule=@SubModule,Problem=@Problem,Solution=@Solution,Status=@Status,Emailed=@Emailed,Tasked=@Tasked,ModifiedBy=@ModifiedBy,ModifiedDate=SYSDATETIME() WHERE Id=@Id;";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteAsync(sql, s) > 0;
    }
    public async Task<bool> DeleteSupportAsync(int id, int? userId) => await DeleteAsync("SupportLogs", id, userId);

    // ---------------- Onsite ----------------
    public async Task<int> AddOnsiteAsync(OnsiteVisit o)
    {
        const string sql = @"INSERT INTO app.OnsiteVisits
            (ClientCode,Person,Age,MobileNo,Location,FromDate,ToDate,Days,TicketCharge,HotelCharge,FoodCharge,SiteCharge,Status,Emailed,CreatedBy)
            OUTPUT INSERTED.Id VALUES
            (@ClientCode,@Person,@Age,@MobileNo,@Location,@FromDate,@ToDate,@Days,@TicketCharge,@HotelCharge,@FoodCharge,@SiteCharge,@Status,@Emailed,@CreatedBy);";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, o);
    }
    public async Task<bool> UpdateOnsiteAsync(OnsiteVisit o)
    {
        const string sql = @"UPDATE app.OnsiteVisits SET
            Person=@Person,Age=@Age,MobileNo=@MobileNo,Location=@Location,FromDate=@FromDate,ToDate=@ToDate,
            Days=@Days,TicketCharge=@TicketCharge,HotelCharge=@HotelCharge,FoodCharge=@FoodCharge,SiteCharge=@SiteCharge,Status=@Status,Emailed=@Emailed,
            ModifiedBy=@ModifiedBy,ModifiedDate=SYSDATETIME()
            WHERE Id=@Id;";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteAsync(sql, o) > 0;
    }
    public async Task<bool> DeleteOnsiteAsync(int id, int? userId) => await DeleteAsync("OnsiteVisits", id, userId);

    // ---------------- Dashboard ----------------
    public async Task<(int total, int inImpl, int goLive, int openCr)> GetStatsAsync()
    {
        const string sql = @"
            SELECT
              (SELECT COUNT(*) FROM app.Clients) AS Total,
              (SELECT COUNT(*) FROM app.Clients WHERE Status IN ('Provisioning','Training','Pending Kick-Off')) AS InImpl,
              (SELECT COUNT(*) FROM app.Clients WHERE Status = 'Go-Live') AS GoLive,
              (SELECT COUNT(*) FROM app.ChangeRequests WHERE Status = 'Open' AND ISNULL(IsDeletedTransaction,0)=0) AS OpenCr;";
        await using var db = await _db.OpenAsync();
        var r = await db.QuerySingleAsync(sql);
        return ((int)r.Total, (int)r.InImpl, (int)r.GoLive, (int)r.OpenCr);
    }

    // callers pass a bare table name (Milestones/TrainingUpdates/ChangeRequests/SupportLogs/OnsiteVisits) — all in the app schema.
    // SOFT delete: mark IsDeletedTransaction=1 + audit (DeletedBy/DeletedDate) instead of removing the row.
    private async Task<bool> DeleteAsync(string table, int id, int? userId)
    {
        await using var db = await _db.OpenAsync();
        return await db.ExecuteAsync(
            $"UPDATE app.{table} SET IsDeletedTransaction=1, DeletedBy=@userId, DeletedDate=SYSDATETIME() WHERE Id = @id",
            new { id, userId }) > 0;
    }
}
