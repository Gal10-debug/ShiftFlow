# Phase 8B: statistics and analytics

## Verified base and scope

Started from a clean working tree on `main`, commit
`33b169e07770bbb02526124c7764775c284e6585`, containing merged Phase 8A and Phase 4C.
A read-only `git ls-remote` confirmed the same remote head; no fast-forward was
necessary. Implementation belongs to `feature/phase-8b-statistics-analytics`.
No remote reference, publication, branch protection, CI configuration, solver
objective, dependency, database schema, or migration is changed.

## Authoritative data and service boundaries

`SchedulingAnalyticsService` is the authorized application entry point. It validates
periods, revalidates the current account/security stamp and role through `TeamService`,
and opens a repeatable-read transaction encompassing authorization, publication
selection, current memberships and aggregation. HTTP controllers add policy guards
and validate query keys. Future application tools must call this service with the
requesting principal; they must not call the pure calculators as an authorization
shortcut.

Schedule statistics come exclusively from `SchedulePublication.Snapshot`, joined to
its `SchedulePeriod`. `PublicationAccess.Visible` and `Latest` select one authorized
publication per month. No live `ShiftAssignment`, mutable shift definition, absence,
or availability row is used to reinterpret a published assignment. Identity and
membership reads supply current administrative display names, eligible zero-work
employees, and team groupings. Personal responses contain no workforce list,
assignment IDs, publisher identities, notes, or another worker's statistics.

`SchedulingAnalyticsCalculator` aggregates typed results. Personal duration, assignment
deduplication, category and day/night classification come from the existing
`PlannedWorkHoursCalculator`. Administrative processing partitions each snapshot
by employee once before calling that same calculator, avoiding a full roster rescan
for each worker. Frontend code formats integer minutes as Hebrew display hours and
sizes native meter charts; it does not reconstruct authoritative statistics.

## Definitions and monthly/yearly rules

- **Assigned shifts:** unique assignment IDs per employee and published calendar
  month, including manual work, legacy assignments, on-call memberships and evening
  availability. Conflicting duplicate records retain Phase 8A's conservative unknown
  duration. Empty staffing cells do not create assignments.
- **Planned minutes:** sum known integer elapsed UTC minutes of frozen intervals.
  Jerusalem DST boundaries remain those frozen at publication. The complete
  `totalPlannedMinutes` is null when any duration is unknown; `knownPlannedMinutes`
  and `unknownDurationAssignments` remain available. A complete zero is valid only
  alongside publication provenance; missing publication is a separate state.
- **Month ownership:** Phase 8A `ASSIGNMENT_DATE`, in `Asia/Jerusalem`. A night
  starting on the month's final date belongs entirely to that month. No split or
  import from the next month occurs.
- **Day/night:** frozen period metadata, excluding on-call and evening availability,
  exactly as Phase 8A. Other supported short ranges retain their frozen classification.
- **Weekend:** each assignment dated Friday or Saturday, including special duties.
  This counts assignments, not distinct weekends or days worked.
- **Special duties:** assignments classified by Phase 8A as on-call, availability,
  SIX_NINE, rehabilitation or early-end work. This is an overlapping descriptive
  count, not additional hours or distinct on-call allocations. The UI explicitly
  explains that Friday/Saturday on-call memberships count separately.
- **Shift type:** stable frozen definition ID; legacy work groups by frozen period.
  Annual counts, known minutes and unknown counts sum the monthly breakdowns.
  The first reporting month's frozen name labels a renamed type's annual group;
  identity and calculations do not depend on the label.
- **Monthly comparison:** previous calendar month's latest authorized publication,
  including December of the preceding year for January. Deltas are null unless
  both publications are available. January of year 2 has no supported previous
  reporting month. Known-minute deltas are explicitly subtotal
  comparisons; `bothDurationsComplete` states whether both totals are complete.
- **Yearly:** exactly twelve monthly buckets, including missing months with null
  publication. All totals are sums of the available monthly results. Missing months
  are not certified zero work and do not imply a complete reporting year. Reporting
  months are 1–12 and years 2–9998, matching Phase 8A.

## Special durations

On-call and evening availability have unknown work duration even if their constraint
intervals exist. Invalid, missing, conflicting or sub-minute intervals retain the
existing unknown behavior. No special-duty duration is estimated. Configured timed
rehabilitation, SIX_NINE and early-end work use frozen intervals, never editable
names. No represented on-call-duty count is summed into an annual allocation count.
No statistic represents attendance, payroll, overtime or actual worked hours.

## Publication/version handling

Default monthly and yearly views select the latest publication for each period,
provided that publication is visible. Preliminary and final publications are valid.
A hidden/deleted latest publication blocks historical fallback. An unavailable or
legacy null snapshot produces a missing month, rather than reading the draft.

Monthly requests may select an explicit positive version. It must be visible under
the same latest-publication gate; otherwise the response is 404. Each returned month
includes publication ID, version and type. A year is an explicit collection of
monthly versions; no two versions of one month are combined. Annual responses do
not accept a version selector because a single version number cannot identify a
year's twelve publications.

Correction mode and approved replacements/exchanges leave analytics on the previous
publication until explicit republication. New publications change the default view;
previous immutable snapshots remain selectable while authorized. A transaction
prevents a single response from combining pre-change and post-change membership or
visibility. Browser pages poll every five seconds while visible and clear results
on focus/visibility/offline revalidation or failed reads. No caching is introduced.

## Administrative workforce and coverage

Organization results include current active eligible employees with zero work plus
all people assigned in the selected snapshots, including historically assigned
inactive employees. Current administrators and team leaders do not enter the
zero-work eligibility cohort. Previously assigned identities still retain their
historical assignments. Employee names are current, with a historical fallback if
no current identity exists. An employee filter narrows this population.

Minimum/maximum/average shifts and the histogram use this explicitly reported
`includedEmployees` denominator. `scheduledEmployees` means employees with at least
one assignment. `averageShiftsPerScheduledEmployee` uses that separate denominator.
Empty populations return zero averages and an empty histogram. Night/weekend counts
appear for each employee. These are descriptive workload measures; availability,
qualification and eligibility prevent interpreting them as definitive fairness
scores. The scheduling solver is unchanged.

Team filters and summaries use **current membership**, explicitly labelled with
`CURRENT_MEMBERSHIP`. Membership history is not frozen in publication JSON; moving
a worker can reclassify historical workload between teams, without changing any
publication or organization/personal assignment total. Unaffiliated employees have
a separate summary. Team/employee filter combinations must match; nonexistent IDs
return 404 and contradictory combinations return 400.

Staffing positions come from applicable frozen snapshot cells excluding `ManualOnly`
rows, matching the operational shortage model. Duplicate cells and assignment IDs
are counted once; filled positions are capped at each cell's nonnegative required
capacity. Legacy work without a cell does not fill a different row's requirement.
Unfilled positions equal required minus filled. Coverage is `100 * filled / required`,
rounded to two decimal places, or null when required is zero. Annual coverage sums
positions before division, rather than averaging monthly percentages. Type totals
include required, filled and unfilled positions.

Requirements have no meaningful team/employee ownership. Coverage is therefore
available only for the organization view with available publications; filtered
responses return null coverage, and the UI explains why. No shortage is attributed
to a team. Missing months do not manufacture requirements from today's settings.

## API contracts and authorization

All routes are authenticated GETs with existing session, password-change, security
stamp, no-store and security middleware. They introduce no writes or CSRF exemptions.
Unknown query keys, repeated/blank values, malformed filters and invalid periods are
rejected.

| Endpoint | Optional filters | Response |
| --- | --- | --- |
| `/api/my-statistics/{year}/{month}` | `version` | `PersonalAnalytics` with previous-month comparison |
| `/api/my-statistics/{year}` | none | `PersonalAnalytics` with twelve months |
| `/api/admin/statistics/{year}/{month}` | `teamId`, `employeeId`, `version` | `AdminAnalytics` |
| `/api/admin/statistics/{year}` | `teamId`, `employeeId` | `AdminAnalytics` with twelve months |

Personal identity comes exclusively from the validated session. Personal endpoints
reject employee/team filters; employee-ID path variants have no route. The shared
`TeamService.SchedulingEmployee` authorization also backs the existing personal
schedule: employees/deputies may read their own statistics; administrators and
organizational team leaders are excluded from personal scheduling participation.

Administrative endpoints and service calls require the current ADMIN role. Team
leads/deputies have delegated availability and absence permissions, which do not
clearly authorize analytics; **delegated team analytics remains disabled**.
Coordinators receive no new administrative access. A coordinator with EMPLOYEE may
read their own statistics under the same participant rules. Administrators can
filter all organization employees/teams. No arbitrary SQL/filter expression or AI
integration is exposed. Error responses use existing generic API codes.

The strongly typed contracts expose totals, monthly publication provenance,
comparison, coverage, employee workloads, team summaries and explicit distribution
denominators. `isComplete` describes known durations within available publications,
not whether every calendar month has been published.

## Frontend

`/my-statistics` (סטטיסטיקות אישיות) provides month/year selection, summary cards,
day/night meters, monthly shift/hour trend meters with readable table values,
shift-type breakdown, previous-month comparison and incomplete-duration counts.
`/statistics` (סטטיסטיקות שיבוץ) adds organization/team/employee filters, staffing
coverage and shortages, workload distribution and employee/team comparisons.
Monthly views allow optional historical version selection. Both link from the
existing role-scoped sidebar. Query parameter `period=YYYY-MM` selects an initial
reporting month; otherwise the current Israel-local month is used.

Existing panels, tables, form fields and notices are reused. Native HTML meters,
labelled controls, table captions/header scopes, status/error states, RTL direction,
wrapped controls and scrollable tables preserve accessibility and mobile layout.
No chart dependency is added. Loading, unavailable publication, published zero,
request error/retry and partial-hour states are distinct. Late responses cannot
replace results for a newly selected filter or period.

## Performance and validation

Validation results are local; no GitHub Actions result is claimed.

The representative read fixture contains 24 scheduled employees, twelve immutable
monthly publications, 18,615 cells (51 rows per date) and 4,380 assignments, including
day and night shifts. It uses real Identity cookies, in-process HTTP/JSON reads,
.NET 10.0.403 with two reported processors, and an owned PostgreSQL 17 container
limited to two CPUs/512 MiB. Each endpoint has one warm-up plus twenty measured
requests. It is synthetic reporting data, not a solver benchmark or a production SLO.

| Endpoint | Median (ms) | Maximum (ms) |
| --- | ---: | ---: |
| Personal monthly | 13.866 | 23.801 |
| Personal yearly | 53.909 | 93.331 |
| Administrator monthly | 11.048 | 12.622 |
| Administrator yearly | 65.223 | 114.044 |

The earlier one-employee exploratory measurement is retained separately; these
samples are not a before/after performance claim. Raw measurements are under ignored
`TestResults/phase8b/analytics-latency-final.json`.

- Release backend build: zero warnings/errors; EF reports no pending model changes.
- Full PostgreSQL correctness suite: **486 passed**, zero failed/skipped, **193 s**.
  A final targeted run after query validation, deterministic month ordering,
  comparison and workload-fixture refinements: **42 passed**, including all **15 new
  analytics cases** and **27 security cases**, zero failed/skipped, **37 s**. The
  targeted build initially caught an xUnit analyzer finding; its assertion was
  corrected before the successful final run. A subsequent **14/14** analytics
  rerun verifies the lower supported year and blank-query guards, excluding the
  already measured latency case.
- Frontend: **152 passed**, including **12 new statistics tests**. Production
  TypeScript/Vite build and E2E TypeScript validation pass. Final browser bundle is
  approximately 121.27 kB gzip JavaScript and 4.57 kB gzip CSS; no new dependency.
- Playwright workflows: **36 passed**, zero skipped/flaky, **75.693 s**. The Production
  browser performance/owned-failure recovery suite also passes **2/2**, **43.243 s**.
  The final statistics-only browser rerun passes **3/3**, **5.841 s**, after the
  early-year label refinement.
- Both local Production application/proxy images build. The Production pilot harness
  passes **15 checks**, including HTTPS, migrations/bootstrap into an owned clean
  database, scheduling/publication/exchange, authorization/CSRF, key continuity,
  owned-database restart/outage, Hebrew browser layout, revoked copied cookies and
  backup/restore to a separate owned database. Cleanup is PASS. This local package
  validation is not a hosted deployment or pilot-readiness approval.
- PostgreSQL image probe passes health, migration rollback, owned-volume durability,
  backup/restore and private permissions checks.
- Helper suites: **7** development-script, **20** pilot guard, **2** timing parser,
  **9** scanner-installer and **4** E2E artifact-safety tests pass. `actionlint` passes.
- Audit: **zero** NuGet/npm advisories and **zero** Gitleaks findings in history and
  current source. Existing CI and its performance optimizations remain unchanged.

All database-writing runs used guarded isolated harnesses and newly owned synthetic
resources. Persistent development/demo databases were never connected to, migrated,
reset, reseeded or edited. No persistent service/volume was restarted or deleted.
No existing persistent publication was changed. No schema migration is introduced.
The unchanged solver benchmark fact passes once, executing exactly six experiments
(normal and severe shortage at 30/60/120-second configured budgets), with **352 s**
reported test duration. All normal trials are OPTIMAL with 238 critical and 56 normal
assignments and `PROVEN_TARGET_MET`; all shortage trials are FEASIBLE with 176 critical
assignments. The existing shortage-optimality proof limitation remains. This phase
changes no solver logic or objective. Raw diagnostics are under ignored
`TestResults/phase8b/solver/` and the benchmark TRX under `TestResults/phase8b/benchmarks/`.

Final read-only container inspection confirms the original development/demo IDs
`62f7ee472643` and `f00454a53922`, with unchanged November 5 start times and zero
restarts. Only newly owned test resources were cleaned up. `main` and `origin/main`
remain at the verified base. The implementation is committed locally on the dedicated
feature branch; no remote branches are updated and no pull request is created.

The retrieval is bounded to one year (twelve snapshots) or one month plus the previous
month for personal comparisons. One query retrieves the necessary publications;
admin identity/membership queries are bounded population reads, without per-employee
round trips. Rows are ordered by reporting month for deterministic annual labels.
There are no live roster loads, all-history scans, or application caches. Snapshot
JSON is still materialized per requested month; unusually large organizations may
benefit from a future authorized projection after measurement.

## Automated coverage

New backend cases cover monthly/yearly sums, partial years, day/night/weekend,
special duties, mixed unknown durations, duplicates/conflicting durations, weighted
coverage and shortages, empty periods, workload denominators, draft/initial/final
visibility, immutable historical replacements, approved exchanges awaiting
republication, team/employee filters, invalid periods and unauthorized identities.
The existing admin denial matrix now includes both analytics routes; copied revoked
session tests include all four analytics routes.

Frontend tests cover summaries, meters, provenance, monthly/yearly navigation,
team/employee filters, loading, errors/retry, missing publications, published zero,
incomplete hours/comparisons, historical versions, denied revalidation and Hebrew
RTL. Playwright adds personal monthly/yearly mobile views, unauthorized access,
organization coverage, team filters and leader/deputy denial. The existing exchange
workflow now checks statistics before/after republication and in historical versions.

## Phase 8C and limitations

Expose narrowly scoped future tools that wrap `SchedulingAnalyticsService.Personal`
and `Administrative` with the authenticated principal. Return the typed results and
publication IDs; let language generation explain them without recomputing authoritative
statistics, bypassing authorization, or accepting SQL. Select the maximum monthly
assignment count from the already authorized month results when answering trend
questions. Use the existing coverage and workload distributions for shortage and
night-allocation questions. Do not grant AI its own broader identity.

Team membership and display names are current rather than frozen historical facts.
Missing published months are explicit. On-call/availability hours still need a
separate business policy. Multi-month historical selection would require an explicit
version-per-period contract, rather than one annual version number. Current snapshot
JSON reads are suitable for the measured small-workforce fixture; results do not
establish production network latency or a large-organization SLO. Descriptive workload
metrics do not normalize for availability, qualifications or eligibility. No cache,
migration or historical data backfill is introduced.
