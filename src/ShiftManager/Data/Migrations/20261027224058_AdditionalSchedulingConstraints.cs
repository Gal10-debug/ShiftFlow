using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdditionalSchedulingConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Semantic",
                table: "ShiftTypeDefinition",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000001"),
                column: "Semantic",
                value: "SIX_NINE");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000002"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000003"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000004"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000005"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000006"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000007"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000008"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000009"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000010"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000011"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000012"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000013"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000014"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000015"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000016"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000017"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000018"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000019"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000020"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000021"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000022"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000023"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000024"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000025"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000026"),
                column: "Semantic",
                value: "WEEKEND_ON_CALL");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000027"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000028"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000029"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000030"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000031"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000032"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000033"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000034"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000035"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000036"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000037"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000038"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000039"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000040"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000041"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000042"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000043"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000044"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000045"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000046"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000047"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000048"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000049"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000050"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.UpdateData(
                table: "ShiftTypeDefinition",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000051"),
                column: "Semantic",
                value: "ORDINARY");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypeDefinition_Semantic",
                table: "ShiftTypeDefinition",
                column: "Semantic",
                unique: true,
                filter: "\"Semantic\" <> 'ORDINARY'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTypeDefinition_Semantic",
                table: "ShiftTypeDefinition",
                sql: "\"Semantic\" IN ('ORDINARY','SIX_NINE','WEEKEND_ON_CALL')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftTypeDefinition_Semantic",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTypeDefinition_Semantic",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropColumn(
                name: "Semantic",
                table: "ShiftTypeDefinition");
        }
    }
}
