# Flare.Api

The Query API for [Flare](../../docs/explanation/architecture.md) — the read side of `clickhousedb.logs`.
`Flare.Ingest` writes that table; this project turns structured search/filter/
time-range/aggregate requests into parameterized ClickHouse queries over it.

## What it does today

Implements v1's **"Query API: search, filter, time-range, aggregate"** and **"Live-tail
streaming endpoint"** roadmap items, plus the **"Alerting"** item promoted out of
"Later":

- **`POST /api/logs/search`** — paginated, most-recent-first log event list. Filters by
  time range, service, severity level, exact trace id, exact span id (trace/log
  correlation — see below), attribute key/value equality (any of the three OTel
  attribute bags), and free-text substring search against `Body`. Keyset-paginated via
  `(Timestamp, EventId)` — see "Pagination" below.
- **`POST /api/logs/aggregate`** — bucketed event counts for the dashboard's volume
  chart, optionally grouped by service or level. Same filter shape as `/search`.
- **`GET /api/logs/tail`** — WebSocket live tail, real-time events filtered by the same
  `LogFilter` shape. See "Live-tail streaming" below.
- **`/api/alerts/*`** — threshold/query-based alert rule CRUD, fired-alert history, and
  evaluation dry-runs, plus the `AlertEvaluationWorker` background service that actually
  evaluates them and sends webhook/Slack, Telegram, or email notifications. See
  "Alerting" below.

The two `/api/logs/*` POST endpoints take a JSON body (not query-string params) —
filters are multi-valued/structured (service lists, attribute key/value pairs), which
doesn't fit cleanly in a URL. `/api/alerts` endpoints follow the same convention for the
same reason (a rule's condition is a full `LogFilter`).

**Explicitly not here:** auth (no roadmap item has it yet), a general-purpose saved-query
feature (a dashboard-side concern distinct from alert rules — an alert rule is a saved
condition *plus* a threshold and notification target, not a bare re-runnable search), and
materialized views/pre-aggregation for `/aggregate` — see `db/clickhouse/README.md`'s "No
materialized views" note for why plain `GROUP BY` queries are the right v1 call.

## Project layout

```
Model/      LogFilter (shared by both /api/logs endpoints AND reused verbatim as an
            AlertRule's condition), request/response DTOs, LogEventDto (the API-facing
            row shape - a deliberate, separate mirror of Flare.Ingest.Model.LogEvent, not
            a shared reference - see LogEventDto's doc comment for why), LogTailMessages
            (the live-tail WebSocket envelope types), AlertModels (AlertRule/
            AlertRuleRequest/AlertThreshold/AlertHistoryEntry/AlertTestResult).
Query/      LogFilterSqlBuilder (LogFilter -> parameterized WHERE clause, shared),
            LogSearchQueryBuilder / LogAggregateQueryBuilder (full SELECT statements),
            LogSearchCursor (keyset pagination token), LogQueryService (the only piece
            that talks to ClickHouse for logs), LogFilterMatcher (LogFilter -> boolean
            match, live-tail's in-memory counterpart to LogFilterSqlBuilder),
            AlertQueryService (rule CRUD + fired-alert history + the count/last-fired
            queries AlertEvaluationWorker runs - the alerting equivalent of
            LogQueryService, reusing LogFilterSqlBuilder for its threshold count query).
Endpoints/  LogsEndpoints - the two /api/logs POST routes. LogTailEndpoints - the
            WebSocket route. AlertEndpoints - /api/alerts CRUD + history + test-run routes.
Json/       LogsJsonContext, LogTailJsonContext, AlertsJsonContext, TelegramJsonContext -
            source-generated System.Text.Json contracts.
LiveTail/   LogTailBroadcaster (the single background XREAD-and-fan-out reader over
            Redis's flare:logs stream), LogTailSubscription (one connection's state),
            LiveTailOptions, BufferedLogEvent + BufferedLogEventJsonContext +
            BufferedLogEventMapper (deserializing/normalizing the Redis wire format -
            same "deliberate mirror, not a shared reference" convention as LogEventDto).
Alerting/   AlertEvaluationWorker (the poll-loop BackgroundService that evaluates every
            enabled rule and notifies on breach), AlertingOptions, EmailOptions (app-wide
            SMTP server settings), AlertMessageFormatter (the fired-alert text shared by
            every channel), IAlertNotifier + WebhookAlertNotifier (the webhook/Slack
            sender), TelegramAlertNotifier (the Telegram sender), EmailAlertNotifier (the
            email sender), CompositeAlertNotifier (picks between the three per rule).
```

`Model` and `Query` are deliberately pure/ClickHouse-free wherever possible
(`LogFilterSqlBuilder`, `LogSearchQueryBuilder`, `LogAggregateQueryBuilder`,
`LogSearchCursor`) — same "pure function, unit-testable on its own" style
`Flare.Ingest`'s `ClickHouseRowMapper` already uses for the write side. `LogQueryService`
is the one seam that holds an `IClickHouseClient`.

## Pagination

`clickhousedb.logs` had no unique per-row id before this roadmap item —
`db/clickhouse/0002_logs_event_id.sql` adds `EventId UUID`, generated by
`Flare.Ingest`'s `OtlpLogMapper` for every record. `/api/logs/search` sorts
`Timestamp DESC, EventId DESC` and paginates via
`(Timestamp, EventId) < (cursorTimestamp, cursorEventId)` tuple comparison — `EventId`
is the tiebreaker for the (rare, but real) case of two rows sharing an exact
`Timestamp`. The cursor returned in `NextCursor` is an opaque base64 token
(`LogSearchCursor`) — not a public contract, just round-tripped by the caller.

## Query safety

Every query `LogQueryService` runs sets ClickHouse's `max_execution_time`,
`timeout_before_checking_execution_speed`, `max_rows_to_read`, `max_result_rows`, and
`result_overflow_mode` via `QueryOptions.CustomSettings` — self-hosted ClickHouse has no
default caps on any of these. Reviewed against the `clickhouse-best-practices` skill's
`agent-query-safety` rule. `/api/logs/search` also defaults the time range to the last
hour when `From`/`To` are omitted (`LogFilterSqlBuilder.DefaultLookback`), so an
unfiltered request doesn't scan the whole table.

## Live-tail streaming

`GET /api/logs/tail` upgrades to a WebSocket. A connection gets no events until it sends
a `subscribe` message; it can re-subscribe with a new filter, or `pause`/`resume`, at any
point without reconnecting:

```jsonc
// client -> server
{"type":"subscribe","filter":{"services":["flare-ingest"],"severityNumbers":[17,21]}}
{"type":"pause"}
{"type":"resume"}

// server -> client
{"type":"Event","event":{ /* LogEventDto, same shape as /api/logs/search's Events */ }}
{"type":"Dropped","droppedCount":3}
{"type":"Error","error":"Malformed message: ..."}
```

**Source: the Redis Stream, not ClickHouse.** A single background reader
(`LogTailBroadcaster`) polls the same Redis Stream key `Flare.Ingest`'s
`RedisStreamLogEventSink` writes into and `ClickHouseFlushWorker` consumes from
(`LogEventPipelineOptions.StreamKey`, default `flare:logs` — both sides bind the same
`LogEventPipeline` config section, so an override reaches both if applied to both, see
`.env.example`), via a plain `XREAD` (no consumer group — it never joins `Flare.Ingest`'s
`flare-ingest` group or touches its ack/PEL accounting) and fans each new entry out to every subscribed
connection's channel after applying that connection's current filter. This gets
sub-second latency without adding per-viewer ClickHouse query load, at the accepted
trade-off that a tailed event is shown slightly before it's durably in ClickHouse — no
different from how the event already sits in Redis before the batched flush picks it up
either way. A live tail has no durability requirement: if `Flare.Api` restarts, connected
clients just reconnect and see whatever's new from that point; nothing is replayed.

**`LogFilter.From`/`To` are ignored** for `/tail` — a live stream is inherently
open-ended; use `/api/logs/search` for a bounded historical range.

**Backpressure**: each connection's channel is bounded
(`LiveTailOptions.SubscriberChannelCapacity`, default 500). A slow reader's channel fills
up and further events fail to enqueue rather than blocking the shared broadcaster loop
for every other subscriber — the connection's send loop reports the drop count via a
`dropped` message instead. Events published while `paused` are dropped outright (not
queued), so resuming doesn't create a burst of stale events.

## Alerting

A saved `LogFilter` condition plus a count threshold over a rolling window, evaluated
periodically and notified via webhook/Slack, Telegram, or email on breach:

```
POST   /api/alerts             create
GET    /api/alerts             list
GET    /api/alerts/{id}        get
PUT    /api/alerts/{id}        update
DELETE /api/alerts/{id}        soft-delete
GET    /api/alerts/{id}/history?limit=50   fired-alert history
POST   /api/alerts/{id}/test               dry-run the saved rule (ignores cooldown, writes nothing)
POST   /api/alerts/test                    dry-run an unsaved draft (same body shape as create/update)
```

**Storage: `alert_rules` (ReplacingMergeTree) + `alert_events` (append-only MergeTree)**,
`db/clickhouse/0003_alert_rules.sql` / `0004_alert_events.sql` (plus
`0005_alert_rules_telegram.sql`, which adds the `TelegramBotToken`/`TelegramChatId`
columns, and `0006_alert_rules_email.sql`, which adds `EmailTo`). Rule CRUD is INSERT-only —
every create/update inserts a new version, delete inserts an `IsDeleted=1` tombstone, and
every read goes through `FROM alert_rules FINAL WHERE IsDeleted = 0`. See those
migrations' own comments and `db/clickhouse/README.md`'s "Design decisions" for the full
rationale (`ALTER TABLE ... UPDATE/DELETE` are async mutations, the wrong tool for
"write, read back immediately" CRUD).

**Evaluation: periodic polling, in-process.** `AlertEvaluationWorker` (a
`BackgroundService`, same poll-loop idiom as `Flare.Ingest`'s `ClickHouseFlushWorker`)
runs every `AlertingOptions.PollInterval` (default 30s). Each tick, for every enabled
rule: count matching logs over the rule's own rolling window (`WindowSeconds`) by cloning
the rule's `LogFilter` condition with `From`/`To` overridden and running it through
`LogFilterSqlBuilder` — the same compiler `/api/logs/search` uses — then a tighter
query-safety cap than `LogQueryService`'s (`max_execution_time=10`, since this runs once
per rule *every* tick). If the threshold breaches and the rule isn't in cooldown
(`SELECT maxOrNull(FiredAt) FROM alert_events WHERE RuleId = ...` — cooldown state lives
in the history table itself, not a separate cache), it notifies and inserts a new
`alert_events` row. No streaming/near-real-time evaluation — deliberately out of scope
for this pass, since polling matches "threshold/query-based" exactly and is the simplest
correct implementation.

**Notification: exactly one channel per rule, picked by `CompositeAlertNotifier`.** A
rule sets exactly one of `WebhookUrl`, `TelegramBotToken`+`TelegramChatId`, or `EmailTo`
— never more than one, never none (`AlertRuleRequest.ValidateChannel` 400s a create/update
that breaks this). `CompositeAlertNotifier` (the `IAlertNotifier` actually registered for
DI) inspects the rule and delegates to one of:

- `WebhookAlertNotifier` — POSTs JSON with a top-level `text` (what Slack's
  incoming-webhook parser renders) plus flat structured fields (`ruleId`,
  `observedCount`, `thresholdCount`, `windowSeconds`, `firedAt`) a generic webhook
  consumer can read directly — Slack ignores unrecognized top-level keys, so one shape
  serves both.
- `TelegramAlertNotifier` — POSTs `{chat_id, text, parse_mode}` to
  `https://api.telegram.org/bot{TelegramBotToken}/sendMessage`. Telegram returns HTTP 200
  with `{"ok":false,"description":"..."}` for most delivery failures (bad chat ID, bot
  blocked/kicked) rather than a non-2xx status, so its `NotificationResult.Success` is
  derived from the parsed `ok` field, not `IsSuccessStatusCode` alone — otherwise a failed
  Telegram send would be misrecorded as `"Sent"` in `alert_events`.
- `EmailAlertNotifier` — emails `EmailTo` through the app-wide SMTP server in
  `EmailOptions` (bound from the `Email` configuration section / `Email__*` env vars —
  see `docker-compose.yml`/`.env.example`), via MailKit's `SmtpClient` (connect →
  authenticate if a username is set → send → disconnect, a fresh client per send). Unlike
  the other two channels, the SMTP server itself isn't per-rule config — only the
  recipient is — so credentials live in one place, not duplicated across rules or stored
  in `alert_rules`. A blank `EmailOptions.Host` (SMTP never configured) is a per-send
  failure recorded in `alert_events`, not a startup error.

The webhook and Telegram notifiers share the fired-alert message text
(`AlertMessageFormatter.BuildText`, also the email body) and are sent via their own
named/typed `HttpClient`s (`AddHttpClient<WebhookAlertNotifier>`,
`AddHttpClient<TelegramAlertNotifier>`), which inherit `Flare.ServiceDefaults`' resilience
handler (retries/circuit-breaking) for free. `EmailAlertNotifier` has no `HttpClient` —
MailKit's `SmtpClient` is its own socket-based client, not HTTP.

## A known, inherited trade-off

`logs`' `ORDER BY (ServiceName, SeverityNumber, Timestamp, TraceId)` favors "browse one
service over a time range" over "browse everything, no service filter" — a `/search`
request with no `Services` filter can't use that primary index prefix (see
`db/clickhouse/README.md`'s "Design decisions" for the full history). Not fixed by this
roadmap item; the named follow-up (a `Timestamp`-first projection) stays deferred until
real query latency data says it's needed.

## Free-text search vs. the `Body` index

`Search` compiles to `Body ILIKE '%term%'` — case-insensitive substring match, the UX a
search box implies. The `tokenbf_v1` skip index already on `Body` is tuned for
token-aligned, case-sensitive matches instead, so it mostly won't prune granules for an
`ILIKE` query; the scan happens within whatever granules survive the other filters
(time range/service/level, which usually already narrow a lot). Named follow-up if this
is slow on real data: switch to token-based search (`hasToken`, case-sensitive) or swap
the index to `ngrambf_v1`.

## Running it

Via the Aspire AppHost (recommended — wires the ClickHouse connection and health checks):

```bash
dotnet run --project ../Flare.AppHost
```

Or standalone (needs `ConnectionStrings:clickhousedb` supplied some other way, e.g.
user secrets or an env var, since there's no AppHost to inject it):

```bash
dotnet run --project .
```

## Smoke-testing manually

Get the `api` resource's HTTP endpoint from the Aspire dashboard, then:

```bash
export API='<the "http" url from the Aspire dashboard's api resource, e.g. http://localhost:5285>'

# Unfiltered search (last hour by default)
curl -s -X POST "$API/api/logs/search" -H 'Content-Type: application/json' -d '{}'

# Filtered: one service, warn+error, with a trace id
curl -s -X POST "$API/api/logs/search" -H 'Content-Type: application/json' -d \
  '{"filter":{"services":["flare-ingest"],"severityNumbers":[13,17,21]}}'

# Volume by service, 1-minute buckets
curl -s -X POST "$API/api/logs/aggregate" -H 'Content-Type: application/json' -d \
  '{"bucketWidthSeconds":60,"groupBy":"Service"}'

# Live tail (needs a WebSocket client, e.g. websocat: https://github.com/vi/websocat).
# Connect, then paste a subscribe message and watch events arrive as you send more logs.
websocat "$(echo "$API" | sed 's#^http#ws#')/api/logs/tail"
# > {"type":"subscribe","filter":{}}

# Create an alert rule (point webhookUrl at a real Slack incoming-webhook URL, or a
# throwaway HTTP sink like webhook.site, to see a real notification land). "enabled",
# "cooldownSeconds", etc are all optional and default to true/300/"" when omitted -
# AlertRuleRequest declares them nullable specifically so System.Text.Json can tell
# "omitted" apart from "explicitly false/0/empty" (see AlertRuleRequest's remarks); shown
# explicitly below anyway for clarity.
curl -s -X POST "$API/api/alerts" -H 'Content-Type: application/json' -d \
  '{"name":"high error rate","enabled":true,"condition":{"severityNumbers":[17,21]},"threshold":{"count":10,"comparator":"GreaterThanOrEqual"},"windowSeconds":300,"cooldownSeconds":300,"webhookUrl":"https://webhook.site/<your-id>"}'

# Or notify via Telegram instead - webhookUrl, telegramBotToken/telegramChatId, and
# emailTo are mutually exclusive (a bot token from @BotFather, a chat id from a
# getUpdates call or @userinfobot)
curl -s -X POST "$API/api/alerts" -H 'Content-Type: application/json' -d \
  '{"name":"high error rate","enabled":true,"condition":{"severityNumbers":[17,21]},"threshold":{"count":10,"comparator":"GreaterThanOrEqual"},"windowSeconds":300,"cooldownSeconds":300,"telegramBotToken":"<bot-token>","telegramChatId":"<chat-id>"}'

# Or email instead - needs Email__Host/Email__From (and usually Email__Username/
# Email__Password) configured on the server first (see docker-compose.yml/.env.example);
# otherwise the notification just fails per-send with a clear error
curl -s -X POST "$API/api/alerts" -H 'Content-Type: application/json' -d \
  '{"name":"high error rate","enabled":true,"condition":{"severityNumbers":[17,21]},"threshold":{"count":10,"comparator":"GreaterThanOrEqual"},"windowSeconds":300,"cooldownSeconds":300,"emailTo":"oncall@example.com"}'

# Dry-run it against current data without waiting for the next poll tick
curl -s -X POST "$API/api/alerts/<id-from-create-response>/test"

# Fired-alert history
curl -s "$API/api/alerts/<id-from-create-response>/history"
```

## Tests

`../Flare.Api.Tests` covers the pure query builders with plain xUnit unit tests — no
hosting, no network, no containers: `LogFilterSqlBuilder` (every filter field, one at a
time and combined), `LogSearchQueryBuilder` (column list, page-size clamping, cursor
presence/absence), `LogAggregateQueryBuilder` (each `GroupBy` value, bucket-width
validation), `LogSearchCursor` (round-trip, malformed input), `LogFilterMatcher` (the
live-tail counterpart to `LogFilterSqlBuilder` - same cases, asserting a boolean match
instead of a SQL fragment), `BufferedLogEventMapper` (null-coalescing/fallback
conventions), `BufferedLogEventJsonContext` (round-trips, plus a hand-written fixture
proving it parses `Flare.Ingest.Pipeline.LogEventJsonContext`'s exact wire format).

`AlertThresholdTests` covers `AlertThreshold.IsBreached` (both comparators, boundary
values) — the one piece of pure alerting logic worth a unit test on its own.

`LogQueryService`, `LogTailBroadcaster`, `AlertQueryService`, and `AlertEvaluationWorker`
(real `IClickHouseClient`/`IConnectionMultiplexer`/`HttpClient` I/O) are deliberately
**not** unit-tested against a fake, same reasoning `Flare.Ingest.Tests` documents for its
own ClickHouse/Redis-touching classes — covered by real end-to-end runs instead (see
"Smoke-testing manually" above, plus `EXPLAIN indexes=1` checks in
`db/clickhouse/README.md`). The alerting feature's own end-to-end verification (webhook
delivery, cooldown suppression, rolling-window expiry, delete-stops-evaluation) was run
this way against a real `docker compose`-built `api` image, a real ClickHouse/Redis, and
a real webhook receiver — not re-derived as a mock-based unit test.

Run with `dotnet test`.
