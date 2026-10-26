using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleShiftLeadRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftTypeDefinition_RequiresShiftLeadQualification",
                table: "ShiftTypeDefinition");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypeDefinition_RequiresShiftLeadQualification",
                table: "ShiftTypeDefinition",
                column: "RequiresShiftLeadQualification",
                unique: true,
                filter: "\"RequiresShiftLeadQualification\" = TRUE");
        }
    }
}
