# ADR-0177: MAUI BeforeSend, wider scrubbing and offline queue limits

Status: Accepted

Date: 2026-10-10

## Context

[ADR-0176](0176-maui-enrichment-and-scrubbing.md) added attribute scrubbing and left three gaps: span names and
exception messages, dropping a whole span, and the disk-retry directory, whose size and age the OpenTelemetry
exporter does not let the app configure.

## Decision

- **`BeforeSend(Activity) -> bool`** runs when a span ends; `false` drops it. It works by clearing the span's
  `Recorded` flag in a processor that runs before the exporter processors, which only queue recorded spans. A unit test
  against the in-memory exporter confirms the span is not exported. A throwing callback keeps the span. Logs have no
  equivalent flag, so `BeforeSend` does not apply to them; `ScrubAttribute` does.
- **`ScrubAttribute` also sees the span name** (key `span.name`) and **status message** (`status.message`).
  Exception events are immutable once added, so for exceptions Flare reports itself (`app.unhandled_exception`,
  `RecordException`) the reporter writes the event itself with `exception.type`, `exception.message` and
  `exception.stacktrace` passed through the scrubber. Exceptions recorded by other instrumentation, such as the
  `HttpClient` one, are not scrubbed; drop those spans with `BeforeSend` if needed.
- **Offline queue limits are Flare's own:** `OfflineQueueMaxAge` (2 days) and `OfflineQueueMaxBytes` (25 MB). At
  startup, before the exporter is created, files in the queue directory older than the age are deleted, then the
  oldest remaining until the total fits. This is a startup trim, not a live cap: the queue can exceed the size while the
  app runs, and the exporter's own maintenance still applies. Covered by unit tests on a temp directory.

## Not decided here

Dropping log records, scrubbing resource attributes, and a live (not startup-only) queue cap.
