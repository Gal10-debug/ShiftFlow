# Local migration validation results — 2026-11-10

**Result: ready for an approval decision. No migration was applied to the original database.**

Code/application baseline: `65d3809`, branch `feature/full-local-docker-environment`.
Detailed [audit, exact approval-gated execution commands and recovery procedure](local-migration-validation.md).
Reviewed [generated SQL](local-migration-up.sql).

## Backup status

Restricted location (outside the database volume, ignored by Git):

```text
/Users/galzada/VcProjects/ShiftFlow/.local-docker-private/20261110T191529Z-c448e80200b347f9991c31d1f3a70ff7/
```

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| `cluster.tar` | 67,533,312 bytes | `33b1eaee8ee831654d29834b3451e9fd4489978fa43e4e573d4fd31b78337ec7` |
| `shiftmanager.dump` | 96,497 bytes | `eadf1fc4ecb4fa07a87a849abe32fdcd7996c38b815959704ef7a503587bd637` |

The root/run directories are 0700 and both backup files are 0600. Existing backups were
not overwritten. The complete cold backup was restored using the original PostgreSQL image;
the logical custom-format backup was inspected and restored into a second fresh database.
Every existing table's counts and content fingerprints matched between the two restores.
Private manifests and `validation.json` hold the machine-readable evidence. No raw employee,
account or credential values are included in this report or committed to Git.

## Migration and integrity results

All five migrations succeeded **only in the restored RAM-backed disposable database**:

1. `20261031164800_WeekendBoardNotes`
2. `20261103080334_Phase6AssignmentModes`
3. `20261103081004_Phase6Absences`
4. `20261104135940_OperationalControls`
5. `20261106074419_SchedulingPriority`

All 26 existing tables remained. Existing-column fingerprints matched exactly except for
the predicted `Semantic`/`Version` changes in two shift definitions and the five appended
migration-history rows. No unexpected data changes were found. New assignment-mode and
priority values matched the audited stable-ID mappings; added notes were null. Eight new
tables were empty, confirming no seeding.

| Existing table | Before | After |
| --- | ---: | ---: |
| `AspNetUsers` | 8 | 8 |
| `AspNetRoles` | 3 | 3 |
| `AspNetUserRoles` | 8 | 8 |
| `AspNetRoleClaims` | 0 | 0 |
| `AspNetUserClaims` | 0 | 0 |
| `AspNetUserLogins` | 0 | 0 |
| `AspNetUserTokens` | 0 | 0 |
| `OrganizationalTeam` | 4 | 4 |
| `TeamMembership` | 1 | 1 |
| `AvailabilitySubmission` | 2 | 2 |
| `AvailabilityWindow` | 2 | 2 |
| `DelegatedAvailabilityAudit` | 0 | 0 |
| `ShiftPreference` | 16 | 16 |
| `SchedulePeriod` | 2 | 2 |
| `Shift` | 122 | 122 |
| `ShiftTypeDefinition` | 51 | 51 |
| `ShiftAssignment` | 0 | 0 |
| `SchedulePublication` | 0 | 0 |
| `SchedulingCell` | 0 | 0 |
| `SchedulingRun` | 0 | 0 |
| `ScheduleEditAudit` | 0 | 0 |
| `ScheduleIssue` | 0 | 0 |
| `ShiftTask` | 0 | 0 |
| `TaskAssignment` | 0 | 0 |
| `TaskTemplate` | 0 | 0 |
| `__EFMigrationsHistory` | 12 | 17 |

New `AbsenceRequest`, `AbsenceConflict`, `AbsenceDecision`, `AbsenceWeekday`,
`PublicationControl`, `ShiftExchange`, `ShiftExchangeAssignment` and
`ShiftExchangeReservation` tables each contained zero rows.

All 62 foreign keys passed orphan checks. Zero unvalidated constraints and zero
invalid/not-ready/not-live indexes were found. Identity tables, password/security-stamp
fields, role mappings, team leadership and memberships, availability and all existing
scheduling fields were covered by full existing-row fingerprints.

The source contains **no absence schema/requests yet, no assignments and no published
versions**. Consequently this real-record rehearsal cannot demonstrate preservation of
populated legacy absence/publication data. The integration tests separately exercise
populated absence requests, decisions, publication snapshots, exchanges, scheduling and
concurrency on fresh disposable databases.

## Application and test results

- Compose configuration validated; persistent mapping remains the existing external
  `shiftflow_postgres-data`, with the exact original image preserved.
- Local application image build passed (React and .NET build stages reused verified cache).
- Backend Release build passed with zero warnings/errors.
- The migrated-copy app became healthy; React/RTL entrypoint, SPA route, same-origin
  `/api/auth/csrf`, PostgreSQL Docker DNS/connectivity and both health endpoints passed.
- Anonymous `/api/auth/me` correctly returned 401. No real account password or user data
  was changed for smoke testing. App startup left the copied database fingerprints unchanged.
- **197 relevant backend integration tests passed; zero failed or skipped.** Coverage includes
  authentication, teams, availability/delegation, scheduling, publication, absence workflows,
  priority migration and constraints. Each fixture used a fresh disposable database.
- Nine migration-comparison regression tests and 11 existing Docker safety tests passed.

The previous full local Docker validation remains recorded in `docs/local-docker.md`;
this task adds migration testing on a restored copy of the actual regular data.

## Original-resource preservation and remaining approval

After testing, a second read-only archive of the original volume had the **identical
SHA-256** as `cluster.tar`. The original container's full state and image were unchanged.
Its last start remains `2026-11-05T09:43:28.363460461Z`; last stop remains
`2026-11-08T22:50:14.003657635Z`. No persistent services were started or restarted.

The demo container remained stopped with its original start/stop timestamps. The existing
three-volume list and original networks were retained. Disposable containers/networks
were cleaned up; no Docker volume was deleted. The normal application was not started
against the original database.

The remaining blocker is explicit approval to **start the existing regular PostgreSQL
container and apply exactly these five migrations**, including their documented shift
configuration backfills. Follow the ordered commands in the linked runbook, verify the
backups and data before app startup, and retain backups for recovery. No push, merge or PR
is part of this work.
