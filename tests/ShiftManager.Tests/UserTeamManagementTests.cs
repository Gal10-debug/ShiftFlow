using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private async Task<UserResponse> CreateEmployee(HttpClient admin, string type, string[]? roles = null)
    {
        var response = await Send(admin, "/api/users", new CreateUserRequest("עובד", type, $"user.{type}", Password,
            roles ?? ["EMPLOYEE"], EmployeeType: type), "POST");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    [PostgresTheory]
    [InlineData("DISPATCHER")]
    [InlineData("MANAGER")]
    [InlineData("RESERVIST")]
    [InlineData("DUTY_MANAGER")]
    public async Task Managed_employee_types_persist_and_existing_new_users_join_teams_before_first_login(string type)
    {
        using var admin = await Login();
        var team = await SetupTeam(admin);
        var created = await CreateEmployee(admin, type);
        Assert.Equal(type, created.EmployeeType);
        Assert.Equal(new[] { "EMPLOYEE" }, created.Roles);
        Assert.True(created.MustChangePassword);
        var directory = (await admin.GetFromJsonAsync<UserPage>($"/api/users?search=user.{type}"))!;
        Assert.Equal(created.Id, Assert.Single(directory.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{created.Id}", new MembershipRequest(team.Id))).StatusCode);
        var savedTeam = (await Teams(admin)).Single(t => t.Id == team.Id);
        Assert.Contains(savedTeam.Members, m => m.Id == created.Id && m.IsEmployee);
        Assert.Equal(team.LeaderUserId, savedTeam.LeaderUserId);
        Assert.Equal(team.DeputyUserId, savedTeam.DeputyUserId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(type, (await db.Users.SingleAsync(u => u.Id == created.Id)).EmployeeType.ToString());
        Assert.Equal(team.Id, (await db.Set<TeamMembership>().SingleAsync(m => m.UserId == created.Id)).TeamId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.UserName == created.Username));
    }

    [PostgresFact]
    public async Task Manual_types_never_enter_automatic_candidates_and_manual_assignments_survive_generation_and_save()
    {
        using var admin = await Login();
        var definitions = await ConfigureAutomatic(2);
        var manual = new List<UserResponse>();
        foreach (var type in new[] { "DISPATCHER", "MANAGER", "RESERVIST", "DUTY_MANAGER" }) manual.Add(await CreateEmployee(admin, type));
        var review = await Review(admin);
        for (var i = 0; i < manual.Count; i++)
        {
            var day = i + 1;
            var candidates = (await admin.GetFromJsonAsync<CandidateResponse[]>($"{ReviewPath}/candidates?definitionId={definitions[0]}&date=2026-10-{day:00}"))!;
            Assert.True(candidates.Single(c => c.UserId == manual[i].Id).Eligible);
            review = await Edit(admin, review, "ASSIGN", manual[i].Id, definitions[0], day);
        }
        var original = review.Draft.Cells.SelectMany(c => c.Employees).ToDictionary(e => e.UserId, e => e.AssignmentId);
        using (var scope = factory.Services.CreateScope())
        {
            var inputs = scope.ServiceProvider.GetRequiredService<SchedulingInputs>();
            var problem = await inputs.Read(2026, 10);
            Assert.Equal(ids["employee"], Assert.Single(problem.Employees).Id);
            Assert.All(manual, u => Assert.DoesNotContain(problem.Employees, e => e.Id == u.Id));
            Assert.Equal(4, problem.ManualEmployees!.Length);
            Assert.All(problem.Work, w => Assert.False(w.Replaceable));
            Assert.Equal(62, problem.Cells.Sum(c => c.Required));
        }
        var run = await Generate(admin);
        Assert.Equal("READY", run.State);
        Assert.All(run.Cells.SelectMany(c => c.Employees).Where(e => !e.Protected), e => Assert.Equal(ids["employee"], e.UserId));
        Assert.Equal(4, run.Cells.SelectMany(c => c.Employees).Count(e => e.Protected));
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        review = await Review(admin);
        Assert.Empty(review.Conflicts);
        foreach (var user in manual)
            Assert.Equal(original[user.Id], review.Draft.Cells.SelectMany(c => c.Employees).Single(e => e.UserId == user.Id).AssignmentId);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var manualIds = manual.Select(u => u.Id).ToArray();
        Assert.Equal(4, await db.Set<ShiftAssignment>().CountAsync(a => manualIds.Contains(a.UserId) && a.GeneratedByRunId == null));
    }

    [PostgresFact]
    public async Task Type_changes_invalidate_previews_preserve_legacy_defaults_and_do_not_grant_permissions()
    {
        using var admin = await Login();
        await ConfigureAutomatic(1);
        var run = await Generate(admin);
        var existing = (await admin.GetFromJsonAsync<UserResponse>($"/api/users/{ids["employee"]}"))!;
        Assert.Equal("REGULAR", existing.EmployeeType);
        var updated = await Send(admin, $"/api/users/{existing.Id}", new ProfileRequest(existing.FirstName, existing.LastName, existing.Username, EmployeeType: "MANAGER"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(new[] { "EMPLOYEE" }, (await updated.Content.ReadFromJsonAsync<UserResponse>())!.Roles);
        var stale = await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_PREVIEW", (await stale.Content.ReadFromJsonAsync<ApiError>())!.Code);
        using var employee = await Login("employee");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(TeamsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, $"{TeamsPath}/members/{existing.Id}", new MembershipRequest((await Teams(admin))[0].Id))).StatusCode);
        using var coordinator = await Login("coordinator");
        Assert.Equal(HttpStatusCode.Forbidden, (await coordinator.GetAsync(TeamsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await coordinator.GetAsync("/api/access/coordinator")).StatusCode);
        // An old client omitting the classification preserves the saved value.
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/users/{existing.Id}", new ProfileRequest(existing.FirstName, existing.LastName, existing.Username))).StatusCode);
        Assert.Equal("MANAGER", (await admin.GetFromJsonAsync<UserResponse>($"/api/users/{existing.Id}"))!.EmployeeType);
        foreach (var invalid in new[] { "ADMIN", "manager", "1", "UNKNOWN" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, "/api/users", new CreateUserRequest("שם", "משפחה", "invalid", Password, ["EMPLOYEE"], EmployeeType: invalid), "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"{TeamsPath}/members/{ids["admin"]}", new MembershipRequest((await Teams(admin))[0].Id))).StatusCode);
    }
}

public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Reclassified_employees_keep_existing_assignments_without_receiving_new_automatic_work()
    {
        using var admin = await Login(); await ConfigureAutomatic(1);
        var first = await Generate(admin);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{first.Id}/save", new { }, "POST")).StatusCode);
        var original = (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Select(e => e.AssignmentId).Order().ToArray();
        Assert.NotEmpty(original);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/users/{ids["employee"]}", new ProfileRequest("ישראל", "employee", "employee", EmployeeType: "RESERVIST"))).StatusCode);
        var next = await Generate(admin); Assert.Equal("READY", next.State);
        Assert.Equal(original.Length, next.Cells.SelectMany(c => c.Employees).Count());
        Assert.All(next.Cells.SelectMany(c => c.Employees), e => Assert.True(e.Protected));
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{next.Id}/save", new { }, "POST")).StatusCode);
        Assert.Equal(original, (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Select(e => e.AssignmentId).Order().ToArray());
    }
}
