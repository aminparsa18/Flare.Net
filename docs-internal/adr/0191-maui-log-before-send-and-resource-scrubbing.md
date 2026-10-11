# ADR-0191: MAUI log BeforeSend and resource-attribute scrubbing

Status: Accepted

Date: 2026-10-11

## Context

[ADR-0177](0177-maui-before-send-and-queue-limits.md) added `BeforeSend` for spans and left dropping log records and
scrubbing resource attributes undecided.

## Decision

- **`BeforeSendLog(LogRecord) -> bool`** drops a log record when it returns `false`. A log record has no recorded
  flag to clear, so `BeforeSendLog` wraps the batch export processor Flare already owns instead of following it: the
  record is forwarded only if the callback keeps it. It runs after enrichment and `ScrubAttribute`, so it sees final
  attributes. A throwing callback keeps the record. Breadcrumbs are taken before it, so a dropped record can still
  appear as a breadcrumb.
- **`ScrubAttribute` runs over resource attributes** when the resource is built (logs, traces and metrics, and the
  native-crash emitter's per-run resource). `service.name` is never removed, since the server cannot attribute a
  resource without it; a throwing scrubber leaves the attribute as it was.

## Not decided here

Exception events recorded by other instrumentation. An `ActivityEvent` cannot be changed or removed once added, and the
HTTP instrumentation records none by default, so there is no processor-level fix; it stays on the roadmap.
