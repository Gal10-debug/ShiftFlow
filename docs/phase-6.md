# Phase 6 — priorities, final corrections and employee absences

Implementation branch: `feature/phase-6-scheduling-absences`. The verified base is
`c84c8e5`, including Phase 4C and the merged scheduling-board UX work. `main` and
`origin/main` are read-only; implementation does not push, merge or open a PR.

## Stage A: scheduling objectives and manual-only rows

Priority rows are the seven stable seeded definition GUIDs ending in
`000000000001` through `000000000007`, under prefix `40000000-0000-0000-0000-`.
Changing names or display order does not change their identities. They form one
priority group; there is no ranking within that group.

CP-SAT runs seven separate lexicographic stages, pinning each proven optimum
before attempting the next stage:

1. Maximize total expert/shift-lead-qualified row coverage, preserving the existing
   expert coverage guarantee.
2. Among those optima, maximize expert coverage in priority rows.
3. Maximize ordinary coverage in priority rows.
4. Maximize remaining ordinary coverage.
5. Minimize the existing opportunity-normalized workload fairness cost.
6. Maximize employee preferences.
7. Maximize stability of replaceable generated assignments.

Lower-priority coverage cannot outweigh a higher objective. If a stage has only a
feasible incumbent when the shared time budget expires, return it without running
later stages. `CoverageOptimal` requires all four coverage stages to be proven;
`OPTIMAL` requires all seven stages. Hard constraints, effective per-date capacity,
zero-required exclusion, expert eligibility, night/weekend quotas and atomic
on-call equality remain binding.

`ShiftTypeDefinition.AssignmentMode` is persisted as `Automatic` or `ManualOnly`.
The user-confirmed mapping uses immutable identities, never runtime name matching:

| Stable slot | Confirmed label | Mode |
| --- | --- | --- |
| 12 | עד 16 | ManualOnly |
| 13 | עד 13 | ManualOnly |
| 14 | זמין ערב | ManualOnly |

Manual-only rows retain their names, order, time ranges, configuration, visual
appearance and effective capacity. They stay on the board and remain manually
editable. The mode overrides the existing automatic-inclusion flag. They do not
produce automatic variables, automatic staffing objectives or automatic shortage
summaries. Their unfilled positions do not inflate the publication acknowledgment
count. Per-cell displayed requirements remain authoritative. Their existing
assignments are retained as protected work when regenerating, including previously
generated memberships; invalid protected work is reported rather than deleted.

## Stage B: final publication corrections and history

The live roster is the working copy retained at publication; employee-visible data
comes exclusively from immutable publication JSON. Clicking an editing action on a
finalized board and saving an assignment command atomically opens or resumes
`UNDER_CORRECTION`. The first command and state transition share the existing
scheduling locks and optimistic input token. A final period must have a real final
publication before it can reopen. The explicit `/correction` action also accepts
finalized periods.

Administrators can assign, remove, replace, move or change notes, including atomic
Friday/Saturday pairs spanning two editable/correcting months. A direct board pair
command can reopen a finalized neighbor as a correction under the same transaction.
The older `/on-call-pair` compatibility endpoint retains its explicit adjacent-period
acknowledgment and requires finalized periods to be opened for correction first.

A correction after final publication publishes directly as a new **FINAL** version.
It cannot publish a new preliminary version or restart the initial workflow. The
initial final publication still requires a preceding preliminary publication.
Repeated final corrections are supported. Old versions and their assignment notes
remain unchanged; employees see the previous final until the replacement final is
published. Assignment changes and final-correction entry record actor and UTC time
in immutable schedule-edit audit records. Stale tokens reject concurrent overwrites.

The comparison selectors, comparison display and unused comparison helper are
removed. History still shows version numbers, publication timestamps, publisher
identity/name, publication notes and a read-only roster for every stored snapshot.
Publisher names are current account display names; stable publisher IDs remain in
administrator API metadata. Employee responses continue to redact publisher identity.

## Stage C: absence requests and decisions

Absences are separate from monthly availability. Four additive entities are used:

- `AbsenceRequest`: employee, proposal kind/status, reason, inclusive effective date
  range, full-day or local-time range, recurrence flag, explanation, version,
  timestamps and optional predecessor/version.
- `AbsenceWeekday`: normalized weekday membership for a weekly rule (Sunday = 0).
  No per-month copies or materialized future occurrences are required.
- `AbsenceDecision`: one immutable decision per request, with actor, UTC time,
  decision and mandatory rejection reason for rejection.
- `AbsenceConflict`: a published assignment requiring correction, referencing its
  original publication and, after resolution, its replacement publication.

Reasons are STUDIES (לימודים), RESERVE (מילואים), VACATION (חופשה) and OTHER (אחר).
Statuses are PENDING, APPROVED, REJECTED and CANCELLED. New requests are pending.
Employees can cancel their own pending proposals immediately. An approved request
has no direct employee cancellation or mutation operation. Instead, MODIFY and
CANCEL proposals reference the approved predecessor and its version. At most one
pending change can target a predecessor. The original remains binding while the
proposal is pending or rejected. Approval atomically cancels the predecessor; an
approved MODIFY becomes the replacement restriction, while an approved CANCEL
proposal is a decision record without an effective restriction.

Only active, authenticated employees submit personal requests. Administrators
approve requests globally. Current team leaders and deputies approve only current
members of their own teams. Team transfers revoke previous approver access. All
list, conflict and decision endpoints enforce this scope on the server; approval
never grants scheduling administrator privileges. Requests/decisions use version
checks, a unique decision index, a unique pending-successor index and transactional
locks to prevent duplicate or conflicting decisions. PostgreSQL rejects mutation
of decision audit rows.

## Stage D: interval rules and conflicts

A single `AbsenceRules.Overlaps` implementation is shared by automatic eligibility,
manual candidates/creation/replacement/movement, retained-work validation, roster
review, publication validation and approval conflict reporting. Scheduling input
fingerprints and explicit-review hashes include effective approved rules and their
versions, so approval, modification or cancellation stales existing previews/reviews.
Pending proposals are excluded from hard constraints and fingerprints, and produce
review warnings only. Approved absences cannot be bypassed by an administrator.

All date/time rules use **Asia/Jerusalem**. Date ranges include both endpoints.
One-time partial-day requests apply the specified time range on each date in the
range; weekly requests apply it on selected weekdays within the effective range.
A missing end date is permitted only for weekly recurrence. A partial end time
before the start time extends into the following day; equal start/end is invalid.
Actual UTC intervals are compared using half-open overlap (`start < otherEnd` and
`otherStart < end`), including overnight shifts that start on the preceding date
or in the preceding month. Existing independent consecutive-shift rules are unchanged.

Each local boundary is converted separately. Full days on Israeli DST transitions
therefore span 23 or 25 elapsed hours. An ambiguous autumn boundary conservatively
covers both clock occurrences (earlier start/later end). A nonexistent spring
boundary advances to the first valid local minute. Recurrence is expanded only over
the actual interval being checked, so indefinite rules apply to future months
without creating future database rows.

Before approval, the approver retrieves a structured conflict report covering
editable working assignments and each period's latest active preliminary/final
snapshot. It includes dates, actual intervals, row names, publication/version,
removability and removal blockers. Old superseded snapshots remain history and do
not create new operational conflict records. Approval recomputes this report under
locks and requires its current token.

Draft removal requires an explicit `confirmDraftRemovals`. Only listed overlapping
assignments are removed, plus their on-call mates to preserve atomic pairs. Audit
records identify removed assignments and the absence request. Unrelated work is
untouched. Task-linked assignments block removal until their task relationships are
resolved. A pair involving a finalized neighbor must first be opened for correction
before absence approval can remove draft work across that boundary.

Published overlaps do not prevent approval. Approval records unresolved conflicts
and leaves published JSON and finalized working assignments untouched. The UI
explains that schedule correction and republication are required, and administrators
can open the relevant month directly. Review/publication rejects working assignments
overlapping approved restrictions. Removing a conflicting working assignment alone
does not resolve the published conflict: a replacement publication, after complete
validation, records its resolution and replacement publication ID. Historical
snapshots remain accessible with their original content. Superseded/cancelled absence
requests retain their decision/conflict audit history; publication conflict records
are closed when a replacement schedule is published.

Lock order is scheduling-global lock → existing period locks in chronological order
→ account lock. This agrees with availability writers' period-before-account order,
avoiding deadlocks when approval removes draft work while availability changes.
The global scheduling lock serializes decisions with preview saves/manual edits and
publication. The account lock protects live team/role authorization during decisions.

## Stage E: Hebrew RTL workflows

- **היעדרויות** (`/absences`): employee request form, one-time/weekly selection,
  reasons, dates, weekdays, full/partial-day hours, explanation, history, status,
  decisions/rejection reasons, pending cancellation and approved-change proposals.
- **אישור היעדרויות הצוות** (`/team-absences`): team-scoped review, conflict preview,
  explicit draft-removal acknowledgment, approve/reject and decision history.
- **ניהול היעדרויות** (`/absence-management`): global management, filters by team,
  employee, status and effective date window, conflict visibility, audit metadata
  and links to `/scheduling?period=YYYY-MM` for correction.

Pending and approved-with-unresolved-publication-conflict states are distinguished
explicitly. Request text/notes are rendered as plain React text. Existing aggregate
staffing-shortage lists remain removed. The scheduling review shows pending-absence
warnings without turning them into hard blocks.

## API

All mutations require the existing authenticated session and CSRF protection.
Dates use `YYYY-MM-DD`; partial-day JSON times use `HH:mm:ss` (the UI normalizes native
hour/minute inputs). All absence writes revalidate the actor after acquiring locks.

| Method | Route under `/api/absences` | Purpose |
| --- | --- | --- |
| GET | `/mine` | Employee's own requests and decisions |
| POST | `/` | Submit a new pending proposal |
| POST | `/{id}/changes` | Propose MODIFY/CANCEL of an approved restriction |
| POST | `/{id}/cancel-pending` | Cancel an employee-owned pending proposal |
| GET | `/manage` | Authorized team/global list; teamId/userId/status/from/through filters |
| GET | `/{id}/conflicts` | Authorized approver's current conflict report/token |
| POST | `/{id}/decision` | Versioned approval/rejection with conflict token and explicit draft-removal confirmation |

Validation rejects invalid dates, unsupported reasons/statuses, duplicate/out-of-range
weekdays, empty weekly selections, indefinite one-time requests, inconsistent full-day
hours, equal partial boundaries and explanations over 2000 characters. Rejection
requires a nonblank explanation. No API permits employees to remove an approved rule.

## Migrations and deployment

- `20261103080334_Phase6AssignmentModes`: adds mode with Automatic default, sets only
  stable slots 12/13/14 to ManualOnly and adds a value constraint. It does not rename,
  reorder, retime, reset demand or delete existing assignments.
- `20261103081004_Phase6Absences`: adds the four absence tables, restricted FKs,
  effective-rule/list/conflict indexes, lifecycle/date/time constraints, concurrency
  metadata and immutable decision trigger. It does not modify monthly availability,
  existing publications, drafts or user records.

No existing development/demo database is migrated by this implementation. Before
operational deployment, identify the actual target connection and apply pending
migrations through the normal controlled process, then deploy matching server and
frontend assets. Older pending migrations, including WeekendBoardNotes, still apply
in order. Never reseed/reset existing data as a migration shortcut. Rolling back the
new absence migration drops its request/decision/conflict history; preserve those
records before an operator-approved rollback. Publication JSON remains immutable.

## Verification

Integration tests use fresh disposable PostgreSQL databases. The final verification
service is a newly created loopback-only PostgreSQL 17 container with a `shiftflow_ci`
bootstrap database; the existing test helper creates a unique `shiftflow_test_*`
database per test and cleans up only that database. Development/demo databases,
running demo processes and stored publications are not used as test targets.

Coverage includes priority tradeoffs, expert guarantees, exact/zero capacity,
manual-only preservation, repeated final revisions, direct board on-call corrections,
immutable history, absence lifecycle/authorization/concurrent decisions, recurring
partial-day rules, cross-month/overnight and Israeli DST intervals, pending warnings,
approved solver/manual/publication exclusions, transactional conflict removal and
resolution only through a new publication. Frontend interaction tests exercise the
Hebrew RTL forms, filters, decision acknowledgments, modification/cancellation,
version history and post-final publishing controls. Script tests remain unchanged.

Live browser/mobile/keyboard visual verification is outstanding: the Browser runtime
reported no available browser connection. Component and HTTP integration tests do
not substitute for visual inspection.

### Recorded final verification — 2026-11-03

- Complete backend/PostgreSQL suite: **282 passed, 0 failed, 0 skipped**; every
  integration test used its own disposable database on the new verification service.
- Frontend: **118 passed** across 8 test files.
- Development script suite: **7 passed**.
- Frontend production build passed (TypeScript and Vite).
- Backend Release build passed with warnings treated as errors: **0 warnings/errors**.
- EF reported no model changes since the last migration; `git diff --check` passed.
- Focused Stage A/B/C/D regressions passed before later-stage work. The original
  full run exposed a legacy on-call compatibility regression; retaining that
  endpoint's explicit correction contract resolved it, and the final full run passed.
- Existing development/demo databases were not migrated, reseeded or modified;
  no running demo was restarted. No remote Git branch was updated.
- No live browser was available, so visual/mobile/native-focus inspection and a live
  browser end-to-end run remain manual follow-up checks. HTTP integration tests and
  frontend interaction tests passed.
