using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ShiftManager.Data;
using ShiftManager.Development;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class DemoIntegrationTests
{
    [PostgresFact]
    public async Task Isolated_demo_is_repeatable_validated_publishable_and_cleanup_preserves_unknown_data()
    {
        await using var database = new TestDatabase();
        await database.StartAsync();
        var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var month = new DateOnly(2026,11,1);
        var name = DemoCli.DatabaseName(month,seed);
        await using var admin = new NpgsqlConnection(database.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"",admin)) await create.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Database = name, Host = "127.0.0.1", Pooling = false };
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".demo", name);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Database",cs.ConnectionString).ConfigureServices(services =>
                {
                    var worker = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(GenerationWorker));
                    if (worker != null) services.Remove(worker);
                }));
            var args = new[] {"seed-demo","--month","2026-11","--seed",seed.ToString()};
            await DemoCli.Run(factory.Services,"Testing",cs.ConnectionString,args);
            var credentials = await File.ReadAllTextAsync(Path.Combine(output,"accounts.json"));
            await DemoCli.Run(factory.Services,"Testing",cs.ConnectionString,args);
            Assert.Equal(credentials,await File.ReadAllTextAsync(Path.Combine(output,"accounts.json")));
            using (var scope = factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(60,await db.Users.CountAsync());
                Assert.Equal(4,await db.Set<OrganizationalTeam>().CountAsync());
                Assert.Equal(60,await db.Set<TeamMembership>().CountAsync());
                Assert.Equal(51,await db.Set<ShiftTypeDefinition>().CountAsync());
                Assert.Equal(8,await db.Set<ShiftTypeDefinition>().CountAsync(d=>d.IncludeInAutomaticScheduling));
                var p=await scope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026,11);
                var teams=await db.Set<OrganizationalTeam>().ToArrayAsync();
                Assert.Equal(56,p.Employees.Length);
                Assert.All(teams,t=>{Assert.DoesNotContain(p.Employees,e=>e.Id==t.LeaderUserId);Assert.Contains(p.Employees,e=>e.Id==t.DeputyUserId);});
                Assert.Equal(52,await db.Set<AvailabilitySubmission>().CountAsync());
            }
            await DemoCli.Run(factory.Services,"Testing",cs.ConnectionString,["smoke-demo","--month","2026-11","--seed",seed.ToString()]);
            using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"verification-report.json")));
            Assert.True(report.RootElement.GetProperty("Success").GetBoolean());
            Directory.CreateDirectory("TestResults");
            File.Copy(Path.Combine(output,"verification-report.json"), Path.Combine("TestResults","demo-verification.json"), true);
            Assert.Empty(report.RootElement.GetProperty("Violations").EnumerateArray());
            // Unknown records, including synthetic-looking ones, must prevent destructive cleanup.
            await using (var connection=new NpgsqlConnection(cs.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command=new NpgsqlCommand("CREATE TABLE foreign_data (value text); INSERT INTO foreign_data VALUES ('preserve me')",connection);
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(()=>DemoCli.Run(factory.Services,"Testing",cs.ConnectionString,
                ["clean-demo","--month","2026-11","--seed",seed.ToString(),"--confirm",name]));
            await using (var connection=new NpgsqlConnection(cs.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command=new NpgsqlCommand("SELECT value FROM foreign_data",connection);
                Assert.Equal("preserve me",await command.ExecuteScalarAsync());
                await using var remove=new NpgsqlCommand("DROP TABLE foreign_data",connection);
                await remove.ExecuteNonQueryAsync();
            }
            await DemoCli.Run(factory.Services,"Testing",cs.ConnectionString,["clean-demo","--month","2026-11","--seed",seed.ToString(),"--confirm",name]);
        }
        finally
        {
            await using var drop=new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)",admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(output)) Directory.Delete(output,true);
        }
    }
    [PostgresFact]
    public async Task Seeding_refuses_a_preexisting_database_even_with_the_expected_name()
    {
        await using var database=new TestDatabase(); await database.StartAsync();
        var seed=RandomNumberGenerator.GetInt32(1,int.MaxValue);var name=DemoCli.DatabaseName(new(2026,11,1),seed);
        await using var admin=new NpgsqlConnection(database.GetConnectionString());await admin.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE DATABASE \"{name}\"",admin))await create.ExecuteNonQueryAsync();
        var cs=new NpgsqlConnectionStringBuilder(database.GetConnectionString()){Database=name,Host="127.0.0.1",Pooling=false};
        try
        {
            await using var connection=new NpgsqlConnection(cs.ConnectionString);await connection.OpenAsync();
            await using(var create=new NpgsqlCommand("CREATE TABLE real_data (id integer); INSERT INTO real_data VALUES (7)",connection))await create.ExecuteNonQueryAsync();
            using var provider=new ServiceCollection().AddDbContext<ApplicationDbContext>(o=>o.UseNpgsql(cs.ConnectionString)).BuildServiceProvider();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>DemoCli.Run(provider,"Testing",cs.ConnectionString,["seed-demo","--month","2026-11","--seed",seed.ToString()]));
            await using var query=new NpgsqlCommand("SELECT id FROM real_data",connection);Assert.Equal(7,await query.ExecuteScalarAsync());
        }
        finally{await using var drop=new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();}
    }
}
