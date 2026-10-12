# ADR-0173: MAUI app hang detection

Status: Accepted

Date: 2026-10-10

## Context

A frozen UI (an Android ANR, an iOS app hang) leaves no exception, so neither the Errors page nor the session
timeline ([ADR-0170](0170-app-session-timeline.md)) shows it.

## Decision

- **A watchdog in `Flare.Maui`** ticks at a quarter of `AppHangThreshold` (default 2 s, minimum 500 ms). Each
  tick posts a ping to the main thread (`MainThread.BeginInvokeOnMainThread`); when the previous ping has not run
  within the threshold it reports one `app.hang` span, status Error, backdated to when the ping was posted, with
  `hang.threshold_ms`, plus a `hang` breadcrumb ([ADR-0172](0172-maui-breadcrumbs.md)), then flushes.
- **Reported at the threshold, not on recovery**, so a hang that ends with the OS killing the app still arrives.
  The span's duration is therefore the threshold plus up to one tick, not the full hang length.
- **Paused in the background** and started on the first foreground event: the OS may suspend the main thread
  legitimately, and startup is not counted.
- **No exception event**, so hangs do not appear on the Errors page; they show on the session timeline with the
  error badge and message. The blocked thread's stack is not captured: .NET cannot take another thread's managed
  stack, and native capture is per platform.
- The core (`AppHangWatchdog`) is MAUI-free with an injected dispatcher and `TimeProvider`, so it is unit-tested.
  Only the `MainThread` and lifecycle wiring is platform glue.

## Consequences

One timer wake-up per quarter threshold while foregrounded. A debugger pause reads as a hang; set
`DetectAppHangs = false` while debugging if that is noisy.
