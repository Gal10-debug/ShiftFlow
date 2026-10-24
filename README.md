# ShiftManager

Phase 1: .NET 10 / EF Core / PostgreSQL database and ASP.NET Core Identity foundation.
There are no frontend components, controllers, registration or authentication endpoints.

## Setup

Install .NET 10 SDK and Docker Desktop. From this directory:

```sh
dotnet tool restore
dotnet restore
dotnet build
# First-time setup: copy the example, then edit .env to choose your local password.
# Skip the copy if .env already exists.
cp -n .env.example .env
# Load the same password for Compose and the .NET connection string.
set -a
. ./.env
set +a
docker compose up -d --wait
export ConnectionStrings__Database="Host=localhost;Database=shiftmanager;Username=shiftmanager;Password=$POSTGRES_PASSWORD"
dotnet ef database update --project src/ShiftManager
dotnet run --project src/ShiftManager
```

Compose automatically reads `.env` from the repository root. The .NET host does not,
so load it into your terminal as shown above before migrations or startup. `.env` is
ignored by Git. If Compose reports that `POSTGRES_PASSWORD` is missing, create `.env`
from `.env.example` and set the password. An exported shell variable overrides `.env`;
use `unset POSTGRES_PASSWORD` if an empty or outdated shell value is overriding it.
For an existing PostgreSQL volume, use its original password: changing `.env` does not
change the database account's password.

The host intentionally exposes no application endpoints. Migrations are applied explicitly,
not automatically at application startup. Roles ADMIN, COORDINATOR and EMPLOYEE are seeded;
no accounts or passwords are seeded. Identity handles hashing and security stamps.

## Verification

```sh
dotnet test                            # model/clock tests; PostgreSQL tests explicitly skipped
RUN_POSTGRES_TESTS=1 dotnet test         # also starts disposable PostgreSQL using Testcontainers
```

Integration tests require a running Docker daemon and access to `postgres:17-alpine`.
On macOS, if Testcontainers cannot discover Docker Desktop, export
`DOCKER_HOST=unix://$HOME/.docker/run/docker.sock` and
`TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.
They apply the migration to clean databases; they never use the development database.

## Structure

- `src/ShiftManager/Domain`: users, nine business entities, enums and Israel shift clock.
- `src/ShiftManager/Data`: Identity context, separate Fluent API configurations and migrations.
- `tests/ShiftManager.Tests`: model/time tests and PostgreSQL integration tests.
- [Database architecture](docs/database-architecture.md): ERD, integrity, concurrency and deferred rules.

Hebrew is the MVP's required language. The host defaults to `he-IL`; future UI must use
`lang="he"`, `dir="rtl"`, centralized translations, Sunday-first weeks, 24-hour times and
DD/MM/YYYY dates. All identifiers and contracts remain English. Scheduling uses
`Asia/Jerusalem`; persisted instants use UTC.
