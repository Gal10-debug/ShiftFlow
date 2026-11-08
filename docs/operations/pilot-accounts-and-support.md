# Pilot accounts, onboarding and support

All operator documentation is English; the application remains Hebrew/RTL. Use only the existing administrative screens and authorization rules. No public self-registration, email reset service or new onboarding business rule is added.

## Before inviting employees

1. Complete the deployment gates and provision the initial admin via the target-confirmed bootstrap operation. No default username/password is supplied. Transfer the temporary credential through an approved private channel, separately from the service URL. The admin must change it at first login; operational pages remain blocked until then.
2. Establish a second authorized administrator for recovery; separately distribute its temporary credential and require its first-login change. Restrict administrative host/Docker access to named operators. Identity lockout and rate limiting remain enabled; repeated login retries are not a recovery strategy.
3. The existing migrations seed four organizational team slots. Configure their approved names instead of creating duplicate teams. Create employee accounts with synthetic identities during rehearsal and approved real identities only after live-data authorization. Assign membership, then an active employee as Team Lead and another eligible employee as Deputy. Verify the team version/leadership rules. Leadership is a team assignment, not an administrator role.
4. Create Workers and Shift Coordinators with only their intended roles. Set shift-lead qualification from an approved qualification list. Review that Team Lead, Deputy, Worker and Coordinator cannot open administrator account/scheduling functions; the Production simulation verifies these denials. Do not grant ADMIN to bypass a missing permission.
5. Review seeded shift definitions/staffing, dates and lead requirements using existing scheduling settings. Protected staffing rows/notes and qualification constraints remain enforced. Approve any staffing changes through the normal business process; this phase does not invent new constraints.
6. Choose a scheduling month, open its availability window, explain submission deadlines and delegated editing, and verify employee submissions. Close the window before generating. Review shortages, hard conflicts and issues before publication. A shortage is a staffing fact, not by itself a solver exception; never claim the schedule fills unavailable staff.
7. Provide Hebrew instructions for login, mandatory password change, availability, published schedule viewing, issue reporting, exchanges and absence requests. Use the existing Hebrew UI terminology, an approved support contact and supported browser. Explain that absence approval must be independent: requesting Team Leads/Deputies cannot self-approve through a different screen.

## Account maintenance

An authorized admin can issue a temporary password through the existing reset action (`POST /api/users/<id>/reset-password`, with CSRF and `{ "temporaryPassword": "<private-value>" }`). Never put a real password in a shell command, ticket or shared screenshot. Reset forces password change, clears lockout and invalidates prior sessions. Users change their own current password through the account flow; self-reset through the administrator reset endpoint is forbidden.

Disable an employee with the existing account status action (`PATCH /api/users/<id>/status`, `{ "isActive": false }`). Role, status, profile and password changes invalidate old cookies. Verify any operational roster implications before changes. The last active administrator cannot be disabled or stripped of ADMIN. Do not delete users or historical publications to remove access.

## Administrator recovery

Prefer the second authorized administrator: independently verify the requester's identity and reset the affected admin account using the normal action. If all admin accounts are inaccessible, a named host operator with explicit incident authorization may provision a **new**, unique emergency administrator through the existing target-confirmed bootstrap command using private updated bootstrap fields/secrets. Bootstrap is idempotent and never resets the old account. Do not modify password hashes/role tables manually or disable last-admin protections. Restrict ingress, record the emergency access, force the new account's password change, restore normal authorized access, then disable the emergency account once another active administrator exists.

## Support and pilot feedback

Ask for approximate time, operation, visible Hebrew error code and `X-Request-ID`; never ask for a password, auth cookie or full database export. Distinguish a shortage, stale schedule token, open issue or independent-approval denial from an application failure. Use the review/correction/republication workflow so historical published snapshots remain immutable. Escalate suspected credential exposure or persistent 503/500 failures via [incident response](pilot-incidents.md). Record pilot feedback without including employee availability/medical details in general technical logs.
