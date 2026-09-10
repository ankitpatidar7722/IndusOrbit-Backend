using Backend.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/keyline")]
    [Authorize]
    public class KeylineController : ControllerBase
    {
        private readonly IKeylineService _service;

        public KeylineController(IKeylineService service)
        {
            _service = service;
        }

        [HttpGet("content-names")]
        public async Task<IActionResult> GetContentNames()
        {
            var data = await _service.GetContentNamesAsync();
            return Ok(data);
        }

        [HttpGet("shape-names")]
        public async Task<IActionResult> GetShapeNames([FromQuery] string contentType, [FromQuery] string grain, [FromQuery] string upsType)
        {
            var data = await _service.GetShapeNamesAsync(contentType, grain, upsType);
            return Ok(data);
        }

        [HttpGet("coordinates")]
        public async Task<IActionResult> GetCoordinates([FromQuery] string contentType, [FromQuery] string grain, [FromQuery] string upsType)
        {
            var data = await _service.GetCoordinatesAsync(contentType, grain, upsType);
            return Ok(data);
        }

        [HttpGet("shape-wise-data")]
        public async Task<IActionResult> GetShapeWiseData([FromQuery] string contentType, [FromQuery] string grain, [FromQuery] string upsType, [FromQuery] string shapeName)
        {
            var data = await _service.GetShapeWiseDataAsync(contentType, grain, upsType, shapeName);
            return Ok(data);
        }

        [HttpGet("formulas")]
        public async Task<IActionResult> GetFormulas()
        {
            var data = await _service.GetFormulasAsync();
            return Ok(data);
        }

        [HttpGet("formula-values")]
        public async Task<IActionResult> GetFormulaValues([FromQuery] string axis, [FromQuery] string contentType, [FromQuery] string grain, [FromQuery] string upsType)
        {
            var data = await _service.GetFormulaValuesAsync(axis, contentType, grain, upsType);
            return Ok(data);
        }

        [HttpGet("meta")]
        public async Task<IActionResult> GetMeta([FromQuery] string contentType, [FromQuery] string grain, [FromQuery] string upsType)
        {
            var data = await _service.GetMetaAsync(contentType, grain, upsType);
            return Ok(data);
        }

        [HttpPost("save-coordinates")]
        public async Task<IActionResult> SaveCoordinates([FromBody] SaveCoordinatesRequest request)
        {
            await _service.SaveCoordinatesAsync(request);
            return Ok(new { message = "Coordinates saved successfully" });
        }

        [HttpPost("save-formula")]
        public async Task<IActionResult> SaveFormula([FromBody] SaveFormulaRequest request)
        {
            await _service.SaveFormulaAsync(request);
            return Ok(new { message = "Formula saved successfully" });
        }

        [HttpDelete("formula/{id}")]
        public async Task<IActionResult> DeleteFormula(int id)
        {
            await _service.DeleteFormulaAsync(id);
            return Ok(new { message = "Formula deleted" });
        }

        [HttpDelete("coordinates")]
        public async Task<IActionResult> DeleteCoordinates([FromQuery] string contentName, [FromQuery] string grain, [FromQuery] string upsType)
        {
            await _service.DeleteCoordinatesAsync(contentName, grain, upsType);
            return Ok(new { message = "Coordinates deleted" });
        }

        [HttpGet("planning")]
        public async Task<IActionResult> GetPlanning([FromQuery] string contentType)
        {
            var data = await _service.GetPlanningAsync(contentType);
            return Ok(data);
        }

        [HttpPost("save-planning")]
        public async Task<IActionResult> SavePlanning([FromBody] SavePlanningRequest request)
        {
            await _service.SavePlanningAsync(request);
            return Ok(new { message = "Planning saved successfully" });
        }

        [HttpDelete("planning")]
        public async Task<IActionResult> DeletePlanning([FromQuery] string contentName)
        {
            await _service.DeletePlanningAsync(contentName);
            return Ok(new { message = "Planning deleted" });
        }
    }
}
