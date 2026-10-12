# ADR-0199: Managed PII redaction at ingest

Status: Accepted

Date: 2026-10-12

## Context

Redaction was only possible through hand-written `RedactRegex` pipeline rules
([ADR-0033](0033-pipeline-rules-extraction-redaction.md)). Operators who just want
"don't store emails, cards or tokens" had to author and maintain the patterns, and a
careless one (a bare digit run) either misses cards or masks order ids.

## Decision

**A built-in detector set runs inside `PipelineRuleAnnotator`, ahead of user rules,
controlled by configuration (`PipelineRules:Pii:Enabled`, `PipelineRules:Pii:Detectors`).**

- `PiiRedactor` is a pure static class in `Flare.Ingest.Pipeline.Rules` with detectors for
  email, card number, IBAN, JWT, bearer token and PEM private key. Patterns are
  copied from `Flare.Api.Ai.AiRedactor` rather than shared, the same
  mirror-don't-reference boundary as ADR-0033/0034; the AI redactor additionally strips
  IPs, URL credentials and long tokens, which are too aggressive for stored logs.
- Cards (Luhn, 13-19 digits) and IBANs (mod 97) are checksum-validated, so timestamps and
  ids are not masked. Replacement text is `[REDACTED:kind]`.
- Config, not a database row: it is a deployment-wide posture, needs no new table or
  migration, and is off by default so existing data is never silently changed. Cost is a
  restart to change it; `PipelineRules:Enabled=false` disables it too.
- Scope: log `Body` and `LogAttributes` values, and for spans the attributes, status message and
  event/link attributes (`exception.message`/`exception.stacktrace` live on events), redacted in
  `SpanFlushWorker` before the ClickHouse write. Resource/scope attributes, span names and metrics
  are not scanned. 
- Runs before user rules so an extraction rule cannot copy a raw value into a new
  attribute, and before Drain clustering so templates never contain the raw value.

## Preview

`POST /api/pipeline-rules/pii-preview` (JSON body `{detectors?, condition?}`) scans the 500
most recent logs with a mirrored `Flare.Api.Query.PiiRedactor` and returns the existing
`PipelineRulePreviewResult`, containing only logs it would change (at most 20). A wider sample
than ADR-0034's 20 rows because most logs hold no PII. The dashboard has a "Preview PII
redaction" button on the Pipeline Rules page. The Api mirror can drift from Ingest; both
carry the same tests.

## Report-only scan

`POST /api/pipeline-rules/pii-scan` (same request body, JSON-only response) runs each detector
separately over the same 500-log sample and returns, per (field, detector), a log count and one
masked, truncated example, so the report never echoes the PII itself. The dashboard exposes it in the
preview dialog.

## Spans in preview and scan

`POST /api/pipeline-rules/pii-span-preview` and `pii-scan` with `signal: "spans"` sample the 500 most recent spans
(`SpanCondition` filters them) and cover exactly the fields Ingest redacts: status message, span attributes, and event
and link attributes. Fields are named `status_message`, `attr:{key}`, `event:{name}:{key}`, `link:{key}`. The span
preview returns field-level before/after pairs (JSON), since a span has no log-style body/attribute-bag shape. The
dashboard dialog has a Logs/Spans switch.

## Consequences

Not covered: resource attributes.

A dashboard toggle is deferred (on the roadmap): Ingest has no runtime-settings mechanism (sampling is also
config-only), so it needs a new table, cluster migration and polling cache. Until then it stays
`PipelineRules__Pii__*` configuration.

Detection is best-effort; names, addresses and free-form identifiers are not detected.
