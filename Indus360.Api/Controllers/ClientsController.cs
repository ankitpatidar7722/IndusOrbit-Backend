using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

public class CreateClientRequest : Client
{
    public List<string> Modules { get; set; } = new();
}

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly ClientRepository _repo;
    public ClientsController(ClientRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _repo.GetAllAsync());

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code)
    {
        var c = await _repo.GetByCodeAsync(code);
        return c is null ? NotFound() : Ok(c);
    }

    /// <summary>Implementation-tracker collections for any client code (works for subscription codes too).</summary>
    [HttpGet("{code}/tracker")]
    public async Task<IActionResult> GetTracker(string code)
    {
        var (milestones, training, changeRequests, support, onsite) = await _repo.GetTrackerAsync(code);
        return Ok(new { milestones, training, changeRequests, support, onsite });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest req)
    {
        var code = await _repo.CreateAsync(req, req.Modules);
        var created = await _repo.GetByCodeAsync(code);
        return CreatedAtAction(nameof(Get), new { code }, created);
    }

    // -------- Milestones --------
    [HttpPost("{code}/milestones")]
    public async Task<IActionResult> AddMilestone(string code, [FromBody] Milestone m)
    { m.ClientCode = code; m.CreatedBy = this.CurrentUserId(); m.Id = await _repo.AddMilestoneAsync(m); return Ok(m); }

    [HttpPut("{code}/milestones/{id:int}")]
    public async Task<IActionResult> UpdateMilestone(string code, int id, [FromBody] Milestone m)
    { m.Id = id; m.ClientCode = code; m.ModifiedBy = this.CurrentUserId(); return await _repo.UpdateMilestoneAsync(m) ? Ok(m) : NotFound(); }

    [HttpDelete("{code}/milestones/{id:int}")]
    public async Task<IActionResult> DeleteMilestone(int id) => await _repo.DeleteMilestoneAsync(id, this.CurrentUserId()) ? NoContent() : NotFound();

    // -------- Training --------
    [HttpPost("{code}/training")]
    public async Task<IActionResult> AddTraining(string code, [FromBody] TrainingUpdate t)
    { t.ClientCode = code; t.CreatedBy = this.CurrentUserId(); t.Id = await _repo.AddTrainingAsync(t); return Ok(t); }

    [HttpPut("{code}/training/{id:int}")]
    public async Task<IActionResult> UpdateTraining(string code, int id, [FromBody] TrainingUpdate t)
    { t.Id = id; t.ClientCode = code; t.ModifiedBy = this.CurrentUserId(); return await _repo.UpdateTrainingAsync(t) ? Ok(t) : NotFound(); }

    [HttpDelete("{code}/training/{id:int}")]
    public async Task<IActionResult> DeleteTraining(int id) => await _repo.DeleteTrainingAsync(id, this.CurrentUserId()) ? NoContent() : NotFound();

    // -------- Change Requests --------
    [HttpPost("{code}/changerequests")]
    public async Task<IActionResult> AddCr(string code, [FromBody] ChangeRequest x)
    { x.ClientCode = code; x.CreatedBy = this.CurrentUserId(); x.Id = await _repo.AddChangeRequestAsync(x); return Ok(x); }

    [HttpPut("{code}/changerequests/{id:int}")]
    public async Task<IActionResult> UpdateCr(string code, int id, [FromBody] ChangeRequest x)
    { x.Id = id; x.ClientCode = code; x.ModifiedBy = this.CurrentUserId(); return await _repo.UpdateChangeRequestAsync(x) ? Ok(x) : NotFound(); }

    public sealed class ToPointRequest { public string? ClientName { get; set; } public string? Application { get; set; } }

    /// <summary>Send a Change Request to Point Management as a new Point; returns the Ticket (Point) id.</summary>
    [HttpPost("{code}/changerequests/{id:int}/to-point")]
    public async Task<IActionResult> ChangeRequestToPoint(string code, int id, [FromBody] ToPointRequest? body)
    {
        try
        {
            var (pointId, product) = await _repo.SendChangeRequestToPointAsync(code, id, this.CurrentUserId(), body?.ClientName, body?.Application);
            return Ok(new { success = true, pointId, product });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpDelete("{code}/changerequests/{id:int}")]
    public async Task<IActionResult> DeleteCr(int id) => await _repo.DeleteChangeRequestAsync(id, this.CurrentUserId()) ? NoContent() : NotFound();

    // -------- Support --------
    [HttpPost("{code}/support")]
    public async Task<IActionResult> AddSupport(string code, [FromBody] SupportLog s)
    { s.ClientCode = code; s.CreatedBy = this.CurrentUserId(); s.Id = await _repo.AddSupportAsync(s); return Ok(s); }

    [HttpPut("{code}/support/{id:int}")]
    public async Task<IActionResult> UpdateSupport(string code, int id, [FromBody] SupportLog s)
    { s.Id = id; s.ClientCode = code; s.ModifiedBy = this.CurrentUserId(); return await _repo.UpdateSupportAsync(s) ? Ok(s) : NotFound(); }

    [HttpDelete("{code}/support/{id:int}")]
    public async Task<IActionResult> DeleteSupport(int id) => await _repo.DeleteSupportAsync(id, this.CurrentUserId()) ? NoContent() : NotFound();

    // -------- Onsite --------
    [HttpPost("{code}/onsite")]
    public async Task<IActionResult> AddOnsite(string code, [FromBody] OnsiteVisit o)
    { o.ClientCode = code; o.CreatedBy = this.CurrentUserId(); o.Id = await _repo.AddOnsiteAsync(o); return Ok(o); }

    [HttpPut("{code}/onsite/{id:int}")]
    public async Task<IActionResult> UpdateOnsite(string code, int id, [FromBody] OnsiteVisit o)
    { o.Id = id; o.ClientCode = code; o.ModifiedBy = this.CurrentUserId(); return await _repo.UpdateOnsiteAsync(o) ? Ok(o) : NotFound(); }

    [HttpDelete("{code}/onsite/{id:int}")]
    public async Task<IActionResult> DeleteOnsite(int id) => await _repo.DeleteOnsiteAsync(id, this.CurrentUserId()) ? NoContent() : NotFound();
}
