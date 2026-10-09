# ADR-0170: Per-session timeline

Status: Accepted

Date: 2026-10-09

## Context

[ADR-0167](0167-app-sessions-view.md) listed sessions and linked each to a Traces search on `session.id`.
That shows the session's traces, but not the order of what the user did or where it went wrong. ADR-0167 named a
dedicated timeline as a follow-up.

## Decision

- **`POST /api/app-sessions/timeline`** takes `{sessionId, fromUnixMs?, toUnixMs?}` and returns the session's spans
  oldest first, with the session's service, version, OS and device taken from the first span. Still derived at
  query time from `spans`, with no table or migration.
- **Errors are spans.** Each event carries `isError` (status `ERROR`), the status message and the first
  `exception.type` / `exception.message` found on the span's events, so the `app.unhandled_exception` spans from
  `Flare.Maui` and failed HTTP or navigation spans show their cause inline. Logs are not merged in: only spans carry
  `session.id` today.
- **The time range bounds the scan.** The sessions table links with the session's first and last seen padded by a
  minute. Without a range the server looks back 24 hours, never wider than 7 days; an inverted range is widened to
  five minutes back from its end. A session that is older than the range shows an empty-state hint.
- **Row cap of 1,000 spans**, one extra fetched so the response says `truncated`.
- **Page `/sessions/[sessionId]`** (the table's session id now links here; the "View traces" button keeps the
  ADR-0167 Traces deep link). Each row shows the offset from the first span, the span name linking to the trace
  with `?span=`, screen and error badges, duration and a bar placed on the session's overall time scale.
- **Plain JSON**, `ServiceScope` applied as for other span queries.

## Consequences

One extra indexed scan per page view (same `mapContains` guard on `idx_span_attr_key`). A span flood in one session
is cut at 1,000 rather than paged; revisit if real sessions hit it.
