using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeekendBoardNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ShiftAssignment",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // Clear the old classification first: the non-ordinary semantic index is unique.
            migrationBuilder.Sql("""
                UPDATE "ShiftTypeDefinition" SET "Semantic" = 'ORDINARY', "Version" = "Version" + 1
                WHERE "Id" = '40000000-0000-0000-0000-000000000026';
                UPDATE "ShiftTypeDefinition" SET "Semantic" = 'WEEKEND_ON_CALL', "Version" = "Version" + 1
                WHERE "Id" = '40000000-0000-0000-0000-000000000016';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "ShiftAssignment");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000016"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000026"),
                column: "Semantic",
                value: "WEEKEND_ON_CALL");
        }
    }
}
