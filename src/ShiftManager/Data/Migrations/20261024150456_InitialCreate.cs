using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ShiftManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.CheckConstraint("CK_User_FirstName", "btrim(\"FirstName\") <> ''");
                    table.CheckConstraint("CK_User_LastName", "btrim(\"LastName\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "SchedulePeriod",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePeriod", x => x.Id);
                    table.CheckConstraint("CK_Period_Month", "\"Month\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_Period_Status", "\"Status\" IN ('DRAFT','PUBLISHED')");
                    table.CheckConstraint("CK_Period_Year", "\"Year\" BETWEEN 1 AND 9999");
                });

            migrationBuilder.CreateTable(
                name: "TaskTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplate", x => x.Id);
                    table.CheckConstraint("CK_Template_Name", "btrim(\"Name\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilityWindow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpensAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityWindow", x => x.Id);
                    table.CheckConstraint("CK_Window_Status", "\"Status\" IN ('OPEN','CLOSED')");
                    table.CheckConstraint("CK_Window_Time", "\"ClosesAt\" IS NULL OR \"ClosesAt\" >= \"OpensAt\"");
                    table.ForeignKey(
                        name: "FK_AvailabilityWindow_AspNetUsers_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvailabilityWindow_AspNetUsers_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvailabilityWindow_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchedulePublication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePublication", x => x.Id);
                    table.CheckConstraint("CK_Publication_Version", "\"VersionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_SchedulePublication_AspNetUsers_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePublication_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shift",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchedulePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CoordinatorUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shift", x => x.Id);
                    table.CheckConstraint("CK_Shift_Time", "\"EndsAt\" > \"StartsAt\"");
                    table.CheckConstraint("CK_Shift_Type", "\"ShiftType\" IN ('DAY','NIGHT')");
                    table.ForeignKey(
                        name: "FK_Shift_AspNetUsers_CoordinatorUserId",
                        column: x => x.CoordinatorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shift_SchedulePeriod_SchedulePeriodId",
                        column: x => x.SchedulePeriodId,
                        principalTable: "SchedulePeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsShiftLead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftAssignment_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftAssignment_Shift_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftPreference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPreference", x => x.Id);
                    table.CheckConstraint("CK_Preference_Type", "\"PreferenceType\" IN ('BLOCKED','PREFERRED')");
                    table.ForeignKey(
                        name: "FK_ShiftPreference_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftPreference_Shift_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTask", x => x.Id);
                    table.CheckConstraint("CK_Task_Title", "btrim(\"Title\") <> ''");
                    table.ForeignKey(
                        name: "FK_ShiftTask_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTask_Shift_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTask_TaskTemplate_TaskTemplateId",
                        column: x => x.TaskTemplateId,
                        principalTable: "TaskTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignment", x => x.Id);
                    table.CheckConstraint("CK_TaskAssignment_Time", "\"EndsAt\" > \"StartsAt\"");
                    table.ForeignKey(
                        name: "FK_TaskAssignment_ShiftAssignment_ShiftAssignmentId",
                        column: x => x.ShiftAssignmentId,
                        principalTable: "ShiftAssignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAssignment_ShiftTask_ShiftTaskId",
                        column: x => x.ShiftTaskId,
                        principalTable: "ShiftTask",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "f195d2c6-5bee-4348-89db-9a81bc73c9f1", "ADMIN", "ADMIN" },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "2ca2b340-2360-4d92-8593-f7e4d75f41fa", "COORDINATOR", "COORDINATOR" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "5c1049e7-1179-4902-8086-f1eef67fc700", "EMPLOYEE", "EMPLOYEE" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityWindow_ClosedByUserId",
                table: "AvailabilityWindow",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityWindow_OpenedByUserId",
                table: "AvailabilityWindow",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityWindow_SchedulePeriodId",
                table: "AvailabilityWindow",
                column: "SchedulePeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePeriod_Year_Month",
                table: "SchedulePeriod",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePublication_PublishedByUserId",
                table: "SchedulePublication",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePublication_SchedulePeriodId_VersionNumber",
                table: "SchedulePublication",
                columns: new[] { "SchedulePeriodId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shift_CoordinatorUserId",
                table: "Shift",
                column: "CoordinatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Shift_SchedulePeriodId",
                table: "Shift",
                column: "SchedulePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Shift_ShiftDate_ShiftType",
                table: "Shift",
                columns: new[] { "ShiftDate", "ShiftType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_ShiftId",
                table: "ShiftAssignment",
                column: "ShiftId",
                unique: true,
                filter: "\"IsShiftLead\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_ShiftId_UserId",
                table: "ShiftAssignment",
                columns: new[] { "ShiftId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignment_UserId",
                table: "ShiftAssignment",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPreference_ShiftId",
                table: "ShiftPreference",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPreference_UserId_ShiftId",
                table: "ShiftPreference",
                columns: new[] { "UserId", "ShiftId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTask_CreatedByUserId",
                table: "ShiftTask",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTask_ShiftId",
                table: "ShiftTask",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTask_TaskTemplateId",
                table: "ShiftTask",
                column: "TaskTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignment_ShiftAssignmentId",
                table: "TaskAssignment",
                column: "ShiftAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignment_ShiftTaskId",
                table: "TaskAssignment",
                column: "ShiftTaskId");

            // Cross-table task invariants cannot be expressed as PostgreSQL CHECK constraints.
            migrationBuilder.Sql("""
                CREATE FUNCTION validate_task_assignment() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent record;
                BEGIN
                    SELECT t."ShiftId" AS task_shift, a."ShiftId" AS employee_shift,
                           s."StartsAt" AS shift_start, s."EndsAt" AS shift_end
                    INTO parent
                    FROM "ShiftTask" t
                    JOIN "Shift" s ON s."Id" = t."ShiftId"
                    CROSS JOIN "ShiftAssignment" a
                    WHERE t."Id" = NEW."ShiftTaskId" AND a."Id" = NEW."ShiftAssignmentId"
                    FOR SHARE OF t, s, a;
                    IF NOT FOUND THEN
                        RETURN NEW; -- Foreign keys produce the appropriate missing-parent error.
                    END IF;
                    IF parent.task_shift <> parent.employee_shift
                       OR NEW."StartsAt" < parent.shift_start OR NEW."EndsAt" > parent.shift_end THEN
                        RAISE EXCEPTION 'Task assignment must belong to and fit inside its shift'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_TaskAssignment_Shift';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER "TR_TaskAssignment_Validate"
                BEFORE INSERT OR UPDATE ON "TaskAssignment"
                FOR EACH ROW EXECUTE FUNCTION validate_task_assignment();

                CREATE FUNCTION guard_task_assignment_parents() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE has_tasks boolean;
                BEGIN
                    IF TG_TABLE_NAME = 'Shift' THEN
                        IF NEW."StartsAt" IS NOT DISTINCT FROM OLD."StartsAt"
                           AND NEW."EndsAt" IS NOT DISTINCT FROM OLD."EndsAt" THEN RETURN NEW; END IF;
                        SELECT EXISTS(SELECT 1 FROM "TaskAssignment" ta
                            JOIN "ShiftTask" t ON t."Id" = ta."ShiftTaskId"
                            WHERE t."ShiftId" = OLD."Id") INTO has_tasks;
                    ELSIF TG_TABLE_NAME = 'ShiftTask' THEN
                        IF NEW."ShiftId" = OLD."ShiftId" THEN RETURN NEW; END IF;
                        SELECT EXISTS(SELECT 1 FROM "TaskAssignment" WHERE "ShiftTaskId" = OLD."Id") INTO has_tasks;
                    ELSE
                        IF NEW."ShiftId" = OLD."ShiftId" THEN RETURN NEW; END IF;
                        SELECT EXISTS(SELECT 1 FROM "TaskAssignment" WHERE "ShiftAssignmentId" = OLD."Id") INTO has_tasks;
                    END IF;
                    IF has_tasks THEN
                        RAISE EXCEPTION 'Remove affected task assignments before changing their parent shift'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_TaskAssignment_ParentChange';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER "TR_Shift_TaskGuard" BEFORE UPDATE OF "StartsAt", "EndsAt" ON "Shift"
                FOR EACH ROW EXECUTE FUNCTION guard_task_assignment_parents();
                CREATE TRIGGER "TR_ShiftTask_TaskGuard" BEFORE UPDATE OF "ShiftId" ON "ShiftTask"
                FOR EACH ROW EXECUTE FUNCTION guard_task_assignment_parents();
                CREATE TRIGGER "TR_ShiftAssignment_TaskGuard" BEFORE UPDATE OF "ShiftId" ON "ShiftAssignment"
                FOR EACH ROW EXECUTE FUNCTION guard_task_assignment_parents();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_TaskAssignment_Validate" ON "TaskAssignment";
                DROP TRIGGER "TR_Shift_TaskGuard" ON "Shift";
                DROP TRIGGER "TR_ShiftTask_TaskGuard" ON "ShiftTask";
                DROP TRIGGER "TR_ShiftAssignment_TaskGuard" ON "ShiftAssignment";
                DROP FUNCTION validate_task_assignment();
                DROP FUNCTION guard_task_assignment_parents();
                """);

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AvailabilityWindow");

            migrationBuilder.DropTable(
                name: "SchedulePublication");

            migrationBuilder.DropTable(
                name: "ShiftPreference");

            migrationBuilder.DropTable(
                name: "TaskAssignment");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ShiftAssignment");

            migrationBuilder.DropTable(
                name: "ShiftTask");

            migrationBuilder.DropTable(
                name: "Shift");

            migrationBuilder.DropTable(
                name: "TaskTemplate");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "SchedulePeriod");
        }
    }
}
