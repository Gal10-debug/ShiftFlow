#!/usr/bin/env python3
"""Target-verified pilot operations. Never adopts an unmarked database or overwrites a restore target."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent
COMPOSE = ROOT / "deploy/pilot/compose.yaml"
POSTGRES = "postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac"


def identifier(value):
    if not re.fullmatch(r"[a-z][a-z0-9_]{0,62}", value):
        raise ValueError("Invalid database identifier")
    return '"' + value + '"'


def literal(value):
    return "'" + value.replace("'", "''") + "'"


class Pilot:
    def __init__(self, config):
        self.config = config
        if config.get("kind") not in ("disposable", "pilot"): raise ValueError("Explicit environment kind required")
        self.id = config["id"]
        if not re.fullmatch("[a-f0-9]{32}", self.id): raise ValueError("Invalid deployment identity")
        self.database = config["database"]
        if not re.fullmatch("shiftflow_pilot_" + self.id + r"(?:_restore_[a-f0-9]{6})?", self.database):
            raise ValueError("Invalid database target")
        self.project = "shiftflow-pilot-" + self.id
        self.secrets = Path(config["secrets"]).resolve()
        self.env = {**os.environ, **config["environment"], "PILOT_PROJECT": self.project,
                    "PILOT_DEPLOYMENT_ID": self.id, "PILOT_DATABASE_NAME": self.database,
                    "PILOT_SECRET_DIRECTORY": str(self.secrets)}
        if config.get("kind") == "disposable" and self.env.get("PILOT_BIND_ADDRESS") != "127.0.0.1":
            raise ValueError("Validation must bind to loopback")

    @property
    def confirmation(self): return self.id + "/" + self.database

    def role(self, purpose): return "sf_" + self.id + "_" + purpose

    def command(self, args, data=None, check=True, timeout=180):
        result = subprocess.run(args, input=data, capture_output=True, env=self.env, cwd=ROOT, timeout=timeout)
        if check and result.returncode:
            private_log = self.secrets.parent / "operation-private.log"
            private_log.write_bytes(result.stdout + result.stderr)
            os.chmod(private_log, 0o600)
            # Commands/exception payloads may contain a secret. Never print them.
            raise RuntimeError("Pilot operation failed; no success is claimed")
        return result

    def compose(self, *args, data=None, check=True):
        return self.command(["docker", "compose", "--env-file", "/dev/null", "--project-name", self.project,
                             "--file", str(COMPOSE), *args], data, check)

    def container(self, service):
        cid = self.compose("ps", "-aq", service).stdout.decode().strip()
        if not re.fullmatch("[a-f0-9]{12,64}", cid): raise RuntimeError("Unique owned container required")
        info = json.loads(self.command(["docker", "inspect", cid]).stdout)[0]
        labels = info["Config"]["Labels"]
        if labels.get("shiftflow.pilot.deployment") != self.id or labels.get("com.docker.compose.project") != self.project:
            raise RuntimeError("Container ownership mismatch")
        if service == "postgres" and info["Config"]["Image"] != POSTGRES:
            raise RuntimeError("Unreviewed PostgreSQL image")
        return cid

    def verify_networks(self):
        for purpose in ("EDGE", "DATA", "INGRESS"):
            name = self.env.get("PILOT_" + purpose + "_NETWORK", "")
            info = json.loads(self.command(["docker", "network", "inspect", name]).stdout)[0]
            if info.get("Labels", {}).get("shiftflow.pilot.deployment") != self.id:
                raise RuntimeError("Network ownership mismatch")
            if purpose != "INGRESS" and not info.get("Internal"):
                raise RuntimeError("Private pilot network required")

    def sql(self, text, database="postgres", check=True):
        identifier(database)
        return self.command(["docker", "exec", "-i", "--user", "70:70", self.container("postgres"), "psql", "-X",
                             "-v", "ON_ERROR_STOP=1", "-U", "pilot_owner", "-d", database, "-Atq"], text.encode(), check)

    def verify(self):
        value = self.sql("SELECT current_database() || ':' || deployment_id FROM shiftflow_ops.identity", self.database).stdout.decode().strip()
        if value != self.database + ":" + self.id: raise RuntimeError("Database ownership marker mismatch")

    def provision(self, restore=False):
        # Explicitly refuse adoption or replacement, even if the name looks like a pilot.
        if self.sql("SELECT 1 FROM pg_database WHERE datname=" + literal(self.database)).stdout.strip():
            raise RuntimeError("Database already exists; refusing provisioning")
        if not restore:
            for purpose in ["runtime", "migrator", "backup"]:
                role = self.role(purpose)
                if self.sql("SELECT 1 FROM pg_roles WHERE rolname=" + literal(role)).stdout.strip():
                    raise RuntimeError("Role already exists; refusing replacement")
                password = (self.secrets / (purpose + "-password")).read_text().strip()
                if len(password) < 24: raise RuntimeError("Strong provisioned secret required")
                self.sql("CREATE ROLE " + identifier(role) + " LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD " + literal(password))
        self.sql("CREATE DATABASE " + identifier(self.database) + " OWNER " + identifier(self.role("migrator")))
        runtime, migrator, backup = [identifier(self.role(p)) for p in ["runtime", "migrator", "backup"]]
        self.sql(f"""
            REVOKE ALL ON DATABASE {identifier(self.database)} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {identifier(self.database)} TO {runtime}, {migrator}, {backup};
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE, CREATE ON SCHEMA public TO {migrator};
            GRANT USAGE ON SCHEMA public TO {runtime}, {backup};
            CREATE SCHEMA shiftflow_ops;
            CREATE TABLE shiftflow_ops.identity (deployment_id text PRIMARY KEY);
            INSERT INTO shiftflow_ops.identity VALUES ({literal(self.id)});
            REVOKE ALL ON SCHEMA shiftflow_ops FROM PUBLIC;
            GRANT USAGE ON SCHEMA shiftflow_ops TO {runtime}, {migrator}, {backup};
            GRANT SELECT ON shiftflow_ops.identity TO {runtime}, {migrator}, {backup};
            ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {runtime};
            ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT SELECT ON TABLES TO {backup};
            ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {runtime};
            ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT SELECT ON SEQUENCES TO {backup};
            ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
            ALTER DEFAULT PRIVILEGES FOR ROLE {migrator} IN SCHEMA public GRANT EXECUTE ON FUNCTIONS TO {runtime};
            """, self.database)
        self.verify()

    def harden_history(self):
        self.verify()
        self.sql(f'REVOKE INSERT, UPDATE, DELETE ON TABLE "__EFMigrationsHistory" FROM {identifier(self.role("runtime"))}', self.database)

    def fingerprints(self):
        self.verify()
        tables = self.sql("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename", self.database).stdout.decode().splitlines()
        result = {}
        for table in tables:
            # Only row counts and hashes leave PostgreSQL, including Identity data.
            escaped = '"' + table.replace('"', '""') + '"'
            value = self.sql(f"SELECT count(*)::text || ':' || md5(coalesce(string_agg(to_jsonb(r)::text, E'\\n' ORDER BY to_jsonb(r)::text), '')) FROM public.{escaped} r", self.database).stdout.decode().strip()
            result[table] = value
        return result

    def backup(self, destination, recipient=None):
        self.verify()
        destination = Path(destination).resolve()
        if destination.exists() or destination.with_suffix(destination.suffix + ".json").exists(): raise RuntimeError("Backup destination must be new")
        if self.config.get("kind") != "disposable" and not recipient:
            raise RuntimeError("Live backups require an approved age encryption recipient")
        if recipient and (not re.fullmatch(r"age1[a-z0-9]{20,100}", recipient) or not shutil.which("age")):
            raise RuntimeError("Approved age recipient and installed age tool required")
        started = time.monotonic()
        # Keep unencrypted synthetic drills private; real backups stream directly into age.
        args = ["docker", "compose", "--env-file", "/dev/null", "--project-name", self.project, "--file", str(COMPOSE), "run", "--rm", "-T", "--no-deps", "backup"]
        with open(destination, "xb", opener=lambda p, f: os.open(p, f, 0o600)) as output, tempfile.TemporaryFile() as errors:
            dump = subprocess.Popen(args, stdout=subprocess.PIPE if recipient else output, stderr=errors, env=self.env, cwd=ROOT)
            encryption = None
            if recipient:
                encryption = subprocess.Popen(["age", "--encrypt", "--recipient", recipient], stdin=dump.stdout, stdout=output, stderr=errors)
                dump.stdout.close()
            try:
                if dump.wait(timeout=180) or (encryption is not None and encryption.wait(timeout=180)):
                    errors.seek(0)
                    log=self.secrets.parent/'backup-private.log';log.write_bytes(errors.read());os.chmod(log,0o600)
                    raise RuntimeError("Backup failed; partial artifact is not a valid backup")
            finally:
                for child in [dump, encryption]:
                    if child is not None and child.poll() is None: child.kill(); child.wait(timeout=10)
        manifest = {"deploymentId": self.id, "database": self.database, "encrypted": bool(recipient),
                    "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(), "elapsedSeconds": time.monotonic() - started}
        destination.with_suffix(destination.suffix + ".json").write_text(json.dumps(manifest, indent=2) + "\n")
        return manifest

    def restore(self, archive, identity=None):
        self.verify()
        if not re.fullmatch("shiftflow_pilot_" + self.id + r"_restore_[a-f0-9]{6}", self.database):
            raise RuntimeError("Restore requires a separate newly provisioned recovery database")
        if self.sql("SELECT 1 FROM pg_tables WHERE schemaname='public' LIMIT 1", self.database).stdout.strip():
            raise RuntimeError("Restore target is not empty; refusing overwrite")
        archive = Path(archive).resolve()
        manifest = json.loads(archive.with_suffix(archive.suffix + ".json").read_text())
        if manifest["deploymentId"] != self.id or manifest["database"] == self.database or hashlib.sha256(archive.read_bytes()).hexdigest() != manifest["sha256"]:
            raise RuntimeError("Backup identity or checksum mismatch")
        if not manifest["encrypted"] and self.config.get("kind") != "disposable":
            raise RuntimeError("Unencrypted restore is restricted to synthetic disposable drills")
        args = ["docker", "exec", "-i", "--user", "70:70", self.container("postgres"), "pg_restore", "--exit-on-error", "--single-transaction",
                "--no-owner", "--no-privileges", "-U", "pilot_owner", "--role", self.role("migrator"), "-d", self.database]
        started = time.monotonic()
        with archive.open("rb") as source, tempfile.TemporaryFile() as errors:
            decryption = None
            if manifest["encrypted"]:
                if not identity or not shutil.which("age"): raise RuntimeError("Private recovery identity and age tool required")
                decryption = subprocess.Popen(["age", "--decrypt", "--identity", str(Path(identity).resolve())], stdin=source, stdout=subprocess.PIPE, stderr=errors)
            restore = subprocess.Popen(args, stdin=decryption.stdout if decryption else source, stdout=subprocess.DEVNULL, stderr=errors, env=self.env)
            if decryption: decryption.stdout.close()
            try:
                if restore.wait(timeout=180) or (decryption is not None and decryption.wait(timeout=180)):
                    errors.seek(0)
                    log=self.secrets.parent/'restore-private.log';log.write_bytes(errors.read());os.chmod(log,0o600)
                    raise RuntimeError("Restore failed; do not expose this recovery database")
            finally:
                for child in [restore, decryption]:
                    if child is not None and child.poll() is None: child.kill(); child.wait(timeout=10)
        self.harden_history()
        return {"elapsedSeconds": time.monotonic() - started, "sourceChecksumVerified": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["verify", "start-database", "start", "stop", "restrict", "inspect-keys", "provision", "provision-restore", "migrate", "bootstrap", "backup", "restore", "invalidate-sessions"])
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--confirm-target", required=True, help="Exact deployment-id/database from the reviewed configuration")
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--age-recipient")
    parser.add_argument("--age-identity", type=Path)
    args = parser.parse_args()
    try:
        pilot = Pilot(json.loads(args.config.read_text()))
        if args.confirm_target != pilot.confirmation: raise RuntimeError("Target confirmation mismatch")
        if args.action == "verify": pilot.verify()
        elif args.action == "start-database":
            pilot.verify_networks(); pilot.compose("up", "-d", "postgres")
        elif args.action == "start":
            pilot.verify(); pilot.verify_networks(); pilot.compose("up", "-d", "app", "proxy")
        elif args.action in ("stop", "restrict"):
            # Emergency isolation must remain possible while the database is down.
            # Exact confirmation and container ownership still apply.
            pilot.container("proxy"); pilot.compose("stop", "-t", "40", "proxy")
            if args.action == "stop":
                pilot.container("app"); pilot.compose("stop", "-t", "40", "app")
        elif args.action == "inspect-keys":
            pilot.verify(); pilot.compose("run", "--rm", "-T", "--no-deps", "maintain")
        elif args.action.startswith("provision"): pilot.provision(restore=args.action == "provision-restore")
        elif args.action in ["migrate", "bootstrap"]:
            pilot.verify(); pilot.compose("run", "--rm", "-T", "--no-deps", args.action)
            if args.action == "migrate": pilot.harden_history()
        elif args.action == "invalidate-sessions":
            pilot.verify(); pilot.compose("run", "--rm", "-T", "--no-deps", "maintain", "--pilot-command", "invalidate-sessions", "--confirm-target", pilot.confirmation)
        elif args.action == "backup":
            if not args.archive: raise RuntimeError("New archive path required")
            pilot.backup(args.archive, args.age_recipient)
        elif args.action == "restore":
            if not args.archive: raise RuntimeError("Archive path required")
            pilot.restore(args.archive, args.age_identity)
        print("Pilot operation completed for the explicitly verified target.")
        return 0
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError):
        print("Pilot operation refused or failed; no success is claimed. Verify target, configuration and private diagnostics.", file=sys.stderr)
        return 1


if __name__ == "__main__": raise SystemExit(main())
