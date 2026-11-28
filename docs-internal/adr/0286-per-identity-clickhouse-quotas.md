# ADR-0286: Per-identity ClickHouse quotas on `flare_query`

Status: Accepted

Date: 2026-11-28

## Context

Flare.Api bounds each query (`QuerySafety`, from `Query:*`) and, for personal access tokens, the request rate
([ADR-0028](0028-personal-access-token-rate-limiting.md)). Neither bounds what one caller costs over time. A user or token that sends
many expensive queries, each one inside the per-query caps, can keep ClickHouse busy for everyone else.
[ADR-0282](0282-clickhouse-service-users.md) moved every request-driven query onto the `flare_query` ClickHouse
user, so a ClickHouse quota on that user covers them all. ClickHouse measures the cost there (rows read, execution
time), which Flare.Api can't measure in advance.

Unless it's keyed, a ClickHouse quota is per user, and every Flare account shares `flare_query`. A keyed quota
counts each client key separately. Over HTTP, the client sends its key in the `X-ClickHouse-Quota` header. The
ClickHouse .NET driver sends custom headers per query (`QueryOptions.CustomHeaders`).

These points were checked against `clickhouse/clickhouse-server:26.8` and ClickHouse.Driver 1.4.0:

- `<keyed/>` in a users file is `KEYED BY client_key, user_name`. A query with no key is charged to the user-name
  bucket (`flare_query`), and each key has its own counters. The driver passes the header through, and an exceeded
  quota throws `ClickHouseServerException` with code 201 (`QUOTA_EXCEEDED`).
- A quota limit read through `from_env` with the variable unset stops ClickHouse from starting (`CANNOT_PARSE_NUMBER`).
  An element can't carry both `from_env` and a default value.
- A second `users.d` file can override a quota's limits, but files merge in name order. A file that sorts before
  `flare-users.xml` is overwritten by it.

## Decision

- **The quota.** The three users files (single-node, cluster, Helm copy) declare a `flare_query` quota with
  `<keyed/>` and a one-hour interval, and assign it to the `flare_query` user. Every limit is `0` (no limit), so by
  default it only records usage per identity in `system.quotas_usage`. Admins set limits in a second `users.d`
  file that sorts after `flare-users.xml`. The Helm chart renders one, `zz-flare-query-quota.xml`, when
  `clickhouse.queryQuota.readRows` or `clickhouse.queryQuota.executionTimeSeconds` is above `0`. A server set up
  with SQL uses `CREATE QUOTA ... KEYED BY client_key, user_name`.
- **No limits by default.** Any fixed hourly cap fits some installs and breaks others: a busy dashboard on
  auto-refresh can read billions of rows an hour. The limits aren't environment variables either: someone who
  mounted the users file by hand and then upgraded would get a ClickHouse that doesn't start.
- **The key.** `ClickHouseQuotaMiddleware` runs after authentication and sets an ambient
  `ClickHouseQuotaKey.Current` (an `AsyncLocal`, as `ServiceScope` does for ADR-0123). `QuerySafety` adds it as the
  `X-ClickHouse-Quota` header to the options every capped query uses, so no service had to change. The key is
  `pat:{id}` for a personal access token (its own bucket, like ADR-0028), `demo:{address}` for the shared demo
  account ([ADR-0278](0278-public-read-only-demo-mode.md)), and `user:{id}` for any other account. Unauthenticated requests
  (auth disabled) and background workers send no key and share the `flare_query` bucket.
- **The error.** The same middleware turns code 201, alone or wrapped, into a 429 when the response hasn't started,
  and logs the key. Other ClickHouse errors pass through unchanged.

## Consequences

- With limits set, one account or token can't use more than its share of ClickHouse per hour, and every account
  gets the same share. A key's usage is visible as `default`:
  `SELECT quota_key, read_rows, execution_time FROM system.quotas_usage WHERE quota_name = 'flare_query'`.
- The background bucket and, with auth disabled, every request share one set of counters, so a cap applies to
  their total. Admins who enable limits on an install without auth are capping the whole Api.
- Queries on `flare_app` (alert evaluation, config, retention) are never charged. AlertWorker never sets a key.
- A query built without `QuerySafety` runs without the header and is charged to the `flare_query` bucket. That
  isn't a bypass, because that bucket has the same limits.
- A page whose service catches ClickHouse errors and degrades shows that part as empty rather than as a 429.
- In cluster mode the quota is counted on the node Flare.Api connects to. Whether rows that other shards read
  for a distributed query count there hasn't been measured.
