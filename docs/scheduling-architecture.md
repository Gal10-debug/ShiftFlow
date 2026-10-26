# Scheduling workspace and automatic drafts (Phases 4A–4B)

## Scope and relationship to existing scheduling data

The unpublished ADMIN-only weekly workspace combines persistent shift-type configuration,
monthly automatic scheduling previews and explicitly saved draft memberships. The board
always identifies itself as UNPUBLISHED. EMPLOYEE and COORDINATOR cannot read the board,
configuration, previews or saved drafts. Unmapped legacy ShiftAssignment records remain
fixed commitments; they are not attributed to an arbitrary board row.

The existing `ShiftType` enum remains DAY/NIGHT. An existing `Shift` is still one calendar
date plus that DAY/NIGHT period, unique across the application. It belongs to the monthly
`SchedulePeriod` containing its starting local date and is the anchor for availability,
hard blocks and existing assignments. The new `ShiftTypeDefinition` is a board row with a configurable local time range, not an
employee slot or duplicate Shift table. Multiple row types can overlap the same date's DAY
or NIGHT anchor; custom ranges can also span more than one availability anchor.

Each board day returns its year, month and existing SchedulePeriod ID (nullable when not
created yet). Each cell returns actual UTC boundaries and all overlapping availability anchors with their
existing Shift IDs when available. Its legacy ShiftId is filled only for the exact DAY/NIGHT
ranges; custom ranges use AvailabilityAnchors instead. Night belongs to the starting date's month even if it ends next month.
GET never creates SchedulePeriod/Shift records or fills missing rows; monthly infrastructure
is created by availability configuration or ensured transactionally by explicit draft save. An existing Shift with the wrong owning period or noncanonical
boundaries produces 409 NON_CANONICAL_SHIFT rather than silently changing history.

## Persistent definitions and initialization

`ShiftTypeDefinition` contains:

| Field | Meaning |
| --- | --- |
| Id | Stable deterministic GUID; never changes when a row is renamed/reordered |
| Slot | Immutable seed identity 1–51; unique and range checked |
| Name | Editable Hebrew placeholder initially `משמרת 1` through `משמרת 51`; required, max 100 characters |
| DisplayOrder | Unique permutation of 1–51, independent of Slot |
| Period | Legacy DAY/NIGHT classification; never sufficient for custom-range hard-block checks |
| TimeRange | One of seven supported ranges; nullable only for legacy seed/client compatibility, falling back to Period |
| RequiredEmployees | Integer 0–1000; zero intentionally requires no staffing |
| IncludeInAutomaticScheduling | Administrator's explicit selection for a future run |
| Version | Positive bigint, incremented on a changed row and checked on save |
| UpdatedAt | Server UTC timestamp; client cannot set it |

The additive `SchedulingBoardShiftTypes` migration creates one table and inserts exactly
51 definitions with fixed GUIDs/slots. Initial defaults are DAY, zero staffing and excluded
from automatic scheduling: no real workplace duties or staffing levels are invented.
There is no startup reseeder and no public add/delete endpoint. Applying migrations again
or restarting the host does not overwrite names/settings. Seed metadata is deliberately
fixed; future migrations must not change seeded configuration defaults with HasData updates
that would erase administrator edits. Normal schema migration coordination is still required.

The unique Slot range prevents a 52nd definition. Supported APIs preserve all 51 records;
trusted manual database deletion is not automatically repaired, and an invalid/incomplete
configuration cannot be saved. A database missing the table needs migrations; no app-start
schema creation is performed. Data is persistent across ordinary restarts.

DisplayOrder uses a PostgreSQL UNIQUE constraint that is DEFERRABLE INITIALLY DEFERRED,
so a complete permutation can be swapped atomically without temporarily assigning an
invalid order outside 1–51. This is represented as a unique index in the EF model; the
migration creates its backing constraint explicitly. The save service uses parameterized
SQL updates because EF's normal unique-index command sorter rejects cyclic swaps before
sending SQL. The database checks final uniqueness at transaction commit. Slot uniqueness,
allowed periods, count range, nonblank name and positive version have database constraints.

## Extended time ranges

The additive ShiftTypeTimeRanges migration adds TimeRange and backfills it from each row's
existing Period without altering administrator edits, row identity or staffing. A check
constraint validates both the allowed range and its legacy classification. Original seed
metadata remains fixed; null seed values fall back to the existing Period at read time.

| TimeRange | Local hours | Legacy Period |
| --- | --- | --- |
| DAY | 07:00–19:00 | DAY |
| NIGHT | 19:00–07:00 next day | NIGHT |
| H06_09 | 06:00–09:00 | DAY |
| H07_13 | 07:00–13:00 | DAY |
| H07_15 | 07:00–15:00 | DAY |
| H15_23 | 15:00–23:00 | DAY |
| H23_07 | 23:00–07:00 next day | NIGHT |

The UI selects TimeRange and supplies its compatible Period automatically. API responses,
configuration snapshots and settings saves include TimeRange. Legacy clients omitting it
preserve a custom range when Period is unchanged; explicitly changing Period resets to that
canonical DAY/NIGHT range. A conflicting explicit TimeRange/Period pair is rejected.

ShiftTimeRanges.Boundaries converts start/end independently with Asia/Jerusalem rules.
ShiftTimeRanges.AvailabilityAnchors exposes every canonical interval that overlaps the
actual UTC range (touching endpoints do not overlap). Board cells expose these anchors,
including adjacent-date/month references. For example 06:00–09:00 checks the previous
night and current day; 15:00–23:00 checks the current day and current night. Phase 4B does
inspect hard blocks and availability for **all** returned anchors, not just legacy Period
or the first ShiftId. Existing availability submissions and canonical shifts are unchanged.
No engine or employee assignment generation is introduced by adding these ranges.

## Administrator configuration and concurrency

List and board responses are ordered by DisplayOrder with Slot as a deterministic fallback.
The UI opens an editable configuration table with name, order, day/night, staffing and
inclusion controls. Edits remain local until **שמירת הגדרות**. Zero staffing is labeled
**ללא צורך באיוש**; the automatic flag may remain selected, but zero-count rows are not
eligible for a run. Configuration changes do not create, remove or reclassify any historical
Shift, ShiftAssignment, preference, submission, publication or task.

PUT submits all 51 definitions with expectedVersion for each. Requests with missing/duplicate
IDs, duplicate/out-of-range orders, invalid periods or invalid staffing are rejected.
The complete save is one transaction. The existing AccountService transaction advisory
lock serializes this infrequent operation against other configuration saves and account
changes; actor role, active flag, initial-password requirement and security stamp are
rechecked after waiting. All incoming versions must match. A stale editor receives 409;
no partial changes survive. A repeated no-op save does not increment versions.

The UI shows unsaved changes, explicit success/error feedback and warns before leaving
via month/week controls, links, refresh or page unload. Native browser history navigation
is not blocked. After save it reloads the board so a DAY/NIGHT change updates canonical
cell boundaries. A failed read after a successful save retains the saved versions and
success message but hides the stale board and offers refresh. No browser draft storage
or full configuration audit/version history is implemented.

## Week and time contract

The default week uses TimeProvider.GetUtcNow converted to Asia/Jerusalem, even when the
UTC calendar date is still Saturday and Israel is already Sunday. An explicit date is
a DateOnly business date; the server normalizes it to Sunday and returns seven consecutive
calendar dates through Saturday. Cross-month/year weeks remain single boards.

Each cell uses ShiftClock.Boundaries independently for its local start/end: DAY 07:00–19:00;
NIGHT 19:00–07:00 next day. Never add twelve elapsed UTC hours to a night start. DST nights
may be eleven or thirteen elapsed hours. API instants are UTC; headers are local date-only
DD/MM/YYYY labels beneath א׳, ב׳, ג׳, ד׳, ה׳, ו׳, ש׳. Supported weeks fit entirely within
years 2–9998, leaving room for canonical next-day boundaries.

The table has 51 data rows, a fixed row-label column, seven date columns, sticky labels
and headers, independent two-axis scrolling, semantic table headers and a keyboard-focusable
scroll region. Narrow screens retain the seven-day grid with horizontal scrolling instead
of hiding dates. The configuration table also scrolls horizontally. Color is not required
to understand unassigned/zero-staffing/excluded states.

## APIs

All routes require ADMIN, the normal password-change gate and active session validation.
PUT also uses existing CSRF protection. No new permissions or secrets are required.

| Method | Route | Result |
| --- | --- | --- |
| GET | /api/admin/scheduling/board?date=2026-10-29 | Canonical Sunday–Saturday dates, ordered rows, existing monthly/time-shift IDs, saved memberships and staffing snapshots; date optional |
| GET | /api/admin/scheduling/shift-types | All 51 definitions in display order |
| PUT | /api/admin/scheduling/shift-types | Atomic complete configuration replacement with per-row expectedVersion |
| GET | /api/admin/scheduling/automatic-configuration | Positive-staffing selected rows plus explicit excluded-definition IDs |

PUT body contains `shiftTypes`, exactly 51 objects, each with id, expectedVersion, name,
displayOrder, period (`DAY`/`NIGHT`), requiredEmployees and includeInAutomaticScheduling.
IDs/versions are taken from GET. Slot and UpdatedAt are server-owned and omitted from PUT.
Invalid inputs return 400; unauthenticated/inactive sessions 401; unauthorized/password-gated
users 403; stale configuration or noncanonical existing time shifts 409. Data uses DTOs,
not serialized tracked entities. Board reads use REPEATABLE READ for a coherent snapshot.

## Five-person legacy rule and Phase 4B integration

The original Phase 1 document proposed five employees per DAY/NIGHT Shift. That is **not**
the staffing requirement for the new row types: each definition has its own explicit count,
including zero and values other than five. No fixed-five validation is introduced here.
Existing unique (ShiftId, UserId), one filtered lead index per Shift, task foreign keys and
hard-block triggers remain intact; no historical assignment is moved or deleted.

`IAutomaticSchedulingConfiguration.GetConfiguration()` exposes a read-only snapshot. Rows
contains only IncludeInAutomaticScheduling=true AND RequiredEmployees>0, in display order.
ExcludedDefinitionIds includes all other rows and is an explicit do-not-touch set. Each
included row retains identity/version, DAY/NIGHT classification, TimeRange and required count. It
cannot generate assignments and is not a complete scheduling API. Run inputs
snapshot/revalidate the configuration versions within the scheduling transaction.

Phase 4B associates existing ShiftAssignment memberships with dated SchedulingCell records,
without inventing an association for historical assignments. Every overlapping availability
anchor is checked, while the original canonical lead/member uniqueness rules remain intact.
The detailed implementation, objective ordering, preview/save protocol and current manual
verification instructions are documented in **Phase 4B: automatic draft scheduling** below.
Publication, manual assignment editing, automatic lead selection, coordinator tasks and
deployment remain outside this phase.

## Validation and local review

Run `./scripts/dev.sh migrate`, then `./scripts/dev.sh`. Sign in as an administrator and
open **סידור עבודה** (`/scheduling`). Verify 51 rows and seven date headers; navigate across
month/year boundaries and back to the current week. Open configuration, rename a row,
swap two display-order values, select NIGHT, enter staffing and toggle future participation.
Save, refresh and restart to confirm persistence. Try zero, negative/decimal counts,
duplicate orders and stale edits from a second tab. Check a tablet/mobile width and keyboard
focus/scrolling; no generated or published employee roster should appear.

The normal CI jobs run all tests and migrations without workflow changes. PostgreSQL tests
exercise clean migration, Phase 3 upgrade preserving users/preferences/submissions/assignments,
idempotency/restart persistence, ordering swaps, zero/positive staffing, inclusion/exclusion,
concurrent stale edits, weekly boundaries/DST, authorization, deactivation, CSRF and SQL
constraints. Frontend tests cover 51 rows/seven dates, Hebrew RTL, navigation, editing,
save/error feedback, local drafts and role-aware access. No test or build is bypassed.

## Phase 4B: automatic draft scheduling

The existing 51-row board remains the administrator workspace. `Shift` remains a
canonical DAY/NIGHT availability and task anchor (unique date/type). A
`SchedulingCell` adds a unique definition/date, its actual UTC interval, monthly
`SchedulePeriod`, required-staffing snapshot and configuration version. Existing
`ShiftAssignment` is the single employee membership source: its optional cell FK
identifies the board position; its Shift FK retains existing availability/task
relationships. Legacy assignments without a cell are protected commitments and
cannot be attributed to an arbitrary board row. They consume employee capacity
but do not count toward any row's requirement. No legacy records are deleted or
reclassified automatically.

`GeneratedByRunId` records assignment provenance. Manual/legacy assignments,
locked assignments, shift leads, assignments with tasks, excluded rows, and rows
currently requiring zero employees are protected. Only generated, unlocked,
non-lead memberships without tasks in included positive-demand target-month
cells can be replaced. Configuration saves never delete assignments. Replaced
cell snapshots update only on explicit draft save; excluded/zero rows retain
both memberships and snapshots. Historical run summaries are retained; cells
represent the latest saved workspace, not immutable versions of every roster.

### Optimizer

Google.OrTools **9.15.6755**, CP-SAT, targets .NET Standard and restores under
.NET 10. NuGet supplies native macOS arm64/x64 and Linux x64/arm64 runtimes. CI
uses Ubuntu 24.04 x64 with PostgreSQL 17; solver unit tests execute the native
library, so incompatible native packaging fails CI. The official installation
reference is https://developers.google.com/optimization/install/dotnet/ .

One Boolean decision is created per legally eligible employee/cell. Constraints:

* Only active users with the EMPLOYEE role are candidates. Multiple-role users
  remain employees when explicitly granted EMPLOYEE; coordinator-only/admin-only
  users do not. A coordinator field adds no staffing position.
* Every overlapping canonical availability anchor is checked, including previous
  night for a 06:00 start and both day and night for 15:00–23:00. Missing
  availability permits assignment. Preferred selections require a submitted
  monthly version; hard blocks include legacy rows defensively, matching Phase 3.
  No availability drafts are persisted. The three-block Sunday–Saturday submission
  rule remains unchanged, with no administrator override.
* Actual UTC intervals cannot overlap or touch for the same employee. Touching
  intervals mean consecutive shifts, including across month boundaries and for
  custom ranges. Each optional interval is extended by one second for CP-SAT's
  no-overlap constraint. No assumed 24-hour date arithmetic is used for work times.
  Local endpoints resolve separately through Asia/Jerusalem (DST nights vary).
* Existing fixed work in the target month and two adjacent dates either side is
  an obstacle. The canonical Shift/User unique index also limits one membership
  per availability anchor, even for otherwise disjoint custom ranges.
* Cell assignments, including protected members, never exceed persisted demand.
  Zero-demand and excluded rows create no candidates and are immutable.
* Invalid protected memberships in included cells cause explicit failure. The
  solver never fixes them by deletion. It selects no arbitrary shift leads.

Five **separate lexicographic solves** maximize expert coverage, maximize ordinary coverage, minimize fairness cost,
maximize preferences, then maximize retained generated memberships. A stage's
objective is fixed only after CP-SAT proves it optimal; otherwise return its best
feasible solution and stop. There are no potentially unsafe weighted tradeoffs
between objectives. Single worker, random seed 42; the wall-time limit may still
make time-limited results machine-dependent. `AutomaticScheduling:SolverSeconds`
defaults to 30 seconds, clamped to 0.01–300, shared across all five solves and model
construction. Cancellation interrupts native solving. Modeling time and database
snapshot time are not forcibly interrupted by this solver time budget; model
construction checks cancellation and an over-budget model returns TIMEOUT.

Fairness minimizes the sum of opportunity-weighted squared employee loads. Each employee's
opportunity capacity is the maximum count of non-touching eligible intervals,
computed by earliest-finish interval scheduling (duplicate rows don't inflate
capacity). This is an interval-based opportunity metric; the additional legacy
canonical-membership rule can further reduce achievable load for disjoint custom
ranges sharing one anchor. It is not an employee quota or a coverage constraint. Each employee's cost is
`floor(1000 * (fixedCount + newCount)^2 / (opportunityCapacity + fixedCount))`.
Weighting squared load by capacity favors equal marginal utilization rather than
making a small pool of highly available employees carry all assignments. Fixed work includes the
month and boundary dates, so fairness does not ignore protected commitments.
Employees with no legal options have no variable fairness term. Differing
availability therefore yields proportional opportunity utilization, rather than
requiring identical assignment counts from employees with differing eligibility.
Preference counts employees/cells with any overlapping submitted preferred anchor.
Stability counts exact employee/definition/date memberships retained; protected
members never participate in churn optimization.

### Preview, execution and persistence

`POST /api/admin/scheduling/generation` with `{year,month}` returns 202 and a run
ID. A bounded in-process channel (8 waiting, one worker) runs asynchronously.
`GET /generation/{id}` returns QUEUED/RUNNING/READY/TIMEOUT/FAILED/CANCELED/SAVED,
solver status, proven objective count, cells and summary. Paths here are relative
to `/api/admin/scheduling`. `GET /generation/{id}/shortages` filters the result;
`DELETE /generation/{id}` cancels/discards it. `POST /generation/{id}/save` explicitly
saves READY results. Existing `GET /board?date=YYYY-MM-DD` includes saved cell
memberships and staffing snapshots. All endpoints retain ADMIN authorization,
password-change gates, cookie validation, no-store headers and CSRF on mutations.
The bounded queue rejects simultaneous same-month generation within one process;
saves serialize across processes in PostgreSQL. No new employee draft endpoint
exists. Existing login rate limiting remains intact; generation adds queue bounds.

Preview objects are memory-only, limited to 32 retained runs, expiring in 30 minutes.
Generation and cancellation perform **no database writes**. Restart loses previews;
multiple replicas require sticky routing or a future distributed queue/cache.
Only save persists SchedulingRun audit metadata. Saved drafts survive restart and
are retrieved through the board. Runs are shared among administrators, not tied
to the administrator who requested generation; saving records the approving user.
The UI polls once per second without blocking the board; progress is stage state,
not a fabricated percentage. Network failures are visible. Cancel aborts the worker
and drops preview cells. Navigate across Sunday–Saturday weeks to inspect the full
month; boundary weeks include neighboring saved dates.

Input reads use a repeatable-read snapshot. SHA-256 fingerprints cover configuration,
accounts and roles, relevant canonical shifts, cells, assignments/task protection,
preferences, submission versions, and period state. Save obtains the global
scheduler advisory lock, adjacent month creation/period locks in chronological
order, account/configuration lock, then all employee locks in ID order. This
matches Phase 3's period-before-employee protocol. Re-read inputs after acquiring
locks; reject changed inputs with STALE_PREVIEW. An independent validator rechecks
eligibility, hard blocks, capacity, protected membership and intervals. Insert
run/cells and replace eligible memberships in one transaction; any failure rolls
back even the initial replacement delete. Account/role changes and configuration
writes use the shared account lock; availability changes use month/employee locks.
Future manual scheduling writers must join this protocol. Raw SQL by an external
operator is outside the application lock protocol.

`OPTIMAL` means all five objectives were proven. `FEASIBLE` means a legal incumbent
was found before the budget ended; `CoverageOptimal` separately indicates whether
both coverage objectives were proven. `TIMEOUT` has no solution and cannot be saved; `FAILED`
is a solver/model/technical failure. A READY result may be understaffed. Summary
reports required, filled, missing, understaffed cells and fully staffed cells over
included positive-demand generated cells, including their protected members.
Shortage is `max(0, requiredSnapshot - assignmentCount)`. No placeholders exist.
Zero/excluded rows are outside this run summary. Hebrew singular/plural shortage
labels appear directly in cells and in an optional scrollable shortage list.

PublicationReady is always false: no lead qualification or designation policy is
established. Existing lead memberships remain unchanged; new leads remain pending.
No publication, employee draft access, coordinator tasks or deployment is added.

### Manual verification

1. Apply migrations with `dotnet ef database update --project src/ShiftManager`.
2. Sign in as administrator, open `/scheduling`, set several included rows to
   different required counts (e.g. 5/3/1/0), and leave another excluded. Save settings.
3. Choose a month, expand requirements, generate, and wait for a preview. Navigate
   weeks; inspect names, shortage labels and overall summary/list. Verify the
   preview label and pending-lead notice. Cancel and refresh: assignments unchanged.
4. Regenerate and explicitly confirm save. Refresh: saved-draft labels, employee
   names and snapshots persist. Employees cannot access these administrator APIs.
5. Generate again; change availability, account eligibility or saved configuration
   in a second session. Attempt save: stale result rejected; regenerate.
6. Test final-night/next-month and previous-night/first-day commitments, DST months,
   protected/excluded memberships and a low solver time limit. FEASIBLE shortages
   are never presented as necessarily unavoidable. TIMEOUT cannot be saved.
7. Run `RUN_POSTGRES_TESTS=1 dotnet test ShiftManager.slnx` (Docker PostgreSQL 17),
   `npm test --prefix frontend`, `npm run build --prefix frontend`, and EF pending
   model checks. CI runs migrations and all integration tests on disposable DBs.

The migration extends Phase 3's PostgreSQL hard-block backstop to use cell work
intervals on membership writes and preference writes, and guards cell interval
changes. Thus a block overlapping another canonical anchor cannot be bypassed
by a direct administrator assignment or block insertion. Rollback restores the
original Phase 3 trigger function and removes cell-specific triggers.

### Shift-lead qualification and expert coverage

`ApplicationUser.IsShiftLeadQualified` is an administrator-managed qualification, independent
of Identity roles. Only active employees are solver candidates. The additive migration defaults
all qualifications and row classifications to false, preserving users, row IDs, names and staffing.
An administrator must explicitly designate the existing expert row; names are never used as keys.
`ShiftTypeDefinition.RequiresShiftLeadQualification` has a filtered unique index, permitting
zero or one designated row among the existing 51. Renames preserve classification.

The existing CP-SAT model solves the complete problem in five stages: maximize expert
coverage; maximize ordinary coverage; minimize existing normalized squared workload cost;
maximize preferred assignments; maximize retained replaceable assignments. Each stage must
be proven optimal before its value is fixed by equality and the next stage starts. On timeout,
the last feasible solution is returned without claiming unproven optimality; coverage is proven
only after both coverage stages. Fixed assignments contribute constant coverage and workload.
All existing hard constraints apply before any objective. Protected expert members must be
qualified; conflicts abort generation instead of deleting protected work. Qualification and row
classification are included in input fingerprints and checked again during transactional save.

Expert-row membership does not set `ShiftAssignment.IsShiftLead`. That existing designation
remains a separately protected, at-most-one-per-canonical-shift responsibility, enforced by the
unchanged filtered unique index. This allows configurable expert staffing above one without
arbitrarily choosing a designated lead or creating duplicate designations. Existing designated
leads are preserved; no automatic lead selection is introduced.

Manual verification: apply migrations; enable “מוסמך כאחראי משמרת” on an active employee;
mark the existing expert row using “שורת אחראי משמרת”, set staffing and include it in generation.
Rename it and reload. Generate a monthly preview, inspect qualified members and highlighted
expert shortages, cancel, regenerate and save a draft. Change qualification or staffing while
a preview is open and verify stale-save rejection. Confirm ordinary and excluded rows retain
their existing behavior and employee/coordinator accounts cannot access administrator settings.
