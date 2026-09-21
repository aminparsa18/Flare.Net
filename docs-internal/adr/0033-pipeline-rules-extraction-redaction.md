# ADR-0033: User-defined field extraction/redaction at ingest (Phase 1: the rule engine)

Status: Accepted

Date: 2026-09-21

## Context

`docs-internal/planning/roadmap.md` had an open item: `Flare.Ingest` only
does Drain pattern clustering at flush time - there's no
user-configurable way to pull structured fields out of a log's `Body`/
attributes, or to redact sensitive data, before it lands in ClickHouse.
The roadmap item scoped this as rules applied locally in `Flare.Ingest`
at the same flush-time seam as Drain clustering (not a collector-config-
push mechanism - Flare has no fleet of remote OTel collectors to push
config to), reusing `LogFilter` as the scoping condition the same way
it's already reused verbatim for `/api/logs/*` and `AlertRule.Condition`,
and explicitly called out avoiding a bug seen in prior art: a rule with
no scoping condition silently applying to every log line instead of just
the ones it's meant for.

The roadmap item also asked for a dry-run/preview simulator (run a
candidate rule against sampled recent logs and show before/after). This
ADR covers **Phase 1 only - the rule engine itself, full CRUD, and a
dashboard management page.** Preview is a materially separate feature
(reuses `/api/logs/search` sampling plus an in-process before/after
diff) and doesn't block rules from being useful on their own; it's
deferred to a later phase.

## Decision

**A pipeline rule is a `LogFilter` scoping condition plus an ordered
list of regex-based extraction/redaction actions, stored in a new
`pipeline_rules` ClickHouse table, applied by `Flare.Ingest` at flush
time - the same seam Drain pattern clustering already runs at, but
earlier.**

- **Seam and ordering**: `ClickHouseFlushWorker.FlushAsync` now calls a
  new `PipelineRuleAnnotator.AnnotateAsync` immediately before the
  existing `LogPatternAnnotator.AnnotateAsync` call, not after. A rule
  that redacts `Body` needs to run before Drain clustering computes that
  event's `PatternTemplate`, or PII could leak into a cluster template
  even after redaction. Both remain outside the batch's write try/catch
  - an exception in either surfaces through the same failure path a
  failed ClickHouse write already does.
- **Storage**: `pipeline_rules` mirrors `alert_rules`' (ADR/migration
  0003) `ReplacingMergeTree(UpdatedAt)` + `IsDeleted`-tombstone
  CRUD-via-insert shape exactly - `Id, Name, Description, Enabled,
  IsDeleted, ConditionJson, ActionsJson, CreatedAt, UpdatedAt`. Condition
  and actions are stored as opaque JSON, same reasoning `alert_rules.ConditionJson`
  already documents: this table never filters/aggregates on either
  column's contents, only round-trips them through the C# model.
- **Cross-service read, no shared project reference**: `Flare.Api` owns
  writes (`PipelineRuleQueryService`, mirroring `AlertQueryService`).
  `Flare.Ingest` needed a *read* path against the same table - the
  closest existing precedent, `Flare.AlertWorker`, gets its `AlertRule`
  data via a plain `ProjectReference` to `Flare.Api`, but that precedent
  doesn't apply here: `Flare.Ingest` and `Flare.Api` have never
  referenced each other (see `Flare.Api.Model.LogEventDto`'s own remarks
  on why that boundary mirrors types instead), and introducing the first
  ever cross-reference between them for this one feature would be a
  bigger architectural change than the feature itself. Instead,
  `Flare.Ingest` gets its own mirrored `PipelineRule`/`PipelineRuleCondition`/
  `PipelineRuleAction` types (plain POCOs, not `LogFilter`/`AlertRule`
  reused directly) and a `ClickHousePipelineRuleStore` issuing the same
  kind of `SELECT ... FINAL WHERE IsDeleted = 0 AND Enabled = 1` query
  `AlertQueryService.GetEnabledRulesAsync` does, against the
  `IClickHouseClient` `Flare.Ingest` already holds (previously
  write-only, via `ClickHouseLogEventWriter`) - this is that client's
  first read use.
- **Refresh, no cross-replica lock**: `PipelineRuleCache` (a
  `BackgroundService`) polls the store on `PipelineRuleOptions.RefreshInterval`
  (default 30s) and publishes an in-memory snapshot, same poll-loop shape
  as `Flare.AlertWorker`'s `AlertEvaluationWorker` - but *without*
  `AlertEvaluationWorker`'s Redis mutual-exclusion lock. That lock exists
  because concurrent alert evaluation must fire a notification exactly
  once per breach; N `Flare.Ingest` replicas each independently polling
  this same read-only query is harmless - every replica just converges
  on the same snapshot, same as `Patterns.LogPatternOptions`' per-replica
  Drain state already tolerates.
- **Condition matching, no-scoping-condition safety**: `PipelineRuleConditionMatcher`
  mirrors `Flare.Api.Query.LogFilterMatcher`'s pure `LogFilter → bool`
  style field-for-field, against `Flare.Ingest.Model.LogEvent`. An empty
  condition (every field null/empty) matches every log - not a bug, but
  the dashboard's create/edit form always shows an explicit "this rule
  matches all logs" notice in that case, so an unscoped rule is a visible
  and deliberate choice, the mitigation the roadmap item specifically
  asked for.
- **Actions**: `RuleActionKind { ExtractRegex, RedactRegex }` on a flat
  `PipelineRuleAction` record with nullable kind-specific groups
  (`ExtractRegexAction`/`RedactRegexAction`) - the same discriminator
  shape `AlertRule.ConditionKind`/`MetricCondition`/`ExceptionCondition`
  already use, not polymorphic JSON. `ExtractRegexAction`'s pattern uses
  **named capture groups** as the extraction contract: a group's name
  becomes the new `LogAttributes` key directly, so there's no separate
  target-key mapping to keep in sync with the pattern. Regexes are
  constructed with a 100ms `MatchTimeout` and fail closed (the action is
  skipped, not thrown) on a parse or timeout error - same posture
  `LogFilterMatcher.RegexMatches` already takes for attribute-filter
  regexes, and the same "one bad input can't blow up flush-path latency"
  reasoning `Patterns.LogPatternOptions.MaxBodyLength` documents.
  Compiled `Regex` instances are cached per pattern string in a
  process-wide `ConcurrentDictionary`, not recompiled per event.
- **Rule ordering**: multiple matching rules apply in ascending
  `CreatedAt` order, each rule's actions seeing the previous rule's
  output. No explicit priority/reorder UI in Phase 1 - same scope-trim
  `AlertRule` itself accepts (it has no priority field either).
- **Dashboard condition UI**: reuses `AlertRuleFormDialog.svelte`'s
  hand-rolled Services/SeverityNumbers/Search pickers (via the existing
  generic `PopoverMultiSelect.svelte`) rather than a shared filter-
  builder component - confirmed none exists today (`AttributeFiltersRow.svelte`
  is tightly coupled to `logsExplorerContext`, and `AlertRuleFormDialog`
  itself doesn't use it either). Attribute-bag conditions are out of the
  Phase 1 UI (`AlertRuleFormDialog` has the same gap), but
  `PipelineRuleConditionMatcher` still supports them for forward
  compatibility - the cost was trivial once `LogFilterMatcher`'s logic
  was mirrored, and a future UI addition shouldn't need an ingest-side
  change to go with it.
- **Wire format**: `ExtractRegexAction`/`RedactRegexAction`/`PipelineRuleAction`
  have no `DateTimeOffset`/`IReadOnlyList<T>` member (even nested), so
  all three carry `[GenerateTypeScript]` and are real MemoryPack-TS-
  generated classes - confirmed a generated class can nest another
  generated class cleanly (`PipelineRuleAction` nests both action types).
  `PipelineRule`/`PipelineRuleRequest`/`PipelineRuleListResponse` nest
  `LogFilter` and/or a list of actions, so all three are hand-written
  MemoryPack TypeScript companions (`$lib/memorypack/`), same convention
  `AlertRule.ts`/`AlertRuleRequest.ts` already set - `PipelineRule.ts`
  reuses `LogFilter.ts`'s existing `logFilterFromPlain`/`logFilterToPlain`
  directly rather than re-deriving them.

## Alternatives considered

- **`Flare.Ingest` calls `Flare.Api`'s HTTP API to fetch rules**, instead
  of reading ClickHouse directly. Rejected: no precedent for
  service-to-service HTTP calls exists in this codebase (`Flare.AlertWorker`
  reads ClickHouse directly via a shared project, not HTTP), it adds a
  hard runtime dependency from ingest availability on API availability
  that doesn't otherwise exist, and ClickHouse is already the correctly-
  positioned shared source of truth both services already talk to.
- **Redis pub/sub to push rule changes instead of polling.** Rejected:
  nothing in this codebase uses Redis pub/sub today, the added
  complexity (a new message-delivery-guarantee question: what happens to
  a rule change published while `Flare.Ingest` is down) isn't justified
  by a 30-second worst-case staleness window, and polling is the
  established pattern for cross-service config here (`AlertEvaluationWorker`
  itself polls rather than subscribes).
- **A JSON-field-extraction action kind alongside regex.** The roadmap
  item mentioned both regex/JSON field extraction. Deferred: regex alone
  covers both extraction and redaction with one execution model: JSON-
  path extraction would be a second, structurally different action kind
  worth its own scoping pass rather than folding into Phase 1's already
  broad surface.

## Consequences

- A newly created, edited, or disabled rule takes up to `PipelineRuleOptions.RefreshInterval`
  (default 30s) to take effect on any given `Flare.Ingest` replica - an
  accepted, documented staleness window, not a bug (same tradeoff
  `AlertEvaluationWorker`'s own poll interval already accepts for alert
  rule changes).
- `PipelineRuleOptions.Enabled = false` is an immediate, config-only
  kill switch (no redeploy/migration rollback needed) if the engine ever
  misbehaves in production - same role `LogPatternOptions.Enabled`
  already plays for Drain clustering.
- Redaction and extraction are irreversible at the point they run: once
  a rule redacts `Body`, the original text is gone from what's written
  to ClickHouse (by design - that's the point of redaction), and Drain's
  `PatternTemplate` reflects the redacted text too. A rule authored with
  an overly broad pattern can't be "undone" for already-ingested rows,
  only prevented going forward - the dashboard's "matches all logs"
  warning and the deferred Phase 2 dry-run preview are the two
  mitigations for authoring a rule safely before it runs for real.
- Dry-run/preview mode remains open - tracked as a remaining sub-item on
  the roadmap, not closed by this ADR.

## Related documentation

- `db/clickhouse/0003_alert_rules.sql` - the CRUD-via-tombstone shape
  `pipeline_rules` reuses directly.
- `db/clickhouse/0024_pipeline_rules.sql` / `db/clickhouse-cluster/0024_pipeline_rules.sql`.
- `src/Flare.Ingest/Pipeline/Rules/` - the mirrored condition matcher,
  action executor, ClickHouse store, cache, and annotator.
- `docs-internal/adr/0018-alert-worker-extraction.md` - the closest
  existing precedent for a background/ingest-side process consuming
  config authored via `Flare.Api`/the dashboard, and why its
  `ProjectReference`/single-evaluator-lock shape doesn't transfer here.
