# ADR-0183: Pin the ClickHouse image to a tested LTS line

Status: Accepted

Date: 2026-10-10

## Context

Every compose file, the `flare` CLI templates and the Helm chart pulled `clickhouse/clickhouse-server:latest` (and
`clickhouse-keeper:latest`). A plain `pull` therefore crossed ClickHouse's breaking changes without notice. 26.9 makes the
analyzer impossible to disable, turns `system.query_log` `interface`/`http_method` into `Enum8`, requires `SOURCES`
grants for `BACKUP ... TO Disk` and changes backslash handling in `tokenbf_v1`/`ngrambf_v1`/text indexes (which affects
the body-search index, ADR-0073). 26.6 to 26.8 also carried a `LowCardinality` small-read CPU regression and a
`Nullable ... IN (subquery)` full-scan regression.

## Decision

- **Pin to the `26.8` tag** (the LTS line) in `docker-compose*.yml`, `src/Flare.Cli/Templates/`, and
  `deploy/helm/flare/values.yaml`, for both server and keeper. The minor tag still receives 26.8 patch releases.
- **Bump deliberately.** Moving to a new line is a PR that changes every one of those files together, after an end-to-end
  run in single and cluster mode, and re-checks a literal-backslash body search when crossing 26.9.
- **Aspire is unchanged.** `AddClickHouse` resolves its own image tag from the Aspire hosting package.

## Consequences

- `docker compose pull` and `helm upgrade` no longer change the ClickHouse major line on their own.
- An existing deployment that ran `:latest` on a newer line must not be downgraded: ClickHouse data written by a newer
  version may not open on 26.8. Those users set the image to their running line.
- Someone has to remember to bump the tag; the upgrade policy is documented in the Helm how-to.
