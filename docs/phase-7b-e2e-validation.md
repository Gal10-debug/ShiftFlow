# Phase 7B — End-to-end workflow validation

## Architecture

Playwright Test 1.65.0 drives the actual Hebrew RTL React application in Chromium.
`frontend/e2e/run.mjs` provisions one PostgreSQL 17 Alpine container with a 512 MB
memory limit, two CPU limit, tmpfs data, and a dynamically allocated loopback port.
It starts one real Kestrel backend and one Vite frontend, reusing both across the
suite. Tests run with one worker, fresh browser contexts, no retries, and explicit
fixture resets before every test. No application API responses are mocked.

`tests/ShiftManager.E2EHost` uses `WebApplicationFactory<Program>.UseKestrel` to run
the production middleware, controllers, Identity cookies, antiforgery, services,
EF migrations, PostgreSQL constraints, and scheduling worker. Its separate assembly
adds fixture controls; the production application neither references nor exposes
them. Authentication and authorization are exercised through the real login UI.
Negative requests use the same browser context and real CSRF tokens, so a failed
business-rule check cannot be mistaken for missing authentication or CSRF.

The host and browser share the fixed instant `2030-12-01T10:00:00Z`, with a January
2031 scheduling period and Israel browser time zone. `page.clock.setFixedTime`
fixes dates while leaving polling and request timers running. Fixtures define a
separate ADMIN, two organizational leaders, a deputy, regular employees, qualified
employees, and an initial-password account. Stable fixture IDs identify accounts
and rows; generated assignment/publication IDs are read from actual responses.

The base fixture configures the availability window through `AvailabilityService`,
which also creates canonical day/night anchors. Published fixture profiles create
assignments, acknowledge review, and publish through the real scheduling service.
Exchange outcomes are never seeded or applied by writing assignments directly.
The full lifecycle test generates its own draft and publishes through the UI.
Each fixture gets a distinct loopback client-address partition so the real login
rate limiter starts independently; its ten-attempt policy remains intact inside a
test. Rate limiting itself remains covered by backend authentication tests.

## Isolation and cleanup

The runner ignores externally supplied connection strings and generates a new
32-character run ID, database, role, database password, login passwords, and control
secret. Database and role names must both be `shiftflow_e2e_<run-id>`. The host refuses
non-loopback hosts, ports 5432/55432, short/missing credentials, mismatched identities,
and any nonempty database at initial startup. Nine guard cases (one valid and eight
unsafe targets) run without opening a database connection before every suite.

Before each schema reset, the host verifies the connection guard again and checks
the exact run ID in `shiftflow_e2e_control.owner`. It also requires the application's
actual EF connection to match the owned connection before resetting or migrating.
Only its `public` schema is reset;
the ownership schema survives. Migrations run only after that check. Unknown fixture
profiles are rejected before mutation. Controls require a constant-time comparison
of the per-run secret and are covered by an unauthorized-access browser test.

Backend, frontend, and frontend-to-backend proxy readiness each prove their run
identity. Tests verify the proxy identity before reset and before every login,
refusing to authenticate against an unrelated application. Data-protection keys
are written to the run's private directory with a distinct application name.
No persistent Docker volumes, demo credentials, or normal development ports are used.

Normal completion, test failure, and handled termination stop the owned processes.
Container removal checks its ID and exact run label. An interrupted GitHub job has
an additional cleanup step on its dedicated hosted runner. A hard OS kill cannot
execute a local process's cleanup: inspect `docker ps --filter label=shiftflow.e2e.run`
and verify the run label before manually removing a leftover test container.
Do not remove development or demo containers.

Read-only checks before and after the repeated browser run verified demo counts of 60 users, 1,397
assignments, three publications, and 29 absences, and development counts of eight
users, zero assignments, and zero publications. The development database predates
the absence table; it was not migrated. The demo remains at migration
`20261106074419_SchedulingPriority` and development at
`20261027224058_AdditionalSchedulingConstraints`. No E2E container remained after the run.

## Local execution

Prerequisites: .NET 10 SDK, Node.js 24+, Python 3, Docker, and sufficient resources
for one small PostgreSQL container plus Chromium and the application.

```sh
cd frontend
npm ci
npx playwright install chromium
npm run typecheck:e2e
npm run test:e2e:safety
npm run test:e2e
```

On Linux, install browser dependencies with `npx playwright install --with-deps chromium`.
The runner builds the host in Release with warnings as errors; no separate migration,
admin bootstrap, existing application server, or manually supplied password is needed.
Do not invoke Playwright directly against an existing application: configuration
refuses execution without the runner's disposable environment.

Useful focused/repeated runs still create a fresh isolated environment:

```sh
npm run test:e2e -- --grep "Exchange"
npm run test:e2e -- --repeat-each=2
```

For local inspection only, `node e2e/run.mjs --serve` prints the disposable frontend
URL and the path to a private control file containing only E2E variables. Open it in
a separate private browser window. Stop with
Ctrl+C. `SHIFTFLOW_E2E_KEEP_PRIVATE=1` retains raw diagnostics for local debugging;
these contain disposable credentials and must never be uploaded, committed, or shared.
Normal runs remove them after producing sanitized diagnostics.

## Reports and secret handling

Playwright retains screenshots and traces on failure and produces a JSON report.
All raw logs, reports, traces, and data-protection keys start beneath the ignored
mode-0700 `.e2e-private/<run-id>/` directory. Child output is captured there instead
of streaming request details to CI. Password fields stay masked; tests never toggle
password visibility. Videos and source capture are disabled.

`frontend/e2e/sanitize.py` discovers cookie/header/token values in trace records and
resources, redacts credential keys, removes known environment secrets from text,
and rewrites trace ZIP contents while retaining resource filenames. Cookie arrays,
Set-Cookie/authorization/CSRF/control headers, and login/password-change payloads
are scrubbed. Only PNG/JPEG binary resources are allowed; unsupported binary data
fails closed. Three focused Python tests verify redaction and binary refusal.

Publication happens from a sanitized staging directory only. The public
`frontend/e2e-report/` contains `index.html`, `report.json`, sanitized service logs,
and failure attachments when applicable. The report is a static index and JSON
detail view. Open a sanitized trace with `npx playwright show-trace <trace.zip>`.
During tests, the runner forwards only completed-case counts and a fixed status
vocabulary, so CI shows progress without request data or test error text.
Detailed errors are printed only after sanitization. CI uploads
only this public directory, never the private captures or a browser storage state.
Future tests involving secrets displayed as ordinary text require another artifact
review: text redaction does not redact pixels in screenshots.

## Coverage: 28 browser tests

| Area | Count | Workflow and assertions |
| --- | ---: | --- |
| Authentication, roles, fixture safety, availability | 11 | Invalid/valid login, session reload, logout, protected routes, initial password change, separate admin/leader/deputy/regular/qualified accounts, role navigation and API denial, fixture-secret denial, admin window open/close, submit/reload, three-block Sunday-week limit and server refusal without persistence, closed-window employee denial, own-team leader/deputy updates and cross-team denial. |
| Absences, exchanges, employee visibility | 10 | One-time/weekly/partial/overnight proposals and persistence, pending withdrawal, leader/deputy approval, modification and cancellation preserving the prior approved rule, rejection without a block, approved absence blocking manual work, other-team/employee approval denial, explicit draft-removal confirmation, pending warnings and immutable published conflicts, recipient consent/rejection, administrator approval/rejection, explicit exchange republication, unavailable/unqualified/conflicting/unauthorized replacement refusal, personal issue ownership and protected API denial. |
| Scheduling and publication | 7 | Generate/preview/explicit draft save/reload, preliminary publication, employee view, correction/final publication and immutable earlier version, shortage indicators/capacity/zero demand/qualified users, Critical/High/Normal configuration independent of row order and priority coverage under shortage, manual add/replace/move/remove/reload and candidate restrictions, overlap/consecutive/qualification/zero-capacity refusals, protected preliminary roster, hide and soft-delete retaining historical snapshots. |

The employee product intentionally shows the shared published roster, highlighting
personal assignments. Tests assert personal issue/report ownership and administrative
API protection; they do not invent a policy hiding other employees' published names.
There is no task view/editor on the current personal schedule or task HTTP controller;
task workflows are therefore not fabricated in this phase. Assignment notes are
covered across correction, final publication, and historical-version selection.

## Reused scheduling regressions

The existing backend suite provides the expensive constraint and concurrency coverage:

- `SchedulingValidationTests`, `SchedulingValidationIntegrationTests`: independent
  oracle, two weekly nights, weekend limits, manual-only rows 12–14, protected work,
  previous-month/year commitments, overnight/partial absence boundaries and Israel DST.
- `ConstraintIntegrationTests`, `AdditionalSchedulingRulesTests`,
  `ExchangePairIntegrationTests`: weekly 6–9 rule, paired Friday/Saturday on-call,
  cross-month atomicity, weekend quotas and pair exchange republication.
- `RehabilitationMorningTests`, `WeekendBoardIntegrationTests`: stable rehabilitation
  identity, Friday staffing, Saturday exclusion, and board applicability.
- `ShiftLeadIntegrationTests`, `StaffingCapacityIntegrationTests`,
  `AbsenceSchedulingIntegrationTests`: qualifications, capacity, absence conflicts.
- `SchedulingStressTests`, priority optimization/integration tests and benchmarks:
  legal partial solutions, priority preservation, determinism in controlled cases,
  and proof-aware quality reporting. Browser tests do not require global OPTIMAL.

Row 26 remains an ordinary scheduling row. No optimizer, fairness measure, hard rule,
administrator override, analytics, statistics dashboard, or LLM feature was changed.

## CI

`.github/workflows/ci.yml` retains the Backend and Frontend jobs and adds the
independent **Browser workflows** job for the existing pull-request/main triggers.
It installs .NET 10, Node 24, locked npm packages and Chromium with system dependencies,
checks E2E types and artifact redaction, then invokes the same local runner.
The runner provisions PostgreSQL, migrates, seeds, starts applications, tests, sanitizes,
and cleans up. No repository/database secrets are needed. Pinned upload-artifact v4
publishes sanitized reports with seven-day retention, including on failure.
The workflow was checked with actionlint, including expression-context validation.

No remote branch was updated and no PR or CI run was created. CI success can only
be established after the user authorizes pushing and GitHub actually runs this job.

## Defects and validation results

No production application defect has been demonstrated by the completed workflows.
Harness fixes during development included the host content root, canonical fixture
anchors, exact Hebrew accessible selectors, awaiting refreshed board state before
publication acknowledgement, and restoring a selected week after page reload.
A development-time Vite configuration reload also motivated a dedicated test frontend
configuration plus proxy-ownership proofs; unrelated services must never be reused.
Application authorization and scheduling rules were not relaxed to make tests pass.

All 28 browser tests passed in a full run and then in a repeat run: 56/56 repeated
executions passed, zero failed/skipped/flaky, in 353.6 seconds under local machine
load. All 127 frontend tests passed with `npm test -- --maxWorkers=1`, and the
production frontend built. An earlier concurrent frontend/browser run hit three
frontend timing failures; running checks sequentially with one frontend worker
resolved them without changing assertions or application code. Three redaction
tests, nine connection-guard cases, and seven development-script tests also passed.
The complete Release backend suite passed: **392 passed, zero failed, zero skipped**
in 14 minutes 3 seconds. It used a separately created, owned PostgreSQL container,
`RUN_POSTGRES_TESTS=1`, the guarded disposable `shiftflow_ci` service database, and
`DOTNET_PROCESSOR_COUNT=2`. All six 30/60/120-second benchmark trials completed.
The solution built with zero warnings/errors, clean-database migration succeeded,
and EF reported no pending model changes. The validation container was removed.
The final focused exchange/manual run passed **2/2**, zero failed/skipped, in
40.3 seconds. It exercised the final EF-connection identity guard, safe live CI
progress, cleanup, exact exchange refusal codes, and the manual cell selector.
The sanitized repeated-run report is preserved locally at
`TestResults/phase7b-e2e-repeat/index.html`; `frontend/e2e-report/index.html` contains
the latest focused run. Both report directories are ignored by Git.

## Limits and acceptance recommendation

Chromium is the only browser in this resource-conscious suite; mobile, Firefox,
WebKit, visual baselines, load tests, and future statistics/tasks screens are outside
this phase. Large solver quality, cross-month exchange pairs, DST and quota permutations
remain covered by targeted backend tests rather than duplicate browser suites.
Browser tests use Development-mode HTTP on loopback; production HTTPS and secure-cookie
behavior remain covered by the existing authentication integration tests, not a
production deployment smoke test.

Local workflow, repeated browser execution, frontend, and backend checks are green.
No critical unresolved application defect was identified by this validation.
Final Phase 7B acceptance should wait for the new GitHub job to pass on the pushed
commit. A valid workflow file and local success alone are not a claim of CI success.
