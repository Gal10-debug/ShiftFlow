using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;
namespace ShiftManager.Api;

[ApiController, Route("api/admin/teams"), Authorize(Policy = "ADMIN")]
public sealed class TeamsAdminController(TeamService teams, ApplicationDbContext db, RosterValidationService roster) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List() => Ok(await teams.List(User, true));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, UpdateTeamRequest input)
    {
        await teams.Update(id, input, User);
        return Ok(new { teams = await teams.List(User, true), conflicts = await roster.Conflicts() });
    }
    [HttpPut("members/{userId:guid}")] public async Task<IActionResult> Membership(Guid userId, MembershipRequest input)
    {
        await teams.Membership(userId, input.TeamId, User); return Ok(await teams.List(User, true));
    }
    [HttpGet("conflicts")] public async Task<IActionResult> Conflicts() { await teams.Actor(User, true); return Ok(await roster.Conflicts()); }
    [HttpGet("audit")] public async Task<IActionResult> Audit([FromQuery] int page = 1)
    {
        await teams.Actor(User, true);
        if (page < 1 || page > 1000000) throw new ApiException("VALIDATION_ERROR");
        var events = from a in db.Set<DelegatedAvailabilityAudit>().AsNoTracking()
                     join period in db.Set<SchedulePeriod>() on a.SchedulePeriodId equals period.Id
                     orderby a.OccurredAt descending, a.Id
                     select new { a.Id, a.ActorUserId, a.TargetUserId, a.TeamId, a.SchedulePeriodId,
                         period.Year, period.Month, a.OccurredAt, a.InitialSubmission, a.PreviousValues, a.NewValues };
        return Ok(await events.Skip((page - 1) * 50).Take(50).ToArrayAsync());
    }
}
[ApiController, Route("api/team-availability"), Authorize]
public sealed class TeamAvailabilityController(TeamService teams, AvailabilityService availability) : ControllerBase
{
    [HttpGet("members")] public async Task<IActionResult> Members() => Ok(await teams.List(User, false));
    [HttpGet("employees/{userId:guid}/periods")] public async Task<IActionResult> Periods(Guid userId)
    {
        return Ok(await availability.Periods(userId, User));
    }
    [HttpGet("employees/{userId:guid}/{year:int}/{month:int}")]
    public async Task<IActionResult> Get(Guid userId, int year, int month) => Ok(await availability.Get(year, month, userId, User));
    [HttpPost("employees/{userId:guid}/{year:int}/{month:int}")]
    public async Task<IActionResult> Submit(Guid userId, int year, int month, SubmitAvailabilityRequest input) =>
        StatusCode(201, await availability.SubmitDelegated(year, month, userId, input, User, false));
    [HttpPut("employees/{userId:guid}/{year:int}/{month:int}")]
    public async Task<IActionResult> Update(Guid userId, int year, int month, SubmitAvailabilityRequest input) =>
        Ok(await availability.SubmitDelegated(year, month, userId, input, User, true));
}
