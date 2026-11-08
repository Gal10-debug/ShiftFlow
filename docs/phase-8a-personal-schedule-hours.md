# Phase 8A: personal schedule and planned work hours

## Discovery recorded before implementation

Verified base: `c992659aef3f401d38d79c1c26d4048a9521f871`, including Phase 4C and
Phase 7E. A read-only remote query confirmed the same `main`; no fast-forward was
necessary. The initial working tree was clean.

The existing employee page selects published months and immutable versions, shows
the full published roster and personal assignments, polls visibility, submits
preliminary-publication issues, and links to exchanges. `ScheduleReviewService.Mine`
uses employee authorization and a repeatable-read transaction. Initial and final
publications are visible; drafts and legacy null snapshots are not. Hiding/deleting
the latest publication also blocks historical access. Publisher identity is redacted.

`ScheduleSnapshot` freezes per-cell definition ID, name, semantic, period, time range,
UTC start/end, qualification requirement and employee assignment IDs. Legacy work
also freezes UTC boundaries. Mutable definitions must never reinterpret history.
`ShiftTimeRanges` explicitly defines DAY 07–19, NIGHT 19–07, H06_09, H07_13,
H07_15, H15_23 and H23_07. Nullable definition ranges inherit the canonical period;
publication materializes the effective boundaries. `Asia/Jerusalem` is the established
operational zone, independently resolving each boundary, including DST changes.

Stable special identities are documented in Phase 6: slot 12 is “עד 16”, 13 “עד 13”,
14 “זמין ערב” (all manual-only), 8 “שיקום בוקר”. SIX_NINE is a persisted semantic,
and WEEKEND_ON_CALL moved to slot 16 in the weekend-board migration. Names are
editable and cannot define duration. No H07_16 range exists. “עד 16” therefore
cannot imply 07–16: the snapshot interval alone describes its published plan.
Expert requirements and employee lead flags describe qualification/responsibility,
not additional work. Rehabilitation is a timed operational row (including Fridays,
excluding Saturdays), not an extra accounting category.

On-call has two stored Friday/Saturday memberships but one allocation, keyed by
definition and Friday (`SchedulingRules.Friday/AllocationMonth`). Those cell
intervals enforce scheduling constraints; no business rule equates on-call duty to
working time. Similarly, the availability designation at stable slot 14 has no
established working-time accounting rule. Both must have unavailable planned work
duration. No schema change or invented historical backfill is needed for a safe
partial summary. A later explicit, versioned accounting policy would be the smallest
extension if these duties are to contribute hours.

Corrections and approved exchanges change the live working copy and remain pending
publication. New immutable publications expose the replacement; old snapshots stay
unchanged. The summary must use precisely the selected snapshot, never live work.

## Calculation policy

The reusable pure backend calculator consumes a single snapshot and employee ID
after authorization. It uses integer elapsed UTC minutes; decimal display hours
are derived only in response/presentation properties. Valid frozen intervals are
authoritative for timed work, including configured short/manual rows; labels never
change their boundaries. On-call and availability return explicit reason codes and
null minutes. Invalid, missing, non-UTC or sub-minute intervals also return unknown.
Known minutes are a subtotal, and the complete total is null whenever any duration
is unknown. Zero assignments is a complete zero, not unavailable data.

Counts and all minutes of an overnight timed assignment belong to its published
local assignment date/month, preserving snapshot ownership. An October 31 night is
entirely October, including November 1 hours; November does not import it again.
Assignments outside the requested month are excluded. On-call memberships remain
separate assignments for reporting/actions; a separate distinct Friday-keyed duty
count identifies pairs represented in that month. A pair across months can be
represented in both monthly duty counts; these counts are not additive yearly
allocations. No on-call work minutes are counted. Future allocation analytics must
use the existing Friday allocation month and explicitly join authorized versions.

Duplicate IDs count once. Conflicting duplicate records conservatively have unknown
duration instead of selecting an arbitrary interval. Staffing requirements without
an assigned employee never count. Day/night counts follow frozen period metadata;
on-call and availability are classified separately. Qualification adds no minutes.

## API and authorization

The existing `GET /api/my-schedule/{year}/{month}?version={positiveVersion}` now
includes `personalSchedule` in the publication response. No duplicate route or
employee-ID parameter was introduced. Year/month use the existing supported range
(years 2–9998, months 1–12); nonpositive versions return 400. Unpublished, hidden,
deleted or unavailable versions retain the existing 404 behavior.

`personalSchedule.assignments` is chronologically ordered and contains assignment
and definition IDs, published date/name/period, accounting category, nullable frozen
UTC boundaries, overnight flag, nullable integer planned minutes, unavailable reason
and existing lead flag. Unknown reasons distinguish on-call, availability, invalid
intervals and conflicting duplicates. Legacy shift names come from their frozen
period rather than the legacy employee-name field.

`personalSchedule.summary` contains year/month, unique assigned shift count, known
minutes/hours, nullable complete minutes/hours, unknown count, completeness, day/night
counts, represented on-call duty count and per-definition/per-category breakdowns.
The year/month identify the calendar-month breakdown; future yearly callers can
combine explicitly authorized monthly results. `timeZone` and `attributionPolicy`
are explicit. The UI divides minutes for display with Hebrew locale formatting;
it does not reconstruct durations or independently calculate scheduling statistics.

Only `Mine` attaches this employee-specific result. Administrative publication/history
responses do not compute another employee's statistics. `Employee(actor)` still checks
current account state and roles, excludes administrators and organizational leaders,
and derives identity from the session. A deputy receives only their own personal
summary; coordinator status grants no administrative access. Existing full published
roster visibility is retained intentionally, including its published names/notes;
the new personal response adds no other employees' statistics or publisher identity.
The calculator is pure and has no data-access or authorization capability of its own.
Callers must authorize an immutable version before invoking it. Cookie validation,
session revocation and CSRF handling are unchanged.

## Publication consistency and performance design

The response and summary use the same immutable object within the existing
repeatable-read transaction. There are no extra database queries, mutable definition
lookups, live assignment loads, per-employee queries or cached authorization results.
The existing API already returns the authorized full monthly roster for the employee
grid; this phase reuses that one snapshot rather than loading organization-wide live
work for a personal calculation. CPU work scans this monthly snapshot once for the
employee, then groups only their assignments. Historical requests never query current
timing configuration. Exchange approval remains pending until explicit republication.

The UI retains the published roster, preliminary issue forms and exchange link. The
personal chronological list and monthly summary precede the roster. Month controls
navigate only published periods; history clearly identifies the selected version.
Weekly filtering affects the list, while the summary remains monthly. Loading,
unpublished, zero-assignment, error/retry and incomplete-data states are distinct.
Hebrew RTL, Israel-local date/time formatting and the existing horizontally scrollable
table container support mobile layouts. Visibility revalidation removes both the
snapshot and summary on denial; the existing focus/visibility/offline refresh and
five-second polling remain. Version-list reads occur on publication changes rather
than every unchanged polling response. No framework, dependency, schema migration,
deployment setting, security configuration or solver objective is changed.

## Remaining business decisions and Phase 8B

- Define working-time accounting separately for on-call and evening availability.
  Constraint intervals alone do not authorize counting these duties as working hours.
- Confirm that configured early-end row intervals express the intended plan. There
  is no 07–16 range today; labels never shorten a published DAY interval. If 16:00
  boundaries are required, add an explicit supported range in a separately reviewed
  scheduling change and apply it only to newly published versions.
- For yearly duty allocations, preserve Friday ownership and deduplicate pairs across
  explicitly authorized monthly versions. Represented monthly duty counts are not
  additive allocations. Never mix snapshots implicitly.
- Reuse this calculator behind authorized employee/admin workload services in Phase
  8B. Retain incomplete totals and version provenance in comparisons; do not compare
  a known subtotal with another employee's complete total as though both were complete.
  Consider database-side personal snapshot projection if a future personal-only API
  no longer needs the full published roster. Measure larger history and employee
  populations before adding indexes or denormalized aggregates. AI tools should call
  those authorized services, with no SQL or independent arithmetic.

No planned result represents attendance, actual worked hours, payroll or overtime.

## Validation results

All results below are local; no GitHub Actions success is claimed.

- Release backend build using the existing cached SDK 10.0.403: zero warnings/errors.
- Full PostgreSQL correctness suite: **472 passed**, zero skipped/failed, about
  **185.49 seconds** process time. This includes **27 named security regressions**,
  migration consistency/immutability, authorization/session/CSRF checks, scheduling
  stress and historical/correction/exchange behavior. The unchanged long solver
  experiments are excluded from this correctness run and run separately once.
- New backend coverage: **32 calculation cases** and **four HTTP integration cases**.
  A final focused rerun of all **36 passed** after interval precision and legacy-name
  refinements. Coverage includes every explicit time range, 11/13-hour DST nights,
  month/year ownership, leap/non-leap February, duplicates/conflicting duplicates,
  special/manual/legacy work, mixed/unknown durations, zero work, unsupported periods,
  initial/final publications, historical replacements and pending/applied exchanges.
- Frontend: **140 passed**, including **12 new personal-schedule tests**. Production
  TypeScript/Vite build and E2E TypeScript checks pass. Existing React `act` warnings
  remain in the older publication/session tests; they are not test failures.
- Playwright workflow/security suite: **33 passed**, zero skipped/flaky, **70.712 s**.
  New coverage verifies unpublished access, two published months, chronological
  personal retrieval, 24/12-hour monthly navigation and a 390-pixel RTL viewport
  without document overflow. The extended exchange workflow asserts 12 → 0 hours for
  the requester, 12 for the replacement, and 12 in the requester's immutable history.
  The first run had 31 passes and two failures in new exact-label selectors; the
  corrected selectors use existing accessible combobox roles. No automatic retries
  were used, and the first report is retained under ignored `TestResults/phase8a`.
- Helper checks: **four** E2E artifact-safety, **20** pilot guard, **seven** development
  script, **two** timing-report and **nine** scanner-installer tests pass. `actionlint`
  passes. The repository audit reports **zero** NuGet/npm advisories and **zero**
  Gitleaks findings in history and current source.

The personal endpoint measurement uses real Identity cookies, repeatable-read
publication lookup, JSON deserialization/calculation and in-process HTTP on a real
51-row October publication, one personal assignment and disposable PostgreSQL.
After one warm-up, **20 requests** had median **28.702 ms**, maximum **35.135 ms**.
An earlier full-suite sample measured median 16.530 ms/max 18.334 ms; these are
different concurrently loaded runs, not a before/after speedup. No p95 or production
SLO is inferred from these small samples. There is no added database round trip.
Raw TRX, both browser-run summaries and latency JSON are under ignored
`TestResults/phase8a/`; the measured fixture is intentionally small and does not
establish large-organization or long-history performance.

All database writes used the existing guarded test harnesses: newly owned labeled
containers, random loopback ports and per-test databases. No development/demo database
was connected to, migrated, reseeded, reset or modified. No published persistent
snapshot was changed. No persistent PostgreSQL service or volume was restarted or
deleted. The code introduces **no migration**.

The unchanged solver benchmark fact passed once, executing exactly six experiments
in **260 seconds**: normal staffing at 30/60/120-second budgets was OPTIMAL with
238 critical and 56 normal assignments and the fairness target proven; shortage
trials were FEASIBLE with 176 critical assignments at all three budgets. The existing
shortage-optimality limitation remains; Phase 8A changes no solver behavior. The
owned PostgreSQL image health/rollback/restart/backup-restore/private-volume probe
also passed, removing only the disposable resources it created for that run.

The actual Production application and proxy Docker images build successfully.
The existing pilot-deployment harness passes **15 checks**, including loopback TLS,
clean migrations/bootstrap, provisioning, delegated availability, generation,
publication/correction/exchange, secure cookies/CSRF/CORS/host controls, least
privilege, application replacement/key continuity, owned-database restart/outage,
invalid credentials, missing configuration, Hebrew browser layout, copied-cookie
revocation and backup/restore into a separate disposable database with session
invalidation. Cleanup is PASS. The application uses a new local image tag
`shiftflow-pilot:phase8a-local`; no image is pushed.

The unchanged production-browser performance and owned-failure recovery suite also
passes **2/2**, zero skips/flaky tests, **42.924 s**. Its production build uses the
final frontend. The final bundle is about **117.93 kB** gzip JavaScript and **4.48 kB**
gzip CSS on the local toolchain. These package checks establish local compatibility,
not a new pilot-readiness approval or hosted CI result.

Final read-only Docker inspection confirms the original development/demo container
IDs `62f7ee472643` / `f00454a53922`, November 5 start times and zero restarts. Only
newly owned test resources were cleaned up. `main` and `origin/main` remain at the
verified base. Implementation is committed only on
`feature/phase-8a-personal-schedule-hours`; no remote branch is updated and no pull
request is created.
