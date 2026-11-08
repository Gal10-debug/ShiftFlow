using Npgsql;
using ShiftManager.Domain;

namespace ShiftManager.Tests;

public partial class PostgresTests
{
    [PostgresFact]
    public async Task Database_image_preserves_UTF8_ICU_and_Jerusalem_boundaries()
    {
        // The application's clock uses host tzdata; PostgreSQL uses image tzdata.
        // Compare both sides so an image update cannot silently move stored shifts.
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using (var settings = new NpgsqlCommand("""
            SELECT current_setting('server_version_num')::int,
                current_setting('server_encoding'),
                EXISTS (SELECT 1 FROM pg_collation WHERE collname = 'he-x-icu'),
                EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'plpgsql')
            """, connection))
        await using (var reader = await settings.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.InRange(reader.GetInt32(0), 170000, 179999);
            Assert.Equal("UTF8", reader.GetString(1));
            Assert.True(reader.GetBoolean(2), "The image must retain Hebrew ICU collation support.");
            Assert.True(reader.GetBoolean(3), "Migration triggers require PL/pgSQL.");
        }
        foreach (var (date, hours) in new[] { (new DateOnly(2026, 3, 26), 11), (new DateOnly(2026, 10, 24), 13), (new DateOnly(2026, 1, 10), 12) })
        {
            await using var query = new NpgsqlCommand("""
                SELECT (@day::date + TIME '19:00') AT TIME ZONE 'Asia/Jerusalem',
                    ((@day::date + 1) + TIME '07:00') AT TIME ZONE 'Asia/Jerusalem',
                    @text::text
                """, connection);
            query.Parameters.AddWithValue("day", date);
            query.Parameters.AddWithValue("text", "בדיקת תאריך ושעון ישראל");
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var expected = ShiftClock.Boundaries(date, ShiftType.NIGHT);
            Assert.Equal(expected.StartsAt, reader.GetDateTime(0));
            Assert.Equal(expected.EndsAt, reader.GetDateTime(1));
            Assert.Equal(hours, (reader.GetDateTime(1) - reader.GetDateTime(0)).TotalHours);
            Assert.Equal("בדיקת תאריך ושעון ישראל", reader.GetString(2));
        }
    }
}
