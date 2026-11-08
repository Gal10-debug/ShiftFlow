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

### PostgreSQL image review — initial Phase 7C baseline

**Historical scan at `bcb2580`:** the Phase 7C.1 section below supersedes the image
selection, residual assessment and validation results. Protected running databases
remain on this original image until a separately approved maintenance operation.

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

## CI and validation results — Phase 7C baseline at `bcb2580`

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
security guarantee. The initial Phase 7C recommendation was **continue security stabilization**
until CI and the image/deployment hardening requirements are satisfied. Its local regression
results passed; the Phase 7C.1 recommendation and latest results follow below. No MFA, SSO, email reset, external penetration test, production proxy/TLS
test or broad load/DoS exercise is claimed. Account-management audit expansion and
centralized operational alerting are deployment/future work; existing immutable
workflow audits are preserved and tested.

## Phase 7C.1 — PostgreSQL container security remediation

Assessment date: **2026-11-08**. Continues `bcb2580` on the same feature branch.
PostgreSQL remains **17.11**; no PostgreSQL major-version change, schema change,
production deployment or persistent-container upgrade is performed.

### Image inventory and candidate comparison

Before this change, `compose.yaml`, `compose.demo.yaml`, the Backend CI service,
backend Testcontainers fallback, security runner and browser runner all used the
floating `postgres:17-alpine` reference. The audit script scanned that same local tag.
Both protected local databases actually resolve to the recorded ARM64 artifact
`sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24`:
PostgreSQL 17.11, Alpine 3.24.2, musl 1.2.6-r2 and zlib 1.3.2-r0.
Historical GitHub Actions resolutions were not recorded, so the 29 findings are not
assumed to describe every historical AMD64 CI run. No production image, Dockerfile
or production deployment configuration exists in this repository.

Official image metadata and actual package inventories were inspected. Trivy **0.76.2**,
vulnerability database schema 2 updated **2026-11-07 18:32:58 UTC**, scanned each image
with `--scanners vuln --severity HIGH,CRITICAL`. The same cached database was used for
comparison; no ignore file, severity override, VEX suppression or `--ignore-unfixed`
was applied. Both OS packages and the Go executable in the final runtime filesystem
were included. Downloading images did not retag, restart or replace a protected container.

| Image / native ARM64 scan | Runtime OS | High | Critical | Total | Assessment |
| --- | --- | ---: | ---: | ---: | --- |
| Original local `postgres:17-alpine` artifact above | Alpine 3.24.2 | 28 | 1 | 29 | Reproduced baseline; three fixable OS-package findings |
| `postgres:17.11-alpine3.24` | Alpine 3.24.4 | 25 | 1 | 26 | Selected: same PostgreSQL minor, libc family, paths and UID; three OS findings fixed |
| `postgres:17.11-alpine3.23` | Alpine 3.23.8 | 25 | 1 | 26 | No additional reduction; unnecessary older OS branch |
| `postgres:17.11-trixie` | Debian 13.7 | 91 | 2 | 93 | Different libc, locale implementation and UID; more package findings, without fixing bundled Go metadata |

Counts from different distribution advisory feeds are not a direct exploitability
ranking. Trixie was not rejected solely because its count is higher: switching musl
to glibc, postgres UID/GID 70 to 999, and the available locales would add migration
and ownership risks without addressing the remaining helper findings. Alpine 3.24
retains ICU support and the existing `/var/lib/postgresql/data` layout. No external
PostgreSQL extension is required by the current migrations; built-in PL/pgSQL is used
for triggers. Optional deployment extensions still require their own compatibility review.

The alternative multi-architecture index digests were
`bc2701f9a79f9248bcb18743bc9b2c732d9a1d16737ca44a8f577752fc43b61f` (Alpine 3.23)
and `ff87138949ad9a46f32ff23bd6852b52ce024a13dffe9b4087be98292cdd3b4f` (Trixie).
The official [image catalog](https://github.com/docker-library/official-images/blob/master/library/postgres)
identifies PostgreSQL 17.11 variants supporting both ARM64 and AMD64.
PostgreSQL lists [major 17 as supported through 2029-11-08](https://www.postgresql.org/support/versioning/).
Alpine lists [3.24 main support through 2028-06-01](https://alpinelinux.org/releases/);
community packages have the shorter next-stable-release support window. Recheck these
policies and package provenance during maintenance; a digest pin does not extend support.

### Selected remediation and exact references

All existing executable image references now use:

```text
postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac
```

This is the **multi-architecture index**, not an ARM-only digest. Its inspected raw
manifest hashes to that exact digest and includes:

| Platform | Image manifest digest | Selected scan |
| --- | --- | --- |
| Linux ARM64/v8, Apple Silicon Docker | `sha256:1108a8fe22dff4eab48870e081be046d549ddc1b6f5972e0a766eaf1e0b1003c` | 25 High / one Critical |
| Linux AMD64, GitHub Actions | `sha256:4717f90960a225c2d1c5dac50508c401230f693ff626e06e84e6db0a68ded508` | 25 High / one Critical |

The native ARM64 image was executed for compatibility tests. AMD64 was scanned from
the registry and its manifest verified; local execution of AMD64 or a GitHub CI run
is not claimed. Both selected-platform scans have the same remaining CVE/package set.
The refreshed image contains musl/musl-utils **1.2.6-r3**, zlib **1.3.2-r1**,
ICU **78.1-r0**, and tzdata **2026f-r0**. No custom image or package installation layer
was introduced. The digest fixes the tested package set; it is an update baseline,
not an automatic security-update mechanism.

Updated consumers: `compose.yaml`, `compose.demo.yaml`, `.github/workflows/ci.yml`,
`tests/ShiftManager.Tests/TestDatabase.cs`, `scripts/test-security.py`,
`frontend/e2e/run.mjs`, and `scripts/security-audit.py`. The new storage probe uses the
same pin. The README now uses the owned disposable runner instead of a manual
fixed-port recipe that conflicted with the demo port. Changing these files has no
runtime effect on existing containers. Running Compose up/recreate to apply the
reference to existing data requires separate explicit approval.

### Assessment of every original finding

There were **29 package findings across 28 distinct CVEs**: the musl advisory was
reported for two installed packages. The table below covers all three OS findings.
Each package is present in the original final runtime filesystem; the replacement
still contains the package with the patched version. The scanner source is **Alpine
Secdb**, and vendor fixed versions were checked against the Alpine tracker.

| CVE | Package | Original version | Verified fixed/selected version | Severity | Classification and exposure |
| --- | --- | --- | --- | --- | --- |
| [CVE-2026-107891](https://security.alpinelinux.org/vuln/CVE-2026-107891) | musl | 1.2.6-r2 | 1.2.6-r3 | High | Available fix applied; runtime libc regexp issue. An application-to-vulnerable-function path was not demonstrated; private networking alone is not a mitigation. |
| [CVE-2026-107891](https://security.alpinelinux.org/vuln/CVE-2026-107891) | musl-utils | 1.2.6-r2 | 1.2.6-r3 | High | Same advisory, second package instance; available fix applied. |
| [CVE-2026-85091](https://security.alpinelinux.org/vuln/CVE-2026-85091) | zlib | 1.3.2-r0 | 1.3.2-r1 | High | Available fix applied; specific nonblocking gzip-write/gzprintf path. No such application path demonstrated; client/backup functionality must also be considered. |

The remaining **26** findings all identify `stdlib v1.24.6` through the installed
`/usr/local/bin/gosu` **1.19** executable's Go build metadata. The Go runtime/executable
is present in the final selected image; that does **not** establish that every
standard-library function named below is linked or invoked. The scanner source for
every row is the **Go Vulnerability Database**. Each linked canonical OSV record was
retrieved and reviewed for affected ranges, fix versions, withdrawal and affected APIs.
None of these records was withdrawn. The listed upstream fixes exist, but none of the
compared official images bundles a Go runtime rebuild addressing this metadata set.

| CVE | Canonical source | Severity | Upstream fixed Go versions reported by Trivy | Affected standard-library API/package | Deployment assessment |
| --- | --- | --- | --- | --- | --- |
| CVE-2025-68121 | [GO-2026-4337](https://pkg.go.dev/vuln/GO-2026-4337) | Critical | 1.24.13, 1.25.7, 1.26.0-rc.3 | `crypto/tls` | No reachable use demonstrated; residual review |
| CVE-2025-61726 | [GO-2026-4341](https://pkg.go.dev/vuln/GO-2026-4341) | High | 1.24.12, 1.25.6 | `net/url` | No reachable use demonstrated; residual review |
| CVE-2025-61729 | [GO-2025-4155](https://pkg.go.dev/vuln/GO-2025-4155) | High | 1.24.11, 1.25.5 | `crypto/x509` | No reachable use demonstrated; residual review |
| CVE-2026-111943 | [GO-2026-6809](https://pkg.go.dev/vuln/GO-2026-6809) | High | 1.26.10, 1.27.3 | `crypto/x509` | No reachable use demonstrated; residual review |
| CVE-2026-25679 | [GO-2026-4601](https://pkg.go.dev/vuln/GO-2026-4601) | High | 1.25.8, 1.26.1 | `net/url` | No reachable use demonstrated; residual review |
| CVE-2026-27145 | [GO-2026-5037](https://pkg.go.dev/vuln/GO-2026-5037) | High | 1.25.11, 1.26.4 | `crypto/x509` | No reachable use demonstrated; residual review |
| CVE-2026-32280 | [GO-2026-4947](https://pkg.go.dev/vuln/GO-2026-4947) | High | 1.25.9, 1.26.2 | `crypto/x509` | No reachable use demonstrated; residual review |
| CVE-2026-32281 | [GO-2026-4946](https://pkg.go.dev/vuln/GO-2026-4946) | High | 1.25.9, 1.26.2 | `crypto/x509` | No reachable use demonstrated; residual review |
| CVE-2026-32283 | [GO-2026-4870](https://pkg.go.dev/vuln/GO-2026-4870) | High | 1.25.9, 1.26.2 | `crypto/tls` | No reachable use demonstrated; residual review |
| CVE-2026-33811 | [GO-2026-4981](https://pkg.go.dev/vuln/GO-2026-4981) | High | 1.25.10, 1.26.3 | `net` | No reachable use demonstrated; residual review |
| CVE-2026-33814 | [GO-2026-4918](https://pkg.go.dev/vuln/GO-2026-4918) | High | 1.25.10, 1.26.3 | `net/http` | No reachable use demonstrated; residual review |
| CVE-2026-33818 | [GO-2026-5972](https://pkg.go.dev/vuln/GO-2026-5972) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `encoding/asn1` | No reachable use demonstrated; residual review |
| CVE-2026-39820 | [GO-2026-4986](https://pkg.go.dev/vuln/GO-2026-4986) | High | 1.25.10, 1.26.3 | `net/mail` | No reachable use demonstrated; residual review |
| CVE-2026-39821 | [GO-2026-5026](https://pkg.go.dev/vuln/GO-2026-5026) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `net/http`, `net/http/internal/http2` | No reachable use demonstrated; residual review |
| CVE-2026-39822 | [GO-2026-4970](https://pkg.go.dev/vuln/GO-2026-4970) | High | 1.25.12, 1.26.5, 1.27.0-rc.2 | `os` | No reachable use demonstrated; residual review |
| CVE-2026-39836 | [GO-2026-4971](https://pkg.go.dev/vuln/GO-2026-4971) | High | 1.25.10, 1.26.3 | `net` | Linux not affected (Windows-only) |
| CVE-2026-42499 | [GO-2026-4977](https://pkg.go.dev/vuln/GO-2026-4977) | High | 1.25.10, 1.26.3 | `net/mail` | No reachable use demonstrated; residual review |
| CVE-2026-42504 | [GO-2026-5038](https://pkg.go.dev/vuln/GO-2026-5038) | High | 1.25.11, 1.26.4 | `mime` | No reachable use demonstrated; residual review |
| CVE-2026-56853 | [GO-2026-6089](https://pkg.go.dev/vuln/GO-2026-6089) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `net/http` | No reachable use demonstrated; residual review |
| CVE-2026-56858 | [GO-2026-6091](https://pkg.go.dev/vuln/GO-2026-6091) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `html/template` | No reachable use demonstrated; residual review |
| CVE-2026-56859 | [GO-2026-6088](https://pkg.go.dev/vuln/GO-2026-6088) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `encoding/xml` | No reachable use demonstrated; residual review |
| CVE-2026-56860 | [GO-2026-6218](https://pkg.go.dev/vuln/GO-2026-6218) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `net/url` | No reachable use demonstrated; residual review |
| CVE-2026-56862 | [GO-2026-6090](https://pkg.go.dev/vuln/GO-2026-6090) | High | 1.25.13, 1.26.6, 1.27.0-rc.3 | `crypto/tls` | No reachable use demonstrated; residual review |
| CVE-2026-78667 | [GO-2026-6609](https://pkg.go.dev/vuln/GO-2026-6609) | High | 1.26.9, 1.27.2 | `net/http` | No reachable use demonstrated; residual review |
| CVE-2026-78669 | [GO-2026-6611](https://pkg.go.dev/vuln/GO-2026-6611) | High | 1.26.9, 1.27.2 | `net/http`, `net/http/internal/http2` | No reachable use demonstrated; residual review |
| CVE-2026-97031 | [GO-2026-6607](https://pkg.go.dev/vuln/GO-2026-6607) | High | 1.26.9, 1.27.2 | `crypto/tls` | No reachable use demonstrated; residual review |

`gosu` switches UID/GID/groups and then `exec`s PostgreSQL; its inspected source has
no HTTP/TLS listener, HTTP client, certificate processing, mail, template or XML input
flow. Its user-setup path reads image-controlled passwd/group files. The deployed
Linux platform also makes **CVE-2026-39836**'s Windows-only panic inapplicable. The
`os.Root` advisory was considered separately from networking: the inspected gosu path
does not call those Root methods. These are source/deployment assessments, not a
binary-level call-graph proof. No symbol-level `govulncheck` result is claimed.

The [gosu security policy](https://github.com/tianon/gosu/blob/1.19/SECURITY.md) explains
why compiler-version scanners can report unused APIs; its advice does not itself
prove a false positive for this exact binary. The newer [gosu 1.20 release](https://github.com/tianon/gosu/releases/tag/1.20)
also reports Go 1.24.6, so replacing 1.19 with 1.20 would not resolve these compiler
metadata findings. It addresses a separate user-parser advisory; image/vendor updates
still need ongoing review. A custom helper replacement or distribution switch solely
to change counts was not justified here.

The resulting classification is explicit:

- **Three OS-package findings fixed**, representing two CVEs; no new High/Critical
  CVE/package pair introduced on the selected ARM64 scan.
- **26 raw residual findings**: 25 High and one Critical, retained without suppression.
- **One** residual High finding is platform-inapplicable on Linux (Windows-only).
- **25** residual findings have no demonstrated reachable use in this deployment and
  require recorded residual review; they are not counted as patched or conclusively
  proven false positives. This includes the Critical TLS metadata finding.
- **Zero confirmed reachable High/Critical application/container exploits** were
  demonstrated. This bounded result is not a claim of zero relevant risk. The three
  original potentially relevant OS-package findings were fixed; 25 residual metadata
  matches still need deployment-owner review, and one is platform-inapplicable.
- No investigated original advisory lacks an upstream fix. There are no confirmed
  withdrawn/disputed records or package-version mismatches beyond the stated platform
  mismatch. Availability of a Go fix is distinct from availability in an official image.

### Disposable compatibility and regression evidence

The full backend/security suite uses the pinned image in one owned tmpfs container,
generated credentials and a random loopback port; per-test databases are disposable.
Fresh EF migrations and the pending-model check run only there. Existing integration
cases exercise real authentication/stamp changes, scheduling persistence, approved
and pending absences, immutable publication snapshots, exchanges, transaction rollback,
concurrent approvals and reads through fresh application hosts/contexts.

`PostgresImageCompatibilityTests` adds a server-side check for PostgreSQL major 17,
UTF-8, Hebrew ICU collation and PL/pgSQL, then compares PostgreSQL's image tzdata with
.NET's `ShiftClock` across Jerusalem's spring/autumn DST transitions and an ordinary
night: **11 / 13 / 12 hours**, with Hebrew text round trips. No broad normalization or
UI change is made.

`scripts/test-postgres-image.py` additionally owns a unique, labeled **disposable named
volume** and container, with no network or exposed port. It accepts no database target.
It verifies Docker health, transaction rollback, 0700/70:70 data-directory permissions,
synthetic UTF-8/date/UTC/jsonb persistence across a restart of **that owned container
only**, and a `pg_dump`/`pg_restore` round trip into another disposable database.
Backup bytes stay in memory. Exact ownership labels gate cleanup, and only its own
container/volume are removed. This probe also runs in the Backend CI job.

The first backend attempt exposed a Phase 7C demo-scenario regression: some random
seeds selected a deputy to decide their own personal proposal. The approved independent
approver rule correctly rejected it. The demo tool now chooses that deputy's team
leader, preserving server enforcement. The integration fixture uses seed `7100001`
to always cover the former selection, checks every decision has a different actor,
and retains rollback/idempotency/data-preservation assertions. This fix was tested
only in disposable fixtures; no existing demo scenario was reapplied.

The final exclusive backend run passed **420/420 tests, zero failed/skipped**, including
all **27 Phase 7C security cases** and the new image/time-zone case. Release builds
had zero warnings/errors; clean migrations and pending-model validation passed. The
new image/time-zone case also passed **1/1** through the separate Testcontainers
fallback, proving that image reference can start directly. The final storage probe
passed with its disposable volume removed.

Earlier attempts remain part of the validation record: the first had **418 passes /
two failures** in demo-scenario helpers, corrected by the independent-approver selection
fix. The second had **418 passes / two failures** at the same instant in
`Cross_year_weeks_share_the_same_three_block_limit` and
`Delegated_availability_changes_stale_review_without_mutating_preliminary_snapshot`.
These were connection/HTTP-500 failures, including an Npgsql end-of-stream during
connection negotiation. The captured disposable-container state/logs showed no
restart, OOM kill or PostgreSQL crash; a root cause was not established. Both tests
passed in the final complete run without overlapping scanners/suites. No automatic
retry or weakened assertion was added. This is a local validation limitation to
monitor in CI, not evidence that a transport defect was fixed.

Final local results on the selected image/toolchain:

| Check | Result |
| --- | --- |
| Complete backend suite, including all 27 Phase 7C security cases | **420 passed; zero failed/skipped** |
| Separate Testcontainers image/time-zone compatibility path | **One passed; zero failed/skipped** |
| Complete frontend suite | **128 passed; zero failed/skipped** |
| Complete Phase 7B/7C Chromium suite, one worker | **31 passed; zero failed/skipped/flaky** |
| Browser isolation guards | **Nine checks passed**: one valid and eight unsafe targets, without connecting |
| Artifact-redaction safety tests | **Three passed** |
| Development-runner safety tests | **Seven passed** |
| Image storage probe | Passed: healthy startup, rollback, restart durability, backup/restore, permissions and cleanup |
| Backend/E2E-host Release builds and clean migrations/model check | Passed; zero build warnings/errors, no pending model changes |
| Frontend production build and E2E type checking | Passed |
| Compose image resolution, Actionlint, Python/Node/shell syntax and whitespace checks | Passed |
| NuGet/npm dependency audits; all-history/source Gitleaks | Zero advisory/secret findings |
| Selected image scans | ARM64 and AMD64: **25 High / one Critical**, same CVE/package set; three original findings fixed, zero introduced |

All full-suite figures are actual executed local results. The backend transport
failures above are disclosed despite the final passing run. CI now uses the same
image pin and includes the storage probe; its YAML passes Actionlint. **No remote CI
run was triggered or observed at the completion of the local Phase 7C.1 implementation.**
The subsequently reported scanner-installation failure is recorded in the follow-up below.

Reproduce from the repository root with the documented toolchain and Docker:

```sh
python3 scripts/test-postgres-image.py
python3 scripts/test-security.py --all
npm test --prefix frontend -- --maxWorkers=1
npm run build --prefix frontend
npm run typecheck:e2e --prefix frontend
npm run test:e2e:safety --prefix frontend
npm run test:e2e --prefix frontend
python3 scripts/test-dev.py
python3 scripts/security-audit.py --gitleaks /path/to/verified/gitleaks --trivy /path/to/verified/trivy
# Explicit cross-architecture scan (registry image; no AMD64 execution implied):
python3 scripts/security-audit.py --gitleaks /path/to/verified/gitleaks --trivy /path/to/verified/trivy --image-platform linux/amd64
```

The comparison's safe local JSON evidence is ignored under `TestResults/security/phase7c1/`,
including the original/candidate ARM64 reports, selected AMD64 report, raw index manifests,
canonical Go records and storage-probe result. `scripts/security-audit.py` now scans the
pinned image and optionally selects ARM64/AMD64 explicitly; scanner failure is not a clean
result. Image findings are still reported separately from the dependency/secret gate.
No credentials, raw browser captures, database dumps or employee data are committed.

### Persistent-environment verification and deployment requirements

The two protected containers, images, start times and mounts were recorded read-only
before testing. Demo: **60 users / 1,397 assignments / three publications / 29 absences**,
migration `20261106074419_SchedulingPriority`; development: **eight users / zero assignments /
zero publications**, migration `20261027224058_AdditionalSchedulingConstraints`.
Final read-only checks matched both baselines, including migration levels. Image IDs,
start times and exact volume mounts were unchanged and both containers were healthy.
No image-probe volumes or image/security/E2E-owned containers remained, and raw private
browser captures were removed. Neither persistent target was migrated, reseeded,
restarted, upgraded or used for compatibility payloads. Their old image exposure
therefore remains until an explicitly approved maintenance operation.

Before a pilot or any approved persistent-image rollout:

1. Keep PostgreSQL off public interfaces. The local Compose ports remain **loopback-only**
   (5432 development / 55432 demo); the storage probe has no network, and other disposable
   services bind random loopback ports. A real deployment needs a restricted private
   database network and trusted TLS/authentication when crossing hosts. Private networking
   reduces exposure but does not fix vulnerable libraries or authorize untrusted SQL.
2. Use a dedicated non-superuser runtime role with only required database/schema/table/
   sequence privileges. Separate migration/bootstrap ownership and DDL privileges;
   no runtime CREATEDB/CREATEROLE or disposable superuser credentials. The official image's
   `POSTGRES_USER` bootstrap role and public CI credentials are not a production role design.
3. Store generated production credentials in an approved secret store or protected files,
   with access restrictions and rotation. Never put them in Git, browser artifacts,
   reports or shell arguments. Retain SCRAM authentication and review `pg_hba.conf`.
4. Preserve exact volume identity and PostgreSQL ownership/permissions. The selected
   image retains UID/GID **70:70**, and its fresh data directory is **0700**. A distribution
   change would need separate ownership, locale, collation and extension planning.
   Do not run `down -v`, renew anonymous volumes, prune volumes or attach a valuable
   directory to an experimental image. The demo currently uses an anonymous volume;
   its identity must be recorded and deliberately preserved during any approved change.
5. Before touching a protected database, obtain explicit approval, create and verify a
   protected backup/restore target, review installed extensions and collation versions,
   plan downtime and rollback, and verify volume ownership and connection targets.
   PostgreSQL's [minor-update guidance](https://www.postgresql.org/docs/17/upgrading.html)
   preserves storage compatibility within major 17, but does not certify OS/ICU changes.
   The synthetic backup/restore probe is not a verified backup of real development/demo data.
6. Review official PostgreSQL/Alpine/gosu advisories and both architecture scans at least
   monthly and before each rollout; triage critical/high alerts promptly. Update the
   specific tag **and** digest in all consumers together, compare CVE/package sets,
   rerun compatibility/security suites and require review. Pinning must not become
   permanent patch deferral. Resolve or explicitly accept residual helper findings
   with the deployment owner; maintain no blanket scanner allowlist.
7. Complete the existing Phase 7C HTTPS/trusted-proxy configuration, production Secure
   `__Host-` cookies, protected persistent ASP.NET Data Protection keys, production
   secret management, backup monitoring and final-commit CI requirements. A fresh
   disposable volume does not validate production storage encryption, real disaster
   recovery, live TLS/proxy behavior or multi-instance key management.

### Updated recommendation

The selected image applies the available OS-package fixes with the smallest compatible
change. All raw residual findings remain visible. **Phase 7C is ready for PR review**:
the final local suites pass, the approved authorization fixes remain intact, and all
29 original findings have an explicit disposition. This is not pilot approval or a
production-security guarantee. The protected running databases intentionally remain
on the original image. Residual-helper review, an approved persistent-image rollout,
real CI (including monitoring the disclosed transport failures), and deployment
controls remain open acceptance requirements before a pilot.

### Follow-up — CI scanner download reliability

The user subsequently reported a failure in **Install checksum-verified Gitleaks**:
the release download returned HTTP **504** for the initial request and all three
retries, ending with curl exit 22. The scanner had not run; this was not a reported
secret finding, dependency finding or checksum mismatch. Official release metadata
confirmed the existing URL, version **8.30.1**, Linux x64 asset and pinned SHA-256.

The job now uses `scripts/install-gitleaks.py` and caches only the archive using
checksum-pinned `actions/cache` **v5.0.3**. Cache availability is optional and bounded
to two minutes; installation and the audit remain mandatory. A restored archive is
rehashed every time, then its executable version is checked before installation.
Corrupt cache bytes are discarded and downloaded again. Downloaded checksum mismatches
stop installation, and archive links are not extracted/executed.

Cold-cache downloads use bounded curl retries, connection/request/overall timeouts,
and HTTPS-only redirects. After a transport/HTTP failure, the installer tries the
same release asset through GitHub's official
[release-asset API](https://docs.github.com/en/rest/releases/assets#get-a-release-asset),
asset ID `378332058`, with `Accept: application/octet-stream`. This public endpoint
requires no token. Both paths retain the original SHA-256
`551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb`.
Neither a fallback to an unpinned version nor skipping the scanner is permitted.
If both official paths fail, the job fails without a clean security result.

Targeted validation: **nine offline installer tests passed**, including exhausted
HTTP-504 retries, timeout fallback, verified/corrupt caches, wrong checksums, both
endpoints failing, wrong executable versions and unsafe archive links. The actual
Linux archive was retrieved from the official API and its SHA-256 verified locally.
The final live helper also recovered from a primary-download connection failure via
that API, then reused its verified cache without another download. Linux executable
execution is not claimed on macOS. Workflow lint and Python syntax checks passed;
the real local NuGet/npm and redacted history/source checks reported zero findings.
These installer regressions now run in CI before installation.
Application/database code is unchanged by this follow-up; the full-suite results
above are the preceding Phase 7C.1 results. A successful run on the real Linux CI
runner remains required after the user publishes the local fix.

## Primary references

- [ASP.NET Core 10 antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [ASP.NET Core 10 CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
- [Microsoft .NET 10.0.14 servicing notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.14/10.0.14.md)
- [Official PostgreSQL image Dockerfile](https://github.com/docker-library/postgres/blob/master/17/alpine3.24/Dockerfile)
- [gosu source](https://github.com/tianon/gosu/blob/master/main.go)
- [PostgreSQL security advisories](https://www.postgresql.org/support/security/), [17.11 release notes](https://www.postgresql.org/docs/release/17.11/)
