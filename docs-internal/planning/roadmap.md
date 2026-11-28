# Roadmap

Forward-looking, still-open items only — no diary of what's already
shipped. See [`../README.md`](../README.md) for the rule this file exists
to enforce (a completed item is deleted here the same PR that ships it,
not checked off and kept); `git log` and the `adr`/`investigations`
folders are where "what happened and why" actually lives.

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
- **Verify the Service Bus and SQS Messaging backlog against real clouds.**
  ADR-0119 and ADR-0120 took metric and attribute names from the Collector
  receivers' source, not a live run. Confirm `azure_activemessages_average` with
  `metadata_entityname` (`azuremonitor` receiver) and
  `ApproximateNumberOfMessagesVisible_avg` with `QueueName` (`awsfirehose` +
  `transform`), and fix the constants in `MessagingQueryBuilder` if they differ.
- **Continuous profiling: span links.** Ingest, the Profiles page, the how-to and retention (migration
  0069, `RetentionSignals`) are done ([ADR-0141](../adr/0141-continuous-profiling-ingest.md)), and
  verified end to end with the OpenTelemetry eBPF profiler (`otelcol-ebpf-profiler` 0.163.2,
  whole-host, Docker Desktop and Linux). The Collector's `pprof` receiver (v0.162) still emits samples
  without a stack table, so skip it; re-check on each Collector bump. OTLP profiles is still Alpha, so
  re-check the vendored proto and the profiler version on each bump. `profile_samples` already stores
  each sample's linked `TraceId`/`SpanId`; what's missing is a sender that fills them for .NET:
  - **eBPF path, blocked upstream.** The eBPF profiler reads the active span only from processes
    that publish it in the OTEP 4947 thread-local (`otel_thread_ctx_v1`, reader merged in
    `interpreter/threadcontext`). OpenTelemetry .NET doesn't publish it yet
    ([opentelemetry-dotnet#7404](https://github.com/open-telemetry/opentelemetry-dotnet/issues/7404),
    unassigned); the OTEP rates .NET feasible through a native library. Re-check on each SDK release.
  - **Auto-instrumentation path, doable now.** The opentelemetry-dotnet-instrumentation continuous
    profiler already records the span context per sample but ships only a console exporter (its OTLP
    profiles exporter lives in integration tests). A Flare plugin exporter that maps its samples to
    OTLP profiles would give linked .NET profiles today.
- **User settings page (`/settings`), starting with Appearance & layout.**
  The dashboard has outgrown the user-menu dropdown as the home for
  preferences: theme, language and display time zone live in
  `NavUserMenu.svelte`, and the rest are scattered across per-feature
  `localStorage` keys with no UI to see or reset them (pinned log
  attributes, facet sidebar prefs, logs column visibility and lines per
  row, home-dashboard choice, last-used saved views, recent searches and
  custom ranges, update-notice dismissal). Build a sectioned settings
  route (left rail of sections, deep-linkable `/settings/<section>`) and
  move the dropdown's controls there, leaving the dropdown as a shortcut.
  Not started. Remaining: instance defaults an admin can set for new users
  (theme, layout, time zone).
- **.NET MAUI / mobile SDK (later).** `Flare.Maui` (ADR-0166, `src/Flare.Maui`) ships the
  `UseFlare()` core: OTLP/HTTP export, disk retry, device/session attributes, `HttpClient`
  and Shell navigation spans, unhandled-exception capture. Remaining for it: Windows. Native crashes found on the next launch (Android `ApplicationExitInfo`, iOS MetricKit,
  ADR-0178) are done apart from the follow-ups below. The macOS CI job (`maui-ci.yml`) and the NuGet
  publish path (`flare-maui-v*` tag in `nuget-publish.yml`) exist but have not run yet: the
  first release also needs a nuget.org trusted-publishing policy for `Flare.Maui`. Server side (ADR-0167) is done: scoped ingest keys (ADR-0149/0150/0051), the
  Sessions page, and app-version facets on `/errors`. Release health (crash-free sessions and users per version, ADR-0175) is done too, with users counted from `user.id`
  (`SetUser`, ADR-0176; scrubbing, `BeforeSend` and queue limits, ADR-0177). Remaining there: a rollup table if long
  windows prove slow. The per-session timeline is done (ADR-0170). Trimmed/Mono
  stack traces are symbolicated from uploaded dll+PDB symbols (ADR-0168); Native AOT frames on
  iOS/macOS are covered from the `.dSYM`; remaining there: ELF/PDB
  symbols for Linux/Windows AOT, and verifying Mono's offsets on a device.
  The `Platform/` glue's trimming is verified on an iOS Release build on a device; an Android Release build is untested.
    Next for `Flare.Maui`, from the gap against Sentry's MAUI SDK (what a team would miss moving over), roughly in
  priority order:
  - Native crash follow-ups (ADR-0178): server-side symbolication of the raw iOS offsets against a dSYM; verify the
    MetricKit renderer on an iOS device (the tombstone one is verified on Android); Android before 11
    and iOS jetsam out-of-memory kills, which have no report to read; an exact iOS crash time (it is estimated from
    the last lifecycle event).
  - Mobile performance follow-ups (ADR-0180; verified on a real Android device by Maestro flows 12-14 and on an iPhone by `run-ios-device.sh`):
    an Android Release build; a trend over time and an alert rule on a start/load
    regression; iOS process start time; time-to-first-frame rather than first resume (a warm start is only the
    `OnRestart` to `OnResume` gap, about 12 ms).
  - Profiling follow-ups (ADR-0181): iOS, `app.start` profiles, managed frames, and a flame graph in the dashboard.
  - Windows support.
  - Scrub exception events recorded by other instrumentation (ADR-0191): an `ActivityEvent` is immutable once added, so
    this needs a different mechanism than a processor.
  - Other client SDKs on the same server pieces (sessions, release health, scoped keys): Android native, iOS native,
    React Native and Flutter, each thin over OTLP.
- **Issue workflow, alert integrations and hosted-service parity.** Platform-wide, not specific to any one app or SDK.
  - Alert integrations beyond webhook/Slack/Telegram/email/PagerDuty: Teams, Opsgenie, Discord, and a Jira ticket on
    first occurrence.
  - Hosted-service parity items a self-hoster still expects: SSO group mapping and an upgrade guide with tested
    rollback.
- **APM depth.** Flare receives OTLP and ships no agent, so these close gaps without changing that.
  - **A per-statement database view:** slowest normalized statements with rate and p95, from the `db.*` span attributes.
    Check first that N+1 detection and span-duration comparison don't already cover the need.
  - **Span-to-profile links**: the waterfall-to-flame-graph link. The data model is ready (`profile_samples.TraceId`/`SpanId`),
    so build and test it with seeded OTLP profile samples linked to real spans; no real sender fills the ids for .NET yet
    (see the continuous profiling item above).
- **Security monitoring (not a SIEM).** The audit log has a hash chain, export and forwarding
  ([ADR-0202](../adr/0202-audit-log-hash-chain-and-export.md), [ADR-0203](../adr/0203-audit-log-forwarding.md)). Two
  small items fit the existing design; correlation rules, case management and threat-intel feeds stay out of scope.
  - **Security alert templates:** ready-made log-count rules for failed logins, privilege changes and repeated 401/403s,
    shipped like the built-in dashboard templates ([ADR-0088](../adr/0088-built-in-dashboard-templates.md)).
  - **Enrichment actions in pipeline rules:** GeoIP and ASN lookup on an IP attribute at ingest, next to the existing
    extract, redact and parse-JSON actions ([ADR-0033](../adr/0033-pipeline-rules-extraction-redaction.md),
    [ADR-0070](../adr/0070-pipeline-rules-parse-json.md)). Needs a bundled or user-supplied database and a decision on
    licensing.
- **Grafana interoperability.** Today there is OTLP forwarding to a Grafana endpoint
  ([ADR-0155](../adr/0155-otlp-forwarding.md), with a Grafana Cloud preset and a test-connection button in
  [ADR-0249](../adr/0249-grafana-cloud-forwarding-preset-and-test-connection.md)), a read-only Prometheus API a Grafana data source can query
  ([ADR-0109](../adr/0109-prometheus-query-api.md), extended with operators between series, `offset`, `topk` and more
  functions in [ADR-0239](../adr/0239-prometheus-api-binary-operators-offset-topk-functions.md), then `absent`,
  `label_replace` and `label_join` in [ADR-0240](../adr/0240-prometheus-api-absent-label-replace-label-join.md),
  exemplars in [ADR-0241](../adr/0241-metric-exemplars.md), and release health as `flare_release_*` metrics in
  [ADR-0250](../adr/0250-release-health-prometheus-metrics.md), with trailing-24h users gauges in
  [ADR-0251](../adr/0251-rolling-window-release-users-gauges.md), and alerts and releases as annotation metrics in
  [ADR-0252](../adr/0252-alert-and-release-annotation-metrics.md)), a Flare data source plugin for sessions, errors and
  release health ([ADR-0254](../adr/0254-grafana-data-source-plugin.md)), a read-only Tempo-compatible trace API
  ([ADR-0217](../adr/0217-tempo-compatible-trace-api.md)) and a read-only Loki-compatible log API
  ([ADR-0236](../adr/0236-loki-compatible-log-api.md), extended with `logfmt`, `line_format`, `unwrap`, live tail and
  Logs Drilldown in [ADR-0237](../adr/0237-loki-api-logfmt-line-format-unwrap-tail-and-drilldown.md), then
  `rate_counter`, `absent_over_time`, `logfmt --strict`, unwrap conversion errors and tail dropped entries in
  [ADR-0238](../adr/0238-loki-api-rate-counter-absent-strict-logfmt-unwrap-errors-dropped-entries.md)). Remaining,
  roughly in priority order:
  - Grafana data source plugin follow-up ([ADR-0254](../adr/0254-grafana-data-source-plugin.md),
    [ADR-0255](../adr/0255-grafana-plugin-session-timeline-performance-dashboard.md),
    [ADR-0256](../adr/0256-grafana-plugin-go-backend.md) and
    [ADR-0257](../adr/0257-grafana-plugin-release-health-value.md) ship it with a Go backend, a bundled dashboard, a
    tag-driven release workflow and an alertable release health value): Grafana catalog submission (needs an account, a
    signing token and review).
  - A how-to for sending Flare data to Grafana (Grafana Cloud and self-hosted through Alloy or a collector), verified
    against a real instance first.
- **Privacy, compliance and security hardening.** None of these exist today (the audit log, login throttling, ingest-time
  redaction rules, ingest-key allowlists and TTL retention are already shipped). In rough priority order:
  - **Threat-model findings to fix first** (from [the threat model](../threat-model.md); each was confirmed in code):
    - **Default-open instances.** With `AuthSettings.Enabled=false` every caller is effectively Admin, and ingest keys are
      off by default. Decide whether a fresh install bound beyond loopback should warn loudly or require enabling auth
      and ingest keys.
    - **MFA / TOTP for local and LDAP accounts.** Password plus lockout only; external IdPs already cover MFA. Add TOTP,
    recovery codes and a "require MFA for Admins" setting (WebAuthn/passkeys later).
- **Cluster mode without Postgres.** Api stays the single owner of the SQLite identity store and publishes the ingest-key
  set (hash, limits, origins, services, expiry, revoked) to a versioned ClickHouse table (ADR-0009/0074), which
  Ingest's `IngestApiKeyCache` polls. Ingest then drops its `Flare.Identity` reference and the identity migrations, and
  `docker-compose.cluster.yml` drops `postgres`. Postgres stays available for anyone running several Api replicas.
  Needs an ADR superseding ADR-0111's "cluster mode uses Postgres"
  ([investigation](../investigations/clickhouse-access-control-vs-identity-store.md)).
- **Map-aware text index for attribute filters, once ClickHouse 26.9+ is pinned.** The `keyValuePairs` text index serves
  `attr['k'] = 'v'` only and 4 to 7x speeds up broad equality filters, at about 15x the bloom filters' size and with the
  bloom filters still needed for `mapContains`/`IN`/regex; it does not exist on the pinned 26.8 LTS. Re-benchmark on a real
  corpus after the bump (see [ADR-0184](../adr/0184-defer-keyvaluepairs-attribute-index.md) and
  [the investigation](../investigations/map-attribute-text-index.md)). Body search on a `text` index (26.9 `LIKE`/`ILIKE`,
  vs ADR-0073's `ngrambf_v1`) is a separate benchmark.
- **Force-merge closed partitions.** Logs and spans are partitioned monthly, so a
  closed month keeps whatever part fragmentation it had when it closed.
  ClickHouse 26.9 adds the `min_partition_age_to_force_merge_seconds` MergeTree
  setting, which compacts a partition once every part is older than the
  threshold. Benchmark on a month of data (parts per query, compression, merge
  I/O, TTL rewrite cost), then set it per table in an additive migration
  (single + cluster) alongside the retention TTL work. Flare sets no
  force-merge or `ttl_only_drop_parts` settings today. Prior art: [ClickHouse
  26.9 changelog](https://github.com/ClickHouse/ClickHouse/blob/master/CHANGELOG.md#269).
- **Run the benchmarks again once BenchmarkDotNet supports .NET 11 in a stable release.** 0.15.8 crashes in
  `DotNetSdkValidator` (`GetRuntimeVersion not implemented for NotRecognized`) on `net11.0`, so no .NET suite in
  `scripts/bench.sh` runs. 0.16.0-preview.2 knows `Net11`, but we're staying on stable versions. After the bump, run
  `scripts/bench.sh` and commit the run, including the new `request-decompression` suite (gzip vs zstd decode
  cost and allocations for OTLP export requests), which has had only an in-process dry run.
- **Switch `Flare.OpenTelemetry` to the upstream consistent-probability sampler once it is stable.** `ConsistentProbabilitySampler`
  in `OpenTelemetry.Extensions` is prerelease only (latest `1.21.0-beta.1`). When a stable release ships, replace the package's
  own `ConsistentProbabilitySampler` with it and drop ours (ADR-0280).
- **Retire `Flare.Hosting.Aspire`** ([ADR-0281](../adr/0281-aspire-integration-moves-to-community-toolkit.md)). Once
  [CommunityToolkit/Aspire#2610](https://github.com/CommunityToolkit/Aspire/pull/2610) (the missing hosting features) merges and
  ships, move `examples/ExampleApp.AppHost` to `CommunityToolkit.Aspire.Hosting.Flare` and delete `src/Aspire.Hosting.Flare`. The
  example's k3s deploy then uses `WithIdentityDatabase` or `WithDataVolume` plus Aspire's own `WithPersistentVolume`, instead of
  the old `WithPersistentStorage`.
- **Fold `Flare.Aspire` into `Flare.OpenTelemetry`, then deprecate it** ([ADR-0281](../adr/0281-aspire-integration-moves-to-community-toolkit.md)).
  Make `AddFlareTelemetry` fall back to `ConnectionStrings:flare` (and `Aspire:Flare:ApiKey`) when no `Flare:Endpoint` is set,
  switch `examples/ExampleApp.Shop` to it, then deprecate `Flare.Aspire` on nuget.org with `Flare.OpenTelemetry` as the alternative.
- **Point the toolkit Flare README at the aspire.dev page.** Once [microsoft/aspire.dev#2263](https://github.com/microsoft/aspire.dev/pull/2263)
  merges, open a small CommunityToolkit/Aspire PR changing the "Additional Information" link in
  `src/CommunityToolkit.Aspire.Hosting.Flare/README.md` from the nonexistent `learn.microsoft.com/dotnet/aspire/community-toolkit/hosting-flare`
  to `https://aspire.dev/integrations/observability/flare/flare-get-started/`.
- **Docs site (https://aminparsa18.github.io/Flare.Net/) follow-ups.** Built by `scripts/build-docs-site.py` (docfx, one site per language).
  - **API reference** for the client packages (`Flare.OpenTelemetry`, `Flare.Aspire`, maybe `Flare.Maui`) from their XML docs, the
    way TantivyDotNet's site does it. Check the XML doc comments are worth publishing first.
  - **Changelog page** built from the GitHub release notes the release job already generates.
  - **Generated reference pages**: all `Query__*` options, the ClickHouse schema columns, `flare --help`, so they track the code
    instead of being kept in sync by hand.
  - **Translation staleness check.** Each translated page records `source-sha256` of the English file it came from; have the build
    warn when the English page has changed since.
  - **Decide whether `docs-links.yml` is still needed.** The strict docfx build catches what `check-docs-links.py` does, plus
    anchors on translated pages, which the checker misses. Keep it as the fast pre-check or drop it.
  - **Check the README links into the site.** All four `README*.md` now link to site pages and anchors, and nothing validates
    them, so a renamed page breaks them silently. Add them to the strict build or the link checker.
  - **Versioned docs** (`/v0.7/` beside latest), once people run older releases and need matching docs.
  - **Custom domain** (e.g. `docs.<domain>`), if the marketing site gets one.
  - **Link to the site from the product**: the dashboard's help menu, `flare --help` and the marketing site, instead of GitHub.
  - **Search engine setup**: submit the existing `sitemap.xml` to Google Search Console and add a `robots.txt`.
