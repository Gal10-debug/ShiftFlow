using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;

namespace ShiftManager.Api;

[ApiController, Route("api/my-statistics"), Authorize(Policy = "EMPLOYEE")]
public sealed class PersonalAnalyticsController(SchedulingAnalyticsService service) : ControllerBase
{
    [HttpGet("{year:int}/{month:int}")]
    public async Task<IActionResult> Monthly(int year, int month, [FromQuery] int? version)
    { AnalyticsQuery.Validate(Request.Query, "version"); return Ok(await service.Personal(year, month, User, version)); }
    [HttpGet("{year:int}")]
    public async Task<IActionResult> Yearly(int year)
    { AnalyticsQuery.Validate(Request.Query); return Ok(await service.Personal(year, null, User)); }
}

[ApiController, Route("api/admin/statistics"), Authorize(Policy = "ADMIN")]
public sealed class AdminAnalyticsController(SchedulingAnalyticsService service) : ControllerBase
{
    [HttpGet("{year:int}/{month:int}")]
    public async Task<IActionResult> Monthly(int year, int month, [FromQuery] Guid? teamId, [FromQuery] Guid? employeeId, [FromQuery] int? version)
    {
        AnalyticsQuery.Validate(Request.Query, "teamId", "employeeId", "version");
        return Ok(await service.Administrative(year, month, User, teamId, employeeId, version));
    }
    [HttpGet("{year:int}")]
    public async Task<IActionResult> Yearly(int year, [FromQuery] Guid? teamId, [FromQuery] Guid? employeeId)
    {
        AnalyticsQuery.Validate(Request.Query, "teamId", "employeeId");
        return Ok(await service.Administrative(year, null, User, teamId, employeeId));
    }
}

internal static class AnalyticsQuery
{
    public static void Validate(IQueryCollection query, params string[] allowed)
    {
        if (query.Any(p => p.Value.Count != 1 || string.IsNullOrWhiteSpace(p.Value[0]) || !allowed.Contains(p.Key, StringComparer.OrdinalIgnoreCase)))
            throw new ApiException("VALIDATION_ERROR");
    }
}
