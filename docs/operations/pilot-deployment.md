# Pilot deployment and updates

This is a preparation runbook, not approval to deploy. The current live-use recommendation is **NOT READY**; see [the readiness assessment](../phase-7e-pilot-readiness.md). No command below authorizes public exposure, real employee data, or an operation on an existing development/demo database.

## Topology and prerequisites

Use one supported Linux Docker/Compose host, one ASP.NET Core process serving the Vite production bundle, one TLS reverse proxy and one PostgreSQL 17 instance. Keep admin access behind an approved VPN or IP allowlist. Start with loopback binding; opening a host interface requires the owner's separate deployment approval, firewall rules and a valid domain certificate. Docker daemon access is privileged administrative access.

The proxy alone joins the ingress bridge. The proxy and app share a private internal edge network; app and database share a separate private internal data network. Only proxy port 8443 is published, bound to 127.0.0.1 by default. App 8080 and PostgreSQL 5432 are never published. The proxy replaces forwarded scheme/client headers, strips alternative forwarded host headers and accepts only the configured hostname. ASP.NET trusts one exact static proxy IP, one forwarding hop, with header symmetry. Never set the global forwarded-headers enable switch or trust all private networks.

Resource limits: app 1 GiB/2 CPUs, PostgreSQL 512 MiB/2 CPUs, proxy 64 MiB/0.5 CPU. Start host sizing at 2–4 vCPUs/4–8 GiB RAM with encrypted persistent SSD; approve sizing using representative pilot load and reserve room for the OS, image builds and backups. Local recovery timings do not establish host capacity. Run one app instance; the generation queue is in memory. An interrupted generation may need an explicit retry after the worker recovers; retain the existing scheduling review safeguards.

Prerequisites: Docker Engine/Compose v2, Python 3, OpenSSL, trusted TLS provisioning, a reviewed image build, a private operator config, secret files, three dedicated networks, persistent storage and approved recovery ownership. Install `age` before any live backup and retain its private recovery identity off the application host. No orchestration or external secret manager is required.

## Build and validate locally

From the feature branch/reviewed commit:

```sh
docker build -f deploy/pilot/Dockerfile -t shiftflow-pilot:reviewed .
docker build -f deploy/pilot/Dockerfile.proxy -t shiftflow-pilot-proxy:phase7e-local .
python3 scripts/test-pilot.py
python3 scripts/test-pilot-deployment.py --image shiftflow-pilot:reviewed
```

The integration harness creates a new deployment ID, random synthetic credentials, short-lived local certificates, a new database volume and a new encrypted keyring. It binds only loopback and verifies ownership before cleanup. It never adopts an existing database or volume. Only status/count/timing evidence is exported to `TestResults/phase7e/deployment-validation.json`. Do not publish `.pilot-private`, raw database backups, cookie jars or framework logs. `--keep-private` is for local debugging only; retained files contain synthetic secrets and require private storage/removal.

Base images are digest-pinned. The proxy additionally pins reviewed Alpine security package versions; the build fails if those versions disappear. Rebuild/scan and review new exact versions rather than silently dropping pins. PostgreSQL retains the Phase 7C.1 digest. The build context excludes local keys, secret directories, build outputs and dependency folders. The runtime is chiseled/non-root with native solver libraries included; the local drill executes the real solver.

## Prepare an approved host (external decision pending)

1. Record authorized operator, host, region, budget, domain, firewall/VPN rules, incident contact and the PostgreSQL risk decision. Do not proceed to live data before these gates close.
2. Copy `deploy/pilot/configuration.example.json` to a host-private directory. Generate a new 32-character lowercase hex UUID and use it in both ID and `shiftflow_pilot_<id>`. Keep this deployment ID stable for the lifetime of its keys/cookies. Set `kind` to `pilot`; never label real data disposable. Point image fields at reviewed immutable image IDs/digests, not mutable tags.
3. Allocate unused Docker subnets. Create dedicated internal edge/data networks and a proxy-only ingress bridge, all labeled `shiftflow.pilot.deployment=<id>`. Reserve `.2` for proxy and `.3` for app on the edge subnet. Check for host/VPN route overlap. Example creation **only after reviewing these names/subnets**:

```sh
docker network create --internal --subnet 172.30.90.0/24 --label "shiftflow.pilot.deployment=$PILOT_ID" "$PILOT_EDGE_NETWORK"
docker network create --internal --label "shiftflow.pilot.deployment=$PILOT_ID" "$PILOT_DATA_NETWORK"
docker network create --label "shiftflow.pilot.deployment=$PILOT_ID" "$PILOT_INGRESS_NETWORK"
```

4. Provision [secrets and key protection](pilot-secrets-and-keys.md). The template is intentionally unusable until replaced. Match public host, origin, TLS certificate and proxy IP. Do not store password values in the JSON or shell history. Keep config and parent directory private (0600/0700), even though config contains only paths and identities.
5. Read the config and independently compare the ID, database, image IDs, network labels and bind address with the approved change record. Define these **non-secret** shell variables in the private operator shell:

```sh
PILOT_CONFIG=/absolute/private/path/configuration.json
PILOT_TARGET=reviewed-deployment-id/reviewed-database-name
```

6. Start only the new database, then provision roles/schema marker. These commands create state and require the exact reviewed confirmation:

```sh
python3 scripts/pilot.py start-database --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py provision --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py migrate --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py bootstrap --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py start --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
```

Wait for PostgreSQL's TCP health check before provisioning. Provisioning refuses pre-existing roles/databases; it never adopts a similarly named target. Partial provisioning requires an operator diagnosis on the new owned instance, not blind retries or dropping objects. Migrations and bootstrap are repeatable; bootstrap does not reset an existing account. No app-startup migration is permitted. The runtime role cannot create schema or modify migration history.

7. Verify trusted HTTPS, app readiness, key encryption, forced initial password change, administrative restrictions and safe log output. Readiness must pass with the exact runtime role and database marker. The external proxy blocks health endpoints; use the container's `--healthcheck live|ready` command via verified container ID. Complete [onboarding](pilot-accounts-and-support.md), then approve a small representative pilot before broader enrollment.

## Startup, restriction and shutdown

```sh
python3 scripts/pilot.py verify --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py start --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py restrict --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
python3 scripts/pilot.py stop --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET"
```

`restrict` stops only the verified proxy. `stop` stops proxy then app gracefully (40-second container grace, 30-second .NET shutdown timeout). Both use explicit confirmation/container labels and work while PostgreSQL is unavailable. Neither removes volumes nor stops PostgreSQL. Do not use `down -v`, volume prune or broad container cleanup. The validated database/key volumes survive app replacement and database restart; host loss still requires off-host backups.

## Safe updates and migration failure

Restrict access; ensure no active generation; take an encrypted database backup and protected key/certificate/config backup. Restore the backup into a separate owned empty database and test the proposed migration on that clone. Review SQL, lock duration, downtime, data loss and compatibility with both old and new binaries. Document whether an old binary can read the new schema. Build/scan the new immutable image; preserve the old image ID.

Stop the app before a schema-changing migration. Update image IDs in private config, run target-confirmed `migrate`, verify migration history, readiness and representative authorization/scheduling workflows, then `start`. Never grant migration permissions to the runtime account. A migration exception leaves access restricted. Investigate whether the migration transaction rolled back; do not infer rollback merely from a nonzero exit code. Prefer a reviewed roll-forward. If restoration is required, follow [recovery](pilot-backup-recovery.md) into a separate database, verify data, invalidate sessions, and only then switch connections. Never run automatic EF `Down` migrations against unknown data or overwrite the original database.

Record operator, target, pre/post image IDs, migration IDs, backup checksum, verification outcome and approval. Do not record passwords or row contents.
