#!/usr/bin/env python3
"""Exercise the local image in an isolated Compose project with a RAM-only database."""
import argparse
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parent.parent
POSTGRES = "postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backend-tests", action="store_true", help="Also run backend correctness tests on disposable databases")
    args = parser.parse_args()
    project = "shiftflow-local-test-" + uuid.uuid4().hex
    env = {**os.environ, "POSTGRES_PASSWORD": secrets.token_hex(24), "LOCAL_POSTGRES_IMAGE": POSTGRES,
           "BOOTSTRAP_ADMIN_USERNAME": "docker-probe", "BOOTSTRAP_ADMIN_FIRST_NAME": "בדיקה",
           "BOOTSTRAP_ADMIN_LAST_NAME": "מקומית", "BOOTSTRAP_ADMIN_PASSWORD": "Probe!" + secrets.token_hex(16)}
    env["LOCAL_DATABASE_CONNECTION"] = "Host=postgres;Database=shiftmanager;Username=shiftmanager;Password=" + env["POSTGRES_PASSWORD"]
    # Override both published ports, every data mount, and the data/key volume declarations.
    # No persistent resource from the regular or demo projects is accepted as input.
    override = '''services:
  postgres:
    ports: !override ["127.0.0.1::5432"]
    volumes: !reset []
    tmpfs: ["/var/lib/postgresql/data:rw,size=512m"]
  app:
    ports: !override ["127.0.0.1::8080"]
    volumes: !override
      - type: bind
        source: ${LOCAL_TEST_KEYS}
        target: /var/lib/shiftflow/keys
    tmpfs: !override
      - /tmp:rw,noexec,nosuid,size=64m,uid=1654,gid=1654,mode=700
volumes:
  postgres-data: !reset {}
  local-keys: !reset {}
'''
    def command(argv, data=None, command_env=None):
        result = subprocess.run(argv, cwd=ROOT, env=command_env or env, input=data, text=True, capture_output=True)
        if result.returncode:
            raise RuntimeError("Disposable probe command failed: " + argv[0] + " (raw output suppressed)")
        return result.stdout.strip()

    with tempfile.TemporaryDirectory(prefix="shiftflow-local-test-") as directory:
        keys_path = Path(directory) / "keys"
        keys_path.mkdir(mode=0o777)
        keys_path.chmod(0o777)  # Synthetic local-test keys; allow the container's uid 1654.
        env["LOCAL_TEST_KEYS"] = str(keys_path)
        override_path = Path(directory) / "compose.test.yaml"
        override_path.write_text(override)
        compose = ["docker", "compose", "--project-directory", str(ROOT), "--project-name", project,
                   "--env-file", "/dev/null", "-f", str(ROOT / "compose.yaml"),
                   "-f", str(ROOT / "compose.local.yaml"), "-f", str(override_path)]
        try:
            config = json.loads(command(compose + ["config", "--format", "json"]))
            assert config["name"] == project and project.startswith("shiftflow-local-test-")
            assert not config["services"]["postgres"].get("volumes"), "Probe database must be RAM-only"
            mounts = config["services"]["app"]["volumes"]
            assert len(mounts) == 1 and all(mounts[0].get(key) == value for key, value in {
                "type": "bind", "source": str(keys_path), "target": "/var/lib/shiftflow/keys"}.items()), "Only the disposable key directory may be mounted"
            command(compose + ["up", "-d", "--wait", "postgres"])
            postgres = command(compose + ["ps", "-q", "postgres"])
            port = command(["docker", "port", postgres, "5432/tcp"])
            assert port.startswith("127.0.0.1:")
            host_connection = f"Host=127.0.0.1;Port={port.split(':')[1]};Database=shiftmanager;Username=shiftmanager;Password={env['POSTGRES_PASSWORD']}"
            migration_env = {**env, "ConnectionStrings__Database": host_connection, "ASPNETCORE_ENVIRONMENT": "Development"}
            command(["dotnet", "ef", "database", "update", "--project", "src/ShiftManager", "--configuration", "Release", "--no-build"], command_env=migration_env)
            command(compose + ["run", "--rm", "--no-deps", "-e", "BOOTSTRAP_ADMIN_USERNAME", "-e", "BOOTSTRAP_ADMIN_PASSWORD",
                               "-e", "BOOTSTRAP_ADMIN_FIRST_NAME", "-e", "BOOTSTRAP_ADMIN_LAST_NAME", "app", "--bootstrap-admin"])
            command(compose + ["up", "-d", "--no-build", "--wait", "--wait-timeout", "90", "app"])
            app = command(compose + ["ps", "-q", "app"])
            binding = command(["docker", "port", app, "8080/tcp"])
            assert binding.startswith("127.0.0.1:")
            base = "http://" + binding
            jar = http.cookiejar.CookieJar()
            client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

            def request(path, body=None, token=None):
                headers = {"Content-Type": "application/json"}
                if token: headers["X-CSRF-TOKEN"] = token
                req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers)
                return client.open(req, timeout=10)

            for path in ["/", "/scheduling", "/health/live", "/health/ready"]:
                with request(path) as response:
                    content = response.read().decode()
                    assert response.status == 200
                    if path in ("/", "/scheduling"):
                        assert 'dir="rtl"' in content and '<script type="module"' in content, "React/RTL entrypoint missing"
            with request("/") as response:
                import re
                asset = re.search(r'src="([^"]+\.js)"', response.read().decode()).group(1)
            with request(asset) as response: assert "javascript" in response.headers["Content-Type"]
            try:
                request("/api/auth/me")
                raise AssertionError("Anonymous API access must fail")
            except urllib.error.HTTPError as error: assert error.code == 401

            def csrf():
                with request("/api/auth/csrf") as response: return json.load(response)["requestToken"]

            token = csrf()
            with request("/api/auth/login", {"username": env["BOOTSTRAP_ADMIN_USERNAME"], "password": env["BOOTSTRAP_ADMIN_PASSWORD"]}, token) as response:
                assert response.status == 200
            auth = next(cookie for cookie in jar if cookie.name == "ShiftFlow.Auth")
            attributes = {key.lower(): value for key, value in auth._rest.items()}
            assert not auth.secure and "httponly" in attributes and attributes.get("samesite", "").lower() == "strict", "Unexpected local auth cookie flags"
            with request("/api/auth/me") as response: assert response.status == 200
            token = csrf()
            with request("/api/auth/change-password", {"currentPassword": env["BOOTSTRAP_ADMIN_PASSWORD"], "newPassword": "Changed!" + secrets.token_hex(16)}, token) as response:
                assert response.status == 200
            with request("/api/access/admin") as response: assert response.status == 204
            with request("/api/admin/scheduling/shift-types") as response: assert len(json.load(response)) == 51
            token = csrf()
            try:
                request("/api/admin/scheduling/generation", {"year": 2026, "month": 11})
                raise AssertionError("Missing CSRF must fail")
            except urllib.error.HTTPError as error: assert error.code == 400
            with request("/api/admin/scheduling/generation", {"year": 2026, "month": 11}, token) as response:
                job = json.load(response)
            for attempt in range(60):
                with request("/api/admin/scheduling/generation/" + job["id"]) as response: job = json.load(response)
                if job["state"] not in ("QUEUED", "RUNNING"): break
                time.sleep(1)
            assert job["state"] == "READY", "Native OR-Tools scheduling did not finish successfully"
            assert list(keys_path.glob("key-*.xml")), "Expected persisted data-protection key files"
            command(compose + ["up", "-d", "--no-deps", "--no-build", "--force-recreate", "--wait", "app"])
            app = command(compose + ["ps", "-q", "app"])
            base = "http://" + command(["docker", "port", app, "8080/tcp"])
            with request("/api/auth/me") as response: assert response.status == 200, "Login must survive app recreation"
            print("Disposable Compose runtime passed: React/RTL, SPA/assets, Docker DNS, readiness, cookie login/password change, login across app recreation, key persistence, authorization, CSRF and native OR-Tools.", flush=True)
            if args.backend_tests:
                command(["docker", "exec", postgres, "psql", "-U", "shiftmanager", "-d", "shiftmanager", "-c", "CREATE DATABASE shiftflow_ci"])
                test_env = {**migration_env, "RUN_POSTGRES_TESTS": "1", "TEST_POSTGRES_CONNECTION_STRING": host_connection.replace("Database=shiftmanager;", "Database=shiftflow_ci;")}
                result = command(["dotnet", "test", "tests/ShiftManager.Tests", "-c", "Release", "--no-build", "--no-restore",
                                  "--filter", "Category!=SchedulingBenchmark", "--logger", "console;verbosity=quiet"], command_env=test_env)
                print(result.splitlines()[-1])
        finally:
            # Fixed file set, unique owned project, RAM database and temporary keys only.
            command(compose + ["down", "--remove-orphans"])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Disposable runtime probe failed ({type(error).__name__}): {error}")
        raise SystemExit(1)
