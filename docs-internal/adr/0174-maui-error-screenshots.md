# ADR-0174: MAUI error screenshots

Status: Accepted

Date: 2026-10-10

## Context

A stack trace says what failed, not what the user saw. `Flare.Maui` reports exceptions as `app.unhandled_exception`
spans ([ADR-0166](0166-maui-sdk.md)) and the session timeline ([ADR-0170](0170-app-session-timeline.md)) lists them.
A screenshot of the page at the moment of the error closes that gap, but images do not fit the OTLP signals
([ADR-0012](0012-otlp-only-ingestion.md)) and would bloat `spans` if put in an attribute: every span read would drag the blob
along and the value bloom filter would index it.

## Decision

- **A separate table, `app_screenshots`** (migration 0079, single node and cluster): `StartTime`, `ServiceName`,
  `SessionId`, `TraceId`, `SpanId`, `ContentType`, and the image base64-encoded in a String. Base64 avoids a
  binary-safe string path in the insert; the 33% overhead is accepted for images capped at 512 KB. The time column
  is named `StartTime` so the trace retention TTL ([ADR-0143](0143-retention-ttl.md)) covers the table; it is listed
  with `spans` under the Traces signal.
- **`POST /v1/screenshots` on Flare.Ingest**, body = the image (`image/jpeg|png|webp`, at most 512 KB), query =
  `service`, `session_id`, `trace_id` (32 hex), `span_id` (16 hex). It lives under `/v1` so the ingest-key
  middleware, per-key limits' bearer check, the browser CORS policy and the key's service allow-list
  (`IngestKeyScope.RejectsService`) apply unchanged. It inserts directly, with no Redis stream: one small
  best-effort row per crash does not justify the at-least-once pipeline, and a failed insert returns 503 which the
  client ignores.
- **Client, opt-in:** `CaptureScreenshotOnError` (default off; a screenshot can show anything on screen).
  After an unhandled or `RecordException` span is reported, `Flare.Maui` captures the current page with MAUI's
  `CaptureAsync`, re-encodes JPEG at quality 70, 50, 30 until it is under `ScreenshotMaxBytes` (default 300 KB) and
  uploads it. A fatal crash waits up to 3 seconds for the upload, after the span flush. At most 5 per launch and one
  per 10 seconds, so an exception loop cannot flood the server. Failures are swallowed.
- **API:** the timeline events carry `hasScreenshot` (one extra query for the session's screenshot span ids; failure
  hides only the flag), and `POST /api/app-sessions/screenshot {sessionId, spanId}` returns the newest image as
  `{contentType, imageBase64}` under the same service scope as the timeline.
- **Dashboard:** a Screenshot button on such rows shows the image inline, fetched on first open.

## Not decided here

Masking sensitive views before capture (Sentry has this), capturing on ANR spans, and screenshots from the browser
SDK. The server stores whatever the app sends; the app is the place to hide views.

## Consequences

Screenshots are the largest rows Flare stores per error, so they follow the Traces retention but are capped only
by count and size per launch, not per project. A service with a crash loop across many devices can still add up;
a per-key byte limit is the follow-up if that shows up.
