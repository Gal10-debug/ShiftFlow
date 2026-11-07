using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.E2EHost;

// This executable is never referenced by the production application.
internal static class EntryPoint
{
    public static async Task Main(string[] args)
    {
        if (args is ["--verify-guards"]) { VerifyGuards(); return; }
        var run = Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_RUN") ?? "";
        var connection = Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_CONNECTION") ?? "";
        var token = Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_TOKEN") ?? "";
        var password = Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_PASSWORD") ?? "";
        Guard(run, connection, token, password);
        await ClaimEmptyDatabase(connection, run);
        var fixtures = new Fixtures(run, connection, password);
        await using var factory = new E2EFactory(connection, fixtures, token);
        factory.UseKestrel(int.Parse(Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_BACKEND_PORT")!));
        factory.StartServer();
        await fixtures.Reset(factory.Services, "empty");
        Console.WriteLine("Disposable E2E host ready.");
        try { await Task.Delay(Timeout.Infinite, factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping); }
        catch (OperationCanceledException) { /* graceful runner teardown */ }
    }

    internal static void Guard(string run, string connection, string token, string password)
    {
        var c = new NpgsqlConnectionStringBuilder(connection);
        if (!Regex.IsMatch(run, "^[a-f0-9]{32}$") || c.Host != "127.0.0.1" || c.Port is 5432 or 55432
            || c.Database != "shiftflow_e2e_" + run || c.Username != c.Database || string.IsNullOrEmpty(c.Password) || c.Password.Length < 24
            || token.Length < 32 || password.Length < 16)
            throw new InvalidOperationException("Refusing an unowned E2E target.");
    }

    private static void VerifyGuards()
    {
        var run = new string('a', 32);
        var valid = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 60000, Database = "shiftflow_e2e_" + run,
            Username = "shiftflow_e2e_" + run, Password = new string('p', 24) };
        Guard(run, valid.ConnectionString, new string('t', 32), new string('P', 16));
        Action<NpgsqlConnectionStringBuilder>[] unsafeTargets = [c => c.Port = 5432, c => c.Port = 55432,
            c => c.Host = "localhost", c => c.Host = "db.example.com", c => c.Database = "shiftflow_demo_202611_42",
            c => c.Database = "shiftmanager", c => c.Username = "postgres", c => c.Password = ""];
        foreach (var mutate in unsafeTargets)
        {
            var candidate = new NpgsqlConnectionStringBuilder(valid.ConnectionString); mutate(candidate);
            try { Guard(run, candidate.ConnectionString, new string('t', 32), new string('P', 16)); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("Unsafe target was accepted by E2E guard.");
        }
        Console.WriteLine("E2E database safety: one valid target and eight unsafe targets verified without connecting.");
    }

    private static async Task ClaimEmptyDatabase(string connection, string run)
    {
        await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')", c);
        if ((long)(await check.ExecuteScalarAsync())! != 0) throw new InvalidOperationException("E2E database must start empty; refusing adoption.");
        await using var claim = new NpgsqlCommand("CREATE SCHEMA shiftflow_e2e_control; CREATE TABLE shiftflow_e2e_control.owner (run text PRIMARY KEY); INSERT INTO shiftflow_e2e_control.owner VALUES (@run)", c);
        claim.Parameters.AddWithValue("run", run); await claim.ExecuteNonQueryAsync();
    }
}

internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2030-12-01T10:00:00Z");
}

internal sealed class E2EFactory(string connection, Fixtures fixtures, string token) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(Directory.GetCurrentDirectory(), "src", "ShiftManager"));
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Database"] = connection }));
        builder.ConfigureLogging(log => log.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider, FixedClock>();
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_PRIVATE")!, "keys"))).SetApplicationName("ShiftFlow.E2E." + fixtures.Run);
            services.AddSingleton<IStartupFilter>(new ControlEndpoints(fixtures, token));
        });
    }
}

internal sealed class ControlEndpoints(Fixtures fixtures, string token) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, proceed) =>
        {
            // Each independent fixture gets its own rate-limit partition. The real
            // ten-attempt policy still applies inside each browser test.
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse($"127.1.{fixtures.Epoch / 250}.{fixtures.Epoch % 250 + 1}");
            var proxyProof = ctx.Request.Path == "/api/__e2e/ready";
            if (!ctx.Request.Path.StartsWithSegments("/__e2e") && !proxyProof) { await proceed(); return; }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(ctx.Request.Headers["X-E2E-Control"].ToString()), Encoding.UTF8.GetBytes(token)))
            { ctx.Response.StatusCode = 404; return; }
            if ((ctx.Request.Path == "/__e2e/ready" || proxyProof) && ctx.Request.Method == "GET")
                await ctx.Response.WriteAsJsonAsync(new { fixtures.Run, fixtures.Ready });
            else if (ctx.Request.Path == "/__e2e/reset" && ctx.Request.Method == "POST")
            {
                var profile = ctx.Request.Query["profile"].ToString();
                if (profile is not ("empty" or "published" or "qualified" or "shortage")) { ctx.Response.StatusCode = 400; return; }
                await fixtures.Reset(ctx.RequestServices, profile);
                await ctx.Response.WriteAsJsonAsync(new { fixtures.Run });
            }
            else if (ctx.Request.Path == "/__e2e/state" && ctx.Request.Method == "GET")
            {
                using var scope = ctx.RequestServices.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var publications = await db.Set<SchedulePublication>().AsNoTracking().OrderBy(p => p.VersionNumber).ToArrayAsync();
                await ctx.Response.WriteAsJsonAsync(new
                {
                    assignments = await db.Set<ShiftAssignment>().AsNoTracking().Select(a => new { a.Id, a.UserId }).ToArrayAsync(),
                    publications = publications.Select(p => new { p.Id, p.VersionNumber,
                        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Snapshot))) }),
                    controls = await db.Set<PublicationControl>().AsNoTracking().ToArrayAsync(),
                    absences = await db.Set<AbsenceRequest>().AsNoTracking().Select(a => new { a.Id, a.Status }).ToArrayAsync()
                });
            }
            else ctx.Response.StatusCode = 404;
        });
        next(app);
    };
}

internal sealed class Fixtures(string run, string connection, string password)
{
    public string Run => run;
    public bool Ready { get; private set; }
    public int Epoch { get; private set; }
    private readonly SemaphoreSlim gate = new(1);
    public static Guid User(int n) => Guid.Parse($"71000000-0000-0000-0000-{n:000000000000}");
    public static Guid Row(int n) => Guid.Parse($"40000000-0000-0000-0000-{n:000000000000}");

    public async Task Reset(IServiceProvider services, string profile)
    {
        await gate.WaitAsync();
        try
        {
            Ready = false;
            Epoch++;
            EntryPoint.Guard(run, connection, Environment.GetEnvironmentVariable("SHIFTFLOW_E2E_TOKEN")!, password);
            using var scope = services.CreateScope(); var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            if (!StringComparer.Ordinal.Equals(db.Database.GetConnectionString(), connection))
                throw new InvalidOperationException("E2E application connection does not match the owned target.");
            await using (var c = new NpgsqlConnection(connection))
            {
                await c.OpenAsync();
                await using var ownership = new NpgsqlCommand("SELECT run FROM shiftflow_e2e_control.owner", c);
                if ((string?)await ownership.ExecuteScalarAsync() != run) throw new InvalidOperationException("E2E ownership mismatch.");
                await using var reset = new NpgsqlCommand("SET lock_timeout = '5s'; DROP SCHEMA public CASCADE; CREATE SCHEMA public", c);
                await reset.ExecuteNonQueryAsync();
            }
            await db.Database.MigrateAsync();
            var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in new[] { "ADMIN", "EMPLOYEE" })
                if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole<Guid>(role)));
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            string[] names = ["מנהל מערכת", "ראש צוות", "סגן צוות", "עובדת אלף", "עובד בית", "מוסמכת גימל", "ראש אחר", "עובד אחר", "כניסה ראשונה"];
            for (var i = 1; i <= names.Length; i++)
            {
                var parts = names[i - 1].Split(' ');
                var u = new ApplicationUser { Id = User(i), UserName = "e2e-" + i, FirstName = parts[0], LastName = parts[1],
                    IsShiftLeadQualified = i is 4 or 6 or 8, MustChangePassword = i == 9 };
                Check(await users.CreateAsync(u, password)); Check(await users.AddToRoleAsync(u, i == 1 ? "ADMIN" : "EMPLOYEE"));
            }
            var teams = await db.Set<OrganizationalTeam>().OrderBy(t => t.Slot).ToArrayAsync();
            foreach (var i in Enumerable.Range(2, 8)) db.Add(new TeamMembership { UserId = User(i), TeamId = teams[i is 7 or 8 ? 1 : 0].Id });
            await db.SaveChangesAsync();
            teams[0].LeaderUserId = User(2); teams[0].DeputyUserId = User(3); teams[1].LeaderUserId = User(7);
            var period = new SchedulePeriod { Id = Guid.Parse("72000000-0000-0000-0000-000000000001"), Year = 2031, Month = 1 };
            db.Add(period);
            foreach (var row in await db.Set<ShiftTypeDefinition>().ToArrayAsync())
            {
                row.RequiredEmployees = row.Slot == 26 ? (profile == "shortage" ? 12 : 1) : 0;
                row.IncludeInAutomaticScheduling = row.Slot == 26;
                if (row.Slot == 26) { row.Name = "משמרת בדיקה"; row.RequiresShiftLeadQualification = profile is "qualified" or "shortage"; row.TimeRange = ShiftTimeRange.DAY; row.Period = ShiftType.DAY; }
            }
            await db.SaveChangesAsync();
            var admin = (await users.FindByIdAsync(User(1).ToString()))!;
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim("AspNet.Identity.SecurityStamp", admin.SecurityStamp!), new Claim(ClaimTypes.Role, "ADMIN")], "E2E"));
            // Use the real configuration service so canonical availability anchors
            // and all window invariants are created exactly as in the admin UI.
            await sp.GetRequiredService<AvailabilityService>().Configure(2031, 1,
                new(new FixedClock().GetUtcNow().AddDays(-1), DateTimeOffset.Parse("2030-12-31T20:00:00Z")), principal);
            if (profile is "published" or "qualified")
            {
                var review = sp.GetRequiredService<ScheduleReviewService>();
                var r = await review.Review(2031, 1, principal);
                r = await review.Manual(2031, 1, new(r.Token, "ASSIGN", null, User(4), Row(26), new DateOnly(2031, 1, 5), "בדיקת פרסום"), principal);
                r = await review.Acknowledge(2031, 1, new(r.Token), principal);
                await review.Publish(2031, 1, "PRELIMINARY", new(r.Token, true, true, "בסיס לבדיקת תהליך"), principal);
            }
            Ready = true;
        }
        finally { gate.Release(); }
    }
    private static void Check(IdentityResult result)
    { if (!result.Succeeded) throw new InvalidOperationException("Invalid E2E account fixture: " + string.Join(',', result.Errors.Select(e => e.Code))); }
}
