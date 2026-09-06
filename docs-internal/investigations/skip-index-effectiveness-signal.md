# Investigation: is there a real skip-index effectiveness signal for the Indexing page?

Date: 2026-09-06
Related: `planning/roadmap.md` ("Research: a real skip-index effectiveness
signal for the Indexing page")

## Problem statement

An earlier pass deferred shipping skip-index effectiveness on the Indexing
page after finding no reliable, retrospective, production-safe ClickHouse
signal to build it on. The open question left behind: is there a version- or
config-gated ClickHouse mechanism that would make this honest, rather than
invented? This is upstream research (web/GitHub, no live ClickHouse instance
involved) to answer that before anyone picks the item back up again.

## What was checked

- ClickHouse upstream GitHub issues/PRs for prior art on exactly this ask.
- The current `ClickHouse/ClickHouse@master` source for `system.query_log`'s
  schema (`src/Interpreters/QueryLog.h`), to confirm what's actually shipped
  today rather than trusting changelog prose.
- `system.text_log`'s schema, for whether the per-index granule-drop debug
  line is at least joinable back to a query after the fact.

## Findings

1. **This exact feature was requested upstream and already has a name.**
   [ClickHouse/ClickHouse#78676](https://github.com/ClickHouse/ClickHouse/issues/78676)
   ("`system.query_log` should record if the query used skip indexes"),
   opened April 2025, asks for precisely the retrospective, per-query signal
   the Indexing page would need — analogous to `query_log`'s existing
   `projections` column for materialized-view/projection usage.

2. **It was built, merged, and reverted the same day.**
   [ClickHouse/ClickHouse#99793](https://github.com/ClickHouse/ClickHouse/pull/99793)
   added `skip_indices Array(LowCardinality(String))` to `system.query_log`,
   populated with the names of indexes that actually filtered granules
   during execution (not merely considered at planning time) — real,
   retrospective, joinable to every other `query_log` row for free, exactly
   the shape this investigation was looking for. It merged ~2026-03-28, then
   was reverted by the same maintainer the same day. Reason: with
   `use_skip_indexes_on_data_read=1` — now the **default**, landed via
   [ClickHouse/ClickHouse#93407](https://github.com/ClickHouse/ClickHouse/pull/93407)
   — skip-index filtering applied during the data-read phase wasn't reliably
   captured, so `skip_indices` could come back empty even when an index
   genuinely pruned granules. A false negative, not just noise: worse than
   shipping nothing, since it would make an Indexing page built on it
   actively claim an effective index was useless.
   - The author said (2026-04-02) they'd resubmit; a maintainer asked again
     (2026-04-29 - "later means when?") with no resolution found since, and
     no follow-up PR located.

3. **Confirmed against current source, not just changelog prose.** Pulling
   `QueryLog.h` off `master` directly today shows no `skip_indices` or
   equivalent column — `query_log` still only carries `projections`. The
   feature is not sitting in an unreleased-but-merged state; it plain isn't
   in the codebase.

4. **Everything the prior pass already ruled out is still ruled out.**
   `EXPLAIN indexes = 1` / `EXPLAIN ESTIMATE` remain single-query, not
   retrospective dashboard traffic; `query_log`'s existing counters
   (`SelectedMarks`/`SelectedRows`/`SelectedRanges`) still conflate
   primary-key pruning with skip-index pruning.

5. **Minor refinement, doesn't change the conclusion:** `system.text_log`
   does carry a `query_id` column, so the per-index "has dropped N/M
   granules" debug line is at least joinable back to a specific query if a
   deployment opts into Debug-level logging. Doesn't remove the objection —
   it's still non-default logging that has to be turned on *before* the
   traffic you'd want to analyze happens, not something retroactively
   enabled on demand.

## Conclusion

No version- or config-gated ClickHouse mechanism exists today that would
make a real skip-index-specific signal honest. The closest thing —
upstream issue #78676 / PR #99793 — is the right thing to track, not
reinvent: it's the same signal this investigation would otherwise have to
ask ClickHouse to build. It's currently blocked on a correctness bug tied to
a setting (`use_skip_indexes_on_data_read`) Flare doesn't control on
self-hosted ClickHouse, and has been dormant since 2026-04-29.

Action for whoever revisits this: check whether #78676/#99793 (or a
successor) has landed and is reliable under the default
`use_skip_indexes_on_data_read=1` before building anything skip-index-
specific. Until then, the fallback identified in the original pass stands:
a differently-labeled, genuinely-computable proxy from `system.query_log`
(e.g. "% of queries reading under N% of their table's total rows") — real,
just not skip-index-specific, since primary-key pruning contributes too.

## Unresolved / follow-ups

- Re-check [ClickHouse/ClickHouse#78676](https://github.com/ClickHouse/ClickHouse/issues/78676)
  periodically for a successor PR — it was reopened in spirit by the
  maintainer's 2026-04-29 nudge but has no tracked follow-up as of this
  writing.
- No live verification against Flare's own ClickHouse version was possible
  or attempted here (no MCP/ClickHouse connection in this session) — this
  is upstream-source research, not a claim about what any specific
  self-hosted version currently returns.
