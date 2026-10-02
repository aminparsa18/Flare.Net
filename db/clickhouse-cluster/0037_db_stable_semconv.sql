-- Stable database semantic conventions, migration 0037 - CLUSTER VARIANT.
--
-- Same change/rationale as db/clickhouse/0037_db_stable_semconv.sql: the three views
-- read `db.system.name`/`db.operation.name` as well as `db.system`/`db.operation`. Still
-- read `spans_local`, same as their 0023/0035 cluster variants.
ALTER TABLE clickhousedb.service_call_breakdown_database_mv ON CLUSTER 'flare_cluster' MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['db.system.name'] != '', SpanAttributes['db.system.name'], SpanAttributes['db.system']) AS DbSystem,
    multiIf(SpanAttributes['db.operation.name'] != '', SpanAttributes['db.operation.name'], SpanAttributes['db.operation'] != '', SpanAttributes['db.operation'], upperUTF8(extract(if(SpanAttributes['db.query.text'] != '', SpanAttributes['db.query.text'], SpanAttributes['db.statement']), '^[[:space:]]*([A-Za-z]+)'))) AS DbOperation,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans_local
WHERE DbSystem != ''
GROUP BY TimeBucket, ServiceName, DbSystem, DbOperation;

ALTER TABLE clickhousedb.service_call_breakdown_external_mv ON CLUSTER 'flare_cluster' MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], if(Kind = 3 AND SpanAttributes['db.system.name'] = '' AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '', multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))), '')) AS PeerService,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans_local
WHERE PeerService != ''
GROUP BY TimeBucket, ServiceName, PeerService;

ALTER TABLE clickhousedb.outbound_calls_mv ON CLUSTER 'flare_cluster' MODIFY QUERY
SELECT
    StartTime,
    TraceId,
    SpanId,
    ServiceName,
    multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))) AS Domain,
    Name,
    StatusCode,
    DurationNano
FROM clickhousedb.spans_local
WHERE Kind = 3 AND SpanAttributes['db.system.name'] = '' AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '' AND SpanAttributes['peer.service'] = '' AND Domain != '';
