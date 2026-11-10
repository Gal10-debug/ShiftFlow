# Phase 9A: delegated availability workflow

## Base and scope

The clean base was `7e75f3e40d139c11dffc9e1d26ad67f6f7ed4c5c`, the Phase 8B merge.
Read-only `git ls-remote origin refs/heads/main` verified the same remote commit.
Phase 4C team management and delegated availability are present in that history.
Local main already matched, so no fast-forward or remote-tracking update was needed.
Implementation belongs to `feature/phase-9a-availability-workflow`.

## Previous and current workflow

Previously, employees could explicitly POST their own monthly availability and PUT
replacements while the administrator-controlled window was open. Team leads and
deputies could already enter and correct availability for their team, including
after the window closed. Administrators had global inspection and window management.

For the December 2026 pilot, employees communicate constraints to their team lead or
deputy outside ShiftFlow. The representative records them through the existing team
availability editor. Employees review the saved values and contact their representative
outside the application if a correction is needed. There is no employee submission,
approval, acknowledgment, confirmation tracking, or correction-request workflow.
Absence requests remain a separate employee self-service workflow.

## Permissions and authoritative membership

| Actor | Availability permissions |
| --- | --- |
| Employee | Read own periods, saved blocks, preferences, status and update time. No personal create, replacement or deletion. |
| Team lead | Read and explicitly create/update eligible members of the currently led team. Excluded from scheduling workforce. |
| Deputy | Same delegated access for the currently deputized team, including their own entry through the team editor. Remains eligible for scheduling. Personal page is read-only. |
| Administrator | Existing global read, window configuration, team/user administration, scheduling and authorized schedule correction. |
| Shift coordinator | No availability write capability from the coordinator role. An explicit deputy appointment independently confers team authority. |

The established administrator boundary is preserved: ADMIN alone does not permit
cross-team availability entry, and existing `ADMIN_NOT_TEAM_MEMBER` checks remain.
Administrative **schedule** corrections remain available through their established
API and audit workflow; this phase adds no administrator availability override.

Delegated mutations continue to use `TeamService.AuthorizeTarget` under the global
account lock. Current database membership and leadership appointments determine
access, not client-supplied team IDs or stale navigation flags. Transfers and
appointment revocation take effect on subsequent requests. Read summaries authorize
and query membership in one repeatable-read transaction. No target list from the
browser is trusted as authorization.

## APIs

- Personal GET `/api/availability/periods` and `/{year}/{month}` are unchanged.
- Legacy personal POST/PUT `/{year}/{month}` now return `403 FORBIDDEN` from the
  service before validation, locks or data mutations. Authentication, password
  change, CSRF and JSON contract validation still run at the HTTP boundary.
- No DELETE availability endpoint exists. Attempts return the existing `404` and
  cannot delete a submission or preference.
- Existing delegated GET/POST/PUT `/api/team-availability/employees/{userId}/...`
  retain current target authorization, optimistic versions and atomic audit writes.
- GET `/api/team-availability/{year}/{month}` reuses `WindowSummaryResponse` and
  `AvailabilityService.Summary` to provide recorded/missing statuses for the actor's
  eligible team members only. It adds no storage or parallel availability model.
- Administrator and absence APIs retain their existing permissions and behavior.

## Windows, states and scheduling semantics

Administrators still configure, open and close windows. Window states remain visible
to employees and representatives. Personal writes are denied regardless of window
state. Delegated entry remains available before opening and after closure/expiry,
subject to existing schedule-state restrictions: DRAFT, PRELIMINARY_PUBLISHED and
UNDER_CORRECTION allow it; final periods reject it. Published snapshots remain immutable.

`AvailabilitySubmission`, `ShiftPreference` and `DelegatedAvailabilityAudit` remain
authoritative. Delegated saves create/update the same submission, version and timestamp
already consumed by scheduling. Employees need not submit or approve anything again.
An explicitly recorded empty selection is a valid all-available submission; a missing
submission remains missing. Existing records retain their meaning and values.

The Sunday–Saturday maximum of three blocked shifts, cross-month/year counting,
preference behavior, day/night times and DST boundaries are unchanged. Delegated
blocks retain existing working assignments and surface conflicts for administrative
resolution; they never silently alter a publication. The solver, its objective
hierarchy, workforce eligibility, input fingerprint and existing constraints are unchanged.

## Hebrew RTL UI

The personal heading/navigation label is `אילוצי השיבוץ שלי`. The page displays the
selected month, window state, recorded/missing status, last update, disabled shift
selectors showing saved blocks/preferences, weekly counters and month navigation.
Submission, update and reset actions are absent. Hebrew guidance directs corrections
to the team lead or deputy. Missing entries have an explicit empty-state explanation
so default available-looking cells do not imply a recorded submission.

The existing team editor keeps explicit submission/update, error/success feedback,
version checks, weekly display and closed-window editing. After selecting a member,
the monthly team summary marks recorded/missing entries in the member selector and
shows a total. It refreshes after saving or changing months with one summary request,
without requesting every member's calendar. Team groups and names remain available
for approximately four teams and 60 users. Existing responsive calendar styles and
RTL layout are reused.

## Absence preservation

No absence controller, service, model or approval rule changes. Employees continue
to submit and view their own requests. Independent approval, team-scoped authority,
conflict checks and audit/session protections remain enforced. Backend regression
and browser workflows cover these independently of availability, including a worker
submitting an absence after reviewing corrected delegated constraints.

## Verification

The regression suite includes worker own reads; denied personal POST/PUT/DELETE and
foreign delegated writes; coordinator denial; leader/deputy creation and correction;
closed/expired window behavior; current membership transfers and appointment removal;
admin window, inspection and schedule corrections; absence self-service and independent
approval. Unauthorized regression cases fingerprint all mapped database tables and
assert no changes. Direct personal service calls are denied too.

Existing availability semantics tests now record through an authorized team lead,
preserving all-available, preferences, weekly/cross-month/year limits, concurrent
updates, audit rollback, stale versions, hard blocks and legacy migration coverage.
Scheduling/publication fixtures use delegated writes and assert setup success.
Frontend tests cover read-only open/closed/empty/recorded personal states and delegated
editing. Playwright covers lead entry, worker review/no editing, deputy correction,
worker seeing the correction, team scoping, window controls and absence self-service.
The performance and Production validation harnesses also use delegated entry. The
development demo generator records through each team's authorized lead; its existing
isolation and cleanup guards are unchanged. It was exercised only in disposable tests.

Local validation completed on November 9, 2026:

| Check | Result |
| --- | --- |
| Release backend build, warnings as errors | PASS, zero warnings/errors |
| Backend correctness/security/integration | 491 passed, zero failed/skipped; 3 min 8 s |
| Frontend Vitest | 155 passed across 11 files |
| Frontend TypeScript/Vite production build | PASS |
| E2E TypeScript | PASS |
| Full Playwright workflows | 36 passed, zero skipped/flaky; 76.989 s |
| Phase 7D Production browser performance/recovery | 2 passed; 42.119 s |
| Production application/proxy image builds | PASS |
| Production pilot package validation | 15 checks passed, cleanup PASS |
| EF pending-model check on disposable PostgreSQL | No pending model changes |
| E2E artifact-safety tests | 4 passed |
| Pilot operation guards | 20 passed |
| Performance timing parser | 2 passed |

The initial backend/browser runs exposed an incorrect expected DELETE status and
legacy availability setup calls in constraint, demo and harness code. These were
corrected before the successful runs above; no regression coverage was removed.

The unchanged benchmark fact passed once in 4 min 19 s, executing exactly six
experiments: normal and severe-shortage scenarios at 30/60/120-second budgets. All
normal runs were OPTIMAL with `PROVEN_TARGET_MET`; all shortage runs were FEASIBLE
with no optimality proof claimed. Independent schedule validity assertions passed.
The backend total is 492 passing test cases including this benchmark fact. Raw
synthetic diagnostics and TRX evidence are under ignored `TestResults/phase9a/`.

CI definitions and Phase 7D performance mechanisms are unchanged. Local results do
not claim that remote GitHub Actions ran or passed.

## Data migration and isolation

No schema migration, data conversion or reseeding is needed. Old submissions remain
valid. Database-writing tests use owned disposable PostgreSQL resources with unique
container identities and random loopback ports. Backend fixtures create fresh databases
inside the owned test service; the E2E harness owns a separate tmpfs container. The
Production validation harness owns labeled synthetic containers, databases, networks
and volumes and verifies ownership before cleanup. No persistent development/demo
database, existing published schedule, volume or PostgreSQL service is a test target.

## Remaining pilot risks and next phase

- External communication is intentionally the source of requested changes; there is
  no in-app correction queue or employee confirmation tracking.
- Representatives must explicitly record all-available months so missing entries do
  not block publication readiness. Coverage depends on current team appointments.
- Existing severe-shortage benchmark runs can end with a valid feasible solution
  without an optimality proof; this phase does not change that solver limitation.
- Existing version conflicts require refreshing and reapplying changes. Finalized
  schedules still require the established operational correction process.
- Delegated blocks can expose retained assignment conflicts requiring administrator
  resolution before publication; this behavior is preserved.
- The team status summary refreshes after saves/month changes; concurrent edits from
  another browser appear when reselecting/reloading. The selected calendar polls
  saved values every 15 seconds.

Recommended next pilot phase: a supervised December rehearsal with four team
representatives, including recording coverage, worker review, independent absence
approval, scheduling, conflict resolution and publication. Confirm operational owners
and response times for corrections, and review the existing solver fairness evidence
before pilot acceptance.
