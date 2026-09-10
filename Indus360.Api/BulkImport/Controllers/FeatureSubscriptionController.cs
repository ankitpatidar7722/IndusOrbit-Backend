using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.DTOs;
using Backend.Services;

/* ============================================================================
   Sahay & Email feature-subscription management — TENANT-LOCAL design (Option A).

   No plan catalog: the admin types the plan (name/price/cycle/dates) at Assign
   time and it's stored in the company's OWN database. Controller + DTOs in one
   file (DTOs stay in Backend.DTOs so other `using Backend.DTOs;` is unchanged).
   ============================================================================ */
namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FeatureSubscriptionController : ControllerBase
    {
        private readonly IFeatureSubscriptionService _service;

        public FeatureSubscriptionController(IFeatureSubscriptionService service)
        {
            _service = service;
        }

        // Current Sahay + Email subscriptions for a company (reads its tenant DB).
        [HttpGet("company/{companyUserID}")]
        public async Task<IActionResult> GetCompanyState(string companyUserID)
            => Ok(await _service.GetCompanyStateAsync(companyUserID));

        // Company's users (+ current Sahay entitlement) for the seat picker.
        [HttpGet("company/{companyUserID}/users")]
        public async Task<IActionResult> GetClientUsers(string companyUserID)
            => Ok(await _service.GetClientUsersAsync(companyUserID));

        // Assign / update a feature (plan typed inline).
        [HttpPost("assign")]
        public async Task<IActionResult> Assign([FromBody] AssignFeatureRequest request)
            => Ok(await _service.AssignAsync(request));

        [HttpPost("suspend")]
        public async Task<IActionResult> Suspend([FromBody] FeatureStatusRequest request)
            => Ok(await _service.SuspendAsync(request));

        [HttpPost("resume")]
        public async Task<IActionResult> Resume([FromBody] FeatureStatusRequest request)
            => Ok(await _service.ResumeAsync(request));
    }
}

namespace Backend.DTOs
{
    /* ────────────────────────────────────────────────────────────────────────
       Tenant-local feature-subscription DTOs (Option A — no FeaturePlan catalog).
       ──────────────────────────────────────────────────────────────────────── */

    // A user in the tenant DB, with current Sahay entitlement (from UserMaster JSON).
    public class ClientUserDto
    {
        public int UserID { get; set; }
        public string UserName { get; set; } = string.Empty;
        public bool IsSahayActive { get; set; }
    }

    public class ClientUserListResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<ClientUserDto> Data { get; set; } = new();
    }

    // One purchased feature for a company (stored in its tenant DB).
    public class FeatureSubscriptionDto
    {
        public int SubscriptionID { get; set; }
        public string FeatureCode { get; set; } = string.Empty;   // 'Sahay' | 'Email'
        public string PlanName { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = string.Empty;  // 'MONTHLY' | 'ANNUAL'
        public decimal UnitPrice { get; set; }
        public bool PerUser { get; set; }
        public int SeatCount { get; set; }
        public decimal TotalPrice { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "ACTIVE";
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<ClientUserDto> Seats { get; set; } = new();    // populated for Sahay
    }

    public class CompanyFeatureStateResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public FeatureSubscriptionDto? Sahay { get; set; }
        public FeatureSubscriptionDto? Email { get; set; }
    }

    // Assign payload — the plan is typed inline (no catalog PlanID).
    public class AssignFeatureRequest
    {
        public string CompanyUserID { get; set; } = string.Empty;
        public string FeatureCode { get; set; } = string.Empty;   // 'Sahay' | 'Email'
        public string PlanName { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = "MONTHLY";     // 'MONTHLY' | 'ANNUAL'
        public decimal UnitPrice { get; set; }                    // per cycle; PER SEAT for Sahay
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<int> SeatUserIds { get; set; } = new();       // Sahay only; ignored for Email
    }

    public class AssignFeatureResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public FeatureSubscriptionDto? Data { get; set; }
    }

    public class FeatureStatusRequest
    {
        public string CompanyUserID { get; set; } = string.Empty;
        public string FeatureCode { get; set; } = string.Empty;
    }

    public class FeatureStatusResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
