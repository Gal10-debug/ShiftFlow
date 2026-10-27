# Phase 5 — Review, manual corrections and publication

## Git safety

Root `AGENTS.md` was created and applied during this implementation. `main` and `origin/main` are read-only. All changes belong to `feature/schedule-publication`. Remote writes, PR creation and merges are not part of implementation authorization. The base was verified using a separately authorized read-only `git ls-remote`, without fetching or updating refs.

## Lifecycle and ownership

| State | Administrator | Employee |
| --- | --- | --- |
| DRAFT | Review, manually edit, generate/save and acknowledge review; publish preliminary | No draft access |
| PRELIMINARY_PUBLISHED | Review reports; finalize or enter corrections | Latest preliminary snapshot and own issue reports |
| UNDER_CORRECTION | Edit the same working schedule; generate/save; republish preliminary or finalize | Last published preliminary snapshot, unchanged by draft edits |
| FINAL_PUBLISHED | Read-only schedule; historical review; resolve previously acknowledged reports | Official final snapshot; no new issue creation |

Only ADMIN controls transitions. Membership, organizational leadership and COORDINATOR confer no scheduling or publication powers. Multi-role users retain their explicit Identity privileges. Final publication requires a prior PRELIMINARY publication. There is no transition back from FINAL_PUBLISHED and no shift-exchange workflow in Phase 5.

The historical `PUBLISHED` enum value remains supported as a read-only legacy state. Old publication rows have type LEGACY and a JSON null snapshot because historical assignment content cannot safely be reconstructed. They are retained in administrator history but are not presented as new preliminary/final publications to employees.

## One working schedule, immutable published history

The existing `SchedulePeriod`, `Shift`, `SchedulingCell`, `ShiftAssignment`, 51 shift-type definitions and automatic-scheduling path are reused. No duplicate editable assignment store exists. `SchedulePeriod.Revision` is an EF concurrency token. `ReviewedInputs` records the hash of the inputs last explicitly reviewed or used for an explicitly saved automatic run.

`SchedulePublication` now stores publication type, JSONB snapshot, publisher, UTC timestamp, sequential monthly version, notes, input fingerprint, acknowledgment flags and unresolved-report count. The snapshot contains every row/day's name, stable definition ID, order, day/night classification, effective interval, staffing and expert requirement, plus assignment IDs, employee IDs and names. Canonical historical assignments without a scheduling cell are preserved separately inside the snapshot. Names, staffing and employee details do not resolve through mutable live records when reading history.

PostgreSQL triggers reject UPDATE and DELETE of publication rows and manual-edit audit rows. A new publication always inserts a new version. A unique `(SchedulePeriodId, VersionNumber)` index and transaction locking serialize publication numbers. Adding acknowledgment columns uses a second additive migration; no existing tables or assignments are recreated.

The employee endpoint returns only snapshot cells/legacy assignments belonging to the authenticated employee; it never returns another employee's name or assignment. Version metadata and optional publication notes are published information. Administrator history returns complete snapshots and the UI compares row/day assignments, names, staffing and legacy assignments.

## Validation and manual edits

`ScheduleReviewService` centralizes the lifecycle, manual corrections, publication checks and reporting workflows. It consumes existing `SchedulingInputs` in review mode. Generation retains its original protected-assignment validation and solver behavior; review mode permits inspection of conflicts that generation itself must reject.

Manual operations are ASSIGN, REMOVE, REPLACE and MOVE. A target is identified by definition/date; cells and canonical shifts are created as needed, with a persisted MANUAL scheduling run supplying the existing cell provenance FK. Manually added assignments are locked against automatic replacement. Replacement/move preserves an existing explicit shift-lead designation and requires qualification. Removal/replacement of assignments with linked tasks is rejected to preserve task integrity. Removal may resolve one conflict at a time without being blocked by unrelated existing conflicts.

Candidate responses explain inactivity/missing EMPLOYEE role, organizational leadership, hard blocks, overlap/consecutive work, missing expert qualification and capacity. Candidates are advisory; writes repeat all validation. The hard constraints include actual cell intervals, touching/consecutive intervals, same canonical day/night membership and adjacent-month work. Deputies follow the normal employee path. Team leaders are always excluded. Hard blocks remain binding; AVAILABLE/PREFERRED selections are not hard constraints.

Publication revalidates all assignments, including legacy assignments, against current eligibility, hard blocks, overlapping/touching work, expert qualifications, team-leader exclusion, staffing capacity and consistent shift/cell ownership. A row-time configuration change conflicting with a saved cell is surfaced rather than silently changing work times. Current staffing requirements govern review and shortages. Existing canonical-time guards remain in force.

Manual edits atomically insert `ScheduleEditAudit` with actor, UTC time, operation and before/after assignment IDs, employee, cell, date and work interval. Issue status changes and entering correction mode are also audited. Administrators can inspect `/audit`.

## Concurrency and stale drafts

Critical operations acquire locks in existing order: global scheduling lock → ordered adjacent-month creation/period locks → account lock → employee advisory locks. Team/role/configuration writes share the account lock; availability changes hold period/account/employee locks. This makes publication linearizable with staffing, eligibility, delegated availability, manual edits and issue submissions.

Review tokens reuse the cryptographic scheduling fingerprint, now including period revisions. Every manual operation, transition, review acknowledgment and publication requires the exact displayed token. Issue creation/status updates also increment the period revision so a publisher cannot silently miss a newly opened or changed report. Concurrent publications using the same token have one winner.

The reviewed input hash covers submitted versions, stored preferences, eligibility/roles, leadership and shift configuration. It is deliberately conservative across periods: unrelated input changes can also request fresh review. A stale draft requires explicit administrator review acknowledgment after conflicts are resolved; viewing the page alone never marks it reviewed. Automatic saves mark their current inputs reviewed. All publication operations repeat validation even after acknowledgment.

Delegated availability remains editable during preliminary publication and corrections, using the Phase 4C atomic audit protocol. The PostgreSQL hard-block trigger's audited exception covers DRAFT, PRELIMINARY_PUBLISHED and UNDER_CORRECTION. It never overrides blocks when inserting assignments. New blocks can leave retained working assignments in conflict and stale the draft, while employee-visible immutable snapshots remain unchanged. Final periods reject availability edits. Ordinary personal availability retains the open-window requirement and is writable only in DRAFT.

## Shortages and issues policy

Hard violations always prevent publication. Staffing shortages are allowed only when the administrator explicitly acknowledges them in the request and confirmation dialog. Final publication additionally requires explicit acknowledgment of every remaining OPEN/IN_REVIEW report count, or resolution/rejection of those reports. Acknowledgments and the open count are stored with that immutable publication.

An employee can create one report per own assignment per published version, only against the latest preliminary version while the period is preliminary/under correction. Reports retain period, publication FK, immutable assignment ID, employee FK, explanation, UTC timestamps, status and version. The assignment ID deliberately does not reference the live table: later draft deletion must not erase or invalidate the report. A report never changes an assignment or creates a hard scheduling constraint.

Statuses are OPEN, IN_REVIEW, RESOLVED and REJECTED. Administrators can reopen reports, supply a response and update status using the issue's expected version. Employees see only their own reports, including reports about previous versions. New reports against superseded preliminary versions or finalized periods are rejected. Previously acknowledged reports may still be resolved after finalization.

## API

All mutations use the existing cookie, password-change gate, CSRF header and structured Hebrew error conventions. Service-level authorization checks current security stamps and live roles.

Administrator prefix: `/api/admin/scheduling/periods/{year}/{month}`.

| Method | Suffix | Action |
| --- | --- | --- |
| GET | empty | Lifecycle, token, review status, monthly snapshot, conflicts, shortages, open reports |
| GET | `/candidates?definitionId=...&date=...&replacing=...` | Candidate eligibility and explanations |
| POST | `/assignments` | Manual operation and expectedToken |
| POST | `/review` | Explicit current-input review acknowledgment |
| POST | `/correction` | Enter UNDER_CORRECTION |
| POST | `/publications/PRELIMINARY` | Publish new preliminary version |
| POST | `/publications/FINAL` | Publish final version |
| GET | `/publications` | Complete immutable history for comparison |
| GET | `/issues` | All period reports |
| PUT | `/issues/{id}` | Expected-version status/response update |
| GET | `/audit` | Manual/correction/report-status audit history |

The existing board endpoint accepts `monthly=true`, retaining its weekly default and 51-row layout.

Employee endpoints: GET `/api/my-schedule/periods`, GET `/api/my-schedule/{year}/{month}`, GET and POST `/api/my-schedule/{year}/{month}/issues`. Target employee IDs are never accepted as a way to select another employee's published schedule or reports.

## UI and deliberate limitations

The existing Hebrew RTL scheduling board now supports month selection and monthly/weekly display, manual cell selection, eligible-candidate explanations, review acknowledgment, publication confirmations, issue management and version comparison. `/my-schedule` (סידור העבודה שלי) shows only personal published work, month/week filtering, preliminary/final labels and issue submission/status. Existing team-management permissions remain unchanged.

No exchanges, coordinator task assignment, notifications, deployment or unrelated authentication changes are included. Legacy PUBLISHED history without snapshots is retained but not reconstructed. Saved cells whose timing differs from current definitions must be emptied before a manual addition can adopt the current timing, or regenerated under matching definitions; the application does not silently retime existing assignments. Task-linked assignments require task resolution before removal. Review fingerprints conservatively invalidate across periods. Published snapshots are intentionally never revised after finalization.

## Migrations and rollback

- `20261027215951_SchedulePublicationWorkflow`: additive period/publication fields, issue/audit tables, lifecycle constraint expansion, immutable-history triggers and updated audited hard-block function.
- `20261027220339_PublicationAcknowledgments`: persisted shortage/report acknowledgment fields.

Existing users, teams, preferences, submissions, assignments and publication rows survive upgrades. JSON null and LEGACY defaults preserve preexisting publications. Rolling Phase 5 back drops only the added Phase 5 columns/tables, restores the Phase 4C trigger and maps published/correcting new states to legacy PUBLISHED; it necessarily removes Phase 5 snapshots, reports and audit details. Back up those records before any operational rollback. It never reconstructs historical snapshot data or resets an existing schedule to an editable draft.

## Manual verification

1. Run `./scripts/dev.sh migrate`, then `./scripts/dev.sh`. Sign in as ADMIN. Select a month on the scheduling board; switch between weekly/monthly layouts and verify all 51 rows.
2. Assign an eligible employee to a positive-staffing cell. Try a leader, an unqualified expert candidate, an overlapping or consecutive shift, a full cell and a blocked date. Verify Hebrew reasons and server rejection. Check neighboring days/month boundaries.
3. Open two administrator sessions on the same token. Save a manual edit in one; the other must receive STALE_DRAFT. Exercise replacement, move and removal; inspect `/audit`.
4. Acknowledge review. Attempt preliminary publication without accepting shortages; verify it is blocked. Accept shortages and confirm publication. Inspect immutable version 1.
5. Sign in as an employee/deputy and open סידור העבודה שלי. Verify only their assignments appear and no administrator draft is accessible. Submit an explanation for an assigned shift; verify own status.
6. Enter correction mode. Move/remove a draft assignment. Refresh the employee view; version 1 must remain unchanged. Review and resolve/reject the report with an administrator response.
7. Make a delegated availability change after preliminary publication. Verify the old snapshot remains visible, the working draft becomes stale and conflicts prevent acknowledgment/publication until resolved. Republish preliminary to create version 2.
8. Leave a report unresolved. Final publication must require explicit acknowledgment or resolution. Confirm final publication creates the next immutable version; verify the employee's final label and absence of new-report actions.
9. Change a user's display name, row name or staffing afterward. Administrator history must retain the original snapshot details and comparisons. Verify no edit/correction transition is available after final publication.
10. Run `RUN_POSTGRES_TESTS=1 dotnet test -m:1`, `dotnet build -c Release --warnaserror -m:1`, `dotnet ef migrations has-pending-model-changes --project src/ShiftManager`, `npm test --prefix frontend` and `npm run build --prefix frontend`. Integration tests use disposable PostgreSQL databases, including legacy migration and rollback checks.
