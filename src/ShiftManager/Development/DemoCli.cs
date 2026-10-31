using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Development;

// No HTTP route registers this class. The marker is deliberately outside application migrations.
public static class DemoCli
{
    public static void ValidateTarget(string environment, string connection, DateOnly month, int seed)
    {
        if (environment is not ("Development" or "Testing")) throw new InvalidOperationException("Demo commands require Development or Testing.");
        var cs = new NpgsqlConnectionStringBuilder(connection);
        if (cs.Host is not ("localhost" or "127.0.0.1" or "::1") || cs.Database != DatabaseName(month, seed) || cs.Multiplexing)
            throw new InvalidOperationException("Set SHIFTFLOW_DEMO_CONNECTION to the exact isolated loopback demo database; normal development targets are rejected.");
    }
    public static string DatabaseName(DateOnly month, int seed) => $"shiftflow_demo_{month:yyyyMM}_{seed.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace('-', 'n')}";
    public static ClaimsPrincipal Principal(ApplicationUser user, params string[] roles) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!),
            new Claim("password_change_required", "false") }.Concat(roles.Select(r => new Claim(ClaimTypes.Role, r))), "demo-cli"));

    public static async Task Run(IServiceProvider services, string environment, string connection, string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i += 2)
            if (i + 1 == args.Length || args[i] is not ("--month" or "--seed" or "--confirm") || !options.TryAdd(args[i], args[i + 1]))
                throw new InvalidOperationException("Usage: seed-demo|clean-demo|smoke-demo --month YYYY-MM --seed 42 [--confirm DATABASE_NAME]");
        var month = DemoPlan.ParseMonth(options.GetValueOrDefault("--month", ""));
        if (!int.TryParse(options.GetValueOrDefault("--seed", "42"), out var seed)) throw new InvalidOperationException("Invalid integer seed.");
        ValidateTarget(environment, connection, month, seed);
        var name = DatabaseName(month, seed);
        await using var guard = new NpgsqlConnection(connection);
        await guard.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(781520)", guard)) await command.ExecuteNonQueryAsync();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = await Scalar(guard, "SELECT to_regclass('public.shiftflow_demo_owner') IS NOT NULL") is true;
        if (!marker)
        {
            if (args[0] != "seed-demo" || await Scalar(guard, "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema')") is not long count || count != 0)
                throw new InvalidOperationException("Unverified database: seed only a newly created, empty dedicated database. No existing data was changed.");
            await using var command = new NpgsqlCommand("CREATE TABLE public.shiftflow_demo_owner (id integer PRIMARY KEY CHECK(id=1), version text NOT NULL, fingerprint text NOT NULL, complete boolean NOT NULL)", guard);
            await command.ExecuteNonQueryAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO public.shiftflow_demo_owner VALUES (1,@version,'',false)", guard);
            insert.Parameters.AddWithValue("version", $"{DemoPlan.Version}/{month:yyyy-MM}/{seed}");
            await insert.ExecuteNonQueryAsync();
        }
        var expected = $"{DemoPlan.Version}/{month:yyyy-MM}/{seed}";
        if (!Equals(await Scalar(guard, "SELECT version FROM public.shiftflow_demo_owner WHERE id=1"), expected)) throw new InvalidOperationException("Demo ownership mismatch.");
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".demo", name);
        Directory.CreateDirectory(output);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (args[0] == "clean-demo")
        {
            if (options.GetValueOrDefault("--confirm") != name) throw new InvalidOperationException($"Cleanup requires explicit --confirm {name}");
            // Fence new connections before checking ownership, so a concurrent writer cannot
            // add real data between fingerprint verification and DROP DATABASE.
            var adminSettings = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres", Pooling = false };
            await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
            await admin.OpenAsync();
            var dropped = false;
            await using (var fence = new NpgsqlCommand($"ALTER DATABASE \"{name}\" ALLOW_CONNECTIONS false", admin)) await fence.ExecuteNonQueryAsync();
            try
            {
                await using var sessions = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname=@name AND pid<>@pid", admin);
                sessions.Parameters.AddWithValue("name", name);
                sessions.Parameters.AddWithValue("pid", guard.ProcessID);
                if (Convert.ToInt64(await sessions.ExecuteScalarAsync()) != 0)
                    throw new InvalidOperationException("Stop all demo database connections before cleanup.");
                var fingerprint = await Fingerprint(guard);
                if (!Equals(await Scalar(guard, "SELECT fingerprint FROM public.shiftflow_demo_owner WHERE id=1"), fingerprint))
                    throw new InvalidOperationException("Cleanup refused: database changed since seeding/verified smoke run; ownership is ambiguous. Preserve and inspect it.");
                await guard.CloseAsync();
                await db.DisposeAsync();
                NpgsqlConnection.ClearAllPools();
                // No FORCE and no disabling immutable-publication triggers or foreign keys.
                await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\"", admin);
                await drop.ExecuteNonQueryAsync();
                dropped = true;
            }
            finally
            {
                if (!dropped)
                {
                    await using var restore = new NpgsqlCommand($"ALTER DATABASE \"{name}\" ALLOW_CONNECTIONS true", admin);
                    await restore.ExecuteNonQueryAsync();
                }
            }
            File.Delete(Path.Combine(output, "accounts.json"));
            Console.WriteLine($"Removed verified isolated demo database {name}. Reports retained.");
            return;
        }
        if (args[0] == "smoke-demo")
        {
            if (environment != "Testing") throw new InvalidOperationException("smoke-demo requires Testing and a freshly seeded dedicated test demo database.");
            if (await Scalar(guard, "SELECT complete FROM public.shiftflow_demo_owner WHERE id=1") is not true
                || !Equals(await Scalar(guard, "SELECT fingerprint FROM public.shiftflow_demo_owner WHERE id=1"), await Fingerprint(guard)))
                throw new InvalidOperationException("Smoke test requires an unchanged, completed seed; existing modifications are preserved.");
            await DemoSmoke.Run(scope.ServiceProvider, month, output);
        }
        else if (await Scalar(guard, "SELECT complete FROM public.shiftflow_demo_owner WHERE id=1") is not true)
        {
            await db.Database.MigrateAsync();
            if (await db.Users.AnyAsync() || await db.Set<SchedulePeriod>().AnyAsync() || await db.Set<TeamMembership>().AnyAsync()
                || await db.Set<OrganizationalTeam>().AnyAsync(t => t.LeaderUserId != null || t.DeputyUserId != null))
                throw new InvalidOperationException("Partial seed or existing data detected. Preserved for inspection; not overwritten.");
            await Seed(scope.ServiceProvider, month, seed, output);
            await using var complete = new NpgsqlCommand("UPDATE public.shiftflow_demo_owner SET complete=true WHERE id=1", guard);
            await complete.ExecuteNonQueryAsync();
        }
        else
        {
            Console.WriteLine("Identical demo scenario already seeded; no database or credential changes made.");
            return;
        }
        var hash = await Fingerprint(guard);
        await using var update = new NpgsqlCommand("UPDATE public.shiftflow_demo_owner SET fingerprint=@hash WHERE id=1", guard);
        update.Parameters.AddWithValue("hash", hash);
        await update.ExecuteNonQueryAsync();
        Console.WriteLine($"Demo artifacts: {output}");
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); return await command.ExecuteScalarAsync(); }

    internal static async Task<string> Fingerprint(NpgsqlConnection connection)
    {
        var tables = new List<(string Schema, string Name)>();
        await using (var command = new NpgsqlCommand("SELECT schemaname,tablename FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema') AND tablename <> 'shiftflow_demo_owner' ORDER BY schemaname,tablename", connection))
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) tables.Add((reader.GetString(0), reader.GetString(1)));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var table in tables)
        {
            string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            digest.AppendData(Encoding.UTF8.GetBytes(table.Schema + "." + table.Name));
            await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM {Quote(table.Schema)}.{Quote(table.Name)} t ORDER BY row_to_json(t)::text", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) digest.AppendData(Encoding.UTF8.GetBytes(reader.GetString(0) + "\n"));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static async Task Seed(IServiceProvider services, DateOnly month, int seed, string output)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var teams = services.GetRequiredService<TeamService>();
        var accounts = DemoPlan.Accounts(seed);
        var directory = new List<object>();
        // Secrets are random, never derived from the repeatable public seed.
        foreach (var a in accounts)
        {
            var password = "Df!9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
            var user = new ApplicationUser { Id = a.Id, UserName = a.Login, FirstName = "Demo", LastName = $"Synthetic {a.Index + 1:00}",
                IsShiftLeadQualified = a.Qualified, LockoutEnabled = true };
            ApiErrors.Ensure(await users.CreateAsync(user, password));
            ApiErrors.Ensure(await users.AddToRolesAsync(user, a.Index == 0 ? ["ADMIN", "EMPLOYEE"] : ["EMPLOYEE"]));
            directory.Add(new { a.Login, Password = password, user.FirstName, user.LastName, a.Team, a.Position, a.Qualified, Roles = a.Index == 0 ? "ADMIN,EMPLOYEE" : "EMPLOYEE" });
        }
        var path = Path.Combine(output, "accounts.json");
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var stream = new FileStream(path, fileOptions))
            await JsonSerializer.SerializeAsync(stream, directory, new JsonSerializerOptions { WriteIndented = true });
        var admin = Principal((await users.FindByIdAsync(accounts[0].Id.ToString()))!, "ADMIN", "EMPLOYEE");
        var existingTeams = await db.Set<OrganizationalTeam>().OrderBy(t => t.Slot).ToArrayAsync();
        if (existingTeams.Length != 4) throw new InvalidOperationException("Expected exactly the four migrated teams.");
        foreach (var t in existingTeams)
        {
            foreach (var a in accounts.Where(a => a.Team == t.Slot)) await teams.Membership(a.Id, t.Id, admin);
            var current = (await teams.List(admin, true)).Single(x => x.Id == t.Id);
            await teams.Update(t.Id, new(current.Name, accounts.Single(a => a.Team == t.Slot && a.Position == "leader").Id,
                accounts.Single(a => a.Team == t.Slot && a.Position == "deputy").Id, current.Version), admin);
        }
        var scheduling = services.GetRequiredService<SchedulingService>();
        var definitions = await scheduling.List();
        var selected = definitions.Where(d => d.Semantic != "ORDINARY").Select(d => d.Id)
            .Concat(definitions.Where(d => d.Semantic == "ORDINARY").OrderBy(d => d.Slot).Take(6).Select(d => d.Id)).ToHashSet();
        await scheduling.Update(new(definitions.Select((d) => new ShiftTypeUpdate(d.Id, d.Version, d.Name, d.DisplayOrder,
            d.Slot == 3 ? "NIGHT" : "DAY", selected.Contains(d.Id) ? 1 : 0, selected.Contains(d.Id),
            d.Semantic == SchedulingRules.SixNine ? "H06_09" : d.Slot == 3 ? "NIGHT" : "DAY", d.Slot == 4)).ToArray()), admin);
        // Scheduling settings use parameterized SQL; start the next service operation
        // with a fresh identity map, as separate HTTP request scopes normally do.
        db.ChangeTracker.Clear();
        var availability = services.GetRequiredService<AvailabilityService>();
        await availability.Configure(month.Year, month.Month, new(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)), admin);
        await availability.SetState(month.Year, month.Month, true, admin);
        foreach (var a in accounts.Where(a => a.Position != "leader" && a.Index % 10 != 0))
        {
            var user = (await users.FindByIdAsync(a.Id.ToString()))!;
            await availability.Submit(month.Year, month.Month, new(0, DemoPlan.Selections(month, seed, a.Index)), Principal(user, "EMPLOYEE"), false);
        }
        var summary = await availability.Summary(month.Year, month.Month);
        await File.WriteAllTextAsync(Path.Combine(output, "seed-report.json"), JsonSerializer.Serialize(new { Accounts = 60, Leaders = 4, Deputies = 4,
            OrdinaryEmployees = 52, Eligible = summary.EligibleCount, Submitted = summary.SubmittedCount, Teams = accounts.GroupBy(a => a.Team).Select(g => new { Team = g.Key, Members = g.Count() }),
            Scheduling = "Not run", Publication = "Not run", TimeZone = SchedulingService.ZoneId }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
