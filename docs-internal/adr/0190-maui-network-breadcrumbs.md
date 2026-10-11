# ADR-0190: MAUI network breadcrumbs and offline-aware failures

Status: Accepted

Date: 2026-10-11

## Context

Breadcrumbs ([ADR-0172](0172-maui-breadcrumbs.md)) cover lifecycle, taps, pages and logs, but not the network calls
that usually precede a crash or an error screen. The `HttpClient` spans exist, but carry no sizes, and a failure
with no connectivity looks the same as a server that answered badly.

## Decision

- **Enrich the existing `HttpClient` instrumentation** (`EnrichWith*` callbacks) rather than add a message
  handler, so the SDK needs no change to how an app builds its `HttpClient`.
- **Sizes** are `http.request.body.size` and `http.response.body.size` from `Content-Length`. A chunked or streamed
  body has no size; reading the stream to count it would change the app's behavior.
- **An `http` breadcrumb per call**: `GET host/path 200, 2 KB, 85 ms`, or `... failed: offline` / `failed: <exception type>`.
  The query string is never included (tokens, PII). The breadcrumb is its own trace, not a child of the HTTP span,
  so it does not appear inside the request on the waterfall. It follows `Breadcrumbs`.
- **Offline-aware failures**: the platform glue supplies `Connectivity.NetworkAccess == Internet`. A failed call
  while it is false gets `network.offline = true`. The flag is only set on failure, so a flaky connectivity
  report cannot mislabel a call that succeeded.
- `InstrumentHttpClient = false` turns all of it off, as before.

## Consequences

Every HTTP call adds a breadcrumb span, so chatty apps reach the timeline's 1,000-span cap sooner (the per-session
breadcrumb cap from ADR-0172 is still the follow-up). Sizes are unavailable for chunked bodies. The dashboard
shows the breadcrumb through the existing timeline; a dedicated network view is not built.
