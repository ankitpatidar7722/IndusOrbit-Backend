using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

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
        if (milestones.Count == 0)
        {
            // First tracker view of a client (incl. newly-provisioned ones) → seed the fixed roadmap.
            // Best-effort: a seeding hiccup must never break the tracker load.
            try
            {
                await SeedMilestonesAsync(db, code, null);
                milestones = (await db.QueryAsync<Milestone>("SELECT * FROM app.Milestones WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY SortOrder, Id", p)).ToList();
            }
            catch { /* ignore — tracker still loads (empty milestones) */ }
        }
        var training = (await db.QueryAsync<TrainingUpdate>("SELECT * FROM app.TrainingUpdates WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        var cr = (await db.QueryAsync<ChangeRequest>("SELECT * FROM app.ChangeRequests WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        var support = (await db.QueryAsync<SupportLog>("SELECT * FROM app.SupportLogs WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        var onsite = (await db.QueryAsync<OnsiteVisit>("SELECT * FROM app.OnsiteVisits WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY Id DESC", p)).ToList();
        return (milestones, training, cr, support, onsite);
    }

    // ---------------- Milestones ----------------
    public async Task<int> AddMilestoneAsync(Milestone m)
    {
        DeriveMilestoneVariance(m);
        const string sql = @"INSERT INTO app.Milestones
            (ClientCode,MilestoneGroup,Name,TaskTimeline,PlannedDate,ActualDate,EndDate,ResPerson,Status,
             StartDateVariance,ScheduledStartStatus,RemarkStartDelay,TimelineVariance,TimelineVarianceStatus,RemarkDuration,SortOrder,Emailed,Tasked,CreatedBy)
            OUTPUT INSERTED.Id VALUES
            (@ClientCode,@MilestoneGroup,@Name,@TaskTimeline,@PlannedDate,@ActualDate,@EndDate,@ResPerson,@Status,
             @StartDateVariance,@ScheduledStartStatus,@RemarkStartDelay,@TimelineVariance,@TimelineVarianceStatus,@RemarkDuration,@SortOrder,@Emailed,@Tasked,@CreatedBy);";
        await using var db = await _db.OpenAsync();
        return await db.ExecuteScalarAsync<int>(sql, m);
    }

    // ── Fixed "Roadmap to Success" milestone template ──────────────────────────────────────────
    // Seeded into EVERY client's tracker: existing clients via POST /api/clients/seed-milestone-template,
    // new clients lazily on their first tracker view (see GetTrackerAsync). Only Group + Phase are
    // filled — Task Timeline / dates / status start empty for the team to complete per client.
    public static readonly (string Group, string Name, string Timeline)[] MilestoneTemplate =
    {
        ("Milestone-1", "Order Date", "0"),
        ("Milestone-1", "Database Configuration", "0"),
        ("Milestone-1", "Kick-Off and Masters Email", "1"),
        ("Milestone-1", "Internal Kick-Off Meeting", "1"),
        ("Milestone-1", "Kick-Off Meeting With Client", "0"),
        ("Milestone-2", "Master Configuration & Preparation", "3"),
        ("Milestone-2", "Remaining Requirement Gathering (Masters)", "3"),
        ("Milestone-2", "Master Data Upload", "1"),
        ("Milestone-2", "Testing 3 Job (Start to End)", "1"),
        ("Milestone-3", "Implementation and Trainings", "6"),
        ("Milestone-3", "User Practice", "3"),
        ("Milestone-3", "Proposed Deliverable", "0"),
        ("Milestone-3", "Sign-Off (Completion Date)", "1"),
        ("Milestone-4", "Support", "NA"),
    };

    /// <summary>Insert the fixed milestone template for a client — ONLY if it has no milestones yet
    /// (idempotent). Returns rows inserted (0 if the client already had milestones).</summary>
    private static async Task<int> SeedMilestonesAsync(SqlConnection db, string code, int? createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;
        var existing = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM app.Milestones WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0", new { code });
        if (existing > 0) return 0;
        const string ins = @"INSERT INTO app.Milestones (ClientCode,MilestoneGroup,Name,TaskTimeline,Status,SortOrder,Emailed,Tasked,CreatedBy,CreatedDate)
                             VALUES (@code,@grp,@name,@tl,'',@sort,0,0,@createdBy,SYSDATETIME());";
        int sort = 1;
        foreach (var (grp, name, tl) in MilestoneTemplate)
            await db.ExecuteAsync(ins, new { code, grp, name, tl, sort = sort++, createdBy });
        return MilestoneTemplate.Length;
    }

    /// <summary>Seed the milestone template for one client (idempotent).</summary>
    public async Task<int> SeedMilestonesAsync(string code, int? createdBy = null)
    {
        await using var db = await _db.OpenAsync();
        return await SeedMilestonesAsync(db, code, createdBy);
    }

    /// <summary>Bulk-seed the template for many client codes (skips any that already have
    /// milestones). Returns (clients processed, rows inserted).</summary>
    public async Task<(int clients, int seeded, int failed)> SeedMilestonesForAllAsync(IEnumerable<string?> codes, int? createdBy = null)
    {
        int clients = 0, seeded = 0, failed = 0;
        foreach (var code in codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            clients++;
            // Own (pooled) connection per client + per-client isolation: one bad client / transient
            // network drop can't abort the whole backfill (and re-running resumes — it's idempotent).
            try { seeded += await SeedMilestonesAsync(code, createdBy); }
            catch { failed++; }
        }
        return (clients, seeded, failed);
    }

    // ── Roadmap auto-init on Database creation ─────────────────────────────────────────────────
    // When a client's DB is provisioned: Order Date phase gets today's date in Estimated/Actual/End,
    // Res.Person = the CRM sales person, Status = Complete, Timeline Var Status = On Time; every later
    // phase's Estimated Start cascades from the previous one by its Task Timeline in days — SKIPPING
    // SUNDAYS only (Mon–Sat count). Dates stored as yyyy-MM-dd (what the date fields use).
    public async Task InitRoadmapAsync(string code, string? salesPerson, int? actorUserId)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        await using var db = await _db.OpenAsync();
        await SeedMilestonesAsync(db, code, actorUserId);   // ensure the template exists (idempotent)
        var rows = (await db.QueryAsync<Milestone>(
            "SELECT * FROM app.Milestones WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY SortOrder, Id",
            new { code })).ToList();
        if (rows.Count == 0) return;

        var today = DateTime.Today;
        static string Iso(DateTime d) => d.ToString("yyyy-MM-dd");

        // Estimated Start cascade: phase[0] = today; phase[i] = phase[i-1] + THIS phase's own TaskTimeline
        // (skip Sundays). e.g. Kick-Off Email (Timeline 1) = previous date + 1.
        var planned = new DateTime[rows.Count];
        planned[0] = today;
        for (int i = 1; i < rows.Count; i++)
            planned[i] = AddSkippingSundays(planned[i - 1], ParseDays(rows[i].TaskTimeline));

        for (int i = 0; i < rows.Count; i++)
        {
            if (i == 0)
                await db.ExecuteAsync(@"UPDATE app.Milestones SET PlannedDate=@p, ActualDate=@p, EndDate=@p,
                    ResPerson=@res, Status='Complete', TimelineVarianceStatus='On Time', StartDateVariance='0',
                    ModifiedBy=@mod, ModifiedDate=SYSDATETIME() WHERE Id=@id",
                    new { p = Iso(planned[0]), res = salesPerson, mod = actorUserId, id = rows[0].Id });
            else
                await db.ExecuteAsync(
                    "UPDATE app.Milestones SET PlannedDate=@p, ModifiedBy=@mod, ModifiedDate=SYSDATETIME() WHERE Id=@id",
                    new { p = Iso(planned[i]), mod = actorUserId, id = rows[i].Id });
        }
    }

    /// <summary>Add <paramref name="days"/> to a date counting Mon–Sat only (Sundays are skipped, not counted).</summary>
    private static DateTime AddSkippingSundays(DateTime start, int days)
    {
        var d = start;
        for (int added = 0; added < days;)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek != DayOfWeek.Sunday) added++;
        }
        return d;
    }

    private static int ParseDays(string? t) => int.TryParse((t ?? "").Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>Auto-derive Start Date Variance (Actual − Estimated, days) + Timeline Variance Status
    /// ("On Time" when Actual ≤ Estimated, else "Delayed") from the dates — both must be valid.
    /// Applied on every milestone add/update so the status is always in sync with the dates.</summary>
    private static void DeriveMilestoneVariance(Milestone m)
    {
        if (DateTime.TryParse(m.PlannedDate, out var est) && DateTime.TryParse(m.ActualDate, out var act))
        {
            m.StartDateVariance = ((int)(act.Date - est.Date).TotalDays).ToString();
            m.TimelineVarianceStatus = act.Date > est.Date ? "Delayed" : "On Time";
        }
    }

    /// <summary>Re-run the Estimated-Start cascade for a client's roadmap, anchored at <paramref name="baseDate"/>
    /// (the just-saved Order Date row): every LATER phase's Estimated Start (PlannedDate) = the previous
    /// phase's + that phase's own Task Timeline in days, skipping Sundays — the same rule the DB-creation
    /// roadmap uses. Only Estimated Start (and derived variance/status where the phase already has an Actual
    /// Start) is touched; the anchor row itself is left exactly as saved.</summary>
    private static async Task RecascadeEstimatedStartsAsync(SqlConnection db, string code, DateTime baseDate, int anchorId, int? actorUserId)
    {
        var rows = (await db.QueryAsync<Milestone>(
            "SELECT * FROM app.Milestones WHERE ClientCode=@code AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY SortOrder, Id",
            new { code })).ToList();
        int start = rows.FindIndex(r => r.Id == anchorId);
        if (start < 0) start = 0;   // fall back to the very first row if the anchor wasn't found

        static string Iso(DateTime d) => d.ToString("yyyy-MM-dd");
        var prev = baseDate;
        for (int i = start + 1; i < rows.Count; i++)
        {
            prev = AddSkippingSundays(prev, ParseDays(rows[i].TaskTimeline));
            var row = rows[i];
            row.PlannedDate = Iso(prev);
            DeriveMilestoneVariance(row);   // keep variance/status in sync if this phase already has an Actual Start
            await db.ExecuteAsync(
                @"UPDATE app.Milestones SET PlannedDate=@PlannedDate, StartDateVariance=@StartDateVariance,
                    TimelineVarianceStatus=@TimelineVarianceStatus, ModifiedBy=@mod, ModifiedDate=SYSDATETIME() WHERE Id=@Id",
                new { row.PlannedDate, row.StartDateVariance, row.TimelineVarianceStatus, mod = actorUserId, row.Id });
        }
    }

    // Phases that auto-complete (Actual Start = today, Status = Complete) the day their email is sent
    // to the client — the Kick-Off email and the Sign-Off.
    private static readonly HashSet<string> AutoCompleteOnEmailPhases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kick-Off and Masters Email",
        "Sign-Off (Completion Date)",
    };

    public async Task<bool> UpdateMilestoneAsync(Milestone m)
    {
        // Auto-complete these phases the day their email is sent to the client: when the row gets flagged
        // emailed and has no Actual Start yet → Actual Start = today, Status = Complete.
        if (m.Emailed && m.Name != null && AutoCompleteOnEmailPhases.Contains(m.Name.Trim())
            && string.IsNullOrWhiteSpace(m.ActualDate))
        {
            m.ActualDate = DateTime.Today.ToString("yyyy-MM-dd");
            m.Status = "Complete";
        }
        DeriveMilestoneVariance(m);   // derive variance/status from planned + (possibly just-set) actual

        await using var db = await _db.OpenAsync();

        // If this is the anchor "Order Date" row, remember its current Estimated Start first, so we can
        // tell whether the edit actually changed it — we only re-cascade the roadmap when it changed
        // (or was blank), never when the user edits the Order Date row for some other reason.
        bool isOrderDate = m.Name != null && string.Equals(m.Name.Trim(), "Order Date", StringComparison.OrdinalIgnoreCase);
        string? oldPlanned = isOrderDate
            ? await db.ExecuteScalarAsync<string?>("SELECT PlannedDate FROM app.Milestones WHERE Id=@Id", new { m.Id })
            : null;

        const string sql = @"UPDATE app.Milestones SET MilestoneGroup=@MilestoneGroup,Name=@Name,TaskTimeline=@TaskTimeline,PlannedDate=@PlannedDate,
            ActualDate=@ActualDate,EndDate=@EndDate,ResPerson=@ResPerson,Status=@Status,StartDateVariance=@StartDateVariance,
            ScheduledStartStatus=@ScheduledStartStatus,RemarkStartDelay=@RemarkStartDelay,TimelineVariance=@TimelineVariance,
            TimelineVarianceStatus=@TimelineVarianceStatus,RemarkDuration=@RemarkDuration,Emailed=@Emailed,Tasked=@Tasked,ModifiedBy=@ModifiedBy,ModifiedDate=SYSDATETIME() WHERE Id=@Id;";
        var ok = await db.ExecuteAsync(sql, m) > 0;

        // When the Order Date's Estimated Start is set/changed, re-cascade every LATER phase's Estimated
        // Start from it (each += its own Task Timeline in days, skipping Sundays). The Tracker refetches
        // after save, so the recomputed dates appear immediately.
        if (ok && isOrderDate && !string.IsNullOrWhiteSpace(m.ClientCode)
            && DateTime.TryParse(m.PlannedDate, out var baseDate)
            && (!DateTime.TryParse(oldPlanned, out var oldDate) || oldDate.Date != baseDate.Date))
        {
            await RecascadeEstimatedStartsAsync(db, m.ClientCode!, baseDate, m.Id, m.ModifiedBy);
        }
        return ok;
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

    /// <summary>
    /// "Send To → Task" for a Tracker row (Milestone / Training / Change Request): appends the row
    /// as a line item to the ACTING (logged-in) user's TODAY Daily Worklog DRAFT in the internal CRM
    /// app (IndusInternalApp), then marks the tracker row Tasked=1. The user is matched to an
    /// IndusInternalApp employee BY EMAIL (never by name — duplicate names exist). Draft worklog +
    /// entries live in IndusAppDB (reached via the IndusApp connection, same DB the CRM picker uses);
    /// tracker rows + the acting user's email live in the app schema (Default connection).
    /// Append-only (does NOT replace the day's other entries). Returns (ok, message).
    /// </summary>
    public async Task<(bool ok, string message)> SendTrackerRowToWorklogAsync(
        string clientCode, string entity, int rowId, string? clientName, int? actingUserId)
    {
        entity = (entity ?? "").Trim().ToLowerInvariant();
        var projectLabel = (clientName ?? "").Trim();

        // 1) Acting user's email (app schema / Default conn) — the ONLY key we map to an employee.
        string? email;
        await using (var app = await _db.OpenAsync())
        {
            var u = await app.QuerySingleOrDefaultAsync("SELECT Email FROM app.Users WHERE UserId=@id", new { id = actingUserId });
            email = ((string?)u?.Email)?.Trim();
        }
        if (string.IsNullOrWhiteSpace(email))
            return (false, "Your account has no email on file, so it can't be matched to an IndusInternalApp employee.");

        // 2) The tracker row → worklog Title + Description (Default conn, app schema).
        string title = "", description = "";
        await using (var app = await _db.OpenAsync())
        {
            if (entity == "milestone")
            {
                var r = await app.QuerySingleOrDefaultAsync("SELECT Name, MilestoneGroup FROM app.Milestones WHERE Id=@rowId AND ClientCode=@clientCode AND ISNULL(IsDeletedTransaction,0)=0", new { rowId, clientCode });
                if (r is null) return (false, "Milestone row not found.");
                title = ((string?)r.Name ?? "").Trim();
                var grp = ((string?)r.MilestoneGroup ?? "").Trim();
                description = (grp != "" ? $"Roadmap: {grp}" : "") + (projectLabel != "" ? $" - Client: {projectLabel}" : "");
            }
            else if (entity == "training")
            {
                var r = await app.QuerySingleOrDefaultAsync("SELECT ModuleName, SubModule, Details FROM app.TrainingUpdates WHERE Id=@rowId AND ClientCode=@clientCode AND ISNULL(IsDeletedTransaction,0)=0", new { rowId, clientCode });
                if (r is null) return (false, "Training row not found.");
                title = ((string?)r.ModuleName ?? "").Trim();
                var sub = ((string?)r.SubModule ?? "").Trim();
                var det = ((string?)r.Details ?? "").Trim();
                description = (sub != "" ? $"Sub Module: {sub}. " : "") + det + (projectLabel != "" ? $" - Client: {projectLabel}" : "");
            }
            else if (entity == "changerequest")
            {
                var r = await app.QuerySingleOrDefaultAsync("SELECT ModuleName, Description FROM app.ChangeRequests WHERE Id=@rowId AND ClientCode=@clientCode AND ISNULL(IsDeletedTransaction,0)=0", new { rowId, clientCode });
                if (r is null) return (false, "Change request row not found.");
                title = ((string?)r.ModuleName ?? "").Trim();
                description = ((string?)r.Description ?? "").Trim() + (projectLabel != "" ? $" - Client: {projectLabel}" : "");
            }
            else return (false, "Unknown tracker entity.");
        }
        if (string.IsNullOrWhiteSpace(title)) title = projectLabel != "" ? projectLabel : "Tracker task";

        // 3) IndusInternalApp DB (IndusApp conn): resolve employee, find-or-create today's Draft, append entry.
        await using (var hr = await _db.OpenAppDbAsync())
        {
            var empId = await hr.ExecuteScalarAsync<int?>("SELECT TOP 1 EmployeeID FROM dbo.Employees WHERE Email=@e ORDER BY EmployeeID", new { e = email });
            if (empId is null || empId <= 0)
                return (false, $"Your email ({email}) is not linked to an IndusInternalApp employee, so no worklog could be created.");

            // If the client name matches a real project, link it (shows in cost report); else free-text label.
            int? projectId = projectLabel == "" ? null
                : await hr.ExecuteScalarAsync<int?>("SELECT TOP 1 ProjectID FROM dbo.Projects WHERE IsDeleted=0 AND ProjectName=@n ORDER BY ProjectID", new { n = projectLabel });

            var existing = await hr.QuerySingleOrDefaultAsync("SELECT WorkLogID, Status FROM dbo.DailyWorkLog WHERE EmployeeID=@e AND WorkDate=CAST(GETDATE() AS date)", new { e = empId });
            int workLogId;
            if (existing is not null)
            {
                if (string.Equals((string?)existing.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "Today's worklog is already approved and can no longer be edited.");
                workLogId = (int)existing.WorkLogID;
            }
            else
            {
                workLogId = await hr.ExecuteScalarAsync<int>(@"
                    INSERT INTO dbo.DailyWorkLog (EmployeeID, WorkDate, Status, CreatedAt)
                    OUTPUT INSERTED.WorkLogID
                    VALUES (@e, CAST(GETDATE() AS date), 'Draft', GETDATE());", new { e = empId });
            }

            await hr.ExecuteAsync(@"
                INSERT INTO dbo.DailyWorkEntry (WorkLogID, Title, Description, Category, ProjectID, ProjectLabel, TaskID, Hours, ProjectTimeLogID, CreatedAt)
                VALUES (@W, @Title, @Desc, NULL, @P, @PL, NULL, 0, NULL, GETDATE());",
                new { W = workLogId, Title = title, Desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                      P = projectId, PL = projectId.HasValue ? (string?)null : (projectLabel == "" ? null : projectLabel) });
        }

        // 4) Mark the tracker row Tasked=1 (so the ✓ shows) — Default conn, app schema.
        var table = entity switch { "milestone" => "Milestones", "training" => "TrainingUpdates", "changerequest" => "ChangeRequests", _ => null };
        if (table != null)
            await using (var app = await _db.OpenAsync())
                await app.ExecuteAsync($"UPDATE app.{table} SET Tasked=1, ModifiedBy=@u, ModifiedDate=SYSDATETIME() WHERE Id=@rowId", new { rowId, u = actingUserId });

        return (true, "Added to today's Draft worklog.");
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
