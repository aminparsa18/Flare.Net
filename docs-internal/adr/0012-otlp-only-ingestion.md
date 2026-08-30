# ADR-0012: OTLP is the only ingestion protocol

Status: Accepted
Date: 2026-08-07 (v1 design principles, stated from the project's outset)

## Context

Flare needed to decide how logs (and later traces/metrics) reach it from
an application. The self-hosted observability space's usual answer is a
proprietary agent or wire format per vendor (a Seq client library, a
Datadog agent, etc.), each requiring its own client-side integration work
and locking ingestion to whatever languages/frameworks that vendor chose
to support.

## Decision

**One protocol in: OTLP (the OpenTelemetry Protocol).** Every supported
logging library reaches Flare through OTLP, using packages that already
exist and are already maintained (`Serilog.Sinks.OpenTelemetry`,
`NLog.Targets.OpenTelemetryProtocol`, ZLogger/`Microsoft.Extensions.Logging`
via the standard `OpenTelemetry.Exporter.OpenTelemetryProtocol`) — Flare
writes zero ingestion adapters of its own.

## Alternatives considered

- **A proprietary Flare client library/wire format**, the pattern most
  self-hosted logging tools (including Seq, the closest direct
  competitor) actually use. Rejected: it would mean writing and
  maintaining an adapter per logging library/language, and would lock out
  every non-.NET service from reaching Flare without Flare-specific
  tooling.
- **Support both OTLP and a handful of legacy formats** (e.g. accepting
  raw Serilog CLEF, syslog, or similar) for broader compatibility.
  Rejected implicitly by the "one protocol in" framing — Flare's bet is
  that OTLP adoption is where the ecosystem is heading, not a format to
  hedge against.

## Consequences

- **Any OTLP source works for free** — not just .NET. This is a
  first-class benefit of the decision, not a side effect: a Go, Node, or
  Python service emitting standard OTLP reaches Flare exactly the same way
  a .NET service does, with zero Flare-specific code on either side.
- Flare's getting-started experience is tailored to .NET loggers (the
  primary audience), but polyglot teams are never locked out — see
  `docs/how-to/run-standalone.md`'s logger snippets and
  `docs/tutorials/getting-started.md`.
- Flare's own ingestion side depends only on the OTLP wire format, not on
  any individual client package's version — client-side churn in the
  broader OpenTelemetry ecosystem (which does still track pre-release SDK
  versions in places) doesn't break the server. See
  `docs/reference/otlp-logger-versions.md` for the known-good client
  versions this was verified against.
- Flare will never gain a proprietary high-performance client SDK the way
  some vendors offer as a faster alternative to their standard protocol
  path — OTLP is the only path, by design, not a v1 limitation to
  eventually lift.

## Related documentation

- `docs/explanation/architecture.md` — the ingestion pipeline this feeds
- `docs/how-to/run-standalone.md`, `docs/how-to/run-with-aspire.md` — the
  per-logger integration guides
- ADR-0013 — the storage-side counterpart to this decision (don't
  reinvent what's already solved)