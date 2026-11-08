# Phase 7C — Security and authorization validation

Assessment date: **2026-11-08**. Base: merged Phase 7B, `5927c40`.
Branch: `feature/phase-7c-security-validation`.

## Scope and trust boundaries

This assessment reviews the actual cookie-based ASP.NET Core/Identity application,
React frontend, EF Core/Npgsql access, scheduling workflows, test artifacts, and CI.
It prioritizes exploitable authorization and workflow failures. All active tests use
disposable PostgreSQL and synthetic accounts. No production, third-party application,
real employee account, or persistent development/demo record was attacked or changed.
Public package advisories, vendor release metadata, and scanner binaries were retrieved
for dependency review; no discovered credentials were tested against external services.

The browser is untrusted. React navigation and disabled buttons are conveniences;
controllers and services enforce permissions. Identity issues encrypted, authenticated
cookies, and `AppCookieEvents` checks the current active account/security stamp on
every authenticated request. Account, membership, approval and roster writers also
recheck authority under the existing transaction/advisory locks. PostgreSQL enforces
membership, capacity, audit and immutable-publication constraints.

Production is a same-origin deployment: ASP.NET serves the built frontend and `/api`.
Development Vite proxies `/api`; the disposable E2E frontend has its own guarded
configuration. There is no production CORS allowlist or credentialed cross-origin grant.
The test-only fixture control routes live in a separate executable, not in the
production application. A SPA fallback response is not a fixture API or OpenAPI document.

## Authorization matrix

Identity roles are **ADMIN**, **EMPLOYEE**, and **COORDINATOR**. Organizational leadership
and deputy authority come from current team membership/leadership records, not new
Identity roles. Shift-lead qualification is a scheduling attribute, not administrative
authority. A standalone COORDINATOR has its existing access marker but no automatic
team, employee or administrative permissions. ADMIN and team leadership use separate
accounts; an administrator cannot become a team member through the supported API.

| Endpoint family / supported methods | Expected authority and object scope |
| --- | --- |
| `GET /api/auth/csrf`, `POST /api/auth/login` | Anonymous allowed; login still requires antiforgery. Login is rate limited and account lockout applies. |
| `GET /api/auth/me`, `POST /api/auth/change-password`, `POST /api/auth/logout` | Signed-in account, including an account required to change its initial password. Password change uses the current account/current password; logout now revokes all that account's sessions. |
| `GET, POST /api/users`; `GET, PUT /api/users/{id}`; `PATCH .../status`; `PUT .../roles`; `POST .../reset-password` | ADMIN with completed password change. User IDs do not grant profile, status, role or password-reset access. No employee self-edit endpoint is invented. |
| `GET /api/admin/teams`, `.../conflicts`, `.../audit`; `PUT /api/admin/teams/{id}`, `.../members/{userId}` | ADMIN only. Membership and leadership constraints remain enforced. |
| `GET /api/team-availability/members` and `.../employees/{id}/periods`; `GET, POST, PUT .../employees/{id}/{year}/{month}` | Current leader/deputy, current own-team eligible employees only. Leadership is not scheduling administration. Delegated edits may bypass the submission window, not weekly limits or published/final workflow restrictions. |
| `GET /api/availability/periods`; `GET, POST, PUT /api/availability/{year}/{month}` | Eligible EMPLOYEE acting as the signed-in user. Query-string user IDs cannot substitute another identity. Ordinary writes require an open window and valid version. |
| `GET, PUT /api/admin/availability/{year}/{month}`, `PUT .../state`, `GET .../employees/{id}` | ADMIN only; existing period-state and time rules remain mandatory. |
| `GET /api/admin/scheduling/board`, `.../shift-types`, `.../automatic-configuration`; `PUT .../shift-types` | ADMIN only, including required headcounts, qualification and Critical/High/Normal configuration. |
| `POST /api/admin/scheduling/generation`; `GET, DELETE .../generation/{id}`; `GET .../shortages`; `POST .../save` | ADMIN only. A known preview ID is not authority; save rechecks current account/stamp/role and input fingerprint. |
| `GET /api/admin/scheduling/periods/{year}/{month}` and `.../candidates`, `.../publications`, `.../issues`, `.../exchanges`, `.../audit` | ADMIN only. Candidates/replacement IDs and nested period resources remain scoped to their actual period. |
| `POST .../assignments`, `.../on-call-pair`, `.../review`, `.../correction`, `.../publications/{type}`, `.../publications/{id}/control`; `PUT .../issues/{id}` | ADMIN only. Tokens, versions, workflow state, qualifications, hard constraints, immutable history and explicit acknowledgements still apply. No unrestricted override was added. |
| `GET /api/my-schedule/periods`, `.../{year}/{month}`, `.../versions`, `.../issues`; `POST .../issues` | Participating EMPLOYEE, not ADMIN or an excluded organizational leader. Published roster sharing is intentional; own issue history/reporting is identity scoped, and reports require an own assignment in the current visible preliminary publication. |
| `GET /api/absences/mine`, `POST /api/absences`, `POST .../{id}/changes`, `.../cancel-pending` | EMPLOYEE; personal proposals/withdrawals belong to the signed-in user. An approved absence changes only after a separately approved MODIFY/CANCEL proposal. |
| `GET /api/absences/manage`, `.../{id}/conflicts`; `POST .../{id}/decision` | ADMIN globally, or current leader/deputy for current own-team members. List filters can narrow but never widen scope. **Every decision requires an approver different from the request owner**, including CREATE, MODIFY and CANCEL. Owners can still withdraw their own pending proposals. |
| `GET /api/shift-exchanges`, `.../{year}/{month}/assignments`, `.../candidates`; `POST .../{year}/{month}`, `.../{id}/decision` | Participating EMPLOYEE. Source assignment must belong to requester; only the named replacement consents/rejects; only requester cancels. Request IDs do not confer participation. |
| `GET /api/admin/shift-exchanges`, `.../{id}/publication-review`; `POST .../{id}/decision`, `.../{id}/publish` | ADMIN only; consent, eligibility, current versions, atomic paired correction and explicit republication remain required. |
| `GET /api/access/{admin,employee,coordinator}` | Corresponding Identity role plus completed password change; these are access markers, not extra management capabilities. |
| Tasks, uploads, remote URL fetching, email reset | No corresponding public task/file/URL-fetch/email-reset controller exists. Task protection in existing assignments is covered by scheduling/absence/exchange integration tests; nonexistent HTTP methods/resources were not fabricated as IDOR tests. |

The shared published roster intentionally contains employees' published names and
assignments. This assessment does not replace that approved business policy with
personal-only visibility. Private availability, absence reasons, account management,
drafts and other employees' issue histories have separate protections.

## Methodology and automated coverage

New backend cases are in `SecurityAuthorizationTests`, `SecurityEndpointCatalogTests`,
`SecurityRequestTests` (partial `SchedulingTests`) and `SecuritySessionTests` (partial
`AuthTests`). They reuse `TestDatabase`, real Identity login/antiforgery helpers, and
existing scheduling/absence/publication services. Test hosts now use isolated,
ephemeral data-protection keys instead of the development/demo key directory.

- The administrative denial matrix covers every current administrative controller
  method/path using valid users, two teams, real assignments, publications, issues,
  absence proposals, exchange requests, and a ready generation job. Actors include
  anonymous, regular employee, leader, deputy, qualified employee, another-team employee,
  standalone Coordinator, and an ADMIN still required to change its initial password.
- An endpoint-metadata test requires ADMIN policy, rejects `AllowAnonymous`, and
  verifies that each administrative controller action has a real-resource matrix case.
  Adding an untested action therefore fails the catalog check.
- Authorized readers and writers prove that the resources exist and supported
  workflows work. Cross-team direct/nested availability requests, absence decisions,
  filter tampering, ownership substitutions, personal issue reporting, exchange
  creation/consent, and user-ID query tampering are checked directly over HTTP.
- Rejected mutations compare a whole-application database fingerprint. PostgreSQL
  returns only per-table hashes; no employee records, password hashes or security
  stamps appear in assertions. The known generation job is also checked for memory
  side effects. Existing audit actors are verified for successful approval/control.
- Missing/tampered antiforgery tokens are tested across administrative POST, PUT,
  PATCH and DELETE operations. A pre-login token cannot authorize a post-login write.
- Session tests cover copied/tampered tickets, another signed-in session, the initial
  password gate, and an absolute eight-hour lifetime without sliding renewal.
  Existing authentication tests retain reset/disable/role-change/password-change
  invalidation, lockout, rate limiting, host-prefixed secure cookies and last-admin rules.
- Malformed GUID/date inputs, enum/priority/headcount bounds, oversized strings,
  nonexistent team references, missing fields, unexpected JSON/role overposting and
  SQL-like search inputs are rejected without partial writes or exception disclosure.
- Repeated consent/approval, already rejected proposals, and concurrent conflicting
  decisions exercise version checks, one-winner behavior and immutable audit state.
  Existing publication/exchange-pair/concurrency tests retain completed-exchange
  replay, invalid publication transitions and direct snapshot-immutability checks.

Three new Playwright cases in `frontend/e2e/specs/security.spec.ts` use the Phase 7B
owned PostgreSQL/Kestrel/Vite environment and one Chromium worker. A tiny attacker
HTTP server binds only to another random loopback port. It tests a **same-site but
cross-origin** form POST to the real backend: valid cookies alone cannot log out the
victim without the antiforgery token, and JavaScript cannot read cross-origin CSRF
responses. There is no external attacker host or network target. Stored markup is
checked as literal Hebrew text in drafts and employee publications, with no executable
image node/canary. The third case checks the new self-decision UI and direct API denial,
then demonstrates approval by a different team approver. A focused frontend unit
regression preserves teammate decision controls while hiding personal decision controls.

## Confirmed application findings and fixes

### SEC-01 — Copied ticket survives logout (Medium; fixed)

Prerequisite: an attacker already possesses a valid authenticated cookie. Before the
fix, a controlled copied ticket returned 200 after the legitimate browser logged out;
ordinary browser logout only removed its own cookie. The exposure lasted until the
absolute ticket expiry or an account security-stamp change. This was not password
disclosure or an unauthenticated login bypass.

The approved policy is logout from **all account sessions**. Logout now takes the
existing account transaction/lock, reloads and revalidates the actor/stamp, rotates the
security stamp, commits, then clears the browser cookie. Existing per-request stamp
validation rejects copied tickets and other devices on their next request. A fresh
login still works. In-flight requests already authorized before logout are not canceled.
No session-store replacement or new distributed locking was introduced.

Regression: `Security_logout_revokes_copied_tickets_and_other_sessions_without_printing_session_material`
and the initial-password logout case. Cookie values remain in memory and are never
printed in test errors or this report.

### SEC-02 — Team approver can decide own absence proposal (Medium; fixed)

Prerequisite: an active team approver who can submit employee absence requests.
The original team-membership predicate also matched the approver's own membership.
Controlled CREATE, MODIFY and CANCEL self-approval requests returned 200 before the
fix, allowing the requester to make a personal restriction effective or cancel it
without an independent decision.

The approved policy requires a **different approver**. `AbsenceService.Decide` now
rejects an actor whose ID equals the request owner under the existing transaction
and locks, before a decision or scheduling change is written. It covers approvals
and rejections for all proposal kinds. Team/global scope checks and all conflict
acknowledgements remain mandatory. Pending personal withdrawal is still allowed;
withdrawal does not cancel an already approved absence.

The Hebrew RTL management screen explains the requirement and omits personal decision
controls. Direct HTTP attempts return 403/`SELF_ABSENCE_DECISION`; the server is the
enforcement boundary. Three backend cases check rejection without any database change
and subsequent legitimate peer approval; frontend and browser regressions check the UI.

## Authentication, CSRF, CORS and input findings

Authentication cookies remain HttpOnly, SameSite=Strict, secure on production HTTPS,
host-prefixed with path `/` and no Domain attribute in production. The server ticket
has an absolute eight-hour lifetime; session cookies are not persistent browser
storage states. Reset, disable, profile/qualification, role and password changes
invalidate older tickets. Wrong/missing/inactive login responses stay generic.

SameSite is defense in depth, not the sole CSRF boundary. Every API mutation requires
ASP.NET antiforgery validation; login is protected too. React keeps the request token
in module memory and resets it on authentication transitions. Same-origin token
delivery and no credentialed CORS grant prevent foreign origins reading it. A caller
already holding both a valid session and its matching CSRF token is not equivalent
to a tokenless cross-site browser attacker. No state-changing business GET was found;
the CSRF GET intentionally manages only the antiforgery cookie/token.

Production HTTPS handling fails closed without a configured redirect destination.
HSTS, `nosniff`, same-origin referrer policy and the existing CSP (including
`frame-ancestors 'none'` and `form-action 'self'`) were inspected/tested. No new CSP
restriction or cross-origin allowance was introduced. Production OpenAPI documents
and test controls are not exposed; generic error/SPA responses are distinguished
from actual debug APIs.

EF Core predicates and parameterized/interpolated SQL were reviewed. Remaining raw
SQL uses fixed commands or validated/generated identifiers in explicit local CLI/test
operations. No public arbitrary-SQL, shell, file-path or URL-fetch endpoint was found.
React's default escaping is retained; no `dangerouslySetInnerHTML`/equivalent sink was
found in application source. Hebrew text is not broadly stripped or sanitized.

## Dependencies, secrets and supply chain

`scripts/security-audit.py` checks NuGet including transitives, locked npm dependencies,
Gitleaks across all local Git history and tracked/nonignored working source, and private
file ignore protections. High/critical NuGet/npm advisories or secret findings fail
the command. Scanner/feed failures fail closed rather than becoming a clean result.
Only safe counts and redacted finding locations are printed. Raw scanner reports are
not uploaded by the new CI job.

Observed audits: **zero NuGet advisories, zero npm advisories, zero Gitleaks history
findings and zero working-source findings**. These are advisory/signature-based results,
not proof that unknown vulnerabilities or every possible secret format are absent.
Intentional synthetic test/CI passwords are not deployment credentials. Ignored `.env`,
`.demo` and E2E private captures were not copied into source scans or documentation.
The Phase 7B artifact redaction pipeline is preserved.

The installed system runtime was 10.0.9, behind Microsoft's security servicing baseline.
Project/EF tooling and Microsoft packages were updated within .NET 10 to **10.0.14**;
Microsoft.OpenApi **2.12.2** is the compatible minimum required by that servicing update.
`global.json` requires SDK **10.0.403** or a compatible later .NET 10 feature band.
`TargetLatestRuntimePatch` makes generated application/test-host runtime configurations
require both Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.14 with this SDK.
This introduces no schema migration or unrelated major-version upgrade.

Validation uses a private SDK installed from Microsoft's release metadata with verified
SHA-512, leaving the system SDK and running services unchanged. Gitleaks **8.30.1** and
Trivy **0.76.2** binaries were downloaded from official releases and checked against
published SHA-256 values. CI pins the Linux Gitleaks checksum; existing GitHub Actions
remain pinned by commit SHA with `contents: read` and no persisted checkout credentials.
Local macOS tool restore reported skipped NuGet package signature verification; no
independent package-signature attestation is claimed from that restore.

### PostgreSQL image review — deployment hardening remains open

Trivy scanned local ARM64 `postgres:17-alpine`, digest
`sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24`
(PostgreSQL 17.11 / Alpine 3.24.2). It reported **29 package findings: one Critical and
28 High**, including duplicate package instances of the same CVE. These severities
are scanner/package classifications, not 29 demonstrated application vulnerabilities.

- Three OS-package findings affect musl/musl-utils (`CVE-2026-107891`, fixed package
  `1.2.6-r3`) and zlib (`CVE-2026-85091`, fixed `1.3.2-r1`). The vulnerable functions
  concern untrusted POSIX regex compilation and a particular nonblocking gzip-write
  path. This assessment did not demonstrate an HTTP-to-vulnerable-function path,
  but does not approve leaving these packages unpatched in a pilot image.
- Twenty-six findings are in the Go standard library reported for `/usr/local/bin/gosu`.
  The Critical example, `CVE-2025-68121`, concerns TLS session resumption. Source review
  shows gosu is a user-switch/exec startup helper, not a listening HTTP/TLS service.
  Its use here does not establish reachability of those network-code findings. This
  is an exposure assessment, not a blanket suppression or proof about every component.
- PostgreSQL's own 17.11 security release/advisory list was reviewed separately:
  an OS package scanner is not a complete audit of a server built from source.

Before pilot deployment, update/rebuild and rescan the **actual target-architecture
image digest**, resolve applicable OS findings, and explicitly review any residual
unused-code findings with the deployment owner. A floating tag can change and an
ARM64 scan does not certify the GitHub runner's AMD64 image. No persistent database
container was upgraded, recreated, restarted or modified as part of this assessment.

## Isolation and local reproduction

`python3 scripts/test-security.py` creates one PostgreSQL container with generated
credentials, a unique run/name/role, a run ownership label, 512 MB/two CPU limit,
tmpfs data and a random loopback port. It rejects persistent ports 5432/55432 and
does not accept supplied development/demo connection strings. Existing `TestDatabase`
creates a fresh `shiftflow_test_<uuid>` database per test in that owned service.
Cleanup verifies the exact run label, including startup failures before an ID is returned.

```sh
# Requires .NET SDK 10.0.403+, Python 3 and Docker.
python3 scripts/test-security.py
python3 scripts/test-security.py --all

# Requires Node 24+, Chromium, and a checksum-verified Gitleaks binary.
cd frontend
npm ci
npx playwright install chromium
npm run test:e2e
npm test -- --maxWorkers=1
```

From the repository root, run the lightweight checks with
`python3 scripts/security-audit.py --gitleaks /path/to/gitleaks`.
Add `--trivy /path/to/trivy` to reproduce the image review; image findings are reported
for explicit exposure triage, not silently reclassified as safe by the dependency gate.
No test cookie or password is needed in a command argument. Run expensive suites
sequentially on a constrained machine; the browser runner owns its independent
container and has the Phase 7B connection, ownership-marker, proxy and artifact guards.

Read-only baseline checks: demo **60 users / 1,397 assignments / three publications /
29 absences**, migration `20261106074419_SchedulingPriority`; development **eight users /
zero assignments / zero publications**, migration `20261027224058_AdditionalSchedulingConstraints`.
Neither persistent target receives test migrations, reseeding, cleanup or security payloads.
Final read-only verification matched both baselines, including migration levels.
Only the two original healthy persistent containers remained; no security/E2E-owned
containers or private browser captures remained after cleanup.

## CI and validation results

The Backend job automatically includes the new PostgreSQL security cases. The Frontend
job retains all existing tests plus the UI regression. The existing E2E job includes
the three browser security cases without a duplicated browser job. A new lightweight
Dependency and secret checks job restores dependency graphs, installs checksum-pinned
Gitleaks and runs the same audit command. Workflow permissions remain least privilege.
No scanner credentials or live secrets are introduced. Image review is available locally;
the lightweight CI dependency gate does not claim to rescan/approve a production image.

Added **31 security regression cases**: 27 backend, three browser and one frontend
unit case. Final local validation on the patched runtime:

| Validation | Result |
| --- | --- |
| Complete backend suite, disposable PostgreSQL | **419 passed, zero failed/skipped** |
| Complete frontend unit suite | **128 passed, zero failed/skipped** |
| Complete Chromium workflow/security suite, one worker | **31 passed, zero failed/skipped/flaky** |
| Browser database connection guards | **Nine checks passed**: one valid and eight unsafe targets, without connecting |
| Browser artifact-safety tests | **Three passed** |
| Development runner safety tests | **Seven passed** |
| Release backend/E2E-host builds | Passed, zero warnings/errors |
| Fresh disposable migrations and pending-model check | Passed; no new migration or pending model changes |
| Frontend production build and E2E TypeScript check | Passed |
| Actionlint and Git whitespace check | Passed |
| NuGet including transitives / npm / all-history and source Gitleaks | Zero advisory/secret findings |
| Optional Trivy PostgreSQL image review | 29 high/critical package findings; open deployment requirements above |

Focused pre-fix testing reproduced copied-ticket replay and three self-decision
failures; the completed suites include the corrected regressions. All disposable
containers were removed and raw browser captures cleaned. Both persistent database
baselines were unchanged. Local validation is complete, but **GitHub Actions has not
been run for this unpushed branch**. No remote branch or PR is changed by this work;
a new remote run cannot be claimed before the user authorizes a push.

## Pilot deployment requirements and acceptance

1. Use serviced .NET/ASP.NET Core 10.0.14+ and a reviewed, patched PostgreSQL image/OS
   baseline. Validate the actual deployed digest, extensions and target architecture.
2. Serve the app/API on the intended HTTPS origin with `Production` environment,
   explicit database configuration, trusted TLS and no publicly reachable database.
   Behind TLS termination, configure only trusted forwarded-header proxies/networks;
   the current app does not blindly trust client-supplied forwarded headers.
3. Separate migration/bootstrap privileges from the runtime database role; disposable
   test superuser/CREATEDB credentials must never become production runtime credentials.
4. Persist and protect production data-protection keys across restarts/instances,
   restrict filesystem access and use appropriate at-rest protection. Keep deployment
   secrets outside Git and outside published browser artifacts; rotate bootstrap/temp
   credentials and require the existing initial password change.
5. Obtain a successful CI run on the final commit, verify backup/restore and monitoring,
   and communicate the approved all-device logout and independent absence-approval policies.

No confirmed critical/high application exploit was demonstrated by the completed
assessment. That statement is bounded by this scope and is not a production
security guarantee. Final Phase 7C acceptance remains **continue security stabilization**
until CI and the image/deployment hardening requirements are satisfied. Local regression
results pass. No MFA, SSO, email reset, external penetration test, production proxy/TLS
test or broad load/DoS exercise is claimed. Account-management audit expansion and
centralized operational alerting are deployment/future work; existing immutable
workflow audits are preserved and tested.

## Primary references

- [ASP.NET Core 10 antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [ASP.NET Core 10 CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
- [Microsoft .NET 10.0.14 servicing notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.14/10.0.14.md)
- [Official PostgreSQL image Dockerfile](https://github.com/docker-library/postgres/blob/master/17/alpine3.24/Dockerfile)
- [gosu source](https://github.com/tianon/gosu/blob/master/main.go)
- [PostgreSQL security advisories](https://www.postgresql.org/support/security/), [17.11 release notes](https://www.postgresql.org/docs/release/17.11/)
