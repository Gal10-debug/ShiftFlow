# Pilot backup, restore and recovery

## Proposed objectives (not approved guarantees)

Propose an RPO of 24 hours with a daily encrypted logical backup, plus a backup immediately before every migration/update. Propose an RTO of four hours including host access, key recovery, database restoration, verification and user notification. A small local synthetic restore is not evidence that a full host-loss incident can meet those targets. The operator must approve objectives, size the storage, arrange an independent recovery owner and rehearse on the chosen host.

Proposed retention: seven daily, four weekly and three monthly copies, subject to privacy/operational approval. Keep at least one encrypted off-host copy, with access restricted separately from the app host. Do not configure automated deletion against real storage in this phase. Record checksum, source deployment/database, timestamp, schema/image versions and restore verification; alerts should fire if a backup is missing or older than the approved RPO. The backup script's sidecar manifest contains identity/checksum/timing, not an authenticated signature: protect both archive and manifest from tampering. Checksum detects accidental corruption, not a malicious authorized operator.

## Backup

Set the private config and exact target as in [deployment](pilot-deployment.md). Independently inspect the target container labels, pinned PostgreSQL image and ownership marker before the command. The script repeats those checks. Choose a **new** private archive destination and an approved age public recipient (not its private key).

```sh
python3 scripts/pilot.py backup --config "$PILOT_CONFIG" --confirm-target "$PILOT_TARGET" --archive "$NEW_ENCRYPTED_ARCHIVE" --age-recipient "$APPROVED_AGE_RECIPIENT"
```

`pg_dump --format=custom` obtains a consistent PostgreSQL logical snapshot using a SELECT-only backup role. The ownership-marker schema is excluded so restore cannot overwrite target identity. No owner/ACL statements are restored. The private pgpass file is copied into a 0600 tmpfs file; passwords are not process arguments. For `kind=pilot`, unencrypted backup is refused; stdout streams directly into age. A partial file without a successful manifest is **not** a valid backup and is never overwritten automatically. Investigate failures without publishing private stderr.

The disposable harness alone may create an unencrypted synthetic dump in a private 0700 directory/0600 file. The local drill did not install or exercise age encryption/decryption. An encrypted backup/restore and protected off-host key-bundle restore on the selected host are still live-use gates; do not relabel real data disposable to bypass them.

Logical backup does not include Data Protection keys, protection certificate/password, TLS private key, host configuration, images or the PostgreSQL instance role catalog. Keep an independently encrypted [key/config recovery bundle](pilot-secrets-and-keys.md) and provisioning credentials. An approved new-host recovery must recreate least-privilege roles on a new owned instance. Never reuse an existing demo instance.

## Restore into a separate database — creates state

1. Restrict ingress; stop app writes. Preserve the original database and images. Determine the backup timestamp/RPO loss and obtain the change owner's recovery decision. If the original host is unavailable, first create an approved replacement with the same deployment ID and recovered keys/secrets; host-loss recovery remains unvalidated until that drill occurs.
2. Copy private config to a recovery config; set database to `shiftflow_pilot_<same-id>_restore_<6-lowercase-hex>`. Six suffix characters keep the full ASCII name within PostgreSQL's 63-byte identifier limit, avoiding silent truncation. Do not change the deployment ID or shared role names. Set `RECOVERY_CONFIG` to its private absolute path and `RECOVERY_TARGET` to its exact `id/database`. Independently verify both values and database absence. Existing/nonempty targets are refused.
3. Provision an empty marked recovery database and restore the verified archive:

```sh
python3 scripts/pilot.py provision-restore --config "$RECOVERY_CONFIG" --confirm-target "$RECOVERY_TARGET"
python3 scripts/pilot.py restore --config "$RECOVERY_CONFIG" --confirm-target "$RECOVERY_TARGET" --archive "$VERIFIED_ARCHIVE" --age-identity "$PRIVATE_RECOVERY_IDENTITY_FILE"
```

Restore checks deployment ID, checksum, separate target and empty public schema, then runs `pg_restore --single-transaction --exit-on-error` as the dedicated migrator role through the verified local administrator container. The original database remains intact. On failure leave recovery isolated; inspect private diagnostics and use a **new** recovery target after diagnosing, rather than overwriting a partial/unverified database.

4. Compare all expected public tables, row counts and canonical row fingerprints with the source/drill baseline; verify migration history, representative publications, users, absences and protected notes without exporting row contents. In a real incident, compare available backup metadata and business expectations if the source no longer exists. The helper `Pilot.fingerprints()` performs comparison inside PostgreSQL; the local drill verifies every public table before changing sessions.
5. With app stopped, privately update both connection secret files to the recovery database, retaining the expected runtime/migrator role and strong password. Refresh/recreate containers after bind-mounted file replacement; do not assume an old open file descriptor sees a newly replaced inode. Run session invalidation **before starting ingress**:

```sh
python3 scripts/pilot.py invalidate-sessions --config "$RECOVERY_CONFIG" --confirm-target "$RECOVERY_TARGET"
python3 scripts/pilot.py start --config "$RECOVERY_CONFIG" --confirm-target "$RECOVERY_TARGET"
```

All restored Identity security/concurrency stamps change intentionally, so `AspNetUsers` fingerprints change at this step. Every other restored table must still match. A copied pre-logout cookie must fail authentication: restoring an old database can otherwise resurrect a previously revoked security stamp. Retain the complete old keyring for recovery of other protected state, but require users to log in again after restoration.
6. Verify readiness, trusted HTTPS, new login/CSRF, role restrictions, schedules/publication history, shortage presentation and key encryption. Communicate recovery point and any lost writes. Measure time from recovery start through ready service and record actual RPO/RTO, operator and outcome. Switch the operational config to the verified recovery target. Keep the original database restricted until an approved retention decision; this runbook never drops it.

## Roll-forward or rollback

Use a reviewed compatible prior app image only when its schema compatibility is established. If a migration has committed incompatible changes, prefer a reviewed forward fix or this separate-database restoration procedure. Do not promise EF `Down` can recover deleted/transformed data. Restore loses writes after the selected backup; that is a business decision, not an automatic deployment action.
