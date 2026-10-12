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
- **Issue workflow, alert integrations and hosted-service parity.** Platform-wide, not specific to any one app or SDK.
  - Issue workflow follow-ups (ADR-0195, ADR-0196, ADR-0197): prefilled new-issue links and issue creation for Jira and
    GitLab.
  - Alert integrations beyond webhook/Slack/Telegram/email/PagerDuty: Teams, Opsgenie, Discord, and a Jira ticket on
    first occurrence.
  - Hosted-service parity items a self-hoster still expects: SSO group mapping and an upgrade guide with tested
    rollback.
- **Privacy, compliance and security hardening.** None of these exist today (the audit log, login throttling, ingest-time
  redaction rules, ingest-key allowlists and TTL retention are already shipped). In rough priority order:
  - **Threat-model findings to fix first** (from [the threat model](../threat-model.md); each was confirmed in code):
    - **WebSocket hardening** (triaged in this order; live-tail, host stats and resource graph, in
      [`Flare.Api/Endpoints`](../../src/Flare.Api/Endpoints)):
      - **Follow-ups to the shipped live-tail limits, Origin check and regex guard** (code in `LiveTail/` and
        `Endpoints/WebSocketOriginGuard.cs`):
        - Document the new `LiveTail__*` options (connection caps, `MaxClientMessageBytes`, filter size limits, regex
          pattern count, length and `RegexMatchTimeout`) and the `Cors:AllowedOrigins` requirement for WebSockets, in
          `docs/` with the ru, fr and zh-CN translations. Say that live-tail regexes are now linear-time, so lookarounds
          and backreferences are rejected, which can break an existing saved view.
        - `LiveTailFilterGuard.TryCompile` calls `pattern.Length` on `attribute.Value` and `bodyFilter.Value`; a client
          sending `"value": null` (System.Text.Json can leave non-nullable strings null) throws and ends that client's
          receive loop. Coalesce to `""`.
        - Run the new `LiveTailHardeningTests` against the old code once to confirm the Origin, 1009 and cap tests fail
          without the fix; they were only seen passing.
        - Host stats and the resource graph got the Origin check but not connection caps or a message-size limit (their
          inbound frames are discarded, so only the socket count matters). Anonymous callers share one bucket, so with
          auth off the per-user cap is a second global cap. The 10 per user, 200 total and filter limits are untuned
          defaults.
      - **Authorization is checked only at upgrade (Low, P3).** Session revocation, sign-out everywhere, user disable, idle
        timeout ([ADR-0206](../adr/0206-session-idle-timeout-password-policy.md)) and PAT expiry do not close an open
        socket, and a tail keeps the `AllowedServices` it was created with after a user is removed from a project (the
        resource-graph watch now captures its scope the same way). Add a
        maximum socket lifetime (the client reconnects) or re-validate session and scope every 1 to 5 minutes.
    - **Hash session tokens at rest.** `Sessions.Id` holds the raw cookie token, while PATs and ingest keys are stored
      as SHA-256 hashes. Store the digest and look up by digest (Sqlite and Postgres migrations; existing sessions can
      simply be invalidated at upgrade).
    - **ClickHouse published with a default password.** `docker-compose.yml` publishes `8123` with the default password
      `flare`. Bind to `127.0.0.1` by default (opt in to wider exposure) and have the installer generate a random
      password.
    - **SSRF guard coverage.** [ADR-0201](../adr/0201-outbound-ssrf-guard.md) covers alert notifiers and OTLP forwarding
      only. Source-repo fetch (which sends a stored access token), S3 archive and the LLM endpoint are unguarded; apply
      `AddOutboundGuard()` there with the same `Outbound__Allow__N` opt-out. Synthetic probes stay exempt on purpose.
    - **Secrets at rest beyond notification channels.** [ADR-0205](../adr/0205-encrypt-notification-secrets-at-rest.md)
      stops at notification channels. Audit and extend it to telemetry-export config, source-link tokens and the
      SSO/LDAP/OIDC client secrets and bind passwords (first confirm whether those are already encrypted).
    - **Dashboard defense-in-depth guards (low risk; the 2026 XSS review found no exploitable telemetry-driven XSS).**
      The only attacker-reachable `{@html}` is the sanitized Markdown panel, and the CSP (nonce scripts, no
      `unsafe-inline`) backstops the rest, so these only matter if the CSP is off (`FLARE_CSP=off`) or stripped by a proxy:
      - Run `safeHttpUrl` over three `href`s that come from internal sources not traced to a server-side validator:
        `release.url` (`UpdateNotice.svelte`, `releases/+page.svelte`) and `node.urls` (`ResourceNode.svelte`).
      - Allow-list the session-replay screenshot `contentType` to `image/png`, `image/jpeg` and `image/webp` before it
        is placed in a `data:` URL (`timeline.svelte.ts`); it comes from the ingest client.
      - Validate the submitted email before building `mailto:` in `feedback/+page.svelte`, since a crafted address can add
        `?cc=` or `?body=`.
      - Consider narrowing `img-src` (it allows any `https:`), so telemetry-derived images cannot load from arbitrary
        hosts and leak the viewer's IP.
      - Not reviewed: `/status/[slug]` and the server routes (`+server.ts`, form actions), CodeMirror and Lottie
        dependency CVEs, `npm audit`, and a check on the built output that the CSP and nonce apply to every response.
    - **Default-open instances.** With `AuthSettings.Enabled=false` every caller is effectively Admin, and ingest keys are
      off by default. Decide whether a fresh install bound beyond loopback should warn loudly or require enabling auth
      and ingest keys.
    - **Smaller items:** pin `tecnativa/docker-socket-proxy` to a digest or version in `docker-compose.yml` and
      `docker-compose.install.yml`; document the trusted-proxy range for Kubernetes deployments; the docs say
      passwords use bcrypt but the threat-model pass found PBKDF2 via ASP.NET `PasswordHasher` (verify, then correct the
      docs and their translations); correct the `SessionAuthenticationHandler` comment and threat-model S9, which say a
      PAT cannot be used on the WebSocket (a PAT `Authorization` header works on the upgrade for non-browser clients, so
      the PAT rate limit covers only the upgrade).
  - **Managed PII redaction dashboard toggle.** Enabling it is `PipelineRules__Pii__*` Ingest config today (restart
    required). A settings page toggle needs a runtime-settings mechanism Ingest does not have: a settings table with
    single and cluster migrations, a polling cache in Ingest, an API and an audit entry. Needs its own ADR.
  - **MFA / TOTP for local and LDAP accounts.** Password plus lockout only; external IdPs already cover MFA. Add TOTP,
    recovery codes and a "require MFA for Admins" setting (WebAuthn/passkeys later).
  - **Live end-to-end test of credential expiry warnings.** Pre-expiry notifications
    ([ADR-0208](../adr/0208-credential-expiry-warnings.md)), the personal access token expiry badge,
    `flare apikey create --expires-in-days` and `expiresAt` in `flare config` export/apply shipped with unit tests only. Still
    to run against a real stack: a warning delivered to a channel for a key and a token close to expiry, no repeat on the next
    check, and a config export/apply round trip of an expiry.
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
