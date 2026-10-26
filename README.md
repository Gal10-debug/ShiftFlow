# ShiftFlow

Hebrew-first workforce application. Phase 1 provides PostgreSQL/EF Core scheduling data;
Phase 2 provides cookie authentication, authorization, manager-controlled accounts and a
Hebrew RTL React interface. Phase 3 adds explicit monthly availability submissions,
administrator-controlled windows and cross-month weekly hard-block limits. Phase 4A adds an
unpublished weekly scheduling board and 51 configurable shift-type rows. Phase 4B adds monthly
CP-SAT automatic scheduling, staffing shortages, previews and explicit transactional draft saves.
Manual assignment editing, publication and task-allocation features remain deferred.

## Local setup

Requirements: .NET 10 SDK, a supported Node.js LTS (24 recommended; minimum 22.12), npm,
Docker Desktop and Bash (macOS/Linux). Start Docker Desktop first.

Preserve your existing `.env` and database password. On a fresh installation, create an
untracked root `.env` with `POSTGRES_PASSWORD=<your-local-password>`. For passwords with
special characters, use Compose dotenv quoting, for example `POSTGRES_PASSWORD='...'`.
Do not commit secrets. Changing this value does not change the password in an existing volume.

Run once after cloning, or when .NET/npm dependencies or the EF tool manifest change:

```sh
./scripts/dev.sh setup
```

This starts PostgreSQL and waits for health, restores .NET dependencies/tools, runs `npm ci`
and applies migrations. It does not create an administrator or start the application servers.
For new migrations without reinstalling tools or frontend dependencies:

```sh
./scripts/dev.sh migrate
```

Both commands preserve the existing PostgreSQL volume. Normal startup checks dependencies,
PostgreSQL authentication and applied migration IDs and points to setup/migrate when needed;
it never automatically installs tools, reinstalls npm packages or applies migrations.

## Start development

From the repository root, run one command:

```sh
./scripts/dev.sh
```

Open **http://127.0.0.1:5173**. The script starts the existing PostgreSQL Compose service,
waits for health, configures the backend database connection and launches ASP.NET Core and
Vite together. Backend: **http://localhost:5080**. Output from both servers appears in the
same terminal. Ctrl+C stops both servers and their child processes. If either server exits,
the other is stopped too and the command fails. PostgreSQL stays running so its data is
preserved. Stop it separately when desired with `docker compose stop postgres`.

The script works from any directory when invoked by its path, reports occupied ports and
missing configuration/dependencies, and uses Compose's dotenv parser without executing `.env`
as shell code. The root `.env` is authoritative for POSTGRES_PASSWORD, even if an older
value was exported in your terminal. The connection string is built only in memory and is
not passed to Vite. No credential files or application settings are generated. A password
mismatch with an existing database is reported; the script never deletes or resets its volume.
Do not run the script with shell tracing (`bash -x`) or print resolved Compose configuration,
which contains secrets. Migrations remain explicit, never applied at web-host startup.

Sign in using your existing administrator account. For a new database, bootstrap the first
administrator using the separate instructions below. Vite proxies `/api` to
`http://127.0.0.1:5080`; use the frontend origin consistently. Only Development permits HTTP
cookies. Production requires HTTPS and Secure cookies. CI and production startup are unchanged.

For a same-origin frontend build served by ASP.NET Core, stop the combined servers first,
build with `npm run build --prefix frontend`, then run the backend with the manually configured
local database connection described in the bootstrap section. Generated frontend assets go to
ignored `src/ShiftManager/wwwroot`. For deployment, build frontend assets before
`dotnet publish src/ShiftManager -c Release`. Configure `ConnectionStrings__Database` from
a secret store, persist data-protection keys in protected storage and terminate HTTPS at
a trusted proxy or Kestrel. Do not set Development in production. Forwarded-header trust
must be configured for the actual proxy; arbitrary proxy headers are not trusted. See
[authentication architecture](docs/authentication-architecture.md). Automated deployment
remains deferred.

## Bootstrap the first administrator

There is no registration or bootstrap HTTP endpoint. Run this explicit setup command once
against the migrated database. In an interactive Bash/Zsh terminal, read secrets without
putting them into command history:

```sh
# Run from the repository root in the same terminal as the bootstrap command.
set -a
. ./.env
set +a
export ConnectionStrings__Database="Host=localhost;Database=shiftmanager;Username=shiftmanager;Password=$POSTGRES_PASSWORD"
export BOOTSTRAP_ADMIN_USERNAME='choose-your-username'
export BOOTSTRAP_ADMIN_FIRST_NAME='שם'
export BOOTSTRAP_ADMIN_LAST_NAME='משפחה'
printf 'Temporary administrator password: '
read -rs BOOTSTRAP_ADMIN_PASSWORD
printf '\n'
export BOOTSTRAP_ADMIN_PASSWORD
dotnet run --project src/ShiftManager --no-launch-profile -- --bootstrap-admin
unset BOOTSTRAP_ADMIN_PASSWORD BOOTSTRAP_ADMIN_USERNAME BOOTSTRAP_ADMIN_FIRST_NAME BOOTSTRAP_ADMIN_LAST_NAME
```

Choose a unique temporary password of 12–128 characters with an uppercase and lowercase
Latin letter, digit and special character. No default credentials exist. The command
requires all four settings, uses Identity hashing, is transactional and safe to rerun for
the same active administrator. It does not replace an existing password or elevate an
existing non-admin account. Once an active administrator exists, use the authenticated
user-management screen for additional administrators. First login requires changing the
temporary password. Passwords must be delivered to employees through a separate secure
organizational channel; no email recovery or password delivery is implemented here.

## Availability and preferences

Employees use **הזמינות שלי** (`/employee` or `/availability`) to select day/night availability.
Calendar edits stay local until **הגש זמינות**; subsequent **עדכן זמינות** actions replace the
entire monthly submission. An all-Available month still counts as submitted. Submitted data
and unsaved edits have separate indicators; refreshing reloads the last successful submission.

Administrators use **ניהול זמינות** (`/availability-admin`) to configure a monthly window,
explicitly open/close it and review active employees' submission status and read-only
preferences. Saving dates does not reopen a closed window. Reopening preserves submissions
and permits updates until the new deadline. Only active EMPLOYEE role holders may submit;
coordinators have no additional availability permissions.

Available is the default, Preferred is a soft request, and Blocked is a hard constraint.
Each employee may block at most three shifts per Sunday–Saturday week in Asia/Jerusalem,
counting across month/year boundaries. The server enforces the complete replacement in
one locked transaction. PostgreSQL also prevents assigning a blocked employee to that shift.
The UI and window times use Israel time; actual timestamps are stored in UTC.

After pulling this phase, apply the additive migration before starting the app:

```sh
./scripts/dev.sh migrate
```

The script configures the local database connection. No new secrets or CI settings
are required. See [availability architecture](docs/availability-architecture.md) for window
states, API routes, schema/backfill assumptions, locking, Phase 4 contracts, limitations
and a step-by-step manual test. The existing CI reproduction commands run all new tests.

## Weekly scheduling board and automatic drafts (Phases 4A–4B)

Administrators open **סידור עבודה** (`/scheduling`) for a Sunday–Saturday board with exactly
51 ordered shift-type rows and Hebrew weekday/date headers. Week navigation, a current-week
shortcut, sticky headers/labels and two-axis scrolling support desktop/tablet and narrow screens.
The board is explicitly unpublished and shows saved assignments or clearly marked unsaved previews.

Open **הגדרות סוגי משמרות** to rename placeholders, change display order, classify each row
with a time range, configure 0–1000 required employees and select automatic participation.
Save explicitly; edits remain local until saved. Swap order values as a complete unique
permutation of 1–51. Zero means **ללא צורך באיוש** and is excluded from automatic runs,
even if its inclusion flag is selected. No fixed five-employees rule applies to these rows.

Time ranges include the existing 07:00–19:00 and 19:00–07:00, plus **06:00–09:00**,
**07:00–13:00**, **07:00–15:00**, **15:00–23:00** and **23:00–07:00**. Overnight ranges end
on the following date. All use Israel local time with UTC timestamps and DST handling.
The `ShiftTypeTimeRanges` migration preserves existing names, ordering and Day/Night choices.

Apply the additive migration with `./scripts/dev.sh migrate`. It inserts `משמרת 1` through
`משמרת 51` once, initially DAY with zero staffing and automatic participation off. Restarting
or rerunning migrations preserves administrator edits. Configuration saves never change
existing availability, time shifts or employee assignments. No new CI settings are required.

The administrator API provides GET `/api/admin/scheduling/board`, GET/PUT
`/api/admin/scheduling/shift-types` and GET `/api/admin/scheduling/automatic-configuration`.
See [scheduling architecture](docs/scheduling-architecture.md) for the schema, UTC/DST and
monthly mapping, concurrency, the transition from legacy staffing, the Phase 4B engine,
limitations and manual verification steps.

Choose a month, review saved requirements, and click **צור סידור אוטומטי**. The server runs
OR-Tools CP-SAT in a bounded background queue; the board remains usable. Review names,
shortage labels and summary, then cancel or explicitly confirm **אישור ושמירת טיוטה**.
Previews expire after 30 minutes and never write assignments. Drafts remain administrator-only.
Changed inputs invalidate a preview. Excluded rows and protected assignments remain intact.
Set `AutomaticScheduling:SolverSeconds` (environment `AutomaticScheduling__SolverSeconds`)
to configure the shared optimization time limit (default 30 seconds). Pending lead selection
means generated drafts are not publication-ready. Apply the `AutomaticScheduling` migration
before running the updated app. See the architecture document for API routes and limitations.

## Authentication and API

Identity application cookies are HttpOnly and SameSite=Strict, with an eight-hour ticket
and no sliding renewal. Every authenticated request checks current active status and the
security stamp. Password resets, role/status changes and profile edits revoke existing
cookies; password changes refresh only the current session. Public registration is absent.

For unsafe requests, first GET `/api/auth/csrf`, keep `requestToken` only in memory, then
send it in `X-CSRF-TOKEN` with same-origin cookies. Fetch a new token after login/logout or
password change. Login itself requires CSRF protection. No auth token uses browser storage.
Errors are `{ code, message }` with stable English codes and Hebrew messages.

| Method | Endpoint | Access |
| --- | --- | --- |
| GET | `/api/auth/csrf` | Anonymous or signed in |
| POST | `/api/auth/login` | Anonymous, CSRF, IP rate limit, account lockout |
| POST | `/api/auth/logout` | Signed in, CSRF |
| GET | `/api/auth/me` | Signed in, including temporary-password sessions |
| POST | `/api/auth/change-password` | Signed in, current password and CSRF |
| GET | `/api/users?page=1&pageSize=20&search=...` | ADMIN after required password change |
| GET | `/api/users/{id}` | ADMIN |
| POST | `/api/users` | ADMIN, temporary password, roles and status |
| PUT | `/api/users/{id}` | ADMIN, first/last name and username |
| PATCH | `/api/users/{id}/status` | ADMIN, explicit `isActive` |
| PUT | `/api/users/{id}/roles` | ADMIN, one or more allowed roles |
| POST | `/api/users/{id}/reset-password` | ADMIN, another user's temporary password |
| GET | `/api/access/admin`, `/api/access/coordinator`, `/api/access/employee` | Respective role after password change |

All unsafe API routes validate antiforgery tokens. User DTOs never include password hashes,
security stamps or reset tokens. Profile/role/status edits use separate typed endpoints;
there is no mass assignment of Identity fields. Directory pagination is capped at 100 rows.
Development-only OpenAPI JSON is at `/openapi/v1.json`.

## Builds and tests

Validate the local startup orchestration without touching Docker or your development database:

```sh
bash -n scripts/dev.sh
python3 scripts/test-dev.py
```

The startup tests use temporary projects and fake command-line tools to exercise failure,
setup/migration separation, secret handling and process-group shutdown (Python 3 required
only for these tests).


```sh
dotnet build
dotnet test                              # model/clock tests; database tests explicitly skip
RUN_POSTGRES_TESTS=1 dotnet test          # full PostgreSQL + real-cookie HTTP integration tests
npm run build --prefix frontend
npm test --prefix frontend
npm audit --prefix frontend
dotnet list package --vulnerable --include-transitive
```

Docker is required for PostgreSQL integration tests. By default they migrate fresh disposable
Testcontainers databases and never use the development database. On macOS, if Testcontainers cannot discover Docker:

```sh
export DOCKER_HOST="unix://$HOME/.docker/run/docker.sock"
export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
```

## Continuous integration

`.github/workflows/ci.yml` runs on pull requests targeting `main` and pushes to `main`.
The independent **Backend** and **Frontend** checks build and test the application:

- Backend: .NET 10, Release build with warnings as errors, PostgreSQL 17 integration tests,
  application of migrations to a clean database, and EF pending-model-change verification.
- Frontend: Node.js 24 LTS, cached npm downloads, `npm ci`, all Vitest tests in run mode,
  TypeScript validation, production build, and `npm audit --audit-level=high` (high/critical
  findings or an unavailable audit service fail the check).

Actions use immutable commit pins from the official repositories. Workflow permissions are
limited to reading contents; checkout credentials are not persisted. Superseded runs are
cancelled, and jobs have timeouts. No repository secrets or administrator bootstrap settings
are needed. Service credentials in the workflow are temporary and public by design.

View results in the repository's **Actions → CI** or the pull request's **Checks** tab.
Local reproduction from the repository root (.NET 10, Node.js 24, npm and Docker required):

```sh
docker run -d --name shiftflow-ci-local \
  -e POSTGRES_DB=shiftflow_ci -e POSTGRES_USER=shiftflow_ci \
  -e POSTGRES_PASSWORD=ci-only-disposable \
  -p 127.0.0.1:55432:5432 \
  --health-cmd 'pg_isready -U shiftflow_ci -d shiftflow_ci' \
  --health-interval 5s --health-timeout 5s --health-retries 12 postgres:17-alpine
# Wait for "healthy" before proceeding; stop if the container exits or remains unhealthy.
docker inspect --format '{{.State.Health.Status}}' shiftflow-ci-local

export CI=true RUN_POSTGRES_TESTS=1 ASPNETCORE_ENVIRONMENT=CI
export TEST_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=55432;Database=shiftflow_ci;Username=shiftflow_ci;Password=ci-only-disposable'
export ConnectionStrings__Database="$TEST_POSTGRES_CONNECTION_STRING"
dotnet restore ShiftManager.slnx
dotnet tool restore
dotnet build ShiftManager.slnx -c Release --no-restore --warnaserror
dotnet ef database update --project src/ShiftManager --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/ShiftManager --configuration Release --no-build
dotnet test ShiftManager.slnx -c Release --no-build --no-restore
npm ci --prefix frontend
npm test --prefix frontend
npm run build --prefix frontend
npm audit --prefix frontend --audit-level=high

# Cleanup even when a check fails:
docker rm -f -v shiftflow-ci-local
unset CI RUN_POSTGRES_TESTS ASPNETCORE_ENVIRONMENT TEST_POSTGRES_CONNECTION_STRING ConnectionStrings__Database
```

The service connection must use a loopback host and the disposable `shiftflow_ci` database.
Tests create and drop a unique `shiftflow_test_*` database per test, preserving isolation
for parallel authentication tests and migration rollback tests. The service role needs
permission to create/drop databases. CI fails if this connection is absent; local runs
without it retain the existing Testcontainers path. Never point it at a development or
production database. The separate `shiftflow_ci` database verifies the EF CLI migration path.
Run `actionlint .github/workflows/ci.yml` locally if actionlint is installed.

After the first GitHub run, configure **Settings → Rules → Rulesets** (or branch protection)
for `main`: require a pull request, require the **Backend** and **Frontend** status checks,
require branches to be up to date, and block force pushes and branch deletion. Select the
actual check names shown by GitHub. Enable GitHub Actions and allow the pinned official
`actions/checkout`, `actions/setup-dotnet`, and `actions/setup-node` actions under repository
or organization policy. No deployment permissions or secrets are required.

CI validates changes. CD delivers or deploys builds to an environment. Automated production
deployment is intentionally deferred until a production environment exists.

## Structure and conventions

- `src/ShiftManager/Domain`, `Data/Configurations`, `Data/Migrations`: preserved Phase 1 data
  model plus `ApplicationUser.MustChangePassword` and its additive migration.
- `src/ShiftManager/Api`: typed controllers/contracts and centralized Hebrew API errors.
- `src/ShiftManager/Security`, `Services`: cookie/claims validation, account transactions and bootstrap.
- `frontend/src`: React Router shell, login, first-password, profile and user-management screens,
  small fetch client and centralized Hebrew resources; no global state library.
- `tests/ShiftManager.Tests`: model, DST, PostgreSQL, authentication/security, availability and scheduling/configuration concurrency tests.
- [Database architecture](docs/database-architecture.md),
  [authentication architecture](docs/authentication-architecture.md),
  [availability architecture](docs/availability-architecture.md),
  [scheduling architecture](docs/scheduling-architecture.md).

Hebrew is the MVP language: `lang="he"`, `dir="rtl"`, logical CSS, LTR password inputs,
centralized visible text, he-IL date formatting as DD/MM/YYYY, 24-hour times, Israel-local
Sunday–Saturday weeks and UTC persisted instants. Availability and the unpublished scheduling workspace are implemented; assignment generation,
publication and task execution remain deferred.
