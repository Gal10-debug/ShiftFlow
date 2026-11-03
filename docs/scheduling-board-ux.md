# Phase 5.3 — scheduling board and weekend rules

## Confirmed row identities

The confirmed demo mapping supersedes the initial three-row request: **slots 2, 3,
4, 5 and 16** may be staffed on Friday/Saturday; slot **16** is the atomic weekend
on-call row. These are immutable seeded GUID suffixes `000000000002`,
`000000000003`, `000000000004`, `000000000005`, `000000000016` under prefix
`40000000-0000-0000-0000-`. Names and display order may change without changing the
rule. The existing 51 definitions and their order are retained.

`SchedulingRules.Applicable` is authoritative for solver eligibility, manual
candidate validation, review/publication validation and effective demand. Other
weekend cells have zero effective demand and no artificial shortage. Stored weekday
staffing remains configurable. On-call has zero weekday demand. Dates use existing
Asia/Jerusalem shift boundaries; neither UTC weekdays nor browser-local weekdays
determine domain eligibility. Existing expert requirements are unchanged by the
migration: ordinary facility A work does not automatically acquire expert status.
New synthetic demo seeds qualify only slots 2/4 as experts; slots 3/5/16 do not
require that qualification.

## Migration and existing data

Apply `20261031164800_WeekendBoardNotes` explicitly with the normal migration
command after reviewing the target. It adds nullable `ShiftAssignment.Note`
(varchar 1000) and transfers the unique `WEEKEND_ON_CALL` classification from
slot 26 to confirmed slot 16. It clears slot 26 first to satisfy the unique semantic
index, and increments both definition versions. It changes no names, staffing,
qualifications, row order, assignments or stored publications. Down migration
restores the old classification and drops notes; back up notes before rollback.

**Existing slot-26 draft work is not automatically moved to slot 16.** It remains
visible for review and may now violate the weekend rule; correct affected editable
drafts explicitly. Existing final/preliminary snapshots remain immutable and keep
historical names, assignments, staffing and qualifications. Old snapshots without
notes show none; old snapshots without applicability metadata retain their recorded
cells. Do not apply this migration to an existing manually edited demo as part of
verification, or silently overwrite its data. Review environment-specific row
configuration before deploying to databases with labels different from the demo.

## Direct cell editing

The same `RosterGrid` renders administrative drafts, employee publications and
administrator historical versions. The administrator clicks a cell's add/edit
button or an existing employee's name. A compact RTL native dialog allows add,
remove, replace, move and note editing without leaving the board. Native modal
focus/escape behavior supports keyboard users. Candidate responses include full
name, expert qualification, submitted preference and authoritative eligibility
reasons. Invalid candidates cannot be selected; loading, errors and success are
shown. Submissions are guarded against duplicates. A successful edit refreshes
board demand, assignments and shortages.

Only DRAFT/UNDER_CORRECTION permits manual changes. Publication controls, audit,
optimistic draft tokens, transaction locks, task-linked protections and final
immutability remain in force. Moves between ordinary cells stay within the selected
month, as before. On-call may move to another on-call weekend pair; moves between
an ordinary row and the pair row are rejected. Resolve task-linked allocations
before removing/replacing/moving them. Note-only edits do not detach tasks.

## Automatic on-call pairing

`POST .../assignments` transparently routes on-call add/remove/replace/move/note
commands to the existing atomic pair transaction. Either Friday or Saturday can
be the selected cell. Both days use the same employee and both adjacent periods
must be editable. One blocked day rejects the whole transaction. Availability,
qualification, overlaps, weekly nights, maximum two monthly weekend allocations
and canonical-shift membership still apply. One pair counts once in Friday's month.
Candidates are checked against the complete pair, not only the clicked date.

The separate pair editor has been removed. The old `/on-call-pair` endpoint remains
for compatibility and retains its explicit adjacent-period acknowledgment contract.
The ordinary cell endpoint handles adjacent days automatically under locks. Solver
equalities retain identical Friday/Saturday membership. As before, monthly automatic
runs do not write adjacent months: a boundary pair needs an existing matching mate,
or must be created with a manual cell operation. Unfilled applicable cells remain
honest staffing shortages.

## Assignment notes

Notes are untrusted plain text, at most 1000 characters, and may contain Hebrew and
newlines. Null, empty or whitespace-only input removes a note. React text rendering
escapes markup. Notes appear under the employee name and are explicitly described
as visible to **all employees after publication** in the editor.

Lifecycle policy:

- Add: optional note belongs only to the new assignment.
- Note operation: updates that assignment, increments the period revision and writes
  before/after audit values; does not change its identity or task relationships.
- Move: carries the assignment's note to the destination.
- Replace: clears the previous employee's note; an explicitly provided new note may
  be attached to the replacement. It never transfers another employee's instructions
  implicitly.
- Remove: deletes the live note with the assignment; publication/audit history keeps
  the previously recorded value.
- On-call: a note is identical on both linked assignments. Editing either day updates
  both atomically; pair movement carries it; replacement clears it unless explicitly
  supplied anew. Both periods receive audits when the pair crosses months.
- Automatic regeneration: an assignment with a note is protected from automatic
  replacement, so the generator cannot silently delete an administrator's note.

Publication serializes notes into immutable JSON. A correction after preliminary
publication changes only the working draft. Employees see it only after explicit
republication/final publication. Historical notes are read from that version's JSON,
never joined from live assignments.

## Full employee roster and presentation

`GET /api/my-schedule/{year}/{month}` now returns the complete latest authorized
preliminary/final snapshot. Optional `?version=N` reads a historical published
version of the same period. No draft, availability submission, credentials or user
profile data is returned. The employee response redacts publisher user ID. All
published assignment notes are shared. Employee issue reporting still enforces
ownership, current preliminary version and allowed lifecycle state on the server;
users cannot report other people's allocations or edit assignments.

Month/week filtering and version selection render the same dates, ordered rows,
labels, staff and notes as administrator snapshots. The current employee is
identified by immutable user ID and marked with an outline, weight and ★ label;
duplicate display names cannot highlight another employee. Rows use centralized,
deterministic restrained HSL colors derived from the definition GUID. No random
colors, new color schema or duplicate rows are introduced. Sticky row labels share
the complete row's color. Employee dividers, shortage indicators and disabled-cell
hatching remain distinguishable. Applicable/disabled state comes from backend
metadata, not a second frontend implementation of the weekend rule.

## Verification and demo safety

Use a **new empty isolated demo database/seed**, following [demo-testing.md](demo-testing.md).
The seed uses the new on-call identity and valid weekend demand, with example
facility labels and explicit qualifications. The smoke workflow adds a public
Hebrew assignment note, verifies the complete published roster, creates employee
issues, corrects/re-publishes and verifies immutable history plus all scheduling
constraints. Ownership markers, fingerprints, environment gates and cleanup checks
are unchanged. Do not seed over `shiftflow_demo_202611_42` or any manually edited
database. PostgreSQL integration tests create fresh disposable databases and a fresh
random isolated demo scenario; they do not target normal development data.

```sh
dotnet test ShiftManager.slnx
RUN_POSTGRES_TESTS=1 dotnet test ShiftManager.slnx
npm test --prefix frontend
npm run build --prefix frontend
dotnet build ShiftManager.slnx -c Release
python3 scripts/test-dev.py
```

Backend coverage includes all 51 stable identities, solver eligibility and demand,
manual rollback and publication rejection, both on-call days, cross-month pairs,
notes/authorization/limits/audit and immutable public history. React tests cover
direct editing, eligible candidates, paired-command feedback, notes as text, row
colors/dividers, ID-based highlights, disabled/read-only cells and Hebrew RTL.
Browser visual verification requires an available browser connection; component
and HTTP integration tests do not substitute for a visual inspection.

### Local verification results (2026-10-31)

- Full backend/PostgreSQL suite: 236 passed, zero failed/skipped, including fresh
  isolated demo seeding/smoke, migration and immutable-history checks.
- Standalone backend suite: 122 passed, 114 PostgreSQL-only tests explicitly skipped.
- Fresh isolated demo suite rerun after aligning synthetic morning/night ranges:
  2 passed, zero failed/skipped. Ownership/cleanup refusal checks remain intact.
- Frontend: 92 passed across 7 files.
- Script suite: 7 passed.
- Frontend production build and backend Release build succeeded. A concurrent build
  attempt raced frontend asset generation; rerunning sequentially resolved it.
- `git diff --check` passed. Browser connection reported no available browser;
  live visual, mobile and native keyboard-focus inspection was not performed.

Existing development and manually edited demo databases were not migrated or
reseeded. The supplied migration is tested only in disposable databases here.

## Focused follow-up fixes — 2026-11-01

Row 3 (immutable ID suffix `000000000003`) now requires **one person on Friday
and one on Saturday**, independently of its weekday setting. Sunday–Thursday and
all other definitions keep their configured counts and previous applicability.
The approved weekend identities remain 2/3/4/5/16. This is an effective domain
staffing override, not a React constant or an update to saved configuration.
`WeekendRequiredEmployees` exposes the same derived demand to configuration clients;
an enabled row with zero weekday demand still has weekend demand. Solver inputs,
manual capacity checks, board cells, stored cell demand on successful edits,
review and newly published snapshots use the same calculation. Existing snapshots
and live allocations are not automatically rewritten. Existing excess allocations
must be corrected explicitly before publication. No new database migration is needed.

The automatic-generation aggregate shortage checkbox/list was removed. Per-cell
counts/shortages, backend summary data, hard constraints and mandatory publication
acknowledgments remain.

### Editing failure investigation

Read-only inspection of the existing demo startup record identified backend PID
36621, started 2026-10-31 at 15:07 with `dotnet run --no-build --no-restore`.
The Phase 5.3 commit was created later that day at 19:03. The earlier server's
`ManualAssignmentRequest` lacks `Note`. The new board sends `note`, including a
JSON null value for operations without a note. Both application versions configure
`JsonUnmappedMemberHandling.Disallow`. The old MVC JSON binder therefore rejects
that payload before the domain service/EF pipeline and returns the application's
400 `VALIDATION_ERROR`; the frontend displayed its generic Hebrew field/length
message. Existing database schema does not explain that particular binding failure:
EF is never reached in this reproduction.

`BoardBugFixTests` reproduces all five operations with the exact old DTO shape and
strict JSON settings, then exercises an isolated MVC TestServer using the real
`ApiErrors.Validation` factory. The old shape returns 400/VALIDATION_ERROR and the
current shape binds successfully. PostgreSQL tests submit the board's exact
camelCase JSON, including null IDs/date/note fields, and verify persisted/reloaded
Hebrew notes, empty/null notes, exactly 1000 characters, rejection at 1001, manual
operations, authorization, state restrictions and immutable published history.
No live browser request body was captured; the available evidence is the recorded
process start and the reproducible old/new binding behavior.

Review responses now advertise `editingContractVersion: 1`. The UI blocks manual
edits when it is missing/unsupported and explains that the updated server must be
started after migration checks, rather than sending incompatible JSON. Genuine
note/ID/token validation failures return safe field identifiers and stable codes;
the editor displays the corresponding Hebrew explanation, without echoing JSON
parser messages, note text or credentials. Length limits and strict validation
remain unchanged.

**Operational limitation:** the existing demo/development databases and running demo
process were intentionally not changed, migrated, reseeded or restarted during this
fix. To use Phase 5.3 in that environment, its server binary and previously supplied
schema migration must be brought into sync through a separately controlled update.
The compatibility message identifies this requirement; it does not bypass it.

A subsequent read-only metadata query confirmed the existing demo's latest applied
migration is `20261027224058_AdditionalSchedulingConstraints`; `ShiftAssignment.Note`
is absent. Thus that environment needs the **previous Phase 5.3** migration
`20261031164800_WeekendBoardNotes` as well as a server restart. This follow-up adds
no new schema migration. Applying the earlier migration to a manually edited demo
was explicitly outside the authorized testing/setup scope and was not performed.

Follow-up verification results:

- Before the staffing fix: 3 new staffing cases failed, while all 5 legacy/current
  payload reproduction cases and the already-one-person case passed.
- Final full backend/PostgreSQL run: 248 passed, zero failed/skipped, including all
  scheduling/publication/demo regressions and the new persisted-request tests.
- Final standalone backend run: 131 passed; 117 PostgreSQL-only tests skipped.
  The strengthened real-MVC contract reproduction passed in this final run.
- Frontend: 95 passed across 7 files. Script tests: 7 passed.
- Frontend production and backend Release builds succeeded; no backend build warnings.
- Existing databases and published schedules were preserved; tests used disposable
  databases. The live demo was queried only for migration/column metadata, not used
  as an automated test target.

## Effective capacity and complete shortage-summary removal — 2026-11-01

The retained-assignment regression is reproduced in
`StaffingCapacityIntegrationTests`: an excluded row has current effective demand
zero, but an earlier locked assignment remains in its saved cell. Previously the
solver validated capacity only for its selected planning cells. The run returned
READY while leaving that excluded assignment on the board. This was not a CP-SAT
variable exceeding its existing `sum(choices) <= required - fixed` bound; the
retained work fell outside that validation set. The regression failed before the
fix (`READY` rather than expected `FAILED`).

`SchedulingInputs` now constructs effective capacity cells for **all 51 definitions
and every requested date**, using the existing authoritative
`SchedulingRules.Required`. The selected planning subset still respects automatic
inclusion. `SchedulingProblem.ExceedsCapacity` checks the complete retained/output
roster against all those capacities before optimization, after solving and before
saving a preview. Zero-demand cells have no eligible solver variables. Required
0/1/2/N is always the target/upper bound; insufficient eligible staff leaves an
internal shortage. Existing zero-demand/excess assignments cause
`STAFFING_EXCEEDED`, without deleting, moving or unlocking them. Review/publication
and manual edits continue to enforce the same effective required value. On-call
cell persistence now also stores that derived value, including existing paired
cells, rather than independently copying the row configuration.

The board API's per-date `requiredEmployees` is now non-null, and the React adapter
uses it directly instead of falling back to a row's weekday configuration. Row
headings refer to the per-date count rather than presenting a weekday zero as a
universal capacity. Sunday–Thursday configured demand, Friday/Saturday restrictions,
row 3's one-person weekend requirement, on-call equality, adjacent-month rules,
qualifications, fairness objectives and Phase 5.1 quotas remain in force. Historical
publication counts stay frozen in their JSON; existing drafts are not rewritten.

Removed UI elements:

- AutomaticGeneration's aggregate “סיכום איוש” block: total required/filled/missing,
  understaffed/fully staffed counts, and aggregate shortage/coverage suggestion.
- The separate expandable row staffing-requirements list above the board, which
  repeated weekday-only counts instead of each cell's effective requirement.
- ScheduleReviewPanel's aggregate “חוסרים” counter beside lifecycle status.
- The unused `.shortage-list` CSS container.

The former shortage-list toggle/list remains absent. No replacement summary,
collapsed shortage list or empty container is rendered. The same RosterGrid keeps
per-cell required/assigned counts and shortage indicators in weekly/monthly,
administrative/employee and historical views. The explicit shortage publication
acknowledgment remains **only when publication is possible and shortages exist**;
no redundant acknowledgment counter is shown in final read-only state. Backend
shortage summaries and publication acknowledgments remain unchanged.

Verification includes exact 0/1/2/7 solver capacities, insufficient-staff shortages,
rejection of retained excluded overstaffing by solver/preview save/publication,
per-date board metadata, manual capacities, row-3 weekend overrides, on-call pairs
and boundary months. React lifecycle × week/month tests assert no aggregate
shortage panel/count/list while retaining cell “נדרשים” and required publication
acknowledgments. Existing data is not used for testing, migrated, reseeded or
modified in this task; PostgreSQL tests use disposable databases only. No schema
migration is introduced.

Final verification for this capacity/UI fix: 254 backend/PostgreSQL tests passed
with zero failures/skips (disposable databases, including isolated demo and
publication-history regression tests); standalone backend tests passed 136 with
118 PostgreSQL-only tests explicitly skipped; frontend passed 103 across 7 files;
script tests passed 7. Frontend production and backend Release builds succeeded,
with zero backend warnings/errors. No application schema migration, live demo
restart, existing-database operation or remote Git write was performed in this task.
