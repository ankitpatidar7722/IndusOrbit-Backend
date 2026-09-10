using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.DTOs;
using Backend.Services;

/* ============================================================================
   Plan Catalog API — GLOBAL plan management (admin/indus side).
   Feature → Plans (sub-features + card fields stored inline on the plan).
   Lives in the Indus DB. Applying plans to companies is a separate controller
   (Chunk 2).
   ============================================================================ */
namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PlanCatalogController : ControllerBase
    {
        private readonly IPlanCatalogService _service;
        public PlanCatalogController(IPlanCatalogService service) => _service = service;

        // ── Features ──────────────────────────────────────────────────────────
        [HttpGet("features")]
        public async Task<IActionResult> GetFeatures() => Ok(await _service.GetFeaturesAsync());

        [HttpPost("features")]
        public async Task<IActionResult> UpsertFeature([FromBody] FeatureUpsertRequest req)
            => Ok(await _service.UpsertFeatureAsync(req));

        // ── Plans ───────────────────────────────────────────────────────────────
        [HttpGet("plans")]
        public async Task<IActionResult> GetPlans([FromQuery] int? featureId)
            => Ok(await _service.GetPlansAsync(featureId));

        [HttpPost("plans")]
        public async Task<IActionResult> UpsertPlan([FromBody] PlanUpsertRequest req)
            => Ok(await _service.UpsertPlanAsync(req));

        [HttpDelete("plans/{planId:int}")]
        public async Task<IActionResult> DeletePlan(int planId)
            => Ok(await _service.DeletePlanAsync(planId));
    }
}
