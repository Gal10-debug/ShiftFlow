using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private sealed class AdminRoleReadCounter : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int Reads;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (Enabled && command.CommandText.Contains("\"AspNetRoles\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is string role && role == "ADMIN"))
                Interlocked.Increment(ref Reads);
            return ValueTask.FromResult(result);
        }
    }

    [PostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Absence_list_role_reads_do_not_grow_with_request_count_and_preserve_visibility(bool manage)
    {
        using var employee = await Login("employee");
        await SubmitAbsence(employee);
        var counter = new AdminRoleReadCounter();
        await using var instrumented = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .WithIsolatedTestKeys().UseSetting("ConnectionStrings:Database", database.GetConnectionString())
            .ConfigureServices(s => s.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(counter))));
        using var client = instrumented.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/login", new { username = manage ? "admin" : "employee", password = Password }, "POST")).StatusCode);
        var path = manage ? "/api/absences/manage" : "/api/absences/mine";
        counter.Enabled = true;
        var first = (await client.GetFromJsonAsync<AbsenceResponse[]>(path))!;
        Assert.Single(first);
        var singleRequestRoleReads = counter.Reads;
        Assert.True(singleRequestRoleReads > 0, "The real administrator-role query must be observed.");
        counter.Enabled = false;
        await SubmitAbsence(employee, Absence() with { StartDate = new(2026, 10, 4), EndDate = new(2026, 10, 4) });
        await SubmitAbsence(employee, Absence() with { StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 5) });
        counter.Reads = 0; counter.Enabled = true;
        var multiple = (await client.GetFromJsonAsync<AbsenceResponse[]>(path))!;
        Assert.Equal(3, multiple.Length);
        Assert.All(multiple, r => Assert.Equal(ids["employee"], r.UserId));
        Assert.Equal(singleRequestRoleReads, counter.Reads);
    }
}
