using Microsoft.Data.SqlClient;
using Dapper;
using Backend.DTOs;
using System.Text.Json;

namespace Backend.Services;

/* ============================================================================
   Plan Catalog service — the GLOBAL catalog in the Indus DB.
   CRUD for Feature and FeaturePlan (tiers). 2-table model: sub-features and the
   customer-facing card fields are stored inline on FeaturePlan.
   Applying plans to companies' tenant DBs is Chunk 2 (separate service).
   ============================================================================ */
public interface IPlanCatalogService
{
    // Features
    Task<List<FeatureDto>> GetFeaturesAsync();
    Task<CatalogResponse<FeatureDto>> UpsertFeatureAsync(FeatureUpsertRequest req);

    // Plans
    Task<List<PlanDto>> GetPlansAsync(int? featureId = null);
    Task<CatalogResponse<PlanDto>> UpsertPlanAsync(PlanUpsertRequest req);
    Task<CatalogResponse<bool>> DeletePlanAsync(int planId);
}

public class PlanCatalogService : IPlanCatalogService
{
    private readonly IConfiguration _config;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public PlanCatalogService(IConfiguration config) => _config = config;

    private SqlConnection Indus() => new(_config.GetConnectionString("IndusConnection"));

    // ── Features ─────────────────────────────────────────────────────────────
    public async Task<List<FeatureDto>> GetFeaturesAsync()
    {
        using var c = Indus();
        var rows = await c.QueryAsync<FeatureDto>(
            "SELECT FeatureID, FeatureCode, FeatureName, IsActive FROM Feature ORDER BY FeatureName");
        return rows.ToList();
    }

    public async Task<CatalogResponse<FeatureDto>> UpsertFeatureAsync(FeatureUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FeatureCode) || string.IsNullOrWhiteSpace(req.FeatureName))
            return Fail<FeatureDto>("Feature code and name are required.");

        using var c = Indus();

        // Guard the UNIQUE(FeatureCode) so we return a friendly message, not a 500.
        var clash = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Feature WHERE FeatureCode=@FeatureCode AND FeatureID<>@Id",
            new { req.FeatureCode, Id = req.FeatureID ?? 0 });
        if (clash > 0) return Fail<FeatureDto>($"A feature with code '{req.FeatureCode}' already exists.");

        int id;
        if (req.FeatureID is null or 0)
        {
            id = await c.ExecuteScalarAsync<int>(
                @"INSERT INTO Feature (FeatureCode, FeatureName, IsActive)
                  VALUES (@FeatureCode, @FeatureName, @IsActive);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new { req.FeatureCode, req.FeatureName, req.IsActive });
        }
        else
        {
            id = req.FeatureID.Value;
            await c.ExecuteAsync(
                @"UPDATE Feature SET FeatureCode=@FeatureCode, FeatureName=@FeatureName,
                  IsActive=@IsActive, UpdatedAt=GETUTCDATE() WHERE FeatureID=@Id",
                new { req.FeatureCode, req.FeatureName, req.IsActive, Id = id });
        }

        var dto = await c.QuerySingleAsync<FeatureDto>(
            "SELECT FeatureID, FeatureCode, FeatureName, IsActive FROM Feature WHERE FeatureID=@Id", new { Id = id });
        return Ok(dto, "Feature saved.");
    }

    // ── Plans ────────────────────────────────────────────────────────────────
    public async Task<List<PlanDto>> GetPlansAsync(int? featureId = null)
    {
        using var c = Indus();
        var rows = await c.QueryAsync<PlanRow>(
            @"SELECT p.PlanID, p.FeatureID, f.FeatureCode, p.CompanyUserID, p.CompanyName, p.PlanName, p.PlanDisplayName, p.PlanCode,
                     p.BillingCycle, p.UnitPrice, p.AnnualPrice, p.PerUser, p.PerUserNote, p.Blurb,
                     p.Highlight, p.Badge, p.FeaturesJson, p.SubFeaturesJson, p.RazorpayPlanId, p.IsActive
              FROM FeaturePlan p JOIN Feature f ON f.FeatureID = p.FeatureID
              WHERE (@FeatureID IS NULL OR p.FeatureID=@FeatureID)
              ORDER BY f.FeatureName, p.CompanyUserID, p.UnitPrice",
            new { FeatureID = featureId });
        return rows.Select(ToDto).ToList();
    }

    public async Task<CatalogResponse<PlanDto>> UpsertPlanAsync(PlanUpsertRequest req)
    {
        if (req.FeatureID <= 0) return Fail<PlanDto>("FeatureID is required.");
        if (string.IsNullOrWhiteSpace(req.PlanName)) return Fail<PlanDto>("Plan name is required.");
        // Plan code is the checkout id AND the dashboard catalog key — a plan without it
        // is invisible/unbuyable on the dashboard, so reject it here too (not just in the UI).
        if (string.IsNullOrWhiteSpace(req.PlanCode)) return Fail<PlanDto>("Plan code is required (used at checkout).");

        using var c = Indus();
        var subsJson = JsonSerializer.Serialize(req.SubFeatures ?? new(), JsonOpts);
        var featuresJson = JsonSerializer.Serialize(req.Features ?? new(), JsonOpts);
        var cycle = string.Equals(req.BillingCycle, "ANNUAL", StringComparison.OrdinalIgnoreCase) ? "ANNUAL" : "MONTHLY";

        // Guard PlanCode uniqueness WITHIN the same scope (used at checkout). A company
        // override is ALLOWED to reuse a global plan's code (that's the point of an
        // override), so the clash check is scoped to the same CompanyUserID bucket — NULL
        // (global) and each company are independent namespaces.
        if (!string.IsNullOrWhiteSpace(req.PlanCode))
        {
            var clash = await c.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM FeaturePlan
                  WHERE PlanCode=@PlanCode AND PlanID<>@Id
                    AND ((@CompanyUserID IS NULL AND CompanyUserID IS NULL) OR CompanyUserID=@CompanyUserID)",
                new { req.PlanCode, Id = req.PlanID ?? 0, req.CompanyUserID });
            if (clash > 0)
            {
                var scope = req.CompanyUserID is null ? "another global plan" : "another plan for this company";
                return Fail<PlanDto>($"Plan code '{req.PlanCode}' is already used by {scope}.");
            }
        }

        var p = new
        {
            req.FeatureID, req.CompanyUserID, req.CompanyName, req.PlanName, req.PlanDisplayName, req.PlanCode, Cycle = cycle,
            req.UnitPrice, req.AnnualPrice, req.PerUser, req.PerUserNote, req.Blurb,
            req.Highlight, req.Badge, FeaturesJson = featuresJson, SubsJson = subsJson, req.IsActive,
        };

        int id;
        if (req.PlanID is null or 0)
        {
            id = await c.ExecuteScalarAsync<int>(
                @"INSERT INTO FeaturePlan (FeatureID, CompanyUserID, CompanyName, PlanName, PlanDisplayName, PlanCode, BillingCycle, UnitPrice,
                                           AnnualPrice, PerUser, PerUserNote, Blurb, Highlight, Badge,
                                           FeaturesJson, SubFeaturesJson, IsActive)
                  VALUES (@FeatureID, @CompanyUserID, @CompanyName, @PlanName, @PlanDisplayName, @PlanCode, @Cycle, @UnitPrice,
                          @AnnualPrice, @PerUser, @PerUserNote, @Blurb, @Highlight, @Badge,
                          @FeaturesJson, @SubsJson, @IsActive);
                  SELECT CAST(SCOPE_IDENTITY() AS INT);", p);
        }
        else
        {
            id = req.PlanID.Value;
            await c.ExecuteAsync(
                @"UPDATE FeaturePlan SET FeatureID=@FeatureID, CompanyUserID=@CompanyUserID, CompanyName=@CompanyName, PlanName=@PlanName, PlanDisplayName=@PlanDisplayName,
                  PlanCode=@PlanCode, BillingCycle=@Cycle, UnitPrice=@UnitPrice, AnnualPrice=@AnnualPrice,
                  PerUser=@PerUser, PerUserNote=@PerUserNote, Blurb=@Blurb, Highlight=@Highlight, Badge=@Badge,
                  FeaturesJson=@FeaturesJson, SubFeaturesJson=@SubsJson, IsActive=@IsActive,
                  UpdatedAt=GETUTCDATE() WHERE PlanID=@Id",
                new { p.FeatureID, p.CompanyUserID, p.CompanyName, p.PlanName, p.PlanDisplayName, p.PlanCode, p.Cycle, p.UnitPrice,
                      p.AnnualPrice, p.PerUser, p.PerUserNote, p.Blurb, p.Highlight, p.Badge,
                      p.FeaturesJson, p.SubsJson, p.IsActive, Id = id });
        }

        var plan = (await GetPlansAsync()).First(p => p.PlanID == id);
        return Ok(plan, "Plan saved.");
    }

    public async Task<CatalogResponse<bool>> DeletePlanAsync(int planId)
    {
        using var c = Indus();
        await c.ExecuteAsync("DELETE FROM FeaturePlan WHERE PlanID=@Id", new { Id = planId });
        return Ok(true, "Plan deleted.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private class PlanRow
    {
        public int PlanID { get; set; }
        public int FeatureID { get; set; }
        public string FeatureCode { get; set; } = string.Empty;
        public string? CompanyUserID { get; set; }
        public string? CompanyName { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string? PlanDisplayName { get; set; }
        public string? PlanCode { get; set; }
        public string BillingCycle { get; set; } = "MONTHLY";
        public decimal UnitPrice { get; set; }
        public decimal? AnnualPrice { get; set; }
        public bool PerUser { get; set; }
        public string? PerUserNote { get; set; }
        public string? Blurb { get; set; }
        public bool Highlight { get; set; }
        public string? Badge { get; set; }
        public string? FeaturesJson { get; set; }
        public string? SubFeaturesJson { get; set; }
        public string? RazorpayPlanId { get; set; }
        public bool IsActive { get; set; }
    }

    private static PlanDto ToDto(PlanRow r) => new()
    {
        PlanID = r.PlanID,
        FeatureID = r.FeatureID,
        FeatureCode = r.FeatureCode,
        CompanyUserID = r.CompanyUserID,
        CompanyName = r.CompanyName,
        PlanName = r.PlanName,
        PlanDisplayName = r.PlanDisplayName,
        PlanCode = r.PlanCode,
        BillingCycle = r.BillingCycle,
        UnitPrice = r.UnitPrice,
        AnnualPrice = r.AnnualPrice,
        PerUser = r.PerUser,
        PerUserNote = r.PerUserNote,
        Blurb = r.Blurb,
        Highlight = r.Highlight,
        Badge = r.Badge,
        RazorpayPlanId = r.RazorpayPlanId,
        IsActive = r.IsActive,
        Features = ParseFeatures(r.FeaturesJson),
        SubFeatures = ParseSubs(r.SubFeaturesJson),
    };

    private static List<PlanSubFeature> ParseSubs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<PlanSubFeature>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private static List<string> ParseFeatures(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private static CatalogResponse<T> Ok<T>(T data, string msg) => new() { Success = true, Message = msg, Data = data };
    private static CatalogResponse<T> Fail<T>(string msg) => new() { Success = false, Message = msg };
}
