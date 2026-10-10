# ADR-0180: MAUI mobile performance

Status: Accepted

Date: 2026-10-10

## Context

`Flare.Maui` reports errors, hangs and crashes but not how fast the app is: how long it takes to start, how long a
screen takes to appear, how smooth scrolling is.

## Decision

- **Three span names, no new table or migration.** They land in `spans` and are read back with the existing
  `session.id`-scoped queries ([ADR-0167](0167-app-sessions-view.md)):
  - `app.start` with `app.start.type` (`cold`/`warm`) and `app.start.origin` (`process`/`sdk`). Cold is the first
    foreground, measured from process start on Android (`Process.StartElapsedRealtime`) and from the `UseFlare` call
    elsewhere, because iOS has no cheap, supported process start time; `origin` says which. Warm is a foreground
    after `OnRestart`/`WillEnterForeground`; a bare resume (a dialog closing) is not a start.
  - `screen.load`: Shell `Navigating` to `Navigated`, named like the `navigation` span (`screen.name`). The first screen
    has no navigation and is covered by `app.start`. Non-Shell navigation is not timed.
  - `screen.frames`: one per screen visit, flushed when the next screen is shown and when the app is flushed (the
    background), with `frames.total`, `frames.slow`, `frames.frozen`. Counters, not a span per frame, so the cost is a
    few increments per frame and one span per visit.
- **Slow at 20 ms, frozen at 700 ms**, both options; frozen frames also count as slow. A fixed slow threshold is a
  compromise: it is blind to 90/120 Hz frames between the display budget and 20 ms, and it avoids flagging 60 Hz jitter.
- **Frame source per platform:** Android `FrameMetrics.TotalDuration` (API 26+; older versions record nothing), iOS and
  Mac Catalyst the delta between `CADisplayLink` timestamps, paused outside the foreground.
- **The API adds `POST /api/app-sessions/performance`**: p50/p95 of `app.start` per version and type, and per
  screen the load p50/p95 and summed frame counts. The Sessions page shows it under release health with the same
  filters. Percentiles are over spans, so a trace-sampled app ([ADR-0122](0122-ingest-trace-sampling.md)) is not
  re-weighted.
- The core (`PerformanceTracker`) is MAUI-free with an injected `TimeProvider`, so it is unit-tested; the lifecycle,
  Shell and frame wiring is platform glue.

## Consequences

Verified on an Android device (arm64, Debug) with Maestro flows 12-14 against `ExampleApp.Maui`; iOS, an emulator and a
Release build are not. A warm start is only the `OnRestart` to `OnResume` gap (about 12 ms). The iOS process-start gap
makes iOS cold starts slightly optimistic. No per-screen trend over time and no alert on a regression yet.
