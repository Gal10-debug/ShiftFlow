#!/usr/bin/env python3
"""Rehearse the compatible upgrade from a verified backup; never starts or writes the source."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
from datetime import datetime, timezone
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("local_docker", ROOT / "scripts/local-docker.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
BASELINE = "20261027224058_AdditionalSchedulingConstraints"
TARGET = "20261106074419_SchedulingPriority"
PENDING = ["20261031164800_WeekendBoardNotes", "20261103080334_Phase6AssignmentModes",
           "20261103081004_Phase6Absences", "20261104135940_OperationalControls", TARGET]
NEW_TABLES = {"AbsenceRequest", "AbsenceConflict", "AbsenceDecision", "AbsenceWeekday",
              "PublicationControl", "ShiftExchange", "ShiftExchangeAssignment", "ShiftExchangeReservation"}
ADDED = {"ShiftAssignment": ["Note"], "ShiftTypeDefinition": ["AssignmentMode", "SchedulingPriority"]}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def quote(identifier):
    return '"' + identifier.replace('"', '""') + '"'


def fingerprint_query(table, excluded=()):
    value = "to_jsonb(t)"
    if excluded:
        value += " - ARRAY[" + ",".join("'" + c.replace("'", "''") + "'" for c in excluded) + "]"
    return f"""SELECT json_build_object('count',count(*), 'sha256',
      encode(sha256(convert_to(coalesce(string_agg(row::text, E'\\n' ORDER BY row::text COLLATE "C"),''),'UTF8')),'hex'))
      FROM (SELECT {value} AS row FROM public.{quote(table)} t) rows"""


def compare(before, after):
    require(set(after) - set(before) == NEW_TABLES, "Unexpected new tables")
    require(not set(before) - set(after), "An existing table disappeared")
    for table, original in before.items():
        if table == "__EFMigrationsHistory":
            require(after[table]["count"] == original["count"] + 5, "Migration history count mismatch")
        else:
            require(after[table] == original, f"Unexpected record change in {table}")
    require(all(after[table]["count"] == 0 for table in NEW_TABLES), "New tables must be empty; no seed data is permitted")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backup", type=Path, default=ROOT / ".local-docker-private/20261110T191529Z-c448e80200b347f9991c31d1f3a70ff7",
                        help="Verified pre-migration backup directory (never overwritten)")
    args = parser.parse_args()
    os.umask(0o077)
    backup_directory = args.backup.resolve()
    manifest = json.loads((backup_directory / "cold-backup.json").read_text())
    backup_report = json.loads((backup_directory / "validation.json").read_text())
    source = guard.inspect("container", guard.CONTAINER)
    require(source["State"]["Status"] == "exited" and not source["State"]["Running"],
            "Source is not stopped. No source service will be started/stopped by this tool; arrange a reviewed consistent backup.")
    require(source["Image"] == manifest["postgres_image"]
            and source["State"]["StartedAt"] == manifest["source_started_at"]
            and source["State"]["FinishedAt"] == manifest["source_finished_at"], "Source changed since the verified backup")
    env = {**os.environ}
    env.pop("POSTGRES_PASSWORD", None)
    base = ["docker", "compose", "--project-directory", str(ROOT), "--project-name", "shiftflow",
            "--env-file", str(ROOT / ".env"), "-f", str(ROOT / "compose.yaml")]
    db_env = json.loads(guard.run(base + ["config", "--format", "json"], env=env))["services"]["postgres"]["environment"]
    consumers = guard.run(["docker", "ps", "-a", "--filter", "volume=" + guard.VOLUME, "--format", "{{.Names}}"]).splitlines()
    guard.verify_metadata(source, guard.inspect("volume", guard.VOLUME), consumers, guard.inspect("network", guard.NETWORK), db_env)
    run_id = uuid.uuid4().hex
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    private_root = ROOT / ".local-docker-private"
    private_root.mkdir(mode=0o700, exist_ok=True)
    require(not private_root.is_symlink() and private_root.stat().st_mode & 0o077 == 0, "Backup root must be a private directory (0700)")
    directory = private_root / (timestamp + "-" + run_id)
    directory.mkdir(mode=0o700)
    print("Restricted validation directory: " + str(directory), flush=True)

    def save(name, value):
        path = directory / name
        with path.open("x") as output:
            output.write(json.dumps(value, indent=2) + "\n")
        return path

    def command(argv, *, data=None, command_env=None, binary=False):
        result = subprocess.run(argv, cwd=ROOT, env=command_env or env, input=data,
                                text=not binary, capture_output=True)
        if result.returncode:
            # Failure diagnostics can include record values: retain only in the private directory.
            with (directory / ("failure-" + uuid.uuid4().hex + ".log")).open("xb") as log:
                log.write(result.stderr if binary else result.stderr.encode())
            raise RuntimeError("Command failed; inspect the restricted failure log. No raw database output was printed.")
        return result.stdout

    readonly = ["docker", "run", "--rm", "--network", "none", "--read-only", "--entrypoint", "sh",
                "--mount", f"type=volume,src={guard.VOLUME},dst=/source,readonly", source["Image"], "-ec"]
    clean = "test \"$(cat /source/PG_VERSION)\" = 17; test ! -e /source/postmaster.pid; pg_controldata /source | grep -q 'Database cluster state: *shut down$'"
    command(readonly + [clean])
    archive = backup_directory / "cluster.tar"
    with tarfile.open(archive) as backup:
        members = {m.name.removeprefix("./"): m for m in backup}
        require("global/pg_control" in members and "PG_VERSION" in members, "Cold backup lacks cluster control data")
        require(backup.extractfile(members["PG_VERSION"]).read().strip() == b"17", "Unexpected backup PostgreSQL version")
        require(not any(m.issym() or m.islnk() for m in members.values()), "External tablespace/WAL links require a separately reviewed backup")
    def sha(path):
        with path.open("rb") as input_file:
            return hashlib.file_digest(input_file, "sha256").hexdigest()
    cold_sha = sha(archive)
    logical = backup_directory / "shiftmanager.dump"
    require(cold_sha == manifest["cluster_tar_sha256"] == backup_report["cluster_tar_sha256"]
            and sha(logical) == backup_report["logical_dump_sha256"], "Existing backup checksum mismatch")
    print("Existing cold/logical backups verified; neither backup nor persistent container was changed.", flush=True)
    project = "shiftflow-migration-test-" + run_id
    keys = directory / "test-keys"
    keys.mkdir(mode=0o777)
    keys.chmod(0o777)  # Parent remains 0700; synthetic test-only protection keys.
    env.update(POSTGRES_PASSWORD=db_env["POSTGRES_PASSWORD"], LOCAL_POSTGRES_IMAGE=source["Image"],
               LOCAL_DATABASE_CONNECTION=guard.connection_string(db_env), LOCAL_BACKUP=str(archive), LOCAL_TEST_KEYS=str(keys))
    override = directory / "compose.test.yaml"
    override.write_text('''services:
  postgres:
    ports: !override ["127.0.0.1::5432"]
    volumes: !override
      - type: bind
        source: ${LOCAL_BACKUP}
        target: /backup/cluster.tar
        read_only: true
    tmpfs: ["/var/lib/postgresql/data:rw,size=1024m"]
    entrypoint: [sh, -ec]
    command: ["test ! -e /var/lib/postgresql/data/PG_VERSION; tar -xf /backup/cluster.tar -C /var/lib/postgresql/data; exec /usr/local/bin/docker-entrypoint.sh postgres"]
  app:
    ports: !override ["127.0.0.1::8080"]
    volumes: !override
      - type: bind
        source: ${LOCAL_TEST_KEYS}
        target: /var/lib/shiftflow/keys
volumes:
  postgres-data: !reset {}
  local-keys: !reset {}
''')
    compose = ["docker", "compose", "--project-directory", str(ROOT), "--project-name", project,
               "--env-file", "/dev/null", "-f", str(ROOT / "compose.yaml"), "-f", str(ROOT / "compose.local.yaml"), "-f", str(override)]
    config = json.loads(command(compose + ["config", "--format", "json"]))
    require(config["name"] == project, "Disposable project mismatch")
    for service_name, target, mount_source in [("postgres", "/backup/cluster.tar", archive), ("app", "/var/lib/shiftflow/keys", keys)]:
        mounts = config["services"][service_name]["volumes"]
        require(len(mounts) == 1 and mounts[0]["type"] == "bind" and mounts[0]["source"] == str(mount_source)
                and mounts[0]["target"] == target, "Only backup and private test-key bind mounts may be used")
    require(config["services"]["postgres"]["volumes"][0]["read_only"], "Backup mount must be read-only")
    try:
        command(compose + ["up", "-d", "--wait", "--wait-timeout", "90", "postgres"])
        postgres = command(compose + ["ps", "-q", "postgres"]).strip()
        require(postgres, "Disposable PostgreSQL was not created")
        def sql(text, database="shiftmanager"):
            return command(["docker", "exec", "-i", postgres, "psql", "-X", "-qAt", "-U", "shiftmanager", "-d", database,
                            "-v", "ON_ERROR_STOP=1"], data="BEGIN READ ONLY;\n" + text + ";\nROLLBACK;\n").strip()
        catalog = json.loads(sql(guard.SQL.removeprefix("BEGIN READ ONLY;").removesuffix("ROLLBACK;")))
        expected = {p.stem for p in (ROOT / "src/ShiftManager/Data/Migrations").glob("[0-9]*_*.cs") if not p.name.endswith(".Designer.cs")}
        require(guard.verify_catalog(catalog, expected) == PENDING, "Source pending migrations differ from the five audited migrations")
        require(catalog["migrations"][-1] == BASELINE, "Unexpected source baseline")
        tables = json.loads(sql("SELECT json_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname='public'"))
        before = {table: json.loads(sql(fingerprint_query(table))) for table in tables}
        before_definitions = json.loads(sql('SELECT json_agg(to_jsonb(t) ORDER BY "Slot") FROM "ShiftTypeDefinition" t'))
        save("before.json", before)
        # Restore-test the existing logical backup; never overwrite previous backup artifacts.
        toc = command(["docker", "exec", "-i", postgres, "pg_restore", "--list"], data=logical.read_bytes(), binary=True)
        require(b"TABLE DATA public AspNetUsers" in toc, "Logical backup has no account data entry")
        command(["docker", "exec", postgres, "createdb", "-U", "shiftmanager", "-T", "template0", "backup_restore_check"])
        command(["docker", "exec", "-i", postgres, "pg_restore", "--exit-on-error", "-U", "shiftmanager", "-d", "backup_restore_check"],
                data=logical.read_bytes(), binary=True)
        restored = {table: json.loads(sql(fingerprint_query(table), "backup_restore_check")) for table in tables}
        require(restored == before, "Logical restore differs from cold-backup baseline")
        print("Cold and logical backups restored successfully; all original table fingerprints match.", flush=True)
        port = command(["docker", "port", postgres, "5432/tcp"]).strip()
        require(re.fullmatch(r"127\.0\.0\.1:\d+", port) and port not in ("127.0.0.1:5432", "127.0.0.1:55432"), "Unsafe test port")
        connection = guard.connection_string(db_env).replace("Host=postgres;Port=5432;", "Host=127.0.0.1;Port=" + port.split(":")[1] + ";")
        migration_env = {**env, "ASPNETCORE_ENVIRONMENT": "Development", "ConnectionStrings__Database": connection}
        command(["python3", "-B", "scripts/prepare-local-migrations.py"])
        plan = (ROOT / "docs/local-migration-up.sql").read_text()
        output = command(["docker", "exec", "-i", postgres, "psql", "-X", "-U", "shiftmanager", "-d", "shiftmanager",
                          "-v", "ON_ERROR_STOP=1"], data=plan)
        (directory / "migration.log").write_text(output)
        after_tables = json.loads(sql("SELECT json_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname='public'"))
        after = {table: json.loads(sql(fingerprint_query(table, ADDED.get(table, ())))) for table in after_tables}
        compare(before, after)
        after_definitions = json.loads(sql('SELECT json_agg(to_jsonb(t) ORDER BY "Slot") FROM "ShiftTypeDefinition" t'))
        require(len(before_definitions) == len(after_definitions) == 51, "Expected all 51 shift definitions")
        for old, new in zip(before_definitions, after_definitions):
            require(all(new[key] == value for key, value in old.items()), "An existing shift field changed")
            require(new["AssignmentMode"] == "Automatic" and new["SchedulingPriority"] == "Normal", "Unsafe new configuration defaults")
        save("shift-definitions.json", {"before": before_definitions, "after": after_definitions})
        after_history = json.loads(sql('SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM "__EFMigrationsHistory"'))
        require(after_history == catalog["migrations"] + PENDING, "Migration history was unexpectedly rewritten")
        checks = json.loads(sql('''SELECT json_build_object(
          'invalid_constraints',(SELECT count(*) FROM pg_constraint WHERE connamespace='public'::regnamespace AND NOT convalidated),
          'invalid_indexes',(SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid
            WHERE c.relnamespace='public'::regnamespace AND NOT (i.indisvalid AND i.indisready AND i.indislive)),
          'non_null_added_notes',(SELECT count(*) FROM "ShiftAssignment" WHERE "Note" IS NOT NULL),
          'wrong_assignment_modes',(SELECT count(*) FROM "ShiftTypeDefinition" WHERE "AssignmentMode" <> 'Automatic'),
          'wrong_priorities',(SELECT count(*) FROM "ShiftTypeDefinition" WHERE "SchedulingPriority" <> 'Normal'))'''))
        require(not any(checks.values()), "Schema or backfill integrity check failed")
        # Check every actual FK for orphan rows, including composite keys and nullable references.
        orphan_queries = sql('''SELECT format('SELECT count(*) FROM %s c WHERE %s AND NOT EXISTS (SELECT 1 FROM %s p WHERE %s)',
          conrelid::regclass, string_agg(format('c.%I IS NOT NULL',ca.attname),' AND ' ORDER BY cols.ord), confrelid::regclass,
          string_agg(format('c.%I = p.%I',ca.attname,pa.attname),' AND ' ORDER BY cols.ord))
          FROM pg_constraint k CROSS JOIN LATERAL unnest(k.conkey,k.confkey) WITH ORDINALITY cols(child,parent,ord)
          JOIN pg_attribute ca ON ca.attrelid=k.conrelid AND ca.attnum=cols.child
          JOIN pg_attribute pa ON pa.attrelid=k.confrelid AND pa.attnum=cols.parent
          WHERE k.contype='f' AND k.connamespace='public'::regnamespace GROUP BY k.oid,k.conrelid,k.confrelid''').splitlines()
        require(all(sql(query) == "0" for query in orphan_queries), "Foreign key orphan found")
        save("after.json", after)
        # EF must recognize the schema/history without replaying the original backfills.
        command(["dotnet", "ef", "database", "update", TARGET, "--project", "src/ShiftManager", "--configuration", "Release", "--no-build"], command_env=migration_env)
        command(["dotnet", "ef", "migrations", "has-pending-model-changes", "--project", "src/ShiftManager", "--configuration", "Release", "--no-build"], command_env=migration_env)
        require(json.loads(sql('SELECT json_agg(to_jsonb(t) ORDER BY "Slot") FROM "ShiftTypeDefinition" t')) == after_definitions,
                "EF replay changed the preserved configuration")
        print("Compatible upgrade passed: all 51 definitions unchanged, safe defaults, EF/model consistency, FKs and indexes.", flush=True)
        command(compose + ["up", "-d", "--no-build", "--no-deps", "--wait", "--wait-timeout", "90", "app"])
        app = command(compose + ["ps", "-q", "app"]).strip()
        app_port = command(["docker", "port", app, "8080/tcp"]).strip()
        require(re.fullmatch(r"127\.0\.0\.1:\d+", app_port), "Unsafe app port")
        origin = "http://" + app_port
        for path in ["/", "/scheduling", "/health/live", "/health/ready", "/api/auth/csrf"]:
            with urllib.request.urlopen(origin + path, timeout=10) as response:
                content = response.read().decode()
                require(response.status == 200, "Copied-database application startup failed")
                if path in ("/", "/scheduling"):
                    require('dir="rtl"' in content and '<script type="module"' in content, "React/RTL routing failed")
        try:
            urllib.request.urlopen(origin + "/api/auth/me", timeout=10)
            raise RuntimeError("Unauthenticated access was permitted")
        except urllib.error.HTTPError as error:
            require(error.code == 401, "Unexpected authentication response")
        after_start = {table: json.loads(sql(fingerprint_query(table, ADDED.get(table, ())))) for table in after_tables}
        require(after_start == after, "Application startup unexpectedly wrote application records")
        # Separate fresh per-test databases; no fixtures write to the restored real-record copy.
        command(["docker", "exec", postgres, "createdb", "-U", "shiftmanager", "shiftflow_ci"])
        test_env = {**migration_env, "RUN_POSTGRES_TESTS": "1", "TEST_POSTGRES_CONNECTION_STRING": connection.replace("Database=shiftmanager;", "Database=shiftflow_ci;")}
        test_filter = "|".join("FullyQualifiedName~" + name for name in ["AuthTests", "TeamIntegrationTests", "AvailabilityTests", "AvailabilityWorkflowIntegrationTests", "SchedulingTests", "PublicationIntegrationTests", "AbsenceIntegrationTests", "ConstraintMigrationTests", "PrioritySchedulingTests", "PriorityOptimizationTests", "AdditionalSchedulingRulesTests", "AbsenceRulesTests", "ModelTests"])
        print("Copied-database app is healthy; running backend integration tests in separate disposable databases.", flush=True)
        tests = command(["dotnet", "test", "tests/ShiftManager.Tests", "-c", "Release", "--no-build", "--no-restore", "--filter", test_filter,
                         "--logger", "console;verbosity=quiet"], command_env=test_env)
        (directory / "backend-tests.log").write_text(tests)
        summary = next(line for line in reversed(tests.splitlines()) if "Passed!" in line)
        # Verify the source's bytes and original container state are unchanged after all tests.
        source_hash = subprocess.Popen(readonly + [clean + "; tar -cf - -C /source ."], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        digest = hashlib.sha256()
        while chunk := source_hash.stdout.read(1024 * 1024):
            digest.update(chunk)
        source_hash.stdout.close()
        require(source_hash.wait() == 0 and digest.hexdigest() == cold_sha, "Source physical bytes changed during validation")
        final_source = guard.inspect("container", guard.CONTAINER)
        require(final_source["State"] == source["State"] and final_source["Image"] == source["Image"], "Source container state changed")
        require(sha(archive) == cold_sha and sha(logical) == backup_report["logical_dump_sha256"], "An existing backup changed")
        report = {"status": "passed", "backup_directory": str(backup_directory), "validation_directory": str(directory), "postgres_image": source["Image"],
                  "cluster_tar_sha256": cold_sha, "logical_dump_sha256": sha(logical), "backup_restore_fingerprints_match": True,
                  "before": before, "after_projected": after, "preserved_shift_definitions": 51,
                  "compatibility_sql_sha256": sha(ROOT / "docs/local-migration-up.sql"), "ef_replay_preserves_configuration": True,
                  "migrations_applied_to_copy": PENDING, "schema_checks": checks, "foreign_keys_checked": len(orphan_queries),
                  "copied_database_app_healthy": True, "copied_database_startup_no_record_writes": True,
                  "backend_tests": summary.strip(), "original_volume_bytes_unchanged": True, "original_container_state_unchanged": True}
        save("validation.json", report)
        print(summary.strip(), flush=True)
        print("Validation passed. Original database bytes and container state are unchanged. Report: " + str(directory / "validation.json"), flush=True)
    finally:
        # Only this UUID-named project's RAM database/containers/network are removed; no volume deletion.
        command(compose + ["down", "--remove-orphans"])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Validation failed ({type(error).__name__}): " + (str(error) if isinstance(error, (RuntimeError, guard.GuardError)) else "details withheld; inspect private artifacts"), file=sys.stderr)
        sys.exit(1)
