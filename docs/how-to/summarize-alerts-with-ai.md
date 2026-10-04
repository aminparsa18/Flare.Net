# How to get an AI summary of a fired alert

Flare can ask a language model to write a short incident summary each time an
alert fires: the first error, the failing service or span, and what changed
compared with the previous window. It is off by default and uses a model you
bring: any OpenAI-compatible endpoint, including a local Ollama.

The summary never delays the alert. The normal notification goes out first;
the summary is generated afterwards and, if it succeeds, appears in the alert
history and follows as a second message.

## Turn it on

Set these on **both** `Flare.Api` and `Flare.AlertWorker` (the `api` and
`alert-worker` services in `docker-compose.yml`). The worker is the process that
evaluates rules and calls the model:

```bash
Ai__Enabled=true
Ai__IncidentSummaries=true
Ai__Endpoint=http://localhost:11434/v1   # base URL; Flare calls /chat/completions
Ai__Model=llama3.1
Ai__ApiKey=...                            # optional for local models
```

`Ai__Enabled` alone only turns on [Explain this exception](link-exceptions-to-source-code.md#explain-an-exception-with-ai-optional);
alert summaries need `Ai__IncidentSummaries` as well.

## What is sent

For each fired alert Flare sends the rule name, description and threshold, the
observed value and the same measure for the previous window, and a small sample
of the evidence behind it, depending on the rule type:

- top log patterns in the window (log and metric rules);
- the most frequent exceptions (exception rules);
- the failing spans of one representative trace.

Flare redacts tokens, passwords, connection-string secrets, emails and IP
addresses first, but pattern matching can miss things, so use a local model if
your telemetry is sensitive. The exact redacted prompt is stored next to each
summary in the `alert_event_summaries` table, and logged at Debug.

## Limits

- The prompt is capped at `Ai__MaxInputChars` (12000) and the answer at
  `Ai__MaxOutputTokens` (800).
- At most `Ai__IncidentSummariesPerHour` (20) summaries are generated per hour
  across all rules. Alerts over the cap keep their normal notification and have
  no summary.
- Two summaries run at a time; more are skipped, not queued.
- Resolved events and alerts suppressed by a maintenance window get no summary.
  An absent-data alert gets one built from the rule alone, since there is no data.

## Where the summary shows up

- **Alert history** in the dashboard shows it under the event, as plain text.
- **Follow-up message** to Slack webhooks, Telegram, Microsoft Teams, Discord and
  email channels, titled `AI summary: <rule name>`. PagerDuty, Jira, incident.io,
  JSM Ops and generic webhooks don't get one, because a second message would
  open a second incident or look like a second firing alert.

This needs the `0048_alert_event_summaries.sql` migration. Fresh installs apply
it automatically; on an existing instance run it by hand with `clickhouse-client`.

## See also

- [Architecture decision: ADR-0104](../../docs-internal/adr/0104-ai-incident-summary.md)
- [ADR-0103](../../docs-internal/adr/0103-explain-exception-llm.md)
