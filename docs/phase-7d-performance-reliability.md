# Phase 7D — Performance, reliability and CI optimization

## Baseline protocol (recorded before optimization)

Base: `543184fd172937dc1bbf23846aa6bd81ca84c9fe`, verified against remote `main` with read-only `git ls-remote`. Phase 4C, 7A, 7B and 7C/7C.1 are present. Branch: `feature/phase-7d-performance-reliability`. No remote state changes are authorized.

The first full backend measurement uses the unmodified Release binaries from this base, .NET SDK 10.0.403/runtime 10.0.14, macOS arm64, `DOTNET_PROCESSOR_COUNT=2`, and one freshly created PostgreSQL container limited to two CPUs and 512 MiB. Docker has approximately 3.82 GiB allocated. The container binds a random loopback port, uses tmpfs and the unchanged reviewed PostgreSQL image/digest. Every integration test creates its own database within that owned service; no persistent connection strings are accepted by the runner. Existing development/demo containers are not stopped, restarted, migrated, seeded or queried.

Command (build first with the SDK pinned in `global.json`):

```sh
dotnet build ShiftManager.slnx -c Release --no-restore --warnaserror
python3 scripts/performance.py --suite all --output TestResults/phase7d/baseline-full
```

TRX durations exclude asynchronous xUnit fixture initialization/disposal, verified by the connection-cycle probe described below. Explicit lifecycle timings must be assessed separately. Class and assembly totals are sums, not wall time: other classes can overlap. The solver benchmark collection disables parallelization and runs after other collections; its six cases are sequential inside one fact. Before optimizing CI, observed normal-case trials took 16.87, 17.11 and 17.02 seconds for the configured 30/60/120-second budgets, while severe-shortage trials used their full 30- and 60-second budgets. The 120-second severe trial and full-run summary were still pending when this protocol was recorded. Source budget totals (420 seconds) must not be reported as measured elapsed time.

No production solver budget, hard constraints, objective hierarchy, authentication policy or database schema is changed to obtain this baseline. New instrumentation/test harness code is not a runtime optimization. Final measured results and limitations follow below after validation.

The first run passed 420/420 tests in 471.628 seconds. A subsequent full solution restore exposed stale local dependency assets in the initial build. Therefore, a second baseline was launched from a temporary `git archive` of the verified base, restored and built independently with the pinned SDK/dependencies. The initial run is retained as an exploratory measurement, not silently treated as an equivalent dependency-controlled comparison. The clean baseline encountered one 30-second HTTP 500 during security generation polling; that failure is retained for investigation rather than retried invisibly.

## Required CI organization

The existing **Backend** check becomes a fail-closed aggregate of **Backend correctness** and **Scheduling benchmarks**. Both run for every relevant pull request targeting `main` and every push to `main`, using the existing workflow triggers. There are no path filters or optional benchmark outcomes. The correctness command excludes only `Category=SchedulingBenchmark`; all existing correctness, migration, authorization and session tests remain enabled against the disposable service.

The benchmark matrix runs budgets 30, 60 and 120 seconds, with at most two matrix jobs at once. Every job executes both normal and severe-shortage scenarios at its assigned unchanged budget, checks that exactly those two trials were reported, and publishes objective values, bounds, proof flags and fairness results. Across the matrix, each of the original six experiments runs exactly once. Benchmarks require no PostgreSQL service. Their isolation avoids competing with other test classes for solver CPU. `fail-fast: false` preserves diagnostics from the other budgets when a trial fails.

The aggregate uses `if: always()` and explicitly requires both dependencies to be `success`; failures, cancellation and skipped dependencies cannot pass it. Its display name remains **Backend**, preserving the existing required-check entry. No branch protection or default-branch settings are changed. The existing **Frontend**, **Browser workflows**, and **Dependency and secret checks** jobs remain. The browser job additionally runs the bounded production-bundle pilot and owned database-outage tests after the existing E2E suite. Separate sanitized directories preserve both reports.

TRX bodies and raw application logs are not uploaded. The new timing summary exports only test/class/assembly names, outcomes, counts and durations. Solver reports contain deterministic synthetic employee identities. Existing artifact redaction and least-privilege `contents: read` remain in force.

This rearrangement reduces the expected critical path by overlapping independent required suites. It does not reduce solver limits, remove scenarios or establish a measured GitHub Actions speedup. Hosted queue time, architecture, runner CPU allocation, build overhead and simultaneous jobs can change the outcome. The reported historical 627-second test step is user-supplied context; this phase has not yet run remotely.

## Reproducible commands and safety boundaries

Run local heavy suites sequentially on the development machine:

The measured runs selected the matching preinstalled SDK with `PATH=/private/tmp/shiftflow-dotnet-10.0.403:$PATH`; the system-default 10.0.301 SDK does not satisfy `global.json`. Use an installed matching SDK or the matching local SDK path before these commands. The repository SDK pin was preserved.

```sh
dotnet restore ShiftManager.slnx
dotnet tool restore
dotnet build ShiftManager.slnx -c Release --no-restore --warnaserror
python3 scripts/performance.py --suite correctness --output TestResults/phase7d/correctness
python3 scripts/performance.py --suite benchmarks --output TestResults/phase7d/benchmarks
python3 scripts/summarize-performance.py --trx 'TestResults/phase7d/correctness/*.trx' --output TestResults/phase7d/timing.json
npm run test:performance --prefix frontend
```

For the full or correctness suite, the backend runner creates one container with a unique ownership label, a fresh tmpfs, two CPUs, 512 MiB RAM and a random loopback port. Benchmark-only runs need no database or Docker resource. The runner overrides inherited service connection settings and never accepts an external database target. Each integration test owns a separate newly created database. Output must be a new directory under ignored `TestResults`; raw logs are private, and cleanup verifies the exact run label. A timeout terminates the owned test process group and removes only the owned container and its anonymous resources. The PostgreSQL image remains:

```text
postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac
```

The pilot reuses the Phase 7B/7C executable, empty-database claim, token-protected controls, deterministic clock and synthetic accounts. It creates 29 accounts, 20 independent load-user sessions, 51 rows, 1,581 January board cells, 220 weekday assignments and 100 historical approved absences. January is published; February is a separate open DRAFT availability period. Write tests use independent users and fresh absence dates. Exactly one March generation preview runs at the unchanged production 30-second budget; previews are not saved, and monthly solves are never loaded concurrently.

`npm run test:performance` builds production JavaScript and serves it from the isolated Kestrel host. The host's existing Development environment is retained for HTTP test cookies and fixture controls; this is not a TLS or production-deployment benchmark. Set `SHIFTFLOW_PERFORMANCE_BUILD=development` to compare Vite/StrictMode behavior. Frontend timings include network transfer, React rendering, layout and two animation frames. The first board navigation loads the page; subsequent iterations use the actual month selector within the same application instance. Browser metrics include heap, DOM nodes, layout/style counts and script/task duration, with a final explicit GC sample to distinguish live memory from deferred collection.

Optional bounded controls:

```sh
SHIFTFLOW_PERFORMANCE_SECONDS=3 SHIFTFLOW_PERFORMANCE_CONCURRENCY=1,5,10,20 SHIFTFLOW_PERFORMANCE_NAVIGATIONS=10 npm run test:performance --prefix frontend
SCHEDULING_BENCHMARK_BUDGETS=30 python3 scripts/performance.py --suite benchmarks --output TestResults/phase7d/budget30
```

Duration is 1–10 seconds per read wave, concurrency is a unique subset of 1/5/10/20, navigation count is 3–20, and each virtual user waits 25 ms between requests. Login, availability submission, absence creation/approval and administrative correction are isolated write samples, not unrestricted write loops. Single publication and exchange lifecycle timings are exploratory only. Body transfer is included; mutation timings include the CSRF preflight. Endpoint-first samples follow fixture initialization and login, so they are not cold process-start measurements. Fresh backend processes/databases provide a separate startup perspective through fixture timing.

p95 is omitted below 100 samples. Even larger short, autocorrelated loopback samples do not justify production SLOs or hardware-independent thresholds. No arbitrary latency pass/fail threshold is introduced. CI fails on correctness, unexpected HTTP errors, missing required benchmark reports, failed invariants or recovery failures; timing artifacts provide the baseline for a future measured regression policy.

## Absence list query baseline (before the targeted runtime change)

The instrumented pilot captured exactly **29 EF reader commands per personal absence GET** for five returned historical requests, at every concurrency level. At concurrency 1, 24 requests had p50 18.251 ms and maximum 22.401 ms; concurrency 5 produced 105 requests with p50 21.697 ms and exploratory p95 59.462 ms. Later 10/20-user waves were materially slower (96.092/233.044 ms p50), demonstrating local run variability; these are not SLOs. Each response transferred 2,766 bytes. The raw aggregate remains in `TestResults/phase7d/api-browser-before-absence-fix.json`.

Inspection of the measured path found `HasRole(id, "ADMIN")` inside the serialization loop, repeating the same role lookup for every returned request. The whole list already executes in one repeatable-read transaction, so these role reads see the same snapshot. Evaluating that flag once per nonempty response can remove exactly four redundant commands for this five-row fixture, while retaining every authorization predicate and conflict-visibility query. No cross-request role cache is proposed. A real PostgreSQL regression tests both personal and administrator lists as the returned request count grows from one to three; it is executed before applying the correction.

Both regression cases failed against the original implementation: the three-row reads made three and four counted administrator-role lookups respectively. The correction hoists only that visibility flag out of the loop and preserves the empty-list behavior, repeatable-read transaction, team/employee predicates and publication-visibility checks. The remaining per-row response queries are retained; batching those safely is a separate potential improvement if longer-history measurements justify it.

## Reliability and recovery coverage

The two new backend regressions use fresh databases and the real application, Identity, transactions and PostgreSQL:

- A held scheduling advisory lock makes an explicitly test-only one-second command timeout fail a correction with HTTP 500/`INTERNAL_ERROR`. The draft token, assignment count and audit history remain unchanged. After releasing the lock, an explicit retry succeeds once; repeating the old version returns 409 and leaves exactly one assignment and audit entry.
- A test-owned PostgreSQL trigger pauses after the audit INSERT, inside the real correction transaction. The test waits for that exact writer and cancels only its backend in the current disposable database. The assignment and audit both roll back. After removing the trigger, the same correction succeeds. Disposing and recreating the application host against the same owned database preserves the saved assignment and audit.

The browser reliability case verifies its exact run UUID and container label before pausing PostgreSQL. A bounded database outage returns a generic HTTP 500 without leaking database details; an interrupted read times out. A `finally` block unpauses the owned container. Requests then succeed again and complete assignment/absence/publication-hash state matches the pre-outage snapshot. No development/demo service is interrupted. A tmpfs container restart is deliberately not used as a persistence test: tmpfs data is disposable across container restarts. The existing Phase 7C.1 owned-volume durability probe and database image tests remain in CI.

Existing regressions retain simultaneous correction/version conflicts, one-winner publication, concurrent absence decisions, replacement conflicts across exchange approvals, duplicate exchange requests, stale availability submissions, immutable snapshots and independent absence approval. They also retain team substitution checks, publication visibility, malformed input/CSRF handling and session revocation. No automatic retry policy is introduced. Versioned writes require rereading state after ambiguous outcomes; unversioned create operations should not be blindly replayed. Idempotency keys for new-create workflows remain a product/contract decision, not an implicit change in this phase.

The clean base run's single generation-polling HTTP 500 is an unresolved observation, not a confirmed diagnosis. It did not recur in the subsequent full correctness run or the separate 27-security-test rerun. The profiling configuration now records exception type chains, method names and thread-pool counts for unexpected controller failures; exception messages, SQL, request bodies and credentials are excluded. The full failed base report is retained locally. No test failure is hidden or converted into a pass by retrying it.

## Fixture connection churn and timing semantics

The later full unpooled correctness run took **934.046 seconds**, with 423/424 passing and a PostgreSQL read timeout during `Notes_protect_generated_assignments_and_legacy_notes_are_published`. That failure remains recorded. It occurred in account/role lookup, outside the optimized absence-list loop. Fixture timings also grew substantially, so the earlier 215.277-second correctness run cannot be treated as a stable machine baseline.

An explicit connection probe reproduced a fixture inefficiency: the service-backed `TestDatabase` set `Pooling=false`. Fifty sequential open/query/close cycles took **1.552210 seconds** and opened **50 distinct server backends**. The regression failed before the correction. With pooling enabled and bounded to eight connections **per unique test database**, the same probe took **0.022228 seconds**, reused one backend, and passed the isolation/cleanup checks. This is a focused approximately 70-fold connection-cycle improvement, not a measured 70-fold suite or hosted-CI improvement.

Every test still receives a fresh database, host, users and migrations. No database template, mutable fixture or account is shared. Disposal clears only that exact database connection pool, then drops the same owned database with `FORCE`; it never calls `ClearAllPools`. The regression opens another disposable database, disposes it and verifies that the first database's backend remains usable and unchanged. Five focused connection, absence and failure-recovery cases pass together. Migrations, rollback-to-zero and concurrent writer regressions remain in the full validation suite.

The probe also establishes timing semantics: its TRX duration was **1.563652 seconds**, nearly identical to its explicitly timed **1.552210-second** method body, despite separate earlier database creation and migration. Async fixture initialization/disposal is excluded from these xUnit TRX durations. This explains why sorting TRX alone misses substantial setup cost. The report parser and CI summary now explicitly distinguish test-body/class totals, fixture stage totals and process wall time. Fixture disposal is recorded separately as well. Older raw exploratory JSON retains its original timing note; this corrected interpretation supersedes that note.

The [xUnit 2.9.3 invoker source](https://github.com/xunit/xunit/blob/v2-2.9.3/src/xunit.execution/Sdk/Frameworks/Runners/TestInvoker.cs) confirms that async setup and disposal sit outside its execution timer. Synchronous construction/disposal can still be timed. Explicit fixture-stage totals are diagnostic, not automatically additive to TRX: tests that create another fixture inside their method already include that nested work in their method duration, and parallel classes overlap.

Connection churn is a confirmed inefficiency. It is not established that pooling alone eliminates every observed transient timeout; the final full run and hosted follow-up must validate that separately. No production database pooling setting or timeout is changed by this fixture correction.

## Confirmed artifact-processing bottleneck

A failed trace-publication process was sampled while spending CPU repeatedly JSON-escaping and sorting the secret set. The sanitizer did that work for every trace string. Caching the ordered raw/escaped replacements, and invalidating the cache whenever discovery adds a new secret, preserves the redaction order and semantics.

A five-trial synthetic microbenchmark used 500 synthetic secrets and 2,000 ordinary trace strings per trial. Median runtime changed from **0.300157 s to 0.021544 s** (about 13.9 times faster). These are helper timings, not whole-workflow or GitHub Actions speedups. Four artifact-safety tests pass, including new escaped-secret/cache-invalidation coverage. Fifty additional nested/escaped documents produced output identical to the base sanitizer. Full failure-artifact publication still depends on trace size and the number of distinct secrets.

## Final measured results

Machine-readable observations are committed in [phase-7d-performance-results.json](phase-7d-performance-results.json). Raw TRX/logs and sanitized browser reports remain under ignored `TestResults/phase7d/`. All timings below are local, not GitHub-hosted measurements.

| Run | Wall seconds | Passed / total | Notes |
| --- | ---: | ---: | --- |
| Exploratory full base | 471.628 | 420 / 420 | Cached dependency assets; all tests including six sequential trials |
| Clean pinned full base | 510.491 | 419 / 420 | One generation-polling failure; retained |
| Initial split correctness | 215.277 | 422 / 422 | Before runtime absence fix/pooling |
| Slow unpooled correctness | 934.046 | 423 / 424 | One account/role PostgreSQL timeout; retained |
| Final pooled correctness | 173.733 | 425 / 425 | Fresh databases; all correctness/security checks |

The final required correctness suite passes **425/425 with zero skips** in **173.733 seconds**. The six long experiments remain required and were validated separately in their exact CI partitions: 49.201, 77.758 and 139.167 seconds for 30/60/120-second budget jobs, each containing two scenarios. Suite composition differs from the full-base run; this is not a claim that the same full suite now executes serially in 174 seconds. The matrix overlaps its independent required experiments, with at most two jobs active.

The clean base benchmark fact took 262.070 seconds; its sixteen monthly stress cases totaled 126.876 seconds in TRX. The final seventeen-case stress class totals 134.917 seconds. Additional major base test bodies were the full demo workflow (38.517 s), failed security-poll case (30.698 s), password-change case (29.003 s), and demo smoke workflow (8.899 s). The latter long password-change observation passed; it still demonstrates timing variability. The final slowest nonbenchmark body is the full demo workflow at 37.769 s. All per-test/class/assembly sums and slowest twenty cases are retained in JSON.

| Fixture stage | Unpooled p50 ms | Pooled p50 ms | Unpooled summed s | Pooled summed s |
| --- | ---: | ---: | ---: | ---: |
| database-create | 23.787 | 5.442 | 5.432 | 1.416 |
| scheduling-host-start | 222.305 | 58.517 | 36.343 | 10.095 |
| scheduling-migrations | 690.557 | 174.406 | 119.615 | 22.885 |
| scheduling-account-seed | 786.002 | 135.973 | 117.860 | 17.487 |

Final database disposal: 190 samples, p50 2.198 ms. The final full-suite connection probe reused one backend for fifty cycles in 0.010329 seconds. The slow and final runs contain different fixture counts after new regressions; lifecycle sums are descriptive, not a controlled statistical speedup estimate.

### Solver quality

| Scenario | Budget s | Solve s | Critical / High / Normal filled | Status | Proven stages | Critical value / bound |
| --- | ---: | ---: | --- | --- | ---: | --- |
| normal | 30 | 17.463 | 238 / 0 / 56 | OPTIMAL | 6 / 6 | 238 / 238 |
| severe-shortage | 30 | 30.017 | 176 / 0 / 0 | FEASIBLE | 0 / 6 | 176 / 198 |
| normal | 60 | 16.970 | 238 / 0 / 56 | OPTIMAL | 6 / 6 | 238 / 238 |
| severe-shortage | 60 | 60.019 | 176 / 0 / 0 | FEASIBLE | 0 / 6 | 176 / 198 |
| normal | 120 | 17.131 | 238 / 0 / 56 | OPTIMAL | 6 / 6 | 238 / 238 |
| severe-shortage | 120 | 120.042 | 176 / 0 / 0 | FEASIBLE | 0 / 6 | 176 / 198 |

Normal staffing fills all 294 places with all six stages proven; both qualification cohorts receive five to six assignments, and the fairness target is proven met. Severe shortage fills 176 critical places with bound 198 and leaves later objectives unrun. It remains FEASIBLE even at 120 seconds; this is not a proven optimum or a fairness acceptance result. Model building was approximately 8–88 ms in the long trials, so it was not their primary cost. The production budget remains 30 seconds and search workers remain one.

All seventeen existing/new monthly stress cases pass independent hard-constraint validation at the unchanged existing eight-second stress budget. They cover normal/moderate/severe shortages, absences, scarce leads, protected work, restrictions and calendar boundaries. The new mixed-priority case verifies all three coverage tiers. Full stage values/bounds, timing, coverage, fairness cohort summaries and violation arrays are in JSON. Zero reported peak-memory values on this Mac are marked unavailable; they do not mean zero memory use.

### API and database

| Operation | Users | Samples | p50 ms | p95 ms (N ≥ 100) | Maximum ms | Requests/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| personal-schedule | 1 | 20 | 23.843 | — | 35.792 | 19.91 |
| personal-schedule | 20 | 158 | 103.854 | 151.080 | 152.218 | 147.41 |
| availability-read | 1 | 31 | 5.855 | — | 14.828 | 30.23 |
| availability-read | 20 | 488 | 14.252 | 17.910 | 21.378 | 474.46 |
| absence-read | 1 | 24 | 15.382 | — | 25.153 | 23.45 |
| absence-read | 20 | 388 | 27.547 | 48.382 | 52.133 | 363.18 |
| monthly-board | 1 | 25 | 15.888 | — | 21.559 | 24.03 |
| monthly-board | 20 | 208 | 77.259 | 86.297 | 88.944 | 200.88 |
| availability-submit | 1 | 1 | 46.992 | — | 46.992 | 21.20 |
| absence-create | 1 | 1 | 23.659 | — | 23.659 | 42.05 |
| absence-approve | 1 | 1 | 34.661 | — | 34.661 | 28.75 |
| availability-submit | 20 | 20 | 68.791 | — | 112.422 | 174.58 |
| absence-create | 20 | 20 | 66.601 | — | 116.062 | 168.56 |
| absence-approve | 20 | 20 | 151.099 | — | 291.020 | 68.16 |
| exchange-read | 1 | 22 | 19.384 | — | 24.809 | 21.71 |
| exchange-read | 20 | 528 | 11.176 | 22.479 | 28.055 | 512.93 |

All four concurrency levels (1/5/10/20) and every endpoint-first observation are retained in JSON. There were zero unexpected HTTP errors. Think time is included in throughput, so these are not maximum-capacity rates. Login, team-scoped member reads, publication history, administrative correction, publication and complete exchange create/consent/approval/republication are also measured. Mutation samples are too few for reliable tail percentiles.

The five-row absence list now issues exactly **25 reader commands per GET**, versus **29 before**, at every concurrency level (13.8% fewer). Concurrency-1 p50 changed from 18.251 to 15.383 ms in these runs; higher-concurrency timings varied much more. Only the command-count reduction is a strong causal result; the short local latency differences are not a hardware-independent claim. All visibility and authorization regressions remain enforced.

One real monthly generation preview completed in 4.151 seconds end-to-end, status OPTIMAL, 6 proven stages. It ran alone and was canceled after inspection without saving assignments. Its summary and stage diagnostics are retained in JSON.

`EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` captured 48 business-query shapes after `ANALYZE`, on the owned pilot database only. Execution p50 was 0.014 ms and maximum 0.247 ms. Captured plans cover board/cell/assignment reads, availability, absence conflicts, publications, exchanges and team membership. At this scale, index additions or schema migrations were not justified. Small-table sequential scans are not automatically defects. No authorization predicate was removed and no migration rollout is needed.

The after-workload connection snapshot was 20; it is not a peak count. Kestrel working set was 498.2 MiB. Per-wave backend CPU seconds, working set, response bytes and reader-command counts are recorded. Peak working set is unavailable on this runtime. Instrumentation and response transfer add overhead; tmpfs and warm cache underrepresent durable-storage latency.

### Frontend

The production bundle loaded and rendered the first 51-row monthly board in 162.289 ms. Five subsequent in-app month changes ranged 91.539–127.881 ms, median 106.857 ms. No p95 is reported from six navigations. Each month change issued one board request plus one review/history/issues request each; there were no duplicate production fetches for the same transition. Initial navigation intentionally fetches a week and then a month when the view selector changes. Development StrictMode duplicated mount effects, so development and production samples are explicitly separate.

After explicit GC, heap was 10.92 MiB with 12,984 DOM nodes. This short run does not establish long-session leak freedom. Hebrew RTL and 51-row/date-boundary assertions pass. The production bundle remains about 116.65 kB gzip JavaScript and 4.42 kB gzip CSS. No frontend framework, rendering policy or speculative memoization was introduced.

## Final validation and isolation verification

- Release build: zero warnings/errors; production frontend build and E2E TypeScript checks pass.
- Backend correctness/security: **425 passed, zero skipped/failed**, 173.733 seconds.
- Required scheduling experiments: **six passed** across all three unchanged-budget partitions.
- Frontend unit tests: **128 passed**.
- Original browser workflows/security: **31 passed**, zero skipped/flaky, 66.155 seconds.
- Production pilot and database-outage/recovery tests: **two passed**, zero skipped/flaky, 41.115 seconds. Every benchmark endpoint returned its expected successful status; the failure test asserted its deliberate timeout/500 separately.
- Timing-report tests: **two passed**; artifact-safety tests: **four passed**.
- Model/migration consistency: no pending model changes, checked using an explicitly unused connection without migrating any existing database.
- Workflow syntax: `actionlint` passes. Dependency and secret audits report zero findings. No remote CI pass or speedup is claimed.

After cleanup, Docker reports **zero** containers carrying either this phase's performance-run label or the E2E-run label. The original development and demo containers retain IDs beginning `62f7ee472643` and `f00454a53922`, start timestamps on November 5, and restart counts of zero. No harness connected to their database ports, applied migrations, seeded accounts or queried persistent record fingerprints. Isolation is established by newly created, labeled containers, random loopback bindings, strict database names/credentials, the E2E empty-database ownership claim and scoped cleanup—not by Git branch names. No persistent volume was removed.

`main` and `origin/main` remain at the verified base `543184fd172937dc1bbf23846aa6bd81ca84c9fe`. Work is committed locally on the requested feature branch. No remote branch, pull request, branch-protection rule or repository setting is changed.

## Remaining risks and Phase 7E recommendation

**Recommendation: continue stabilization before pilot acceptance.** The local Phase 7D implementation and final validation are ready for review, but this does not establish production readiness.

1. Obtain a GitHub-hosted run after the user authorizes pushing/opening a PR. Verify aggregate **Backend** still gates acceptance, every original experiment runs exactly once, and the complete job wall time improves. The under-five-minute target is aspirational until that hosted measurement exists. Local matrix partition timing is not a hosted result.
2. Retain and investigate any recurrence of the two baseline/unpooled PostgreSQL timeouts. The final pooled suite and browser recovery checks pass, but a single green full run does not prove permanent elimination. Use the new failure/fixture diagnostics; do not hide failures with retries or larger solver budgets.
3. Severe staffing shortage still has an unproven critical optimum (176 incumbent / 198 bound), even at 120 seconds. Keep the production 30-second budget and objective hierarchy until explicit scheduling-policy decisions are approved. Normal staffing has proven coverage/fairness; shortage outcomes still need operational review.
4. Run longer pilot-like waves, browser navigation soaks, WAN/TLS measurements and durable-storage trials on representative deployment hardware. The current tmpfs/loopback runs are deliberately bounded. The two-second test-only database connection/command settings used for the browser fault scenario also apply to this pilot host and are not production defaults or supported latency SLOs.
5. Track large immutable-publication payloads, remaining per-row absence response queries, high-history admin/exchange operations and global writer-lock contention as the dataset grows. The measured query plans do not justify a new index at the current scale. Preserve publication visibility, team filters and immediate session revocation when considering batching or caching.
6. Define retry/idempotency behavior for unversioned create operations before adding automatic retries. Current versioned correction/decision/publication paths enforce one-winner semantics and rollback; ambiguous create outcomes still require reconciliation.
7. Establish performance thresholds only after repeated controlled measurements and hosted history. Preserve the JSON artifacts and record runner, dataset, load, errors, proof bounds and fixture costs with each comparison.
