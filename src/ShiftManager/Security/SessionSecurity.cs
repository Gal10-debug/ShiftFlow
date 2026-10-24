using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ShiftManager.Domain;
using ShiftManager.Api;
namespace ShiftManager.Security;

public static class AppRoles
{
    public static readonly string[] All = ["ADMIN", "COORDINATOR", "EMPLOYEE"];
}

public sealed class AppClaimsFactory(UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var claims = await base.GenerateClaimsAsync(user);
        claims.AddClaim(new Claim("password_change_required", user.MustChangePassword ? "true" : "false"));
        return claims;
    }
}

public sealed class AppCookieEvents(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var user = context.Principal is null ? null : await users.GetUserAsync(context.Principal);
        var stamp = context.Principal?.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        if (user is null || !user.IsActive || stamp is null || stamp != user.SecurityStamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 401;
        return context.Response.WriteAsJsonAsync(ApiErrors.Error("UNAUTHENTICATED"));
    }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 403;
        var code = context.HttpContext.User.HasClaim("password_change_required", "true") ? "PASSWORD_CHANGE_REQUIRED" : "FORBIDDEN";
        return context.Response.WriteAsJsonAsync(ApiErrors.Error(code));
    }
}
