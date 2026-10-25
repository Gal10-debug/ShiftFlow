#!/usr/bin/env bash
# Local development only. Compatible with macOS Bash 3.2 and Linux Bash.
set -euo pipefail
# Never expose secrets if this script is invoked with bash -x or inherited tracing.
set +x

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
MODE="${1:-start}"
BACKEND_PID=""
FRONTEND_PID=""

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
usage() {
  cat <<'HELP'
Usage: ./scripts/dev.sh [start|setup|migrate]
  start    Start PostgreSQL, backend and frontend (default).
  setup    Restore .NET dependencies/tools, install frontend dependencies and migrate.
  migrate  Apply EF migrations explicitly; does not reinstall tools or npm dependencies.
Ctrl+C stops both development servers. PostgreSQL and its data are kept running.
HELP
}
case "$MODE" in
  start|setup|migrate) ;;
  -h|--help) usage; exit 0 ;;
  *) usage >&2; exit 2 ;;
esac
[[ $# -le 1 ]] || { usage >&2; exit 2; }

for tool in docker dotnet node npm; do
  command -v "$tool" >/dev/null 2>&1 || fail "Missing $tool. Install .NET 10 SDK, a supported Node.js LTS (24 recommended), npm and Docker Desktop."
done
node -e 'const [major, minor] = process.versions.node.split(".").map(Number); process.exit(major < 22 || (major === 22 && minor < 12) ? 1 : 0)' || fail "Node.js 22.12+ is required; use Node.js 24 LTS."
dotnet --version >/dev/null 2>&1 || fail "The .NET SDK required by global.json is unavailable. Install .NET 10."
docker compose version >/dev/null 2>&1 || fail "Docker Compose v2 is required (included in Docker Desktop)."
[[ -f "$ROOT/.env" ]] || fail "Missing .env. Create an untracked .env with POSTGRES_PASSWORD=<your-local-password>. Preserve the password of an existing database volume."
docker info >/dev/null 2>&1 || fail "Docker is unavailable. Start Docker Desktop and wait until its engine is ready."

compose() {
  # .env is authoritative, including when the calling terminal has a stale exported password.
  # Compose parses dotenv syntax; the file is never sourced as shell code.
  (unset POSTGRES_PASSWORD; docker compose --project-directory "$ROOT" --env-file "$ROOT/.env" -f "$ROOT/compose.yaml" "$@")
}
# Assignment and export are separate so failures from either side of the pipe are preserved.
ConnectionStrings__Database="$(compose config --format json | node "$ROOT/scripts/dev-config.mjs")" || fail "Could not load local database configuration. Check POSTGRES_PASSWORD in .env."
export ConnectionStrings__Database
export ASPNETCORE_ENVIRONMENT=Development

if [[ "$MODE" == start ]]; then
  [[ -f frontend/node_modules/vite/bin/vite.js && -f src/ShiftManager/obj/project.assets.json ]] || fail "Dependencies are not ready. Run ./scripts/dev.sh setup once (and after dependency changes)."
  # Fail before starting either server when a development port is occupied.
  node --input-type=module <<'JS' || fail "Port 5080 or 5173 is occupied. Stop the existing development server and retry."
import net from "node:net";
for (const port of [5080, 5173]) {
  await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once("error", reject);
    server.listen(port, "127.0.0.1", () => server.close(resolve));
  }).catch(() => { process.exitCode = 1; });
}
JS
fi

printf 'Starting local PostgreSQL...\n'
compose up -d --wait --wait-timeout 90 postgres || fail "PostgreSQL failed to start or become healthy. Check Docker and port 5432, then run: docker compose logs --tail=50 postgres"
# pg_isready alone does not authenticate. Detect a changed .env password against an old volume.
compose exec -T postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -qAtc "SELECT 1"' >/dev/null 2>&1 || fail "PostgreSQL authentication failed. Check that .env matches the password already stored in the database volume. Do not delete the volume to fix this."

if [[ "$MODE" == setup ]]; then
  dotnet restore ShiftManager.slnx
  dotnet tool restore
  (unset ConnectionStrings__Database POSTGRES_PASSWORD; npm ci --prefix frontend)
fi
if [[ "$MODE" == setup || "$MODE" == migrate ]]; then
  dotnet ef --version >/dev/null 2>&1 || fail "EF tools are not restored. Run ./scripts/dev.sh setup first."
  dotnet ef database update --project src/ShiftManager
  printf 'Local database is ready. Start development with: ./scripts/dev.sh\n'
  exit 0
fi

# Detect unapplied migrations without installing tools or changing the schema on normal startup.
APPLIED_MIGRATIONS="$(compose exec -T postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -qAt' <<< 'SELECT "MigrationId" FROM "__EFMigrationsHistory"' 2>/dev/null)" || fail "The database is not initialized. Run ./scripts/dev.sh setup."
for migration in src/ShiftManager/Data/Migrations/[0-9]*_*.cs; do
  [[ "$migration" != *.Designer.cs ]] || continue
  migration="${migration##*/}"
  migration="${migration%.cs}"
  [[ $'\n'"$APPLIED_MIGRATIONS"$'\n' == *$'\n'"$migration"$'\n'* ]] || fail "Database migrations are pending. Run ./scripts/dev.sh migrate."
done

# Independent process groups let Ctrl+C/TERM reach npm/Vite and dotnet's child host.
# Bash's monitor mode works without setsid, which is absent on standard macOS installations.
set -m
cleanup() {
  local pid alive round
  trap - EXIT
  trap '' INT TERM
  for pid in "$BACKEND_PID" "$FRONTEND_PID"; do
    [[ -z "$pid" ]] || kill -TERM -- "-$pid" 2>/dev/null || true
  done
  # Give graceful shutdown five seconds, then remove any remaining owned process groups.
  for round in 1 2 3 4 5; do
    alive=0
    for pid in "$BACKEND_PID" "$FRONTEND_PID"; do
      if [[ -n "$pid" ]] && kill -0 -- "-$pid" 2>/dev/null; then alive=1; fi
    done
    [[ "$alive" == 1 ]] || break
    sleep 1
  done
  for pid in "$BACKEND_PID" "$FRONTEND_PID"; do
    if [[ -n "$pid" ]]; then
      kill -KILL -- "-$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
  printf '\nDevelopment servers stopped. PostgreSQL is still running.\n'
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

dotnet run --project src/ShiftManager --launch-profile http --no-restore &
BACKEND_PID=$!
# Vite does not need the database credentials. Do not pass them to its process.
(unset ConnectionStrings__Database POSTGRES_PASSWORD; exec npm run dev --prefix frontend -- --clearScreen false) &
FRONTEND_PID=$!
printf '\nFrontend: http://127.0.0.1:5173\nBackend:  http://localhost:5080\nPress Ctrl+C to stop both servers.\n\n'

while :; do
  for pid in "$BACKEND_PID" "$FRONTEND_PID"; do
    if ! kill -0 "$pid" 2>/dev/null; then
      status=0
      wait "$pid" || status=$?
      printf 'A development server exited (status %s); stopping both servers.\n' "$status" >&2
      [[ "$status" != 0 ]] || status=1
      exit "$status"
    fi
  done
  sleep 1
done
