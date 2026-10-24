# ShiftFlow

Hebrew-first workforce application. Phase 1 provides PostgreSQL/EF Core scheduling data;
Phase 2 provides cookie authentication, authorization, manager-controlled accounts and a
Hebrew RTL React interface. Availability, scheduling and task-allocation features are deferred.

## Local setup

Requirements: .NET 10 SDK, Node.js 22.12+ (or newer supported LTS), npm and Docker Desktop.
Preserve your existing `.env` and database password. For a fresh installation, create an
untracked `.env` containing `POSTGRES_PASSWORD=your-local-password` before Compose startup.
Do not commit secrets. Changing this value does not change the password in an existing volume.

```sh
# From the repository root; .env must contain shell-compatible assignments.
set -a
. ./.env
set +a
export ConnectionStrings__Database="Host=localhost;Database=shiftmanager;Username=shiftmanager;Password=$POSTGRES_PASSWORD"
docker compose up -d --wait
dotnet tool restore
dotnet restore
dotnet ef database update --project src/ShiftManager
npm ci --prefix frontend
```

Compose reads `.env` automatically; .NET and Vite do not load the root file. Loading it in
your shell supplies the password for the connection string. An exported shell variable
can override Compose's `.env` value. The existing PostgreSQL Compose service is preserved.
Migrations are explicitly applied, never automatically run at web-host startup.

## Bootstrap the first administrator

There is no registration or bootstrap HTTP endpoint. Run this explicit setup command once
against the migrated database. In an interactive Bash/Zsh terminal, read secrets without
putting them into command history:

```sh
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

## Start development

Keep the database connection environment from setup in the backend terminal:

```sh
dotnet run --project src/ShiftManager --launch-profile http
```

In a second terminal:

```sh
npm run dev --prefix frontend
```

Open **http://127.0.0.1:5173** and sign in using your bootstrap credentials, then change the
temporary password. Vite proxies `/api` to `http://127.0.0.1:5080`; use the frontend origin
consistently. There is no cross-origin credential/CORS configuration to maintain.
Only Development permits HTTP cookies. Production requires HTTPS and Secure cookies.

For a same-origin build served by ASP.NET Core:

```sh
npm run build --prefix frontend
dotnet run --project src/ShiftManager --launch-profile http
# Open http://localhost:5080
```

The frontend build writes ignored generated assets to `src/ShiftManager/wwwroot`.
For deployment, build frontend assets before `dotnet publish src/ShiftManager -c Release`.
Use a supported Node LTS for builds, configure `ConnectionStrings__Database` from a secret
store, persist ASP.NET Core data-protection keys in protected storage, and terminate HTTPS
at a correctly configured trusted proxy or Kestrel. Do not set Development in production.
Forwarded-header trust must be configured for the actual proxy; no arbitrary proxy headers
are trusted. See [authentication architecture](docs/authentication-architecture.md).

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

```sh
dotnet build
dotnet test                              # model/clock tests; database tests explicitly skip
RUN_POSTGRES_TESTS=1 dotnet test          # full PostgreSQL + real-cookie HTTP integration tests
npm run build --prefix frontend
npm test --prefix frontend
npm audit --prefix frontend
dotnet list package --vulnerable --include-transitive
```

Docker is required for PostgreSQL integration tests. They migrate fresh disposable databases
and never use the development database. On macOS, if Testcontainers cannot discover Docker:

```sh
export DOCKER_HOST="unix://$HOME/.docker/run/docker.sock"
export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
```

## Structure and conventions

- `src/ShiftManager/Domain`, `Data/Configurations`, `Data/Migrations`: preserved Phase 1 data
  model plus `ApplicationUser.MustChangePassword` and its additive migration.
- `src/ShiftManager/Api`: typed controllers/contracts and centralized Hebrew API errors.
- `src/ShiftManager/Security`, `Services`: cookie/claims validation, account transactions and bootstrap.
- `frontend/src`: React Router shell, login, first-password, profile and user-management screens,
  small fetch client and centralized Hebrew resources; no global state library.
- `tests/ShiftManager.Tests`: model, DST, PostgreSQL and authentication/security tests.
- [Database architecture](docs/database-architecture.md),
  [authentication architecture](docs/authentication-architecture.md).

Hebrew is the MVP language: `lang="he"`, `dir="rtl"`, logical CSS, LTR password inputs,
centralized visible text, he-IL date formatting as DD/MM/YYYY, 24-hour times, Israel-local
Sunday–Saturday weeks and UTC persisted instants. No availability/schedule/task UI is present.
