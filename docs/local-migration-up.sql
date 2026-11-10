START TRANSACTION;
ALTER TABLE "ShiftAssignment" ADD "Note" character varying(1000);

UPDATE "ShiftTypeDefinition" SET "Semantic" = 'ORDINARY', "Version" = "Version" + 1
WHERE "Id" = '40000000-0000-0000-0000-000000000026';
UPDATE "ShiftTypeDefinition" SET "Semantic" = 'WEEKEND_ON_CALL', "Version" = "Version" + 1
WHERE "Id" = '40000000-0000-0000-0000-000000000016';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261031164800_WeekendBoardNotes', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE "ShiftTypeDefinition" ADD "AssignmentMode" character varying(16) NOT NULL DEFAULT 'Automatic';

UPDATE "ShiftTypeDefinition" SET "AssignmentMode" = 'ManualOnly'
WHERE "Id" = '40000000-0000-0000-0000-000000000012';

UPDATE "ShiftTypeDefinition" SET "AssignmentMode" = 'ManualOnly'
WHERE "Id" = '40000000-0000-0000-0000-000000000013';

UPDATE "ShiftTypeDefinition" SET "AssignmentMode" = 'ManualOnly'
WHERE "Id" = '40000000-0000-0000-0000-000000000014';

ALTER TABLE "ShiftTypeDefinition" ADD CONSTRAINT "CK_ShiftTypeDefinition_AssignmentMode" CHECK ("AssignmentMode" IN ('Automatic','ManualOnly'));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261103080334_Phase6AssignmentModes', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE "AbsenceRequest" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Kind" character varying(16) NOT NULL,
    "Status" character varying(16) NOT NULL,
    "Reason" character varying(16) NOT NULL,
    "Explanation" character varying(2000),
    "StartDate" date NOT NULL,
    "EndDate" date,
    "Recurring" boolean NOT NULL,
    "FullDay" boolean NOT NULL,
    "StartTime" time without time zone,
    "EndTime" time without time zone,
    "ReplacesId" uuid,
    "ReplacesVersion" bigint,
    "Version" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AbsenceRequest" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Absence_Dates" CHECK ("StartDate" BETWEEN '0002-01-01' AND '9998-12-30' AND ("EndDate" IS NULL OR "EndDate" BETWEEN "StartDate" AND '9998-12-30') AND ("Recurring" OR "EndDate" IS NOT NULL)),
    CONSTRAINT "CK_Absence_Kind" CHECK (("Kind" = 'CREATE' AND "ReplacesId" IS NULL AND "ReplacesVersion" IS NULL) OR ("Kind" IN ('MODIFY','CANCEL') AND "ReplacesId" IS NOT NULL AND "ReplacesVersion" > 0)),
    CONSTRAINT "CK_Absence_Reason" CHECK ("Reason" IN ('STUDIES','RESERVE','VACATION','OTHER')),
    CONSTRAINT "CK_Absence_Status" CHECK ("Status" IN ('PENDING','APPROVED','REJECTED','CANCELLED')),
    CONSTRAINT "CK_Absence_Times" CHECK (("FullDay" AND "StartTime" IS NULL AND "EndTime" IS NULL) OR (NOT "FullDay" AND "StartTime" IS NOT NULL AND "EndTime" IS NOT NULL AND "StartTime" <> "EndTime")),
    CONSTRAINT "CK_Absence_Version" CHECK ("Version" > 0),
    CONSTRAINT "FK_AbsenceRequest_AbsenceRequest_ReplacesId" FOREIGN KEY ("ReplacesId") REFERENCES "AbsenceRequest" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AbsenceRequest_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AbsenceConflict" (
    "Id" uuid NOT NULL,
    "AbsenceRequestId" uuid NOT NULL,
    "SchedulePeriodId" uuid NOT NULL,
    "PublicationId" uuid NOT NULL,
    "AssignmentId" uuid NOT NULL,
    "Date" date NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone NOT NULL,
    "ShiftName" character varying(100) NOT NULL,
    "ResolvedAt" timestamp with time zone,
    "ResolvedByPublicationId" uuid,
    CONSTRAINT "PK_AbsenceConflict" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbsenceConflict_AbsenceRequest_AbsenceRequestId" FOREIGN KEY ("AbsenceRequestId") REFERENCES "AbsenceRequest" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AbsenceConflict_SchedulePeriod_SchedulePeriodId" FOREIGN KEY ("SchedulePeriodId") REFERENCES "SchedulePeriod" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AbsenceConflict_SchedulePublication_PublicationId" FOREIGN KEY ("PublicationId") REFERENCES "SchedulePublication" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AbsenceConflict_SchedulePublication_ResolvedByPublicationId" FOREIGN KEY ("ResolvedByPublicationId") REFERENCES "SchedulePublication" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AbsenceDecision" (
    "Id" uuid NOT NULL,
    "AbsenceRequestId" uuid NOT NULL,
    "ActorUserId" uuid NOT NULL,
    "Decision" character varying(16) NOT NULL,
    "RejectionReason" character varying(2000),
    "OccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AbsenceDecision" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AbsenceDecision_Value" CHECK ("Decision" IN ('APPROVED','REJECTED','CANCELLED') AND ("Decision" <> 'REJECTED' OR length(btrim("RejectionReason")) > 0 AND "RejectionReason" IS NOT NULL)),
    CONSTRAINT "FK_AbsenceDecision_AbsenceRequest_AbsenceRequestId" FOREIGN KEY ("AbsenceRequestId") REFERENCES "AbsenceRequest" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AbsenceDecision_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AbsenceWeekday" (
    "AbsenceRequestId" uuid NOT NULL,
    "Day" integer NOT NULL,
    CONSTRAINT "PK_AbsenceWeekday" PRIMARY KEY ("AbsenceRequestId", "Day"),
    CONSTRAINT "CK_AbsenceWeekday_Day" CHECK ("Day" BETWEEN 0 AND 6),
    CONSTRAINT "FK_AbsenceWeekday_AbsenceRequest_AbsenceRequestId" FOREIGN KEY ("AbsenceRequestId") REFERENCES "AbsenceRequest" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_AbsenceConflict_AbsenceRequestId_PublicationId_AssignmentId" ON "AbsenceConflict" ("AbsenceRequestId", "PublicationId", "AssignmentId");

CREATE INDEX "IX_AbsenceConflict_PublicationId" ON "AbsenceConflict" ("PublicationId");

CREATE INDEX "IX_AbsenceConflict_ResolvedByPublicationId" ON "AbsenceConflict" ("ResolvedByPublicationId");

CREATE INDEX "IX_AbsenceConflict_SchedulePeriodId_ResolvedAt" ON "AbsenceConflict" ("SchedulePeriodId", "ResolvedAt");

CREATE UNIQUE INDEX "IX_AbsenceDecision_AbsenceRequestId" ON "AbsenceDecision" ("AbsenceRequestId");

CREATE INDEX "IX_AbsenceDecision_ActorUserId" ON "AbsenceDecision" ("ActorUserId");

CREATE UNIQUE INDEX "IX_AbsenceRequest_ReplacesId" ON "AbsenceRequest" ("ReplacesId") WHERE "Status" = 'PENDING' AND "ReplacesId" IS NOT NULL;

CREATE INDEX "IX_AbsenceRequest_Status_CreatedAt" ON "AbsenceRequest" ("Status", "CreatedAt");

CREATE INDEX "IX_AbsenceRequest_UserId_Status_StartDate" ON "AbsenceRequest" ("UserId", "Status", "StartDate");

CREATE FUNCTION immutable_absence_decision() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Absence decisions are immutable' USING ERRCODE = '23514'; END $$;
CREATE TRIGGER immutable_absence_decision BEFORE UPDATE OR DELETE ON "AbsenceDecision"
FOR EACH ROW EXECUTE FUNCTION immutable_absence_decision();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261103081004_Phase6Absences', '10.0.14');

COMMIT;

START TRANSACTION;
CREATE TABLE "PublicationControl" (
    "PublicationId" uuid NOT NULL,
    "Hidden" boolean NOT NULL,
    "DeletedAt" timestamp with time zone,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_PublicationControl" PRIMARY KEY ("PublicationId"),
    CONSTRAINT "FK_PublicationControl_SchedulePublication_PublicationId" FOREIGN KEY ("PublicationId") REFERENCES "SchedulePublication" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ShiftExchange" (
    "Id" uuid NOT NULL,
    "SchedulePeriodId" uuid NOT NULL,
    "RequesterUserId" uuid NOT NULL,
    "ReplacementUserId" uuid NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Reason" character varying(2000),
    "Version" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ShiftExchange" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Exchange_Status" CHECK ("Status" IN ('AWAITING_REPLACEMENT','AWAITING_ADMIN','APPROVED_PENDING_PUBLICATION','APPLIED','REJECTED','CANCELLED','SUPERSEDED')),
    CONSTRAINT "FK_ShiftExchange_AspNetUsers_ReplacementUserId" FOREIGN KEY ("ReplacementUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ShiftExchange_AspNetUsers_RequesterUserId" FOREIGN KEY ("RequesterUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ShiftExchange_SchedulePeriod_SchedulePeriodId" FOREIGN KEY ("SchedulePeriodId") REFERENCES "SchedulePeriod" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ShiftExchangeAssignment" (
    "Id" uuid NOT NULL,
    "ShiftExchangeId" uuid NOT NULL,
    "PublicationId" uuid NOT NULL,
    "SourceAssignmentId" uuid NOT NULL,
    "DefinitionId" uuid NOT NULL,
    "Date" date NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone NOT NULL,
    "IsShiftLead" boolean NOT NULL,
    "Note" character varying(1000),
    "ReplacementAssignmentId" uuid,
    "AppliedPublicationId" uuid,
    CONSTRAINT "PK_ShiftExchangeAssignment" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ShiftExchangeAssignment_SchedulePublication_AppliedPublicat~" FOREIGN KEY ("AppliedPublicationId") REFERENCES "SchedulePublication" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ShiftExchangeAssignment_SchedulePublication_PublicationId" FOREIGN KEY ("PublicationId") REFERENCES "SchedulePublication" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ShiftExchangeAssignment_ShiftExchange_ShiftExchangeId" FOREIGN KEY ("ShiftExchangeId") REFERENCES "ShiftExchange" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ShiftExchangeAssignment_ShiftTypeDefinition_DefinitionId" FOREIGN KEY ("DefinitionId") REFERENCES "ShiftTypeDefinition" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ShiftExchangeReservation" (
    "AssignmentId" uuid NOT NULL,
    "ShiftExchangeId" uuid NOT NULL,
    CONSTRAINT "PK_ShiftExchangeReservation" PRIMARY KEY ("AssignmentId"),
    CONSTRAINT "FK_ShiftExchangeReservation_ShiftExchange_ShiftExchangeId" FOREIGN KEY ("ShiftExchangeId") REFERENCES "ShiftExchange" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_ShiftExchange_ReplacementUserId" ON "ShiftExchange" ("ReplacementUserId");

CREATE INDEX "IX_ShiftExchange_RequesterUserId" ON "ShiftExchange" ("RequesterUserId");

CREATE INDEX "IX_ShiftExchange_SchedulePeriodId_Status" ON "ShiftExchange" ("SchedulePeriodId", "Status");

CREATE INDEX "IX_ShiftExchangeAssignment_AppliedPublicationId" ON "ShiftExchangeAssignment" ("AppliedPublicationId");

CREATE INDEX "IX_ShiftExchangeAssignment_DefinitionId" ON "ShiftExchangeAssignment" ("DefinitionId");

CREATE INDEX "IX_ShiftExchangeAssignment_PublicationId" ON "ShiftExchangeAssignment" ("PublicationId");

CREATE UNIQUE INDEX "IX_ShiftExchangeAssignment_ShiftExchangeId_SourceAssignmentId" ON "ShiftExchangeAssignment" ("ShiftExchangeId", "SourceAssignmentId");

CREATE INDEX "IX_ShiftExchangeReservation_ShiftExchangeId" ON "ShiftExchangeReservation" ("ShiftExchangeId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261104135940_OperationalControls', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE "ShiftTypeDefinition" ADD "SchedulingPriority" character varying(16) NOT NULL DEFAULT 'Normal';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000001';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000002';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000003';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000004';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000005';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000006';

UPDATE "ShiftTypeDefinition" SET "SchedulingPriority" = 'Critical'
WHERE "Id" = '40000000-0000-0000-0000-000000000007';

ALTER TABLE "ShiftTypeDefinition" ADD CONSTRAINT "CK_ShiftTypeDefinition_SchedulingPriority" CHECK ("SchedulingPriority" IN ('Critical','High','Normal'));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261106074419_SchedulingPriority', '10.0.14');

COMMIT;
