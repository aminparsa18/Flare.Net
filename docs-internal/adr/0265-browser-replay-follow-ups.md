# ADR-0265: Browser replay: record around errors, find recordings, count their bytes

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0264](0264-browser-session-replay.md) shipped recording and playback and left five things open that need no new
storage: recording only around an error, telling which sessions have a recording, reaching a recording from an error, a limit
on what a key may upload, and publishing the recorder. Console and network capture and canvas recording stay open; they
change what is recorded and how much, and want their own look at the privacy cost.

## Decision

- **`mode: 'onError'` is a rolling buffer in the recorder, not a server feature.** rrweb takes a fresh page snapshot every
  `checkoutEveryNms`; in this mode that is `bufferMs` (default 30 s). `RollingBuffer` holds the last two segments (a segment is
  everything from one snapshot, rrweb's `Meta` event, to the next), so what it holds always starts at a snapshot the player
  can draw from and reaches back between `bufferMs` and twice that. An error (`handle.keep()`, or `window` `error` and
  `unhandledrejection` unless `captureErrors` is off) hands the held events to the normal uploader and passes events straight
  through for `afterErrorMs` (default 10 s). Then it discards events until the next snapshot, because incremental events with
  no snapshot under them cannot be replayed, and goes back to buffering. An error that arrives in that gap starts the next
  recording at the next snapshot. The server and the stored format are unchanged: a recording from this mode is chunks like any
  other, a session with several errors has several snapshots (which the player already handles), and a session with none
  stores nothing. The buffer is pure and takes the clock as an argument, so the state machine is unit-tested; the wiring was
  checked in headless Chromium (no request before the error; the first request after it starts with `Meta` + `FullSnapshot`
  and reaches back several seconds; nothing is sent once the window closes).
  A page is still recorded into memory the whole time, so masking applies in both modes. Memory is bounded by two segments and
  by `maxBuffered` (20 000 events): past it the oldest whole segment is dropped, never the only one.
- **A recording counts against the key's byte limits and not its event limits.** `POST /v1/replay` adds the chunk's
  decompressed size to `IngestKeyUsageFeature` as bytes with zero events, so the existing per-minute and per-day byte caps
  ([ADR-0051](0051-per-ingest-key-ingestion-limits.md)) apply, and a key over a cap gets the same `429` + `Retry-After` the OTLP
  endpoints give. Events are not counted because an rrweb event is not a telemetry record (one page snapshot is one event of
  hundreds of kilobytes), so an events-per-minute cap set for logs would otherwise mean nothing for replay. No new columns,
  migration or UI. The cost is that replay shares the byte budget with OTLP instead of having its own; a separate replay
  cap is on the roadmap if that proves too coarse. The recorder reads a `429`, drops batches for `Retry-After` (default
  60 s, at most an hour) and carries on, instead of posting into a closed door every few seconds.
- **The sessions list marks and filters recorded sessions with a second query, not a join.** `AppSession.HasReplay` and
  `AppSessionsRequest.ReplayOnly`. The marker is one `SELECT DISTINCT SessionId FROM web_replay_chunks` over the window, narrowed
  to the listed sessions' ids; `ReplayOnly` runs that first (up to 5 000 ids) and restricts the spans query to them, so the
  500-row cap applies to recorded sessions rather than to a page of recent ones filtered afterwards. Both are in
  `AppSessionQueryService` and share the service-scope clause with the rest of replay. The spans query stays free of the
  replay table, so a deployment that has not applied migration 0090 gets sessions with no marker and an empty list under the
  filter, not an error. A session with a recording and no spans is not listed (the list is built from spans), as before.
- **An error occurrence carries its span's `session.id`.** `ExceptionOccurrence.SessionId` (last member of the MemoryPack
  record, the hand-written TS decoder updated to match), selected from `SpanAttributes['session.id']`. The occurrence
  dialog links to `/sessions/<id>?from&to&at`: the spans window is an hour before to ten minutes after the error, and `at`
  makes the player open five seconds before it once loaded. The link shows only when the id is present, which is when the app
  copied the recorder's session id onto its spans (ADR-0264).
- **The recorder is published by `browser-replay-publish.yml`** on a `flare-browser-replay-vX.Y.Z` tag that must match
  `package.json`, with npm trusted publishing and provenance, the same arrangement as the CLI's `npm-publish.yml`. The first
  release is manual because npm attaches a trusted publisher only to a package that exists.

## Consequences

- Teams can turn replay on for every user at the price of the errors' seconds, not whole sessions. What `onError` cannot show
  is a session that went wrong without throwing or reporting; `keep()` is the hook for those.
- A burst of errors in one session extends the live window rather than making a recording each; errors further apart than
  `afterErrorMs` plus the snapshot interval make separate recordings with a gap between them.
- The byte caps now also bound browser replay, so an operator who set a tight bytes-per-minute cap for logs may see replay
  uploads rejected with `429` before logs are. They can raise the cap or lower `sampleRate`.
- The sessions list does two more small ClickHouse queries only when `web_replay_chunks` exists.
