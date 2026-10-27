using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchedulePublicationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Period_Status",
                table: "SchedulePeriod");

            migrationBuilder.AddColumn<string>(
                name: "InputFingerprint",
                table: "SchedulePublication",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "SchedulePublication",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationType",
                table: "SchedulePublication",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "LEGACY");

            migrationBuilder.AddColumn<string>(
                name: "Snapshot",
                table: "SchedulePublication",
                type: "jsonb",
                nullable: false,
                defaultValue: "null");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SchedulePeriod",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedInputs",
                table: "SchedulePeriod",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "SchedulePeriod",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ScheduleEditAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    After = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleEditAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleEditAudit_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleEditAudit_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Response = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleIssue", x => x.Id);
                    table.CheckConstraint("CK_Issue_Status", "\"Status\" IN ('OPEN','IN_REVIEW','RESOLVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_ScheduleIssue_AspNetUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleIssue_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleIssue_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleIssue_SchedulePublication_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "SchedulePublication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Period_Status",
                table: "SchedulePeriod",
                sql: "\"Status\" IN ('DRAFT','PUBLISHED','PRELIMINARY_PUBLISHED','UNDER_CORRECTION','FINAL_PUBLISHED')");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEditAudit_ActorUserId",
                table: "ScheduleEditAudit",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEditAudit_SchedulePeriodId_OccurredAt",
                table: "ScheduleEditAudit",
                columns: new[] { "SchedulePeriodId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleIssue_PublicationId_AssignmentId_ReporterUserId",
                table: "ScheduleIssue",
                columns: new[] { "PublicationId", "AssignmentId", "ReporterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleIssue_ReporterUserId",
                table: "ScheduleIssue",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleIssue_ReviewedByUserId",
                table: "ScheduleIssue",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleIssue_SchedulePeriodId",
                table: "ScheduleIssue",
                column: "SchedulePeriodId");
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
                        AND period."Status" IN ('DRAFT','PRELIMINARY_PUBLISHED','UNDER_CORRECTION')
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
                CREATE FUNCTION protect_schedule_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Schedule history is immutable' USING ERRCODE = '23514', CONSTRAINT = 'CK_ImmutableScheduleHistory'; END $$;
                CREATE TRIGGER "TR_Publication_Immutable" BEFORE UPDATE OR DELETE ON "SchedulePublication"
                    FOR EACH ROW EXECUTE FUNCTION protect_schedule_history();
                CREATE TRIGGER "TR_EditAudit_Immutable" BEFORE UPDATE OR DELETE ON "ScheduleEditAudit"
                    FOR EACH ROW EXECUTE FUNCTION protect_schedule_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Publication_Immutable" ON "SchedulePublication";
                DROP TRIGGER "TR_EditAudit_Immutable" ON "ScheduleEditAudit";
                DROP FUNCTION protect_schedule_history();
                UPDATE "SchedulePeriod" SET "Status" = 'PUBLISHED'
                WHERE "Status" IN ('PRELIMINARY_PUBLISHED','UNDER_CORRECTION','FINAL_PUBLISHED');
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
            migrationBuilder.DropTable(
                name: "ScheduleEditAudit");

            migrationBuilder.DropTable(
                name: "ScheduleIssue");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Period_Status",
                table: "SchedulePeriod");

            migrationBuilder.DropColumn(
                name: "InputFingerprint",
                table: "SchedulePublication");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "SchedulePublication");

            migrationBuilder.DropColumn(
                name: "PublicationType",
                table: "SchedulePublication");

            migrationBuilder.DropColumn(
                name: "Snapshot",
                table: "SchedulePublication");

            migrationBuilder.DropColumn(
                name: "ReviewedInputs",
                table: "SchedulePeriod");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SchedulePeriod");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SchedulePeriod",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Period_Status",
                table: "SchedulePeriod",
                sql: "\"Status\" IN ('DRAFT','PUBLISHED')");
        }
    }
}
