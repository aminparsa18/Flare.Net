# Flare.AlertWorker

The alert evaluation process for [Flare](../../docs/explanation/architecture.md) — polls
every enabled alert rule on a fixed interval and sends notifications on breach. Split out
of `Flare.Api` into its own deployable so that an `Flare.Api` deploy/restart no longer
also stops alert evaluation, and so the poll loop no longer competes with query/dashboard
traffic for `Flare.Api`'s own thread pool. See
[`docs-internal/adr/0018-alert-worker-extraction.md`](../../docs-internal/adr/0018-alert-worker-extraction.md)
for the full "why" and its consequences.

## What it does today

Runs `AlertEvaluationWorker` (a `BackgroundService`, same poll-loop idiom as
`Flare.Ingest`'s `ClickHouseFlushWorker`) every `AlertingOptions.PollInterval` (default
30s). Each tick, for every enabled rule: evaluate its condition over its own rolling
window - a log-filter row count for a `LogCount` rule, or a metric-query result for a
`MetricThreshold` rule (see
[`docs-internal/adr/0020-metric-threshold-alerting.md`](../../docs-internal/adr/0020-metric-threshold-alerting.md)) -
and if the threshold breaches and the rule isn't in cooldown, notify through whichever
single channel the rule is configured for and record a new `alert_events` row.
Every replica coordinates through a single Redis-backed lock (`flare:alerts:eval-lock`)
so only one replica evaluates per tick even when more than one is running — see
`AlertEvaluationWorker`'s own remarks for the full mechanism.

**This project owns none of that logic itself.** `AlertEvaluationWorker` and
`AlertingOptions` are the only two types that actually moved here from `Flare.Api`;
everything else it depends on — `AlertQueryService` (rule reads + the count/last-fired
queries, reusing `LogFilterSqlBuilder`), `CompositeAlertNotifier` + the four channel
notifiers (`WebhookAlertNotifier`/`TelegramAlertNotifier`/`EmailAlertNotifier`/
`PagerDutyAlertNotifier`), `EmailOptions`, and the `AlertRule`/`AlertThreshold`/
`AlertHistoryEntry`/`LogFilter` model types — stays defined in `Flare.Api` and is reused
here as-is via a plain `ProjectReference`, the same "shared library referenced by more
than one process" shape `Flare.Identity` already is for `Flare.Ingest`/`Flare.Api`.
`Flare.Api` still registers and uses all of the above itself, for its own rule CRUD and
`/api/alerts/*/send-test` endpoints — nothing here is exclusive to this process.

## Project layout

```
Alerting/   AlertEvaluationWorker (the poll-loop BackgroundService), AlertingOptions -
            the only two types that live here rather than in Flare.Api.
Program.cs  Minimal host: ClickHouse/Redis client wiring, the same alert-notifier DI
            registrations Flare.Api's own Program.cs makes, /health + /alive only (no
            other HTTP surface - AddServiceDefaults()/MapDefaultEndpoints() are pulled in
            purely so Aspire's WithHttpHealthCheck has an endpoint to poll).
```

No `Model/`, `Query/`, or `Json/` folders here on purpose — see "What it does today"
above.

## Configuration

Same `Alerting__*` (`PollInterval`/`MaxRulesPerTick`, bound by this project's own
`AlertingOptions`, plus `PublicUrl`, bound by `Flare.Api`'s `AlertLinkOptions` over the
same section — the dashboard's public base URL, used to build the deep link a real fired
alert's notification carries back to the rule; blank means no link) and `Email__*` (SMTP
server for the Email channel) configuration keys `Flare.Api` already documents, plus
`ConnectionStrings__clickhousedb`/`ConnectionStrings__redis` — see
`docker-compose.yml`/`.env.example` for the full set. Webhook/Telegram/PagerDuty channels
need no app-wide config; their URL/token/routing key live per-rule.

## Tests

`AlertEvaluationWorker` is deliberately **not** unit-tested against a fake — same
reasoning `Flare.Api/README.md` documents for `AlertQueryService`/`LogTailBroadcaster`
and `Flare.Ingest.Tests` documents for its own ClickHouse/Redis-touching classes: real
`IClickHouseClient`/`IConnectionMultiplexer`/`HttpClient` I/O, covered by real
end-to-end runs instead. `AlertingOptions` is a plain options POCO with no logic of its
own. Every other type this process depends on (`AlertQueryService`, the notifiers,
`AlertThreshold.IsBreached`, etc.) is unit-tested in `../Flare.Api.Tests` where it's
actually defined — see that project's own README.

The Dockerfile's assumption that this image needs no `libldap2` install (unlike
`Flare.Api/Dockerfile`, despite transitively carrying `Flare.Api.dll`) is noted there as
not yet confirmed against a real built-and-run container.
