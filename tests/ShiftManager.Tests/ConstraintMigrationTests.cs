using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftManager.Domain;
namespace ShiftManager.Tests;
public partial class PostgresTests
{
    [PostgresFact]
    public async Task Constraint_migration_binds_stable_existing_IDs_without_overwriting_names_or_creating_rows()
    {
        await using var db=Context();var migrator=db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261027220339_PublicationAcknowledgments");
        await db.Database.ExecuteSqlRawAsync("UPDATE \"ShiftTypeDefinition\" SET \"Name\"='שם ששונה לפני המיגרציה', \"RequiredEmployees\"=2 WHERE \"Slot\" IN (1,26)");
        await db.Database.MigrateAsync();db.ChangeTracker.Clear();var rows=await db.Set<ShiftTypeDefinition>().OrderBy(d=>d.Slot).ToArrayAsync();
        Assert.Equal(51,rows.Length);Assert.Equal("SIX_NINE",rows[0].Semantic);Assert.Equal("WEEKEND_ON_CALL",rows[25].Semantic);
        Assert.Equal("שם ששונה לפני המיגרציה",rows[0].Name);Assert.Equal(2,rows[25].RequiredEmployees);
        Assert.Equal(49,rows.Count(r=>r.Semantic=="ORDINARY"));await db.Database.MigrateAsync();Assert.Equal(51,await db.Set<ShiftTypeDefinition>().CountAsync());Assert.False(db.Database.HasPendingModelChanges());
    }
}
