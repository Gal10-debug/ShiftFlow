#!/usr/bin/env python3
"""Run backend security tests against one owned, disposable PostgreSQL service."""
import argparse
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import time
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--all", action="store_true", help="Run the complete backend suite, including solver benchmarks")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    run = uuid.uuid4().hex
    name = "shiftflow-security-" + run
    username = "shiftflow_security_" + run
    env = {**os.environ, "POSTGRES_DB": "shiftflow_ci", "POSTGRES_USER": username,
           "POSTGRES_PASSWORD": secrets.token_hex(24), "CI": "true", "RUN_POSTGRES_TESTS": "1",
           "DOTNET_PROCESSOR_COUNT": "2", "DOTNET_NOLOGO": "true", "DOTNET_CLI_TELEMETRY_OPTOUT": "true",
           "SCHEDULING_VALIDATION_REPORT_DIRECTORY": str(root / "TestResults/scheduling-validation")}
    container_id = None
    result = 1

    def command(command_args, capture=False):
        return subprocess.run(command_args, cwd=root, env=env, check=True, text=True,
                              stdout=subprocess.PIPE if capture else None).stdout

    try:
        # Credentials are environment variables, never command arguments or output.
        container_id = command(["docker", "run", "-d", "--name", name, "--label", "shiftflow.security.run=" + run,
            "--memory=512m", "--cpus=2", "--tmpfs", "/var/lib/postgresql/data:rw", "-p", "127.0.0.1::5432",
            "-e", "POSTGRES_DB", "-e", "POSTGRES_USER", "-e", "POSTGRES_PASSWORD", "postgres:17-alpine"], True).strip()
        binding = command(["docker", "port", container_id, "5432/tcp"], True).strip()
        if not re.fullmatch(r"127\.0\.0\.1:\d+", binding):
            raise RuntimeError("Unexpected disposable database binding")
        port = int(binding.rsplit(":", 1)[1])
        if port in (5432, 55432):
            raise RuntimeError("Refusing a persistent database port")
        deadline = time.monotonic() + 60
        while subprocess.run(["docker", "exec", container_id, "pg_isready", "-U", username, "-d", "shiftflow_ci"],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
            if time.monotonic() >= deadline:
                raise RuntimeError("Disposable PostgreSQL readiness timed out")
            time.sleep(0.2)
        # Existing TestDatabase creates a new shiftflow_test_<uuid> for each test;
        # it never resets the service database or adopts a supplied application DB.
        connection = f"Host=127.0.0.1;Port={port};Database=shiftflow_ci;Username={username};Password={env['POSTGRES_PASSWORD']}"
        env["TEST_POSTGRES_CONNECTION_STRING"] = connection
        env["ConnectionStrings__Database"] = connection
        command(["dotnet", "build", "ShiftManager.slnx", "-c", "Release", "--warnaserror"])
        if args.all:
            command(["dotnet", "tool", "restore"])
            command(["dotnet", "ef", "database", "update", "--project", "src/ShiftManager", "--configuration", "Release", "--no-build"])
            command(["dotnet", "ef", "migrations", "has-pending-model-changes", "--project", "src/ShiftManager", "--configuration", "Release", "--no-build"])
        test_args = ["dotnet", "test", "ShiftManager.slnx", "-c", "Release", "--no-build", "--no-restore",
                     "--logger", "trx;LogFileName=phase7c-backend.trx" if args.all else "trx;LogFileName=phase7c-security.trx"]
        if not args.all:
            test_args += ["--filter", "FullyQualifiedName~Security_"]
        command(test_args)
        result = 0
    except (subprocess.CalledProcessError, RuntimeError, OSError) as error:
        # Never echo a process environment or connection string on failure.
        print(f"Security validation did not complete ({type(error).__name__}).", file=sys.stderr)
    finally:
        target = container_id or name
        check = subprocess.run(["docker", "inspect", "--format", '{{index .Config.Labels "shiftflow.security.run"}}', target],
                               text=True, capture_output=True)
        if check.returncode == 0:
            if check.stdout.strip() != run:
                print("Container ownership mismatch; refusing cleanup.", file=sys.stderr)
                result = 1
            elif subprocess.run(["docker", "rm", "-f", target], stdout=subprocess.DEVNULL).returncode:
                print("Owned security container cleanup failed.", file=sys.stderr)
                result = 1
            else:
                print("Owned security-validation container removed.")
        elif container_id:
            print("Security container cleanup could not be verified.", file=sys.stderr)
            result = 1
    return result


if __name__ == "__main__":
    raise SystemExit(main())
