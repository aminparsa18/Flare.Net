# ADR-0178: MAUI native crash capture on next launch

Status: Accepted

Date: 2026-10-10

## Context

Release health ([ADR-0175](0175-release-health.md)) counts a session as crashed when it has an
`app.unhandled_exception` span with `exception.escaped = true`, which `Flare.Maui` writes from the managed
`AppDomain` handler ([ADR-0166](0166-maui-sdk.md)). A SIGSEGV, an Android ANR or low-memory kill, or an iOS watchdog
termination ends the process before any managed code runs, so the most damaging crashes were the ones release health
could not see.

## Decision

- **Read what the OS recorded, on the next launch.** Android (11+) reads `ActivityManager.GetHistoricalProcessExitReasons`
  for the app's main process: crash, native crash, ANR (with the trace text, capped at 16 KB), initialization failure,
  excessive resource use, and low-memory kills only when the process was visible. iOS 14+ and Mac Catalyst subscribe to
  MetricKit and take its crash diagnostics. Both feed one internal `NativeCrash` record.
- **Report into the earlier session.** A small journal (`flare/runs.json` in the app data directory, last 10 launches)
  stores each launch's `session.id`, effective `service.version`, build, last lifecycle time and whether it ended in the
  foreground. A native crash is matched to its launch and sent as an `app.unhandled_exception` span with
  `exception.escaped = true`, `crash.native = true`, `crash.kind`, and an exception event of type `Native.<Kind>`.
  Release health and the Errors page need no change. Because `service.version` is a resource attribute, each affected
  launch gets its own short-lived tracer provider carrying that launch's version, so a crash found after an app update
  still counts against the release that crashed. The span is dated when the crash happened (Android), or at the
  launch's last lifecycle event (iOS, where MetricKit only gives a window of up to a day).
- **No double counting.** The managed fatal path marks the launch in the journal; a plain crash or native crash found for
  a launch with a reported managed fatal is the same event and is skipped. Android lists old exits on every call, so a
  watermark of the newest handled timestamp is kept; iOS reports are matched at most once per launch. Crashes older than
  the first journalled launch are ignored, so installing the SDK does not replay history.
- **Same privacy path.** The exception event goes through `ScrubAttribute`, and `BeforeSend` sees the span. The journal
  holds no device identifier. `CaptureNativeCrashes` (default on) turns it off.
- **Core and glue split.** Matching, the journal and the emitter are plain `net10.0` and unit-tested, including the
  in-memory-exporter shape of the span; only the `ApplicationExitInfo` and MetricKit reads live in `Platform/`.

## Not decided here

Symbolicating NDK tombstones (protobuf on Android 12+) and MetricKit's call-stack JSON, Android before 11, iOS jetsam
out-of-memory kills (not in MetricKit crash diagnostics), an exact iOS crash time, and verification on a device: the
platform reads compile for all mobile targets but have not run on one.
