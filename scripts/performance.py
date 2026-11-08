#!/usr/bin/env python3
"""Bounded backend profiling on one owned PostgreSQL container; never adopts a database."""
import argparse
from collections import defaultdict
import json
import os
from pathlib import Path
import platform
import secrets
import signal
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
IMAGE = "postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac"
LABEL = "shiftflow.performance.run"


def summarize_trx(path):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(path).getroot()
    definitions = {x.attrib["id"]: x.find("t:TestMethod", ns).attrib
                   for x in root.findall(".//t:UnitTest", ns)}
    tests, classes, assemblies = [], defaultdict(float), defaultdict(float)
    for result in root.findall(".//t:UnitTestResult", ns):
        parts = result.attrib.get("duration", "0:0:0").split(":")
        seconds = sum(float(v) * m for v, m in zip(parts, (3600, 60, 1)))
        method = definitions[result.attrib["testId"]]
        cls = method["className"].split(",")[0]
        assembly = Path(method.get("codeBase", "unknown")).name
        tests.append({"name": result.attrib["testName"], "class": cls,
                      "seconds": seconds, "outcome": result.attrib["outcome"]})
        classes[cls] += seconds
        assemblies[assembly] += seconds
    counters = root.find(".//t:Counters", ns)
    return {"counts": dict(counters.attrib) if counters is not None else {},
            "summedTestSeconds": sum(t["seconds"] for t in tests),
            "classesSummedSeconds": dict(sorted(classes.items(), key=lambda x: -x[1])),
            "assembliesSummedSeconds": dict(assemblies),
            "tests": sorted(tests, key=lambda t: -t["seconds"])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--suite", choices=("all", "correctness", "benchmarks"), default="all")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--filter", help="Additional VSTest filter; suite selection still applies")
    parser.add_argument("--timeout", type=int, default=1200)
    args = parser.parse_args()
    if not 60 <= args.timeout <= 1800:
        parser.error("timeout must be between 60 and 1800 seconds")
    output = args.output.resolve()
    if not output.is_relative_to(ROOT / "TestResults"):
        parser.error("output must be inside the ignored TestResults directory")
    output.mkdir(parents=True, exist_ok=False, mode=0o700)
    run = uuid.uuid4().hex
    name = "shiftflow-performance-" + run
    password = secrets.token_hex(24)
    env = {**os.environ, "POSTGRES_DB": "shiftflow_ci", "POSTGRES_USER": "shiftflow_ci",
           "POSTGRES_PASSWORD": password, "RUN_POSTGRES_TESTS": "0" if args.suite == "benchmarks" else "1", "DOTNET_PROCESSOR_COUNT": "2",
           "DOTNET_NOLOGO": "true", "DOTNET_CLI_TELEMETRY_OPTOUT": "true",
           "SCHEDULING_VALIDATION_REPORT_DIRECTORY": str(output / "solver")}
    env["TEST_TIMING_DIRECTORY"] = str(output)
    # Do not inherit any persistent connection. TestDatabase creates a separate DB per test.
    env.pop("ConnectionStrings__Database", None)
    env.pop("TEST_POSTGRES_CONNECTION_STRING", None)
    child = None
    created = False
    attempted = False
    code = 1

    def docker(*arguments):
        return subprocess.check_output(["docker", *arguments], env=env, stderr=subprocess.DEVNULL).decode().strip()

    def interrupted(signum, _frame):
        raise InterruptedError(f"Signal {signum}")

    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, interrupted)
    try:
        if args.suite != "benchmarks":
            attempted = True
            docker("run", "-d", "--name", name, "--label", LABEL + "=" + run,
                   "--memory=512m", "--cpus=2", "--tmpfs", "/var/lib/postgresql/data:rw",
                   "-p", "127.0.0.1::5432", "-e", "POSTGRES_DB", "-e", "POSTGRES_USER", "-e", "POSTGRES_PASSWORD", IMAGE)
            created = True
            binding = docker("port", name, "5432/tcp")
            host, port = binding.split(":")
            if host != "127.0.0.1" or int(port) in (5432, 55432):
                raise RuntimeError("Refusing unsafe database binding")
            env["TEST_POSTGRES_CONNECTION_STRING"] = f"Host=127.0.0.1;Port={port};Database=shiftflow_ci;Username=shiftflow_ci;Password={password}"
            deadline = time.monotonic() + 60
            while subprocess.run(["docker", "exec", name, "pg_isready", "-U", "shiftflow_ci", "-d", "shiftflow_ci"],
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
                if time.monotonic() > deadline:
                    raise TimeoutError("Disposable database readiness timed out")
                time.sleep(0.2)
        command = ["dotnet", "test", "ShiftManager.slnx", "-c", "Release", "--no-build", "--no-restore",
                   "--results-directory", str(output), "--logger", "trx;LogFileName=backend.trx"]
        selection = {"all": "", "correctness": "Category!=SchedulingBenchmark", "benchmarks": "Category=SchedulingBenchmark"}[args.suite]
        if args.filter:
            selection = f"({selection})&({args.filter})" if selection else args.filter
        if selection:
            command += ["--filter", selection]
        started = time.monotonic()
        # Raw framework output stays private: only timings and fixed status fields are exported.
        with open(output / "private.log", "w", opener=lambda p, f: os.open(p, f, 0o600)) as log:
            child = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            code = child.wait(timeout=args.timeout)
        report = {"suite": args.suite, "wallSeconds": time.monotonic() - started, "exitCode": code,
                  "platform": platform.platform(), "architecture": platform.machine(),
                  "processorLimit": 2, "databaseMemoryMiB": 512 if attempted else None, "image": IMAGE if attempted else None,
                  "timingNote": "TRX durations exclude asynchronous fixture initialization/disposal; explicit fixture timings are separate. Parallel class sums overlap."}
        trx = output / "backend.trx"
        if trx.exists():
            report.update(summarize_trx(trx))
            if int(report["counts"].get("executed", "0")) == 0:
                code = 1
        else:
            code = 1
        report["exitCode"] = code
        (output / "timings.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps({k: report[k] for k in ("suite", "wallSeconds", "exitCode")}))
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError, InterruptedError):
        print("Isolated profiling failed; private diagnostics remain in TestResults.")
        code = 1
    finally:
        if child is not None and child.poll() is None:
            os.killpg(child.pid, signal.SIGTERM)
            try:
                child.wait(timeout=10)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait(timeout=10)
        if attempted:
            try:
                owner = docker("inspect", "--format", '{{index .Config.Labels "' + LABEL + '"}}', name)
                if owner != run:
                    raise RuntimeError("Ownership mismatch")
                docker("rm", "-f", "-v", name)
            except (RuntimeError, subprocess.SubprocessError):
                if created:
                    print("Owned profiling container cleanup failed.")
                    code = 1
    return code


if __name__ == "__main__":
    raise SystemExit(main())
