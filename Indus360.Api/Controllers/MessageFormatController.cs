using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Predefined ERP message templates for the "Format Message" picker on the
/// Company Detail / subscription form. Backed by dbo.MessageFormatMaster in the
/// Indus control DB (no new table — same table BulkImport's Company Subscription uses).
/// </summary>
[ApiController]
[Route("api/messageformat")]
public sealed class MessageFormatController : ControllerBase
{
    private readonly SubscriptionRepository _repo;
    public MessageFormatController(SubscriptionRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(new { success = true, message = "OK", data = await _repo.GetMessageFormatsAsync() });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MessageFormatSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.MessageTitle) || string.IsNullOrWhiteSpace(req.MessageContent))
            return BadRequest(new { success = false, message = "Title and content are required." });
        var id = await _repo.CreateMessageFormatAsync(req);
        return Ok(new { success = true, message = "Template created.", data = new { messageID = id } });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] MessageFormatSaveRequest req)
    {
        if (req.MessageID is null or 0)
            return BadRequest(new { success = false, message = "MessageID is required." });
        if (string.IsNullOrWhiteSpace(req.MessageTitle) || string.IsNullOrWhiteSpace(req.MessageContent))
            return BadRequest(new { success = false, message = "Title and content are required." });
        var rows = await _repo.UpdateMessageFormatAsync(req);
        return rows > 0
            ? Ok(new { success = true, message = "Template updated." })
            : NotFound(new { success = false, message = "Template not found." });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var rows = await _repo.DeleteMessageFormatAsync(id);
        return rows > 0
            ? Ok(new { success = true, message = "Template deleted." })
            : NotFound(new { success = false, message = "Template not found." });
    }
}
