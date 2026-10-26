using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchedulingBoardShiftTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftTypeDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequiredEmployees = table.Column<int>(type: "integer", nullable: false),
                    IncludeInAutomaticScheduling = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTypeDefinition", x => x.Id);
                    table.CheckConstraint("CK_ShiftTypeDefinition_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ShiftTypeDefinition_Order", "\"DisplayOrder\" BETWEEN 1 AND 51");
                    table.CheckConstraint("CK_ShiftTypeDefinition_Period", "\"Period\" IN ('DAY','NIGHT')");
                    table.CheckConstraint("CK_ShiftTypeDefinition_Slot", "\"Slot\" BETWEEN 1 AND 51");
                    table.CheckConstraint("CK_ShiftTypeDefinition_Staffing", "\"RequiredEmployees\" BETWEEN 0 AND 1000");
                    table.CheckConstraint("CK_ShiftTypeDefinition_Version", "\"Version\" > 0");
                });

            migrationBuilder.InsertData(
                table: "ShiftTypeDefinition",
                columns: new[] { "Id", "DisplayOrder", "IncludeInAutomaticScheduling", "Name", "Period", "RequiredEmployees", "Slot", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000001"), 1, false, "משמרת 1", "DAY", 0, 1, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000002"), 2, false, "משמרת 2", "DAY", 0, 2, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000003"), 3, false, "משמרת 3", "DAY", 0, 3, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000004"), 4, false, "משמרת 4", "DAY", 0, 4, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000005"), 5, false, "משמרת 5", "DAY", 0, 5, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000006"), 6, false, "משמרת 6", "DAY", 0, 6, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000007"), 7, false, "משמרת 7", "DAY", 0, 7, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000008"), 8, false, "משמרת 8", "DAY", 0, 8, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000009"), 9, false, "משמרת 9", "DAY", 0, 9, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000010"), 10, false, "משמרת 10", "DAY", 0, 10, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000011"), 11, false, "משמרת 11", "DAY", 0, 11, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000012"), 12, false, "משמרת 12", "DAY", 0, 12, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000013"), 13, false, "משמרת 13", "DAY", 0, 13, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000014"), 14, false, "משמרת 14", "DAY", 0, 14, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000015"), 15, false, "משמרת 15", "DAY", 0, 15, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000016"), 16, false, "משמרת 16", "DAY", 0, 16, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000017"), 17, false, "משמרת 17", "DAY", 0, 17, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000018"), 18, false, "משמרת 18", "DAY", 0, 18, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000019"), 19, false, "משמרת 19", "DAY", 0, 19, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000020"), 20, false, "משמרת 20", "DAY", 0, 20, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000021"), 21, false, "משמרת 21", "DAY", 0, 21, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000022"), 22, false, "משמרת 22", "DAY", 0, 22, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000023"), 23, false, "משמרת 23", "DAY", 0, 23, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000024"), 24, false, "משמרת 24", "DAY", 0, 24, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000025"), 25, false, "משמרת 25", "DAY", 0, 25, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000026"), 26, false, "משמרת 26", "DAY", 0, 26, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000027"), 27, false, "משמרת 27", "DAY", 0, 27, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000028"), 28, false, "משמרת 28", "DAY", 0, 28, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000029"), 29, false, "משמרת 29", "DAY", 0, 29, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000030"), 30, false, "משמרת 30", "DAY", 0, 30, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000031"), 31, false, "משמרת 31", "DAY", 0, 31, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000032"), 32, false, "משמרת 32", "DAY", 0, 32, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000033"), 33, false, "משמרת 33", "DAY", 0, 33, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000034"), 34, false, "משמרת 34", "DAY", 0, 34, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000035"), 35, false, "משמרת 35", "DAY", 0, 35, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000036"), 36, false, "משמרת 36", "DAY", 0, 36, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000037"), 37, false, "משמרת 37", "DAY", 0, 37, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000038"), 38, false, "משמרת 38", "DAY", 0, 38, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000039"), 39, false, "משמרת 39", "DAY", 0, 39, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000040"), 40, false, "משמרת 40", "DAY", 0, 40, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000041"), 41, false, "משמרת 41", "DAY", 0, 41, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000042"), 42, false, "משמרת 42", "DAY", 0, 42, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000043"), 43, false, "משמרת 43", "DAY", 0, 43, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000044"), 44, false, "משמרת 44", "DAY", 0, 44, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000045"), 45, false, "משמרת 45", "DAY", 0, 45, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000046"), 46, false, "משמרת 46", "DAY", 0, 46, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000047"), 47, false, "משמרת 47", "DAY", 0, 47, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000048"), 48, false, "משמרת 48", "DAY", 0, 48, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000049"), 49, false, "משמרת 49", "DAY", 0, 49, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000050"), 50, false, "משמרת 50", "DAY", 0, 50, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { new Guid("40000000-0000-0000-0000-000000000051"), 51, false, "משמרת 51", "DAY", 0, 51, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            // Allow order swaps in one transaction while still requiring a unique final order.
            migrationBuilder.Sql("""
                ALTER TABLE "ShiftTypeDefinition"
                    ADD CONSTRAINT "IX_ShiftTypeDefinition_DisplayOrder" UNIQUE ("DisplayOrder")
                    DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypeDefinition_Slot",
                table: "ShiftTypeDefinition",
                column: "Slot",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftTypeDefinition");
        }
    }
}
