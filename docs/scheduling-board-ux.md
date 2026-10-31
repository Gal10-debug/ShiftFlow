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
