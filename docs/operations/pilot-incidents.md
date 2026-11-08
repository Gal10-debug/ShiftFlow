# Pilot troubleshooting and incident response

The operations owner, alternate contact, support hours and notification route are deployment decisions. No monitoring service or outbound notification is configured by this phase. Before live use, assign someone to check readiness, disk space, certificate expiry, backup freshness and bounded log storage daily and after changes. Agree thresholds and how an alert reaches that person.

## Distinguishing failures

| Symptom | Safe diagnostic / action |
| --- | --- |
| Proxy unavailable or certificate rejected | Check owned proxy/container status, exact hostname, certificate expiry/chain, bind address and firewall. Do not disable client TLS validation. |
| App liveness fails | On the ownership-verified app ID, run `docker exec <id> dotnet ShiftManager.dll --healthcheck live`. Inspect bounded JSON logs, process state and memory limit. A restart is allowed only for the verified pilot service under an approved incident/change record. |
| Liveness passes, readiness fails / API 503 | Run `--healthcheck ready`; check owned PostgreSQL health, runtime credential file, correct database/role and `shiftflow_ops.identity`. Readiness enforces a two-second dependency timeout. It returns only Healthy/Unhealthy, never connection details. Do not solve this by pointing at a development database or granting owner access to runtime. |
| Startup refuses configuration | Fixed safe startup diagnostics identify a setting category. Check Production/PILOT_MODE, ID/database pair, exact trusted proxy, absolute secret paths, permissions, certificate validity and pool limit. No default secret fallback exists. |
| 401 / required password change / 403 | Check session age (fixed eight-hour ticket), account active state, role, first-login password change and security-stamp changes. A restored database intentionally invalidates all sessions. Confirm the user is entitled before changing access. |
| 400 CSRF / 429 / lockout | Reload the trusted same-origin page and obtain a fresh CSRF token; honor rate-limit/lockout policy. Do not relax cookies, enable permissive CORS or share accounts. |
| Scheduling error | Find the server request ID and generation status/diagnostics. Distinguish worker failure/interruption from a feasible result with shortages or unproven optimization. Review constraints and stale tokens; explicitly retry failed/interrupted generation when appropriate. Never publish a draft solely because its HTTP request succeeded. |
| Restore verification fails | Keep proxy restricted and original DB intact. Verify archive checksum/identity, empty recovery target, role permissions and migration version. Do not overwrite a target, suppress pg_restore errors or expose a partly restored database. |

## Containment

Verify the private config/target, then stop the owned proxy without depending on database availability:

```sh
python3 scripts/pilot.py restrict --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
```

This operation checks explicit confirmation and container deployment/project labels. If the Docker daemon is unavailable, use the approved host firewall/VPN restriction and host operator; no general firewall mutation is automated here. Preserve original database/key volumes and image IDs. Never use broad Docker cleanup or stop an unrelated development/demo PostgreSQL container.

For suspected credential/session compromise, restrict first, preserve private evidence, rotate affected secrets through approved provisioning, disable affected accounts/reset passwords and invalidate all sessions when required. Verify the target before target-confirmed `invalidate-sessions`. If keys or host access were compromised, follow a reviewed keyring/certificate recovery plan. Do not publish raw logs, cookies, key XML, employee records or archives as incident artifacts.

## Recovery and reopening

Record detection time, symptoms, request IDs, scoped actions, operator, target and approvals. Restore only to a separate verified empty database using [the recovery runbook](pilot-backup-recovery.md). Perform readiness, authentication, authorization, schedule/publication and integrity checks before reopening. The incident owner decides roll-forward versus compatible image rollback or restoration, accepts any post-backup write loss and approves user communication. Record actual recovery time and follow-up changes; use a synthetic isolated reproduction for defects rather than experimenting on live data.

Review image advisories at least weekly and before deployment. The PostgreSQL High/Critical findings have **not** been accepted. A zero-finding scan is a time-stamped tool result, not evidence that a host, configuration or workflow has no vulnerabilities.
