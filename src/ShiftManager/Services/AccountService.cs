using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Security;
namespace ShiftManager.Services;

public sealed class AccountService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public async Task<UserResponse> Response(ApplicationUser user) => new(user.Id, user.FirstName, user.LastName,
        user.UserName!, user.IsActive, user.MustChangePassword, user.CreatedAt, (await users.GetRolesAsync(user)).Order().ToArray());

    public async Task<ApplicationUser> Find(Guid id) => await users.FindByIdAsync(id.ToString()) ?? throw new ApiException("USER_NOT_FOUND", 404);

    public static string[] ValidateRoles(string[] roles)
    {
        if (roles.Length == 0 || roles.Distinct().Count() != roles.Length || roles.Any(x => !AppRoles.All.Contains(x)))
            throw new ApiException("INVALID_ROLES");
        return roles;
    }

    // Account writes share one cross-process lock. This serializes final-admin checks and updates.
    public async Task Lock()
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(4218072)");
        // Authentication may have loaded a user before waiting for the lock.
        db.ChangeTracker.Clear();
    }

    private async Task AuthorizeActor(ClaimsPrincipal actor)
    {
        var user = await users.GetUserAsync(actor);
        if (user is null || !user.IsActive || user.MustChangePassword || !await users.IsInRoleAsync(user, "ADMIN")
            || actor.FindFirstValue("AspNet.Identity.SecurityStamp") != user.SecurityStamp)
            throw new ApiException("FORBIDDEN", 403);
    }

    public async Task<UserResponse> Create(CreateUserRequest input, ClaimsPrincipal actor)
    {
        var roles = ValidateRoles(input.Roles);
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock();
        await AuthorizeActor(actor);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            UserName = input.Username,
            IsActive = input.IsActive,
            MustChangePassword = true,
            LockoutEnabled = true
        };
        ApiErrors.Ensure(await users.CreateAsync(user, input.InitialPassword));
        ApiErrors.Ensure(await users.AddToRolesAsync(user, roles));
        await tx.CommitAsync();
        return await Response(user);
    }

    public async Task<UserResponse> Update(Guid id, ClaimsPrincipal actor, ProfileRequest? profile = null,
        string[]? roles = null, bool? active = null, string? password = null)
    {
        if (roles is not null) ValidateRoles(roles);
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock();
        await AuthorizeActor(actor);
        var user = await Find(id);
        var oldRoles = await users.GetRolesAsync(user);
        var newRoles = roles ?? oldRoles.ToArray();
        if (user.IsActive && oldRoles.Contains("ADMIN") && (!(active ?? user.IsActive) || !newRoles.Contains("ADMIN")))
        {
            var activeAdmins = await (from u in db.Users
                                      join ur in db.UserRoles on u.Id equals ur.UserId
                                      join role in db.Roles on ur.RoleId equals role.Id
                                      where u.IsActive && role.NormalizedName == "ADMIN"
                                      select u.Id).CountAsync();
            if (activeAdmins <= 1) throw new ApiException("LAST_ADMIN", 409);
        }
        if (password is not null)
        {
            if (actor.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString()) throw new ApiException("SELF_RESET");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            ApiErrors.Ensure(await users.ResetPasswordAsync(user, token, password));
            user.MustChangePassword = true;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
        }
        if (profile is not null)
        {
            user.FirstName = profile.FirstName.Trim();
            user.LastName = profile.LastName.Trim();
            user.UserName = profile.Username;
        }
        if (active is not null) user.IsActive = active.Value;
        if (roles is not null)
        {
            ApiErrors.Ensure(await users.RemoveFromRolesAsync(user, oldRoles.Except(newRoles)));
            ApiErrors.Ensure(await users.AddToRolesAsync(user, newRoles.Except(oldRoles)));
        }
        // Profile, role, status and password changes invalidate all previously issued cookies.
        ApiErrors.Ensure(await users.UpdateSecurityStampAsync(user));
        await tx.CommitAsync();
        return await Response(user);
    }
}
