# ADR-0021: Reusable, named notification channels, coexisting with the legacy inline fields

Status: Accepted
Date: 2026-09-11

## Context

`AlertRule`'s notification destination was four mutually-exclusive
inline fields on the rule itself — `WebhookUrl`/`TelegramBotToken`+
`TelegramChatId`/`EmailTo`/`PagerDutyRoutingKey` — "a rule notifies
exactly one channel," per that type's own doc comment. There was no
saved/named channel entity: the same Slack webhook or PagerDuty routing
key had to be re-entered on every rule that should reach it, rotating a
key meant updating every rule referencing it individually, and a single
critical rule couldn't fan out to more than one destination (e.g. Slack
*and* PagerDuty for the same breach). See former
`docs-internal/planning/roadmap.md` entry "Reusable, named notification
channels" (SigNoz's own version,
[signoz#1458](https://github.com/SigNoz/signoz/commit/7881aee3501c5081f020e61badd7c1607fc9946f),
models channels as their own managed, named objects a rule
multi-selects).

## Decision

**A new `NotificationChannel` entity, CRUD'd on its own
`/notification-channels` page, that `AlertRule` references by ID
(`ChannelIds: IReadOnlyList<Guid>`) — shipped additively, coexisting
with the legacy inline fields rather than replacing or migrating them.**

- **Coexistence, not a data migration.** `AlertRule` keeps its four
  legacy inline fields unchanged; `ChannelIds` is a new, separately
  additive column (`db/clickhouse/0017_alert_rules_channel_ids.sql`,
  `Array(UUID) DEFAULT []`) that every pre-existing rule reads back as
  empty — such a rule keeps notifying through its legacy inline channel,
  completely unchanged. `AlertRuleRequest.ValidateChannel()` extends
  (not replaces) its existing mutual-exclusion check: a rule is valid
  with *either* the legacy single inline channel *or* one-or-more
  `ChannelIds`, never both, never neither. The dashboard mirrors this at
  the UI level — a rule being created, or one already on `ChannelIds`,
  gets the channel multi-select; a rule still on its legacy inline
  channel keeps showing that same block when edited. No forced
  migration, no risky one-time data rewrite against a self-hosted
  ClickHouse instance with no migration-runner automation yet (see
  `db/clickhouse/README.md`'s "Migration convention" — every migration
  here is `ALTER TABLE ... ADD COLUMN`, applied by hand, never a
  same-migration data rewrite). A full replace-and-migrate design was
  considered and rejected — see Alternatives.
- **`NotificationChannel`** (`Id`, `Name`, `Description`, `Type`
  [`Webhook`/`Telegram`/`Email`/`PagerDuty`], the matching one of
  `WebhookUrl`/`TelegramBotToken`+`TelegramChatId`/`EmailTo`/
  `PagerDutyRoutingKey`, `CreatedAt`/`UpdatedAt`) is exactly `AlertRule`'s
  four legacy channel fields factored into their own entity — same
  "field present, meaningful only for one mode" convention, just named
  and independently CRUD'd. New `notification_channels` ClickHouse table
  (`db/clickhouse/0016_notification_channels.sql`), same
  `ReplacingMergeTree(UpdatedAt)` / tombstone-delete / `FINAL WHERE
  IsDeleted = 0` CRUD pattern as `alert_rules` (see migration 0003's own
  rationale, unchanged here) — `NotificationChannelQueryService` mirrors
  `AlertQueryService`'s shape directly.
- **Notifier fan-out, not a bigger single-channel dispatch.** Every
  per-channel notifier (`WebhookAlertNotifier`/`TelegramAlertNotifier`/
  `EmailAlertNotifier`/`PagerDutyAlertNotifier`) now takes an explicit
  `NotificationChannel` parameter instead of reading its destination off
  `AlertRule` directly — the rule is still passed, for message
  formatting only. `CompositeAlertNotifier.SendAllAsync` sends to every
  one of a rule's resolved channels concurrently and returns one
  `NotificationResult` per channel. A new `NotificationChannelResolver`
  is the one place "which channels does this rule notify" is decided —
  `ChannelIds` resolved from the store when non-empty, else one
  ephemeral `NotificationChannel` synthesized from the rule's legacy
  inline fields (never persisted, `Id = Guid.Empty` — the same
  "not a real persisted row" sentinel `AlertEndpoints`' own unsaved
  draft-rule send-test already uses). Both `AlertEvaluationWorker` (the
  real fire path) and `AlertEndpoints`'s send-test handlers go through
  this one resolver.
- **`AlertHistoryEntry` gains `ChannelResults: IReadOnlyList<AlertChannelResult>`**
  (new: `ChannelId?`, `ChannelName`, `Type`, `Success`, `StatusCode`,
  `Error`) — a fan-out fire's per-channel outcome, visible in the
  dashboard's history sheet as a per-channel breakdown. The existing
  `NotificationStatus`/`NotificationStatusCode`/`NotificationError`
  fields stay as the backward-compatible summary across every channel
  ("Sent" only if all succeeded, "Failed" if any did, error joining
  per-channel failures) — a single-channel rule's summary is unchanged.
  New `db/clickhouse/0018_alert_events_channel_results.sql` adds
  `ChannelResultsJson` (opaque JSON, same "store opaque, round-trip
  through the C# model" convention `ConditionJson`/`MetricConditionJson`
  already use) to `alert_events`.
- **`NotificationChannelRequest`/`AlertChannelResult` carry
  `[GenerateTypeScript]` directly** (no `DateTimeOffset`/nested
  generator-ineligible member — flat strings + the plain
  `NotificationChannelType` enum) — Roslyn-generated TypeScript mirrors,
  no hand-written companion needed for either. `NotificationChannel`
  itself (has `CreatedAt`/`UpdatedAt`) and `NotificationChannelListResponse`
  (nests it) are hand-written (`$lib/memorypack/`), same reasoning
  `AlertRule.ts`/`AlertRuleListResponse.ts` already document (ADR-0016).
  `AlertRule.ChannelIds`/`AlertHistoryEntry.ChannelResults` were
  appended after every pre-existing field in both the C# records and
  their hand-written TypeScript mirrors, same versioning convention
  `ConditionKind` already established (ADR-0020).

## Alternatives considered

- **Force-migrate every existing rule's inline destination into a new
  `NotificationChannel` row, then drop the inline fields going
  forward.** Cleaner end state, but a real one-time data migration
  against a self-hosted ClickHouse instance with no automated migration
  runner — a bigger, less reversible change to ship in one pass, and
  inconsistent with every `alert_rules` migration to date (0005/0006/
  0012/0014 are all pure `ADD COLUMN`, never a same-migration data
  rewrite). Rejected in favor of the additive coexistence design above;
  a forced migration/cleanup of legacy-channel rules remains a possible
  future follow-up once the channel picker has been the default path
  for a while, not attempted here.
- **A CLI `flare notification-channels` subcommand alongside the
  dashboard page.** Deferred, not built now — same precedent personal
  access tokens shipped without a CLI create command
  (`docs-internal/adr/0019-personal-access-tokens.md`'s equivalent
  scoping call). `AlertsListCommand`'s rule summary shows a referenced
  channel count, not names, until this exists.

## Consequences

- A rule notifies through *either* its legacy inline channel *or* one
  or more saved `NotificationChannel`s — never both. There is
  intentionally no in-place "convert this rule's legacy channel to a
  saved channel" action; editing a legacy-channel rule keeps showing
  that same block unless a future change adds one.
- A rule's `ChannelIds` entry that no longer resolves (its channel was
  deleted after the rule was saved) is silently dropped at fire time —
  the remaining channels still fire rather than the whole notification
  silently no-opping. There's no per-rule "channel missing" surfacing
  yet — a named follow-up, not built now.
- `flare notification-channels` CLI management (create/update/delete
  from the terminal) is not built — see Alternatives above.

## Related documentation

- `docs-internal/adr/0016-memorypack-dashboard-typescript-adoption.md` —
  the generated-vs-hand-written TypeScript split this ADR's design
  leans on directly.
- `docs-internal/adr/0018-alert-worker-extraction.md` — why evaluation
  (and now channel resolution/fan-out) runs in `Flare.AlertWorker`, not
  `Flare.Api`; unchanged by this ADR.
- `docs-internal/adr/0020-metric-threshold-alerting.md` — the most
  recent prior alerting-schema ADR; that ADR's own "Consequences"
  section named this one as a separate, already-tracked roadmap item,
  unaffected by its condition-kind change (this ADR only changes how a
  rule *notifies*, not its condition).
