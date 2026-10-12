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
- **Terraform / OpenTofu provider.** Design settled in ADR-0146 (separate Go repo,
  hand-written schemas, names as the stable key, write-only secrets, `/api/version`
  compatibility). Server prerequisites (phases 1 and 3 name uniqueness) are done and
  the provider repo has `flare_notification_channel`, `flare_alert_rule`, `flare_slo`,
  `flare_maintenance_window`, `flare_pipeline_rule`, `flare_ingest_key`,
  `flare_service_account`, `flare_dashboard` (layout JSON passed through as-is,
  ADR-0147 makes names unique per project), `flare_metric_attribute_rule`,
  `flare_forwarding_target` and `flare_archive_settings` (ADR-0157), verified
  with OpenTofu acceptance tests against a live stack. `minServerVersion` is 0.6.0, the
  first Flare release with the server-side prerequisites. Provider v0.1.0 is published on
  the Terraform Registry as `aminparsa18/flare` (GPG-signed releases via GoReleaser).
  Remaining: list it on the OpenTofu Registry (a PR to `opentofu/registry` with the same
  public key).
- **.NET MAUI / mobile SDK (later).** `Flare.Maui` (ADR-0166, `src/Flare.Maui`) ships the
  `UseFlare()` core: OTLP/HTTP export, disk retry, device/session attributes, `HttpClient`
  and Shell navigation spans, unhandled-exception capture. Remaining for it: Windows,
  and verifying on a device or emulator. Native crashes found on the next launch (Android `ApplicationExitInfo`, iOS MetricKit,
  ADR-0178) are done apart from the follow-ups below. The macOS CI job (`maui-ci.yml`) and the NuGet
  publish path (`flare-maui-v*` tag in `nuget-publish.yml`) exist but have not run yet: the
  first release also needs a nuget.org trusted-publishing policy for `Flare.Maui`. Server side (ADR-0167) is done: scoped ingest keys (ADR-0149/0150/0051), the
  Sessions page, and app-version facets on `/errors`. Release health (crash-free sessions and users per version, ADR-0175) is done too, with users counted from `user.id`
  (`SetUser`, ADR-0176; scrubbing, `BeforeSend` and queue limits, ADR-0177). Remaining there: a rollup table if long
  windows prove slow. The per-session timeline is done (ADR-0170). Trimmed/Mono
  stack traces are symbolicated from uploaded dll+PDB symbols (ADR-0168); Native AOT frames on
  iOS/macOS are covered from the `.dSYM`; remaining there: ELF/PDB
  symbols for Linux/Windows AOT, and verifying Mono's offsets on a device.
  The `Platform/` glue's trimming (the core is verified, ADR-0169) is untested until a device build.
    Next for `Flare.Maui`, from the gap against Sentry's MAUI SDK (what a team would miss moving over), roughly in
  priority order:
  - Device verification: `ExampleApp.Maui` is verified on a real Android device (arm64, Debug build): export, the disk
    queue and its replay, breadcrumbs, `app.hang` with a stack, ANR and native-crash reporting on relaunch, a managed
    crash, scrubbing and a screenshot row. Remaining: an Android emulator, an iOS simulator and an iOS device; a Release
    build to prove the `Platform/` glue's trimming; and the Sessions page's release comparison. Known gap: a managed
    crash also emits a spurious `app.hang`, because the crash handler's flush blocks the UI thread for over 2 s.
  - Native crash follow-ups (ADR-0178): server-side symbolication of the raw iOS offsets against a dSYM; verify the
    MetricKit renderer on an iOS device (the tombstone one is verified on Android); Android before 11
    and iOS jetsam out-of-memory kills, which have no report to read; an exact iOS crash time (it is estimated from
    the last lifecycle event).
  - Mobile performance follow-ups (ADR-0180; verified on a real Android device, arm64 Debug, by Maestro flows 12-14):
    an iOS device, an Android emulator and a Release build; a trend over time and an alert rule on a start/load
    regression; iOS process start time; time-to-first-frame rather than first resume (a warm start is only the
    `OnRestart` to `OnResume` gap, about 12 ms).
  - Profiling follow-ups (ADR-0181): iOS, `app.start` profiles, managed frames, and a flame graph in the dashboard.
  - Windows support.
  - Scrub exception events recorded by other instrumentation (ADR-0191): an `ActivityEvent` is immutable once added, so
    this needs a different mechanism than a processor.
  - Other client SDKs on the same server pieces (sessions, release health, scoped keys): Android native, iOS native,
    React Native and Flutter, each thin over OTLP.
  - Issue workflow follow-ups (ADR-0195): ownership rules from CODEOWNERS paths (the groups list carries no stack
    frames to match), showing the owner in alert messages, a "my errors" filter, and prefilled new-issue links and
    issue creation for Jira and GitLab.
  - Alert integrations beyond webhook/Slack/Telegram/email/PagerDuty: Teams, Opsgenie, Discord, and a Jira ticket on
    first occurrence.
  - Hosted-service parity items a self-hoster still expects: SSO group mapping and
    an upgrade guide with tested rollback.
- **Privacy, compliance and security hardening.** None of these exist today (the audit log, login throttling, ingest-time
  redaction rules, ingest-key allowlists and TTL retention are already shipped). In rough priority order:
  - **Data-subject erasure and export (GDPR Art. 15/17).** Telemetry can only leave through TTL; there is no delete or
    export by predicate. Add an admin endpoint, CLI command and page that deletes (`ALTER ... DELETE`, single + cluster)
    or exports every log, span, metric, exception and session row matching a `user.id`, email or IP, with a dry-run
    count, a confirmation step, mutation-progress tracking (mutations are async, see `src/Flare.Api/README.md`) and an
    audit-log entry. Needs an ADR; sits next to [ADR-0143](../adr/0143-retention-ttl.md).
  - **Managed PII detection at ingest.** Redaction today means hand-written regex pipeline rules. Ship a built-in rule
    set (emails, Luhn-checked card numbers, IBANs, JWTs, bearer tokens, private keys) reusing the patterns in
    `AiRedactor`, one toggle, previewable with the [ADR-0034](../adr/0034-pipeline-rules-preview.md) simulator. A
    report-only scan that lists attributes that look like PII, without changing data, is a smaller first step.
  - **Security response headers.** No HSTS, `X-Content-Type-Options`, `Referrer-Policy`, `frame-ancestors` or CSP is set
    by Flare.Api or the dashboard's Node server. Add them with a configurable CSP and document the reverse-proxy
    interaction.
  - **MFA / TOTP for local and LDAP accounts.** Password plus lockout only; external IdPs already cover MFA. Add TOTP,
    recovery codes and a "require MFA for Admins" setting (WebAuthn/passkeys later).
  - **Webhook SSRF guard.** Alert webhooks, status-page subscriptions and forwarding targets post to user-supplied URLs
    with no private-range check (`WebhookPost`). Add an opt-out block on loopback, link-local (`169.254.169.254`) and
    RFC1918 targets, with an allowlist, enforced at connect time so redirects and DNS rebinding can't bypass it.
  - **Audit export, streaming and read auditing.** CSV/JSON export, optional forwarding to a syslog/webhook sink, and
    hash-chaining so tampering is detectable ([ADR-0079](../adr/0079-audit-log.md) covers config changes only). An
    opt-in record of who queried telemetry is the other half compliance reviewers ask for.
  - **Encrypt stored channel secrets.** Webhook URLs, bot tokens and routing keys come back masked, but nothing in
    `src/Flare.Api` calls Data Protection on them, so they look plaintext at rest. Verify first; if so, wrap them with
    `IDataProtector` (the key ring is already shared via Redis) and add key rotation, otherwise document it.
  - **Session and credential hygiene.** Sessions have an absolute 14-day lifetime and no idle timeout. Add an idle
    timeout, "sign out everywhere" per user, a configurable password policy (only `MinPasswordLength = 8` in
    `AdminProvisioner` today), and expiry/rotation warnings for ingest keys and personal access tokens.
  - **Data-residency, DPA and sub-processor docs** (replaces the data-residency mention in the hosted-parity line above):
    a `docs/explanation` page on where each store lives, what can leave the box (AI providers, notification channels,
    update check), and a no-phone-home statement.
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
