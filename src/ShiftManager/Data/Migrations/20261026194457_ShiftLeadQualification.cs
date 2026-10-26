using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShiftLeadQualification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresShiftLeadQualification",
                table: "ShiftTypeDefinition",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsShiftLeadQualified",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypeDefinition_RequiresShiftLeadQualification",
                table: "ShiftTypeDefinition",
                column: "RequiresShiftLeadQualification",
                unique: true,
                filter: "\"RequiresShiftLeadQualification\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftTypeDefinition_RequiresShiftLeadQualification",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropColumn(
                name: "RequiresShiftLeadQualification",
                table: "ShiftTypeDefinition");

            migrationBuilder.DropColumn(
                name: "IsShiftLeadQualified",
                table: "AspNetUsers");
        }
    }
}
