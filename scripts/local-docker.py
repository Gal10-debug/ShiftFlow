#!/usr/bin/env python3
"""Guarded local Compose entry point. No migrations, seeding or database recreation."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent.parent
CONTAINER = "shiftflow-postgres-1"
VOLUME = "shiftflow_postgres-data"
NETWORK = "shiftflow_default"
TARGET = "/var/lib/postgresql/data"
SQL = '''BEGIN READ ONLY;
SELECT json_build_object(
 'database', current_database(),
 'owner', (SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),
 'databases', (SELECT json_agg(datname ORDER BY datname) FROM pg_database WHERE NOT datistemplate),
 'users_table', to_regclass('public."AspNetUsers"') IS NOT NULL,
 'migrations', (SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM "__EFMigrationsHistory"));
ROLLBACK;'''
# A stopped, clean cluster is copied into RAM. PostgreSQL only writes to this copy.
SNAPSHOT = '''test "$(cat /source/PG_VERSION)" = 17
test ! -e /source/postmaster.pid
pg_controldata /source | grep -q 'Database cluster state: *shut down$'
cp -a /source/. /snapshot/
pg_ctl -D /snapshot -o '-c listen_addresses= -c unix_socket_directories=/tmp' -l /tmp/postgres.log -w start >/dev/null
psql -X -qAt -h /tmp -U shiftmanager -d shiftmanager -v ON_ERROR_STOP=1
pg_ctl -D /snapshot -m fast -w stop >/dev/null
'''


class GuardError(Exception):
    pass


def parse_psql_value(output):
    """Parse one psql scalar/JSON result without treating nonempty text as true."""
    value = output.strip()
    if value == "t":
        return True
    if value == "f":
        return False
    try:
        return json.loads(value)
    except ValueError:
        raise GuardError("Expected one PostgreSQL boolean or JSON value; result contents withheld.") from None


def run(args, *, env=None, data=None, stream=False):
    result = subprocess.run(args, cwd=ROOT, env=env, input=data, text=True,
                            stdout=None if stream else subprocess.PIPE,
                            stderr=None if stream else subprocess.PIPE)
    if result.returncode:
        # Never echo Compose config, connection strings or raw database errors.
        raise GuardError("Command failed; no credentials were printed. Check Docker, .env and the documented prerequisites.")
    return result.stdout


def inspect(kind, name):
    return json.loads(run(["docker", kind, "inspect", name]))[0]


def verify_metadata(container, volume, consumers, network, database_env):
    labels = container["Config"].get("Labels", {})
    if (labels.get("com.docker.compose.project") != "shiftflow"
            or labels.get("com.docker.compose.service") != "postgres"
            or Path(labels.get("com.docker.compose.project.working_dir", "")) != ROOT):
        raise GuardError("Existing PostgreSQL container is not owned by this regular development checkout.")
    mounts = container.get("Mounts", [])
    if len(mounts) != 1 or not all(mounts[0].get(k) == v for k, v in {
            "Type": "volume", "Name": VOLUME, "Destination": TARGET, "RW": True}.items()):
        raise GuardError("Unexpected database mounts; refusing to start services.")
    labels = volume.get("Labels") or {}
    if (volume.get("Name") != VOLUME or labels.get("com.docker.compose.project") != "shiftflow"
            or labels.get("com.docker.compose.volume") != "postgres-data"
            or volume.get("Driver") != "local" or volume.get("Options")):
        raise GuardError("Database volume ownership or storage configuration is unverified.")
    if consumers != [CONTAINER]:
        raise GuardError("Another container uses the regular database volume; refusing to continue.")
    labels = network.get("Labels") or {}
    if (set(container["NetworkSettings"]["Networks"]) != {NETWORK}
            or labels.get("com.docker.compose.project") != "shiftflow"
            or labels.get("com.docker.compose.network") != "default"):
        raise GuardError("Unexpected development network ownership.")
    actual = dict(item.split("=", 1) for item in container["Config"]["Env"] if "=" in item)
    if (actual.get("POSTGRES_DB") != "shiftmanager" or actual.get("POSTGRES_USER") != "shiftmanager"
            or actual.get("PGDATA", TARGET) != TARGET
            or any(actual.get(key) != database_env.get(key) for key in
                   ["POSTGRES_DB", "POSTGRES_USER", "POSTGRES_PASSWORD"])):
        raise GuardError("Existing database configuration does not match .env. Preserve the existing password and data.")
    if container["State"]["Status"] not in ("running", "exited"):
        raise GuardError("Database container is in an unexpected state.")
    if container.get("HostConfig", {}).get("PortBindings") != {
            "5432/tcp": [{"HostIp": "127.0.0.1", "HostPort": "5432"}]}:
        raise GuardError("Existing database must publish only the regular loopback port 5432.")
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", container["Image"]):
        raise GuardError("Cannot pin the existing PostgreSQL image.")


def verify_catalog(catalog, expected):
    if (catalog.get("database") != "shiftmanager" or catalog.get("owner") != "shiftmanager"
            or catalog.get("databases") != ["postgres", "shiftmanager"] or catalog.get("users_table") is not True):
        raise GuardError("Database contents do not identify the regular development database.")
    applied = set(catalog.get("migrations") or [])
    if applied - expected:
        raise GuardError("Database contains migrations unknown to this checkout.")
    return sorted(expected - applied)


def connection_string(database_env):
    def quote(value):
        return '"' + value.replace('"', '""') + '"'
    return ("Host=postgres;Port=5432;Database=shiftmanager;Username=shiftmanager;Password="
            + quote(database_env["POSTGRES_PASSWORD"]) + ";Include Error Detail=false")


def catalog(container):
    if container["State"]["Running"]:
        output = run(["docker", "exec", "-i", CONTAINER, "sh", "-ec",
                      'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -X -qAt -h 127.0.0.1 -U shiftmanager -d shiftmanager -v ON_ERROR_STOP=1'], data=SQL)
    else:
        # Discover the image's postgres uid rather than assuming a distro layout.
        uid = run(["docker", "run", "--rm", "--network", "none", "--read-only",
                   "--entrypoint", "id", container["Image"], "-u", "postgres"]).strip()
        if not uid.isdigit():
            raise GuardError("Cannot identify the existing image's PostgreSQL user.")
        output = run(["docker", "run", "--rm", "-i", "--user", "postgres", "--network", "none", "--read-only",
                      "--mount", f"type=volume,src={VOLUME},dst=/source,readonly",
                      "--tmpfs", f"/snapshot:rw,size=512m,mode=700,uid={uid}",
                      "--tmpfs", "/tmp:rw,size=16m", "--entrypoint", "sh", container["Image"], "-ec", SNAPSHOT], data=SQL)
    result = parse_psql_value(output)
    if not isinstance(result, dict):
        raise GuardError("Expected database catalog metadata as a JSON object.")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["verify", "config", "build", "up", "status", "logs", "stop"])
    args = parser.parse_args()
    env = dict(os.environ)
    # The existing ignored .env is authoritative. Never source it as shell code.
    env.pop("POSTGRES_PASSWORD", None)
    base = ["docker", "compose", "--project-directory", str(ROOT), "--env-file", str(ROOT / ".env"),
            "--project-name", "shiftflow", "-f", str(ROOT / "compose.yaml")]
    if not (ROOT / ".env").is_file():
        raise GuardError("Missing existing .env; do not substitute a new password or database.")
    config = json.loads(run(base + ["config", "--format", "json"], env=env))
    database_env = config["services"]["postgres"]["environment"]
    container = inspect("container", CONTAINER)
    consumers = run(["docker", "ps", "-a", "--filter", f"volume={VOLUME}", "--format", "{{.Names}}"]).splitlines()
    verify_metadata(container, inspect("volume", VOLUME), consumers, inspect("network", NETWORK), database_env)
    env["LOCAL_POSTGRES_IMAGE"] = container["Image"]
    env["LOCAL_DATABASE_CONNECTION"] = connection_string(database_env)
    compose = base + ["-f", str(ROOT / "compose.local.yaml")]
    run(compose + ["config", "--quiet"], env=env)
    if args.action == "config":
        print("Valid: shiftflow project; postgres + app; external regular volume; exact existing PostgreSQL image; loopback HTTP.")
    elif args.action == "build":
        run(compose + ["build", "app"], env=env, stream=True)
    elif args.action in ("verify", "up"):
        expected = {p.stem for p in (ROOT / "src/ShiftManager/Data/Migrations").glob("[0-9]*_*.cs")
                    if not p.name.endswith(".Designer.cs")}
        pending = verify_catalog(catalog(container), expected)
        print("Verified regular database identity, ownership and migration history using read-only inspection.", flush=True)
        if pending:
            raise GuardError("Pending migrations: " + ", ".join(pending)
                             + ". Obtain approval and apply them explicitly before starting. No persistent services were started.")
        if args.action == "verify":
            return
        # Build before starting persistent services. Never let Compose recreate postgres.
        run(compose + ["build", "app"], env=env, stream=True)
        if not container["State"]["Running"]:
            run(["docker", "start", CONTAINER])
        for attempt in range(45):
            if inspect("container", CONTAINER)["State"].get("Health", {}).get("Status") == "healthy":
                break
            time.sleep(2)
        else:
            raise GuardError("Existing PostgreSQL did not become healthy; it was not recreated. Inspect its logs.")
        # Recheck authenticated connectivity and schema after startup.
        if verify_catalog(catalog(inspect("container", CONTAINER)), expected):
            raise GuardError("Migration history changed during startup; app was not started.")
        run(compose + ["up", "-d", "--no-deps", "--wait", "--wait-timeout", "90", "app"], env=env)
        address = run(compose + ["port", "app", "8080"], env=env).strip()
        print(f"ShiftFlow: http://{address}")
    elif args.action == "stop":
        run(compose + ["stop", "app"], env=env)
        run(["docker", "stop", CONTAINER])
        print("Local containers stopped; database and cookie key volumes retained.")
    else:
        command = ["ps", "-a"] if args.action == "status" else ["logs", "--tail", "100", "app", "postgres"]
        run(compose + command, env=env, stream=True)


if __name__ == "__main__":
    try:
        main()
    except (GuardError, OSError, ValueError, KeyError) as error:
        print(f"Error: {error}" if isinstance(error, GuardError) else "Error: local Docker inspection failed; no credentials were printed.", file=sys.stderr)
        sys.exit(1)
