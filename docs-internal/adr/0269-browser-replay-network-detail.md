# ADR-0269: Browser replay: request method, headers and bodies

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0266](0266-browser-replay-console-network.md) recorded the network list as URL, status and timing only, and left the
request method, headers and bodies open because they leak: headers carry credentials and cookies, bodies carry whatever the
page sent, and neither is masked the way form fields are. It also noted the method cannot come from the browser's
`PerformanceObserver`; it needs the plugin to wrap `fetch` and `XMLHttpRequest`, and the plugin only does that when
`recordHeaders` or `recordBody` is on. The use that justifies paying for it: a failed request in a replay is much easier to
act on when the list says it was a `POST` to `/api/orders` that returned 500 with `{"error":"stock"}`, instead of only a path
and a status.

## Decision

- **A separate opt-in, `networkDetail`, that only applies when `captureNetwork` is on.** `true` records the method.
  `{ headers: string[], bodies: boolean, bodyLimit?: number }` adds headers and bodies. Off by default, so every existing
  recording and integration is unchanged, and the wrapping of `fetch`/XHR (the one new risk to the host page) happens only for
  an integrator who asked for it. The plain list keeps using the `PerformanceObserver` and wraps nothing.
- **The method alone does not record headers.** The plugin treats any truthy `recordHeaders` as "wrap the calls", so the
  method-only mode passes `{ request: false, response: false }`, which is truthy and records no header. Everything the plugin
  hands to `transformRequestFn` still passes through `sanitizeRequest`, which builds a fresh object, so a header or body the
  plugin read is dropped unless this ADR's rules keep it.
- **Headers are an allowlist, not a blocklist.** Only the names the integrator lists are kept, for both sides of the request,
  lower-cased, values cut to 200 characters. Names that look like credentials (`auth`, `cookie`, `token`, `secret`,
  `password`, `key`, `session`, `credential`) are refused even when listed: `Authorization` and `Set-Cookie` can never be
  recorded by a typo or a copied config. A blocklist would store the next header nobody thought of.
- **Bodies are kept only for failed requests, only JSON, and redacted.** Status 400 or above; the success path, which is
  almost all traffic, stores no body at all. The plugin reads only bodies whose `Content-Type` contains `json`, so no binary,
  form or multipart body is read. `redactJsonBody` parses the text (anything that does not parse, including the plugin's own
  `[rrweb/network] ...` placeholders, is dropped), replaces the value under any key that looks like a secret (`password`,
  `token`, `secret`, `auth`, `apiKey`, `card`, `cvv`, `iban`, `ssn`, `otp`, ...) at any depth with `[redacted]`, caps depth
  and item counts, and cuts the result to `bodyLimit` characters (default 2048, clamped 256 to 8192). A key-name redactor
  cannot know that `note` holds an address; the documentation says so and tells integrators whose error responses echo
  personal data to leave `bodies` off.
- **No server change.** The values travel as ordinary fields of the existing `rrweb/network@1` events, stored as before and
  counted by the existing event and byte caps ([ADR-0265](0265-browser-replay-follow-ups.md),
  [ADR-0268](0268-browser-replay-byte-limits.md)). The dashboard's `extractNetwork` reads the new fields defensively (a chunk
  is whatever a client posted) and `WebReplayCapture` shows the method beside the status and, on a row that has detail, an
  arrow that opens the headers and bodies. Recordings made without `networkDetail` look as before.

## Consequences

- A failed request in a replay shows its method and, when asked, the error body, in the same view as the video.
- The recorder now wraps `fetch` and XHR for integrators who opt in, the code ADR-0266 avoided owning. The plugin's wrapper
  was checked in headless Chromium: a streaming-body `fetch` and `XMLHttpRequest` PUT behave as without it, and recorded
  `Authorization`, `Cookie` and `Set-Cookie` never reach the events.
- Redaction is by key name, so it is a safety net, not a guarantee. Sending bodies stays an explicit choice per integration.
- Reading a response body clones it and waits up to 500 ms (the plugin's own timeout); this happens only for JSON responses
  when `bodies` is on.
