"""Isolated startup tests: fake tools and a temporary project, never the developer database.
Run: python3 scripts/test-dev.py
"""
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time
import unittest

SCRIPTS = Path(__file__).resolve().parent
NODE = shutil.which("node")
MOCK = r'''#!/usr/bin/env python3
import json, os, pathlib, subprocess, sys, time
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
case = os.getenv("DEV_TEST_CASE", "")
with open(os.environ["DEV_TEST_LOG"], "a") as log:
    log.write(json.dumps({"tool": name, "args": args, "pid": os.getpid(),
        "database_env": "ConnectionStrings__Database" in os.environ,
        "password_env": "POSTGRES_PASSWORD" in os.environ}) + "\n")
if name == "node":
    if "--input-type=module" in args:
        sys.exit(1 if case == "port-busy" else 0)
    os.execv(os.environ["DEV_REAL_NODE"], [os.environ["DEV_REAL_NODE"], *args])
if name == "docker":
    if args == ["info"]: sys.exit(1 if case == "docker-down" else 0)
    if "config" in args:
        print(json.dumps({"services": {"postgres": {"environment": {
            "POSTGRES_DB": "shiftmanager", "POSTGRES_USER": "shiftmanager", "POSTGRES_PASSWORD": "test-only"},
            "ports": [{"target": 5432, "published": "5432", "host_ip": "127.0.0.1"}]}}}))
    if "up" in args: sys.exit(1 if case == "unhealthy" else 0)
    if "exec" in args:
        if "SELECT 1" in args[-1]: sys.exit(1 if case == "bad-password" else 0)
        if case == "uninitialized": sys.exit(1)
        print("20260101000000_Test" if case != "pending" else "")
    sys.exit(0)
if name == "npm" and "dev" in args or name == "dotnet" and "run" in args:
    child = subprocess.Popen(["sleep", "300"])
    with open(os.environ["DEV_TEST_LOG"], "a") as log:
        log.write(json.dumps({"child": child.pid, "group": os.getpgrp()}) + "\n")
    if case == "backend-exits" and name == "dotnet":
        time.sleep(.3)
        sys.exit(7)
    child.wait()
'''


class ConfigTests(unittest.TestCase):
    def invoke(self, config):
        return subprocess.run([NODE, str(SCRIPTS / "dev-config.mjs")], input=config,
                              capture_output=True, text=True)

    def test_quotes_password_as_data_and_does_not_execute_it(self):
        password = 'semi;" $() \\ hash# space'
        result = self.invoke(json.dumps({"services": {"postgres": {
            "environment": {"POSTGRES_DB": "db", "POSTGRES_USER": "user", "POSTGRES_PASSWORD": password},
            "ports": [{"target": 5432, "published": "5432", "host_ip": "127.0.0.1"}]}}}))
        self.assertEqual(result.returncode, 0)
        self.assertIn('Password="semi;"" $() \\ hash# space";', result.stdout)
        self.assertEqual(result.stderr, "")

    def test_invalid_configuration_fails_without_echoing_input(self):
        for config in ['not-json-sensitive-value', '{}', json.dumps({"services": {"postgres": {
            "environment": {"POSTGRES_PASSWORD": "sensitive-value"}, "ports": []}}})]:
            with self.subTest(config=config):
                result = self.invoke(config)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(result.stdout, "")
                self.assertNotIn("sensitive-value", result.stderr)


class StartupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="shiftflow-dev-test-")
        self.root = Path(self.temp.name)
        (self.root / "scripts").mkdir()
        for name in ["dev.sh", "dev-config.mjs"]:
            shutil.copy2(SCRIPTS / name, self.root / "scripts" / name)
        (self.root / ".env").write_text("POSTGRES_PASSWORD=test-only\nUNTRUSTED=$(touch SHOULD_NOT_EXIST)\n")
        (self.root / "compose.yaml").touch()
        for filename in ["frontend/node_modules/vite/bin/vite.js", "src/ShiftManager/obj/project.assets.json",
                         "src/ShiftManager/Data/Migrations/20260101000000_Test.cs"]:
            p = self.root / filename
            p.parent.mkdir(parents=True, exist_ok=True)
            p.touch()
        self.bin = self.root / "bin"
        self.bin.mkdir()
        for name in ["docker", "dotnet", "npm", "node"]:
            p = self.bin / name
            p.write_text(MOCK)
            p.chmod(0o755)
        self.log = self.root / "calls.jsonl"
        self.env = {**os.environ, "PATH": f"{self.bin}:{os.environ['PATH']}", "DEV_REAL_NODE": NODE,
                    "DEV_TEST_LOG": str(self.log), "POSTGRES_PASSWORD": "stale-shell-password"}
        self.process = None

    def tearDown(self):
        if self.process and self.process.poll() is None:
            self.process.terminate()
            self.process.wait(timeout=12)
        # Clean up any fake children even if a test assertion failed.
        for row in self.rows():
            if "child" in row:
                try:
                    os.kill(row["child"], signal.SIGKILL)
                except ProcessLookupError:
                    pass
        self.temp.cleanup()

    def rows(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []

    def run_script(self, mode="start", case=""):
        return subprocess.run(["bash", str(self.root / "scripts/dev.sh"), mode], env={**self.env, "DEV_TEST_CASE": case},
                              cwd="/tmp", capture_output=True, text=True, timeout=15)

    def test_prerequisite_and_service_errors_are_actionable(self):
        for case, message in [("docker-down", "Docker is unavailable"), ("unhealthy", "failed to start"),
                              ("bad-password", "authentication failed"), ("pending", "migrations are pending"),
                              ("uninitialized", "not initialized"), ("port-busy", "occupied")]:
            with self.subTest(case=case):
                result = self.run_script(case=case)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(message, result.stderr)
                self.assertNotIn("test-only", result.stdout + result.stderr)

    def test_missing_env_and_dependencies(self):
        (self.root / ".env").unlink()
        self.assertIn("Missing .env", self.run_script().stderr)
        (self.root / ".env").touch()
        (self.root / "frontend/node_modules/vite/bin/vite.js").unlink()
        self.assertIn("dev.sh setup", self.run_script().stderr)

    def test_setup_and_migrate_are_explicit(self):
        self.assertEqual(self.run_script("setup").returncode, 0)
        args = [r.get("args") for r in self.rows()]
        self.assertIn(["tool", "restore"], args)
        self.assertIn(["ci", "--prefix", "frontend"], args)
        self.assertIn(["ef", "database", "update", "--project", "src/ShiftManager"], args)
        self.log.unlink()
        self.assertEqual(self.run_script("migrate").returncode, 0)
        args = [r.get("args") for r in self.rows()]
        self.assertNotIn(["tool", "restore"], args)
        self.assertNotIn(["ci", "--prefix", "frontend"], args)
        self.assertFalse((self.root / "SHOULD_NOT_EXIST").exists())

    def test_server_failure_stops_both_process_groups_and_preserves_database(self):
        result = self.run_script(case="backend-exits")
        self.assertEqual(result.returncode, 7)
        self.assertIn("stopping both servers", result.stderr)
        self.assert_server_contract()

    def assert_server_contract(self):
        rows = self.rows()
        for row in rows:
            if row.get("tool") == "docker":
                self.assertNotIn("down", row["args"])
                if "config" in row["args"]:
                    self.assertFalse(row["password_env"])
            if row.get("tool") == "npm" and "dev" in row["args"]:
                self.assertFalse(row["database_env"])
                self.assertFalse(row["password_env"])
            if row.get("tool") == "dotnet" and "run" in row["args"]:
                self.assertTrue(row["database_env"])
            if "child" in row:
                # Parent and child shared the owned process group; neither may survive cleanup.
                with self.assertRaises(ProcessLookupError):
                    os.killpg(row["group"], 0)
        args = [r.get("args") for r in rows]
        self.assertNotIn(["tool", "restore"], args)
        self.assertNotIn(["ci", "--prefix", "frontend"], args)
        self.assertNotIn(["ef", "database", "update", "--project", "src/ShiftManager"], args)

    def test_term_stops_servers_and_their_children(self):
        with tempfile.TemporaryFile(mode="w+") as output:
            self.process = subprocess.Popen(["bash", str(self.root / "scripts/dev.sh")], cwd="/tmp",
                                            env=self.env, stdout=output, stderr=output)
            deadline = time.monotonic() + 8
            while len([r for r in self.rows() if "child" in r]) < 2 and time.monotonic() < deadline:
                time.sleep(.05)
            self.assertEqual(len([r for r in self.rows() if "child" in r]), 2)
            self.process.terminate()
            self.assertEqual(self.process.wait(timeout=12), 143)
        self.assert_server_contract()


if __name__ == "__main__":
    unittest.main()
