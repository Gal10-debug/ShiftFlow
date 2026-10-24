# Authentication and authorization architecture

## Identity, cookies and same-origin hosting

The existing GUID ApplicationUser/Identity tables and three role seeds remain authoritative.
There is no duplicate user model or JWT implementation. Identity UserManager/SignInManager
perform password hashing, verification, policy validation, lockout and reset-token generation.
The only added column is MustChangePassword (non-null boolean, default false for existing rows).
New managed accounts and the bootstrap administrator explicitly set it true.

The Identity application scheme issues an encrypted, signed HttpOnly session cookie. The
ticket expires after eight hours without sliding renewal. Development permits HTTP on
loopback; production uses a `__Host-` cookie, Secure=Always, Path=/, no Domain, SameSite=Strict
and HTTPS redirection/HSTS. If an HTTPS redirect port is unavailable, production
rejects HTTP requests before any authentication endpoint or static file can run. Browser shutdown may clear the session cookie sooner. There is no
"remember me" feature or token in localStorage/sessionStorage. Do not serve untrusted apps on
the same host, and do not widen cookie domains.

React's production build is served from ASP.NET Core wwwroot, including client-route fallback.
API fallbacks return structured 404 responses, never SPA HTML. During development Vite proxies
/api, so browser fetches and cookies still use one origin. CORS is deliberately not enabled.
CSP restricts production assets to self, disables framing and inline scripts; UI text is
React-escaped. API responses use no-store and nosniff headers. Never enable sensitive EF
logging, request-body logging or diagnostic exception payloads in production.

## CSRF protocol

GET `/api/auth/csrf` calls IAntiforgery.GetAndStoreTokens, setting an HttpOnly antiforgery
cookie and returning its corresponding requestToken as JSON. The SPA keeps only the request
token in memory and sends X-CSRF-TOKEN on every POST/PUT/PATCH/DELETE, with same-origin cookies.
Middleware validates unsafe API requests after authentication/authorization and before their
endpoint runs. Login is also protected (login CSRF); anonymous GET token issuance is deliberate.
The token is bound to the current identity and paired cookie, so refresh it after login,
logout or password change. Tokens obtained in a different cookie context are rejected.

No unsafe request is automatically replayed on failure. A CSRF failure clears the cached token
and asks for refresh/retry. Authorization may return 401/403 before antiforgery validation on
protected endpoints; an anonymous caller cannot mutate protected data by omitting CSRF.
SameSite=Strict is an additional protection, not a substitute for token validation.
See [Microsoft antiforgery guidance](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Access policy and first-login gate

Fallback/default authorization requires authentication and password_change_required=false.
ADMIN/COORDINATOR/EMPLOYEE policies add the respective role requirement. An explicit SignedIn
policy permits only me, logout and change-password while a temporary password is in use.
Only login, CSRF issuance, static SPA pages and development OpenAPI are anonymous. Static UI
availability never grants access to protected API data. Client route guards improve navigation;
the backend is the authority. Multi-role users satisfy each of their role policies. Shift lead
remains per-shift and cannot be assigned as an application role.

Controllers accept narrow typed DTOs, reject unmapped JSON properties and validate lengths,
required values and role allowlists. Usernames normalize through Identity and are uniquely
indexed; names support Hebrew. Directory results have stable ordering and bounded pagination.
Sensitive Identity fields and passwords never appear in user/profile responses. Model binding,
Identity failures and unexpected errors become stable codes with centralized Hebrew messages.
Raw validation descriptions/exception text never reach the UI. Unknown frontend error codes
map to a Hebrew generic message rather than displaying server-supplied text.

## Account lifecycle and revocation

Admin creation accepts first/last name, username, temporary password, roles and active status.
Creation plus role assignment is transactional. Required password changes need the current
password and a different new password meeting the Identity policy. The first-login flag is
cleared only after a successful change. The password update changes the security stamp;
only the requesting session is refreshed after the transaction commits. Other cookies fail
on their next request. There is no plaintext temporary-password storage or return value.

Cookie validation loads the current user and checks active status and the security stamp on
every authenticated request, without a validation-interval delay. Status/role/profile updates
and admin resets rotate the stamp. A copied old cookie can no longer authorize a new request.
An in-flight read that already passed authentication may complete; account writes recheck
current actor permissions and stamp inside the transaction lock. No persistent cookie store
or revocation cache is needed for this MVP; the tradeoff is a database read per request.

Deactivation is soft: historical foreign keys are untouched. Reactivation enables a fresh
login, never revives old cookies. Admin reset uses an internally generated Identity reset
token, requires another user as target, clears lockout, sets a supplied temporary password,
marks MustChangePassword=true and revokes existing sessions. The reset token and password
are never returned. An admin changes their own password through the profile flow. There is
no public/email recovery flow; use the organization's controlled admin process.

## Transactions and final-administrator safety

Every account mutation and bootstrap holds a PostgreSQL transaction-scoped advisory lock
(key 4218072). This deliberately serializes low-volume administrative writes across all
application instances. After acquiring it, the context clears prior tracked state and
reloads the actor and target. This avoids stale values loaded during cookie authentication.
The actor must still be active, have ADMIN, have completed first-password change and match
the session stamp. Password changes use the same lock and current-session stamp check.

Before a current active administrator loses ADMIN or becomes inactive, count the active
administrators under that lock. Reject losing the final one. Roles, status, profile, stamp
updates and password resets commit atomically; failures roll back. Identity's concurrency
stamp and database unique indexes are additional safeguards. Two simultaneous self-deactivation
requests cannot both remove the last active administrators; an integration test covers it.
All future account-writer implementations must use the same lock/protocol.

## Abuse controls and bootstrap

Login has a per-process, per-remote-IP limit of ten requests/minute without a queue. Identity
locks an account for fifteen minutes after five failed password attempts. Unknown users,
inactive accounts, wrong passwords and locked accounts return the same status/code/message;
a dummy password-hash verification covers unknown/inactive account paths. This does not claim
constant-time equivalence for all account/lockout states. An edge/global rate limiter is
required for multi-instance internet deployment; configure trusted proxy forwarding explicitly
so remote-IP limits see the intended client address. Do not blindly trust X-Forwarded-For.

`--bootstrap-admin` is an explicit setup-only CLI flag removed before configuration parsing.
It requires BOOTSTRAP_ADMIN_USERNAME/PASSWORD/FIRST_NAME/LAST_NAME from the environment or a
secret provider, applies ordinary field/password validation and uses the same transaction
lock. Migrations must be applied first. It creates only the first administrator, does not
promote an existing non-admin, and safely exits without changes when that active admin already
exists. Credentials are never printed. It starts no HTTP listener and exposes no public route.

Production deployments must persist protected ASP.NET Core data-protection keys between
restarts and share them across replicas. Provision HTTPS, a managed secret store, protected
backups and a DB principal with appropriate permissions. The development Compose database
is loopback-bound and must not be exposed as a production database. No MFA or security-event
audit trail is implemented in this phase; assess those before external exposure.

## Localization and verification

Frontend he.ts contains all visible labels, confirmations, role labels and error translations.
The root document uses Hebrew and RTL. CSS uses logical layout properties, keyboard focus
indicators and responsive navigation; dialogs use native modal focus handling. Password inputs
are LTR. Intl.DateTimeFormat uses he-IL/Asia/Jerusalem with explicit DD/MM/YYYY assembly; any
future time display must use 24-hour formatting. Persisted instants stay UTC.

Backend tests use real PostgreSQL and HTTP clients that retain actual protected cookies and
antiforgery cookies. Coverage includes login/errors/lockout/rate limiting, role boundaries,
creation, duplicate names, forced-password flow, reset/revocation, deactivation/reactivation,
concurrent final-admin protection, CSRF, DTO tampering, bootstrap and production cookies.
Frontend tests exercise Hebrew/RTL, login errors, protected routes, first-login change and
admin create/edit/role/reset/status interactions. See README for exact commands and opt-in
PostgreSQL execution. Browser visual QA is separate from component interaction testing.

## Deferred scope

No availability submissions, schedules, task allocation, password delivery/email recovery,
MFA, session-device management or audit-log UI. Role placeholder pages only verify access.
Next phase: transactional availability windows/submissions and their cross-month weekly rules,
with permissions derived from these policies. Scheduling/publication follow afterward.
