-- Flare service call-breakdown schema, migration 0034.
--
-- Re-points `service_call_breakdown_external_mv` (0023) at the External-calls tab's new
-- grouping key: `peer.service` when set, else - for an outbound CLIENT span that isn't a
-- database or messaging call - the domain it called (`server.address`, else
-- `net.peer.name`, else the URL's host). .NET's HttpClient instrumentation never sets
-- `peer.service`, so before this a typical .NET app's calls to Stripe/Twilio/another
-- team's API never reached this table at all. See ADR-0071.
--
-- The expression is `Flare.Api`'s `ServiceCallBreakdownQueryBuilder.ExternalTargetExpr`,
-- verbatim - the live query path uses the same one, and a unit test checks this file
-- still contains it. No column changes: the value still lands in `PeerService`.
--
-- `MODIFY QUERY` rather than DROP + CREATE: atomic (no window where inserts into `spans`
-- skip the view) and idempotent, so ClickHouseMigrationRunner can re-run it on every
-- boot. Only spans flushed after this runs get the fallback - rows already aggregated
-- keep their `peer.service`-only grouping until they age out.
ALTER TABLE clickhousedb.service_call_breakdown_external_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], if(Kind = 3 AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '', multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))), '')) AS PeerService,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State
FROM clickhousedb.spans
WHERE PeerService != ''
GROUP BY TimeBucket, ServiceName, PeerService;
