# Phase 4C — Organizational teams and delegated availability

## Persistent model

`OrganizationalTeam` contains four seeded, stable UUIDs, slots 1–4, editable Hebrew names, nullable leader/deputy user IDs and an optimistic version. Initialization occurs through EF migration seed data, once per database; restarting or applying migrations again never resets names or appointments. There are no create/delete team endpoints. A unique slot and range constraint cap the table at four rows. Empty leadership slots are permitted until administrators make appointments.

`TeamMembership` has `UserId` as its primary key: a user belongs to at most one team. Both leadership references use composite foreign keys to `(TeamId, UserId)` on membership. PostgreSQL therefore enforces membership in the correct team and a check constraint enforces distinct leader/deputy users. All foreign keys restrict deletion. Transfers clear any previous leadership appointment before replacing membership, inside one transaction. Team versions change on membership updates too. A deputy must have EMPLOYEE role; removing that role requires ending their deputy appointment first. Leadership grants no Identity roles.

`DelegatedAvailabilityAudit` stores actor, target, team, period, UTC timestamp, initial/update indicator and previous/new selection JSON. Date, shift type and state are retained; omitted selections mean AVAILABLE. It contains no request bodies, credentials or personal profile copies. Existing Identity users, availability submissions and shift preferences remain authoritative; no parallel availability store exists.

## Authorization and concurrency

`TeamService` centrally checks active user state, password-change gate and current security stamp. A delegated target must be an active EMPLOYEE in the acting user's explicitly led/deputized team, and cannot be an organizational leader. Administrator role alone does not confer cross-team delegated write access. Administrators retain the existing read-only global availability inspection workflow. Team membership listings disclose only the authorized team. Read operations use repeatable-read snapshots so authorization and returned resources agree.

Writes serialize in the existing order: month creation/period lock, global account lock, employee advisory lock. Team management and role changes share the account lock, preventing membership or leadership revocation from racing delegated writes. The employee lock covers adjacent months. Full-month replacement checks the supplied expected version, previous submission existence and all Sunday–Saturday boundary-week hard blocks. At most three hard blocks are allowed per week. Delegation never bypasses that rule. Serialization/deadlock retries remain bounded.

Audit insertion, preference replacement and submission version/timestamp updates commit atomically. A database error after audit insertion rolls back all three. Delegated writes remain explicit POST/PUT operations; changing a browser selection writes nothing. The solver consumes the last committed submission. Ordinary self-submission still requires an open window. Delegated submission can occur with a closed/expired window while the scheduling period remains DRAFT.

## Scheduling eligibility and conflicts

Organizational leaders are excluded from `SchedulingInputs` workforce candidates and from `SchedulingProblem.Eligible`, irrespective of EMPLOYEE role or expert qualification. Protected-work validation also rejects leadership eligibility. Deputies retain the ordinary employee solver path: blocks, nonoverlap, no consecutive shifts, fairness and explicit shift-lead qualification all apply. Deputy appointment grants no expert qualification. Leaders are excluded from required-submission counts and personal submission is rejected server-side.

Appointments never delete existing assignments. The administrator update response includes detected roster conflicts; the teams page also reloads and displays these conflicts. Generation fails with `TEAM_LEADER_ASSIGNED` when existing leader assignments are present in its relevant input range. Submitted preferences, submission versions, and team leadership/version state participate in the generation fingerprint, so saving an obsolete preview is rejected.

A delegated hard block may conflict with an existing draft assignment. The assignment is retained and `RosterValidationService.Conflicts` identifies the overlap using actual scheduling-cell times, including adjacent date/month anchors. The PostgreSQL preference trigger permits this only with a transaction-local audit ID referring to an audit event for that employee and draft period. Unattributed direct preference changes and ordinary self-submission retain the existing rejection behavior. Assignment insertion/update and cell-time changes still cannot bypass existing hard blocks.

`RosterValidationService.ValidateForPublication(periodId)` is the reusable guard for a future publication workflow. It rejects leader assignments and hard-block conflicts. A future publisher must call it within the same scheduling/period/account/employee lock protocol and transaction as publication. No publication workflow is implemented in Phase 4C.

Regenerating and explicitly saving a draft can resolve conflicts in replaceable automatic assignments. Protected/manual/locked assignments require administrator resolution before generation can proceed. Existing UI does not provide individual assignment editing; this remains an explicit limitation, and such assignments are never silently removed.

## API

All endpoints require the normal authenticated/password-change policy and all mutations require existing CSRF validation.

| Method | Path | Authorization / behavior |
| --- | --- | --- |
| GET | `/api/admin/teams` | ADMIN; four teams, leadership, membership, versions |
| PUT | `/api/admin/teams/{teamId}` | ADMIN; name, leader, deputy, expectedVersion; returns teams and conflicts |
| PUT | `/api/admin/teams/members/{userId}` | ADMIN; `{teamId}` transfers/adds, null removes |
| GET | `/api/admin/teams/conflicts` | ADMIN; retained assignment conflicts |
| GET | `/api/admin/teams/audit?page=1` | ADMIN; 50 events per page, newest first |
| GET | `/api/team-availability/members` | Explicit leader/deputy; own team only |
| GET | `/api/team-availability/employees/{userId}/periods` | Resource-scoped target authorization |
| GET | `/api/team-availability/employees/{userId}/{year}/{month}` | Resource-scoped submitted values, version, weekly counts |
| POST | same employee/month path | Resource-scoped initial submission, expectedVersion 0 |
| PUT | same employee/month path | Resource-scoped full replacement, expectedVersion required |

Other scheduling, staffing, user and team administration endpoints keep their ADMIN policy. Client navigation capability flags are derived from leadership records, not usernames or role inference. They are presentation hints; every request reauthorizes on the server. Re-login or refreshing the app updates navigation after an appointment/revocation.

## Hebrew RTL UI

`/teams` (ניהול צוותים) provides four editors, leadership selection from team members, employee transfers/removal, retained roster conflicts and paginated audit history. `/team-availability` (זמינות הצוות) lists only the authorized team, excludes leaders/nonemployees from target selection and reuses the existing calendar, weekly counters, optimistic submission version and explicit save behavior. Deputies retain normal `/availability` and `/employee` pages. All messages and controls are Hebrew with RTL sections.

## Verification

1. Run `./scripts/dev.sh migrate` explicitly against your development database; preserve the existing volume. Start the app and sign in as administrator.
2. Open ניהול צוותים. Verify four initial names, rename one, refresh and confirm persistence.
3. Assign employee memberships. Appoint different team members as leader/deputy (deputy requires EMPLOYEE). Try a nonmember or identical leader/deputy through the API; verify rejection. Transfer an appointed deputy and confirm the old appointment is cleared.
4. Refresh/sign in as leader/deputy. Open זמינות הצוות, verify only own team is visible and the leader is not a submission target. Try another team's target UUID via GET/POST/PUT; expect 403.
5. Close the availability window as administrator. A regular employee's self-edit must fail, while the team deputy can explicitly submit AVAILABLE/PREFERRED/BLOCKED choices for their member. Verify an unsaved selection does not change server values.
6. Attempt a fourth hard block in one Sunday–Saturday week, including a week spanning two months. Verify rejection and preservation of the previous submission.
7. Open the same submitted employee/month in two browser sessions; submit both versions. Only the first update succeeds; the other reports a conflict.
8. Generate a preview, submit a delegated update, then save that preview. Verify rejection. Generate and save a new draft, then block an assigned shift through delegation. Verify the assignment remains and the administrator sees a conflict and audit event.
9. Appoint a scheduled employee as team leader. Verify the update reports retained assignments and subsequent generation fails until they are resolved. Verify a deputy remains a candidate and does not qualify for expert rows unless independently qualified.
10. Inspect audit events as administrator; confirm actor/target/team/period, UTC event time (displayed in Israel local time), previous/new values and initial/update indicators. Ordinary team members must not access the administrator audit endpoint.

Automated verification: `RUN_POSTGRES_TESTS=1 dotnet test -m:1` uses disposable PostgreSQL containers unless CI's isolated service connection is provided. `dotnet build -c Release --warnaserror`, `dotnet ef migrations has-pending-model-changes --project src/ShiftManager`, `npm test --prefix frontend`, and `npm run build --prefix frontend` mirror relevant CI checks. Existing migration rollback/reapply and legacy-data upgrade tests include Phase 4C. Production deployment, publication, exchanges and error reporting remain outside scope.

## Phase 5 extension

Publication and manual corrections are now implemented as described in [publication architecture](publication-architecture.md). The earlier future-publication/individual-editor limitations above describe Phase 4C's baseline and are superseded: administrators can explicitly correct assignments, and full publication validation extends the original leader/hard-block guard. Team leadership still grants no scheduler privileges. Audited delegated edits continue during preliminary/correction states, but never mutate the published snapshot or finalized periods.
