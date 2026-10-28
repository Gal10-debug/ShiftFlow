using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;
namespace ShiftManager.Api;
[ApiController, Route("api/admin/scheduling/periods/{year:int}/{month:int}"), Authorize(Policy = "ADMIN")]
public sealed class ScheduleReviewController(ScheduleReviewService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Review(int year, int month) => Ok(await service.Review(year, month, User));
    [HttpGet("candidates")] public async Task<IActionResult> Candidates(int year, int month, [FromQuery] Guid definitionId, [FromQuery] DateOnly date, [FromQuery] Guid? replacing) => Ok(await service.Candidates(year, month, definitionId, date, replacing, User));
    [HttpPost("assignments")] public async Task<IActionResult> Manual(int year, int month, ManualAssignmentRequest input) => Ok(await service.Manual(year, month, input, User));
    [HttpPost("on-call-pair")] public async Task<IActionResult> Pair(int year, int month, OnCallPairRequest input) => Ok(await service.OnCallPair(year, month, input, User));
    [HttpPost("review")] public async Task<IActionResult> Acknowledge(int year, int month, ReviewTokenRequest input) => Ok(await service.Acknowledge(year, month, input, User));
    [HttpPost("correction")] public async Task<IActionResult> Correction(int year, int month, ReviewTokenRequest input) => Ok(await service.Correction(year, month, input, User));
    [HttpPost("publications/{type}")] public async Task<IActionResult> Publish(int year, int month, string type, PublishScheduleRequest input) => Ok(await service.Publish(year, month, type, input, User));
    [HttpGet("publications")] public async Task<IActionResult> History(int year, int month) => Ok(await service.History(year, month, User));
    [HttpGet("issues")] public async Task<IActionResult> Issues(int year, int month) => Ok(await service.Issues(year, month, User, true));
    [HttpPut("issues/{id:guid}")] public async Task<IActionResult> Update(int year, int month, Guid id, UpdateIssueRequest input) => Ok(await service.UpdateIssue(year, month, id, input, User));
    [HttpGet("audit")] public async Task<IActionResult> Audit(int year, int month) => Ok(await service.Audit(year, month, User));
}
[ApiController, Route("api/my-schedule"), Authorize(Policy = "EMPLOYEE")]
public sealed class EmployeeScheduleController(ScheduleReviewService service) : ControllerBase
{
    [HttpGet("periods")] public async Task<IActionResult> Periods() => Ok(await service.PublishedPeriods(User));
    [HttpGet("{year:int}/{month:int}")] public async Task<IActionResult> Mine(int year, int month) => Ok(await service.Mine(year, month, User));
    [HttpGet("{year:int}/{month:int}/issues")] public async Task<IActionResult> Issues(int year, int month) => Ok(await service.Issues(year, month, User, false));
    [HttpPost("{year:int}/{month:int}/issues")] public async Task<IActionResult> Report(int year, int month, CreateIssueRequest input) => StatusCode(201, await service.Report(year, month, input, User));
}
