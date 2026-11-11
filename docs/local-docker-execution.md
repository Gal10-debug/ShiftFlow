# Approved local Docker execution — 2026-11-11

The approved compatibility upgrade completed on the original regular `shiftmanager`
database, and the full local stack is running at **http://127.0.0.1:5180**.

## Verification repair

The interrupted verifier queried:

```sql
SELECT current_database() = 'shiftmanager' AND current_user = 'shiftmanager';
```

Authenticated read-only TCP inspection confirmed the actual response was `t`, meaning
the expected database condition was true. Commit `832a34d` adds strict parsing of psql
`t`/`f` alongside typed JSON values. The assertion still requires `is True`; false,
numeric and string results cannot satisfy it, and malformed/multiple results fail.
Fifteen Docker/parser safety tests and nine migration-comparison tests passed.

The resumed execution verified the same container ID, image, volume mounts, credentials,
reviewed SQL checksum, all baseline records and the exact 12-entry migration history.
PostgreSQL was already healthy from the previously approved start; it was not restarted.

## Backups and upgrade

Existing backups were preserved. Fresh physical and logical backups had already been
created and actually restored/compared in isolation before the interrupted attempt:

```text
.local-docker-private/20261111T072411Z-approved-359d4e4a5532447ab9f9f0931df20830/
```

Their checksums and restricted permissions were rechecked before resuming. The directory
is private and ignored by Git; it contains `cluster.tar`, `shiftmanager.dump`, baseline
fingerprints, the isolated-restore report, `persistent-upgrade.json`,
`application-verification.json` and private command logs. No backup was overwritten.

Only the exact reviewed `docs/local-migration-up.sql` was applied, atomically. The ordinary
EF migration update command was not used. These five IDs are now appended correctly:

- `20261031164800_WeekendBoardNotes`
- `20261103080334_Phase6AssignmentModes`
- `20261103081004_Phase6Absences`
- `20261104135940_OperationalControls`
- `20261106074419_SchedulingPriority`

History contains 17 entries, with all original history rows preserved. All 51 definitions
retain every original field, including name, staffing, ordering, enablement, qualifications,
semantics, ranges, periods, version and timestamps. New modes are `Automatic`; new priorities
are `Normal`. `כונן סופש` remains `WEEKEND_ON_CALL`; `שיקום ערב` remains `ORDINARY`.

All existing records match the pre-upgrade fingerprints, including eight users, four teams,
one membership, two availability submissions, two windows, 16 preferences, two schedule
periods and 122 shifts. Eight new tables are empty. All 62 FK orphan checks pass; no invalid
indexes, unvalidated constraints or unexpected notes were found. These comparisons passed
both immediately after migration and after application startup/HTTP verification.

## Live stack and checks

`python3 scripts/local-docker.py up` completed successfully.

| Container | Status | Published endpoint |
| --- | --- | --- |
| `shiftflow-postgres-1` | Healthy | `127.0.0.1:5432` |
| `shiftflow-app-1` | Healthy | `127.0.0.1:5180` |

The original PostgreSQL container/image and external `shiftflow_postgres-data` volume
were retained. React and ASP.NET serve the same origin; the backend reaches PostgreSQL
through Docker DNS and its existing runtime credentials.

- `/`, `/login`, `/scheduling`, JavaScript and CSS assets return 200; entrypoints retain
  Hebrew and RTL markup.
- `/health/live` and `/health/ready` return 200/`Healthy`, including database connectivity.
- `/api/auth/csrf` returns a token with an HttpOnly, SameSite Strict development cookie.
- `/api/auth/me` and `/api/access/admin` reject anonymous requests with 401.
- Login without the required CSRF header returns 400/`CSRF_INVALID` before credential
  processing; no existing account is subjected to a failed password attempt.
- `python3 scripts/local-docker.py logs` succeeds and exposes application/PostgreSQL logs.
  Captured logs include successful startup/listening and no error-level app entries.

Real-account sign-in has not been independently confirmed without user credentials.
Browser automation was unavailable; frontend verification used the live HTTP routes/assets.
No account, password, security stamp or permission was changed for smoke testing.

## Preservation and normal use

Demo container state/data and existing backups are unchanged. No database reset, seeding,
volume deletion, push, merge or PR occurred. There are no upgrade/startup blockers.

```sh
python3 scripts/local-docker.py status
python3 scripts/local-docker.py logs
python3 scripts/local-docker.py stop  # retains all data
```

Normal startup remains `python3 scripts/local-docker.py up`. Do not reapply the compatibility
SQL: its baseline guard correctly rejects this already-upgraded database.
