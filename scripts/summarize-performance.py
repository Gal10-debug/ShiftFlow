#!/usr/bin/env python3
"""Export timing fields from TRX; never publish test output, failure bodies or credentials."""
import argparse
import glob
import json
import os
from pathlib import Path

from performance import summarize_trx


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    files = sorted(glob.glob(args.trx, recursive=True))
    if not files:
        parser.error("No TRX results found")
    reports = [summarize_trx(file) for file in files]
    if not any(int(report["counts"].get("executed", "0")) for report in reports):
        parser.error("No tests executed; refusing an empty timing report")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(reports, indent=2) + "\n")
    lines = ["## Backend test timing", "", "TRX durations exclude asynchronous fixture initialization/disposal. See separate fixture timings; parallel class sums are not wall time.", "",
             "| Test | Seconds | Outcome |", "| --- | ---: | --- |"]
    for test in sorted((t for r in reports for t in r["tests"]), key=lambda t: -t["seconds"])[:20]:
        name = test["name"].replace("|", "\\|").replace("\n", " ")
        lines.append(f"| {name} | {test['seconds']:.3f} | {test['outcome']} |")
    summary = "\n".join(lines) + "\n"
    print(summary)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as stream:
            stream.write(summary)


if __name__ == "__main__":
    main()
