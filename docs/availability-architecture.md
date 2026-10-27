# Availability and preferences (Phase 3)

## Submission contract

Only active users with EMPLOYEE may submit their own availability. ADMIN manages windows
and reads submissions, without an employee override endpoint. A user with multiple roles
may submit only their own employee availability. COORDINATOR adds no availability privileges.
All routes retain the normal password-change gate, cookie/session validation and CSRF on writes.

A local calendar edit is a draft in browser memory. POST creates the first official
submission; PUT replaces the complete monthly selection. Missing entries and AVAILABLE
mean neutral; only PREFERRED and BLOCKED rows are stored. An empty selection is a valid
all-Available submission. A durable AvailabilitySubmission row, never preference-row count,
defines submitted status. LastSubmittedAt is a server UTC instant, and Version starts at 1
and increments on each successful write. POST requires expectedVersion=0; PUT requires the
version returned by GET. Stale or duplicate requests return 409 rather than losing another
tab's submitted version. No partial preference changes or timestamps survive a failed write.

The UI displays submitted status independently from unsaved changes. It warns on month
changes, link navigation and page unload; browser history navigation is not intercepted.
Refresh reloads only the last official submission. No draft is stored in localStorage.
The employee page polls every 15 seconds for administrative status and neighboring-month
counts while preserving local edits. A local clock disables submission at the configured
deadline. Server checks are authoritative even between polls; a closed-window response
also disables the local editor immediately. Manager counts refresh on page/month load,
management operations or the explicit refresh action.

## Window lifecycle

Configuration is an idempotent PUT/upsert by calendar year/month. A month-creation advisory
lock and period row lock prevent duplicate periods/windows. Configure ensures both empty
canonical shift slots for each date; this creates no assignments or scheduling decisions.
Existing noncanonical boundaries cause 409 and require reviewed repair rather than silently
changing historical shifts. Published periods reject all availability mutations.

New windows are administratively CLOSED. Saving dates never reopens a closed window.
Both opening and closing instants are required for newly configured windows and closing
must be strictly later than opening. Existing nullable legacy closing instants remain
readable; an administrator must configure a finite closing time before reopening them.

Effective status uses a single UTC clock and precedence:

1. No window: NOT_CONFIGURED.
2. Administrative CLOSED: CLOSED, regardless of dates.
3. Administrative OPEN and now < OpensAt: SCHEDULED.
4. Administrative OPEN and now >= ClosesAt: EXPIRED.
5. Otherwise OPEN; only this state accepts submissions.

Opening before OpensAt arms the window for automatic opening. Opening an expired window
is rejected until its dates are updated. Manual closure preserves configured dates and
all submissions and prevents further writes. Reopening is explicit, requires a future
closing time, preserves prior submissions/versions, and allows employee updates again.
OpensAt is inclusive and ClosesAt exclusive. OpenedByUserId records the last opening actor
(or the initial configuring actor); ClosedByUserId is cleared on opening and set on manual
closure. These fields are not a complete lifecycle audit log.

## Weekly rules and transactions

Shift ownership uses its starting local date, including month-end nights. DAY is 07:00–19:00
and NIGHT 19:00–07:00 next day in Asia/Jerusalem. ShiftClock converts both boundaries
separately to UTC: DST nights can be 11 or 13 elapsed hours. Sunday week start is the
local date minus its DayOfWeek integer; UTC midnight or elapsed-hour arithmetic is never
used to determine business weeks.

For each replacement, count the proposed BLOCKED rows plus submitted BLOCKED rows from
other months in every intersecting Sunday–Saturday week. Exclude the replaced month's
old rows entirely. Three is allowed; four rejects the complete transaction with
WEEKLY_BLOCK_LIMIT, weekStartsOn and limit=3. This also works across year boundaries.

The global write lock order is:

1. Transaction-scoped month-creation advisory lock `(71003, year * 12 + month)`.
2. SchedulePeriod row FOR UPDATE.
3. Employee transaction advisory lock `hashtextextended('a' + userId.ToString("N"), 0)`.

All window and submission mutations follow steps 1–2; submissions then take step 3.
This adapts Phase 1's allowed cross-process locking strategy to a coarser whole-employee
lock instead of individual weekly locks. It serializes adjacent-month replacements for
the same employee without discovering or ordering a changing set of week locks. Reads
of another period's blocks do not acquire its row lock. Ordinary READ COMMITTED is safe
for these writes because all relevant employee writers serialize before reading the count.
A window close waits for an in-flight submission that owns the period lock; once close
commits, no later submission can succeed. The clock is checked again before saving.
Aborted writes with PostgreSQL 40001/40P01 retry at most twice with bounded backoff;
validation and version conflicts are never retried. Read DTOs use REPEATABLE READ so versions, timestamps and preferences form one snapshot.

Phase 4 writers must use the same order, acquire employee locks in deterministic GUID
order for bulk work, and revalidate constraints inside the transaction. Never acquire
period locks after employee locks. Do not depend on an earlier HTTP read for scheduling
correctness. Database callers outside these services are trusted infrastructure and must
follow this protocol; weekly counting is a service transaction rule, not a SQL CHECK.

## Hard blocks and Phase 4 integration

IHardBlockQuery exposes IsBlocked(userId, shiftId) and BlockedShifts(userId, from, through).
Use it under the shared locks for every scheduling decision, including administrator
operations. It deliberately includes all BLOCKED rows, even legacy records, as a defensive
hard-constraint query. Preferred remains a soft preference only.

PostgreSQL triggers provide a final assignment backstop: inserts and changes of assignment
employee/shift reject an existing hard block (CK_HardBlock, SQLSTATE 23514); blocked
preference insertion/changes reject an existing assignment. Both use the same employee
advisory lock. The application returns SHIFT_ALREADY_ASSIGNED when an employee attempts
to block an already assigned shift. There is no manager bypass API. These triggers do
not implement a scheduler, staffing counts, consecutive-shift rules or publication.

## Schema and upgrade assumptions

The additive AvailabilitySubmissions migration preserves all prior migrations and tables:

- AvailabilitySubmission: GUID key, user and period RESTRICT foreign keys, LastSubmittedAt
  (timestamptz), positive Version (bigint and EF concurrency token).
- Unique (UserId, SchedulePeriodId), plus the period foreign-key index.
- Hard-block integrity function and two triggers; Down removes these and the new table.

Phase 2 exposed no preference-submission API. If manually entered legacy preferences
exist, the migration backfills one submission per user/period, at the maximum UpdatedAt,
version 1. It preserves every preference and account. No historical all-Available
submission can be inferred when there are no preference rows. Backfill does not invent
such submissions or silently delete/normalize old data. Preexisting invalid weekly totals
or assignments conflicting with blocks require operator review before scheduling; the
migration does not erase history. New operations cannot introduce assignment/block conflicts.
Tests upgrade a Phase 2 database containing users and preferences and also migrate from zero.

## API

| Method | Route | Access / behavior |
| --- | --- | --- |
| GET | /api/availability/periods | EMPLOYEE; periods, own submission/window status |
| GET | /api/availability/{year}/{month} | EMPLOYEE; own submitted version, sparse preferences and neighboring-week counts |
| POST | /api/availability/{year}/{month} | EMPLOYEE; first submission, 201 |
| PUT | /api/availability/{year}/{month} | EMPLOYEE; atomic replacement, 200 |
| GET | /api/admin/availability/{year}/{month} | ADMIN; window and active EMPLOYEE submission roster/counts; unconfigured month is readable |
| PUT | /api/admin/availability/{year}/{month} | ADMIN; configure/upsert window, 200 |
| PUT | /api/admin/availability/{year}/{month}/state | ADMIN; explicit open/close, 200 |
| GET | /api/admin/availability/{year}/{month}/employees/{userId} | ADMIN; read-only submitted availability, including retained inactive-user history |

Submission body (omitted shifts are Available):

```json
{
  "expectedVersion": 0,
  "selections": [
    { "date": "2026-10-25", "shiftType": "DAY", "state": "BLOCKED" },
    { "date": "2026-10-26", "shiftType": "NIGHT", "state": "PREFERRED" }
  ]
}
```

Window configuration accepts opensAt/closesAt as ISO 8601 instants with explicit UTC/offset;
state accepts `{ "isOpen": true }` or false. The Hebrew UI converts datetime-local input
using Asia/Jerusalem independently of the browser timezone. It rejects nonexistent and
ambiguous DST wall times and asks for a different unambiguous time. Direct API callers
can specify an exact offset to identify either instant.

Validation rejects invalid months, out-of-month dates, unknown states/types, duplicate
shift entries and more than 62 selections. The availability API accepts years 2–9998 to
leave space for boundary weeks and next-day night endings. Errors use existing Hebrew
ApiError messages: 400 invalid input/weekly limit, 401 unauthenticated, 403 role/password
gate, 404 absent period/user, 409 closed window, published period or version conflict.
Unknown JSON properties are rejected; employee identity is always obtained from the cookie.
Eligible roster members are currently active EMPLOYEE role holders (including those yet
to change an initial password). Role removal/deactivation changes counts without deleting
historical submissions. There is no employee-on-behalf-of API.

## Validation and manual testing

Use the README CI reproduction commands with an isolated PostgreSQL 17 service. No CI
workflow changes or secrets are needed. All tests run through the existing jobs. New
PostgreSQL tests cover atomic all-Available submission, replacement, cross-month limits,
concurrent writes, versions, window time boundaries/reopening, roles/CSRF/inactive sessions,
canonical DST shifts, hard-block query/trigger integrity, and upgrade with existing data.
Frontend tests cover Hebrew/RTL, draft versus submitted status, first/update buttons,
weekly feedback, closed-state controls, administrator status, errors and DST input.

For manual testing on a development database:

1. Apply migrations with `dotnet ef database update --project src/ShiftManager` using the
   existing secret-supplied connection. Start backend/frontend as described in README.
2. Sign in as ADMIN; open **ניהול זמינות**, select a month, save valid Israel opening/closing
   times, then explicitly open the window. Confirm employee count and missing submissions.
3. Sign in as an EMPLOYEE in a separate session. Open **הזמינות שלי**, edit day/night choices;
   verify no submission appears for the manager until **הגש זמינות** is clicked.
4. Submit all-Available once, then edit and **עדכן זמינות**. Refresh both sessions and verify
   the last official selection/timestamp, including removal of previous blocks.
5. Try four blocks in one Sunday–Saturday week, including a week shared with an adjacent
   configured month; verify feedback and atomic server rejection.
6. Close the window as ADMIN; refresh/wait for the employee poll. Confirm read-only retained
   submission and that a direct update is rejected. Reconfigure/reopen and update again.
7. Check a narrow/mobile viewport, keyboard navigation, visible focus, Hebrew labels, and
   a month containing Israel's DST transition. Review other employee availability as ADMIN.

There is no automatic scheduling, roster publication, staffing, task allocation, production
deployment, draft autosave or full submission-history log in this phase. Only the latest
successful submission is retained. Browser-level visual QA depends on an available browser;
the automated component tests are not a substitute for final device/browser review.

## Phase 4C delegation

Explicit organizational leaders and deputies can submit for active EMPLOYEE members of their own team, outside the normal availability window, using the same versioned monthly replacement and weekly block validation. Membership and permission checks share the account lock with team administration. Audit insertion and availability updates are atomic. Draft conflicts are preserved and reported rather than deleting assignments; ordinary self-edits retain the existing assigned-shift rejection. See [team architecture](team-management-architecture.md) for endpoints, transaction-local audited trigger behavior and verification.

## Phase 5 publication interaction

Audited delegated edits are supported during preliminary publication and correction as well as DRAFT; they stale the reviewed input hash and can surface retained draft conflicts. Published snapshots never change. Final periods reject availability writes. Ordinary personal submission remains restricted to DRAFT and an open availability window. See [publication lifecycle and locking](publication-architecture.md).
