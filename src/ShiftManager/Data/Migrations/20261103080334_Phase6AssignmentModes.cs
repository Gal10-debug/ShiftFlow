using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6AssignmentModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignmentMode",
                table: "ShiftTypeDefinition",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Automatic");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000012"),
                column: "AssignmentMode",
                value: "ManualOnly");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000013"),
                column: "AssignmentMode",
                value: "ManualOnly");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000014"),
                column: "AssignmentMode",
                value: "ManualOnly");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTypeDefinition_AssignmentMode",
                table: "ShiftTypeDefinition",
                sql: "\"AssignmentMode\" IN ('Automatic','ManualOnly')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTypeDefinition_AssignmentMode",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "ShiftTypeDefinition");
        }
    }
}
