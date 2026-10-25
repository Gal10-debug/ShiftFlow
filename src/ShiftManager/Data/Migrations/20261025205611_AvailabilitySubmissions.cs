using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AvailabilitySubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvailabilitySubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilitySubmission", x => x.Id);
                    table.CheckConstraint("CK_Submission_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AvailabilitySubmission_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvailabilitySubmission_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilitySubmission_SchedulePeriodId",
                table: "AvailabilitySubmission",
                column: "SchedulePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilitySubmission_UserId_SchedulePeriodId",
                table: "AvailabilitySubmission",
                columns: new[] { "UserId", "SchedulePeriodId" },
                unique: true);

            // Phase 2 had no submission API. Preserve any manually entered legacy preferences
            // as submitted data; all-Available historical submissions cannot be inferred.
            migrationBuilder.Sql("""
                INSERT INTO "AvailabilitySubmission" ("Id", "UserId", "SchedulePeriodId", "LastSubmittedAt", "Version")
                SELECT gen_random_uuid(), p."UserId", s."SchedulePeriodId", MAX(p."UpdatedAt"), 1
                FROM "ShiftPreference" p JOIN "Shift" s ON s."Id" = p."ShiftId"
                GROUP BY p."UserId", s."SchedulePeriodId";

                CREATE FUNCTION enforce_hard_block() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER "TR_Assignment_HardBlock" BEFORE INSERT OR UPDATE OF "UserId", "ShiftId"
                    ON "ShiftAssignment" FOR EACH ROW EXECUTE FUNCTION enforce_hard_block();
                CREATE TRIGGER "TR_Preference_HardBlock" BEFORE INSERT OR UPDATE OF "UserId", "ShiftId", "PreferenceType"
                    ON "ShiftPreference" FOR EACH ROW EXECUTE FUNCTION enforce_hard_block();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Assignment_HardBlock" ON "ShiftAssignment";
                DROP TRIGGER "TR_Preference_HardBlock" ON "ShiftPreference";
                DROP FUNCTION enforce_hard_block();
                """);
            migrationBuilder.DropTable(
                name: "AvailabilitySubmission");
        }
    }
}
