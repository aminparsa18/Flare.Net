# ADR-0053: Errored requests count as Apdex "frustrated"

Status: Accepted (supersedes the span-classification part of ADR-0032)

Date: 2026-09-25

## Context

[ADR-0032](0032-apdex-score-per-service.md) classifies each root span into
Apdex buckets purely by `DurationNano` against the per-service threshold T.
It never addresses span status, so a request that fails in 5ms counts as
satisfied: a service returning 50% fast errors scores 1.00 ("Excellent")
while the Errors column next to it shows 50%.

The original Apdex specification is duration-only, but it predates
tracing, where a span carries an explicit error status. Most APM tools
users arrive from (e.g. New Relic) treat errored requests as frustrated,
so Flare's duration-only score reads as "healthy" in exactly the cases it
shouldn't. Found while checking
[signoz#6460](https://github.com/SigNoz/signoz/commit/c93cf1ce9515526eb6a667298fec56d3a068ee69)
(an unrelated tolerating-halving fix Flare already gets right).

## Decision

**A root span with `StatusCode = 'STATUS_CODE_ERROR'` is always frustrated,
regardless of duration.** `ServiceApdexQueryBuilder` adds
`StatusCode != {errorStatus:String}` to both the satisfied and tolerating
`countIf`s; frustrated remains the implicit remainder of `RequestCount`,
so errored spans fall into it with no change to `ApdexScoreCalculator` or
the denominator. `STATUS_CODE_UNSET` and `STATUS_CODE_OK` are both
non-errors, same as the Errors column's own `ErrorCount` predicate.

Everything else in ADR-0032 (live query, per-service SQLite thresholds,
500ms default, Table-view-only scope) is unchanged.

## Alternatives considered

- **Keep Apdex duration-only** (spec-literal). Rejected: the score is the
  Services tab's one-number health summary, and one that ignores failures
  is misleading; the separate Errors column doesn't fix the summary.
- **Make it a per-service or global toggle.** Rejected: no user has asked
  for spec-literal scoring, and a toggle makes the same number mean
  different things across installations.

## Consequences

- User-visible scoring change: any service with a nonzero error rate sees
  its Apdex drop on upgrade, with no configuration change. The threshold
  popover's description now says errored requests always count as
  frustrated.
- No migration or backfill: thresholds live in SQLite and Apdex is always
  computed live, so the new rule applies to historical windows too.
- Reverting is equally cheap (drop the predicate), should this ever need
  revisiting.
