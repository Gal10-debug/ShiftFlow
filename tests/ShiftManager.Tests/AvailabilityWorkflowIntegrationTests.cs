using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    [PostgresTheory]
    [InlineData("employee")]
    [InlineData("coordinatoronly")]
    public async Task Availability_personal_and_foreign_writes_are_denied_without_database_changes(string actor)
    {
        using var admin = await Login();
        await SecurityUsers(admin);
        await ConfigureWindow(admin, open: true);
        using var leader = await Login("leader");
        Assert.Equal(HttpStatusCode.Created, (await Send(leader, Delegated(ids["employee"]), Choices(0, 1), "POST")).StatusCode);
        using var client = await Login(actor);
        if (actor == "employee")
        {
            var own = (await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!;
            Assert.True(own.Submitted); Assert.Single(own.Selections);
            Assert.Equal(ids["employee"], (await client.GetFromJsonAsync<AvailabilityResponse>($"/api/availability/2026/10?userId={ids["foreign"]}"))!.UserId);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/team-availability/2026/10")).StatusCode);
        var before = await SecurityFingerprint();
        foreach (var method in new[] { "POST", "PUT", "DELETE" })
        {
            var own = await Send(client, "/api/availability/2026/10", Choices(1, 2), method);
            Assert.Equal(method == "DELETE" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, own.StatusCode);
            foreach (var target in new[] { ids["employee"], ids["foreign"] })
                Assert.Equal(method == "DELETE" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
                    (await Send(client, Delegated(target), Choices(1, 2), method)).StatusCode);
            Assert.Equal(before, await SecurityFingerprint());
        }
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
        var error = await Assert.ThrowsAsync<ApiException>(() => service.Submit(2026, 10, Choices(), new(), false));
        Assert.Equal("FORBIDDEN", error.Code);
    }

    [PostgresTheory]
    [InlineData("leader")]
    [InlineData("coordinator")]
    public async Task Availability_delegation_and_status_summary_follow_current_membership(string actor)
    {
        using var admin = await Login(); await SecurityUsers(admin); await ConfigureWindow(admin);
        using var client = await Login(actor);
        const string summaryPath = "/api/team-availability/2026/10";
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(summaryPath)).StatusCode);
        var missing = (await client.GetFromJsonAsync<WindowSummaryResponse>(summaryPath))!;
        Assert.Contains(missing.Employees, e => e.UserId == ids["employee"] && !e.Submitted);
        Assert.DoesNotContain(missing.Employees, e => e.UserId == ids["foreign"] || e.UserId == ids["leader"]);
        Assert.Equal(HttpStatusCode.Created, (await Send(client, Delegated(ids["employee"]), Choices(0, 1), "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, Delegated(ids["employee"]), new SubmitAvailabilityRequest(1,
            [new(new(2026, 10, 2), "NIGHT", "PREFERRED")]))).StatusCode);
        var recorded = (await client.GetFromJsonAsync<WindowSummaryResponse>(summaryPath))!;
        Assert.Equal(1, recorded.SubmittedCount);
        using var worker = await Login("employee");
        var own = (await worker.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!;
        Assert.Equal(2, own.Version); Assert.Equal("PREFERRED", Assert.Single(own.Selections).State);
        var other = (await Teams(admin))[1];
        await Send(admin, $"{TeamsPath}/members/{ids["employee"]}", new MembershipRequest(other.Id));
        var before = await SecurityFingerprint();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, Delegated(ids["employee"]), Choices(2))).StatusCode);
        Assert.Equal(before, await SecurityFingerprint());
        Assert.DoesNotContain((await client.GetFromJsonAsync<WindowSummaryResponse>(summaryPath))!.Employees, e => e.UserId == ids["employee"]);
        Assert.Equal(2, (await worker.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!.Version);
        using var foreignLeader = await Login("foreignleader");
        Assert.Equal(HttpStatusCode.OK, (await Send(foreignLeader, Delegated(ids["employee"]), Choices(2))).StatusCode);
        await Send(admin, $"{TeamsPath}/members/{ids[actor]}", new MembershipRequest(null));
        before = await SecurityFingerprint();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, Delegated(ids["qualified"]), Choices(), "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(summaryPath)).StatusCode);
        Assert.Equal(before, await SecurityFingerprint());
    }
}
