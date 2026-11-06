# Phase 7A: scheduling algorithm validation

Historical Phase 7A baseline follows. The [Phase 7A.1 section](#phase-7a1-configurable-priorities-and-optimization) below documents the current priority model, improvements and remaining acceptance conditions.

Validation date: 2026-11-06. Base: `c15868645ec27cc023b7c186247651d9d09d3c95`
(merged Phase 6.1). Branch: `feature/phase-7a-scheduling-validation`.
Phase 4C team/delegated-availability implementation and documentation are present.
Local `main`, `origin/main`, and the read-only remote `refs/heads/main` query agreed
before branching. No remote refs, application policies, production code, migrations,
or existing databases were changed in this phase.

## Actual architecture and rules

The behavior below was checked against the implementation, including
`SchedulingProblem.cs`, `ScheduleOptimizer.cs`, `SchedulingRules.cs`,
`GeneratedDraftService.cs`, `GenerationQueue.cs`, `ShiftTimeRanges.cs`,
`AbsenceRules.cs`, `ScheduleReviewService.cs`, publication controls and exchanges.

- `SchedulingInputs.Read` reads the current definitions, roles, account state,
  teams, submitted availability, current/neighboring work, and approved absences.
  Its interval extends from the previous month's start minus seven days through
  the following month's end plus six days. Active EMPLOYEE membership is required;
  ADMIN membership and organizational leadership exclude an employee. A deputy
  remains eligible, subject to the same qualification and availability rules.
- Stable UUIDs identify the seven priority rows, rehabilitation morning (8), and
  the weekend allowlist. Display order and names do not determine these rules.
  SIX_NINE and WEEKEND_ON_CALL use persisted semantic identifiers.
- Current configuration determines per-date demand, including zero demand and
  rows excluded from generation. The complete staffing map also bounds protected
  assignments in excluded/manual rows. Row 3 has weekend demand **one even when
  its configured weekday requirement is zero**. This existing exception is tested;
  zero-required exclusion refers to effective per-date demand.
- All generated work fits effective capacity. Shortages are allowed. There is no
  hard minimum-coverage constraint that forces an unavailable/unqualified person
  into a cell.
- Only qualified employees can staff a qualification-required row. That row
  qualification does not automatically designate the generated person as the
  shift's `IsShiftLead`; existing lead designations are protected.
- BLOCKED canonical availability intervals are hard restrictions, including
  legacy blocks without a submission. PREFERRED entries influence the objective
  only with a monthly submission. Custom ranges check every overlapping anchor,
  including the previous night for 06:00 starts. Pending availability drafts are
  not stored server-side.
- Approved, non-CANCEL absence rules block actual UTC interval overlaps. Local
  dates are inclusive; daily intervals are half-open. Recurring weekdays use
  Sunday=0. Partial-day intervals may cross midnight. Pending requests warn;
  REJECTED/CANCELLED requests do not block. An approved predecessor remains
  binding during a pending/rejected modification/cancellation. Approved changes
  cancel the predecessor atomically; approved CANCEL records are not restrictions.
- Assignments cannot overlap **or touch at an endpoint**, which prevents adjacent
  canonical 12-hour shifts. This is stricter than the half-open absence/availability
  overlap test and also applies to custom ranges. At most one membership per
  `(employee, local date, canonical DAY/NIGHT anchor)` is allowed, even if two
  custom intervals with that anchor do not overlap. There is no additional general
  minimum-rest-hours policy in this implementation.
- Calendar weeks run Sunday–Saturday. At most two NIGHT-anchor assignments and
  one SIX_NINE assignment per employee/week are allowed, including neighboring
  month/year work. H23_07 classifies as NIGHT.
- At most two weekend allocations per employee/calendar month. Ordinary Friday
  and Saturday assignments each count separately. Each on-call definition's
  Friday/Saturday pair counts once, in **Friday's month**. Both days must have the
  same employee. A boundary counterpart is a read-only existing commitment;
  generation does not silently create the other month's assignment.
- Ordinary weekend work is allowed for stable definitions 2, 3, 4, 5 and 16.
  Rehabilitation morning (stable definition 8, `שיקום בוקר`) is also allowed on
  Friday at configured capacity and prohibited on Saturday. On-call work is
  weekend-only. Manual-only definitions 12 (`עד 16`), 13 (`עד 13`) and 14
  (`זמין ערב`) have no automatic variables, regardless of their inclusion flag.
  Their manual work still faces capacity, eligibility, qualification, absence,
  availability, conflict and quota validation. Definition 26 retains its ordinary
  existing behavior; no new restriction was introduced.
- Work is replaceable only if generated, unlocked, not a designated lead, without
  tasks or a note, in the target month and in an included automatic definition
  with demand. Everything else remains fixed. Conflicting fixed work causes a
  rejection rather than deletion. Saving a preview rereads/fingerprints current
  inputs and validates before transactional replacement. Scheduling/account/period/
  employee locks protect concurrent operations.
- Publication snapshots are immutable JSON. Preliminary, final and repeated final
  correction publication are distinct lifecycle operations. Hidden/deleted latest
  publications never fall back to an older snapshot. Exchange consent/approval and
  republication revalidate current constraints; paired cross-month republication
  is atomic. Existing integration tests verify these behaviors and snapshot bytes.

## Exact optimization order

The seven stages are lexicographic, not a weighted sum:

1. Maximize all qualification-required row assignments.
2. Maximize qualification-required assignments in priority definitions 1–7.
3. Maximize ordinary assignments in priority definitions 1–7.
4. Maximize all ordinary assignments (the already pinned priority total is constant).
5. Minimize opportunity-normalized squared total workload.
6. Maximize overlapping submitted preferences.
7. Maximize retained replaceable assignments.

Definitions 1–7 form one group; there is no internal row-1-before-row-2 ranking.
An optimum is constrained to its exact value before the next stage begins. A
FEASIBLE/UNKNOWN result or exhausted shared budget stops further stages. Thus a
lower objective cannot purchase a reduction in a *proven* higher optimum. If an
earlier stage is unproven, even its coverage may improve with more time.
`CoverageOptimal` requires four proven stages; `OPTIMAL` requires all seven.
Precondition conflicts produce a domain error; an infeasible CP-SAT model without
an incumbent produces `EXISTING_CONSTRAINT_CONFLICT`. No incumbent before the
deadline produces TIMEOUT, not an empty successful schedule.

Fairness uses `floor(1000 * load² / maxLoad)`, where load includes all retained work
in the input horizon. `maxLoad` adds a greedy non-touching opportunity capacity to
that retained count. It is an approximation: it does not calculate an employee's
exact feasible quota/pair/anchor capacity. Night, weekend and priority-assignment
distributions have **no separate fairness objectives**. No priority or fairness
policy was changed to improve the measurements.

## Coverage additions and independent validation

Before Phase 7A the full PostgreSQL-enabled baseline passed 307 tests. It already
covered individual rules, stale saves, manual edits, publication/correction/
withdrawal workflows, immutable snapshots, exchange validation, rollback and
cross-month pairs. Missing evidence was broad monthly stress measurement,
independent output checks, repeatability under controlled settings, and fairness
metrics stratified by eligibility.

| Area | Focused Phase 7A coverage | Existing regression coverage retained |
| --- | --- | --- |
| Eligibility | Actual DB inputs exclude inactive, ADMIN and leaders; deputy retained | TeamIntegration, OperationalCorrections, ShiftLead |
| Absences | Real pending/rejected/cancelled/approved replacement lifecycle into solver; partial endpoints; overnight DST; recurring studies/reserve workloads | AbsenceRules, AbsenceIntegration, AbsenceSchedulingIntegration |
| Conflicts/boundaries | Independent pairwise overlap/touch/anchor checks; previous-year night, SIX_NINE and touching work | ScheduleOptimizer, ConstraintIntegration, AdditionalSchedulingRules |
| Quotas/pairs | Independent weekly/monthly counts and same-user pairing; monthly/year boundary stress | AdditionalSchedulingRules, ConstraintIntegration, ExchangePair |
| Capacity/configuration | Latest input fingerprint and headcount after rename/reorder; zero-weekday/positive-weekend requirement | StaffingCapacity, StaffingCapacityIntegration, BoardBugFix |
| Special rows | All three manual-only identities retained/blocked by absences; definition 26 ordinary behavior; DST weekends | RehabilitationMorning, WeekendBoardRules (all 51 IDs), Phase6SchedulingIntegration |
| Priorities | Preferences and stability favor conflicting lower row but pinned coverage wins | PriorityScheduling (all seven IDs), expert-first optimizer cases |
| Protected work | Repeated DB generation/save preserves generated notes, tasks and original assignment IDs; controlled qualified lead retention | AutomaticSchedulingIntegration, excluded-row capacity guards |
| Fairness/repeatability | Total/night/weekend/priority distributions; comparable cohorts; three exact controlled repeats with/without protection; repeated full months | Existing small fairness/preference/stability tests |
| Lifecycle/history | Complete existing PostgreSQL suite rerun | Publication, Phase6Scheduling, OperationalCorrections, ExchangePair, immutable DB trigger tests |

`IndependentScheduleValidator` is test-only and does not call production eligibility,
conflict, absence, allocation, solver or draft-validation helpers. It independently
checks the output cell set/metadata, capacity, membership, qualification, approved
absence intervals, hard blocks, weekend applicability, overlaps/touching, canonical
membership, weekly limits, paired on-call work and protected identity/lead/note
preservation. Retained neighboring work is included without double-counting
protected output. Twenty deliberately invalid roster cases verify diagnostic
sensitivity, preventing an always-successful validator from passing the suite.
DB-level account/role filtering remains separately tested because those facts are
not present in the solver's Employee DTO. Production draft validation is also run
on every generated stress schedule.

## Scenario construction and evidence

All monthly inputs use seed **731**, stable synthetic employee identities, and
supported application ranges/definitions. Normally there are 56 eligible employees
from four teams (leaders removed), including 14 qualified people; severe shortage
uses six people, and insufficient-leads keeps one qualification. Automatic rows
are 1–8, 16 and 26. Manual-only rows 12–14 are retained in the staffing map and
excluded from automatic demand. DAY/NIGHT, H06_09, H07_13, H07_15, H15_23 and
H23_07 are represented. Weekday ordinary demand includes headcounts 1 and 2;
effective weekend rules produce zero and row-3-specific demand.

There are 300 planning cells in a 30-day month and 310 in a 31-day month (including
effective zero-demand cells). The fixture intentionally supplies these zero cells
to test the optimizer's defensive exclusion; the normal DB reader may omit an
entire definition with no demand. Availability scenarios block no more than three
anchors per employee/Sunday week. Recurring-study patterns are approved absences,
independent of that availability-submission limit. These are synthetic configured
workloads, not claims about the untouched demo database's present configuration.

The final monthly run uses the normal 30-second solver budget, one search worker,
CP-SAT seed 42, and serial test collections. Earlier eight-second exploratory runs
passed independent validation but often stopped before coverage was proven; they
are not used as the acceptance measurements below. Stopwatch runtime includes
model construction and result extraction, so it can slightly exceed the solver
budget. The API exposes overall status/proven-objective count, not per-stage gap,
last native termination code, or per-stage elapsed times. FEASIBLE measurements
therefore indicate an incumbent with unproven later stages, not a proof of optimal
coverage or fairness.

## Measured monthly results

Complete per-employee evidence is in [phase-7a-validation-results.json](phase-7a-validation-results.json).

| Scenario | Employees | Cells | Absences / blocks | Required | Filled | Missing | Status | Proven / 7 | Seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| autumn-dst | 56 | 310 | 0 / 0 | 296 | 296 | 0 | FEASIBLE | 4 | 30.012 |
| heavily-constrained | 56 | 300 | 47 / 840 | 294 | 294 | 0 | FEASIBLE | 4 | 29.843 |
| insufficient-leads | 56 | 300 | 0 / 0 | 294 | 258 | 36 | FEASIBLE | 4 | 30.023 |
| moderate-absences | 56 | 300 | 19 / 0 | 294 | 294 | 0 | FEASIBLE | 4 | 30.009 |
| normal-repeat | 56 | 300 | 0 / 0 | 294 | 294 | 0 | FEASIBLE | 4 | 30.012 |
| normal | 56 | 300 | 0 / 0 | 294 | 294 | 0 | FEASIBLE | 4 | 30.011 |
| overlapping-restrictions | 56 | 300 | 0 / 840 | 294 | 294 | 0 | FEASIBLE | 4 | 30.010 |
| protected-repeat | 56 | 300 | 0 / 0 | 294 | 294 | 0 | FEASIBLE | 4 | 30.015 |
| protected | 56 | 300 | 0 / 0 | 294 | 294 | 0 | FEASIBLE | 4 | 30.014 |
| reserve-duty | 56 | 300 | 18 / 0 | 294 | 294 | 0 | FEASIBLE | 5 | 30.008 |
| severe-shortage | 6 | 300 | 0 / 0 | 294 | 167 | 127 | FEASIBLE | 2 | 30.002 |
| spring-dst | 56 | 310 | 0 / 0 | 305 | 305 | 0 | FEASIBLE | 4 | 30.012 |
| studies | 56 | 300 | 28 / 0 | 294 | 294 | 0 | FEASIBLE | 4 | 30.010 |
| variable-headcount | 56 | 300 | 0 / 0 | 250 | 250 | 0 | FEASIBLE | 4 | 30.010 |
| widespread-absences | 56 | 300 | 44 / 0 | 294 | 266 | 28 | FEASIBLE | 4 | 30.002 |
| year-boundary | 56 | 310 | 0 / 0 | 296 | 296 | 0 | FEASIBLE | 4 | 30.019 |

All 16 generated schedules passed both validators with **zero hard-constraint violations**. Runtime ranged from 29.843 to 30.023 seconds (median 30.011). All returned FEASIBLE; none proved all seven objectives. Fifteen proved all four coverage objectives; severe shortage stopped after two. Reserve duty also proved the total-load fairness objective (five stages). A proven coverage optimum may still have unavoidable shortages, as in the insufficient-leads and widespread-absence cases. Severe shortage's 127 missing positions are **not proven minimal**.

Expected infeasibility is separately exercised by a minimal protected Friday on-call assignment whose Saturday mate is blocked. The pair equality is infeasible after fixed-work preconditions pass, and the engine must return `EXISTING_CONSTRAINT_CONFLICT` without dropping Friday. This differs from valid shortage schedules. Existing zero-budget/cancellation regressions also verify TIMEOUT and cancellation behavior.

## Fairness measurements and acceptance finding

Each distribution below is **minimum / median / maximum** across all employees, including those with zero opportunity. Total, night and priority metrics count monthly assignments; weekends count allocations, so an on-call pair contributes two assignments but one allocation. Raw evidence retains every employee ID, qualification and eligible-cell count.

| Scenario | Total assignments | Night assignments | Weekend allocations | Priority assignments |
| --- | --- | --- | --- | --- |
| autumn-dst | 2 / 4 / 23 | 0 / 2 / 7 | 0 / 1 / 2 | 1 / 4 / 15 |
| heavily-constrained | 2 / 5 / 8 | 0 / 2 / 5 | 0 / 1 / 2 | 1 / 4 / 8 |
| insufficient-leads | 0 / 2 / 27 | 0 / 1 / 7 | 0 / 0 / 2 | 0 / 1.5 / 24 |
| moderate-absences | 2 / 6 / 8 | 0 / 2 / 5 | 0 / 1 / 2 | 0 / 4 / 8 |
| normal-repeat | 0 / 3.5 / 27 | 0 / 1 / 8 | 0 / 1 / 2 | 0 / 3 / 19 |
| normal | 0 / 3.5 / 27 | 0 / 1 / 8 | 0 / 1 / 2 | 0 / 3 / 19 |
| overlapping-restrictions | 5 / 5 / 6 | 0 / 2 / 5 | 0 / 1 / 2 | 1 / 5 / 6 |
| protected-repeat | 0 / 1 / 26 | 0 / 1 / 10 | 0 / 0 / 2 | 0 / 0 / 22 |
| protected | 0 / 1 / 26 | 0 / 1 / 10 | 0 / 0 / 2 | 0 / 0 / 22 |
| reserve-duty | 2 / 7 / 7 | 0 / 1.5 / 5 | 0 / 1 / 2 | 0 / 4 / 7 |
| severe-shortage | 26 / 28 / 29 | 7 / 8.5 / 10 | 2 / 2 / 2 | 26 / 27.5 / 29 |
| spring-dst | 0 / 1 / 30 | 0 / 1 / 10 | 0 / 0.5 / 2 | 0 / 1 / 25 |
| studies | 0 / 1 / 24 | 0 / 1 / 10 | 0 / 0 / 2 | 0 / 0 / 20 |
| variable-headcount | 0 / 1 / 26 | 0 / 1 / 9 | 0 / 1 / 2 | 0 / 1 / 24 |
| widespread-absences | 0 / 0 / 23 | 0 / 0 / 9 | 0 / 0 / 2 | 0 / 0 / 23 |
| year-boundary | 0 / 3 / 25 | 0 / 1 / 10 | 0 / 1 / 2 | 0 / 2 / 18 |

The normal case has no absences, blocks or retained work. Within each qualification cohort, employees have identical scheduling opportunities. Its cohort results are:

| Cohort | Employees | Eligible cells each | Total | Nights | Weekends | Priority |
| --- | ---: | ---: | --- | --- | --- | --- |
| Unqualified | 42 | 182 | 0 / 3 / 27 | 0 / 1 / 8 | 0 / 0 / 2 | 0 / 2 / 19 |
| Qualified | 14 | 242 | 2 / 5 / 21 | 1 / 3 / 8 | 0 / 2 / 2 | 2 / 5 / 18 |

**High-severity pilot-readiness limitation:** the normal case leaves ten people idle while one comparably eligible unqualified person receives 27 assignments. Its unqualified cohort spans 0–27, and the qualified cohort 2–21. Availability and qualification differences do not explain these within-cohort spreads. Coverage is proven but fairness is not; the incumbent returned at the budget limit is operationally imbalanced. The protected case also has 26 idle people. This is not a hard-constraint violation or a false OPTIMAL claim, and no fairness policy has been rewritten. Reproduce with seed 731 and the normal/protected monthly fixture at 30 seconds. The immediate cause is termination during the fairness stage with an unproven incumbent; improving the optimization formulation or its performance requires a separate reviewed change.

In mixed-absence scenarios, aggregate spread alone does not demonstrate unfairness: compare qualifications, actual restricted dates and available intervals. `Opportunities` counts eligible cells before pair/weekly/monthly/conflict interactions; it is not an exact workload denominator or proof that two differently restricted employees are interchangeable. Reserve duty proves the implemented total fairness cost, but that still does not prove separate equitable night/weekend allocation. Widespread full-month absences correctly give affected employees zero assignments.

The controlled monthly-weekday cases with four comparably eligible employees prove all seven objectives and a total-load spread of at most one; a restricted employee is compared separately. This establishes small-case fairness correctness, not acceptable large-case convergence.

### Repeatability

Three controlled four-day runs with the same problem, one worker and seed 42 return identical assignment hashes and OPTIMAL in both the unprotected and protected-lead cases. The two full normal-month runs also produced the same assignment hash, as did the two protected-month runs:

- Normal: `606DF1B950CDC64382F0B7EC44AA25096F4C6F6950F382F76043ED06902842D4`.
- Protected: `AEBD041F4EE2BCE2662ED64F4C2FCEE95C49DB9F17A8A35733602105B71E6142`.

The monthly repeat fixtures have identical scheduling constraints; only their report/fingerprint scenario labels differ. Matching time-limited results on this host are observations, not a guarantee for future wall-clock-limited runs, other hardware, runtimes or OR-Tools versions.

### Acceptance decision

**Phase 7A validation is complete, but unconditional pilot acceptance is not recommended.** Hard constraints and workflow regressions pass. An operator decision is required on the demonstrated normal/protected workload imbalance and severe-shortage coverage uncertainty. Keep these as acceptance blockers until a separate optimization/performance change establishes acceptable comparable-employee distributions, or the user explicitly accepts a tightly supervised manual-review exception. No such exception, production data change or policy change was assumed.

## Test execution results

- **368 distinct backend cases passed, zero failures/skips**, including PostgreSQL
  integration. This is 61 additions to the 307-case baseline. The serial full run
  passed 367 cases in 11 minutes 26 seconds with all 16 monthly scenarios at 30
  seconds. A final focused run passed all 37 hard-constraint/oracle cases in 237 ms
  after adding the explicit infeasible-pair case and strengthening the year-boundary
  pair assertion. The union is 368 unique passing cases; reruns are not double-counted.
- **125 frontend component tests passed** across nine files; production Vite build
  and TypeScript checks passed.
- **Seven development-script tests passed.**
- Final Release solution build with warnings as errors: **zero warnings/errors**.
- EF pending-model-change check: **none**. No migration was applied to an existing
  database; no schema change was introduced.
- npm audit at high severity threshold: **zero vulnerabilities**.
- Whitespace/static diff checks passed. No frontend or production code was changed.

The existing PostgreSQL suite covers initial/final publication, correction revisions,
withdrawal/no-fallback access, exchange constraints and immutable historical snapshot
bytes. The final noted/task-linked generation test also saved twice and retained the
original assignments and linked task identity. All per-test databases were gone
before the single disposable server was removed.

## Defects and policy decisions

No hard-constraint correctness defect or generated violation was found in the
completed validation. Production defects fixed: **none**. Deliberately injected
invalid schedules are validator tests, not production incidents. Inconsistent protected capacity is a
reproducible rejected input (zero demand plus one fixed member), not an infeasible
staffing requirement that the solver is expected to fill. No production fix or
database change was necessary. The high-severity workload-convergence limitation
and severe-shortage coverage uncertainty remain open as documented above. They require an acceptance decision
or a separately reviewed optimization investigation; no approved objective was
changed to mask either finding.

## Database safety and reproduction

One new PostgreSQL 17 Alpine container, `shiftflow-phase7a-validation`, used a
512 MiB memory limit, two-CPU limit and tmpfs data storage on loopback port 55433.
It had newly chosen disposable credentials and no existing volume. `TestDatabase`
created/dropped a unique `shiftflow_test_<UUID>` database for each integration test.
Test collections were serialized. Only this server received migrations or test
writes. Existing demo port 55432 and development port 5432 were not connected to
for inspection, migration, seeding or writes. Existing snapshots and manually
maintained data were left untouched. The disposable container was removed after
verification. Main and origin/main remained at the verified base.

Use a **fresh disposable** PostgreSQL server configured with the administrative
database/user `shiftflow_ci`; never substitute demo/development credentials. Set
`TEST_POSTGRES_CONNECTION_STRING` to that server, then run:

```sh
dotnet build ShiftManager.slnx -c Release --no-restore --warnaserror
RUN_POSTGRES_TESTS=1 SCHEDULING_STRESS_SECONDS=30 \
  SCHEDULING_VALIDATION_REPORT_DIRECTORY=/tmp/shiftflow-phase7a-metrics \
  dotnet test ShiftManager.slnx -c Release --no-build --no-restore \
  --logger 'trx;LogFileName=phase7a.trx' \
  -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
dotnet ef migrations has-pending-model-changes --project src/ShiftManager --configuration Release --no-build
npm test --prefix frontend
npm run build --prefix frontend
npm audit --prefix frontend --audit-level=high
python3 scripts/test-dev.py
git diff --check
```

Without `RUN_POSTGRES_TESTS=1`, PostgreSQL tests skip; that is not a complete Phase
7A run. Monthly tests default to eight seconds for routine local execution; set
30 explicitly for comparable acceptance measurements. Reports are emitted only
when the report-directory environment variable is supplied. They include all
per-employee measurements, scenario sizes, solver status, elapsed time and output
hashes. Timing-limited outputs may differ on another machine or run.

## Pilot acceptance criteria and limitations

Before an operator accepts a real workplace pilot roster:

1. Work in an explicitly approved isolated copy/export of workplace inputs; confirm
   employee roles, qualifications, definitions, staffing, absences and boundary work.
2. Require zero independent hard-constraint violations, zero unexpected protected
   changes, and successful current-input validation when saving/publishing.
3. Review every required-position shortage. Require four proven coverage objectives
   or an explicitly reviewed coverage exception; FEASIBLE alone is insufficient.
4. Compare workloads among employees with comparable qualifications and actual
   opportunities, and review night/weekend/priority concentration separately.
   Require explicit acceptance of unexplained imbalance; total-load fairness does
   not establish equitable night/weekend distribution.
5. Confirm operational willingness to accept the measured runtime and any lack
   of repeatability for time-limited monthly incumbents. Consider a separately
   approved budget/performance investigation if coverage proofs are too slow.
6. Review the draft, publish through the existing two-stage workflow, and exercise
   an absence correction/exchange on the pilot copy while verifying old snapshot
   bytes remain identical. No automatic publication is part of generation.

These tests do not establish feasibility for every real input, optimality of
time-limited schedules, exact maximum opportunity denominators, production
concurrency throughput, multi-month joint optimization, or determinism across
hardware/library versions. Existing CI has its own disposable service; this phase
does not change CI service configuration or run external production operations.


## Phase 7A.1: configurable priorities and optimization

Implemented on the same `feature/phase-7a-scheduling-validation` branch after `8d0b989`. The preceding sections are the historical Phase 7A baseline; this section supersedes their runtime priority/objective description and pilot findings. Original measurements remain unchanged. Full new evidence is in [phase-7a1-validation-results.json](phase-7a1-validation-results.json).

### Investigation and exact fairness fix

The original normal fixture had no absences, protected work or availability restrictions. Its 42 unqualified employees had identical opportunities, yet received 0–27 assignments; the 14 qualified employees received 2–21. Differences in qualification, absence, availability, pairing or retained work cannot explain those within-cohort spreads. Mixed durations affect hours, but the approved objective balances counts. The original seven-stage run reached the fairness stage with coverage proven and returned an unproven incumbent at the shared deadline.

The fairness formula itself remains unchanged: `floor(1000 * load² / maxLoad)`, with the same opportunity denominator and retained-work horizon. The fix replaces the CP-SAT multiplication/division equalities with an exact finite-domain `AddElement` lookup table for every permitted load. This both tightens the cost domain and removes nonlinear arithmetic from the search; it introduces no workload hard limit, new priority weight or new fairness preference.

An encoding-only control retained the original seven-stage hierarchy and changed only that arithmetic representation. On the identical seed-731 normal fixture it reached all seven optima in 15.275 seconds, filled all 294 positions, and gave both qualification cohorts 5–6 assignments. Thus the normal-case imbalance was a search-convergence/time-limit problem in the original encoding, not an employee-eligibility difference or a need to sacrifice coverage. This control isolates the encoding improvement from the separately requested priority refactor.

The user explicitly selected preservation of assignment-count fairness with hours reported separately. No duration, night or weekend balancing objective was added. `Hours` sums configured assignment intervals in UTC, including 11/13-hour DST nights and both days of on-call pairs. It measures scheduled interval hours, not payroll or actual hours worked. Paired on-call remains two assignments and one weekend allocation. The historical Phase 7A artifact did not capture employee hours; no before-hours values were invented.

### Persistent priority configuration and migration

- `SchedulingPriority` is a strongly typed domain enum: Normal, High, Critical. New definitions default to Normal. PostgreSQL stores the validated strings, with a NOT NULL/default/check constraint. The API rejects unknown, numeric and case-mismatched values.
- `20261106074419_SchedulingPriority` adds only this column/check and initializes exactly the seven stable UUIDs ending 001–007 as Critical. Other definitions are Normal. IDs, order, names, staffing, eligibility, versions, assignments, users, teams and publication JSON are retained. It never assigns priority from current display order.
- Normal EF migration-history guards and the generated idempotent SQL make reapplication a no-op, preserving later administrator changes. Disposable-database tests reverse display order before migration, preserve edited names/headcounts/versions, migrate twice, retain a later High setting and reject invalid database values. The strengthened test also preserves existing assignment IDs and exact publication snapshot bytes through migration.
- The migration was applied **only to disposable test databases**, never to the demo or normal development database. No startup migration, generation or publication was added. Deploy backend/frontend together only after an operator separately approves applying the reviewed migration to an identified target. Downgrading drops the new priority configuration, so preserve it before any separately authorized rollback.
- Administrators use a separate Hebrew RTL selector: קריטית / גבוהה / רגילה. Priority is independent of display order, headcount, qualification and automatic inclusion. Server policy and the service recheck authorization, with existing account locks, optimistic versions and UpdatedAt retained. There was no dedicated configuration-actor audit table to extend; existing version/timestamp tracking remains.
- Older clients omitting priority retain its stored value. Explicit updates change future solver input and its fingerprint, invalidating stale previews; they never rewrite generated drafts or published snapshots. Restart persistence is tested.

### Coverage hierarchy and truthful termination

The previous implementation already identified its special group with stable IDs, so reordering was not a preexisting bug. This refactor removes that runtime ID rule and makes the priority administrator-configurable. Stable IDs are used for migration/seeding and independent weekend/rehabilitation business rules, not for solver coverage importance.

1. Maximize Critical coverage.
2. Pin the **proven** Critical optimum; maximize High coverage.
3. Pin the **proven** High optimum; maximize Normal coverage.
4. With all three coverage optima established, minimize the unchanged count-fairness cost.
5. Maximize submitted preferences.
6. Maximize retained replaceable assignments.

Each assignment has equal coverage weight within its configured level. There is no hidden expert-first sub-ranking. This intentionally replaces the old global qualification-required coverage objective with the hierarchy requested for Phase 7A.1; qualification remains a hard eligibility rule. A Normal qualification-required position cannot displace a Critical position merely because it requires qualification. All other hard constraints and manual-only behavior remain unchanged, including rehabilitation Friday/Saturday rules and the ordinary behavior of definition 26.

Only a native OPTIMAL result is pinned. On FEASIBLE, UNKNOWN, cancellation or budget exhaustion, no unproven optimum is locked and no lower-priority solve is attempted. The best valid incumbent is retained; later stages are NOT_RUN. OPTIMAL requires six proven stages and CoverageOptimal requires three. An unproven earlier stage can still have suboptimal coverage. Precondition conflicts/infeasibility remain domain errors with no silent deletion of protected work.

The generation API and Hebrew result details expose each stage’s objective, native status, incumbent value, best bound, elapsed time and proof flag. The final timing refinement records managed elapsed time plus native wall time separately. NOT_RUN has null value/bound. Fairness is a maximization of negative cost, so its native value/bound are negative. Stage metadata is available with the bounded generation preview; historical saved-run objective counts and publication snapshots are not retroactively reinterpreted. The production timeout stays at 30 seconds.

### Independent shortage validation

Twelve fixed-seed small cases exhaustively enumerate all 243 possible five-cell/two-employee rosters each, using the independent hard-constraint validator and lexicographically comparing independently counted Critical/High/Normal vectors. All 2,916 candidates collectively cover competing qualifications, approved absences, consecutive conflicts, protected work and shortages. The optimizer matches every independently established optimum. Additional controlled cases prove Critical over High, High over Normal, no lower-coverage/preference/stability compensation, and equal coverage weight for expert/ordinary rows in the same level.

All Phase 7A test coverage is retained. Existing priority/qualification coverage assertions and objective-count checks were adapted to the explicitly requested hierarchy; no hard-constraint expectation was relaxed. Migration, authorization, priority persistence/restart, reordered Critical/Normal definitions, future generation and immutable snapshot regressions were added.

### 30 / 60 / 120 second experiments

Experiments ran sequentially with one search worker and seed 42. Coverage vectors are Critical / High / Normal. High demand is zero in the migrated monthly fixture; all three levels compete in the exhaustive and controlled shortage tests. The severe case has six employees, including two qualified people, against 294 positions.

| Scenario | Budget | Managed seconds | Native Critical seconds | Status | Proven / 6 | Coverage C / H / N | Critical incumbent / upper bound | Missing |
| --- | ---: | ---: | ---: | --- | ---: | --- | --- | ---: |
| normal | 30 | 16.372 | 0.760 | OPTIMAL | 6 | 238 / 0 / 56 | 238 / 238 | 0 |
| normal | 60 | 16.094 | 0.753 | OPTIMAL | 6 | 238 / 0 / 56 | 238 / 238 | 0 |
| normal | 120 | 16.111 | 0.761 | OPTIMAL | 6 | 238 / 0 / 56 | 238 / 238 | 0 |
| severe-shortage | 30 | 30.002 | 29.999 | FEASIBLE | 0 | 176 / 0 / 0 | 176 / 198 | 118 |
| severe-shortage | 60 | 60.002 | 59.998 | FEASIBLE | 0 | 176 / 0 / 0 | 176 / 198 | 118 |
| severe-shortage | 120 | 120.021 | 119.987 | FEASIBLE | 0 | 176 / 0 / 0 | 176 / 198 | 118 |

Longer budgets did not improve either measured incumbent or bound. Normal reached the same complete optimum at every budget: 238 Critical plus 56 Normal assignments and fairness cost 29,848. Severe shortage remained at 176 Critical against a native upper bound of 198, with all later stages unattempted. The original Phase 7A severe incumbent contained 166 future-Critical assignments plus one Normal assignment; the new strict hierarchy improves Critical coverage by ten but **does not prove it maximal**. These measurements do not justify raising the production timeout.

Timing limitation: the first severe-120 sample recorded 76.359 managed seconds versus 568.301 native seconds. The cause of the clock discrepancy was not established. It is preserved in the raw evidence and excluded from the primary comparison. A separate repeat recorded 120.021 overall managed seconds, 119.993 managed stage seconds and 119.987 native seconds, with the same incumbent and bound. No claim of a 76-second equivalent-to-120 experiment is made.

### Monthly regression measurements

| Scenario | Seconds | Status | Proven / 6 | Filled | Missing |
| --- | ---: | --- | ---: | ---: | ---: |
| autumn-dst | 16.252 | OPTIMAL | 6 | 296 | 0 |
| heavily-constrained | 6.319 | OPTIMAL | 6 | 294 | 0 |
| insufficient-leads | 15.422 | OPTIMAL | 6 | 258 | 36 |
| moderate-absences | 14.046 | OPTIMAL | 6 | 294 | 0 |
| normal-repeat | 17.315 | OPTIMAL | 6 | 294 | 0 |
| normal | 17.369 | OPTIMAL | 6 | 294 | 0 |
| overlapping-restrictions | 11.001 | OPTIMAL | 6 | 294 | 0 |
| protected-repeat | 14.781 | OPTIMAL | 6 | 294 | 0 |
| protected | 14.962 | OPTIMAL | 6 | 294 | 0 |
| reserve-duty | 30.009 | FEASIBLE | 4 | 294 | 0 |
| severe-shortage | 30.004 | FEASIBLE | 0 | 176 | 118 |
| spring-dst | 18.281 | OPTIMAL | 6 | 305 | 0 |
| studies | 15.038 | OPTIMAL | 6 | 294 | 0 |
| variable-headcount | 12.643 | OPTIMAL | 6 | 250 | 0 |
| widespread-absences | 9.966 | OPTIMAL | 6 | 266 | 28 |
| year-boundary | 16.180 | OPTIMAL | 6 | 296 | 0 |

Fourteen monthly cases prove all six objectives. Reserve duty proves coverage and fairness (four stages), but not the remaining preference/stability stages. Severe shortage proves none of its coverage stages. Every generated scenario passes both independent hard-constraint validation and production draft validation, with zero violations. Approved full-month absences still produce zero assignments. Small controlled repeatability cases remain exact; optimality alone does not imply a unique schedule or cross-platform deterministic tie breaking.

### Before/after fairness and hours

| Normal cohort | Employees | Before assignments min / median / max | After assignments | After interval hours | After nights | After weekend allocations |
| --- | ---: | --- | --- | --- | --- | --- |
| Unqualified | 42 | 0 / 3 / 27 | 5 / 5 / 6 | 24 / 50 / 72 | 0 / 2 / 4 | 0 / 1 / 2 |
| Qualified | 14 | 2 / 5 / 21 | 5 / 5 / 6 | 50 / 60 / 66 | 0 / 2 / 5 | 0 / 1 / 2 |

The normal cohorts have identical opportunities within each qualification group: no blocks, absences or fixed work. No normal employee remains idle. The protected-month cohorts also improve to 5–6 assignments, compared with 0–21 unqualified and 0–26 qualified before. Full raw evidence includes all employee observations, opportunities, qualifications, count/hour/night/weekend/priority distributions and stage bounds.

Availability/qualification groups with different restrictions are not treated as interchangeable. Eligible-cell counts are descriptive, not exact feasible workload capacities; paired/weekly/monthly constraints and retained work also matter. Fairness is still the approved approximate opportunity-normalized count objective, not an hours/night/weekend equality policy. The normal hours spread of 24–72 remains material for operational review even though count fairness is proven. No hard cap or hidden duration preference was introduced to hide that distinction.

### Phase 7A.1 verification and pilot assessment

- **388 distinct backend cases passed**, zero failures/skips, including all PostgreSQL tests, all retained Phase 7A scenarios and 20 added cases. The complete serial run passed 388 cases; after the timing-only metadata refinement, all 371 non-monthly/non-benchmark cases passed again in 4 minutes 13 seconds. The focused timing/migration/diagnostics repeat passed 21 cases including the 120-second trial. Reruns are not double-counted.
- The complete initial run reported 33 minutes 13 seconds and includes the documented host clock anomaly; it is not used as a stable throughput benchmark.
- **127 frontend tests passed**, including priority editing independent of order and Hebrew RTL proof/bound display; TypeScript and the production build passed.
- **Seven script tests passed.** Release build with warnings as errors passed with zero warnings/errors. EF reports no pending model changes. npm audit reports zero vulnerabilities. Diff whitespace checks passed.
- Only the single newly created, 512 MiB disposable PostgreSQL server on port 55433 received migration/test writes. Its per-test databases and the temporary server were removed after verification. Existing demo/development servers and their data were not changed. No remote Git branches were updated.

The evidence retains the legacy per-employee field name `HighPriority`; in new Phase 7A.1 monthly/benchmark records it counts **Critical** assignments, not the newly configurable High level. Coverage vectors and stage names explicitly distinguish all three levels.

**Engineering acceptance:** the configurable-priority architecture and exact fairness-encoding fix satisfy the implemented Phase 7A.1 requirements. The normal count imbalance is explained and materially corrected with preserved full coverage. Small shortages have independently proven priority optima; infeasible/protected and all previous hard-constraint regressions remain covered.

**Pilot recommendation: withhold unconditional pilot-ready approval.** The severe monthly Critical optimum remains unproven (176–198), so treat its 118 missing positions as an incumbent shortage rather than a proven minimum. Normal/protected assignment counts are balanced, but configured hours and night/weekend distributions still require explicit review under the chosen count-based policy. Before a workplace pilot, review real staffing/qualifications/absences and all shortages, demand three proven coverage stages or an explicitly accepted reviewed exception, and inspect hours/night/weekend workloads separately. Further proof-performance work needs a separate scoped investigation; the benchmarks do not support simply increasing timeouts.

Apply no migration, generation or publication to existing environments as part of acceptance of this branch. Existing demo/development databases remain unchanged. The user retains the decision to push/open a PR, approve any identified migration target, and authorize a controlled pilot.

For reproduction, use the isolated PostgreSQL setup above and the same serial test flags. The full run includes the six sequential benchmark trials. Quick runs can exclude `Category=SchedulingBenchmark`; monthly routine budgets still default to eight seconds. Set `SCHEDULING_STRESS_SECONDS=30` for the measured monthly matrix. `SCHEDULING_BENCHMARK_SCENARIOS=severe-shortage` and `SCHEDULING_BENCHMARK_BUDGETS=120` reproduce only the timing repeat. New benchmark reports must be written to a new directory so historical evidence is not overwritten.

### CI repeatability regression correction

The first GitHub Actions run passed 386 of 388 backend cases. Both failures were
in the controlled repeatability test: its three solves per case returned valid
FEASIBLE incumbents after reaching the ten-second wall-clock limit, with only two
or three objectives proven. The production solver reported that distinction
correctly. The test incorrectly assumed that a shortage fixture with 40 cells and
eight employees would always prove all six objectives within ten seconds on a CI
runner. Earlier local verification used serial test collections; CI uses default
parallelization, making this assumption especially fragile.

The controlled fixture now uses six cells, three employees and four dates. It
exercises Critical/High/Normal coverage, qualification, touching overnight work,
preferences, regeneration stability, and optional protected shift-lead work. Its
optima are known independently: coverage 2 / 1 / 3, two assignments per employee,
fairness cost 3,999, preference value 4 (3 excluding the fixed preferred assignment
when protected), and four retained replaceable memberships. Those memberships and
equal loads determine one final roster, which is asserted explicitly.

The test still requires OPTIMAL, all six proven stages with matching bounds,
independent and production validation, protected identity/lead preservation, and
identical assignment hashes across three solves. The ten-second budget is
unchanged. Monthly stress inputs, benchmark budgets, production solver settings
and the treatment of FEASIBLE results are unchanged.

Validation: the full backend suite passed **388/388 cases**, zero failures/skips,
using the reported Release/no-build/no-restore command with default test
parallelization, `CI=true` and `DOTNET_PROCESSOR_COUNT=2`. It completed in about
seven minutes against one disposable PostgreSQL server on port 55433. After the
final exact-roster refinement, both cases also passed ten separate test-process
runs: **20 passing cases, 60 solver executions**, with stable hashes across
processes. Every solve retained the OPTIMAL and six-stage proof requirements.
The final focused run completed both cases in 228 ms; this is an observation,
not an asserted timing threshold. Release build with warnings as errors passed
with zero warnings/errors, and diff checks passed. No production code, schema or
CI parallelization setting was changed. The disposable server was removed after
per-test database cleanup; demo and development data were not accessed for tests.

### CI benchmark fairness target correction

A subsequent GitHub Actions run passed 387 of 388 cases. The remaining failure
was the normal-month benchmark's unconditional assignment-spread assertion: it
observed a spread of 15 after 30 seconds, against a target of at most two. That
benchmark accepted either OPTIMAL or FEASIBLE, so it could apply its fairness target
before fairness was established. The pasted failure did not include
stage proof/value/bound data because the report was only written after assertions.
The exact interrupted stage cannot be determined from that trace alone.

The six budget experiments now run in an xUnit collection with parallelization
disabled relative to other collections, so they do not compete with monthly
stress tests, PostgreSQL integration tests or other solver tests for their CPU
budget. The experiments remain sequential and all other collections retain their
normal parallelization. Production solver settings, objectives and timeouts are
unchanged.

Each trial is now logged and saved before assertions, including native stage
status/value/bound, proof flags, cohort minimum/maximum/idle counts and a separate
fairness-target assessment. The assessment distinguishes proven/unproven and
met/missed targets. An unproven spread of 15 remains explicitly
`UNPROVEN_TARGET_NOT_MET`; it is never reported as a successful quality target or
pilot acceptance. If the FAIRNESS stage is proven but its normal-fixture target
is violated, the benchmark still fails. This check uses the fairness stage's proof
flag rather than requiring overall OPTIMAL: a global FEASIBLE result can already
have proven fairness while preferences or stability remain unfinished.

Hard-constraint validation, valid-incumbent status checks and proof/bound
consistency checks remain mandatory for every trial. The small, known-optimum
fairness and repeatability regressions remain strict. Four new deterministic
assessment cases cover all combinations of fairness proof and target outcome,
including a synthetic 0/15 cohort matching the reported spread of 15 and a genuine
proven-fairness violation. The original trace did not provide cohort minima/maxima.

CI now writes the machine-readable trial reports to its temporary report directory
and publishes a benchmark table in the job summary even when the test step fails.
The table shows coverage bounds, fairness status/value/bound and cohort spread.
Missed normal targets generate a workflow warning. A green correctness suite is
not a claim that every time-limited monthly roster meets the pilot quality target;
remaining performance/quality concerns must still be reviewed before acceptance.

Validation: Release build with warnings as errors passed with zero warnings/errors.
The full CI-style command passed **392/392 backend cases**, zero failures/skips,
in 7 minutes 19 seconds with default xUnit parallelization and a two-processor
.NET setting. TRX timestamps confirmed that no other test overlapped the benchmark
collection. All six real trial reports were produced, and the workflow summary
was exercised against those reports. The normal cases proved all six stages and
met the quality target; severe shortage remained FEASIBLE with unproven Critical
coverage at every budget. These observations do not promise equivalent convergence
on every runner. Four classification cases and summary dry runs verified that
both unproven target misses and genuine proven violations remain visible, including
a missing report and a partial set of completed trials. The disposable PostgreSQL server was cleaned up after
tests; the demo and development databases were not used. No production scheduler,
frontend, schema or timeout changes were made.
