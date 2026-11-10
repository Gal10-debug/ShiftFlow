# Full local Docker environment

Run from the repository root with Docker Desktop, Docker Compose v2 and Python 3.10+
installed. Node and .NET SDKs are needed only for host development, tests and explicitly
approved migrations; the application image builds with Node 24 and .NET 10 itself.

```sh
python3 scripts/local-docker.py config   # validate without displaying secrets
python3 scripts/local-docker.py verify   # read-only identity/schema check
python3 scripts/local-docker.py up       # verify, build, start and wait for health
```

Open **http://127.0.0.1:5180** and sign in with an existing regular development account.
Startup always builds the current application. To build without starting services:

```sh
python3 scripts/local-docker.py build
```

Routine operations:

```sh
python3 scripts/local-docker.py status
python3 scripts/local-docker.py logs
python3 scripts/local-docker.py stop
```

`stop` stops the local app and regular PostgreSQL container, retaining both database and
cookie key volumes. It does not remove containers, networks or volumes. To change the HTTP
port, use the same setting for subsequent commands, for example:

```sh
LOCAL_HTTP_PORT=5181 python3 scripts/local-docker.py up
```

## Architecture

The wrapper resolves `compose.yaml` plus `compose.local.yaml` with the fixed project name
`shiftflow`. The `postgres` service describes the existing regular database container;
the wrapper starts that exact container with `docker start` when necessary. It never lets
Compose recreate PostgreSQL or apply the base file's newer database image.

The `app` service reuses `deploy/pilot/Dockerfile` without enabling pilot mode. Its build
compiles the React/Vite frontend and publishes ASP.NET Core .NET 10. ASP.NET serves the
compiled frontend and `/api` together on container port 8080. No separate frontend server
or proxy is necessary. Browser requests use the same loopback origin; database requests
use Docker DNS `postgres:5432`. Hebrew RTL, authorization, the scheduling engine and
cookie/CSRF handling are unchanged. The existing runtime includes OR-Tools dependencies.

Only `127.0.0.1:5180` is published for the app; the existing regular PostgreSQL loopback
port is 5432. Do not expose this Development-mode HTTP configuration publicly. Production
uses its existing HTTPS pilot configuration independently.

The app runs as user 1654 with a read-only root filesystem and writable temporary memory.
`shiftflow_local-keys` persists ASP.NET data-protection keys beneath
`/var/lib/shiftflow/keys`, allowing authentication cookies to survive app recreation.
`LocalDocker__KeysDirectory` explicitly selects this path in Development only; host and
Production configuration keep their existing behavior. The image prepares this mount for the app user. These
local keys are not shared with host development, demo or pilot sessions.

`/health/live` checks the web process, `/health/ready` performs a database read, and the
image's .NET health probe needs no shell or curl. The Compose dependency waits for
PostgreSQL health; the wrapper additionally checks authenticated connectivity and migration
history, then starts only `app` with `--no-deps --wait`.

## Database safeguards

The database is **`shiftmanager`**, owned by **`shiftmanager`**, in the pre-existing external
volume **`shiftflow_postgres-data`**. A missing volume or container is an error. No empty
replacement is created. The wrapper checks container and volume Compose ownership, checkout
path, exact mount, network ownership, exclusive volume usage, existing PostgreSQL image,
and `.env` agreement before any startup. It rejects demo targets and unknown migrations.

For a running database, inspection uses a `BEGIN READ ONLY` transaction. For a stopped
database, the wrapper checks PostgreSQL 17's clean shutdown metadata, mounts the original
volume **read-only**, and copies its files into a disposable container's 512 MiB RAM
filesystem. Only that copy is started, without networking, to inspect database ownership,
application schema and migration history. The original container stays stopped during
inspection. The copy is removed automatically on exit. If the database outgrows the RAM
limit or is not cleanly shut down, verification fails; arrange a separately reviewed
inspection rather than bypassing the guard.

No startup path runs migrations, demo commands, bootstrap, cleanup or database write commands.
After startup, normal user actions can of course change application data. Do not run write
smoke tests against this database. Database image upgrades remain separate approved
maintenance, even if the base Compose file has a newer image.

Use the wrapper for this workflow. A bare `docker compose up` only describes PostgreSQL
and may apply the base file's newer image. Directly combining the two files intentionally
requires internal variables supplied by the wrapper; do not bypass verification by setting
them manually. Never use `down -v`, volume pruning/removal, resets or demo seeding on the
regular environment.

## Environment

Preserve the existing ignored root `.env`; it must contain the password already used by
the regular database:

```dotenv
# Illustration only; do not replace an existing .env or its password.
POSTGRES_PASSWORD='<existing-regular-development-password>'
```

Compose parses dotenv quoting; the wrapper never sources shell code. The `.env` password
takes precedence over an exported `POSTGRES_PASSWORD` and must match the existing container.
`LOCAL_HTTP_PORT` is optional (default 5180). No new secret file is needed.

The wrapper supplies `LOCAL_POSTGRES_IMAGE` from the verified container's immutable image
ID and constructs `LOCAL_DATABASE_CONNECTION` in memory with quoted Npgsql values.
Compose passes it as `ConnectionStrings__Database` only to the runtime app. Secrets are
not build arguments or image content; `.dockerignore` excludes `.env`, development settings,
private keys and generated files. Docker administrators can inspect runtime environment
variables, so do not share raw `docker inspect` or resolved `compose config` output.

## Explicit migrations

Pending migrations block `verify` and `up` **before** building or starting persistent
services. Review the listed migrations and obtain the database owner's approval first.
Take a verified backup and review the existing migration procedure before applying them.
Neither this task nor normal local Docker startup grants migration permission.

After approval, preserve the existing PostgreSQL container and image:

1. Start that verified container with `docker start shiftflow-postgres-1`.
2. Load the connection into the current shell without printing credentials:

   ```sh
   export ConnectionStrings__Database="$(
     unset POSTGRES_PASSWORD
     docker compose --project-name shiftflow --env-file .env -f compose.yaml config --format json |
       node scripts/dev-config.mjs
   )"
   dotnet tool restore
   dotnet ef database update --project src/ShiftManager
   unset ConnectionStrings__Database
   ```

3. Rerun `python3 scripts/local-docker.py verify`, then `up`.

Do not use `scripts/dev.sh setup` or `migrate` for this maintenance until its ordinary
Compose startup's database image change has also been reviewed. Those host-development
commands call `compose up postgres`, whereas this wrapper preserves the existing image.

## Environments and troubleshooting

| Workflow | App origin | Database/persistence |
| --- | --- | --- |
| Full local Docker | `http://127.0.0.1:5180` | Existing `shiftmanager`, `shiftflow_postgres-data` |
| Host development (`scripts/dev.sh`) | `http://127.0.0.1:5173` | Same regular database; coordinate use and migrations |
| Demo (`compose.demo.yaml`) | See demo instructions | Separate `shiftflow-demo` project, credentials and database |
| Pilot (`deploy/pilot/compose.yaml`) | Configured HTTPS origin | Independent deployment identity, database, secrets and networks |

- **Pending migrations:** follow the approval procedure above; do not reset the database.
- **Ownership/mount/network mismatch:** stop and inspect metadata. Do not rename or replace
  resources to make validation pass. A different checkout path requires reviewed ownership.
- **Password mismatch:** restore the correct existing local credential; changing `.env`
  does not change a persisted PostgreSQL password. No credentials are printed by the guard.
- **Port occupied:** select an unused `LOCAL_HTTP_PORT`. Stop a competing regular PostgreSQL
  process before starting the verified container; do not change demo ports or containers.
- **Unhealthy app:** inspect `logs`; check database authentication, schema and Docker DNS.
- **Sign-in after switching environments:** use the documented origin consistently; cookie
  keys are separate between host and container workflows. Sign in again as needed.
- **ARM/macOS:** the existing multi-architecture Dockerfile selects Linux runtime/native
  dependencies; avoid copying host `bin`, `obj` or `node_modules` into the image.

## Validation

```sh
python3 scripts/test-local-docker.py
python3 scripts/local-docker.py config
python3 scripts/local-docker.py build
```

Safety tests cover ownership, shared-volume rejection, password handling, pending/future
migrations, and the read-only snapshot. Integration tests must use disposable databases;
never point a test fixture at the regular or demo database.

To exercise the built container and optionally the backend correctness suite, install the
host .NET 10 SDK and EF tool, then run:

```sh
dotnet tool restore
dotnet build ShiftManager.slnx -c Release --warnaserror
python3 scripts/test-local-docker-runtime.py --backend-tests
```

The runtime probe overrides the Compose project, ports and all mounts. It creates a fresh
RAM-only PostgreSQL database, explicitly migrates and bootstraps synthetic credentials
there, then checks frontend/SPA assets, RTL, health, Docker DNS, cookie authentication,
password changes, authorization, CSRF, key storage, login across app recreation and native
OR-Tools generation. Synthetic keys use a private temporary host directory that is deleted
after testing. The probe removes its disposable containers and network. No regular or demo volume
is mounted. The optional backend suite also uses fresh databases in that disposable server.

During implementation on 2026-11-10, the stopped regular volume was verified using its
existing image and a RAM copy. It contains eight accounts and 12 applied migrations,
ending at `20261027224058_AdditionalSchedulingConstraints`. Five migrations are pending:

- `20261031164800_WeekendBoardNotes`
- `20261103080334_Phase6AssignmentModes`
- `20261103081004_Phase6Absences`
- `20261104135940_OperationalControls`
- `20261106074419_SchedulingPriority`

They were not applied. Regular-stack startup remains blocked until approved maintenance.

Validation completed: Compose configuration and image build; frontend production build
and 155 tests; backend Release build with zero warnings; 491 backend correctness tests
against disposable databases, with none skipped; 11 local Docker safety tests; seven
existing host-development script tests; and the disposable container runtime probe above.
The real `up` command was checked to refuse the pending migrations. The regular stack
was not started, no persistent services were restarted, and demo resources were untouched.
