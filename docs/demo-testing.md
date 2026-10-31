# Development demo and workflow verification

The demo CLI has no HTTP endpoint. It accepts only Development or Testing, a loopback
PostgreSQL host, and the exact database name `shiftflow_demo_YYYYMM_SEED`. Negative
seeds use `n` instead of `-` in database names. It never uses the normal development
connection string. A matching name alone is insufficient: the initial database must
be empty. A durable ownership marker records the generator version, month, seed,
completion state, and content fingerprint. Existing databases, accounts, teams,
schedules, and configuration are never adopted or overwritten.

## Prerequisites and isolated database

Use .NET 10, Node 22.12+ (24 recommended), npm, Docker Desktop and PostgreSQL 17.
Restore dependencies with `dotnet restore` and `npm ci --prefix frontend`.
Create an ignored `.env.demo` file (mode 600) containing a random password:

```dotenv
DEMO_POSTGRES_PASSWORD=replace-with-a-long-random-local-password
DEMO_DATABASE=shiftflow_demo_202611_42
```

Start the dedicated container; this file does not reference the application's normal
PostgreSQL service or volume:

```sh
docker compose --project-name shiftflow-demo --env-file .env.demo -f compose.demo.yaml up -d --wait
export ASPNETCORE_ENVIRONMENT=Development
# Replace the password locally; use Npgsql quoting if it contains delimiters.
export SHIFTFLOW_DEMO_CONNECTION='Host=127.0.0.1;Port=55432;Database=shiftflow_demo_202611_42;Username=shiftflow_demo;Password=your-local-password;Pooling=false'
./scripts/dev.sh seed-demo --month 2026-11 --seed 42
```

The CLI applies existing migrations only to that initially empty database. No
application migration was changed. For another scenario use another empty database
and its corresponding month/seed name. Never point a demo command at shared data.

## Accounts and availability

Exactly 60 accounts are generated: four organizational leaders, four deputies and
52 ordinary employees, 15 members in each of the four migrated teams. Every member
has the EMPLOYEE role; `demo.01` additionally has ADMIN. Leaders are excluded by
the existing scheduling rules, leaving 56 eligible employees. Fourteen eligible
accounts have independent shift-lead qualifications. Deputies can be qualified or
unqualified; organizational leadership does not imply expert qualification.

The CLI writes `.demo/shiftflow_demo_202611_42/accounts.json`. It contains clearly
synthetic names, usernames, team slots, leadership positions, qualifications, roles,
and random per-account passwords. The directory is private (700), the credentials
file is private (600) on Unix, and `.demo/` is ignored by Git. Do not share the file.
On Windows protect the directory with local ACLs. Passwords are generated with a
cryptographic RNG, independently of the seed; their hashes use normal Identity.
These CLI-created, isolated accounts can log in normally without a bootstrap
password reset. Authentication and authorization rules are unchanged.

| Representative | Login | Position |
| --- | --- | --- |
| Administrator | `demo.01` | Also team 1 leader |
| Team leader | `demo.16` | Team 2 leader |
| Deputy | `demo.02` | Team 1 deputy, scheduling eligible |
| Employee | `demo.03` | Team 1 ordinary employee |

Read each password from the local directory. Log out before switching accounts.
Never use these accounts or credentials outside this database.

Availability is submitted through `AvailabilityService.Submit`, with real account
roles, security stamps and normal validation. Leaders have no submissions. There
are 52 submitted users and four eligible non-submitters. Day, night, mixed and
sparse preferences are included. Absolute Sunday–Saturday week keys select zero,
one, two or three hard-block anchors per week, consistently across adjacent
months. Time boundaries use the existing Asia/Jerusalem domain clock. Unselected
anchors remain available under current business rules; sparse preferences are
not a new mechanism for unlimited hard unavailability.

The same month/seed produces the same account IDs, membership, qualifications and
availability choices. Repeating a completed seed changes neither database records
nor credentials, even after subsequent manual edits. Generated timestamps,
passwords and solver output are not claimed to be byte-for-byte deterministic.
A partially failed seed is preserved and refused on retry rather than overwritten.

## Browser workflow

Start the backend with the explicit isolated connection, then Vite in another terminal:

```sh
export ConnectionStrings__Database="$SHIFTFLOW_DEMO_CONNECTION"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/ShiftManager --launch-profile http
npm run dev --prefix frontend
```

Open `http://127.0.0.1:5173`. For this workflow do not run plain `dev.sh start`, which
intentionally selects the normal development database.

Eight existing rows are enabled: the semantic SIX_NINE and WEEKEND_ON_CALL rows
plus the first six ordinary rows. Each requires one employee, one row is NIGHT,
and one requires shift-lead qualification. The remaining 43 rows are disabled only
in the isolated database. No row is duplicated, including the 51 migrated IDs.
On-call has zero required staffing on weekdays under the existing business rule.

1. Log in as administrator and select November 2026 in the scheduling board.
2. Generate an automatic preview, inspect coverage and shortages, then save the draft.
3. Inspect monthly/weekly board views and candidate explanations. Make a valid manual
   assignment correction and acknowledge review. Use the pair editor for on-call.
4. Publish PRELIMINARY, explicitly acknowledging any shortages.
5. Log out and log in as an assigned employee; view the published schedule and report
   an issue about one of that employee's assigned shifts.
6. Log in as administrator, review the issue, enter correction mode, correct the draft,
   resolve the issue and review again.
7. Publish FINAL and inspect publication history. Earlier snapshots must remain unchanged.

Seeding itself never runs the scheduler or publishes a schedule.

## Automated verification and reports

A freshly seeded database can run the service-level smoke workflow only in Testing:

```sh
ASPNETCORE_ENVIRONMENT=Testing ./scripts/dev.sh smoke-demo --month 2026-11 --seed 42
```

Stop the browser backend first. The smoke command refuses an already edited or
published database. It checks demo passwords with Identity, then uses existing
business services with validated role/security-stamp principals. HTTP login, CSRF
and endpoint authorization remain covered by the existing API integration suite.
The smoke command generates and validates an automatic preview, saves through
GeneratedDraftService, makes audited manual edits, publishes preliminary, accesses
it as an employee, reports/reviews/resolves an issue, enters correction mode,
corrects the draft, publishes final, and checks two immutable publication versions.
It acknowledges shortages explicitly, never silently treats them as full staffing.

Artifacts in `.demo/<database>/`:

- `seed-report.json`: account/team/submission counts; scheduling and publication say
  “Not run” until they actually run.
- `verification-report.json`: success/failure, completed workflow steps, account and
  submission counts, assignments, shortages by date/row, monthly weekend allocation
  counts, Sunday weekly night and SIX_NINE distributions, on-call pair validation,
  roster conflicts and publication version history. Failure exits nonzero and does
  not claim success. Fingerprints are updated only after a successful smoke run.

Saved-roster verification uses the shared Phase 5.1 rules and publication validator:
weekend allocation maximum two (a Friday/Saturday pair counts once in Friday's month),
SIX_NINE maximum one per Sunday week, nights maximum two per Sunday week, on-call
weekend-only with identical paired membership. The existing constraint tests exercise
cross-month weeks/pairs, two allowed pairs and prohibited third allocations. Existing
optimizer tests cover expert-row priority, shortages, hard blocks and consecutive
work; the smoke report checks the generated roster, not every possible rejection case.

Run all automated checks:

```sh
dotnet test ShiftManager.slnx
RUN_POSTGRES_TESTS=1 dotnet test ShiftManager.slnx
npm test --prefix frontend
npm run build --prefix frontend
dotnet build ShiftManager.slnx -c Release
python3 scripts/test-dev.py
```

PostgreSQL tests use disposable Testcontainers databases; optionally set
`TEST_POSTGRES_CONNECTION_STRING` to the existing supported loopback `shiftflow_ci`
service. New demo integration tests create a separately named empty database, seed
it twice, verify population and availability, execute the full smoke workflow, and
verify that foreign data prevents cleanup and remains intact. The successful smoke
report is retained as `tests/ShiftManager.Tests/bin/Debug/net10.0/TestResults/demo-verification.json`
(the test runner working directory), without account passwords.

## Cleanup and repeatability

```sh
./scripts/dev.sh clean-demo --month 2026-11 --seed 42 --confirm shiftflow_demo_202611_42
```

The database name is an explicit destructive confirmation. Cleanup verifies the
non-production environment, exact target, ownership marker and full table-content
fingerprint. It drops only the entire verified isolated demo database, without FORCE,
without disabling foreign keys and without weakening immutable-publication triggers.
Active database connections prevent deletion. Account credentials are removed;
reports remain locally. Recreate an empty database to start the scenario again.

**Conservative limitation:** manual changes, new records, login-driven database
updates or a failed/partial smoke run can invalidate the last verified fingerprint.
Cleanup then refuses, even if changes appear synthetic. Preserve that database for
inspection and use a new empty scenario; do not bypass ownership verification or
reset the fingerprint. Automatic cleanup after arbitrary manual workflows is not
supported. The automated successful smoke scenario updates its fingerprint and can
be cleaned normally. Partial seed recovery is similarly conservative.

The default November scenario does not itself contain an on-call pair crossing a
calendar-month boundary. Cross-month behavior is tested by the dedicated constraint
suite. The seed is not a promise of optimal/full staffing; inspect the actual report.

If Docker is unavailable, PostgreSQL tests cannot execute and are not successful.
For a closed availability window, choose a new fresh scenario or use the normal
administrator window controls; seeding does not overwrite a completed scenario.
For CLI rejection check environment, database name, seed, month and loopback host.
For occupied port 55432 choose a dedicated unused loopback port in the compose file
and connection string. Never change the normal database to resolve a demo error.
