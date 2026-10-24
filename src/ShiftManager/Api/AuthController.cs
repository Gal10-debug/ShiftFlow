using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShiftManager.Domain;
using ShiftManager.Data;
using ShiftManager.Services;
namespace ShiftManager.Api;

[ApiController, Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    AccountService accounts, ApplicationDbContext db) : ControllerBase
{
    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, Guid.NewGuid().ToString());

    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)
        => Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest input)
    {
        var user = await users.FindByNameAsync(input.Username);
        if (user is null || !user.IsActive)
        {
            DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, input.Password);
            throw new ApiException("INVALID_CREDENTIALS", 401);
        }
        var result = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
        if (!result.Succeeded) throw new ApiException("INVALID_CREDENTIALS", 401);
        await signIn.SignInAsync(user, isPersistent: false);
        return Ok(await accounts.Response(user));
    }

    [HttpGet("me"), Authorize(Policy = "SignedIn")]
    public async Task<ActionResult<UserResponse>> Me() => Ok(await accounts.Response((await users.GetUserAsync(User))!));

    [HttpPost("logout"), Authorize(Policy = "SignedIn")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("change-password"), Authorize(Policy = "SignedIn")]
    public async Task<ActionResult<UserResponse>> ChangePassword(ChangePasswordRequest input)
    {
        if (input.NewPassword == input.CurrentPassword) throw new ApiException("PASSWORD_REUSED");
        await using var tx = await db.Database.BeginTransactionAsync();
        await accounts.Lock();
        var user = (await users.GetUserAsync(User))!;
        if (!user.IsActive || user.SecurityStamp != User.FindFirst("AspNet.Identity.SecurityStamp")?.Value)
            throw new ApiException("UNAUTHENTICATED", 401);
        var result = await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (!result.Succeeded) throw new ApiException("PASSWORD_CHANGE_FAILED");
        user.MustChangePassword = false;
        ApiErrors.Ensure(await users.UpdateAsync(user));
        await tx.CommitAsync();
        await signIn.RefreshSignInAsync(user);
        return Ok(await accounts.Response(user));
    }
}
