#!/usr/bin/env python3
"""Prepare/check the explicit legacy upgrade; never connects to or migrates a database."""
import argparse
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parent.parent
BASELINE = "20261027224058_AdditionalSchedulingConstraints"
TARGET = "20261106074419_SchedulingPriority"
PENDING = ["20261031164800_WeekendBoardNotes", "20261103080334_Phase6AssignmentModes",
           "20261103081004_Phase6Absences", "20261104135940_OperationalControls", TARGET]


def compatible_sql(original, baseline_ids):
    """Remove only the 12 reviewed backfill statements; reject any upstream drift."""
    sql = original.lstrip("\ufeff").strip()
    if re.findall(r"VALUES \('([0-9]{14}_[^']+)', '10\.0\.14'\);", sql) != PENDING:
        raise ValueError("Unexpected migration history inserts; review the source SQL")
    for suffix, semantic in [(26, "ORDINARY"), (16, "WEEKEND_ON_CALL")]:
        statement = (f'UPDATE "ShiftTypeDefinition" SET "Semantic" = \'{semantic}\', "Version" = "Version" + 1\n'
                     f'WHERE "Id" = \'40000000-0000-0000-0000-{suffix:012d}\';')
        if sql.count(statement) != 1:
            raise ValueError("Expected semantic backfill changed; review before generating")
        sql = sql.replace(statement, "")
    for column, value, suffixes in [("AssignmentMode", "ManualOnly", [12, 13, 14]),
                                     ("SchedulingPriority", "Critical", range(1, 8))]:
        for suffix in suffixes:
            statement = (f'UPDATE "ShiftTypeDefinition" SET "{column}" = \'{value}\'\n'
                         f'WHERE "Id" = \'40000000-0000-0000-0000-{suffix:012d}\';')
            if sql.count(statement) != 1:
                raise ValueError("Expected configuration backfill changed; review before generating")
            sql = sql.replace(statement, "")
    if 'UPDATE "ShiftTypeDefinition"' in sql:
        raise ValueError("Unexpected shift-definition update remains")
    for marker in ["START TRANSACTION;", "COMMIT;"]:
        if sql.count(marker) != 5:
            raise ValueError("Unexpected transaction structure")
        sql = sql.replace(marker, "")
    if len(baseline_ids) != 12 or baseline_ids[-1] != BASELINE:
        raise ValueError("Unexpected baseline migrations")
    expected = ",\n        ".join("'" + migration + "'" for migration in baseline_ids)
    header = f'''-- Explicit compatibility upgrade for the reviewed legacy 12-migration baseline.
-- Historical EF migrations/model remain unchanged: already-upgraded environments are not repaired/reseeded.
-- Preserves every existing shift field; new AssignmentMode=Automatic and SchedulingPriority=Normal.
-- Run only after approval, backup verification and writer shutdown. psql must use ON_ERROR_STOP=1.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '5min';
LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE;
LOCK TABLE "ShiftTypeDefinition" IN ACCESS EXCLUSIVE MODE;
DO $compatibility$
BEGIN
    IF (SELECT array_agg("MigrationId"::text ORDER BY "MigrationId") FROM "__EFMigrationsHistory")
       IS DISTINCT FROM ARRAY[
        {expected}
       ]::text[] THEN
        RAISE EXCEPTION 'Expected exactly the reviewed 12 migration IDs; no changes applied';
    END IF;
    IF (SELECT count(*) FROM "ShiftTypeDefinition") <> 51 THEN
        RAISE EXCEPTION 'Expected 51 existing shift definitions; no changes applied';
    END IF;
END; $compatibility$;
'''
    return header + "\n" + re.sub(r"\n{3,}", "\n\n", sql).strip() + "\n\nCOMMIT;\n"


def generate():
    env = {**os.environ, "ASPNETCORE_ENVIRONMENT": "Development",
           "ConnectionStrings__Database": "Host=127.0.0.1;Port=1;Database=offline_sql_review;Username=unused"}
    result = subprocess.run(["dotnet", "ef", "migrations", "script", BASELINE, TARGET, "--project", "src/ShiftManager",
                             "--configuration", "Release", "--no-build"], cwd=ROOT, env=env, text=True, capture_output=True)
    if result.returncode:
        raise RuntimeError("Offline EF SQL generation failed; build Release and restore the EF tool first")
    baseline = sorted(p.stem for p in (ROOT / "src/ShiftManager/Data/Migrations").glob("[0-9]*_*.cs")
                      if not p.name.endswith(".Designer.cs") and p.stem <= BASELINE)
    return compatible_sql(result.stdout, baseline)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Write the reviewed SQL artifact; default only checks it")
    args = parser.parse_args()
    prepared = generate()
    target = ROOT / "docs/local-migration-up.sql"
    if args.write:
        target.write_text(prepared)
        print("Prepared compatibility SQL; no database was contacted or modified.")
    elif target.read_text() != prepared:
        raise SystemExit("Compatibility SQL differs from the generated plan; review and regenerate with --write")
    else:
        print("Compatibility SQL matches all five historical schema migrations with only the reviewed backfills omitted.")
