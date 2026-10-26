using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;
namespace ShiftManager.Api;

[ApiController, Route("api/admin/scheduling"), Authorize(Policy = "ADMIN")]
public sealed class SchedulingController(SchedulingService scheduling) : ControllerBase
{
    [HttpGet("board")]
    public async Task<ActionResult<WeeklyBoardResponse>> Board([FromQuery] DateOnly? date) => Ok(await scheduling.Board(date));
    [HttpGet("shift-types")]
    public async Task<ActionResult<ShiftTypeResponse[]>> List() => Ok(await scheduling.List());
    [HttpPut("shift-types")]
    public async Task<ActionResult<ShiftTypeResponse[]>> Update(UpdateShiftTypesRequest input) => Ok(await scheduling.Update(input, User));
    [HttpGet("automatic-configuration")]
    public async Task<ActionResult<AutomaticSchedulingConfigurationResponse>> Automatic() => Ok(await scheduling.GetConfiguration());
}
