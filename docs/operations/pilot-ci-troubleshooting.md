# Pilot CI failure diagnostics

The required `Backend` job is an aggregate. It intentionally fails when correctness,
any benchmark matrix job, or the Production pilot package job fails or is skipped.
Fix the failing dependency; do not weaken that gate.

## Initial Phase 7E run

[Run 42502015259](https://github.com/Gal10-debug/ShiftFlow/actions/runs/42502015259)
at commit `729d5a1` passed backend correctness, all three benchmark budget jobs,
frontend, browser workflows and dependency/secret checks. The Production pilot
step failed about nine seconds after starting. Its status-only artifact recorded
one successful Compose configuration check, a generic `RuntimeError` from a pilot
operation, and successful owned-resource cleanup. The precise setup operation and
raw stderr were not retained publicly. This evidence does **not** establish a
particular database, runtime, permissions or registry fault.

Follow-up local checks passed the complete 15-check Production workflow/recovery
drill, setup with native Linux-backed secret files, Compose v2.40.3 on ARM64 and
AMD64, and setup using an exact AMD64 build of the Production Dockerfile. The
AMD64 containers ran under local Docker Desktop emulation, not a GitHub-hosted
runner. The original CI failure remains unconfirmed and requires a new CI run.

## Reading a failure after the diagnostic change

The workflow pulls the pinned PostgreSQL image in a separate visible step, so
registry failures are reported without requiring access to private test logs.
The harness then prints the attempted setup stage (PostgreSQL start/readiness,
provisioning, migration, bootstrap, or app/proxy startup), exception type and fixed
diagnostic codes. Its artifact records these fields under `failure`. Codes flag
recognized permissions, authentication, image/runtime, storage and configuration
messages; they are indicators, not a substitute for confirming the cause.

Only fixed codes are derived from raw logs. Exception messages and log lines are
never copied into the public failure fields. Passwords, connection strings,
certificates, cookie jars, SQL contents and private framework diagnostics remain
private. Unknown and non-UTF-8 output also remains private. Cleanup failure is
reported explicitly and still causes a nonzero exit.

Push the reviewed fix only with the developer's approval and inspect the CI run
for that **new commit**. Re-running the old run executes the old commit and will
not include the new diagnostics. Preserve the strict aggregate and the existing
two-scenario-per-budget benchmark matrix. Never publish `.pilot-private` to obtain
more information.
