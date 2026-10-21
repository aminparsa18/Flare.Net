# ADR-0266: Browser replay: console output and network requests

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0264](0264-browser-session-replay.md) records the DOM, which shows what the user saw but not why: a failed request or a
logged warning that explains the broken screen is not in the video. [ADR-0265](0265-browser-replay-follow-ups.md) left console
and network capture open because they change what is recorded, and neither is masked the way form fields are. Canvas recording
stays open.

## Decision

- **Use rrweb's own record plugins, not our own patches of `console` and `fetch`.** `@rrweb/rrweb-plugin-console-record` and
  `@rrweb/rrweb-plugin-network-record`, pinned to 2.1.7 like `@rrweb/record`, passed to `record({ plugins })`. They emit
  ordinary rrweb events (type 6, `plugin: 'rrweb/console@1'` or `'rrweb/network@1'`), so the recorder's buffering, the
  `onError` rolling buffer, the upload, `POST /v1/replay` and the stored chunks need no change: the server only checks that
  an event has a numeric `type` and `timestamp`. Writing our own would mean owning the monkey-patching of `console`, `fetch`
  and XHR, which is where such code breaks on real pages.
- **Both are opt-in, and the default is the narrow one.** `captureConsole` takes `true` (`warn` and `error`) or a list of
  levels; `captureNetwork` is a boolean. Console output regularly holds data the page never displays (tokens, ids, whole
  objects), and request URLs hold query strings, so a recorder that captured them by default would store more than the
  masking promise in ADR-0264 covers. The documentation says plainly that nothing logged is masked.
- **Network records timing and status only.** The plugin is configured with `recordHeaders` and `recordBody` off, which also
  keeps it on the browser's `PerformanceObserver` and so avoids wrapping `fetch` and XHR. Only `fetch`, `XMLHttpRequest` and
  beacon requests are kept (not every image and stylesheet). `sanitizeRequest` then drops the query string and fragment unless
  `networkKeepQuery` is set, caps the URL at 300 characters, and removes the recorder's own `{endpoint}/v1/` uploads, which
  would otherwise list themselves forever. What this costs: no request method (the observer does not know it), and no status
  in browsers whose resource timing lacks `responseStatus`. Adding method, headers or bodies later is a plugin option plus a
  redaction design, not a format change.
- **Console arguments are bounded at the source.** 500 characters per string, 30 keys, 3 levels deep. The `error` level also
  makes the plugin record uncaught errors and unhandled rejections, which is what `onError` mode already triggers on.
- **The dashboard reads these events itself.** `extractConsole` and `extractNetwork` (pure, in `web-replay-events.ts`) pull the
  entries out of the events already loaded for the player, and `WebReplayCapture` lists them under it: a **Console** and a
  **Network** list, a row seeks the player to its moment, rows after the current position are dimmed. Not rrweb's
  `rrweb-plugin-console-replay`, which prints to the viewer's own browser console. At most 2 000 entries of each kind are read
  and 300 drawn. A request's start is its event time minus its duration, since the plugin reports it when it finishes.
  The Replayer ignores plugin events it has no replay plugin for, so playback is unchanged. A recording made before this
  change, or without these options, shows no list.

## Consequences

- A replay can show the failed request or the warning that explains what the video shows, in the same view.
- Each captured entry is a few hundred bytes in the stored chunks; a page that logs in a loop is bounded by the event and
  byte caps (ADR-0265), not by the capture.
- Console text is stored as the page wrote it. Teams that log personal data should leave `captureConsole` off or filter at
  the source; the recorder cannot tell.
- Recording the request method needs wrapping `fetch` and XHR (the plugin does this when headers or bodies are on), so it is
  a separate decision.
