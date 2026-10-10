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
  with OpenTofu acceptance tests against a live stack. `minServerVersion` is set to 0.6.0
  (assumed next release; fix it if the release is numbered differently). Remaining:
  registry publishing. In progress.
- **.NET MAUI / mobile SDK (later).** `Flare.Maui` (ADR-0166, `src/Flare.Maui`) ships the
  `UseFlare()` core: OTLP/HTTP export, disk retry, device/session attributes, `HttpClient`
  and Shell navigation spans, unhandled-exception capture. Remaining for it: native crash
  reports delivered on next launch (Android `ApplicationExitInfo`, iOS MetricKit), Windows,
  and verifying on a device or emulator. The macOS CI job (`maui-ci.yml`) and the NuGet
  publish path (`flare-maui-v*` tag in `nuget-publish.yml`) exist but have not run yet: the
  first release also needs a nuget.org trusted-publishing policy for `Flare.Maui`. Server side (ADR-0167) is done: scoped ingest keys (ADR-0149/0150/0051), the
  Sessions page, and app-version facets on `/errors`. Release health (crash-free sessions and users per version, ADR-0175) is done too, with users counted from `user.id`
  (`SetUser`, ADR-0176; scrubbing, `BeforeSend` and queue limits, ADR-0177). Remaining there: a rollup table if long
  windows prove slow. The per-session timeline is done (ADR-0170). Trimmed/Mono
  stack traces are symbolicated from uploaded dll+PDB symbols (ADR-0168); Native AOT frames on
  iOS/macOS are covered from the `.dSYM`; remaining there: ELF/PDB
  symbols for Linux/Windows AOT, and verifying Mono's offsets on a device.
  The `Platform/` glue's trimming (the core is verified, ADR-0169) is untested until a device build.
  