using System.Text.RegularExpressions;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Per-client SOP status for web modules (the "SOP of Web Modules" grid). Reads/writes
/// app.SopModuleStatus keyed by (client CompanyUniqueCode, Keyline ModuleName). Also seeds the
/// Estimoprime default (Status=true for every module that has a SOP document, across all
/// Estimoprime clients).
/// </summary>
[ApiController]
[Route("api/sop-status")]
public sealed class SopStatusController : ControllerBase
{
    private readonly SopStatusRepository _repo;
    private readonly KeylineRepository _keyline;
    private readonly SubscriptionRepository _subs;
    private readonly IWebHostEnvironment _env;
    public SopStatusController(SopStatusRepository repo, KeylineRepository keyline, SubscriptionRepository subs, IWebHostEnvironment env)
    { _repo = repo; _keyline = keyline; _subs = subs; _env = env; }

    // (Module Head Name, Module Display Name) pairs where a same-named SOP must NOT apply (wrong head).
    // Kept in sync with the frontend SOP_EXCLUDE list.
    private static readonly HashSet<string> Exclude = new(new[]
    {
        ("Bottle Cap Manufacturing", "Production Work Order"),
        ("Client Inventory", "Item Issue"),
        ("Client Inventory", "Return To Stock"),
        ("Other Items", "Purchase Invoice"), ("Other Items", "Purchase Order"), ("Other Items", "Purchase Order Approval"),
        ("Other Items", "Purchase Requisition"), ("Other Items", "Requisition Approval"),
        ("Spare Part Inventory", "Purchase Invoice"), ("Spare Part Inventory", "Purchase Order"), ("Spare Part Inventory", "Purchase Order Approval"),
        ("Spare Part Inventory", "Purchase Requisition"), ("Spare Part Inventory", "Requisition Approval"),
        ("Spare Part Inventory", "Return To Stock"), ("Spare Part Inventory", "Return To Supplier"),
        ("Tool Inventory", "Purchase Invoice"), ("Tool Inventory", "Purchase Order"), ("Tool Inventory", "Purchase Order Approval"),
        ("Tool Inventory", "Purchase Requisition"), ("Tool Inventory", "Requisition Approval"), ("Tool Inventory", "Return To Stock"),
    }.Select(p => Slug(p.Item1) + "::" + Slug(p.Item2)));

    private static string Slug(string s)
    {
        s = Regex.Replace((s ?? "").ToLowerInvariant().Replace("(", " ").Replace(")", " "), "[^a-z0-9]+", "-");
        return s.Trim('-');
    }
    private static string NormApp(string? s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

    /// <summary>Saved SOP rows for one client (by CompanyUniqueCode).</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string clientCode)
    {
        if (string.IsNullOrWhiteSpace(clientCode)) return Ok(new { success = true, data = Array.Empty<object>() });
        return Ok(new { success = true, data = await _repo.GetForClientAsync(clientCode.Trim()) });
    }

    public sealed class SaveRequest
    {
        public string? ClientCode { get; set; }
        public string? ModuleName { get; set; }
        public string? ModuleHeadName { get; set; }
        public string? ModuleDisplayName { get; set; }
        public string? SopSlug { get; set; }
        public string? YoutubeLink { get; set; }
        public string? SopDocument { get; set; }
        public bool Status { get; set; }
        public int? UserId { get; set; }
    }

    /// <summary>Upsert one module's SOP data for a client.</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ClientCode) || string.IsNullOrWhiteSpace(req.ModuleName))
            return BadRequest(new { success = false, message = "clientCode and moduleName are required." });
        await _repo.UpsertAsync(req.ClientCode!.Trim(), req.ModuleName!.Trim(), req.ModuleHeadName, req.ModuleDisplayName, req.SopSlug,
            req.YoutubeLink, req.SopDocument, req.Status, req.UserId);
        return Ok(new { success = true });
    }

    /// <summary>The Keyline web modules that have a SOP document (display-name slug matches a SOP file,
    /// minus the wrong-head exclusions), with head/display/slug for the ERP SOP tab.</summary>
    private async Task<List<SopModuleInfo>> SopModulesAsync()
    {
        var mods = await _keyline.GetSopModulesAsync();
        var dir = new DirectoryInfo(Path.Combine(_env.ContentRootPath, "sop-web-modules"));
        var slugs = dir.Exists
            ? new HashSet<string>(dir.GetFiles("*.html").Select(f => Path.GetFileNameWithoutExtension(f.Name)))
            : new HashSet<string>();
        return mods
            .Select(m => new { m, d = Slug(m.ModuleDisplayName) })
            .Where(x => !string.IsNullOrWhiteSpace(x.m.ModuleName) && slugs.Contains(x.d) && !Exclude.Contains(Slug(x.m.ModuleHeadName) + "::" + x.d))
            .GroupBy(x => x.m.ModuleName)
            .Select(g => { var x = g.First(); return new SopModuleInfo(x.m.ModuleName, x.m.ModuleHeadName, x.m.ModuleDisplayName, x.d); })
            .ToList();
    }

    /// <summary>Seed the default: Status=true for every SOP module, across all Estimoprime clients.</summary>
    [HttpPost("seed-estimoprime")]
    public async Task<IActionResult> SeedEstimoprime()
    {
        var modules = await SopModulesAsync();
        var clients = (await _subs.GetAllAsync())
            .Where(x => NormApp(x.ApplicationName) == "estimoprime" && !string.IsNullOrWhiteSpace(x.CompanyUniqueCode))
            .Select(x => x.CompanyUniqueCode!.Trim())
            .Distinct()
            .ToList();
        var rows = await _repo.SeedTrueAsync(clients, modules);
        return Ok(new { success = true, clients = clients.Count, modules = modules.Count, rowsAffected = rows });
    }
}
