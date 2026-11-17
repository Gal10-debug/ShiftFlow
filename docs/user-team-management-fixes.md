# User and team management fixes

## Investigation and root cause

User creation already persists the existing `ApplicationUser` and Identity role rows atomically. `GET /api/users` returns the shared directory with stable pagination (maximum 100 users per request); Teams already follows every directory page. `TeamMembership` is keyed by user ID and references the existing four organizational teams. There is no separate employee table.

The reported symptom was existing users missing from selection. The concrete frontend defects found are:

- Teams takes its directory snapshot only on mount/audit-page changes. A user created in another tab remains missing when returning to the open Teams page.
- Each team editor provides leadership dropdowns restricted to current members, with no way to select an unassigned directory user there. Assignment previously required finding the separate transfer form below all four editors.

The fix refreshes the directory on window focus/visible-page changes, discards obsolete directory responses, and provides an existing-user assignment selector in each team editor. The existing transfer/removal form remains available. Both controls use the existing membership endpoint and reload teams after success, immediately updating membership and leadership choices. ADMIN-role accounts are excluded from assignment choices, consistent with existing server validation; historical admin memberships can still be removed.

The existing PostgreSQL assignment/transfer test passed before implementation. New tests also create users through the real user-management API and assign them before their first login, confirming that the password-change gate does not exclude target users. No persistence or authorization defect was reproduced. The exact state of the user's running instance was not inspected; the stale-directory regression is reproduced in the frontend tests.

## Employee classifications and scheduling

The inspected base had no employee-type property. It had system roles and an independent `IsShiftLeadQualified` flag. An additive `EmployeeType` string-converted enum on the existing user follows the repository's domain enum/EF configuration patterns:

| API/database value | UI label | Automatic scheduling |
| --- | --- | --- |
| REGULAR | עובד רגיל | Existing eligibility rules |
| DISPATCHER | מוקדן/מוקדנית | Manual only |
| MANAGER | מנהל | Manual only |
| RESERVIST | מילואימניק | Manual only |
| DUTY_MANAGER | ת.מנהל | Manual only |

Creation defaults to REGULAR when omitted. Profile edits that omit the property retain its saved value. User responses include the classification; create/edit forms and the user directory expose it separately from roles. Classification changes use the existing account lock, session revocation and preview/review invalidation patterns. No classification adds an Identity role or bypasses existing active-account, EMPLOYEE-role, qualification, team-leader, availability or manual-assignment rules.

`SchedulingInputs.Read` filters these four types out of `Employees` before the solver runs. Review/manual operations retain the full workforce using their existing `review: true` input path. The CP-SAT model, constraint creation, objectives, demand and staffing calculations are unchanged. Fixed assignment validation and output name lookup now also consult separate `ManualEmployees` metadata, necessary to retain manual work without including its employees in the automatic candidate array. Generated-preview validation allows that metadata only for protected work, never for new automatic assignments. Reclassifying a previously automatically scheduled employee protects the retained assignments instead of silently replacing them.

## Existing system roles

All protected routes also require an active account, a valid session and completion of the initial password change. `/api/auth/me`, logout and password change have the existing SignedIn exception. Role combinations are additive; team leadership is a separate database appointment.

| Role | API access | Frontend features |
| --- | --- | --- |
| ADMIN / מנהל מערכת | `/api/users/**`, `/api/admin/teams/**`, `/api/admin/scheduling/**` including review, manual editing, generation and publication; `/api/admin/availability/**`, `/api/admin/statistics/**`, `/api/admin/shift-exchanges/**`; global absence management via `/api/absences/manage`, conflicts and decisions, subject to existing self-decision/resource checks; `/api/access/admin` | Users, teams, scheduling board/settings/generation/publication, global availability inspection, administrative statistics, absence and exchange management, profile |
| COORDINATOR / רכז | `/api/access/coordinator` plus shared authenticated session endpoints. No scheduling/user/team administration permissions from this role alone. Team-scoped `/api/team-availability/**` and absence management require explicit leader/deputy appointment, not COORDINATOR. | `/coordinator` placeholder and profile. Team availability/absence features appear only with explicit leadership. Employee features additionally require EMPLOYEE. |
| EMPLOYEE / עובד | `/api/availability/**` read-only under Phase 9A; `/api/my-schedule/**`, `/api/my-statistics/**`, `/api/shift-exchanges/**`, personal absence operations. Existing resource checks exclude administrators/team leaders from scheduling participation where applicable. | Personal availability, schedule, statistics, exchange requests and absences, subject to existing role/capability guards. |

Frontend/backend role values are consistently ADMIN, COORDINATOR and EMPLOYEE. No inconsistent role mapping was found. ADMIN corresponds to administrator. COORDINATOR is named as a coordinator but is currently only a placeholder, not an operational shift scheduler; scheduling management remains ADMIN-only. This is a product-capability gap to clarify, not authorization changed by this work. Neither the MANAGER employee type nor team leadership grants ADMIN.

## Database migration

`20261117124810_EmployeeTypes` adds a non-null `AspNetUsers.EmployeeType` varchar(20), default REGULAR, and an allowed-values check. Existing users receive REGULAR; user IDs, roles, team memberships, shifts and publications remain intact. Down drops only the new column/check (classification data is lost on downgrade). No historical migrations or compatibility SQL were changed. The frozen legacy-upgrade test now applies subsequent normal EF migrations before exercising the current application.

Apply the migration with the repository's normal explicit migration workflow before running the upgraded application. Implementation/testing does not migrate the user's persistent local or remote database.

## Validation

New regression coverage includes all four types through API creation and PostgreSQL persistence, old-user/default migration preservation, manual candidate selection and assignment, automatic candidate exclusion, fixed-work regeneration/save, reclassification of previously generated work, stale preview rejection, invalid type rejection, role independence, directory pagination/focus refresh, team assignment before first login, leadership preservation, immediate UI membership updates and failed-assignment retry. Existing scheduling, authorization and migration suites remain part of validation.

Validation completed successfully:

| Check | Result |
| --- | --- |
| Full backend suite, `RUN_POSTGRES_TESTS=1 dotnet test -m:1`, using disposable service connection | 502 passed, 0 failed, 0 skipped; 6m 40s |
| `npm test --prefix frontend` | 163 passed across 11 files |
| `npm run build --prefix frontend` | Passed |
| `dotnet build -c Release --warnaserror` | Passed; 0 warnings, 0 errors |
| `dotnet ef migrations has-pending-model-changes --project src/ShiftManager` | No pending model changes |
| `git diff --check` | Passed |

Frontend tests retain pre-existing React `act(...)` warnings in publication/session-expiry scenarios; they pass. The first container-per-test full-suite attempt encountered an existing fixture assumption that different databases have different names (both independent containers name their databases `postgres`); validation uses the supported disposable service mode with uniquely named per-test databases instead.

## Files changed

- `docs/user-team-management-fixes.md`
- `frontend/src/App.test.tsx`
- `frontend/src/Teams.test.tsx`
- `frontend/src/api.ts`
- `frontend/src/pages/Teams.tsx`
- `frontend/src/pages/Users.tsx`
- `src/ShiftManager/Api/Contracts.cs`
- `src/ShiftManager/Data/Configurations/ApplicationUserConfiguration.cs`
- `src/ShiftManager/Data/Migrations/20261117124810_EmployeeTypes.Designer.cs`
- `src/ShiftManager/Data/Migrations/20261117124810_EmployeeTypes.cs`
- `src/ShiftManager/Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `src/ShiftManager/Domain/ApplicationUser.cs`
- `src/ShiftManager/Domain/Enums.cs`
- `src/ShiftManager/Services/AccountService.cs`
- `src/ShiftManager/Services/AutomaticScheduling/GeneratedDraftService.cs`
- `src/ShiftManager/Services/AutomaticScheduling/ScheduleOptimizer.cs`
- `src/ShiftManager/Services/AutomaticScheduling/SchedulingProblem.cs`
- `src/ShiftManager/Services/ScheduleReviewService.cs`
- `tests/ShiftManager.Tests/EmployeeTypeMigrationTests.cs`
- `tests/ShiftManager.Tests/LegacyShiftMigrationIntegrationTests.cs`
- `tests/ShiftManager.Tests/UserTeamManagementTests.cs`
