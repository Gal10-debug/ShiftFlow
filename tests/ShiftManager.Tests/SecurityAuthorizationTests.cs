using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private sealed record SecurityResources(HttpClient Admin, TeamResponse Team, TeamResponse OtherTeam,
        Guid Definition, Guid Assignment, PublicationResponse Publication, AbsenceResponse OwnAbsence,
        AbsenceResponse ForeignAbsence, ExchangeResponse Exchange, Guid Issue, GenerationResponse Job);
    private sealed record SecurityCall(string Method, string Path, object? Body = null);

    private async Task SecurityUsers(HttpClient admin)
    {
        await SetupTeam(admin);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var name in new[] { "qualified", "foreign", "foreignleader", "coordinatoronly" })
        {
            var u = new ApplicationUser { UserName = name, FirstName = "בדיקת", LastName = name, IsShiftLeadQualified = name == "qualified" };
            Assert.True((await users.CreateAsync(u, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(u, name == "coordinatoronly" ? "COORDINATOR" : "EMPLOYEE")).Succeeded); ids[name] = u.Id;
        }
        var teams = await Teams(admin);
        foreach (var name in new[] { "foreign", "foreignleader" })
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{ids[name]}", new MembershipRequest(teams[1].Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{ids["qualified"]}", new MembershipRequest(teams[0].Id))).StatusCode);
        teams = await Teams(admin);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/{teams[1].Id}", new UpdateTeamRequest(teams[1].Name, ids["foreignleader"], null, teams[1].Version))).StatusCode);
    }

    private async Task<GenerationResponse> SecurityGenerate(HttpClient admin, int month = 11)
    {
        var response = await Send(admin, GenerationPath, new { year = 2026, month }, "POST");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<GenerationResponse>())!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        do
        {
            job = (await admin.GetFromJsonAsync<GenerationResponse>($"{GenerationPath}/{job.Id}", deadline.Token))!;
            if (job.State is not ("QUEUED" or "RUNNING")) break;
        } while (await timer.WaitForNextTickAsync(deadline.Token));
        Assert.Equal("READY", job.State); return job;
    }

    private async Task<SecurityResources> SecuritySetup()
    {
        var (admin, definition, _) = await ManualSetup();
        await SecurityUsers(admin); await ConfigureWindow(admin); await ConfigureWindow(admin, 11);
        var r = await Edit(admin, await Review(admin), "ASSIGN", ids["employee"], definition, 25);
        r = await Ack(admin, r); var publication = await Publish(admin, r, "PRELIMINARY");
        var assignment = publication.Snapshot!.Cells.SelectMany(c => c.Employees).Single().AssignmentId;
        using var employee = await Login("employee"); using var foreign = await Login("foreign");
        var own = await SubmitAbsence(employee); var other = await SubmitAbsence(foreign);
        var issueResponse = await Send(employee, "/api/my-schedule/2026/10/issues", new CreateIssueRequest(publication.Id, assignment, "בדיקת הרשאה"), "POST");
        Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);
        var issue = (await issueResponse.Content.ReadFromJsonAsync<ScheduleIssue>())!.Id;
        var exchangeResponse = await Send(employee, "/api/shift-exchanges/2026/10", new ExchangeCreateRequest(publication.Id, assignment, ids["qualified"]), "POST");
        Assert.Equal(HttpStatusCode.Created, exchangeResponse.StatusCode);
        var exchange = (await exchangeResponse.Content.ReadFromJsonAsync<ExchangeResponse>())!;
        var teams = await Teams(admin);
        return new(admin, teams[0], teams[1], definition, assignment, publication, own, other, exchange, issue, await SecurityGenerate(admin));
    }

    // Only hashes leave PostgreSQL, including for Identity tables. Assertion output
    // can never contain a password hash, security stamp, or employee record.
    private async Task<string> SecurityFingerprint()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tables = db.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>().Distinct().Order().ToArray();
        await using var c = new NpgsqlConnection(database.GetConnectionString()); await c.OpenAsync();
        var statements = tables.Select(t => $"SELECT '{t.Replace("'", "''")}' AS name, md5(COALESCE(string_agg(to_jsonb(r)::text, E'\\n' ORDER BY to_jsonb(r)::text), '')) AS hash FROM \"{t.Replace("\"", "\"\"")}\" r");
        await using var query = new NpgsqlCommand(string.Join(" UNION ALL ", statements) + " ORDER BY name", c);
        await using var reader = await query.ExecuteReaderAsync(); var hashes = new StringBuilder();
        while (await reader.ReadAsync()) hashes.Append(reader.GetString(0)).Append(':').AppendLine(reader.GetString(1));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashes.ToString())));
    }

    private static async Task<HttpResponseMessage> SecuritySend(HttpClient c, SecurityCall call, bool csrf = true)
    {
        if (call.Method == "GET") return await c.GetAsync(call.Path);
        return await Send(c, call.Path, call.Body ?? new { }, call.Method, csrf);
    }

    private async Task<SecurityCall[]> SecurityAdminCalls(SecurityResources x)
    {
        var rows = await Types(x.Admin); var r = await Review(x.Admin);
        return [
            new("GET", "/api/admin/statistics/2026/10"), new("GET", "/api/admin/statistics/2026"),
            new("GET", "/api/users"), new("GET", $"/api/users/{ids["foreign"]}"),
            new("POST", "/api/users", new CreateUserRequest("בדיקת", "אבטחה", "unauthorized-create", Password, ["ADMIN"])),
            new("PUT", $"/api/users/{ids["foreign"]}", new ProfileRequest("שינוי", "אסור", "unauthorized-rename")),
            new("PATCH", $"/api/users/{ids["foreign"]}/status", new StatusRequest(false)),
            new("PUT", $"/api/users/{ids["foreign"]}/roles", new RolesRequest(["ADMIN"])),
            new("POST", $"/api/users/{ids["foreign"]}/reset-password", new ResetPasswordRequest("Replacement!2026")),
            new("GET", TeamsPath), new("GET", TeamsPath + "/audit"), new("GET", TeamsPath + "/conflicts"),
            new("PUT", $"{TeamsPath}/{x.OtherTeam.Id}", new UpdateTeamRequest("שינוי אסור", x.OtherTeam.LeaderUserId, null, x.OtherTeam.Version)),
            new("PUT", $"{TeamsPath}/members/{ids["foreign"]}", new MembershipRequest(x.Team.Id)),
            new("GET", "/api/admin/availability/2026/10"), new("GET", $"/api/admin/availability/2026/10/employees/{ids["foreign"]}"),
            new("PUT", "/api/admin/availability/2026/10", new WindowRequest(clock.Now.AddDays(-1), clock.Now.AddDays(10))),
            new("PUT", "/api/admin/availability/2026/10/state", new WindowStateRequest(true)),
            new("GET", "/api/admin/scheduling/board?date=2026-10-25"), new("GET", TypesPath),
            new("GET", "/api/admin/scheduling/automatic-configuration"), new("PUT", TypesPath, Request(rows)),
            new("POST", GenerationPath, new { year = 2026, month = 12 }), new("GET", $"{GenerationPath}/{x.Job.Id}"),
            new("GET", $"{GenerationPath}/{x.Job.Id}/shortages"), new("DELETE", $"{GenerationPath}/{x.Job.Id}"),
            new("POST", $"{GenerationPath}/{x.Job.Id}/save"), new("GET", ReviewPath),
            new("GET", $"{ReviewPath}/candidates?definitionId={x.Definition}&date=2026-10-25&replacing={x.Assignment}"),
            new("POST", ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "REMOVE", x.Assignment, null, null, null)),
            new("POST", ReviewPath + "/on-call-pair", new OnCallPairRequest(r.Token, "REMOVE", new(2026, 10, 23), null, x.Assignment, true)),
            new("POST", ReviewPath + "/review", new ReviewTokenRequest(r.Token)),
            new("POST", ReviewPath + "/correction", new ReviewTokenRequest(r.Token)),
            new("POST", ReviewPath + "/publications/PRELIMINARY", new PublishScheduleRequest(r.Token, true, true, "בדיקת הרשאה")),
            new("POST", ReviewPath + "/publications/FINAL", new PublishScheduleRequest(r.Token, true, true, "בדיקת הרשאה")),
            new("GET", ReviewPath + "/publications"), new("GET", ReviewPath + "/issues"), new("GET", ReviewPath + "/audit"), new("GET", ReviewPath + "/exchanges"),
            new("PUT", $"{ReviewPath}/issues/{x.Issue}", new UpdateIssueRequest(1, "RESOLVED", "שינוי אסור")),
            new("POST", $"{ReviewPath}/publications/{x.Publication.Id}/control", new PublicationControlRequest(0, "HIDE", "שינוי אסור")),
            new("GET", "/api/admin/shift-exchanges"), new("GET", $"/api/admin/shift-exchanges/{x.Exchange.Id}/publication-review"),
            new("POST", $"/api/admin/shift-exchanges/{x.Exchange.Id}/decision", new ExchangeDecisionRequest(x.Exchange.Version, "REJECT", "שינוי אסור")),
            new("POST", $"/api/admin/shift-exchanges/{x.Exchange.Id}/publish", new ExchangePublicationRequest([new(2026, 10, r.Token)], true, true, null))
        ];
    }

    [PostgresTheory]
    [InlineData("employee")] [InlineData("leader")] [InlineData("coordinator")]
    [InlineData("qualified")] [InlineData("foreign")] [InlineData("firstlogin")] [InlineData("anonymous")] [InlineData("coordinatoronly")]
    public async Task Security_every_administrative_route_rejects_nonadministrators_without_side_effects(string actor)
    {
        var x = await SecuritySetup(); using var admin = x.Admin;
        using var client = actor == "anonymous" ? Client() : await Login(actor);
        var expected = actor == "anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var before = await SecurityFingerprint();
        foreach (var call in await SecurityAdminCalls(x))
        {
            using var response = await SecuritySend(client, call);
            Assert.True(response.StatusCode == expected, $"{actor}: {call.Method} {call.Path} returned {(int)response.StatusCode}");
            if (call.Method != "GET") Assert.Equal(before, await SecurityFingerprint());
        }
        Assert.Equal(before, await SecurityFingerprint());
        Assert.Equal("READY", factory.Services.GetRequiredService<GenerationQueue>().Get(x.Job.Id).Response.State);
    }

    [PostgresFact]
    public async Task Security_real_objects_have_authorized_readers_and_writers_and_audited_changes()
    {
        var x = await SecuritySetup(); using var admin = x.Admin;
        foreach (var call in (await SecurityAdminCalls(x)).Where(c => c.Method == "GET" && !c.Path.EndsWith("/publication-review")))
            Assert.Equal(HttpStatusCode.OK, (await SecuritySend(admin, call)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{x.Job.Id}/save", new { }, "POST")).StatusCode);
        var cancel = await SecurityGenerate(admin, 12);
        Assert.Equal(HttpStatusCode.NoContent, (await SecuritySend(admin, new("DELETE", $"{GenerationPath}/{cancel.Id}"))).StatusCode);
        using var deputy = await Login("coordinator");
        Assert.Contains((await deputy.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage"))!, a => a.Id == x.OwnAbsence.Id);
        Assert.DoesNotContain((await deputy.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage"))!, a => a.Id == x.ForeignAbsence.Id);
        var approved = await DecideAbsence(deputy, x.OwnAbsence); Assert.Equal(ids["coordinator"], approved.Decision!.ActorUserId);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, $"{ReviewPath}/publications/{x.Publication.Id}/control", new PublicationControlRequest(0, "HIDE", "בדיקת אבטחה"), "POST")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Contains(await db.Set<ScheduleEditAudit>().ToArrayAsync(), a => a.Operation == "PUBLICATION_HIDE" && a.ActorUserId == ids["admin"]);
    }

    [PostgresTheory]
    [InlineData("leader")] [InlineData("coordinator")]
    public async Task Security_team_object_substitution_and_filter_tampering_do_not_cross_team_boundaries(string actor)
    {
        var x = await SecuritySetup(); using var admin = x.Admin; using var client = await Login(actor);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Delegated(ids["employee"])) ).StatusCode);
        var before = await SecurityFingerprint();
        foreach (var call in new[] {
            new SecurityCall("GET", $"/api/team-availability/employees/{ids["foreign"]}/periods"),
            new("GET", Delegated(ids["foreign"])), new("POST", Delegated(ids["foreign"]), Choices()), new("PUT", Delegated(ids["foreign"]), Choices()),
            new("GET", $"/api/absences/{x.ForeignAbsence.Id}/conflicts"),
            new("POST", $"/api/absences/{x.ForeignAbsence.Id}/decision", new AbsenceDecisionRequest(1, "APPROVED", null)),
            new("POST", $"/api/absences/{x.ForeignAbsence.Id}/changes", new AbsenceChange(1, "CANCEL", null)),
            new("POST", $"/api/absences/{x.ForeignAbsence.Id}/cancel-pending", new AbsenceVersion(1)) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SecuritySend(client, call)).StatusCode);
            Assert.Equal(before, await SecurityFingerprint());
        }
        foreach (var filter in new[] { $"userId={ids["foreign"]}", $"teamId={x.OtherTeam.Id}", $"userId={ids["foreign"]}&teamId={x.OtherTeam.Id}" })
            Assert.Empty((await client.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage?" + filter))!);
        Assert.Equal(before, await SecurityFingerprint());
    }

    [PostgresFact]
    public async Task Security_personal_object_substitution_rejects_real_absences_issues_and_exchanges()
    {
        var x = await SecuritySetup(); using var admin = x.Admin; using var employee = await Login("employee"); using var foreign = await Login("foreign");
        Assert.Contains((await employee.GetFromJsonAsync<ExchangeResponse[]>("/api/shift-exchanges"))!, e => e.Id == x.Exchange.Id);
        Assert.Empty((await foreign.GetFromJsonAsync<ExchangeResponse[]>("/api/shift-exchanges"))!);
        Assert.Empty((await foreign.GetFromJsonAsync<ScheduleIssue[]>("/api/my-schedule/2026/10/issues"))!);
        Assert.Equal(ids["foreign"], (await foreign.GetFromJsonAsync<AvailabilityResponse>($"/api/availability/2026/10?userId={ids["employee"]}"))!.UserId);
        Assert.All((await foreign.GetFromJsonAsync<AbsenceResponse[]>($"/api/absences/mine?userId={ids["employee"]}"))!, a => Assert.Equal(ids["foreign"], a.UserId));
        var before = await SecurityFingerprint();
        foreach (var call in new[] {
            new SecurityCall("POST", $"/api/absences/{x.OwnAbsence.Id}/changes", new AbsenceChange(1, "CANCEL", null)),
            new("POST", $"/api/absences/{x.OwnAbsence.Id}/cancel-pending", new AbsenceVersion(1)),
            new("POST", "/api/my-schedule/2026/10/issues", new CreateIssueRequest(x.Publication.Id, x.Assignment, "ניסיון זר")),
            new("GET", $"/api/shift-exchanges/2026/10/candidates?publicationId={x.Publication.Id}&assignmentId={x.Assignment}"),
            new("POST", "/api/shift-exchanges/2026/10", new ExchangeCreateRequest(x.Publication.Id, x.Assignment, ids["foreign"])),
            new("POST", $"/api/shift-exchanges/{x.Exchange.Id}/decision", new ExchangeDecisionRequest(x.Exchange.Version, "ACCEPT")) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SecuritySend(foreign, call)).StatusCode);
            Assert.Equal(before, await SecurityFingerprint());
        }
    }
}
