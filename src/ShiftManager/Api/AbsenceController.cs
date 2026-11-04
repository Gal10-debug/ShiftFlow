using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;
namespace ShiftManager.Api;

[ApiController, Route("api/absences"), Authorize]
public sealed class AbsenceController(AbsenceService service) : ControllerBase
{
    [HttpGet("mine")] public async Task<IActionResult> Mine() => Ok(await service.List(User));
    [HttpPost] public async Task<IActionResult> Submit(AbsencePattern input) => StatusCode(201, await service.Submit(input, User));
    [HttpPost("{id:guid}/changes")] public async Task<IActionResult> Change(Guid id, AbsenceChange input) => StatusCode(201, await service.Change(id, input, User));
    [HttpPost("{id:guid}/cancel-pending")] public async Task<IActionResult> Cancel(Guid id, AbsenceVersion input) => Ok(await service.CancelPending(id, input, User));
    [HttpGet("manage")] public async Task<IActionResult> Manage([FromQuery] Guid? teamId, [FromQuery] Guid? userId,
        [FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? through) => Ok(await service.List(User, true, teamId, userId, status, from, through));
    [HttpGet("{id:guid}/conflicts")] public async Task<IActionResult> Conflicts(Guid id) => Ok(await service.Conflicts(id, User));
    [HttpPost("{id:guid}/decision")] public async Task<IActionResult> Decide(Guid id, AbsenceDecisionRequest input) => Ok(await service.Decide(id, input, User));
}
