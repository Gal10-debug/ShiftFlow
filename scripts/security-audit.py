#!/usr/bin/env python3
"""Audit dependencies and tracked source without printing secret values."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gitleaks", default="gitleaks", help="Path to checksum-verified Gitleaks 8.30.1")
    parser.add_argument("--trivy", help="Optional path to Trivy for image review; image findings are reported separately")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    reports = root / "TestResults/security"
    reports.mkdir(parents=True, exist_ok=True)
    failures = []

    def run(command, cwd=root, allowed=(0,)):
        result = subprocess.run(command, cwd=cwd, capture_output=True, text=True)
        if result.returncode not in allowed:
            raise RuntimeError("Audit command failed: " + Path(command[0]).name)
        return result

    # Keep raw tool output out of stdout and CI artifacts; publish selected fields.
    dotnet = json.loads(run(["dotnet", "list", "ShiftManager.slnx", "package", "--vulnerable", "--include-transitive", "--format", "json"]).stdout)
    if not dotnet.get("projects") or any(log.get("level", "").lower() in ("error", "warning") for log in dotnet.get("logs", [])):
        raise RuntimeError("NuGet audit did not produce a complete report")
    packages = []
    for project in dotnet["projects"]:
        for framework in project.get("frameworks", []):
            for item in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
                for vulnerability in item.get("vulnerabilities", []):
                    finding = {"project": Path(project["path"]).name, "package": item["id"], "version": item.get("resolvedVersion"),
                               "severity": vulnerability["severity"], "advisory": vulnerability.get("advisoryurl")}
                    packages.append(finding)
                    if finding["severity"].lower() in ("high", "critical"):
                        failures.append("NuGet high/critical advisory")
    print("NuGet dependency advisories:", len(packages))

    npm = json.loads(run(["npm", "audit", "--json"], root / "frontend", (0, 1)).stdout)
    if npm.get("error") or npm.get("auditReportVersion") != 2:
        raise RuntimeError("npm audit did not produce a complete report")
    npm_counts = npm["metadata"]["vulnerabilities"]
    print("npm dependency advisory counts:", json.dumps(npm_counts))
    if npm_counts["high"] or npm_counts["critical"]:
        failures.append("npm high/critical advisory")

    version = run([args.gitleaks, "version"]).stdout.strip()
    if version != "8.30.1":
        raise RuntimeError("Use the reviewed Gitleaks 8.30.1 binary")
    history = reports / "gitleaks-history.json"
    run([args.gitleaks, "git", "--log-opts=--all", "--no-banner", "--redact=100", "--log-level", "error", "--report-format", "json", "--report-path", str(history)], allowed=(0, 1))
    paths = run(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"]).stdout.split("\0")
    source_report = reports / "gitleaks-source.json"
    with tempfile.TemporaryDirectory(prefix="shiftflow-secret-scan-") as directory:
        staging = Path(directory)
        for relative in filter(None, paths):
            source = root / relative
            if source.is_symlink() or not source.is_file():
                continue
            if not source.resolve().is_relative_to(root):
                raise RuntimeError("Source path escaped the repository")
            destination = staging / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
        run([args.gitleaks, "dir", str(staging), "--no-banner", "--redact=100", "--log-level", "error", "--report-format", "json", "--report-path", str(source_report)], allowed=(0, 1))
    secret_findings = []
    for name, report in [("history", history), ("working source", source_report)]:
        findings = json.loads(report.read_text())
        print(f"Gitleaks {name} findings:", len(findings))
        for finding in findings:
            # Deliberately omit Match, Secret, author details and line contents.
            location = finding["File"]
            if name == "working source":
                try:
                    location = str(Path(location).relative_to(staging))
                except ValueError:
                    pass
            secret_findings.append({"scope": name, "file": location, "line": finding["StartLine"], "rule": finding["RuleID"]})
            print(f"Potential secret ({name}): {location}:{finding['StartLine']} [{finding['RuleID']}]")
        if findings:
            failures.append("Potential committed/source secret; inspect redacted locations and rotate if live")
    for relative in filter(None, paths):
        name = Path(relative).name
        if relative.startswith((".demo/", ".e2e-private/")) or (name == ".env" or name.startswith(".env.")) and not name.endswith(".example"):
            failures.append("Private/local configuration tracked: " + relative)
    for sample in [".env", ".env.security", ".demo/accounts.json", ".e2e-private/run/results/trace.zip"]:
        if run(["git", "check-ignore", "--no-index", sample], allowed=(0, 1)).returncode != 0:
            failures.append("Missing ignore protection: " + sample)

    images = []
    if args.trivy:
        report = reports / "postgres-image.json"
        run([args.trivy, "image", "--scanners", "vuln", "--severity", "HIGH,CRITICAL", "--quiet", "--timeout", "5m",
             "--format", "json", "--output", str(report), "postgres:17-alpine"])
        result = json.loads(report.read_text())
        for target in result.get("Results", []):
            for finding in target.get("Vulnerabilities", []):
                images.append({"target": target["Target"], "id": finding["VulnerabilityID"], "package": finding["PkgName"],
                               "version": finding["InstalledVersion"], "fixedVersion": finding.get("FixedVersion"), "severity": finding["Severity"]})
        print("Image high/critical package findings requiring exposure review:", len(images))
        # A bundled startup helper's unused TLS library is not evidence of an
        # exploitable application endpoint. Image findings need explicit triage.
    summary = {"nuget": packages, "npm": npm_counts, "secrets": secret_findings, "imageFindings": images, "failures": failures}
    (reports / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    for failure in failures:
        print(failure, file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, OSError, ValueError, KeyError) as error:
        # A scanner/feed failure must not be reported as a clean audit.
        print(f"Security audit incomplete ({type(error).__name__}). Check tool/feed availability; no clean result is claimed.", file=sys.stderr)
        raise SystemExit(2)
