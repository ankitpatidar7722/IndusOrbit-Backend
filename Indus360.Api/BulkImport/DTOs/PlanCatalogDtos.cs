namespace Backend.DTOs;

/* ============================================================================
   Plan Catalog DTOs — the GLOBAL catalog (Indus DB).
   Structure: Feature → FeaturePlan (tiers). Sub-features are stored inline on
   the plan as JSON (2-table model — no FeatureSubFeature master list).
   FeaturePlan also carries the customer-facing card fields (display name, blurb,
   annual price, features list) so the dashboard is fully catalog-driven.
   Applying a plan to companies is Chunk 2 (separate DTOs).
   ============================================================================ */

// ── Feature ────────────────────────────────────────────────────────────────
public class FeatureDto
{
    public int FeatureID { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class FeatureUpsertRequest
{
    public int? FeatureID { get; set; }                       // null = create
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

// ── Plan (a tier under a feature) ───────────────────────────────────────────
// One sub-feature line on a plan: a capability with an ON/OFF flag. Stored as
// JSON; `key` is stable so it can drive real behaviour later.
public class PlanSubFeature
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public class PlanDto
{
    public int PlanID { get; set; }
    public int FeatureID { get; set; }
    public string FeatureCode { get; set; } = string.Empty;   // joined for convenience
    public string? CompanyUserID { get; set; }                // null = global; value = override for that company only (global-unique key)
    public string? CompanyName { get; set; }                  // display label only — never used for matching
    public string PlanName { get; set; } = string.Empty;      // internal name
    public string? PlanDisplayName { get; set; }              // customer-facing card name
    public string? PlanCode { get; set; }                     // stable id used at checkout
    public string BillingCycle { get; set; } = "MONTHLY";
    public decimal UnitPrice { get; set; }                    // monthly price
    public decimal? AnnualPrice { get; set; }
    public bool PerUser { get; set; }
    public string? PerUserNote { get; set; }
    public string? Blurb { get; set; }
    public bool Highlight { get; set; }
    public string? Badge { get; set; }
    public List<string> Features { get; set; } = new();       // card bullet list (FeaturesJson)
    public List<PlanSubFeature> SubFeatures { get; set; } = new();
    public string? RazorpayPlanId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PlanUpsertRequest
{
    public int? PlanID { get; set; }                          // null = create
    public int FeatureID { get; set; }
    public string? CompanyUserID { get; set; }                // null = global; value = override for that company only (global-unique key)
    public string? CompanyName { get; set; }                  // display label only — never used for matching
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
    public List<string> Features { get; set; } = new();
    public List<PlanSubFeature> SubFeatures { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

// ── Generic response wrapper ────────────────────────────────────────────────
public class CatalogResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}
