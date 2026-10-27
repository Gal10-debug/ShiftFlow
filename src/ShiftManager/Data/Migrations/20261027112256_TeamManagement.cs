using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class TeamManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelegatedAvailabilityAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitialSubmission = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousValues = table.Column<string>(type: "jsonb", nullable: false),
                    NewValues = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegatedAvailabilityAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegatedAvailabilityAudit_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegatedAvailabilityAudit_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegatedAvailabilityAudit_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationalTeam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LeaderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeputyUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationalTeam", x => x.Id);
                    table.CheckConstraint("CK_Team_DistinctLeadership", "\"LeaderUserId\" IS NULL OR \"DeputyUserId\" IS NULL OR \"LeaderUserId\" <> \"DeputyUserId\"");
                    table.CheckConstraint("CK_Team_Slot", "\"Slot\" BETWEEN 1 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "TeamMembership",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembership", x => x.UserId);
                    table.UniqueConstraint("AK_TeamMembership_TeamId_UserId", x => new { x.TeamId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TeamMembership_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembership_OrganizationalTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "OrganizationalTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "OrganizationalTeam",
                columns: new[] { "Id", "DeputyUserId", "LeaderUserId", "Name", "Slot", "Version" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000001"), null, null, "צוות א׳", 1, 1L },
                    { new Guid("40000000-0000-0000-0000-000000000002"), null, null, "צוות ב׳", 2, 1L },
                    { new Guid("40000000-0000-0000-0000-000000000003"), null, null, "צוות ג׳", 3, 1L },
                    { new Guid("40000000-0000-0000-0000-000000000004"), null, null, "צוות ד׳", 4, 1L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedAvailabilityAudit_ActorUserId",
                table: "DelegatedAvailabilityAudit",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedAvailabilityAudit_SchedulePeriodId",
                table: "DelegatedAvailabilityAudit",
                column: "SchedulePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedAvailabilityAudit_TargetUserId",
                table: "DelegatedAvailabilityAudit",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedAvailabilityAudit_TeamId_OccurredAt",
                table: "DelegatedAvailabilityAudit",
                columns: new[] { "TeamId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationalTeam_Id_DeputyUserId",
                table: "OrganizationalTeam",
                columns: new[] { "Id", "DeputyUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationalTeam_Id_LeaderUserId",
                table: "OrganizationalTeam",
                columns: new[] { "Id", "LeaderUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationalTeam_Slot",
                table: "OrganizationalTeam",
                column: "Slot",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DelegatedAvailabilityAudit_OrganizationalTeam_TeamId",
                table: "DelegatedAvailabilityAudit",
                column: "TeamId",
                principalTable: "OrganizationalTeam",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationalTeam_TeamMembership_Id_DeputyUserId",
                table: "OrganizationalTeam",
                columns: new[] { "Id", "DeputyUserId" },
                principalTable: "TeamMembership",
                principalColumns: new[] { "TeamId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationalTeam_TeamMembership_Id_LeaderUserId",
                table: "OrganizationalTeam",
                columns: new[] { "Id", "LeaderUserId" },
                principalTable: "TeamMembership",
                principalColumns: new[] { "TeamId", "UserId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_hard_block() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE work_start timestamptz; work_end timestamptz;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('a' || replace(NEW."UserId"::text, '-', ''), 0));
                    IF TG_TABLE_NAME = 'ShiftAssignment' THEN
                        SELECT COALESCE(c."StartsAt", s."StartsAt"), COALESCE(c."EndsAt", s."EndsAt")
                        INTO work_start, work_end FROM "Shift" s LEFT JOIN "SchedulingCell" c ON c."Id" = NEW."SchedulingCellId"
                        WHERE s."Id" = NEW."ShiftId";
                        IF EXISTS (SELECT 1 FROM "ShiftPreference" p JOIN "Shift" s ON s."Id" = p."ShiftId"
                            WHERE p."UserId" = NEW."UserId" AND p."PreferenceType" = 'BLOCKED'
                            AND s."StartsAt" < work_end AND s."EndsAt" > work_start) THEN
                            RAISE EXCEPTION 'Hard block prevents assignment' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    ELSIF NEW."PreferenceType" = 'BLOCKED' AND NOT EXISTS (
                        SELECT 1 FROM "DelegatedAvailabilityAudit" audit
                        JOIN "Shift" target_shift ON target_shift."SchedulePeriodId" = audit."SchedulePeriodId"
                        JOIN "SchedulePeriod" period ON period."Id" = audit."SchedulePeriodId"
                        WHERE audit."Id"::text = current_setting('shiftflow.delegated_audit', true)
                        AND audit."TargetUserId" = NEW."UserId" AND target_shift."Id" = NEW."ShiftId"
                        AND period."Status" = 'DRAFT'
                    ) THEN
                        SELECT "StartsAt", "EndsAt" INTO work_start, work_end FROM "Shift" WHERE "Id" = NEW."ShiftId";
                        IF EXISTS (SELECT 1 FROM "ShiftAssignment" a JOIN "Shift" s ON s."Id" = a."ShiftId"
                            LEFT JOIN "SchedulingCell" c ON c."Id" = a."SchedulingCellId"
                            WHERE a."UserId" = NEW."UserId" AND COALESCE(c."StartsAt", s."StartsAt") < work_end
                            AND COALESCE(c."EndsAt", s."EndsAt") > work_start) THEN
                            RAISE EXCEPTION 'Existing assignment prevents hard block' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_hard_block() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE work_start timestamptz; work_end timestamptz;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('a' || replace(NEW."UserId"::text, '-', ''), 0));
                    IF TG_TABLE_NAME = 'ShiftAssignment' THEN
                        SELECT COALESCE(c."StartsAt", s."StartsAt"), COALESCE(c."EndsAt", s."EndsAt")
                        INTO work_start, work_end FROM "Shift" s LEFT JOIN "SchedulingCell" c ON c."Id" = NEW."SchedulingCellId"
                        WHERE s."Id" = NEW."ShiftId";
                        IF EXISTS (SELECT 1 FROM "ShiftPreference" p JOIN "Shift" s ON s."Id" = p."ShiftId"
                            WHERE p."UserId" = NEW."UserId" AND p."PreferenceType" = 'BLOCKED'
                            AND s."StartsAt" < work_end AND s."EndsAt" > work_start) THEN
                            RAISE EXCEPTION 'Hard block prevents assignment' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    ELSIF NEW."PreferenceType" = 'BLOCKED' THEN
                        SELECT "StartsAt", "EndsAt" INTO work_start, work_end FROM "Shift" WHERE "Id" = NEW."ShiftId";
                        IF EXISTS (SELECT 1 FROM "ShiftAssignment" a JOIN "Shift" s ON s."Id" = a."ShiftId"
                            LEFT JOIN "SchedulingCell" c ON c."Id" = a."SchedulingCellId"
                            WHERE a."UserId" = NEW."UserId" AND COALESCE(c."StartsAt", s."StartsAt") < work_end
                            AND COALESCE(c."EndsAt", s."EndsAt") > work_start) THEN
                            RAISE EXCEPTION 'Existing assignment prevents hard block' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_TeamMembership_OrganizationalTeam_TeamId",
                table: "TeamMembership");

            migrationBuilder.DropTable(
                name: "DelegatedAvailabilityAudit");

            migrationBuilder.DropTable(
                name: "OrganizationalTeam");

            migrationBuilder.DropTable(
                name: "TeamMembership");
        }
    }
}
