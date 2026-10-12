# ADR-0181: MAUI slow-load profiles

Status: Accepted

Date: 2026-10-10

## Context

[ADR-0180](0180-maui-mobile-performance.md) shows that a screen loaded slowly, not why. There is no cross-platform,
supported way to profile managed code in a running MAUI app, and [ADR-0179](0179-maui-hang-stacks.md) found that only
Android can read another thread's stack.

## Decision

- **A sampling profiler scoped to one screen load.** `Navigating` starts a timer that reads the UI thread's stack every
  `ProfileSampleInterval` (50 ms, at least 10 ms); `Navigated` stops it. Nothing runs outside a load, so the cost is one
  JNI call per interval during navigations only.
- **Kept only for slow loads.** If the load took at least `ProfileSlowLoadThreshold` (500 ms) the `screen.load` span
  gets `profile.samples` (count) and `profile.stacks`; otherwise the samples are dropped. `ProfileSlowLoads = false`
  turns it off. `ScrubAttribute` and `BeforeSend` apply as to any tag.
- **Folded stacks, not one tag per sample.** `profile.stacks` is one line per distinct stack, `count root;...;leaf`,
  most frequent first, at most 64 stacks and 8 KB. It is the input format of flame-graph tools.
- **Android only**, the Java stack of the main thread (managed frames show as the runtime's native ones, as in
  ADR-0179). On iOS and Mac Catalyst the capture delegate is unset, so no timer is created.
- The sampler (`StackSampler`) is MAUI-free and unit-tested; the capture delegate is set by the Android glue.

## Consequences

The profile points at the Android call in progress (layout, inflation, I/O), not at the C# method, and a 50 ms
interval misses anything shorter. `app.start` is not profiled, since the SDK starts after most of a cold start.
The dashboard shows `profile.stacks` as a plain span tag; a flame graph over it is not built.
