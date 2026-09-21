# ADR-0034: Pipeline rule dry-run/preview mode (Phase 2)

Status: Accepted

Date: 2026-09-21

## Context

[ADR-0033](0033-pipeline-rules-extraction-redaction.md) shipped Phase 1 of
user-defined field extraction/redaction rules: the rule engine itself
(`Flare.Ingest`'s `PipelineRuleConditionMatcher`/`PipelineRuleExecutor`,
applied at flush time), full CRUD, and a dashboard management page. It
explicitly deferred a dry-run/preview mode - running a candidate rule
against sampled recent logs and showing a before/after diff before the
rule is saved and starts mutating real ingest traffic - to a later phase,
since redaction/extraction are irreversible at the point they run and a
too-broad pattern can't be undone for already-ingested rows.

`docs-internal/planning/roadmap.md` tracked this as the one remaining
sub-item once Phase 1 shipped. This ADR covers that remaining item.

## Decision

**A rule's actions are applied in-process against a bounded, most-recent
sample of logs already matching its own `Condition` - pulled via the
existing `/api/logs/search` path - and returned as before/after pairs.
Nothing is written.**

- **Mirror, don't reference, same boundary Phase 1 already drew**:
  `Flare.Api` gets its own `PipelineRuleActionExecutor`, a pure
  `LogEventDto → LogEventDto` mirror of `Flare.Ingest.Pipeline.Rules.PipelineRuleExecutor`'s
  action-application logic (`ExtractRegex`/`RedactRegex`, same fail-closed
  100ms-timeout/invalid-pattern posture, same per-pattern compiled-`Regex`
  cache). This is the same "mirror across the Ingest/Api boundary rather
  than add a cross-project reference" decision ADR-0033 already made for
  `PipelineRuleConditionMatcher`/`PipelineRule` itself - see that ADR's
  "Cross-service read, no shared project reference" note and
  `Flare.Api.Model.LogEventDto`'s own remarks.
- **No condition-matcher mirror needed.** Unlike Phase 1's ingest-time
  matching (a live `LogEvent` that must be matched against every enabled
  rule's condition), a preview only ever evaluates *one* rule (saved or
  draft) at a time, and the sample is already pulled via
  `LogQueryService.SearchAsync` using that same rule's `Condition` as the
  search filter - ClickHouse's own `LogFilterSqlBuilder` translation does
  the condition matching, so every `LogEventDto` handed to
  `PipelineRuleActionExecutor.Apply` already matches by construction.
  There is deliberately no `PipelineRuleConditionMatcher`-equivalent
  mirror in `Flare.Api`. Accepted consequence: the sample is scoped by
  ClickHouse's RE2-based `match()`, not the exact .NET `Regex` engine
  `PipelineRuleConditionMatcher` uses at ingest time for attribute-filter
  regexes - the same engine-choice gap `LogFilterMatcher`'s own remarks
  already document and accept for live-tail.
- **Reuses `/api/logs/search`'s existing defaults/caps as-is.** The
  preview's sample inherits whatever `LogSearchQueryBuilder` already
  applies - the 1-hour default lookback when `Condition.From`/`To` are
  unset, and every existing ClickHouse execution-time/row-count safety
  cap. No new time-window concept was introduced.
- **Sample size: a fixed `PreviewSampleSize = 20` constant**, not a
  request-configurable parameter. A preview is rendered in a dialog for a
  human to read individual before/after pairs, not a data table - far
  below `LogSearchQueryBuilder.DefaultPageSize` (200). No pagination
  either; one shot, same simplicity `AlertEndpoints`' dry-run `/test` pair
  already has (also no configurable sample count - it evaluates over the
  rule's own fixed `WindowSeconds` instead).
- **Endpoint pair mirrors `AlertEndpoints`'s saved/draft `/test` shape
  exactly**: `POST /api/pipeline-rules/{id}/preview` (saved, more specific
  route registered first) and `POST /api/pipeline-rules/preview` (draft,
  same `PipelineRuleRequest` body CRUD already accepts). Like
  `AlertEndpoints`'s draft test, the draft preview does **not** call
  `PipelineRuleRequest.Validate()` first - an incomplete draft still being
  edited (e.g. no actions yet) previews leniently rather than 400s, same
  posture `AlertEndpoints.HandleTestDraftAsync` already takes.
- **Response shape**: `PipelineRulePreviewResult { SampledCount,
  ChangedCount, Matches: PipelineRulePreviewMatch[] }`, where each match
  carries `EventId`/`Timestamp`/`ServiceName`, `Before`/`AfterBody`, and
  `Before`/`AfterAttributes` (the `Log` bag only - the one bag an action
  can ever touch) plus a `Changed` flag. `Changed` is computed by the
  endpoint (body or any `Log` attribute differs from before), not left for
  the dashboard to infer, so the "N of M sampled logs would change"
  summary and the per-row badge always agree.
- **Wire format**: `PipelineRulePreviewMatch` has its own `DateTimeOffset`
  (`Timestamp`), and `PipelineRulePreviewResult` nests a list of it, so
  neither can carry `[GenerateTypeScript]` - both get hand-written
  MemoryPack TypeScript companions (`$lib/memorypack/`), same convention
  `PipelineRule.ts`/`AlertTestResult.ts` already set.
- **Dashboard**: a "Preview" action in `PipelineRuleFormDialog.svelte`,
  always run against the form's current (possibly unsaved) field values
  via the draft endpoint - even when editing a saved rule - so previewing
  an in-progress edit never falls back to testing the still-saved version.
  Same reasoning `AlertRuleFormDialog.svelte`'s `handleTest` already
  documents for its own draft-only test call.

## Alternatives considered

- **A separate, request-configurable sample size/window.** Rejected for
  Phase 2: reusing `/api/logs/search`'s existing filter/window/cap
  handling as-is means zero new query-safety surface to reason about, and
  a fixed small sample is enough to judge whether a pattern behaves as
  intended - the roadmap item's own ask ("run against sampled recent
  logs") didn't call for tuning knobs.
- **Re-deriving condition matching in `Flare.Api` for preview, mirroring
  `PipelineRuleConditionMatcher` the way ADR-0033 mirrors it into
  `Flare.Ingest`.** Rejected: preview only ever evaluates a single rule
  against a sample already scoped by that same rule's condition via SQL -
  there is nothing left to re-match in memory, and mirroring an unused
  code path would just be dead weight.
- **A real diff/highlight of changed substrings** (character-level diff
  between before/after `Body`). Deferred: the dashboard shows full
  before/after text plus a per-row `Changed` badge, which is enough to
  judge a rule's effect at Phase 2's scope; character-level highlighting
  can be added later without changing the API shape.

## Consequences

- Closes the "Pipeline rule dry-run/preview mode" roadmap item ADR-0033
  left open - the Phase 1 + Phase 2 pipeline-rules feature is now
  complete end-to-end (author → preview → save → applied at ingest).
- A preview's sample can differ slightly from what the rule would actually
  see at ingest time for two reasons, both accepted: (1) it samples
  already-stored (post-Drain-clustering, post-any-earlier-rule) logs, not
  a live pre-flush event; and (2) ClickHouse's `match()` vs. .NET `Regex`
  engine gap noted above for attribute-filter regex conditions. Neither
  affects the action-application logic itself (`PipelineRuleActionExecutor`
  runs the exact same regex/replace semantics `PipelineRuleExecutor` does
  at ingest time), only which rows get sampled.
- `docs/how-to/manage-pipeline-rules.md`'s "no dry-run/preview" caveat is
  now stale and removed in the same change as this ADR.

## Related documentation

- [ADR-0033](0033-pipeline-rules-extraction-redaction.md) - Phase 1: the
  rule engine, CRUD, and management page this phase adds preview to.
- `src/Flare.Api/Query/PipelineRuleActionExecutor.cs` - the mirrored
  action executor.
- `src/Flare.Api/Endpoints/PipelineRuleEndpoints.cs` - the preview
  saved/draft endpoint pair.
- `docs/how-to/manage-pipeline-rules.md` - the user-facing how-to, updated
  with a "Preview a rule before saving" section.
