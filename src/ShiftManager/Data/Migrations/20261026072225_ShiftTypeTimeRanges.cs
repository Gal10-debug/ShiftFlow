using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShiftTypeTimeRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeRange",
                table: "ShiftTypeDefinition",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Preserve the effective range of every existing administrator-configured row.
            migrationBuilder.Sql("""
                UPDATE "ShiftTypeDefinition" SET "TimeRange" = "Period";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTypeDefinition_TimeRange",
                table: "ShiftTypeDefinition",
                sql: "\"TimeRange\" IS NULL OR (\"TimeRange\" IN ('DAY','NIGHT','H06_09','H07_13','H07_15','H15_23','H23_07') AND \"Period\" = CASE WHEN \"TimeRange\" IN ('NIGHT','H23_07') THEN 'NIGHT' ELSE 'DAY' END)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTypeDefinition_TimeRange",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropColumn(
                name: "TimeRange",
                table: "ShiftTypeDefinition");
        }
    }
}
