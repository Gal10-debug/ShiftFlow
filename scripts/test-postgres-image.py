#!/usr/bin/env python3
"""Check the reviewed PostgreSQL image using an owned disposable volume only."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import uuid

IMAGE = "postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac"


def main():
    run = uuid.uuid4().hex
    name = "shiftflow-image-" + run
    volume = name + "-data"
    label = "shiftflow.image.run"
    username = "shiftflow_image_" + run
    env = {**os.environ, "POSTGRES_USER": username, "POSTGRES_DB": "image_probe",
           "POSTGRES_PASSWORD": secrets.token_hex(24)}
    result = 1
    created = set()

    def command(args, data=None):
        return subprocess.run(["docker", *args], input=data, capture_output=True, env=env, check=True).stdout

    def owns(kind, target):
        inspect = ["volume", "inspect"] if kind == "volume" else ["inspect"]
        template = '{{index .Labels "' + label + '"}}' if kind == "volume" else '{{index .Config.Labels "' + label + '"}}'
        check = subprocess.run(["docker", *inspect, "--format", template, target], capture_output=True, env=env)
        return None if check.returncode != 0 else check.stdout.decode().strip() == run

    def healthy():
        deadline = time.monotonic() + 60
        while command(["inspect", "--format", "{{.State.Health.Status}}", name]).decode().strip() != "healthy":
            if time.monotonic() > deadline:
                raise RuntimeError("Owned image health check timed out")
            time.sleep(0.2)

    def sql(text, database="image_probe"):
        return command(["exec", "-i", name, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", username,
                        "-d", database, "-Atq"], text.encode()).decode().strip()

    try:
        command(["volume", "create", "--label", label + "=" + run, volume])
        created.add("volume")
        command(["run", "-d", "--name", name, "--label", label + "=" + run,
                 "--memory=256m", "--cpus=1", "--network", "none",
                 "--mount", "type=volume,source=" + volume + ",target=/var/lib/postgresql/data",
                 # The entrypoint's temporary initialization server exposes only
                 # a Unix socket. TCP readiness waits for the final server.
                 "--health-cmd", 'pg_isready -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"',
                 "--health-interval", "1s", "--health-timeout", "3s", "--health-retries", "30",
                 "-e", "POSTGRES_USER", "-e", "POSTGRES_DB", "-e", "POSTGRES_PASSWORD", IMAGE])
        created.add("container")
        healthy()
        if not owns("container", name) or not owns("volume", volume):
            raise RuntimeError("Image probe ownership could not be verified")
        sql("""
            CREATE TABLE image_probe (id integer PRIMARY KEY, note text, snapshot jsonb, day date, instant timestamptz);
            INSERT INTO image_probe VALUES (1, 'בדיקת התמדה', '{"status":"published","version":1}', '2026-10-25', '2026-10-24T23:00:00Z');
            BEGIN;
            INSERT INTO image_probe VALUES (2, 'rollback', '{}', '2026-10-25', now());
            ROLLBACK;
            """)
        fingerprint = "SELECT count(*)::text || ':' || md5(string_agg(to_jsonb(p)::text, ',' ORDER BY id)) FROM image_probe p;"
        before = sql(fingerprint)
        if not before.startswith("1:"):
            raise RuntimeError("Transaction rollback failed")
        backup = command(["exec", name, "pg_dump", "-U", username, "-d", "image_probe", "--format=custom"])
        permissions = command(["exec", name, "stat", "-c", "%a %u:%g", "/var/lib/postgresql/data"]).decode().strip()
        if permissions != "700 70:70":
            raise RuntimeError("Unexpected Alpine PostgreSQL data ownership")
        # This is the only restart. Both targets were created above and verified;
        # neither a Compose project nor any existing volume is accepted as input.
        command(["restart", "--time", "30", name])
        healthy()
        if before != sql(fingerprint):
            raise RuntimeError("Data changed across the owned container restart")
        sql("CREATE DATABASE image_restored;")
        command(["exec", "-i", name, "pg_restore", "--exit-on-error", "--no-owner", "-U", username,
                 "-d", "image_restored"], backup)
        if before != sql(fingerprint, "image_restored"):
            raise RuntimeError("Disposable backup/restore comparison failed")
        report = {"image": IMAGE, "healthy": True, "transactionRollback": True,
                  "restartPersistence": True, "backupRestore": True, "volumePermissions": permissions}
        path = Path(__file__).resolve().parent.parent / "TestResults/security/phase7c1/storage-probe.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report, indent=2) + "\n")
        print("Owned image probe passed: health, rollback, restart persistence, backup/restore and private volume permissions.")
        result = 0
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        # Commands, passwords, environments, dumps and SQL contents stay private.
        print(f"Image compatibility probe failed ({type(error).__name__}).")
    finally:
        # Remove the container first; never force-remove a volume still in use.
        for kind, target in [("container", name), ("volume", volume)]:
            owner = owns(kind, target)
            if owner is None and kind in created:
                print("Owned image-probe cleanup could not be verified.")
                result = 1
            elif owner is False:
                print("Image-probe ownership mismatch; refusing cleanup.")
                result = 1
            elif owner:
                args = ["volume", "rm", target] if kind == "volume" else ["rm", "-f", "-v", target]
                try:
                    command(args)
                except (OSError, subprocess.CalledProcessError):
                    print("Owned image-probe cleanup failed.")
                    result = 1
    return result


if __name__ == "__main__":
    raise SystemExit(main())
