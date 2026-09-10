using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.DTOs;
using Backend.Services;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class MessageFormatController : ControllerBase
{
    private readonly IMessageFormatService _service;

    public MessageFormatController(IMessageFormatService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllActive()
    {
        var response = await _service.GetAllActiveAsync();
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MessageFormatSaveRequest request)
    {
        var response = await _service.CreateAsync(request);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] MessageFormatSaveRequest request)
    {
        var response = await _service.UpdateAsync(request);
        return Ok(response);
    }

    [HttpDelete("{messageId}")]
    public async Task<IActionResult> Delete(int messageId)
    {
        var response = await _service.DeleteAsync(messageId);
        return Ok(response);
    }
}
