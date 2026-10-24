using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Domain;
using ShiftManager.Services;
namespace ShiftManager.Api;

[ApiController, Route("api/users"), Authorize(Policy = "ADMIN")]
public sealed class UsersController(UserManager<ApplicationUser> users, AccountService accounts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserPage>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100 || search?.Length > 100) throw new ApiException("VALIDATION_ERROR");
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            var normalized = users.NormalizeName(value);
            query = query.Where(x => x.FirstName.Contains(value) || x.LastName.Contains(value) || x.NormalizedUserName!.Contains(normalized!));
        }
        var count = await query.CountAsync();
        var rows = await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var result = new List<UserResponse>();
        foreach (var row in rows) result.Add(await accounts.Response(row));
        return Ok(new UserPage(result.ToArray(), count, page, pageSize));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id) => Ok(await accounts.Response(await accounts.Find(id)));
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest input)
    {
        var user = await accounts.Create(input, User);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, ProfileRequest input) => Ok(await accounts.Update(id, User, profile: input));
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<UserResponse>> Status(Guid id, StatusRequest input) => Ok(await accounts.Update(id, User, active: input.IsActive!.Value));
    [HttpPut("{id:guid}/roles")]
    public async Task<ActionResult<UserResponse>> Roles(Guid id, RolesRequest input) => Ok(await accounts.Update(id, User, roles: input.Roles));
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> Reset(Guid id, ResetPasswordRequest input)
    {
        await accounts.Update(id, User, password: input.TemporaryPassword);
        return NoContent();
    }
}
