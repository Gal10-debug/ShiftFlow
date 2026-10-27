using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Api;

[ApiController, Route("api/admin/scheduling"), Authorize(Policy = "ADMIN")]
public sealed class SchedulingController(SchedulingService scheduling, GenerationQueue generation, GeneratedDraftService drafts) : ControllerBase
{
    [HttpPost("generation")]
    public ActionResult<GenerationResponse> Generate(GenerateScheduleRequest input) => Accepted(generation.Start(input.Year, input.Month));
    [HttpGet("generation/{id:guid}")]
    public ActionResult<GenerationResponse> Result(Guid id) => Ok(generation.Get(id).Response);
    [HttpGet("generation/{id:guid}/shortages")]
    public ActionResult<GeneratedCell[]> Shortages(Guid id) => Ok(generation.Get(id).Response.Cells.Where(c => c.Shortage > 0).ToArray());
    [HttpDelete("generation/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id) { await generation.Cancel(id); return NoContent(); }
    [HttpPost("generation/{id:guid}/save")]
    public async Task<ActionResult<GenerationResponse>> Save(Guid id, CancellationToken ct) => Ok(await drafts.Save(id, User, ct));
    [HttpGet("board")]
    public async Task<ActionResult<WeeklyBoardResponse>> Board([FromQuery] DateOnly? date, [FromQuery] bool monthly = false) => Ok(await scheduling.Board(date, monthly));
    [HttpGet("shift-types")]
    public async Task<ActionResult<ShiftTypeResponse[]>> List() => Ok(await scheduling.List());
    [HttpPut("shift-types")]
    public async Task<ActionResult<ShiftTypeResponse[]>> Update(UpdateShiftTypesRequest input) => Ok(await scheduling.Update(input, User));
    [HttpGet("automatic-configuration")]
    public async Task<ActionResult<AutomaticSchedulingConfigurationResponse>> Automatic() => Ok(await scheduling.GetConfiguration());
}
