# Phase 6.1: operational corrections

## Deployment and database safety

The additive `20261104135940_OperationalControls` migration creates four empty tables:
`PublicationControl`, `ShiftExchange`, `ShiftExchangeAssignment` and
`ShiftExchangeReservation`, with foreign keys, a status check, a concurrency token and
unique source reservations. It does not update users, credentials, roles, teams,
shift definitions, drafts, absences or publication JSON. The existing publication
and schedule-audit immutability triggers remain intact. No migration runs at web-host
startup. Apply this migration explicitly to an approved target before starting this
version of the backend. Deploy frontend/backend together.

Review the generated SQL before deployment, with the existing connection supplied
securely through the normal environment configuration:

```sh
dotnet ef migrations script 20261103081004_Phase6Absences 20261104135940_OperationalControls --project src/ShiftManager
```

This command generates SQL; it does not apply it. Applying migrations to the current
demo or changing its accounts requires a separately approved operation. Do not run
setup, seed, clean, reset or migration commands against that database as part of
validation. Keep backups and review the precise target connection before any later
approved deployment. A rollback drops the new tables and therefore loses their new
controls and exchange history; preserve/export that history and review visibility
before any approved rollback.

## Administrator and team leadership

Administrators retain global scheduling, account, team and absence administration.
They cannot be added to a team or appointed leader/deputy. Promoting a team member
to ADMIN requires removing the membership first through an explicit admin action.
Existing dual-role accounts are excluded from scheduling inputs and personal
availability; they cannot use delegated team availability, initiate exchanges, or
read employee publication APIs. Account responses never label an administrator as
a team leader. Existing affected records are preserved for explicit correction.
Publication validation reports any existing administrator assignments as ineligible.

Fresh isolated demo seeds now contain 61 accounts: `demo.admin` (ADMIN only, no team)
and the original 60 stable employee identities in four teams of 15. `demo.01` is
Team 1's dedicated employee-role leader; the other leaders and deputies keep their
identities. The 56 scheduling participants remain unchanged. Existing seeds are
never upgraded/reseeded automatically. The additive absence-scenario inspector
recognizes both the legacy 60-account population and this exact new population,
while continuing to reject unknown users. It never treats an ADMIN as eligible.

### Existing demo correction proposal — not executed

Read-only inspection of `shiftflow_demo_202611_42` on 2026-11-04 found:

- 60 users, 60 memberships, four teams, three publications, 1,397 assignments and
  29 absence requests.
- `demo.01` is the system administrator and the stored leader of Team 1 (`צוות א׳`).
  Its deputy is `demo.02`. Other leaders are `demo.16`, `demo.31` and `demo.46`.
- Stable shift definition `40000000-0000-0000-0000-000000000008` has the name
  `שיקום בוקר`, required headcount 1 and automatic scheduling enabled.

For this existing database, propose preserving `demo.01` as the administrator and
creating a distinct Team 1 leader account (for example `demo.leader.01`). This avoids
renaming an existing login or replacing any password. Before execution, prepare a
fresh read-only preview with exact IDs, team version, roles, assignment conflicts,
current account counts and the proposed new login. Obtain explicit user approval
of both the new account and the membership/leadership changes. A later operator can
then, in one reviewed transaction using the account lock and normal service checks:

1. Create the separately approved active EMPLOYEE account with a securely supplied
   temporary password; do not copy or reset an existing password.
2. Remove `demo.01`'s team membership, clearing only its old leadership pointer.
3. Add the new account to Team 1 and appoint it leader, preserving `demo.02` as deputy
   and checking the current team version.
4. Preserve the administrator's ADMIN role and all other accounts/memberships;
   optionally removing its legacy EMPLOYEE role must be separately included in the
   approved preview, not inferred.
5. Verify four teams, exactly one dedicated non-admin leader per team, unchanged
   passwords for all existing accounts, no administrator assignments, unchanged
   absences/drafts/publication JSON and successful scoped access after the leader's
   required first password change. Abort the transaction if invariants differ.

The scenario ownership/fingerprint must not be reset to adopt these edits. The old
seed cleanup/scenario population guards will conservatively refuse a custom new
account; use a fresh isolated database for future automatic demo scenarios. Never
bypass those guards or delete this manually maintained demo to accommodate a seed.

## Publication controls

`POST /api/admin/scheduling/periods/{year}/{month}/publications/{id}/control`
accepts `expectedVersion` (0 before the first control), `operation`
(`HIDE`, `RESTORE`, `DELETE`) and a required nonblank reason. Both the controller
policy and the service verify current administrator status and security stamp.
The Hebrew confirmation dialog identifies the month, version, publication type,
time and UUID. Deletion is an irreversible soft deletion: the publication and all
references remain; RESTORE only undoes hiding and cannot undo deletion.

Each action takes the existing global scheduling, neighboring period, account and
employee locks, increments the control version and period revision, and writes an
immutable `ScheduleEditAudit` with actor, UTC time, publication ID, before/after state
and reason. Stale controls return 409. Snapshot bytes and publication metadata are
never updated. The latest version remains authoritative even when hidden/deleted:
**there is no automatic fallback to an older publication.** The working period's
publication/correction lifecycle is retained so the administrator can correct it
and explicitly publish a new version with a monotonically increasing number.

Employee period lists omit a month whose latest publication is unavailable. Direct
reads, explicit historical versions, the visible-version list, issues, exchange
assignment/candidate APIs and exchange history all enforce visibility server-side.
If the latest version is hidden/deleted, all employee history for that month is
unavailable. If an older version alone is withdrawn, the latest remains active and
that older version is denied. Administrator history includes controls and retains
all snapshots. Absence-conflict responses redact withdrawn publication details for
employees; scoped team approvers must refer hidden-publication conflicts to an
administrator. Administrators can still resolve those conflicts. Deleted latest
publications do not create new absence publication conflicts or reactivate older
versions.

Employee views use no-store requests, clear rendered schedule details on focus,
visibility and connectivity changes, and revalidate every five seconds while
visible. Failed authorization/read checks clear the displayed snapshot. Sequence
guards prevent a late older response from restoring revoked data. Exchange views
use the same lifecycle revalidation and hide withdrawn assignment details. There
is no persistent browser storage for schedule data. Revocation of an already
rendered visible tab is detected on the next poll (up to five seconds plus network
latency); previously viewed/copied data cannot be remotely erased. All new server
requests enforce the current publication state immediately.

Hiding cancels pending requests tied to that publication (or the entire month if
it is the authoritative version). Approved draft corrections remain available to
administrators. Saving a draft never changes visibility. Explicit republication
creates a new visible publication; its dialog states this behavior. Restoring a
hidden older version only restores eligible history access, not active selection.

## Shift exchanges

Employee routes:

- `GET /api/shift-exchanges`: own sent/received request history.
- `GET /api/shift-exchanges/{year}/{month}/assignments`: eligible future own assignments.
- `GET /api/shift-exchanges/{year}/{month}/candidates?publicationId=...&assignmentId=...`:
  currently eligible replacement employees.
- `POST /api/shift-exchanges/{year}/{month}`: publication ID, own assignment ID and
  replacement user ID.
- `POST /api/shift-exchanges/{id}/decision`: expected version, decision and optional reason.

Administrator routes use `/api/admin/shift-exchanges` for list/decision and
`/{id}/publication-review` / `/{id}/publish` for explicit reviewed republication.
All writes retain the existing cookie/CSRF policies. The RTL employee screen is
`/shift-exchanges`; the admin screen is `/exchange-management`.

State transitions are AWAITING_REPLACEMENT -> AWAITING_ADMIN ->
APPROVED_PENDING_PUBLICATION -> APPLIED. Only the proposed replacement may ACCEPT;
only an administrator may APPROVE after consent. Replacement/admin rejection and
requester cancellation retain history. New publications supersede waiting requests.
An approved correction changed again before publication is marked SUPERSEDED.
Every transition writes an immutable audit with request ID, actor and UTC time.
Employees cannot write any draft assignment directly or approve their own requests.

Creation, consent and approval revalidate current eligibility and source/draft
identity, visible authoritative versions, future start time, qualification (including
transfer of shift-lead responsibility), approved absences, availability hard blocks,
capacity, overlap/consecutive work, weekend/month allocations, weekly night and
SIX_NINE limits and paired on-call membership. Existing shared candidate, input,
manual-edit and publication validators enforce these rules. Assignments with linked
tasks, changed live source identities, or legacy snapshot-only rows without a
supported editable cell are ineligible. Availability/absence changes after consent
can invalidate approval; changes after approval can block publication. Names and
published assignment notes transfer through the normal correction workflow.

Unique reservation rows cover every source assignment, including both on-call days.
The existing cross-process scheduling lock serializes requests and approvals with
manual editing, absence approvals, publication and account/availability changes.
Replacement conflicts are checked again after acquiring those locks. Approval uses
the existing audited manual replacement in one transaction; employees continue to
see the original immutable publication until explicit republication. APPLIED means
all new assignment IDs were found in their replacement publication snapshots.

Cross-month on-call exchanges require both original months to have eligible visible
publications and reserve both assignments. A normal single-month publication is
blocked while such an approved correction is pending. The exchange publication
dialog reviews both complete drafts, requires explicit shortage/open-issue consent
and any needed input-review acknowledgment, then publishes both in one transaction.
Every initial token is checked before the first write changes a neighboring period's
fingerprint. Failure in either month rolls back both publications, conflict
resolutions and request status. This prevents half an exchange becoming visible.

## Rehabilitation morning / Friday

The rule targets the stable UUID ending `000000000008`, not display order or mutable
name. It uses configured headcount on Fridays and zero demand / no assignment on
Saturdays. Shared applicability and demand functions serve optimization, draft saves,
manual edits, board rendering and publication checks. Other constraints still apply;
Friday allocations count toward the employee's monthly weekend limit. No existing
shift definition or publication snapshot is rewritten. Review a new/working draft
before republishing; historical snapshots keep their original Friday/Saturday data.

## Validation

Use only fresh per-test PostgreSQL databases managed by `TestDatabase`, with
`RUN_POSTGRES_TESTS=1`. An optional loopback `shiftflow_ci` test server is supported;
never supply the existing demo/development connection. Regression coverage includes
role separation and legacy records, scoped Team 1 approvals, no-fallback withdrawal,
direct/history/absence/exchange denial, audit preservation, version concurrency,
consent and approval authorization, hard-constraint revalidation with rollback,
source reservations, immutable snapshots and cross-month atomic republication
(including a forced failure of the second publication). Friday/Saturday solver,
headcount, stable identity and month/year boundaries have dedicated tests. Existing
Phase 6 absence and publication suites remain part of the full test run.

Validated locally on the feature branch: the complete Release backend/PostgreSQL
suite passed 307 tests with no failures or skips; the Hebrew frontend passed 125
component tests and its production build. The Release build with warnings as errors
had zero warnings/errors; EF reported no pending model changes, and the generated
forward SQL contained only the four new tables/indexes/constraints and its migration
history entry. The development-script suite passed seven tests. Test writes used a
new disposable PostgreSQL server on port 55433 with per-test databases; the existing
demo on port 55432 and development service on port 5432 were not used for test writes.
The demo's final read-only counts matched inspection, its existing leadership stayed
unchanged, and the new control table was absent (no migration applied there).
