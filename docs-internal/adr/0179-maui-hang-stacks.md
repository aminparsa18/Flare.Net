# ADR-0179: MAUI hang stacks

Status: Accepted

Date: 2026-10-10

## Context

[ADR-0173](0173-maui-app-hang-detection.md) reports `app.hang` without the blocked thread's stack, because .NET
cannot take another thread's managed stack and native capture is per platform.

## Decision

- **The watchdog takes an optional stack-capture delegate** and calls it at report time, while the UI thread is
  still blocked. A non-empty result is attached as the `hang.stacktrace` span tag (capped at 8 KB), so
  `ScrubAttribute` and `BeforeSend` apply to it like any other tag. A throwing or empty capture means no tag.
- **Android only:** the glue reads `Looper.MainLooper.Thread.GetStackTrace()`, the Java stack. Managed frames show
  as the runtime's native frames, so it points at the Android call (a blocking `Wait`, I/O, a layout pass), not
  at the C# method.
- **iOS and Mac Catalyst: no stack.** There is no supported API to read another thread's call stack; the
  alternatives (Mach `thread_get_state` plus manual unwinding) are fragile and symbolication-heavy. The span is
  reported as before.

## Consequences

The capture runs on the watchdog's timer thread and costs one JNI call per reported hang. A managed stack for
the blocked thread remains impossible without a cooperating runtime.
