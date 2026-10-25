using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;

namespace ShiftManager.Api;

[ApiController, Route("api/availability"), Authorize(Policy = "EMPLOYEE")]
public sealed class AvailabilityController(AvailabilityService availability) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("periods")]
    public async Task<ActionResult<PeriodResponse[]>> Periods() => Ok(await availability.Periods(UserId));
    [HttpGet("{year:int}/{month:int}")]
    public async Task<ActionResult<AvailabilityResponse>> Get(int year, int month) => Ok(await availability.Get(year, month, UserId));
    [HttpPost("{year:int}/{month:int}")]
    public async Task<ActionResult<AvailabilityResponse>> Submit(int year, int month, SubmitAvailabilityRequest input) =>
        StatusCode(201, await availability.Submit(year, month, input, User, false));
    [HttpPut("{year:int}/{month:int}")]
    public async Task<ActionResult<AvailabilityResponse>> Update(int year, int month, SubmitAvailabilityRequest input) =>
        Ok(await availability.Submit(year, month, input, User, true));
}

[ApiController, Route("api/admin/availability/{year:int}/{month:int}"), Authorize(Policy = "ADMIN")]
public sealed class AvailabilityAdminController(AvailabilityService availability) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WindowSummaryResponse>> Get(int year, int month) => Ok(await availability.Summary(year, month));
    [HttpPut]
    public async Task<ActionResult<WindowSummaryResponse>> Configure(int year, int month, WindowRequest input) =>
        Ok(await availability.Configure(year, month, input, User));
    [HttpPut("state")]
    public async Task<ActionResult<WindowSummaryResponse>> State(int year, int month, WindowStateRequest input) =>
        Ok(await availability.SetState(year, month, input.IsOpen!.Value, User));
    [HttpGet("employees/{userId:guid}")]
    public async Task<ActionResult<AvailabilityResponse>> Employee(int year, int month, Guid userId) => Ok(await availability.Get(year, month, userId));
}
