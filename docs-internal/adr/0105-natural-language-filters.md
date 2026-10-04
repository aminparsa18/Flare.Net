# ADR-0105: Natural language to typed filters

Status: accepted

## Context

Building a Logs or Traces filter takes several clicks across the time range, service, severity,
attribute and structural-query controls. Users know what they want in words ("5xx on checkout in
the last hour, excluding health checks"). The shared AI guard rails (ADR-0103, ADR-0104) apply:
off by default, bring your own model, redacted, bounded, recorded.

## Decision

- **The model proposes a filter, never a query.** It is asked for one JSON object in a fixed
  vocabulary (time range preset or window, services, severity, text search, attribute conditions,
  span status, structural query). It cannot express SQL, and nothing it writes is executed.
- **Whitelisting parser.** `NlFilterParser` is pure. Unknown properties are ignored; enums, bags and
  operators are matched against the real ones; counts and lengths are capped; a custom range must be
  ordered, at most 365 days and not in the future; comparison operators need a numeric value; a
  structural query must pass `TraceStructureSqlBuilder.Validate`. An invalid piece is dropped and
  reported in `warnings` rather than failing the whole filter.
- **Applied as ordinary state.** The response uses the explorers' saved-view field names, and the
  dashboard restores it through `applySavedViewState` (the path `?state=` links and saved views
  use). The user sees normal editable chips and the usual query caps and validators run when the
  search executes. The restore replaces the current filters.
- **Target-specific.** Severity and text search are log-only; span status and structure are
  trace-only; Traces offer only the fixed-duration presets. Out-of-target parts are dropped with a
  warning. Trace duration conditions are expressed inside a structural condition (`minDurationMs`),
  since the traces filter state has no standalone duration field.
- **What leaves the host.** The user's request (redacted) and up to 100 known service names, plus the
  current time. No log, span or attribute data. The redacted prompt is logged at Debug; the call is
  audited as `ai-nl-filter`. Request length is capped at 500 characters and the prompt at
  `Ai__MaxInputChars`.
- **Endpoint.** `POST /api/ai/nl-filter`, any signed-in user, 404 when AI is not configured. The
  dashboard hides the box unless `GET /api/ai/status` reports enabled.

## Consequences

- One model call per submission, on demand only; no caching.
- A model that ignores the format gets a "try rephrasing" error; a model that picks a wrong
  attribute key produces a filter that matches nothing, which the user can see and fix in the chips.
- Metrics, Errors and the other pages are not covered.
