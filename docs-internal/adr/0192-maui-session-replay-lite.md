# ADR-0192: MAUI session replay-lite

Status: Accepted

Date: 2026-10-12

## Context

Error screenshots ([ADR-0174](0174-maui-error-screenshots.md)) show the page at the moment of an exception. The cause is
often earlier: the screen the user was on a few seconds before, or what they had typed. The session timeline
([ADR-0170](0170-app-session-timeline.md)) already lists spans and breadcrumbs ([ADR-0172](0172-maui-breadcrumbs.md)) in
order; screenshots on a schedule fill in the picture between them. A real video replay (DOM or view-tree recording, as
web tools do) is out of scope: it needs a recorder per platform and much more storage, and the point here is a
low-cost approximation.

## Decision

- **Same table, a `Kind` column** (migration 0081, single node and cluster): `error` (the default, so every row from
  migration 0079 stays valid) or `periodic`. A scheduled frame has an empty `TraceId`/`SpanId` and is addressed by its
  `StartTime`. No new table, so the Traces retention TTL ([ADR-0143](0143-retention-ttl.md)) and the key's service
  allow-list already cover it.
- **`POST /v1/screenshots?kind=periodic`** needs only `service` and `session_id`; the default `kind=error` is unchanged.
  Anything else is a 400. Same size cap, same direct insert with no Redis stream.
- **Client, opt-in:** `ScreenshotInterval` (null = off, at least 5 s). A `ReplayScheduler` timer runs only while the app is
  in the foreground (started on resume, stopped on background) and calls the same `ScreenshotReporter` used for errors,
  built with its own cap (`MaxReplayScreenshots`, default 120 per launch) and a spacing of half the interval so a late tick
  is not dropped. Error screenshots keep their own limits (5 per launch, one per 10 s) and are unaffected. The size
  limit validation covers both features.
- **Masking before capture**, for error and scheduled screenshots alike. The capture runs on the UI thread; it sets
  `Opacity = 0` on every view that should be hidden, captures, and restores the previous opacity in a `finally`. Hidden
  views are: a password `Entry` (always), `Entry`/`Editor`/`SearchBar` (`MaskTextInputs`, default on), and any view with the
  `FlareMask.IsMasked` attached property. A masked container is not descended into, since its children go with it.
  Opacity is the one property every MAUI view supports on every platform and it leaves layout untouched.
- **API:** `POST /api/app-sessions/replay` returns the frame times (oldest first, at most 720, a `truncated` flag) so the
  player builds a scrubber without downloading images; `POST /api/app-sessions/replay/frame {sessionId, timeUnixMs}`
  returns one image. Both go through the existing service scope. The error-screenshot flag on timeline events now ignores
  rows with an empty span id.
- **Dashboard:** a Session replay player above the timeline on `/sessions/[sessionId]`: scrubber, previous/next, play at
  one frame per second, the image fetched when first shown and the next one prefetched, and the span the app was in at that
  frame's time. It renders nothing for a session without frames.

## Not decided here

Breadcrumbs are already timeline rows, so the player shows the nearest preceding event rather than a separate breadcrumb
track. Skipping a frame when the page has not changed (a hash of the bitmap), a per-key byte limit for frames, an
adaptive interval after an error, and masking native dialogs or the keyboard are follow-ups.

## Consequences

- Masking by transparency can flicker a text field for a frame on a slow device, since the UI thread renders between the
  property change and the capture. Hiding in a native overlay would avoid it but needs per-platform code.
- Whatever MAUI does not render in the page (native dialogs, the system keyboard, a web view's content) is not masked
  and a web view's page is captured as is. The docs say so.
- Frames are the largest rows Flare stores per session: at 120 frames of up to 300 KB a launch can add tens of
  megabytes in the worst case. The per-launch cap and the Traces retention bound it; a per-key byte limit is the follow-up
  if a service needs one.
- Existing deployments must apply migration 0081 before turning the interval on; until then uploads fail with 503 (which the
  client ignores) and the player stays empty.
