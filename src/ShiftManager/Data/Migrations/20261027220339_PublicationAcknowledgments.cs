using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicationAcknowledgments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcknowledgedOpenIssues",
                table: "SchedulePublication",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AcknowledgedShortages",
                table: "SchedulePublication",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OpenIssueCount",
                table: "SchedulePublication",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedOpenIssues",
                table: "SchedulePublication");

            migrationBuilder.DropColumn(
                name: "AcknowledgedShortages",
                table: "SchedulePublication");

            migrationBuilder.DropColumn(
                name: "OpenIssueCount",
                table: "SchedulePublication");
        }
    }
}
