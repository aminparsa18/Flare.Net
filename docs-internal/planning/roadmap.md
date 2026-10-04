# Roadmap

Forward-looking, still-open items only — no diary of what's already
shipped. See [`../README.md`](../README.md) for the rule this file exists
to enforce (a completed item is deleted here the same PR that ships it,
not checked off and kept); `git log` and the `adr`/`investigations`
folders are where "what happened and why" actually lives.

- **Retention policies + cold storage to S3-compatible object storage
  (RustFS).** A separate item from multi-node scaling (which shipped —
  see [`../adr/0003-distributed-tables-plain-names-and-sharding.md`](../adr/0003-distributed-tables-plain-names-and-sharding.md)
  and [`../../docs/explanation/clustering.md`](../../docs/explanation/clustering.md)):
  this one is retention/cold storage, not horizontal availability/
  throughput. Not started. Prior-art design worth reusing, from SigNoz's
  TTL/cold-storage implementation ([signoz#1173](https://github.com/SigNoz/signoz/commit/5d080f5564c7839d0908db48bc8fff47d0e55648)):
  cold storage isn't app-level archival, it's ClickHouse's own tiered
  storage — an S3-backed disk/volume defined in ClickHouse's own config,
  with `ALTER TABLE ... MODIFY TTL ... DELETE, ... TO VOLUME 'x'` moving
  aged parts onto it, so a table's storage policy just needs assigning
  once (idempotent) rather than anything bespoke on Flare's side; a
  `GetDisks`-style read of `system.disks` lets the retention UI offer a
  dropdown of volumes actually configured instead of free text. Because
  that `MODIFY TTL` is a long-running ClickHouse mutation, the set-TTL
  API should be async and status-tracked (a small table keyed by a
  transaction id, `pending`/`success`/`failed`, one row per underlying
  table) rather than blocking the request — reject a second set-TTL call
  while one's still `pending` instead of queuing another mutation, and
  have the GET endpoint return both the *actual* TTL (parsed live from
  ClickHouse) and the *expected* one (what was last requested) plus
  status, so the UI can show "applying…" instead of a stale value. One
  more ClickHouse config gotcha to get right when this is built: set
  `perform_ttl_move_on_insert: 0` on the S3 volume in ClickHouse's
  storage config - without it, ClickHouse evaluates the TTL-move rule
  synchronously on every insert once cold storage is configured, adding
  latency to the ingest path; the flag defers it to ClickHouse's
  background merge process instead
  ([signoz#1448](https://github.com/SigNoz/signoz/commit/f8f903848e914d529617c6e10c69b3644f8d4c30)).
  Worth designing in from the start: per-resource retention (e.g. keep
  `deployment.environment=dev` for 7 days, everything else 30) via a
  `_retention_days` column computed from ordered resource-attribute rules
  (`multiIf(...)`) and a TTL of `Timestamp + toIntervalDay(_retention_days)`,
  with a default when no rule matches
  ([signoz#8513](https://github.com/SigNoz/signoz/commit/4daec45d987ab07a095f1c225db63193fef93f65)).
  Run every `MODIFY TTL` with `SETTINGS materialize_ttl_after_modify=0`,
  or each retention change rewrites every existing part up front instead
  of letting merges apply it
  ([signoz#9189](https://github.com/SigNoz/signoz/commit/7ddaa84387748a7ee36a1e19199078f86518eee3)).
- **Research: a real "skip-index effectiveness" signal for the Indexing
  page.** Deliberately not shipped — ClickHouse doesn't expose this as
  reliable production telemetry today. Full findings, including upstream
  ClickHouse's own attempt at exactly this (merged then reverted for a
  correctness bug) and what to check before revisiting:
  [`../investigations/skip-index-effectiveness-signal.md`](../investigations/skip-index-effectiveness-signal.md).
  Until upstream lands something reliable, the fallback is a
  differently-labeled, genuinely-computable proxy (e.g. "% of queries
  reading under N% of their table's total rows" from `system.query_log`) —
  real, just not skip-index-specific, since primary-key pruning contributes
  too.
- **Backlog for more brokers on the Messaging page.** Kafka (consumer lag),
  RabbitMQ (queue depth, ADR-0057) and NATS JetStream (ADR-0093) fill the
  `Backlog` column. Next: Service Bus active/dead-letter counts via the
  collector's `azuremonitor` receiver (needs a real Azure subscription to
  verify; the emulator exposes no metrics), and Amazon SQS. The collector's
  `awscloudwatch` receiver reads CloudWatch *Logs*, so SQS queue depth would
  need CloudWatch Metric Streams through the `awsfirehose` receiver. Each
  is one more metric lookup next to `MessagingQueryBuilder.BuildQueueDepth`.
  Not started.
- **Create/invite additional local users.** With local auth,
  `/api/auth/bootstrap` creates only the first admin, and
  `UserEndpoints` can list users, change a role and disable a user, but not
  create one - there's no API or dashboard path to a second local account
  (live e2e runs have had to write users into SQLite directly). Needed: an
  admin-only "invite user" (email/username + role → one-time
  set-password link, expiring) plus a dashboard form on the users page;
  bulk invite is a nice-to-have. Not started. Prior art:
  [signoz#6057](https://github.com/SigNoz/signoz/commit/fc4b55cb34b48fd3f47719be6ad6008b42d7e77d).
  The same expiring set-password token should also back a forgot-password
  flow: there's no reset today, so a locked-out local user needs direct
  SQLite access. Email the link when SMTP is configured, otherwise let an
  admin generate one
  ([signoz#10073](https://github.com/SigNoz/signoz/commit/e1ac992e5a65b49678187303840e79b568feea87)).
  Also missing: local users can't change their own password at all
  (`AuthEndpoints` has only login/logout/bootstrap), and `ISessionStore` can't
  drop a user's sessions. Add `POST /api/auth/password` (current + new, same
  strength rules as bootstrap) that revokes the user's other sessions, and
  make any reset (forgot-password or admin) revoke all of them
  ([signoz#12531](https://github.com/SigNoz/signoz/commit/faaed20dbd08c320fcda4f9cc004d2091c4045de)).
- **OpenAPI.NET v3 (`Microsoft.OpenApi` 3.x, OpenAPI spec 3.2).** Blocked on
  `Microsoft.AspNetCore.OpenApi`: 10.0.x caps it at `[2.12.0, 3.0.0)`, so the
  direct pin in `Directory.Packages.props` stays on 2.x. The first release
  that allows 3.x is 11.0 (RC1 requires `[3.10.0, 4.0.0)`), which needs the
  `net11.0` upgrade, so do both together; no 10.0.x servicing release has lifted the cap.
  See the [OpenAPI.NET v2/v3 announcement](https://devblogs.microsoft.com/openapi/openapi-net-release-announcements/).
- **Provision the admin account from configuration.** The first admin can
  only be created interactively via `/api/auth/bootstrap`, so headless
  installs (compose, the `flare` CLI, Kubernetes/Helm) can't come up with a
  known login. Add optional `Identity__Admin__Username`/`__Password` (plus a
  `__PasswordFile` variant for secrets), applied on startup only when no
  admin exists. An explicit opt-in flag can also reconcile the password to
  config on every start, in which case that account is protected from
  deletion/demotion in the UI. Not started. Prior art:
  [signoz#10313](https://github.com/SigNoz/signoz/commit/6de4520a958fd68c733cf39dbb7594e6198e964d).
- **Deploy / version comparison view.** "Did my deploy break anything?" in
  one screen: pick a service and two `service.version` values (default: the
  latest vs the previous). Compare new Drain log patterns (ADR-0007),
  error-rate and p95 latency per endpoint, new exception types, and new
  outbound dependencies. Each row links into the explorers scoped to that
  version. Versions are detected from first-seen timestamps. No new storage
  is needed for v1. Not started.
- **SLOs with error budgets and burn-rate alerts.** Define SLOs on span data
  (availability: non-error ratio of a service/endpoint; latency: % of
  requests under a threshold) with a target and window (e.g. 99.5% over
  28d). Show remaining error budget, plus multi-window burn-rate alerting
  (e.g. 1h/5m fast burn, 6h/30m slow burn) through the existing alert
  pipeline and channels. Likely needs a pre-aggregated per-minute
  good/total table for long windows. Needs an ADR. Not started.
- **Continuous profiling (later).** Ingest the OTLP profiles signal once it
  stabilizes, store per-service profiles, and link spans to flame graphs of
  what the code was doing during that span. Placeholder for when the spec
  and .NET support settle. Not started.
