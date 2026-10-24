using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;
using Testcontainers.PostgreSql;

namespace ShiftManager.Tests;

public sealed class AuthTests : IAsyncLifetime
{
    private const string Password = "TestPassword!2026";
    private const string NewPassword = "PersonalSecret!2026";
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;
    private readonly Dictionary<string, Guid> ids = [];

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Database", database.GetConnectionString()));
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (name, roles, active, forced) in new[] {
            ("admin", new[] { "ADMIN" }, true, false), ("employee", new[] { "EMPLOYEE" }, true, false),
            ("coordinator", new[] { "COORDINATOR" }, true, false), ("inactive", new[] { "EMPLOYEE" }, false, false),
            ("firstlogin", new[] { "EMPLOYEE" }, true, true) })
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = name, FirstName = "ישראל", LastName = "ישראלי", IsActive = active, MustChangePassword = forced, LockoutEnabled = true };
            Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
            Assert.True((await manager.AddToRolesAsync(user, roles)).Succeeded);
            ids[name] = user.Id;
        }
    }
    public async Task DisposeAsync() { await factory.DisposeAsync(); await database.DisposeAsync(); }
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object? body = null, string method = "POST", bool csrf = true)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (csrf)
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
        }
        return await client.SendAsync(request);
    }
    private static async Task Login(HttpClient client, string username = "admin", string password = Password)
    {
        var response = await Send(client, "/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(code, body!.Code);
        Assert.Matches("[א-ת]", body.Message);
    }

    [PostgresFact]
    public async Task Login_issues_secure_cookie_and_safe_profile_and_logout_removes_access()
    {
        using var client = Client();
        var response = await Send(client, "/api/auth/login", new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("ShiftFlow.Auth="));
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=strict", cookie);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json); Assert.DoesNotContain("securityStamp", json); Assert.DoesNotContain(Password, json);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/logout")).StatusCode);
        await Error(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [PostgresFact]
    public async Task Invalid_missing_and_inactive_accounts_return_same_generic_error()
    {
        using var client = Client();
        foreach (var (username, password) in new[] { ("admin", "WrongPassword!2026"), ("missing", Password), ("inactive", Password) })
            await Error(await Send(client, "/api/auth/login", new { username, password }), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [PostgresFact]
    public async Task Five_failed_attempts_lock_account_even_with_correct_password()
    {
        using var client = Client();
        for (var i = 0; i < 5; i++)
            await Error(await Send(client, "/api/auth/login", new { username = "employee", password = "WrongPassword!2026" }), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await Error(await Send(client, "/api/auth/login", new { username = "employee", password = Password }), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await manager.IsLockedOutAsync((await manager.FindByNameAsync("employee"))!));
    }

    [PostgresFact]
    public async Task Login_rate_limit_rejects_eleventh_attempt()
    {
        using var client = Client();
        for (var i = 0; i < 10; i++) await Send(client, "/api/auth/login", new { username = "missing", password = Password });
        await Error(await Send(client, "/api/auth/login", new { username = "missing", password = Password }), HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    [PostgresFact]
    public async Task Protected_endpoints_require_login_and_registration_is_absent()
    {
        using var client = Client();
        await Error(await client.GetAsync("/api/users"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, "/api/auth/register", new { username = "new", password = Password })).StatusCode);
    }

    [PostgresFact]
    public async Task Employee_and_coordinator_cannot_use_admin_endpoints()
    {
        foreach (var name in new[] { "employee", "coordinator" })
        {
            using var client = Client(); await Login(client, name);
            await Error(await client.GetAsync("/api/users"), HttpStatusCode.Forbidden, "FORBIDDEN");
            await Error(await Send(client, "/api/users", new { firstName = "שם", lastName = "משפחה", username = "escalated", initialPassword = Password, roles = new[] { "ADMIN" } }), HttpStatusCode.Forbidden, "FORBIDDEN");
            Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/access/{name}")).StatusCode);
        }
    }

    [PostgresFact]
    public async Task Administrator_creates_account_with_roles_and_duplicate_names_are_rejected()
    {
        using var client = Client(); await Login(client);
        var request = new { firstName = "דנה", lastName = "כהן", username = "newuser", initialPassword = Password, roles = new[] { "EMPLOYEE", "COORDINATOR" }, isActive = true };
        var response = await Send(client, "/api/users", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.True(user.MustChangePassword); Assert.Equal(2, user.Roles.Length); Assert.Equal("דנה", user.FirstName);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        await Error(await Send(client, "/api/users", request), HttpStatusCode.Conflict, "DUPLICATE_USERNAME");
        var page = await client.GetFromJsonAsync<UserPage>("/api/users?search=דנה&pageSize=1");
        Assert.Single(page!.Items); Assert.Equal(1, page.TotalCount);
    }

    [PostgresFact]
    public async Task First_login_gate_cannot_be_bypassed_and_password_change_refreshes_session()
    {
        using var client = Client(); await Login(client, "firstlogin");
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me"); Assert.True(me!.MustChangePassword);
        await Error(await client.GetAsync("/api/access/employee"), HttpStatusCode.Forbidden, "PASSWORD_CHANGE_REQUIRED");
        var changed = await Send(client, "/api/auth/change-password", new { currentPassword = Password, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<UserResponse>())!.MustChangePassword);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/access/employee")).StatusCode);
        using var oldPasswordClient = Client();
        await Error(await Send(oldPasswordClient, "/api/auth/login", new { username = "firstlogin", password = Password }), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [PostgresFact]
    public async Task Password_change_requires_correct_current_and_distinct_strong_password()
    {
        using var client = Client(); await Login(client, "employee");
        await Error(await Send(client, "/api/auth/change-password", new { currentPassword = "wrong", newPassword = NewPassword }), HttpStatusCode.BadRequest, "PASSWORD_CHANGE_FAILED");
        await Error(await Send(client, "/api/auth/change-password", new { currentPassword = Password, newPassword = Password }), HttpStatusCode.BadRequest, "PASSWORD_REUSED");
        await Error(await Send(client, "/api/auth/change-password", new { currentPassword = Password, newPassword = "onlylowercaseletters" }), HttpStatusCode.BadRequest, "PASSWORD_CHANGE_FAILED");
    }

    [PostgresFact]
    public async Task Administrative_reset_invalidates_existing_sessions_and_requires_password_change()
    {
        using var admin = Client(); using var employee = Client(); await Login(admin); await Login(employee, "employee");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, $"/api/users/{ids["employee"]}/reset-password", new { temporaryPassword = NewPassword })).StatusCode);
        await Error(await employee.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Login(employee, "employee", NewPassword);
        Assert.True((await employee.GetFromJsonAsync<UserResponse>("/api/auth/me"))!.MustChangePassword);
        await Error(await Send(admin, $"/api/users/{ids["admin"]}/reset-password", new { temporaryPassword = NewPassword }), HttpStatusCode.BadRequest, "SELF_RESET");
    }

    [PostgresFact]
    public async Task Deactivation_revokes_session_and_reactivation_allows_new_login()
    {
        using var admin = Client(); using var employee = Client(); await Login(admin); await Login(employee, "employee");
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/users/{ids["employee"]}/status", new { isActive = false }, "PATCH")).StatusCode);
        await Error(await employee.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Error(await Send(employee, "/api/auth/login", new { username = "employee", password = Password }), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/users/{ids["employee"]}/status", new { isActive = true }, "PATCH")).StatusCode);
        await Login(employee, "employee");
    }

    [PostgresFact]
    public async Task Role_grants_and_revocations_require_new_login_and_support_multiple_roles()
    {
        using var admin = Client(); using var employee = Client(); await Login(admin); await Login(employee, "employee");
        var path = $"/api/users/{ids["employee"]}/roles";
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, path, new { roles = new[] { "EMPLOYEE", "COORDINATOR" } }, "PUT")).StatusCode);
        await Error(await employee.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Login(employee, "employee");
        Assert.Equal(HttpStatusCode.NoContent, (await employee.GetAsync("/api/access/coordinator")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, path, new { roles = new[] { "EMPLOYEE" } }, "PUT")).StatusCode);
        await Error(await employee.GetAsync("/api/access/coordinator"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Login(employee, "employee");
        await Error(await employee.GetAsync("/api/access/coordinator"), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [PostgresFact]
    public async Task Last_active_admin_cannot_be_deactivated_or_demoted()
    {
        using var admin = Client(); await Login(admin);
        await Error(await Send(admin, $"/api/users/{ids["admin"]}/status", new { isActive = false }, "PATCH"), HttpStatusCode.Conflict, "LAST_ADMIN");
        await Error(await Send(admin, $"/api/users/{ids["admin"]}/roles", new { roles = new[] { "EMPLOYEE" } }, "PUT"), HttpStatusCode.Conflict, "LAST_ADMIN");
    }

    [PostgresFact]
    public async Task Csrf_is_required_on_login_logout_and_account_writes()
    {
        using var client = Client();
        await Error(await Send(client, "/api/auth/login", new { username = "admin", password = Password }, csrf: false), HttpStatusCode.BadRequest, "CSRF_INVALID");
        await Login(client);
        await Error(await Send(client, "/api/auth/logout", csrf: false), HttpStatusCode.BadRequest, "CSRF_INVALID");
        await Error(await Send(client, $"/api/users/{ids["employee"]}/roles", new { roles = new[] { "ADMIN" } }, "PUT", csrf: false), HttpStatusCode.BadRequest, "CSRF_INVALID");
        using var foreign = Client();
        var token = await foreign.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        forged.Headers.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
        await Error(await client.SendAsync(forged), HttpStatusCode.BadRequest, "CSRF_INVALID");
    }

    [PostgresFact]
    public async Task Profile_edits_are_safe_and_unknown_fields_roles_and_pagination_are_rejected()
    {
        using var client = Client(); await Login(client);
        var path = $"/api/users/{ids["employee"]}";
        var updated = await Send(client, path, new { firstName = "יעל", lastName = "לוי", username = "renamed" }, "PUT");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("יעל", (await updated.Content.ReadFromJsonAsync<UserResponse>())!.FirstName);
        await Error(await Send(client, path, new { firstName = "שם", lastName = "שם", username = "renamed", isAdmin = true }, "PUT"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await Send(client, path + "/roles", new { roles = new[] { "SHIFT_LEAD" } }, "PUT"), HttpStatusCode.BadRequest, "INVALID_ROLES");
        await Error(await client.GetAsync("/api/users?pageSize=101"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var list = await client.GetStringAsync("/api/users"); Assert.DoesNotContain("passwordHash", list); Assert.DoesNotContain("securityStamp", list);
    }

    [PostgresFact]
    public async Task Failed_creation_does_not_leave_partially_created_account()
    {
        using var client = Client(); await Login(client);
        await Error(await Send(client, "/api/users", new { firstName = "שם", lastName = "שם", username = "badroles", initialPassword = Password, roles = new[] { "SHIFT_LEAD" } }), HttpStatusCode.BadRequest, "INVALID_ROLES");
        var page = await client.GetFromJsonAsync<UserPage>("/api/users?search=badroles"); Assert.Empty(page!.Items);
    }

    [PostgresFact]
    public async Task Bootstrap_fails_without_secrets_and_is_idempotent_for_existing_admin()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdminBootstrap.Run(factory.Services, new ConfigurationBuilder().Build()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BOOTSTRAP_ADMIN_USERNAME"] = "admin",
            ["BOOTSTRAP_ADMIN_PASSWORD"] = Password,
            ["BOOTSTRAP_ADMIN_FIRST_NAME"] = "מנהל",
            ["BOOTSTRAP_ADMIN_LAST_NAME"] = "ראשי"
        }).Build();
        await AdminBootstrap.Run(factory.Services, configuration);
        await AdminBootstrap.Run(factory.Services, configuration);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(5, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_admin_deactivations_preserve_one_active_administrator()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var second = new ApplicationUser { Id = Guid.NewGuid(), UserName = "secondadmin", FirstName = "מנהל", LastName = "נוסף" };
            Assert.True((await users.CreateAsync(second, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(second, "ADMIN")).Succeeded);
            ids["secondadmin"] = second.Id;
        }
        using var first = Client(); using var secondClient = Client();
        await Login(first); await Login(secondClient, "secondadmin");
        var results = await Task.WhenAll(
            Send(first, $"/api/users/{ids["admin"]}/status", new { isActive = false }, "PATCH"),
            Send(secondClient, $"/api/users/{ids["secondadmin"]}/status", new { isActive = false }, "PATCH"));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        await Error(Assert.Single(results, x => x.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "LAST_ADMIN");
        using var check = factory.Services.CreateScope();
        var manager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single((await manager.GetUsersInRoleAsync("ADMIN")), x => x.IsActive);
    }

    [PostgresFact]
    public async Task Bootstrap_creates_first_admin_from_secret_settings_without_duplicates()
    {
        // This is a disposable Testcontainers database, never the configured development database.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UserRoles.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
        }
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BOOTSTRAP_ADMIN_USERNAME"] = "bootstrap",
            ["BOOTSTRAP_ADMIN_PASSWORD"] = Password,
            ["BOOTSTRAP_ADMIN_FIRST_NAME"] = "מנהל",
            ["BOOTSTRAP_ADMIN_LAST_NAME"] = "ראשי"
        }).Build();
        await AdminBootstrap.Run(factory.Services, settings);
        await AdminBootstrap.Run(factory.Services, settings);
        using var check = factory.Services.CreateScope();
        var manager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = Assert.Single(await manager.Users.ToListAsync());
        Assert.True(user.MustChangePassword); Assert.True(await manager.IsInRoleAsync(user, "ADMIN"));
        Assert.NotEqual(Password, user.PasswordHash); Assert.True(await manager.CheckPasswordAsync(user, Password));
    }

    [PostgresFact]
    public async Task Production_cookies_require_https_and_use_host_prefix()
    {
        await using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var insecure = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        var blocked = await insecure.GetAsync("/api/auth/csrf");
        Assert.True(blocked.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect);
        Assert.False(blocked.Headers.Contains("Set-Cookie"));
        var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Contains(csrf.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-ShiftFlow.Csrf=") && c.Contains("secure") && c.Contains("httponly") && c.Contains("path=/"));
        var response = await Send(client, "/api/auth/login", new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-ShiftFlow.Auth=") && c.Contains("secure") && c.Contains("httponly") && c.Contains("samesite=strict"));
    }

    [PostgresFact]
    public async Task Password_change_invalidates_other_sessions_and_status_requires_explicit_value()
    {
        using var first = Client(); using var other = Client(); using var admin = Client();
        await Login(first, "employee"); await Login(other, "employee"); await Login(admin);
        Assert.Equal(HttpStatusCode.OK, (await Send(first, "/api/auth/change-password", new { currentPassword = Password, newPassword = NewPassword })).StatusCode);
        await Error(await other.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        await Error(await Send(admin, $"/api/users/{ids["employee"]}/status", new { }, "PATCH"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }
}
