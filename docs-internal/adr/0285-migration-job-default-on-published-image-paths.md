# ADR-0285: Make the migration job the default on the published-image install paths

Status: Accepted

Date: 2026-11-28

Supersedes: the "`docker-compose.install.yml` and the CLI templates: unchanged" bullet of
[ADR-0284](0284-clickhouse-migration-job.md), and its Helm bullet's "off by default" and "the chart refuses to
render job mode without per-user connection strings".

## Context

ADR-0284 put the ClickHouse migrations in a one-shot job (the Api image run as `migrate`) on the paths that
build from source, and left the paths that run published images on startup migrations, because 0.6.0 had no
`migrate` command. v0.7.0 shipped on 2026-11-27 with it, and Flare.Cli 0.1.11 and the chart's `appVersion` pin
0.7.0.

Every published-image path can still run an image older than its template: `FLARE_IMAGE_TAG` for
`install.sh`, `flare update --tag 0.6.0` or a hand-edited `.env` for the CLI, and `image.tag` for Helm. In job
mode, an api image older than 0.7.0 ignores the `migrate` argument and starts the whole API. The job never
exits, and the services that depend on it never start. An older Ingest or Api without `clickhousedb` would
crash anyway. [ADR-0283](0283-clickhouse-service-users-implementation.md) already set the rule that a template
must not strip a connection string an older image still needs.

Each path learns the image tag at a different point, so one mechanism can't cover all three.

## Decision

The job is the default on all three paths. Each path keeps older images working, or refuses them, where it
learns the tag. A release tag older than 0.7.0 is one that matches `v?MAJOR.MINOR[.PATCH][-…]` with major `0`
and minor below `7`. Tags that aren't a version number (`edge`, `latest`, `sha-…`) count as new on every path.

- **Helm falls back at render time.** `clickhouse.migrationJob.enabled` defaults to `true`. The chart renders
  the Job only when the services have their own users (`clickhouse.users.enabled` with the bundled ClickHouse,
  or `clickhouse.external.usersSecret`) and the image tag (`image.tag`, else `appVersion`) isn't a release
  older than 0.7.0. Otherwise it migrates in ingest and api at startup, exactly as before ADR-0284, alert-worker's
  `clickhousedb` included, and `NOTES.txt` says why. The old `fail` is gone: a default must not make a working
  values file stop rendering, and without per-user users every service connects as `default` anyway, so the
  Job would take no credential away.
- **The CLI refuses an older tag.** A Compose file can't branch on the tag, and the CLI never rewrites an
  existing instance's compose file. So only instances created by Flare.Cli 0.1.12 or later get job mode. For an
  instance whose compose file has a `clickhouse-migrate` service, `flare start` and `flare update` refuse a
  release tag older than 0.7.0, whether it came from `--tag` or a hand-edited `.env`. `--tag` is checked before
  `.env` is rewritten. Instances from an older CLI keep startup migrations and accept any tag. Because the
  minimum is enforced, the templates also drop alert-worker's `clickhousedb` (ADR-0283's deferred follow-up).
- **`install.sh` fetches the matching files.** `docker-compose.install.yml` on `main` uses the job. When
  `FLARE_IMAGE_TAG` is a full release version older than 0.7.0 and `FLARE_INSTALL_REF` isn't set, the script
  fetches the files from the git tag `v<FLARE_IMAGE_TAG>`, whose compose file migrates at startup. A partial
  old tag such as `0.6` is refused with a hint, since no git tag matches it. `compose up` now passes
  `--remove-orphans`, so moving back to an older file doesn't leave the job or alert-worker on the newer image.
  Someone running `docker compose` by hand with an old tag gets no guard; the docs state the 0.7.0 minimum.
- **`docker-compose.install.yml` gains `alert-worker`.** Alert rules never fired on the `install.sh` path,
  because the file had no AlertWorker. It now has one, as `flare_app`, without the build-time Chromium for
  scheduled reports, which the published image doesn't include.

## Alternatives considered

- **Keep `clickhousedb` on the services as a fallback.** It works with any image, since the migration runner is
  safe to run concurrently, but it leaves the `default` password where the job was meant to remove it. Rejected.
- **Fail the Helm render for an old tag.** It's explicit, but it breaks `helm upgrade` for anyone pinning
  `image.tag` below 0.7.0 when nothing is wrong with their setup. The chart knows enough to pick the right mode,
  so it does.
- **Only document the minimum version.** Every path then hangs on an old tag, with no message until the
  services time out. Rejected for the CLI and `install.sh`, which can check the tag cheaply. It stays the answer
  for running `docker compose` by hand.
- **Rewrite existing CLI instances' compose files.** It would move them onto the job, but `EnsureInitialized`
  deliberately never overwrites a hand-editable compose file. Recreating an instance stays the way to switch.

## Consequences

- On every install path that runs 0.7.0 or later with per-user users, only the migration job and ClickHouse
  itself see the `default` password.
- The `helm-lint` `install` job now installs in job mode at `appVersion`, so it covers the Job on a real cluster.
- The Helm default reaches users with the next `v*.*.*` tag, which publishes the chart. The CLI change ships
  with the `flare-cli-v0.1.12` tag. `install.sh` and `docker-compose.install.yml` are served from `main`, so they
  change as soon as this merges.
- When a release with a major version above 0, or a new minimum, comes along, the comparison lives in three
  places: `scripts/install.sh`, `Flare.Cli/Internal/ImageTagPolicy.cs` and the chart's
  `flare.migrationJobFallbackReason`.

## Related documentation

- [Reference: ClickHouse service users, "Where migrations run"](../../docs/reference/clickhouse-service-users.md#where-migrations-run)
- [Reference: CLI commands, "Image tag policy"](../../docs/reference/cli-commands.md#image-tag-policy)
- [How-to: Deploy with Helm, "Upgrade"](../../docs/how-to/deploy/deploy-with-helm.md#upgrade)
