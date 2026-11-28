# ADR-0287: Opt-in ClickHouse row policies on `flare_query`

Status: Accepted

Date: 2026-11-28

## Context

Projects restrict what a user can read by service allow-lists ([ADR-0123](0123-projects-team-scoping.md)).
Flare.Api enforces them: `ProjectScopeMiddleware` sets the ambient `ServiceScope`, and some 45 query builders
add it to their `WHERE` clause. A builder that forgets it shows a restricted user every service, and nothing
in ClickHouse notices. [ADR-0282](0282-clickhouse-service-users.md) moved every request-driven query onto the
`flare_query` user. The [investigation](../investigations/clickhouse-access-control-vs-identity-store.md)
behind it showed that a row policy on that user, driven by a custom setting, can filter the same way. It left
this as a possible second line of defence, opt-in and fail closed.

These points were checked against `clickhouse/clickhouse-server:26.8`:

- `access_management` is off for `default`, so `CREATE ROW POLICY` isn't available. A users.d file can declare
  the same policies: `<users><flare_query><databases><clickhousedb><logs><filter>`. They load as permissive
  policies for that one user, whichever order the users.d files are in.
- `users_without_row_policies_can_read_rows` is on in the image's config.xml, so `flare_app`, `flare_ingest`
  and `default` still see every row of a table that has a policy for `flare_query`.
- The image's config.xml allows the `SQL_` prefix for custom settings. A server whose config doesn't allow it
  rejects any query that sends one.
- A custom setting's default in a profile is parsed as a literal. A bare `[]` stops ClickHouse from starting
  (`CANNOT_RESTORE_FROM_FIELD_DUMP`). The value has to be a quoted string, `'[]'`.
- `readonly=2` lets `flare_query` set the custom setting per query, as a URL parameter or a `SETTINGS` clause.
- In cluster mode, `remote_servers.xml` has an inter-server `<secret>`, so shards run a forwarded query as the
  initial user. Row policies don't apply on `Distributed` tables, but they do apply on the `_local` tables each
  shard reads. On the 2-shard, 2-replica Compose cluster, a `Distributed` read with `["checkout-*"]` returned
  exactly the checkout rows from both shards. That held from either node, and with `serialize_query_plan = 1`,
  `prefer_localhost_replica = 0` and parallel replicas. The ClickHouse docs warn that `serialize_query_plan`
  returns unfiltered rows, but that didn't reproduce on 26.8. A ClickHouse upgrade should re-check it.
- End to end (Compose, policies mounted, `Query__RowPolicyScope=true`), the admin's queries sent `["*"]` and
  saw all three test services. A member of a `checkout-*` project sent `["checkout-*"]` and saw two.
  `flare_query` logged no errors.

## Decision

- **A separate, opt-in users.d file.** `db/clickhouse/config/flare-row-policies.xml` (cluster:
  `db/clickhouse-cluster/config/flare-row-policies.xml`, Helm: `files/flare-row-policies.xml`) declares one
  policy per telemetry table keyed by service: 19 tables, on `ServiceName`. The exception is
  `service_dependency_nodes`, which uses `Service`, the `peer.service`-or-`ServiceName` value the map scopes
  by. In cluster mode the policies are on the `_local` tables. Config tables and `slos` aren't covered: they
  are scoped by `ProjectId`, not by service. Nothing changes for an install that doesn't mount the file.
- **The scope travels as `SQL_flare_scope`.** It holds a JSON array of the caller's patterns, with the same
  matching as `ServiceScope` (exact, or a trailing `*` for a prefix). An unrestricted caller sends `["*"]`: a
  global Admin, a background worker, auth disabled, or the public status page. ADR-0123 rejects a bare `*` as a
  project pattern, so it can't come from a project. The filter is
  `has(s, '*') OR arrayExists(p -> if(endsWith(p, '*'), startsWith(ServiceName, prefix), ServiceName = p), s)`.
  The `has` is constant within a query, so unrestricted reads cost one folded check.
- **Fail closed.** The policies file sets the profile default to `'[]'`, so a query that doesn't send the
  setting sees no telemetry. Unparseable values read as `[]` too.
- **Flare.Api opts in separately**, with `Query__RowPolicyScope=true`. `QuerySafety` adds the setting to all
  three presets, next to the quota key ([ADR-0286](0286-per-identity-clickhouse-quotas.md)), and every
  `flare_query` call goes through one of them. It's off by default because a server that doesn't allow the
  `SQL_` prefix would reject every query. The Helm value `clickhouse.rowPolicies.enabled` turns on both halves
  for the bundled ClickHouse, and only the Api half for an external one.
- **A test pins the table list.** `FlareClickHouseRowPolicyTests` scans the single-node migrations for tables
  with a `ServiceName` or `Service` column. A new one fails the build until it gets a policy or an explicit
  exclusion. The test also checks that the cluster and Helm copies match.

## Consequences

- With both halves on, a query builder that drops `ServiceScope` returns only the caller's services, not
  everyone's.
- It doesn't protect against a compromised Flare.Api, or an injection in the generated SQL: the client sends
  the scope, and `flare_query` can set it to `["*"]`.
- The policies are stricter than Flare.Api in two places, so restricted users see less there than without them:
  - The views ADR-0123 deliberately leaves unscoped (host and Kubernetes inventory, broker backlog gauges) read
    metrics tables that are keyed by service. Rows that collectors report under an out-of-scope `service.name`
    disappear.
  - On spans, the map attributes a client span to its `peer.service`, but the policy filters spans on the
    service that recorded them.
- Half-configured installs:
  - The policies are mounted but Flare.Api doesn't send the scope (it's off, or the image predates this ADR).
    Every page is empty for everyone, which is obvious.
  - Flare.Api sends the scope but no policies are mounted. ClickHouse ignores the setting and only Flare.Api's
    filters apply.
- `read_rows`, `system.parts` and `EXPLAIN ESTIMATE` still count the hidden rows. The keyed quota (ADR-0286)
  is charged for them.
- The policy sits on top of Flare.Api's own filter. That filter can use the primary key; the policy's lambda
  runs per row on the rows that are left.
