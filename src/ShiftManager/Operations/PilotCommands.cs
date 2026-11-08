using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Operations;

public static class PilotCommands
{
    public static async Task<int> Run(WebApplication app, PilotConfiguration? pilot, string command, string confirmation)
    {
        if (pilot is null || confirmation != pilot.Confirmation || command is not ("migrate" or "bootstrap" or "invalidate-sessions" or "inspect-keys"))
        { Console.Error.WriteLine("Pilot command refused: explicit matching target confirmation required."); return 1; }
        try
        {
            await using (var connection = new NpgsqlConnection(pilot.Connection))
            {
                await connection.OpenAsync();
                await using var check = new NpgsqlCommand("SELECT current_database() = @database AND (SELECT deployment_id = @id FROM shiftflow_ops.identity)", connection);
                check.Parameters.AddWithValue("database", pilot.DatabaseName); check.Parameters.AddWithValue("id", pilot.DeploymentId.ToString("N"));
                if ((bool?)await check.ExecuteScalarAsync() != true) throw new InvalidOperationException();
            }
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            switch (command)
            {
                case "migrate":
                    await db.Database.MigrateAsync();
                    // This identifier is derived exclusively from the validated GUID.
                    await using (var connection = new NpgsqlConnection(pilot.Connection))
                    {
                        await connection.OpenAsync();
                        await using var revoke = new NpgsqlCommand("REVOKE INSERT, UPDATE, DELETE ON TABLE \"__EFMigrationsHistory\" FROM \"" + pilot.Role("runtime") + "\"", connection);
                        await revoke.ExecuteNonQueryAsync();
                    }
                    break;
                case "bootstrap": await AdminBootstrap.Run(app.Services, app.Configuration); break;
                case "invalidate-sessions":
                    await db.Database.ExecuteSqlRawAsync("UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = gen_random_uuid()::text, \"ConcurrencyStamp\" = gen_random_uuid()::text");
                    break;
                case "inspect-keys":
                    var files = Directory.GetFiles(pilot.DirectoryPath, "key-*.xml");
                    var encrypted = files.Length > 0 && files.All(f => XDocument.Load(f).Descendants()
                        .Any(e => e.Name.LocalName == "encryptedSecret" && e.Descendants().Any(d => d.Name.LocalName == "EncryptedData")));
                    Console.WriteLine(JsonSerializer.Serialize(new { keyFiles = files.Length, encrypted,
                        permissions = OperatingSystem.IsWindows() ? "platform-dependent" : File.GetUnixFileMode(pilot.DirectoryPath).ToString() }));
                    return encrypted ? 0 : 1;
            }
            Console.WriteLine("Pilot operation completed for the verified target."); return 0;
        }
        catch (Exception error)
        {
            // No exception messages or SQL parameters: commands may handle credentials.
            Console.Error.WriteLine($"Pilot operation failed ({error.GetType().Name}); no success is claimed."); return 1;
        }
    }
}
