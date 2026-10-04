# ADR-0102: Pre-aggregated `llm_model_calls` for the `/llm` page

Status: Accepted

Date: 2026-10-04

## Context

[ADR-0100](0100-llm-observability-genai-spans.md) and
[ADR-0101](0101-llm-estimated-cost.md) compute `/llm` from `spans` at query
time and left pre-aggregation open "if volumes warrant it". The page runs two
`spans` scans per load (models and the service picker) over every span in the
window carrying a `gen_ai.*` key, and a 24 h window on a busy LLM-backed
service makes that the slowest on-demand page after `/errors`. ADR-0030 and
ADR-0031 already solved the same shape of problem for the Services tab.

## Decision

Migration 0047 adds `llm_model_calls` (`AggregatingMergeTree`) and a
materialized view `llm_model_calls_mv` on `spans`, keyed
`(TimeBucket minute, ServiceName, Provider, Model)`. Columns: `CallCount`,
`ErrorCount`, `InputTokens`, `OutputTokens` (`SimpleAggregateFunction(sum)`),
`LastSeen` (`max`) and a `quantiles(0.5, 0.95, 0.99)` state. A cluster variant
follows the `_local` + `Replicated…` + `Distributed` pattern of 0022/0023.

- **The view repeats `LlmQueryBuilder`'s expressions verbatim**: the
  model-call condition, the provider/model fallbacks and the token reads.
  A unit test reads both migration files and fails if they drift from the
  builder's constants. A semantic-convention change therefore needs a new
  migration (additive: a new view or column, never an edit) as well as a
  builder edit. This is the cost ADR-0100 named as a reason to wait.
- **Service is a key dimension**, so both the unfiltered view and the
  toolbar's service filter read the rollup, and `ServiceCount` stays an exact
  `uniqExact(ServiceName)`. Unlike the Services tab there is no
  arbitrary-filter case that forces a fallback.
- **Reads happen in `LlmQueryService`** through
  `BuildModelsFromRollup`/`BuildFacetsFromRollup`, producing the same
  columns as the live builders so the reader code is shared. Prices are still
  applied after the read (ADR-0101), so the rollup carries no money.
- **Window edges** floor and ceil to whole minutes, the same bounded
  over-inclusion as ADR-0030 (most visible on the 5 min preset).
- **Rollback valve**: `LlmMetrics:Enabled=false`
  (`LlmMetricsOptions`) makes the service use the live `spans` builders
  again, with no redeploy or migration rollback.
- The live builders stay: they are the valve's target and the reference the
  drift test compares against.

## Consequences

- `/llm` reads a few rows per model per minute instead of scanning spans.
  Latency no longer depends on how many model calls the window holds.
- **No backfill of historical `spans` rows**, same precedent as ADR-0030/0031.
  After upgrading, a window that reaches back before the migration
  under-counts until it has fully rolled past. Setting `LlmMetrics:Enabled=false`
  shows the full history from `spans` in the meantime.
- One more materialized view on `spans`. As with the others, a broken view
  fails silently on ingest and only stops this table updating; check it first
  if `/llm` looks stale while spans are arriving.
- `LastSeen` is a per-bucket max and quantiles are merged sketches, so
  percentiles are approximate exactly as the live `quantiles()` already was.
- Not covered: per-trace or per-service cost, and alert rules on token usage.
