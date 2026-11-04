using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6Absences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbsenceRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Recurring = table.Column<bool>(type: "boolean", nullable: false),
                    FullDay = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ReplacesId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacesVersion = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceRequest", x => x.Id);
                    table.CheckConstraint("CK_Absence_Dates", "\"StartDate\" BETWEEN '0002-01-01' AND '9998-12-30' AND (\"EndDate\" IS NULL OR \"EndDate\" BETWEEN \"StartDate\" AND '9998-12-30') AND (\"Recurring\" OR \"EndDate\" IS NOT NULL)");
                    table.CheckConstraint("CK_Absence_Kind", "(\"Kind\" = 'CREATE' AND \"ReplacesId\" IS NULL AND \"ReplacesVersion\" IS NULL) OR (\"Kind\" IN ('MODIFY','CANCEL') AND \"ReplacesId\" IS NOT NULL AND \"ReplacesVersion\" > 0)");
                    table.CheckConstraint("CK_Absence_Reason", "\"Reason\" IN ('STUDIES','RESERVE','VACATION','OTHER')");
                    table.CheckConstraint("CK_Absence_Status", "\"Status\" IN ('PENDING','APPROVED','REJECTED','CANCELLED')");
                    table.CheckConstraint("CK_Absence_Times", "(\"FullDay\" AND \"StartTime\" IS NULL AND \"EndTime\" IS NULL) OR (NOT \"FullDay\" AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"StartTime\" <> \"EndTime\")");
                    table.CheckConstraint("CK_Absence_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AbsenceRequest_AbsenceRequest_ReplacesId",
                        column: x => x.ReplacesId,
                        principalTable: "AbsenceRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceRequest_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AbsenceConflict",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsenceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ShiftName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByPublicationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceConflict", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceConflict_AbsenceRequest_AbsenceRequestId",
                        column: x => x.AbsenceRequestId,
                        principalTable: "AbsenceRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceConflict_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceConflict_SchedulePublication_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "SchedulePublication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceConflict_SchedulePublication_ResolvedByPublicationId",
                        column: x => x.ResolvedByPublicationId,
                        principalTable: "SchedulePublication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AbsenceDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsenceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceDecision", x => x.Id);
                    table.CheckConstraint("CK_AbsenceDecision_Value", "\"Decision\" IN ('APPROVED','REJECTED','CANCELLED') AND (\"Decision\" <> 'REJECTED' OR length(btrim(\"RejectionReason\")) > 0 AND \"RejectionReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AbsenceDecision_AbsenceRequest_AbsenceRequestId",
                        column: x => x.AbsenceRequestId,
                        principalTable: "AbsenceRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceDecision_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AbsenceWeekday",
                columns: table => new
                {
                    AbsenceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceWeekday", x => new { x.AbsenceRequestId, x.Day });
                    table.CheckConstraint("CK_AbsenceWeekday_Day", "\"Day\" BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_AbsenceWeekday_AbsenceRequest_AbsenceRequestId",
                        column: x => x.AbsenceRequestId,
                        principalTable: "AbsenceRequest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceConflict_AbsenceRequestId_PublicationId_AssignmentId",
                table: "AbsenceConflict",
                columns: new[] { "AbsenceRequestId", "PublicationId", "AssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceConflict_PublicationId",
                table: "AbsenceConflict",
                column: "PublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceConflict_ResolvedByPublicationId",
                table: "AbsenceConflict",
                column: "ResolvedByPublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceConflict_SchedulePeriodId_ResolvedAt",
                table: "AbsenceConflict",
                columns: new[] { "SchedulePeriodId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDecision_AbsenceRequestId",
                table: "AbsenceDecision",
                column: "AbsenceRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDecision_ActorUserId",
                table: "AbsenceDecision",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceRequest_ReplacesId",
                table: "AbsenceRequest",
                column: "ReplacesId",
                unique: true,
                filter: "\"Status\" = 'PENDING' AND \"ReplacesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceRequest_Status_CreatedAt",
                table: "AbsenceRequest",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceRequest_UserId_Status_StartDate",
                table: "AbsenceRequest",
                columns: new[] { "UserId", "Status", "StartDate" });
            migrationBuilder.Sql("""
                CREATE FUNCTION immutable_absence_decision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Absence decisions are immutable' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER immutable_absence_decision BEFORE UPDATE OR DELETE ON "AbsenceDecision"
                FOR EACH ROW EXECUTE FUNCTION immutable_absence_decision();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER immutable_absence_decision ON \"AbsenceDecision\"; DROP FUNCTION immutable_absence_decision();");
            migrationBuilder.DropTable(
                name: "AbsenceConflict");

            migrationBuilder.DropTable(
                name: "AbsenceDecision");

            migrationBuilder.DropTable(
                name: "AbsenceWeekday");

            migrationBuilder.DropTable(
                name: "AbsenceRequest");
        }
    }
}
