using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmployeeType",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "REGULAR");

            migrationBuilder.AddCheckConstraint(
                name: "CK_User_EmployeeType",
                table: "AspNetUsers",
                sql: "\"EmployeeType\" IN ('REGULAR', 'DISPATCHER', 'MANAGER', 'RESERVIST', 'DUTY_MANAGER')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_User_EmployeeType",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmployeeType",
                table: "AspNetUsers");
        }
    }
}
