using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace ShiftManager.Data;

// Migration scaffolding does not start HTTP/background workers or require a live DB.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true).AddEnvironmentVariables().Build();
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(
            config.GetConnectionString("Database") ?? "Host=localhost;Database=shiftmanager;Username=shiftmanager").Options);
    }
}
