-- Span sample weight, migration 0051.
--
-- Ingest-side head/tail sampling (ADR-0122) stores each kept span with `SampleWeight` =
-- how many real spans it stands for (1 for unsampled and tail-kept spans, N for a span
-- kept by a 1-in-N head decision). Every pre-aggregating materialized view below now sums
-- that weight instead of counting rows, so RED, the service map, call breakdowns, LLM
-- rollups and SLI counts stay unbiased when sampling is on. With sampling off every weight
-- is 1 and every number is identical to before.
--
-- Quantiles can't be weighted in the existing `quantile` states, so each quantile table
-- gains weighted t-digest columns (`P50WState`, ...) plus `SampledCount` (spans in the
-- bucket with weight > 1). `Flare.Api` reads the weighted states only when a window's
-- `SampledCount` is non-zero and the original states otherwise, so history from before
-- this migration keeps its percentiles until sampling is actually used. The original
-- states keep being filled (unweighted) for exactly that purpose.
--
-- `MODIFY QUERY` for the same reasons as 0034/0037 (atomic, idempotent). The grouping-key
-- expressions are copied verbatim from 0037/0047; only counts and states changed.
-- `outbound_calls` stays one row per span and gains `SampleWeight` for the leaf counts.

ALTER TABLE clickhousedb.spans
    ADD COLUMN IF NOT EXISTS SampleWeight UInt32 DEFAULT 1 CODEC(ZSTD(1));
ALTER TABLE clickhousedb.service_metrics
    ADD COLUMN IF NOT EXISTS SampledCount SimpleAggregateFunction(sum, UInt64),
    ADD COLUMN IF NOT EXISTS P50WState AggregateFunction(quantileTDigestWeighted(0.5), UInt64, UInt32),
    ADD COLUMN IF NOT EXISTS P95WState AggregateFunction(quantileTDigestWeighted(0.95), UInt64, UInt32),
    ADD COLUMN IF NOT EXISTS P99WState AggregateFunction(quantileTDigestWeighted(0.99), UInt64, UInt32);
ALTER TABLE clickhousedb.service_call_breakdown_external
    ADD COLUMN IF NOT EXISTS SampledCount SimpleAggregateFunction(sum, UInt64),
    ADD COLUMN IF NOT EXISTS P50WState AggregateFunction(quantileTDigestWeighted(0.5), UInt64, UInt32),
    ADD COLUMN IF NOT EXISTS P95WState AggregateFunction(quantileTDigestWeighted(0.95), UInt64, UInt32);
ALTER TABLE clickhousedb.service_call_breakdown_database
    ADD COLUMN IF NOT EXISTS SampledCount SimpleAggregateFunction(sum, UInt64),
    ADD COLUMN IF NOT EXISTS P50WState AggregateFunction(quantileTDigestWeighted(0.5), UInt64, UInt32),
    ADD COLUMN IF NOT EXISTS P95WState AggregateFunction(quantileTDigestWeighted(0.95), UInt64, UInt32);
ALTER TABLE clickhousedb.llm_model_calls
    ADD COLUMN IF NOT EXISTS SampledCount SimpleAggregateFunction(sum, UInt64),
    ADD COLUMN IF NOT EXISTS QuantileWState AggregateFunction(quantilesTDigestWeighted(0.5, 0.95, 0.99), UInt64, UInt32);
ALTER TABLE clickhousedb.outbound_calls
    ADD COLUMN IF NOT EXISTS SampleWeight UInt32 DEFAULT 1 CODEC(ZSTD(1));

ALTER TABLE clickhousedb.service_metrics_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    sum(toUInt64(SampleWeight)) AS RequestCount,
    sumIf(toUInt64(SampleWeight), StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State,
    quantileState(0.99)(DurationNano) AS P99State,
    countIf(SampleWeight > 1) AS SampledCount,
    quantileTDigestWeightedState(0.5)(DurationNano, SampleWeight) AS P50WState,
    quantileTDigestWeightedState(0.95)(DurationNano, SampleWeight) AS P95WState,
    quantileTDigestWeightedState(0.99)(DurationNano, SampleWeight) AS P99WState
FROM clickhousedb.spans
WHERE ParentSpanId = ''
GROUP BY TimeBucket, ServiceName;

ALTER TABLE clickhousedb.service_dependency_nodes_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], ServiceName) AS Service,
    sum(toUInt64(SampleWeight)) AS SpanCount,
    sumIf(toUInt64(SampleWeight), StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    sum(DurationNano * toUInt64(SampleWeight)) AS TotalDurationNano,
    topKState(3)(Name) AS TopOperationsState
FROM clickhousedb.spans
GROUP BY TimeBucket, Service;

ALTER TABLE clickhousedb.service_call_breakdown_external_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['peer.service'] != '', SpanAttributes['peer.service'], if(Kind = 3 AND SpanAttributes['db.system.name'] = '' AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '', multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))), '')) AS PeerService,
    sum(toUInt64(SampleWeight)) AS CallCount,
    sumIf(toUInt64(SampleWeight), StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State,
    countIf(SampleWeight > 1) AS SampledCount,
    quantileTDigestWeightedState(0.5)(DurationNano, SampleWeight) AS P50WState,
    quantileTDigestWeightedState(0.95)(DurationNano, SampleWeight) AS P95WState
FROM clickhousedb.spans
WHERE PeerService != ''
GROUP BY TimeBucket, ServiceName, PeerService;

ALTER TABLE clickhousedb.service_call_breakdown_database_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['db.system.name'] != '', SpanAttributes['db.system.name'], SpanAttributes['db.system']) AS DbSystem,
    multiIf(SpanAttributes['db.operation.name'] != '', SpanAttributes['db.operation.name'], SpanAttributes['db.operation'] != '', SpanAttributes['db.operation'], upperUTF8(extract(if(SpanAttributes['db.query.text'] != '', SpanAttributes['db.query.text'], SpanAttributes['db.statement']), '^[[:space:]]*([A-Za-z]+)'))) AS DbOperation,
    sum(toUInt64(SampleWeight)) AS CallCount,
    sumIf(toUInt64(SampleWeight), StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    quantileState(0.5)(DurationNano) AS P50State,
    quantileState(0.95)(DurationNano) AS P95State,
    countIf(SampleWeight > 1) AS SampledCount,
    quantileTDigestWeightedState(0.5)(DurationNano, SampleWeight) AS P50WState,
    quantileTDigestWeightedState(0.95)(DurationNano, SampleWeight) AS P95WState
FROM clickhousedb.spans
WHERE DbSystem != ''
GROUP BY TimeBucket, ServiceName, DbSystem, DbOperation;

ALTER TABLE clickhousedb.outbound_calls_mv MODIFY QUERY
SELECT
    StartTime,
    TraceId,
    SpanId,
    ServiceName,
    multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], domain(if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url']))) AS Domain,
    Name,
    StatusCode,
    DurationNano,
    SampleWeight
FROM clickhousedb.spans
WHERE Kind = 3 AND SpanAttributes['db.system.name'] = '' AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = '' AND SpanAttributes['peer.service'] = '' AND Domain != '';

ALTER TABLE clickhousedb.llm_model_calls_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['gen_ai.provider.name'] != '', SpanAttributes['gen_ai.provider.name'], SpanAttributes['gen_ai.system']) AS Provider,
    if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model']) AS Model,
    sum(toUInt64(SampleWeight)) AS CallCount,
    sumIf(toUInt64(SampleWeight), StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    sum(toUInt64OrZero(if(SpanAttributes['gen_ai.usage.input_tokens'] != '', SpanAttributes['gen_ai.usage.input_tokens'], SpanAttributes['gen_ai.usage.prompt_tokens'])) * toUInt64(SampleWeight)) AS InputTokens,
    sum(toUInt64OrZero(if(SpanAttributes['gen_ai.usage.output_tokens'] != '', SpanAttributes['gen_ai.usage.output_tokens'], SpanAttributes['gen_ai.usage.completion_tokens'])) * toUInt64(SampleWeight)) AS OutputTokens,
    max(toDateTime64(StartTime, 3)) AS LastSeen,
    quantilesState(0.5, 0.95, 0.99)(DurationNano) AS QuantileState,
    countIf(SampleWeight > 1) AS SampledCount,
    quantilesTDigestWeightedState(0.5, 0.95, 0.99)(DurationNano, SampleWeight) AS QuantileWState
FROM clickhousedb.spans
WHERE (mapContains(SpanAttributes, 'gen_ai.operation.name') OR mapContains(SpanAttributes, 'gen_ai.request.model') OR mapContains(SpanAttributes, 'gen_ai.response.model'))
  AND (SpanAttributes['gen_ai.operation.name'] IN ('chat', 'text_completion', 'generate_content', 'embeddings')
       OR (SpanAttributes['gen_ai.operation.name'] = ''
           AND if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model']) != ''))
GROUP BY TimeBucket, ServiceName, Provider, Model;

ALTER TABLE clickhousedb.span_sli_minute_mv MODIFY QUERY
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    Name,
    sum(toUInt64(SampleWeight)) AS TotalCount,
    sumIf(toUInt64(SampleWeight), StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    sumIf(toUInt64(SampleWeight), DurationNano <= 50000000) AS Under50ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 100000000) AS Under100ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 250000000) AS Under250ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 500000000) AS Under500ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 1000000000) AS Under1000ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 2500000000) AS Under2500ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 5000000000) AS Under5000ms,
    sumIf(toUInt64(SampleWeight), DurationNano <= 10000000000) AS Under10000ms
FROM clickhousedb.spans
WHERE Kind IN (2, 5) OR ParentSpanId = ''
GROUP BY TimeBucket, ServiceName, Name;
