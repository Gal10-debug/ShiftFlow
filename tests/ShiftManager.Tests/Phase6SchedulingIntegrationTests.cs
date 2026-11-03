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
    public async Task Manual_only_mapping_survives_renames_and_automatic_regeneration_preserves_manual_work()
    {
        using var admin = await Login();
        Guid manual;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.Set<ShiftTypeDefinition>().OrderBy(x => x.Slot).ToArrayAsync();
            Assert.Equal(new[] { 12, 13, 14 }, rows.Where(r => r.AssignmentMode == AssignmentMode.ManualOnly).Select(r => r.Slot));
            foreach (var rowItem in rows) { rowItem.RequiredEmployees = 0; rowItem.IncludeInAutomaticScheduling = false; }
            var row = rows[11]; manual = row.Id;
            row.Name = "שם חדש"; row.IncludeInAutomaticScheduling = true; row.RequiredEmployees = 1;
            await db.SaveChangesAsync();
        }
        var r = await Review(admin);
        r = await Edit(admin, r, "ASSIGN", ids["employee"], manual, 1);
        var assignment = r.Draft.Cells.SelectMany(c => c.Employees).Single().AssignmentId;
        var run = await Generate(admin);
        Assert.Equal("READY", run.State);
        Assert.DoesNotContain(run.Cells, c => c.DefinitionId == manual);
        Assert.Equal(0, run.Summary!.Required);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        Assert.Equal(assignment, (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Single().AssignmentId);
    }
    [PostgresFact]
    public async Task Final_board_edits_create_repeatable_corrections_without_changing_published_history()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin, r, "ASSIGN", ids["employee"], definition, 1);
            r = await Ack(admin, r); await Publish(admin, r, "PRELIMINARY");
            var original = await Publish(admin, await Review(admin), "FINAL");
            using var employee = await Login("employee");
            for (var n = 0; n < 2; n++)
            {
                r = await Review(admin);
                var assignment = r.Draft.Cells.SelectMany(c => c.Employees).Single();
                var response = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "NOTE", assignment.AssignmentId, null, null, null, $"תיקון {n}"), "POST");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var corrected = (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
                Assert.Equal("UNDER_CORRECTION", corrected.State); Assert.True(corrected.FinalRevision);
                Assert.Equal(original.VersionNumber + n, (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.VersionNumber);
                Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "REMOVE", assignment.AssignmentId, null, null, null), "POST")).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, ReviewPath + "/publications/PRELIMINARY", new PublishScheduleRequest(corrected.Token, true, true, null), "POST")).StatusCode);
                var final = await Publish(admin, corrected, "FINAL");
                Assert.Equal(original.VersionNumber + n + 1, final.VersionNumber);
                Assert.Equal($"תיקון {n}", final.Snapshot!.Cells.SelectMany(c => c.Employees).Single().Note);
            }
            var history = (await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath + "/publications"))!;
            Assert.Equal(4, history.Length);
            Assert.Null(history.Single(p => p.Id == original.Id).Snapshot!.Cells.SelectMany(c => c.Employees).Single().Note);
            var audit = (await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!;
            Assert.Equal(2, audit.Count(a => a.Operation == "FINAL_CORRECTION"));
            Assert.All(audit, a => Assert.Equal(ids["admin"], a.ActorUserId));
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, ReviewPath + "/correction", new ReviewTokenRequest(r.Token), "POST")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Direct_board_pair_edit_reopens_a_final_neighbor_while_preserving_its_snapshot()
    {
        var (admin, definition, _) = await PairSetup(); using (admin)
        {
            const string april = "/api/admin/scheduling/periods/2027/4", may = "/api/admin/scheduling/periods/2027/5";
            var r = (await admin.GetFromJsonAsync<ReviewResponse>(april))!;
            var response = await Send(admin, april + "/assignments", new ManualAssignmentRequest(r.Token, "ASSIGN", null, ids["employee"], definition, new(2027, 4, 30)), "POST");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var m = (await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            response = await Send(admin, may + "/review", new ReviewTokenRequest(m.Token), "POST"); m = (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
            response = await Send(admin, may + "/publications/PRELIMINARY", new PublishScheduleRequest(m.Token, true, true, null), "POST"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            m = (await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            response = await Send(admin, may + "/publications/FINAL", new PublishScheduleRequest(m.Token, true, true, null), "POST");
            var final = (await response.Content.ReadFromJsonAsync<PublicationResponse>())!;
            r = (await admin.GetFromJsonAsync<ReviewResponse>(april))!;
            var member = r.Draft.Cells.SelectMany(c => c.Employees).Single();
            response = await Send(admin, april + "/assignments", new ManualAssignmentRequest(r.Token, "REMOVE", member.AssignmentId, null, null, null), "POST");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("UNDER_CORRECTION", (await admin.GetFromJsonAsync<ReviewResponse>(may))!.State);
            var history = (await admin.GetFromJsonAsync<PublicationResponse[]>(may + "/publications"))!;
            Assert.Single(history.Single(p => p.Id == final.Id).Snapshot!.Cells.SelectMany(c => c.Employees));
        }
    }
}
