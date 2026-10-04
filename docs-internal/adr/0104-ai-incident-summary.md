# ADR-0104: AI incident summary on fired alerts

Status: accepted

## Context

ADR-0103 put the shared AI guard rails in code (off by default, bring your own model, redaction,
bounded, recorded). The roadmap's next AI item is a summary of a fired alert: first error, failing
service or span, and what changed since the previous window. An alert fires in `Flare.AlertWorker`,
not `Flare.Api`, and the one hard requirement is that the model can never block or delay the plain
notification.

## Decision

- **Strictly after the alert.** `AlertEvaluationWorker` sends the notification and writes the
  history row exactly as before, then hands the fire to `IncidentSummaryService.Enqueue`, which
  returns immediately. The summary runs on its own background task; its failure is logged and
  nothing else.
- **Opt-in twice.** `Ai__Enabled` (ADR-0103) plus `Ai__IncidentSummaries`. Config only, read by the
  worker, so the `Ai__*` settings must be present on `Flare.AlertWorker` as well as `Flare.Api`.
  There is no per-rule switch: what leaves the host is an operator decision, not a rule author's.
- **One model client.** `ILlmClient`/`OpenAiCompatibleLlmClient` is extracted from
  `ExceptionExplainService` and shared by both features: same endpoint, key, timeout, token cap, no
  redirects, no resilience handler (a model call must not be re-sent).
- **Evidence is a bounded sample, not raw data.** `IAlertEvidenceQueryService` reads the top
  log patterns (Drain `PatternTemplate`, so repeated lines collapse), the top exception groups for
  exception rules, and the error spans of one representative trace picked from those rows.
  Non-log rules use the error-or-worse logs of the services they are scoped to. The previous window
  is computed with the rule's own query for comparison; anomaly rules already carry a baseline. Each
  query is best-effort: a failure drops that section, not the summary. All use the alert-evaluation
  execution caps.
- **Redaction and size.** `IncidentSummaryPromptBuilder` redacts every section with `AiRedactor`
  and fills `Ai__MaxInputChars` in priority order (the alert, the comparison, exceptions, log
  patterns, spans), dropping whole trailing sections rather than cutting mid-line. Trace and span
  ids are left out of the prompt.
- **Bounded spend.** At most two summaries in flight (the rest are dropped, not queued) and
  `Ai__IncidentSummariesPerHour` (default 20) per clock hour. The hourly count is a Redis counter,
  because the evaluation lock moves between replicas, and it fails closed: if the budget can't be
  read, no model call is made.
- **Stored in its own table.** `alert_event_summaries` (migration 0048): EventId, RuleId, time,
  model, summary and the exact redacted prompt. `alert_events` rows are immutable and the summary
  arrives after the row is written, so a column there would need an update. History reads look the
  summaries up by EventId; a missing table reads as no summaries, so history works before the
  migration is applied. The stored prompt is the record of what was sent.
- **Follow-up message, to chat channels only.** The summary is sent as a second message through the
  existing notifiers by cloning the rule with a custom title (`AI summary: <rule>`) and body (the
  summary, with `{{` neutralised so it can't be read as a template placeholder). Eligible channels
  are Slack webhooks, Telegram, Teams, Discord and email. PagerDuty, Jira, incident.io and JSM Ops
  would open a second incident, and a generic webhook is machine-read and would see a second
  "firing" payload, so they get the summary in history only.
- **Plain text.** Dashboard history renders the summary as plain text, never markdown or HTML.
- **Not summarised:** resolutions, and alerts suppressed by a maintenance window (nobody was paged).
  An absent-data alert is summarised from the rule alone.

## Consequences

- Summaries cost model calls proportional to alert volume, capped by the hourly limit.
- If the model is slow, the follow-up arrives minutes after the alert; if it's down, nothing is lost.
- A stored prompt can contain redacted-but-sensitive telemetry, so the table inherits the
  retention and access story of `alert_events`.
- The CLI/MCP alert-history wire types do not carry the summary yet.
- The natural-language-filter roadmap item reuses `ILlmClient` and `AiRedactor` the same way.
