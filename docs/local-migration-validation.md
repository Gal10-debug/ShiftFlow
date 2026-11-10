# Local database migration audit and approved execution plan

Validation date: 2026-11-10. Application baseline: `65d3809` on
`feature/full-local-docker-environment`. **Nothing in this document authorizes execution
against the persistent database. Starting it and applying migrations require explicit
approval.** Scheduling, authorization and migration implementations were not changed.

The source is the stopped `shiftflow-postgres-1` container, database/owner `shiftmanager`,
volume `shiftflow_postgres-data`. Its PostgreSQL image is preserved exactly:
`sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24`.
The applied baseline ends at `20261027224058_AdditionalSchedulingConstraints` (12 migrations).

## Audit

The five pending migrations and their generated [forward SQL](local-migration-up.sql)
were inspected. The SQL is a review artifact, not an automatic startup script. It contains
five transaction blocks and updates migration history for each migration. Do not assume a
failed run leaves every migration unapplied; inspect history before recovery or retry.

| Migration | Existing-table changes | Existing-record compatibility and risks | Reversibility |
| --- | --- | --- | --- |
| `20261031164800_WeekendBoardNotes` | Adds nullable `ShiftAssignment.Note`, varchar(1000). Changes `ShiftTypeDefinition.Semantic` for stable IDs ending `026` to `ORDINARY`, `016` to `WEEKEND_ON_CALL`; increments both `Version` values. | No rows deleted. Existing notes start null. This **changes classification**, even for renamed rows; old `026` is cleared first to satisfy the existing filtered unique semantic index. A customized conflicting classification could fail the migration. The restored source passed. Published snapshots are not rewritten. | `Down` drops notes and hardcodes the old semantics. It **does not decrement versions** or recover customized old values; not an exact data inverse. |
| `20261103080334_Phase6AssignmentModes` | Adds non-null `ShiftTypeDefinition.AssignmentMode`, varchar(16), default `Automatic`; stable IDs `012`–`014` become `ManualOnly`. Adds `CK_ShiftTypeDefinition_AssignmentMode`. | All existing rows get valid defaults; existing names, order, staffing and qualification settings are retained. New configuration changes which rows are eligible for future automatic generation. | `Down` drops the check and column; loses assignment-mode configuration. |
| `20261103081004_Phase6Absences` | Creates four absence tables described below; no existing columns altered or removed. | Empty new tables, no seeding/backfill of requests. New FKs restrict deletion of referenced users/publications/periods once requests exist. Adds immutable decisions trigger/function. Existing auth, availability and schedules are retained. | `Down` removes trigger/function and all four tables, losing requests, decisions, weekdays and conflicts created afterward. |
| `20261104135940_OperationalControls` | Creates four publication/exchange tables described below; no existing columns altered or removed. | No publication snapshots or existing records rewritten, no exchanges created. Restrictive FKs apply once new rows exist. Existing publications have no new control row initially. | `Down` drops all four tables, losing visibility/deletion controls, exchanges and reservations. |
| `20261106074419_SchedulingPriority` | Adds non-null `ShiftTypeDefinition.SchedulingPriority`, varchar(16), default `Normal`; stable IDs `001`–`007` become `Critical`. Adds `CK_ShiftTypeDefinition_SchedulingPriority`. | Existing rows receive valid values; names, order, staffing and published snapshots remain intact. Changes priority configuration for future generation. | `Down` drops check and column, losing subsequent priority edits. |

No forward migration drops a table/column, truncates/deletes rows, changes an existing
column's type, or updates Identity credentials. The explicit existing-column data changes
are the two semantic/version updates. New-column backfills are intentional. DDL takes
locks, so execute during exclusive local maintenance with app/host development writers
stopped. Rollback migrations are structurally available but can destroy post-upgrade data;
prefer the verified backup recovery procedure.

### New tables and columns

All columns below are additions in new tables. Types, lengths, nullability, exact constraint
names and expressions appear in the generated SQL.

| Table | Columns |
| --- | --- |
| `AbsenceRequest` | `Id`, `UserId`, `Kind`, `Status`, `Reason`, `Explanation`, `StartDate`, `EndDate`, `Recurring`, `FullDay`, `StartTime`, `EndTime`, `ReplacesId`, `ReplacesVersion`, `Version`, `CreatedAt`, `UpdatedAt` |
| `AbsenceConflict` | `Id`, `AbsenceRequestId`, `SchedulePeriodId`, `PublicationId`, `AssignmentId`, `Date`, `StartsAt`, `EndsAt`, `ShiftName`, `ResolvedAt`, `ResolvedByPublicationId` |
| `AbsenceDecision` | `Id`, `AbsenceRequestId`, `ActorUserId`, `Decision`, `RejectionReason`, `OccurredAt` |
| `AbsenceWeekday` | `AbsenceRequestId`, `Day` |
| `PublicationControl` | `PublicationId`, `Hidden`, `DeletedAt`, `Version` |
| `ShiftExchange` | `Id`, `SchedulePeriodId`, `RequesterUserId`, `ReplacementUserId`, `Status`, `Reason`, `Version`, `CreatedAt`, `UpdatedAt` |
| `ShiftExchangeAssignment` | `Id`, `ShiftExchangeId`, `PublicationId`, `SourceAssignmentId`, `DefinitionId`, `Date`, `StartsAt`, `EndsAt`, `IsShiftLead`, `Note`, `ReplacementAssignmentId`, `AppliedPublicationId` |
| `ShiftExchangeReservation` | `AssignmentId`, `ShiftExchangeId` |

### Indexes, constraints and triggers

`WeekendBoardNotes` adds no indexes/constraints; it uses the existing unique semantic index.
Assignment modes allow only `Automatic`/`ManualOnly`; priority allows `Critical`/`High`/`Normal`.

`Phase6Absences` adds four primary keys (`AbsenceWeekday` has the composite request/day key),
nine secondary indexes, and nine FKs, all `ON DELETE RESTRICT`:

- Request references user and replaced request; conflict references request, period, original
  publication and resolution publication; decision references request and actor; weekday
  references request. `AbsenceConflict.AssignmentId` is a snapshot ID, not a new FK.
- Unique indexes: conflict `(AbsenceRequestId, PublicationId, AssignmentId)`; decision
  `(AbsenceRequestId)`; pending replacement `(ReplacesId)` filtered to pending non-null replacements.
- Other indexes: conflict `(PublicationId)`, `(ResolvedByPublicationId)`,
  `(SchedulePeriodId, ResolvedAt)`; decision `(ActorUserId)`; request `(Status, CreatedAt)`,
  `(UserId, Status, StartDate)`.
- Checks: request dates/recurrence, replacement kind/version, reason, status, times/full-day,
  positive version; decision value/rejection reason; weekday 0–6.
- `immutable_absence_decision()` and its trigger reject updates/deletes to decisions.

`OperationalControls` adds four primary keys, eight secondary indexes and nine restrictive FKs:

- Control references publication; exchange references requester, replacement and period;
  exchange assignment references exchange, definition, original and applied publication;
  reservation references exchange. Source/replacement assignment IDs and reservation's
  assignment key are not newly constrained to live assignment rows.
- Unique index: exchange assignment `(ShiftExchangeId, SourceAssignmentId)`.
- Other indexes: exchange `(ReplacementUserId)`, `(RequesterUserId)`,
  `(SchedulePeriodId, Status)`; assignment `(AppliedPublicationId)`, `(DefinitionId)`,
  `(PublicationId)`; reservation `(ShiftExchangeId)`.
- `CK_Exchange_Status` enumerates the seven existing application exchange states.

No existing index or constraint is dropped or weakened by these forward migrations.

## Backups and rehearsal

The reusable validation command is:

```sh
python3 scripts/validate-local-migrations.py
```

It requires Python 3.11+, Docker Compose supporting `!override`, the built local app image,
the host .NET 10 build and EF tool. It refuses a running source, verifies regular-project
ownership/mounts/credentials, checks PostgreSQL 17 clean shutdown metadata, and reads the
source through a read-only mount. It never starts/stops that persistent container. If a
future backup would require starting or restarting it, stop and obtain authorization first.

Every run creates a new UUID/timestamp directory under ignored `.local-docker-private/`:

- `cluster.tar`: complete consistent cold-cluster backup, including database files and roles;
- `shiftmanager.dump`: PostgreSQL custom-format logical backup of `shiftmanager`, made from
  the restored cold backup **before migration**;
- `cold-backup.json`, `validation.json`: image identity, checksums and results;
- `before.json`, `after.json`: counts and SHA-256 fingerprints, without raw records;
- private migration/test diagnostics and disposable Compose override.

The backup root and run directory are mode 0700; backup, report and log files are mode
0600. The test-only key subdirectory permits the container UID but sits beneath the private
0700 parent. Backup files are outside the database volume, excluded from Git, and excluded
from the image build context. Preserve them; do not share them or overwrite older backups.

The cold archive is checked for PostgreSQL/control files and unsupported tablespace/WAL
links, then actually restored into a UUID-named PostgreSQL container with a RAM data mount.
Its exact original image is used. The logical dump is inspected with `pg_restore --list`,
restored into a second fresh disposable database and compared against every original table's
row count and canonical JSON content fingerprint. Only then are the five migrations applied
to the copied `shiftmanager` database.

Comparison covers all existing columns, including password hashes/security stamps, role
mappings, team leadership/membership, preferences and publication snapshot bytes. It removes
the three new columns when comparing old-row fingerprints and predicts the exact two
semantic/version updates. New columns are checked separately; eight new tables must be empty.
History must retain all 12 old IDs and append exactly the five audited IDs. Every actual FK
is checked for orphans; constraint validation and index ready/valid/live flags are checked.

The app starts only against the migrated copy. React/SPA routing, health, same-origin CSRF
API and anonymous authorization are checked. Startup must leave all copied database rows
unchanged. Backend integration fixtures use additional fresh databases, never the copy
containing real records. Cleanup removes only the test project's containers/network; no
Docker volumes are deleted. The source archive is re-read and its checksum compared at the
end, along with the source container's exact state, to prove it stayed untouched.

Current backup location and actual counts/results are recorded in
[validation results](local-migration-results.md). No persistent migration has been executed.

## Procedure after explicit approval only

Request approval covering **both starting the existing PostgreSQL container and applying
these five migrations**. Keep the app and any host-development writers stopped throughout.
Do not run `scripts/dev.sh setup/migrate`, bare `compose up postgres`, SQL rollback scripts,
or the reviewed SQL file directly: the base Compose file selects a different database image.

1. Confirm the same feature branch, reviewed code and clean working tree. Check the existing
   container is still stopped, its image/volume are unchanged, and the backup is current.
   If records could have changed since the rehearsal, repeat the cold-backup validation first.
   In the same Bash/Zsh terminal, with shell tracing disabled:

   ```sh
   cd /Users/galzada/VcProjects/ShiftFlow
   set +x
   export BACKUP_DIRECTORY="$PWD/.local-docker-private/20261110T191529Z-c448e80200b347f9991c31d1f3a70ff7"
   python3 - <<'PY'
   import hashlib, json, os
   from pathlib import Path
   p = Path(os.environ['BACKUP_DIRECTORY'])
   result = json.loads((p / 'validation.json').read_text())
   assert result['status'] == 'passed'
   for name, key in [('cluster.tar','cluster_tar_sha256'), ('shiftmanager.dump','logical_dump_sha256')]:
       with (p / name).open('rb') as backup:
           assert hashlib.file_digest(backup,'sha256').hexdigest() == result[key]
       assert (p / name).stat().st_mode & 0o077 == 0
   print('Both backup checksums and restricted file permissions verified.')
   PY
   python3 scripts/local-docker.py config
   python3 scripts/local-docker.py verify
   ```

   `verify` is expected to exit nonzero and list **only the five audited migrations**. Any
   other failure is a stop condition. Confirm no competing PostgreSQL process occupies 5432.

2. **Only after approval**, start the existing container without recreation, then load the
   connection from the existing ignored `.env` without printing it:

   ```sh
   (
   set -euo pipefail
   set +x
   docker start shiftflow-postgres-1
   for attempt in {1..45}; do
     migration_health="$(docker inspect shiftflow-postgres-1 --format '{{.State.Health.Status}}')"
     [ "$migration_health" != healthy ] || break
     sleep 2
   done
   [ "$migration_health" = healthy ] || { echo 'Database did not become healthy; stop here.'; exit 1; }
   ConnectionStrings__Database="$(
     unset POSTGRES_PASSWORD
     docker compose --project-name shiftflow --env-file .env -f compose.yaml config --format json |
       node scripts/dev-config.mjs
   )"
   export ConnectionStrings__Database
   export ASPNETCORE_ENVIRONMENT=Development
   dotnet tool restore
   dotnet build ShiftManager.slnx -c Release --no-restore --warnaserror
   dotnet ef database update 20261106074419_SchedulingPriority \
     --project src/ShiftManager --configuration Release --no-build
   unset ConnectionStrings__Database
   )
   ```

   This command is pinned to the audited final migration, not any future head. Expected:
   each of the five IDs is reported as applied, followed by `Done.` and exit status zero.
   If it fails, stop. Keep the app stopped; inspect restricted diagnostics and history.
   Do not reset/reseed, downgrade or retry destructive SQL automatically.

3. Validate **before** app startup:

   ```sh
   python3 scripts/local-docker.py verify
   python3 - <<'PY'
   import importlib.util, json, os, subprocess
   from pathlib import Path
   spec = importlib.util.spec_from_file_location('validation','scripts/validate-local-migrations.py')
   v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)
   backup = Path(os.environ['BACKUP_DIRECTORY'])
   before = json.loads((backup / 'before.json').read_text())
   result = json.loads((backup / 'validation.json').read_text())
   def sql(query):
       cmd = ['docker','exec','-i','shiftflow-postgres-1','psql','-X','-qAt',
              '-U','shiftmanager','-d','shiftmanager','-v','ON_ERROR_STOP=1']
       r = subprocess.run(cmd,input='BEGIN READ ONLY;\n'+query+';\nROLLBACK;\n',
                          text=True,capture_output=True,check=True)
       return json.loads(r.stdout)
   tables = sql("SELECT json_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname='public'")
   after = {t: sql(v.fingerprint_query(t,v.ADDED.get(t,()))) for t in tables}
   v.compare(before,after,result['expected_definition_fingerprint'])
   definitions = sql('SELECT json_agg(json_build_object(\'id\',"Id",\'mode\',"AssignmentMode",\'priority\',"SchedulingPriority")) FROM "ShiftTypeDefinition"')
   manual = {f'40000000-0000-0000-0000-{i:012d}' for i in (12,13,14)}
   critical = {f'40000000-0000-0000-0000-{i:012d}' for i in range(1,8)}
   assert all(d['mode'] == ('ManualOnly' if d['id'] in manual else 'Automatic') and
              d['priority'] == ('Critical' if d['id'] in critical else 'Normal') for d in definitions)
   assert sql('SELECT count(*) FROM "ShiftAssignment" WHERE "Note" IS NOT NULL') == 0
   assert sql("SELECT count(*) FROM pg_constraint WHERE connamespace='public'::regnamespace AND NOT convalidated") == 0
   assert sql("SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid WHERE c.relnamespace='public'::regnamespace AND NOT (i.indisvalid AND i.indisready AND i.indislive)") == 0
   print('Existing records, expected configuration changes, new empty tables and schema validity verified.')
   PY
   ```

   The verification wrapper authenticates and requires no pending/unknown migrations. The
   comparison must pass against the still-current backup while writers remain stopped.
   No write-based smoke test or real-account password change belongs in this step.

4. Once all validation passes, start Docker normally:

   ```sh
   python3 scripts/local-docker.py up
   python3 scripts/local-docker.py status
   curl --fail http://127.0.0.1:5180/health/live
   curl --fail http://127.0.0.1:5180/health/ready
   ```

   Open `http://127.0.0.1:5180`. Use existing credentials for ordinary manual use.
   Shut down without deleting data with `python3 scripts/local-docker.py stop`.

## Recovery / restoration procedure

If migration or validation fails, keep the application stopped and preserve the failed
database and all backups. Do not run `Down`: it would drop added data and cannot exactly
undo the semantic/version updates. Record the actual migration history and error first.

The following restores the **cold backup to a separate RAM-only recovery container** without
overwriting the original database, deleting any volume, or publishing a port. It is a
recovery inspection step; it does not switch the application to an old schema.

```sh
export RECOVERY_CONTAINER="shiftflow-recovery-$(date -u +%Y%m%dT%H%M%SZ)-$$"
export RECOVERY_IMAGE="$(python3 - <<'PY'
import json, os
from pathlib import Path
print(json.loads((Path(os.environ['BACKUP_DIRECTORY'])/'cold-backup.json').read_text())['postgres_image'])
PY
)"
docker run -d --name "$RECOVERY_CONTAINER" --network none \
  --tmpfs /var/lib/postgresql/data:rw,size=1024m \
  --mount "type=bind,source=$BACKUP_DIRECTORY/cluster.tar,target=/backup/cluster.tar,readonly" \
  --entrypoint sh "$RECOVERY_IMAGE" -ec \
  'test ! -e /var/lib/postgresql/data/PG_VERSION; tar -xf /backup/cluster.tar -C /var/lib/postgresql/data; exec /usr/local/bin/docker-entrypoint.sh postgres'
docker exec "$RECOVERY_CONTAINER" pg_isready -h 127.0.0.1 -U shiftmanager -d shiftmanager
docker exec "$RECOVERY_CONTAINER" psql -X -U shiftmanager -d shiftmanager -v ON_ERROR_STOP=1 \
  -c 'BEGIN READ ONLY; SELECT count(*) FROM "AspNetUsers"; SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1; ROLLBACK;'
```

Wait for readiness before the final query. Expect eight accounts and the original 12
migration IDs for the recorded backup. The backup contains original role/password hashes;
no credential replacement or synthetic seeding is required. The logical backup was also
restore-tested with `pg_restore --exit-on-error` into a fresh database, with all table
fingerprints matching. It can be restored to another **new** database with the existing
`shiftmanager` owner role after a separately reviewed recovery plan.

Any replacement/restoration of the original database or cutover requires separate explicit
approval. Preserve the failed database for diagnosis and post-backup writes. Do not copy
cluster files into a running server, delete the persistent volume, or silently point the
app to the recovery copy. The original-schema backup must be re-migrated after fixing the
cause before the current application can use it.
