using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Qualification_is_admin_only_and_expert_classification_survives_rename_and_save()
    {
        using var admin = await Login();
        var profile = new { firstName = "ישראל", lastName = "employee", username = "employee", isShiftLeadQualified = true };
        using var employee = await Login("employee");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, $"/api/users/{ids["employee"]}", profile)).StatusCode);
        var updated = await Send(admin, $"/api/users/{ids["employee"]}", profile);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var user = (await updated.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.True(user.IsShiftLeadQualified); Assert.Equal(new[] { "EMPLOYEE" }, user.Roles);
        var rows = await Types(admin);
        var request = Request(rows);
        request.ShiftTypes[0] = request.ShiftTypes[0] with { Name = "מומחה", RequiresShiftLeadQualification = true, RequiredEmployees = 2, IncludeInAutomaticScheduling = true };
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, request)).StatusCode);
        rows = await Types(admin);
        request = Request(rows);
        request.ShiftTypes[0] = request.ShiftTypes[0] with { Name = "שם חדש", RequiresShiftLeadQualification = true, RequiredEmployees = 3 };
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, request)).StatusCode);
        rows = await Types(admin);
        Assert.Equal(51, rows.Length); Assert.True(rows[0].RequiresShiftLeadQualification);
        Assert.Equal("שם חדש", rows[0].Name);
        request = Request(rows);
        request.ShiftTypes[0] = request.ShiftTypes[0] with { RequiresShiftLeadQualification = true };
        request.ShiftTypes[1] = request.ShiftTypes[1] with { RequiresShiftLeadQualification = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, request)).StatusCode);
        var run = await Generate(admin);
        Assert.Equal("READY", run.State);
        Assert.All(run.Cells, c => { Assert.Equal(3, c.RequiredEmployees); Assert.Equal(2, c.Shortage); Assert.Equal(ids["employee"], c.Employees.Single().UserId); });
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Set<ShiftAssignment>().AnyAsync(a => a.IsShiftLead));
        // Changing qualification invalidates the fingerprint even when roles stay unchanged.
        run = await Generate(admin);
        await Send(admin, $"/api/users/{ids["employee"]}", new { profile.firstName, profile.lastName, profile.username, isShiftLeadQualified = false });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
    }
}
