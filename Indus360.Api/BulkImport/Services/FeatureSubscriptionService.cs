using Microsoft.Data.SqlClient;
using Dapper;
using Backend.DTOs;
using System.Text.Json;

namespace Backend.Services;

/* Manages Sahay & Email feature subscriptions — TENANT-LOCAL design (Option A).
   Interface + implementation in one file.

   No central catalog (FeaturePlan) and no separate seat table. Everything lives
   in the TENANT's own database (resolved via Conn_String from the Indus registry):
     • CompanyFeatureSubscription — one row per purchased feature (plan typed inline)
     • UserMaster.PremiumFeatures (JSON) — per-user Sahay entitlement
   See: IndusWebApi/Docs/SAHAY-EMAIL-SUBSCRIPTION-DESIGN.md */
public interface IFeatureSubscriptionService
{
    // Per-company state (reads the tenant DB)
    Task<CompanyFeatureStateResponse> GetCompanyStateAsync(string companyUserID);
    Task<ClientUserListResponse> GetClientUsersAsync(string companyUserID);    // for Sahay seat picker

    // The core write: assign/update a feature in the tenant DB (plan typed inline)
    Task<AssignFeatureResponse> AssignAsync(AssignFeatureRequest request);

    // Suspend / resume (flip Status + per-user JSON in the tenant DB)
    Task<FeatureStatusResponse> SuspendAsync(FeatureStatusRequest request);
    Task<FeatureStatusResponse> ResumeAsync(FeatureStatusRequest request);
}

public class FeatureSubscriptionService : IFeatureSubscriptionService
{
    private readonly IConfiguration _config;
    private readonly IActivityLogService _activityLogService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FeatureSubscriptionService(
        IConfiguration config,
        IActivityLogService activityLogService,
        IHttpContextAccessor httpContextAccessor)
    {
        _config = config;
        _activityLogService = activityLogService;
        _httpContextAccessor = httpContextAccessor;
    }

    private SqlConnection GetIndusConnection()
        => new SqlConnection(_config.GetConnectionString("IndusConnection"));

    // Connect to a tenant DB from its stored connection string.
    private static SqlConnection ClientConnection(string connString)
    {
        var builder = new SqlConnectionStringBuilder(connString)
        {
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };
        return new SqlConnection(builder.ConnectionString);
    }

    // Resolve a company's tenant-DB connection string from the Indus registry.
    private async Task<string?> ResolveClientConnStringAsync(string companyUserID)
    {
        using var conn = GetIndusConnection();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT Conn_String FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID=@CompanyUserID",
            new { CompanyUserID = companyUserID });
    }

    private async Task LogAsync(string actionType, string description, string? newValue = null)
    {
        try
        {
            var http = _httpContextAccessor.HttpContext;
            var userName = http?.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                        ?? http?.User?.FindFirst("userName")?.Value ?? "Unknown";
            int? webUserId = null;
            try
            {
                using var c = GetIndusConnection();
                webUserId = await c.ExecuteScalarAsync<int?>(
                    "SELECT WebUserId FROM CompanyWebUser WHERE WebUserName = @UserName", new { UserName = userName });
            }
            catch { }

            await _activityLogService.LogActivityAsync(new CreateActivityLogRequest
            {
                WebUserId = webUserId,
                WebUserName = userName,
                ActionType = actionType,
                EntityName = "FeatureSubscription",
                ActionDescription = description,
                NewValue = newValue,
                IPAddress = http?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = http?.Request?.Headers["User-Agent"].ToString(),
                IsSuccess = true
            });
        }
        catch (Exception ex) { Console.WriteLine($"[FeatureSubscription] ActivityLog failed: {ex.Message}"); }
    }

    // Idempotent: create the tenant-local table + UserMaster column if missing, so a
    // freshly-onboarded company works without a manual DDL step.
    private static async Task EnsureTenantSchemaAsync(SqlConnection client)
    {
        await client.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CompanyFeatureSubscription]') AND type = N'U')
            BEGIN
                CREATE TABLE [dbo].[CompanyFeatureSubscription] (
                    [SubscriptionID] INT            IDENTITY(1,1) PRIMARY KEY,
                    [FeatureCode]    NVARCHAR(20)   NOT NULL,
                    [PlanName]       NVARCHAR(100)  NOT NULL,
                    [BillingCycle]   NVARCHAR(10)   NOT NULL,
                    [UnitPrice]      DECIMAL(12,2)  NOT NULL,
                    [PerUser]        BIT            NOT NULL DEFAULT 0,
                    [SeatCount]      INT            NOT NULL DEFAULT 0,
                    [TotalPrice]     DECIMAL(12,2)  NOT NULL DEFAULT 0,
                    [StartDate]      DATETIME       NOT NULL,
                    [EndDate]        DATETIME       NOT NULL,
                    [Status]         NVARCHAR(20)   NOT NULL DEFAULT 'ACTIVE',
                    [CreatedAt]      DATETIME       NOT NULL DEFAULT GETUTCDATE(),
                    [UpdatedAt]      DATETIME       NULL,
                    CONSTRAINT [UQ_CFS_Feature] UNIQUE ([FeatureCode])
                );
            END
            IF COL_LENGTH('UserMaster','PremiumFeatures') IS NULL
                ALTER TABLE [UserMaster] ADD [PremiumFeatures] NVARCHAR(MAX) NULL;");
    }

    // ── Per-company state ─────────────────────────────────────────────────────
    public async Task<CompanyFeatureStateResponse> GetCompanyStateAsync(string companyUserID)
    {
        try
        {
            var connString = await ResolveClientConnStringAsync(companyUserID);
            if (string.IsNullOrWhiteSpace(connString))
                return new CompanyFeatureStateResponse { Success = false, Message = "Client connection string not found." };

            using var client = ClientConnection(connString);
            await client.OpenAsync();
            await EnsureTenantSchemaAsync(client);

            var rows = (await client.QueryAsync<FeatureSubscriptionDto>(
                "SELECT * FROM CompanyFeatureSubscription")).ToList();

            // Attach current Sahay seats (users whose PremiumFeatures JSON has Sahay.active=true).
            var sahay = rows.FirstOrDefault(r => r.FeatureCode == "Sahay");
            if (sahay != null)
                sahay.Seats = (await GetSahayUsersAsync(client)).Where(u => u.IsSahayActive).ToList();

            return new CompanyFeatureStateResponse
            {
                Success = true,
                Sahay = sahay,
                Email = rows.FirstOrDefault(r => r.FeatureCode == "Email")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FeatureSubscription] GetCompanyState Error: {ex.Message}");
            return new CompanyFeatureStateResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ClientUserListResponse> GetClientUsersAsync(string companyUserID)
    {
        try
        {
            var connString = await ResolveClientConnStringAsync(companyUserID);
            if (string.IsNullOrWhiteSpace(connString))
                return new ClientUserListResponse { Success = false, Message = "Client connection string not found." };

            using var client = ClientConnection(connString);
            await client.OpenAsync();
            await EnsureTenantSchemaAsync(client);

            return new ClientUserListResponse { Success = true, Data = await GetSahayUsersAsync(client) };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FeatureSubscription] GetClientUsers Error: {ex.Message}");
            return new ClientUserListResponse { Success = false, Message = ex.Message };
        }
    }

    // Read users + parse Sahay entitlement out of the PremiumFeatures JSON.
    private static async Task<List<ClientUserDto>> GetSahayUsersAsync(SqlConnection client)
    {
        var raw = (await client.QueryAsync<(int UserID, string UserName, string? PremiumFeatures)>(
            "SELECT UserID, UserName, PremiumFeatures FROM UserMaster WHERE ISNULL(IsDeletedTransaction,0)=0 ORDER BY UserName")).ToList();

        var list = new List<ClientUserDto>();
        foreach (var u in raw)
            list.Add(new ClientUserDto { UserID = u.UserID, UserName = u.UserName, IsSahayActive = JsonHasActiveSahay(u.PremiumFeatures) });
        return list;
    }

    private static bool JsonHasActiveSahay(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Sahay", out var s))
            {
                if (s.ValueKind == JsonValueKind.True) return true;
                if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("active", out var a))
                    return a.ValueKind == JsonValueKind.True;
            }
        }
        catch { }
        return false;
    }

    // ── Assign (core write — all in the tenant DB) ────────────────────────────
    public async Task<AssignFeatureResponse> AssignAsync(AssignFeatureRequest request)
    {
        try
        {
            // 1. Validate (plan typed inline — no catalog lookup)
            if (request.FeatureCode != "Sahay" && request.FeatureCode != "Email")
                return new AssignFeatureResponse { Success = false, Message = "FeatureCode must be 'Sahay' or 'Email'." };
            if (string.IsNullOrWhiteSpace(request.PlanName))
                return new AssignFeatureResponse { Success = false, Message = "Plan name is required." };
            if (request.EndDate <= request.StartDate)
                return new AssignFeatureResponse { Success = false, Message = "End date must be after start date." };

            bool perUser = request.FeatureCode == "Sahay";
            var seatIds = (perUser ? request.SeatUserIds : new List<int>()).Distinct().ToList();
            if (perUser && seatIds.Count == 0)
                return new AssignFeatureResponse { Success = false, Message = "Select at least one user for Sahay." };

            var connString = await ResolveClientConnStringAsync(request.CompanyUserID);
            if (string.IsNullOrWhiteSpace(connString))
                return new AssignFeatureResponse { Success = false, Message = "Client connection string not found." };

            var unitPrice = request.UnitPrice;
            var seatCount = perUser ? seatIds.Count : 0;
            var totalPrice = unitPrice * (perUser ? seatCount : 1);

            using var client = ClientConnection(connString);
            await client.OpenAsync();
            await EnsureTenantSchemaAsync(client);

            // 2. Upsert the company-level subscription row (UNIQUE on FeatureCode).
            await client.ExecuteAsync(@"
                MERGE CompanyFeatureSubscription AS t
                USING (SELECT @FeatureCode AS FeatureCode) AS s ON t.FeatureCode = s.FeatureCode
                WHEN MATCHED THEN UPDATE SET
                    PlanName=@PlanName, BillingCycle=@BillingCycle, UnitPrice=@UnitPrice,
                    PerUser=@PerUser, SeatCount=@SeatCount, TotalPrice=@TotalPrice,
                    StartDate=@StartDate, EndDate=@EndDate, Status='ACTIVE', UpdatedAt=GETUTCDATE()
                WHEN NOT MATCHED THEN INSERT
                    (FeatureCode, PlanName, BillingCycle, UnitPrice, PerUser, SeatCount, TotalPrice, StartDate, EndDate, Status)
                    VALUES (@FeatureCode, @PlanName, @BillingCycle, @UnitPrice, @PerUser, @SeatCount, @TotalPrice, @StartDate, @EndDate, 'ACTIVE');",
                new
                {
                    request.FeatureCode, request.PlanName, request.BillingCycle,
                    UnitPrice = unitPrice, PerUser = perUser, SeatCount = seatCount,
                    TotalPrice = totalPrice, request.StartDate, request.EndDate
                });

            // 3. Per-user entitlement in UserMaster.PremiumFeatures (Sahay only).
            if (perUser)
                await ApplySahaySeatsAsync(client, seatIds, request.PlanName, unitPrice, request.BillingCycle);

            await LogAsync("Assign",
                $"Assigned {request.FeatureCode} '{request.PlanName}' to {request.CompanyUserID} " +
                $"({(perUser ? $"{seatCount} seat(s)" : "company-wide")}, ₹{totalPrice}, " +
                $"{request.StartDate:yyyy-MM-dd}→{request.EndDate:yyyy-MM-dd})",
                JsonSerializer.Serialize(new { request.CompanyUserID, request.FeatureCode, request.PlanName, seatCount, totalPrice }));

            var state = await GetCompanyStateAsync(request.CompanyUserID);
            var saved = request.FeatureCode == "Sahay" ? state.Sahay : state.Email;
            return new AssignFeatureResponse { Success = true, Message = "Subscription assigned.", Data = saved };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FeatureSubscription] Assign Error: {ex.Message}");
            return new AssignFeatureResponse { Success = false, Message = ex.Message };
        }
    }

    // Set PremiumFeatures JSON for selected users to active Sahay; clear it for the rest.
    private static async Task ApplySahaySeatsAsync(SqlConnection client, List<int> seatIds,
        string planName, decimal price, string cycle)
    {
        var sahayObj = JsonSerializer.Serialize(new
        {
            Sahay = new { active = true, plan = planName, price, cycle }
        });

        var allUsers = (await client.QueryAsync<(int UserID, string? PremiumFeatures)>(
            "SELECT UserID, PremiumFeatures FROM UserMaster WHERE ISNULL(IsDeletedTransaction,0)=0")).ToList();

        foreach (var u in allUsers)
        {
            bool shouldHave = seatIds.Contains(u.UserID);
            string newJson = SetSahayInJson(u.PremiumFeatures, shouldHave ? sahayObj : null);
            await client.ExecuteAsync(
                "UPDATE UserMaster SET PremiumFeatures = @Json WHERE UserID = @UserID",
                new { Json = (object?)newJson ?? DBNull.Value, UserID = u.UserID });
        }
    }

    // Merge/replace the "Sahay" key in an existing PremiumFeatures JSON object,
    // preserving any other feature keys already present.
    private static string? SetSahayInJson(string? existing, string? sahayFragmentJson)
    {
        var dict = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                using var doc = JsonDocument.Parse(existing);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject())
                        dict[p.Name] = p.Value.Clone();
            }
            catch { }
        }

        if (sahayFragmentJson == null) dict.Remove("Sahay");
        else
        {
            using var frag = JsonDocument.Parse(sahayFragmentJson);
            dict["Sahay"] = frag.RootElement.GetProperty("Sahay").Clone();
        }

        if (dict.Count == 0) return null;
        return JsonSerializer.Serialize(dict);
    }

    // ── Suspend / Resume ──────────────────────────────────────────────────────
    public Task<FeatureStatusResponse> SuspendAsync(FeatureStatusRequest request) => SetActiveAsync(request, false);
    public Task<FeatureStatusResponse> ResumeAsync(FeatureStatusRequest request)  => SetActiveAsync(request, true);

    private async Task<FeatureStatusResponse> SetActiveAsync(FeatureStatusRequest request, bool active)
    {
        try
        {
            var connString = await ResolveClientConnStringAsync(request.CompanyUserID);
            if (string.IsNullOrWhiteSpace(connString))
                return new FeatureStatusResponse { Success = false, Message = "Client connection string not found." };

            using var client = ClientConnection(connString);
            await client.OpenAsync();
            await EnsureTenantSchemaAsync(client);

            var affected = await client.ExecuteAsync(
                "UPDATE CompanyFeatureSubscription SET Status=@Status, UpdatedAt=GETUTCDATE() WHERE FeatureCode=@FeatureCode",
                new { Status = active ? "ACTIVE" : "SUSPENDED", request.FeatureCode });
            if (affected == 0)
                return new FeatureStatusResponse { Success = false, Message = "Subscription not found." };

            await LogAsync(active ? "Resume" : "Suspend",
                $"{(active ? "Resumed" : "Suspended")} {request.FeatureCode} for {request.CompanyUserID}");
            return new FeatureStatusResponse { Success = true, Message = active ? "Resumed." : "Suspended." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FeatureSubscription] SetActive Error: {ex.Message}");
            return new FeatureStatusResponse { Success = false, Message = ex.Message };
        }
    }
}
