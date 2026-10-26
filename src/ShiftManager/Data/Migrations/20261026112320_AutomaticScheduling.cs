using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GeneratedByRunId",
                table: "ShiftAssignment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "ShiftAssignment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SchedulingCellId",
                table: "ShiftAssignment",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchedulingRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SavedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SolverStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProvenObjectives = table.Column<int>(type: "integer", nullable: false),
                    Required = table.Column<int>(type: "integer", nullable: false),
                    Filled = table.Column<int>(type: "integer", nullable: false),
                    Missing = table.Column<int>(type: "integer", nullable: false),
                    UnderstaffedCells = table.Column<int>(type: "integer", nullable: false),
                    FullyStaffedCells = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingRun", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulingRun_AspNetUsers_SavedByUserId",
                        column: x => x.SavedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchedulingCell",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftTypeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequiredEmployees = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    SchedulingRunId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingCell", x => x.Id);
                    table.CheckConstraint("CK_Cell_Required", "\"RequiredEmployees\" BETWEEN 0 AND 1000");
                    table.CheckConstraint("CK_Cell_Time", "\"EndsAt\" > \"StartsAt\"");
                    table.ForeignKey(
                        name: "FK_SchedulingCell_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulingCell_SchedulingRun_SchedulingRunId",
                        column: x => x.SchedulingRunId,
                        principalTable: "SchedulingRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulingCell_ShiftTypeDefinition_ShiftTypeDefinitionId",
                        column: x => x.ShiftTypeDefinitionId,
                        principalTable: "ShiftTypeDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_GeneratedByRunId",
                table: "ShiftAssignment",
                column: "GeneratedByRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_SchedulingCellId_UserId",
                table: "ShiftAssignment",
                columns: new[] { "SchedulingCellId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingCell_SchedulePeriodId",
                table: "SchedulingCell",
                column: "SchedulePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingCell_SchedulingRunId",
                table: "SchedulingCell",
                column: "SchedulingRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingCell_ShiftTypeDefinitionId_Date",
                table: "SchedulingCell",
                columns: new[] { "ShiftTypeDefinitionId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingRun_SavedByUserId",
                table: "SchedulingRun",
                column: "SavedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignment_SchedulingCell_SchedulingCellId",
                table: "ShiftAssignment",
                column: "SchedulingCellId",
                principalTable: "SchedulingCell",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignment_SchedulingRun_GeneratedByRunId",
                table: "ShiftAssignment",
                column: "GeneratedByRunId",
                principalTable: "SchedulingRun",
                principalColumn: "Id",
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
                DROP TRIGGER "TR_Assignment_HardBlock" ON "ShiftAssignment";
                CREATE TRIGGER "TR_Assignment_HardBlock" BEFORE INSERT OR UPDATE OF "UserId", "ShiftId", "SchedulingCellId"
                    ON "ShiftAssignment" FOR EACH ROW EXECUTE FUNCTION enforce_hard_block();
                CREATE FUNCTION guard_cell_hard_blocks() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE member record;
                BEGIN
                    FOR member IN SELECT "UserId" FROM "ShiftAssignment" WHERE "SchedulingCellId" = NEW."Id" ORDER BY "UserId" LOOP
                        PERFORM pg_advisory_xact_lock(hashtextextended('a' || replace(member."UserId"::text, '-', ''), 0));
                        IF EXISTS (SELECT 1 FROM "ShiftPreference" p JOIN "Shift" s ON s."Id" = p."ShiftId"
                            WHERE p."UserId" = member."UserId" AND p."PreferenceType" = 'BLOCKED'
                            AND s."StartsAt" < NEW."EndsAt" AND s."EndsAt" > NEW."StartsAt") THEN
                            RAISE EXCEPTION 'Hard block prevents cell change' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    END LOOP;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_Cell_HardBlock" BEFORE UPDATE OF "StartsAt", "EndsAt" ON "SchedulingCell"
                    FOR EACH ROW EXECUTE FUNCTION guard_cell_hard_blocks();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Cell_HardBlock" ON "SchedulingCell";
                DROP FUNCTION guard_cell_hard_blocks();
                DROP TRIGGER "TR_Assignment_HardBlock" ON "ShiftAssignment";
                CREATE TRIGGER "TR_Assignment_HardBlock" BEFORE INSERT OR UPDATE OF "UserId", "ShiftId"
                    ON "ShiftAssignment" FOR EACH ROW EXECUTE FUNCTION enforce_hard_block();
                CREATE OR REPLACE FUNCTION enforce_hard_block() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('a' || replace(NEW."UserId"::text, '-', ''), 0));
                    IF TG_TABLE_NAME = 'ShiftAssignment' THEN
                        IF EXISTS (SELECT 1 FROM "ShiftPreference" WHERE "UserId" = NEW."UserId"
                            AND "ShiftId" = NEW."ShiftId" AND "PreferenceType" = 'BLOCKED') THEN
                            RAISE EXCEPTION 'Hard block prevents assignment' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    ELSIF NEW."PreferenceType" = 'BLOCKED' THEN
                        IF EXISTS (SELECT 1 FROM "ShiftAssignment" WHERE "UserId" = NEW."UserId" AND "ShiftId" = NEW."ShiftId") THEN
                            RAISE EXCEPTION 'Existing assignment prevents hard block' USING ERRCODE = '23514', CONSTRAINT = 'CK_HardBlock';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignment_SchedulingCell_SchedulingCellId",
                table: "ShiftAssignment");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignment_SchedulingRun_GeneratedByRunId",
                table: "ShiftAssignment");

            migrationBuilder.DropTable(
                name: "SchedulingCell");

            migrationBuilder.DropTable(
                name: "SchedulingRun");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignment_GeneratedByRunId",
                table: "ShiftAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignment_SchedulingCellId_UserId",
                table: "ShiftAssignment");

            migrationBuilder.DropColumn(
                name: "GeneratedByRunId",
                table: "ShiftAssignment");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "ShiftAssignment");

            migrationBuilder.DropColumn(
                name: "SchedulingCellId",
                table: "ShiftAssignment");
        }
    }
}
