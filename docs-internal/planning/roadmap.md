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
- **OpenAPI.NET v3 (`Microsoft.OpenApi` 3.x, OpenAPI spec 3.2).** Blocked on
  `Microsoft.AspNetCore.OpenApi`: 10.0.x caps it at `[2.12.0, 3.0.0)`, so the
  direct pin in `Directory.Packages.props` stays on 2.x. The first release
  that allows 3.x is 11.0 (RC1 requires `[3.10.0, 4.0.0)`), which needs the
  `net11.0` upgrade, so do both together; no 10.0.x servicing release has lifted the cap.
  See the [OpenAPI.NET v2/v3 announcement](https://devblogs.microsoft.com/openapi/openapi-net-release-announcements/).
- **Continuous profiling: a real sender.** Ingest, the Profiles page and the how-to are done
  ([ADR-0141](../adr/0141-continuous-profiling-ingest.md)), verified with hand-written OTLP/JSON.
  The Collector's `pprof` receiver (v0.162) emits samples without a stack table, so it can't
  feed a flame graph yet; re-check on each Collector bump, and try an eBPF profiler as the
  practical source. `profile_samples` has no TTL, same as spans, so retention rides on the
  "Retention policies" item above. OTLP profiles is still Alpha, so re-check the vendored
  proto on each tag bump.
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
  - Known gap: on Android a managed crash also emits a spurious `app.hang`, because the crash handler's flush blocks
    the UI thread for over 2 s (it does not on iOS).
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
  - Issue workflow follow-ups (ADR-0195, ADR-0196, ADR-0197): prefilled new-issue links and issue creation for Jira and
    GitLab.
  - Alert integrations beyond webhook/Slack/Telegram/email/PagerDuty: Teams, Opsgenie, Discord, and a Jira ticket on
    first occurrence.
  - Hosted-service parity items a self-hoster still expects: SSO group mapping and an upgrade guide with tested
    rollback.
- **Error tracking closer to Sentry.** The scope boundary is in
  [the architecture page's non-goals](../../docs/explanation/architecture.md#non-goals). In rough priority order:
  - **Check-in follow-ups** ([ADR-0260](../adr/0260-cron-job-checkins.md)): auto-creating a monitor on first ping; check-ins as status page components.
  - **Suspect commits.** Releases carry a commit and services can have source links ([ADR-0095](../adr/0095-exception-source-links.md)),
    but nothing maps a new error group to the commit or author that introduced it.
  - **Browser session replay**, on the pattern of the MAUI replay-lite ([ADR-0192](../adr/0192-maui-session-replay-lite.md)).
    Other client SDKs are covered by the MAUI item above; per [ADR-0012](../adr/0012-otlp-only-ingestion.md) they stay thin over OTLP.
- **APM depth.** Flare receives OTLP and ships no agent, so these close gaps without changing that.
  - **A one-line .NET setup package** that configures the OTLP exporter, sampler and the common meters for Flare
    (a thin OpenTelemetry helper, not a proprietary format, so it fits ADR-0012).
  - **A per-statement database view:** slowest normalized statements with rate and p95, from the `db.*` span attributes.
    Check first that N+1 detection and span-duration comparison don't already cover the need.
  - **Span-to-profile links**, once a real profiling sender exists (see the continuous profiling item above). Check
    whether profiles already carry trace and span ids before designing it.
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
- **First npm release of the CLI (`@apardev/flare-cli`, [ADR-0214](../adr/0214-npm-distribution-of-the-cli.md)).** The six packages exist at 0.1.9 (published by hand) with trusted
  publishers set, and the `npm Publish` dry run passes, but the trusted-publishing publish itself has never run. Bump `Flare.Cli.csproj` to 0.1.10 and push
  `flare-cli-v0.1.10` to release NuGet and npm together, then confirm provenance shows on npmjs.com. Follow-ups: musl Linux and Windows-on-ARM binaries, and
  mention npm in the csproj `Description` while the version is bumped.
