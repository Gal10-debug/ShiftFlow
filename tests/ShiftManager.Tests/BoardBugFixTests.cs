using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Serialization;
using ShiftManager.Api;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class BoardBugFixTests
{
    private static readonly Guid Row3 = Guid.Parse("40000000-0000-0000-0000-000000000003");
    // Exact pre-Phase-5.3 wire shape from main/41e4eb5. It had no Note member.
    public sealed record LegacyManualRequest(string ExpectedToken, string Operation, Guid? AssignmentId, Guid? UserId, Guid? DefinitionId, DateOnly? Date);
    [Theory]
    [InlineData("ASSIGN", null)] [InlineData("REMOVE", null)] [InlineData("REPLACE", null)]
    [InlineData("MOVE", null)] [InlineData("NOTE", "להגיע מוקדם")]
    public async Task Current_browser_payload_is_rejected_by_old_strict_contract_but_binds_to_current_DTO(string operation, string? note)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var payload = JsonSerializer.Serialize(new { expectedToken = new string('A',64), operation, assignmentId = Guid.NewGuid(),
            userId = (Guid?)null, definitionId = Row3, date = "2026-11-06", note }, options);
        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LegacyManualRequest>(payload, options));
        Assert.Contains("note", error.Message);
        var bound = JsonSerializer.Deserialize<ManualAssignmentRequest>(payload, options)!;
        Assert.Equal(operation, bound.Operation); Assert.Equal(note, bound.Note); Assert.Equal(Row3, bound.DefinitionId);
        // Exercise the actual MVC binder and the application's safe error factory, not just the serializer.
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(LegacyBindingController).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)
                .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ApiErrors.Validation(context))))
            .Configure(app => { app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapControllers()); })).StartAsync();
        using var client = host.GetTestClient();
        var oldResponse = await client.PostAsync("/binding-probe/legacy", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, oldResponse.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await oldResponse.Content.ReadFromJsonAsync<ApiError>())!.Code);
        var currentResponse = await client.PostAsync("/binding-probe/current", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(note, (await currentResponse.Content.ReadFromJsonAsync<ManualAssignmentRequest>())!.Note);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(1000)]
    public void Row3_weekend_demand_is_one_independent_of_weekday_configuration(int weekday)
    {
        Assert.Equal(1,SchedulingRules.Required("ORDINARY",new(2026,11,6),weekday,Row3));
        Assert.Equal(1,SchedulingRules.Required("ORDINARY",new(2026,11,7),weekday,Row3));
        foreach(var day in Enumerable.Range(1,5)) Assert.Equal(weekday,SchedulingRules.Required("ORDINARY",new(2026,11,day),weekday,Row3));
        Assert.Equal(weekday,SchedulingRules.Required("ORDINARY",new(2026,11,6),weekday,Guid.Parse("40000000-0000-0000-0000-000000000002")));
    }
}

// Only the isolated TestServer above registers this test assembly; no production route is added.
[ApiController, Route("binding-probe")]
public sealed class LegacyBindingController : ControllerBase
{
    [HttpPost("legacy")] public IActionResult Legacy(BoardBugFixTests.LegacyManualRequest input) => Ok(input);
    [HttpPost("current")] public IActionResult Current(ManualAssignmentRequest input) => Ok(input);
}
