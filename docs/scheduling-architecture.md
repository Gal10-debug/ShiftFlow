# Weekly scheduling workspace (Phase 4A)

## Scope and relationship to existing scheduling data

This phase adds an unpublished ADMIN-only weekly workspace and persistent shift-type
configuration. It does not generate, edit, publish or expose employee assignments.
The board always identifies itself as UNPUBLISHED and renders neutral cells; it is not
a published roster or a view of legacy ShiftAssignment data. EMPLOYEE and COORDINATOR
cannot read the board, configuration or automatic-scheduling preparation endpoints.

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
is created by availability configuration today and will be ensured transactionally by a
future scheduling mutation. An existing Shift with the wrong owning period or noncanonical
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
night and current day; 15:00–23:00 checks the current day and current night. Phase 4B must
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
| GET | /api/admin/scheduling/board?date=2026-10-29 | Canonical Sunday–Saturday dates, ordered rows, existing monthly/time-shift IDs and neutral cells; date optional |
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
cannot generate assignments and is not a complete scheduling API. Future run inputs must
snapshot/revalidate the configuration versions within the scheduling transaction.

Phase 4B must deliberately add a row-definition association to persisted assignments or
an allocation entity referencing the existing Shift/ShiftAssignment, with appropriate
uniqueness and migration behavior for unmapped historical assignments. This phase does
not invent a parallel assignment store or fabricate that association for old data.
A row's calendar date/TimeRange resolves to all overlapping canonical Shift anchors; employee
availability and hard blocks apply to every overlapping anchor across all configured rows.
Use IHardBlockQuery under Phase 3's period → employee lock protocol. Plan any changes to
lead/staffing constraints explicitly; the old global lead index must not be assumed to
mean one lead per configurable row. Excluded/zero-count rows' existing future allocations
must remain untouched. Count changes, including zero, never authorize deleting existing
assignments during a settings save.

Remaining Phase 4B work: define run/assignment persistence, integrate eligible employees,
availability and hard constraints, validate staffing feasibility, implement the engine,
preview/manage drafts and reconcile conflicts. Publication, manual assignments, leads,
coordinator tasks and deployment remain outside Phase 4A.

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
