# Legacy-compatible migration validation results

**Result: the corrected compatibility path passed isolated validation. The original
regular database remains untouched and still has five pending migrations.**

See the [strategy, runtime ID audit and approval-gated commands](local-migration-validation.md),
[reviewed compatibility SQL](local-migration-up.sql), and
[all 51 shift definitions before and after](local-shift-comparison.md).

## Why this uses a compatibility path

The existing demo was inspected through a read-only RAM copy and already has all 17
migration IDs. Historical EF migration classes, designer models, model snapshot and
application/solver/frontend code were therefore retained unchanged. No deployed history
or administrator edits are rewritten.

The explicit local SQL retains the exact five schema migrations/history IDs while omitting
only the old two semantic/version updates, three manual-only backfills and seven critical
priority backfills. It is generated and checked against EF output. It accepts only the
reviewed 12-migration baseline and 51 definitions, and executes in one transaction.
Already-upgraded/partial targets are rejected. Do not use ordinary EF update on the
unmigrated regular database; follow the compatible runbook.

The earlier unmodified-EF rehearsal in commit `bad8d0b` proved technical migration success
but accepted the incompatible backfills. It is superseded by this configuration-preserving
rehearsal. Its original backups/private evidence are preserved.

## Actual corrected comparison

| Shift / ID suffix | Before | After compatible upgrade |
| --- | --- | --- |
| `שיקום ערב` / `000000000016` | `ORDINARY`, version 4, staffing 0, automatic flag true | Identical; new mode `Automatic`, priority `Normal` |
| `כונן סופש` / `000000000026` | `WEEKEND_ON_CALL`, version 3, staffing 4, automatic flag true | Identical; new mode `Automatic`, priority `Normal` |
| `ב׳ בוקר` / `000000000012` | Automatic flag true, staffing 1 | Preserved; mode `Automatic`, priority `Normal` |
| `ב׳ לילה מומחה` / `000000000013` | Automatic flag true, staffing 1, qualification required | Preserved; mode `Automatic`, priority `Normal` |
| `ב׳ לילה` / `000000000014` | Automatic flag true, staffing 1, NIGHT range | Preserved; mode `Automatic`, priority `Normal` |
| IDs `000000000001`–`000000000007` | Priority column absent; customized names/staffing/flags | All priority `Normal`; every original field retained |
| All 51 definitions | Twelve existing columns per row | Every old value identical; new mode `Automatic` and priority `Normal` |

All IDs have prefix `40000000-0000-0000-0000-`. Existing disable flags still exclude disabled
rows. No row becomes critical solely because of a legacy ID. Administrator changes to
`Critical` and `High` work through the existing API and survive subsequent EF migration
checks. Semantics, names, staffing, order, qualification, periods, ranges, versions and
`UpdatedAt` are all compared exactly, not only row counts.

## Backups and evidence

Existing immutable backups:

```text
/Users/galzada/VcProjects/ShiftFlow/.local-docker-private/20261110T191529Z-c448e80200b347f9991c31d1f3a70ff7/
```

- `cluster.tar`: 67,533,312 bytes; SHA-256 `33b1eaee8ee831654d29834b3451e9fd4489978fa43e4e573d4fd31b78337ec7`.
- `shiftmanager.dump`: 96,497 bytes; SHA-256 `eadf1fc4ecb4fa07a87a849abe32fdcd7996c38b815959704ef7a503587bd637`.

Both were verified, actually restored and compared; neither was overwritten/deleted.
The backup root/run directories remain 0700 and backup files 0600, outside the database
volume and ignored by Git/image context. No real account/employee records enter Git.

New restricted rehearsal artifacts, including `validation.json`, complete before/after
`shift-definitions.json`, fingerprints and test logs:

```text
/Users/galzada/VcProjects/ShiftFlow/.local-docker-private/20261110T201222Z-f5dfb87cfd4d4a4da3e7616ed3572af0/
```

Reviewed compatibility SQL SHA-256: `9d3ab4ab90a7c8bd952474f9be4424b45a54f1b6c0bd014f8873c450fac1cb63`.

## Record and schema preservation

All five migrations succeeded on the restored disposable database. All 26 original tables
remained, with **identical existing-record fingerprints**, including shift definitions.
Only history appends five IDs and new columns/tables appear. All eight new tables are
empty; no demo/synthetic data was imported into the real-record copy.

| Existing table | Before | After |
| --- | ---: | ---: |
| `AspNetRoleClaims` | 0 | 0 |
| `AspNetRoles` | 3 | 3 |
| `AspNetUserClaims` | 0 | 0 |
| `AspNetUserLogins` | 0 | 0 |
| `AspNetUserRoles` | 8 | 8 |
| `AspNetUserTokens` | 0 | 0 |
| `AspNetUsers` | 8 | 8 |
| `AvailabilitySubmission` | 2 | 2 |
| `AvailabilityWindow` | 2 | 2 |
| `DelegatedAvailabilityAudit` | 0 | 0 |
| `OrganizationalTeam` | 4 | 4 |
| `ScheduleEditAudit` | 0 | 0 |
| `ScheduleIssue` | 0 | 0 |
| `SchedulePeriod` | 2 | 2 |
| `SchedulePublication` | 0 | 0 |
| `SchedulingCell` | 0 | 0 |
| `SchedulingRun` | 0 | 0 |
| `Shift` | 122 | 122 |
| `ShiftAssignment` | 0 | 0 |
| `ShiftPreference` | 16 | 16 |
| `ShiftTask` | 0 | 0 |
| `ShiftTypeDefinition` | 51 | 51 |
| `TaskAssignment` | 0 | 0 |
| `TaskTemplate` | 0 | 0 |
| `TeamMembership` | 1 | 1 |
| `__EFMigrationsHistory` | 12 | 17 |

New `AbsenceRequest`, `AbsenceConflict`, `AbsenceDecision`, `AbsenceWeekday`,
`PublicationControl`, `ShiftExchange`, `ShiftExchangeAssignment` and
`ShiftExchangeReservation` tables each have zero rows. All 62 FKs pass orphan checks;
zero invalid/unready/non-live indexes, unvalidated constraints, unexpected notes, unsafe
assignment modes or priorities were found.

The source has no absence schema/requests, assignments or published versions yet. Populated
absence, publication, exchange, availability/delegation and scheduling cases are tested in
separate disposable fixtures. Identity hashes/security stamps/role mappings, team leadership
and membership, availability and every other old field match the source fingerprints.

## Validation and regressions

- **251 backend tests passed, zero failed/skipped**, including restored-schema compatibility,
  all-51 field preservation, automatic eligibility, administrator priority authorization and
  updates, no reseeding/replay, and rollback of schema/history/data after an injected failure.
- Existing solver correctness suites `PrioritySchedulingTests`, `PriorityOptimizationTests`
  and `AdditionalSchedulingRulesTests` passed, with qualification, priority, quota and on-call
  rules intact. No expensive scheduling benchmarks were rerun.
- Existing team-scoped availability, absence and publication tests passed within that run.
- **155 frontend tests passed** (11 files); frontend production build passed. Priority UI
  and its three existing choices are unchanged.
- Backend Release build passed with zero warnings/errors.
- Nine comparison regressions and 11 Docker safety tests passed.
- Compose configuration/image build passed. The migrated-copy app became healthy;
  React/RTL, SPA routing, same-origin CSRF API, anonymous 401, Docker PostgreSQL connectivity
  and both health endpoints passed. Startup did not modify copied records.
- EF reports no pending model changes and no pending migrations after compatible upgrade;
  a subsequent EF update does not execute original backfills or overwrite priorities.
- The normal startup guard still refuses the original database's five pending migrations;
  it does not apply migrations automatically.

## Preservation and remaining decision

After testing, re-reading the original volume through a read-only mount produced the exact
same SHA-256 as the original cold backup. Original container state/image were unchanged;
last start remains `2026-11-05T09:43:28.363460461Z` and last stop
`2026-11-08T22:50:14.003657635Z`. No persistent container was started or restarted.
The demo state, volumes and data remain untouched. Disposable containers/networks were
cleaned up and no Docker volumes deleted.

Existing runtime weekend/reporting ID policies were audited and retained; their semantic
redesign is outside this migration-compatibility task. No scheduling/authorization feature
was rebuilt. External deployments were not assumed to be unmigrated.

The remaining blocker is explicit approval to start the original PostgreSQL container and
apply the reviewed compatibility SQL. After approval, use the linked ordered procedure,
validate all preserved records before writers resume, then run:

```sh
python3 scripts/local-docker.py up
```

Application URL: **http://127.0.0.1:5180**. No push, merge or PR is authorized/performed.
