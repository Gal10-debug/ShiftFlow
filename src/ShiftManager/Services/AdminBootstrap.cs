using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services;

public static class AdminBootstrap
{
    public static async Task Run(IServiceProvider services, IConfiguration configuration)
    {
        string Required(string name) => !string.IsNullOrWhiteSpace(configuration[name]) ? configuration[name]! : throw new InvalidOperationException($"Missing bootstrap setting: {name}");
        var username = Required("BOOTSTRAP_ADMIN_USERNAME");
        var password = Required("BOOTSTRAP_ADMIN_PASSWORD");
        var firstName = Required("BOOTSTRAP_ADMIN_FIRST_NAME");
        var lastName = Required("BOOTSTRAP_ADMIN_LAST_NAME");
        if (username.Length > 100 || firstName.Length > 100 || lastName.Length > 100 || password.Length > 128)
            throw new InvalidOperationException("Bootstrap settings exceed the supported lengths.");
        var input = new CreateUserRequest(firstName, lastName, username, password, ["ADMIN"]);
        if (!Validator.TryValidateObject(input, new ValidationContext(input), [], validateAllProperties: true))
            throw new InvalidOperationException("Bootstrap settings failed validation.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await accounts.Lock();
        var existing = await users.FindByNameAsync(username);
        if (existing is not null)
        {
            if (!existing.IsActive || !await users.IsInRoleAsync(existing, "ADMIN"))
                throw new InvalidOperationException("The bootstrap username already belongs to an account that is not an active administrator.");
            Console.WriteLine("Bootstrap administrator already exists; no changes made.");
            return;
        }
        if (await (from user in db.Users
                   join ur in db.UserRoles on user.Id equals ur.UserId
                   join role in db.Roles on ur.RoleId equals role.Id
                   where user.IsActive && role.NormalizedName == "ADMIN"
                   select user.Id).AnyAsync())
            throw new InvalidOperationException("An active administrator already exists. Use authenticated account management.");
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = username, FirstName = firstName.Trim(), LastName = lastName.Trim(), MustChangePassword = true, LockoutEnabled = true };
        ApiErrors.Ensure(await users.CreateAsync(admin, password));
        ApiErrors.Ensure(await users.AddToRoleAsync(admin, "ADMIN"));
        await tx.CommitAsync();
        Console.WriteLine("Bootstrap administrator created. A password change is required on first login.");
    }
}
