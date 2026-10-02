-- Flare stable database semantic conventions, migration 0037.
--
-- Database client spans following the stable OTel database conventions carry
-- `db.system.name`/`db.operation.name` instead of `db.system`/`db.operation` (Npgsql 10
-- emits only the new ones). The three views below keyed database spans on `db.system`
-- alone, so such spans skipped the Services breakdown's Database tab and were counted as
-- external calls to their `server.address` instead. Each view now reads either
-- generation:
--
-- * `service_call_breakdown_database_mv` (0023): system is `db.system.name`, else
--   `db.system`; operation is `db.operation.name`, else `db.operation`, else the query
--   text's leading keyword (Npgsql 10 sets no operation for plain SQL). These are
--   `Flare.Api`'s `ServiceCallBreakdownQueryBuilder.DbSystemExpr`/`DbOperationExpr`.
-- * `service_call_breakdown_external_mv` (0034) and `outbound_calls_mv` (0035): "not a
--   database call" now means neither attribute is set -
--   `ExternalApiQueryBuilder.OutboundCallCondition`.
--
-- The expressions are verbatim copies of the live query path's; unit tests check this
-- file still contains them. `MODIFY QUERY` for the same reasons as 0034 (atomic,
-- idempotent). Only spans flushed after this runs are classified the new way.
ALTER TABLE clickhousedb.service_call_breakdown_database_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['db.system.name'] != '', SpanAttributes['db.system.name'], SpanAttributes['db.system']) AS DbSystem,
    multiIf(SpanAttributes['db.operation.name'] != '', SpanAttributes['db.operation.name'], SpanAttributes['db.operation'] != '', SpanAttributes['db.operation'], upperUTF8(extract(if(SpanAttributes['db.query.text'] != '', SpanAttributes['db.query.text'], SpanAttributes['db.statement']), '^[[:space:]]*([A-Za-z]+)'))) AS DbOperation,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans
WHERE DbSystem != ''
GROUP BY TimeBucket, ServiceName, DbSystem, DbOperation;

ALTER TABLE clickhousedb.service_call_breakdown_external_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], if(Kind = 3 AND SpanAttributes['db.system.name'] = '' AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '', multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))), '')) AS PeerService,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans
WHERE PeerService != ''
GROUP BY TimeBucket, ServiceName, PeerService;

ALTER TABLE clickhousedb.outbound_calls_mv MODIFY QUERY
SELECT
    StartTime,
    TraceId,
    SpanId,
    ServiceName,
    multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))) AS Domain,
    Name,
    StatusCode,
    DurationNano
FROM clickhousedb.spans
WHERE Kind = 3 AND SpanAttributes['db.system.name'] = '' AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '' AND SpanAttributes['peer.service'] = '' AND Domain != '';
