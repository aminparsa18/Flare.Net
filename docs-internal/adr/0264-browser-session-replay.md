# ADR-0264: Browser session replay

Status: Accepted

Date: 2026-10-21

## Context

The MAUI replay-lite ([ADR-0192](0192-maui-session-replay-lite.md)) approximates a replay with screenshots on a timer. That
does not carry over to a browser: a page is a DOM, a screenshot per few seconds is both coarse and heavy (each is a
bitmap), and the browser already exposes everything a faithful replay needs through `MutationObserver` and event listeners.
Recording a DOM is a solved problem with a mature open-source library, so the work here is the pipe around it and the
privacy defaults, not a recorder.

[ADR-0012](0012-otlp-only-ingestion.md) keeps ingestion thin over OTLP. Replay data is not a log, trace or metric and has no
OTLP signal, so like screenshots ([ADR-0174](0174-maui-error-screenshots.md)) and feedback
([ADR-0193](0193-maui-user-feedback.md)) it gets its own small endpoint under `/v1`, which keeps ingest-key auth, the
service allow-list, per-key origin restrictions and the browser CORS policy applying with no new code.

## Decision

- **The recorder is [rrweb](https://github.com/rrweb-io/rrweb) 2.1.7 (MIT).** `@apardev/flare-browser-replay`
  (`src/flare-browser-replay`) wraps `@rrweb/record`; the dashboard plays back with `@rrweb/replay`'s `Replayer`, imported
  only when someone presses **Watch recording**. rrweb is the same library other session-replay tools build on, its event
  format is the contract between the two halves, and it already solves incremental DOM serialisation, input masking and
  playback. Writing a recorder would be months of work for a worse result. The cost is a large dependency in the dashboard
  (about 400 KB of JavaScript, lazy-loaded) and a recorder tied to rrweb's event schema; the server never interprets events,
  so upgrading rrweb touches the two packages and nothing stored.
- **`POST /v1/replay?service=&session_id=`** takes a JSON array of rrweb events (gzip allowed; the existing request
  decompression middleware undoes it). Every element must be an object with a numeric `type` and `timestamp`, checked with
  one pass of `Utf8JsonReader` (depth limit raised to 4096, since a serialised DOM nests two levels per element) with no
  document built. Anything else is a 400. At most 10 MB after decompression (413 above it). A key's service allow-list
  applies (403). The body is stored as received.
- **Table `web_replay_chunks`** (migration 0090, single node and cluster): `StartTime`, `EndTime` (first and last event
  time, clamped to 24 hours before to 5 minutes after the server clock so a wrong browser clock cannot put rows outside the
  partition range), `ServiceName`, `SessionId`, `ChunkId` (UUID made at ingest), `EventCount`, `Payload` (the JSON text, `ZSTD(3)`),
  `IngestedAt`. Ordered by `(ServiceName, SessionId, StartTime)` and partitioned by month, like `app_screenshots`.
  `StartTime` is named like `spans.StartTime`, so the Traces retention TTL ([ADR-0143](0143-retention-ttl.md)) covers it
  with the same column; it is added to that signal's table list and to the erasure tables
  ([ADR-0198](0198-data-subject-erasure-export.md)), matched by session. A direct insert with no Redis stream, as for
  screenshots: a chunk is a best-effort row every few seconds and the recorder does not retry.
- **A session is the same id as the spans'.** `session_id` is the value of the `session.id` span attribute that
  [ADR-0167](0167-app-sessions-view.md) groups on, so the replay appears on `/sessions/[sessionId]` next to the span list.
  The recorder keeps a generated id in `sessionStorage` for the tab and exposes it (`handle.sessionId`); the app copies it
  onto its spans, one `SpanProcessor` in the docs. A session with a recording but no spans is still reachable by its URL.
- **API:** `POST /api/web-replay` lists a session's chunks (id, start, end, event count, payload size; at most 720, a
  `truncated` flag) without reading any `Payload`, using the timeline's window. `POST /api/web-replay/chunk
  {sessionId, chunkId, startUnixMs}` returns one chunk as the bare stored JSON array. Both apply the caller's service scope,
  and the chunk read is bounded to the chunk's own millisecond so it touches one partition. A missing table (migration not
  applied) is an empty list, not an error.
- **Dashboard:** a **Browser session replay** card above the span list that renders nothing without a recording. Nothing is
  downloaded until **Watch recording**; then the chunks load four at a time, are merged in timestamp order (a stable sort),
  cut to start at the first full snapshot, and handed to a `Replayer`. Own controls (play/pause, scrubber, 1/2/4/8x, skip
  inactive time), the recorded page scaled to fit the card, the span the user was in at the current moment, and a **Show in
  replay** button on every span row that seeks to its start. Over 40 MB of stored JSON warns first.
- **Privacy is on by default, and the recorder is opt-in code.** All inputs are masked (`maskAllInputs`, a password
  field always), `[data-flare-block]` elements are not recorded and `[data-flare-mask]` text is replaced with asterisks;
  `maskAllText` masks everything. rrweb inlines stylesheets so a replay does not depend on the original site's CSS, but
  images and fonts are fetched from their original URLs when someone watches (see Consequences). A sample rate decides once
  per session and is remembered. A recording stops after an hour (the most the dashboard lists) and, on a 401/403, at once.
- **Sending is best effort.** A batch goes out every 5 seconds, at 500 events, and when the tab is hidden. A hidden-tab
  batch under 60 KB is sent uncompressed with `keepalive` because there is no time to wait for gzip; larger ones are
  compressed first and may be lost if the tab dies before the request is accepted. A failed send is dropped, never retried,
  so a replay can have a gap but never repeats an event.

## Not decided here

Recording only the seconds around an error (a rolling buffer flushed when an exception is reported), a "has replay" marker or
filter in the sessions list, a link from an error occurrence to its session's replay, console and network capture, canvas
and WebGL recording, and an npm publish workflow for the recorder are follow-ups. So is a per-key byte limit for replay
(the existing per-key ingest limits count OTLP records, not these bodies).

## Consequences

- A recording is the most sensitive data Flare stores after raw logs: it can show anything on the page that is not masked,
  including text a user typed into a non-input element and personal data in rendered pages. The threat model lists it with
  screenshots as high sensitivity. Masking is a recorder-side promise: anyone with an ingest key can post any events, so the
  server cannot enforce it, and the dashboard shows what was stored.
- Replaying loads images, fonts and stylesheet `url()`s from the original site in the viewer's browser, so watching a
  replay makes requests to the recorded site (an internal host is unreachable from outside the network, a removed image
  is blank). The replay iframe is sandboxed by rrweb without script execution, so recorded `<script>` content does not run
  in the dashboard's origin.
- Storage is real: a full snapshot of a large page is hundreds of kilobytes of JSON before compression, then a few KB per
  chunk of interactions. The Traces retention TTL bounds it, and a sample rate is the lever for high-traffic sites.
- Erasure ([ADR-0198](0198-data-subject-erasure-export.md)) finds a recording by the session ids on the subject's spans, logs
  or metrics, so a recording whose session has no telemetry carrying `session.id` is not found by an identifier lookup.
- Existing deployments apply migration 0090 before recording starts; until then uploads answer 503 (the recorder ignores it)
  and the player stays hidden.
- A page recorded across full navigations produces one full snapshot per page; the `Replayer` handles several in one stream.
  A session whose first chunk is gone (retention, a window that began mid-session) starts at the next snapshot, or shows
  "no page snapshot" if there is none, which is why `checkoutEveryMs` exists.
