using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchedulingPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchedulingPriority",
                table: "ShiftTypeDefinition",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000001"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000002"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000003"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000004"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000005"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000006"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000007"),
                column: "SchedulingPriority",
                value: "Critical");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTypeDefinition_SchedulingPriority",
                table: "ShiftTypeDefinition",
                sql: "\"SchedulingPriority\" IN ('Critical','High','Normal')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTypeDefinition_SchedulingPriority",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropColumn(
                name: "SchedulingPriority",
                table: "ShiftTypeDefinition");
        }
    }
}
