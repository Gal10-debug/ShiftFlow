# Legacy-compatible local database upgrade

The regular database must use the explicit compatibility upgrade below. **Do not run a
normal `dotnet ef database update` against its current 12-migration baseline.** The
historical EF migrations contain demo-layout backfills that do not match this database.
Starting the persistent container and applying the upgrade still require explicit approval.

## Correction strategy and deployed history

Read-only inspection of a RAM copy of the existing local demo confirmed all 17 migrations,
including these five, are already applied. The demo's original container and volume were
not modified. No local pilot deployment exists to inspect; external deployment state is
not assumed. Historical migration classes, designer models, the current model snapshot,
and application scheduling code therefore remain byte-for-byte unchanged.

The correction is an **explicit compatibility path for this legacy database**, not a new
migration or a rewrite of deployed history. `scripts/prepare-local-migrations.py` generates
[the reviewed SQL](local-migration-up.sql) from EF's five original schema migrations and
removes exactly the 12 known fixed-ID update statements. It fails on unexpected update,
history-insert or transaction structure. All schema operations and five history inserts
are retained. One transaction, migration-history/definition locks, a five-second lock
limit, a five-minute statement limit and exact baseline checks protect execution.

The script accepts exactly the original 12 migration IDs and 51 existing definitions.
Already-upgraded or partially upgraded databases are rejected without changes. Existing
administrator edits in deployed databases are not repaired, normalized or reset. The
compatibility path is not intended for upgrading demo/pilot or initializing a fresh site.
The original migration behavior remains available to their established workflows.

No migration ID is renamed/deleted. EF recognizes the resulting 17-entry history and
schema normally. Re-running EF afterward performs no backfills; regression tests verify
that administrator-set priorities survive. Model/snapshot consistency checks pass because
customized seed *values* can differ without changing the schema/model definition.

## Root cause and exact corrections

Fixed seed IDs were treated as universally authoritative business meanings even after
administrators customized their rows. For example, demo slot 16 means weekend on-call,
but the regular database uses that ID for `שיקום ערב`; its actual `כונן סופש` is slot 26.
The same mismatch maps Monday shifts to demo manual-only categories and treats seven
legacy rows as critical regardless of administrator choice.

| Pending migration | Compatible behavior | Existing configuration preserved |
| --- | --- | --- |
| `20261031164800_WeekendBoardNotes` | Adds nullable `ShiftAssignment.Note`; omits both semantic/version updates. | All semantic classifications and versions, including `שיקום ערב` = `ORDINARY` and `כונן סופש` = `WEEKEND_ON_CALL`. |
| `20261103080334_Phase6AssignmentModes` | Adds `AssignmentMode` with `Automatic` default and the original check; omits three `ManualOnly` backfills. | Existing inclusion flags still decide automatic eligibility. Disabled rows remain disabled; the three enabled Monday rows remain automatic. |
| `20261103081004_Phase6Absences` | All original schema/constraint/trigger operations. | No existing records are modified; four new empty tables. |
| `20261104135940_OperationalControls` | All original schema/index/FK operations. | No existing snapshots or records are modified; four new empty tables. |
| `20261106074419_SchedulingPriority` | Adds `SchedulingPriority` with `Normal` default and original check; omits seven `Critical` backfills. | No ID implies criticality in this upgrade. Administrators use the existing UI/API to select `Critical`, `High` or `Normal`. |

All 51 definitions retain names, staffing, order, automatic flags, qualification requirements,
semantics, time ranges, day/night periods, versions and timestamps. Every existing column
is compared; the new columns are checked separately. See the complete
[51-row comparison](local-shift-comparison.md) and [validation results](local-migration-results.md).

New `Automatic` values intentionally preserve the old inclusion flag rather than creating
manual-only rules from a disabled flag or a legacy ID. No independent authoritative rule
requires these three Monday rows to become manual-only. The priority column did not exist
in the source; `Normal` is a neutral initial value, not an override of saved priorities.

## Runtime ID audit and scope

- `ShiftTypeDefinitionConfiguration.HasData` and historical migration snapshots describe
  the established seeded/demo layout. They are not startup reseeding instructions. Changing
  them would introduce future model data updates and alter supported layouts; they are retained.
- Runtime `SchedulingInputs`, `SchedulingService`, the optimizer and frontend use persisted
  `AssignmentMode`, `SchedulingPriority`, semantic classification and inclusion flags.
  The priority UI/API already supports all three values and validates authorization/versioning.
- CP-SAT coverage stages remain Critical, High, Normal, then fairness, preferences and
  stability. Qualification constraints, absences, team-leader exclusions, hard blocks,
  overlap limits, quotas and semantic on-call pairing remain intact.
- Existing documented weekend policy uses stable IDs 2/3/4/5/16, the row-3 staffing exception,
  and row-8 Saturday exclusion. `PlannedWorkHoursCalculator` also has stable reporting
  categories for IDs 8/12/13/14. These are existing explicit policies without an admin
  priority/mode override; they were audited and retained, not generalized or deleted.
  Their labels reflect the seeded layout and may differ from customized names. Redesigning
  those policies needs separate business decisions; this task guarantees configuration
  preservation and removes the conflicting migration backfills, not a new scheduling model.
- Semantic-based `SIX_NINE` and `WEEKEND_ON_CALL` constraints remain semantic-based. The
  regular on-call row 26 keeps pairing/weekend rules; row 16 does not acquire them.

Historical `Down` operations are not a safe recovery strategy: they drop new tables/columns
and `WeekendBoardNotes.Down` can hardcode old semantic values. Use backup recovery instead.

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

## Backup-preserving rehearsal

The existing backups remain under:

```text
.local-docker-private/20261110T191529Z-c448e80200b347f9991c31d1f3a70ff7/
```

`cluster.tar` and `shiftmanager.dump` are never overwritten or deleted. The cold image is
`sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24`.
The backup root/run directory is 0700; backup and report files are 0600. No backup or real
account records enter Git or Docker images. Validation artifacts use a new private directory.

To reproduce, with Python 3.11+, Docker Compose supporting `!override`, .NET 10 and the
restored EF tool:

```sh
dotnet build ShiftManager.slnx -c Release --warnaserror
python3 -B scripts/prepare-local-migrations.py  # checks; never applies SQL
python3 -B scripts/local-docker.py build
python3 -B scripts/validate-local-migrations.py
python3 -B scripts/test-local-migrations.py
python3 -B scripts/test-local-docker.py
npm test --prefix frontend
npm run build --prefix frontend
```

The validator checks source ownership/state and both existing backup checksums, restores
`cluster.tar` into a uniquely named RAM-backed PostgreSQL instance, and restore-tests the
logical dump into another fresh disposable database. It applies the reviewed compatibility
SQL only to that restored copy. It compares all old-table fingerprints, all 51 definitions,
new defaults, history, all FKs and index/constraint validity. It checks EF/model consistency,
no replay/reseeding, copied-app startup/health/routing/authentication, and no startup writes.
Additional synthetic integration fixtures use separate disposable databases.

The source volume is mounted only read-only for verification. The original container is
never started/stopped. After tests, the original cold-volume archive checksum and original
container state must still match. Backups are rechecked for byte equality. Disposable
containers/networks are cleaned up; no Docker volumes are deleted.

## Approved execution procedure — do not execute yet

Approval must explicitly cover starting the existing regular PostgreSQL container and
applying this **compatibility SQL**, not the original EF data backfills. Keep all application
and host-development writers stopped until data validation finishes. If source data changed
since the backup, stop and obtain a new reviewed backup/rehearsal first.

1. From the reviewed feature branch with a clean working tree, verify backup and SQL hashes:

   ```sh
   cd /Users/galzada/VcProjects/ShiftFlow
   set +x
   export BACKUP_DIRECTORY="$PWD/.local-docker-private/20261110T191529Z-c448e80200b347f9991c31d1f3a70ff7"
   export VALIDATION_DIRECTORY="$PWD/.local-docker-private/20261110T201222Z-f5dfb87cfd4d4a4da3e7616ed3572af0"
   python3 - <<'PY'
   import hashlib, json, os
   from pathlib import Path
   b = Path(os.environ['BACKUP_DIRECTORY'])
   r = json.loads((Path(os.environ['VALIDATION_DIRECTORY'])/'validation.json').read_text())
   assert r['status'] == 'passed' and r['preserved_shift_definitions'] == 51
   for path, key in [(b/'cluster.tar','cluster_tar_sha256'), (b/'shiftmanager.dump','logical_dump_sha256'),
                     (Path('docs/local-migration-up.sql'),'compatibility_sql_sha256')]:
       with path.open('rb') as f: assert hashlib.file_digest(f,'sha256').hexdigest() == r[key]
   assert all((b/name).stat().st_mode & 0o077 == 0 for name in ('cluster.tar','shiftmanager.dump'))
   print('Backup and reviewed SQL checksums verified.')
   PY
   python3 -B scripts/prepare-local-migrations.py
   python3 -B scripts/local-docker.py config
   python3 -B scripts/local-docker.py verify
   ```

   `verify` should refuse startup and list exactly the five pending migrations. Any other
   discrepancy is a stop condition. Preserve the existing `.env` password. No new secret
   variables are required: the wrapper uses `.env`, and the approved SQL runs inside the
   verified container as its existing `shiftmanager` user. Do not source `.env` as shell code.

2. **Only after approval**, start the verified container without recreation and apply SQL:

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
   [ "$migration_health" = healthy ] || { echo 'Database not healthy; stop here.'; exit 1; }
   docker exec -i shiftflow-postgres-1 psql -X -U shiftmanager -d shiftmanager \
     -v ON_ERROR_STOP=1 < docs/local-migration-up.sql
   )
   ```

   Expected: `BEGIN`, lock/check/schema operations, five `INSERT 0 1` history results,
   and final `COMMIT`, with exit status zero. There should be **no UPDATE of shift definitions**.
   All five migrations commit together; SQL failure rolls back the transaction. A rerun or
   already-upgraded target is rejected by the history guard. Do not bypass it.

3. Before app startup, verify authentication/schema/history and all preserved records:

   ```sh
   python3 -B scripts/local-docker.py verify
   python3 - <<'PY'
   import importlib.util, json, os, subprocess
   from pathlib import Path
   spec = importlib.util.spec_from_file_location('v','scripts/validate-local-migrations.py')
   v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)
   p = Path(os.environ['VALIDATION_DIRECTORY'])
   before = json.loads((p/'before.json').read_text())
   def sql(query):
       r = subprocess.run(['docker','exec','-i','shiftflow-postgres-1','psql','-X','-qAt',
                           '-U','shiftmanager','-d','shiftmanager','-v','ON_ERROR_STOP=1'],
                          input='BEGIN READ ONLY;\n'+query+';\nROLLBACK;\n',text=True,capture_output=True,check=True)
       return json.loads(r.stdout)
   tables = sql("SELECT json_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname='public'")
   after = {t:sql(v.fingerprint_query(t,v.ADDED.get(t,()))) for t in tables}
   v.compare(before,after)
   assert sql('SELECT count(*) FROM "ShiftTypeDefinition" WHERE "AssignmentMode" <> \'Automatic\' OR "SchedulingPriority" <> \'Normal\'') == 0
   assert sql('SELECT count(*) FROM "ShiftAssignment" WHERE "Note" IS NOT NULL') == 0
   assert sql("SELECT count(*) FROM pg_constraint WHERE connamespace='public'::regnamespace AND NOT convalidated") == 0
   assert sql("SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid WHERE c.relnamespace='public'::regnamespace AND NOT (i.indisvalid AND i.indisready AND i.indislive)") == 0
   print('All existing records/51 definitions preserved; defaults, history and schema verified.')
   PY
   ```

   This check must run before ordinary user writes or administrator priority edits.
   Newly initialized priorities can subsequently be edited through the existing UI/API.

4. After successful validation, start normally:

   ```sh
   python3 scripts/local-docker.py up
   python3 scripts/local-docker.py status
   curl --fail http://127.0.0.1:5180/health/live
   curl --fail http://127.0.0.1:5180/health/ready
   ```

   Open **http://127.0.0.1:5180**. Stop without deleting data using
   `python3 scripts/local-docker.py stop`. Normal startup never migrates or seeds.

## Recovery

If SQL or validation fails, keep writers stopped and preserve the original database,
backups and private diagnostics. Inspect migration history before any recovery. Do not run
historical `Down` operations or restore over a running database. The atomic failure regression
verifies schema, history and every old shift field roll back together.

For backup inspection/recovery, restore to a separate RAM-only container, not the original:

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
# Wait for readiness before querying.
docker exec "$RECOVERY_CONTAINER" psql -X -U shiftmanager -d shiftmanager -v ON_ERROR_STOP=1 \
  -c 'BEGIN READ ONLY; SELECT count(*) FROM "AspNetUsers"; SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1; ROLLBACK;'
```

Expect eight accounts and the original 12 migration IDs for the recorded backup. The logical
backup was also actually restored into a fresh database with matching fingerprints. Any
replacement of the original database or cutover needs separate approval and a reviewed plan;
no volume deletion, reset, synthetic demo data or silent replacement is part of recovery.
