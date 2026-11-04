using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationalControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicationControl",
                columns: table => new
                {
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationControl", x => x.PublicationId);
                    table.ForeignKey(
                        name: "FK_PublicationControl_SchedulePublication_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "SchedulePublication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftExchange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftExchange", x => x.Id);
                    table.CheckConstraint("CK_Exchange_Status", "\"Status\" IN ('AWAITING_REPLACEMENT','AWAITING_ADMIN','APPROVED_PENDING_PUBLICATION','APPLIED','REJECTED','CANCELLED','SUPERSEDED')");
                    table.ForeignKey(
                        name: "FK_ShiftExchange_AspNetUsers_ReplacementUserId",
                        column: x => x.ReplacementUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftExchange_AspNetUsers_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftExchange_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftExchangeAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftExchangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsShiftLead = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReplacementAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedPublicationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftExchangeAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftExchangeAssignment_SchedulePublication_AppliedPublicat~",
                        column: x => x.AppliedPublicationId,
                        principalTable: "SchedulePublication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftExchangeAssignment_SchedulePublication_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "SchedulePublication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftExchangeAssignment_ShiftExchange_ShiftExchangeId",
                        column: x => x.ShiftExchangeId,
                        principalTable: "ShiftExchange",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftExchangeAssignment_ShiftTypeDefinition_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "ShiftTypeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftExchangeReservation",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftExchangeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftExchangeReservation", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_ShiftExchangeReservation_ShiftExchange_ShiftExchangeId",
                        column: x => x.ShiftExchangeId,
                        principalTable: "ShiftExchange",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchange_ReplacementUserId",
                table: "ShiftExchange",
                column: "ReplacementUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchange_RequesterUserId",
                table: "ShiftExchange",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchange_SchedulePeriodId_Status",
                table: "ShiftExchange",
                columns: new[] { "SchedulePeriodId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchangeAssignment_AppliedPublicationId",
                table: "ShiftExchangeAssignment",
                column: "AppliedPublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchangeAssignment_DefinitionId",
                table: "ShiftExchangeAssignment",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchangeAssignment_PublicationId",
                table: "ShiftExchangeAssignment",
                column: "PublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchangeAssignment_ShiftExchangeId_SourceAssignmentId",
                table: "ShiftExchangeAssignment",
                columns: new[] { "ShiftExchangeId", "SourceAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftExchangeReservation_ShiftExchangeId",
                table: "ShiftExchangeReservation",
                column: "ShiftExchangeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationControl");

            migrationBuilder.DropTable(
                name: "ShiftExchangeAssignment");

            migrationBuilder.DropTable(
                name: "ShiftExchangeReservation");

            migrationBuilder.DropTable(
                name: "ShiftExchange");
        }
    }
}
