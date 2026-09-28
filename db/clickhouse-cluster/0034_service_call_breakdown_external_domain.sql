-- Service call-breakdown schema, migration 0034 - CLUSTER VARIANT.
--
-- Same change/rationale as db/clickhouse/0034_service_call_breakdown_external_domain.sql
-- (see ADR-0071): the external-calls view falls back to the called domain when
-- `peer.service` is unset. Still reads `spans_local`, same as 0023's cluster variant.
ALTER TABLE clickhousedb.service_call_breakdown_external_mv ON CLUSTER 'flare_cluster' MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], if(Kind = 3 AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '', multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))), '')) AS PeerService,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans_local
WHERE PeerService != ''
GROUP BY TimeBucket, ServiceName, PeerService;
