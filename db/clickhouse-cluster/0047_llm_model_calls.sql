-- LLM model-call rollup, migration 0047 - CLUSTER VARIANT.
--
-- Same columns/rationale as db/clickhouse/0047_llm_model_calls.sql (see ADR-0102), same
-- `_local` + `Replicated<Engine>` + `Distributed` wrapper pattern as 0022/0023. The view
-- attaches to `spans_local`; `spans` shards by TraceId, so one (service, provider, model)
-- key can hold partial states on several shards, reconciled by the Distributed table +
-- quantilesMerge() at query time.
CREATE TABLE IF NOT EXISTS clickhousedb.llm_model_calls_local ON CLUSTER 'flare_cluster'
(
    TimeBucket DateTime CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    Provider LowCardinality(String) CODEC(ZSTD(1)),
    Model LowCardinality(String) CODEC(ZSTD(1)),

    CallCount SimpleAggregateFunction(sum, UInt64),
    ErrorCount SimpleAggregateFunction(sum, UInt64),
    InputTokens SimpleAggregateFunction(sum, UInt64),
    OutputTokens SimpleAggregateFunction(sum, UInt64),
    LastSeen SimpleAggregateFunction(max, DateTime64(3)),

    QuantileState AggregateFunction(quantiles(0.5, 0.95, 0.99), UInt64)
)
ENGINE = ReplicatedAggregatingMergeTree('/clickhouse/tables/{shard}/clickhousedb/llm_model_calls_local', '{replica}')
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (Model, Provider, ServiceName, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.llm_model_calls ON CLUSTER 'flare_cluster' AS clickhousedb.llm_model_calls_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'llm_model_calls_local', cityHash64(Model))
SETTINGS insert_distributed_sync = 1;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.llm_model_calls_mv ON CLUSTER 'flare_cluster'
TO clickhousedb.llm_model_calls_local
AS
SELECT
    toStartOfMinute(StartTime) AS TimeBucket,
    ServiceName,
    if(SpanAttributes['gen_ai.provider.name'] != '', SpanAttributes['gen_ai.provider.name'], SpanAttributes['gen_ai.system']) AS Provider,
    if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model']) AS Model,
    count() AS CallCount,
    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,
    sum(toUInt64OrZero(if(SpanAttributes['gen_ai.usage.input_tokens'] != '', SpanAttributes['gen_ai.usage.input_tokens'], SpanAttributes['gen_ai.usage.prompt_tokens']))) AS InputTokens,
    sum(toUInt64OrZero(if(SpanAttributes['gen_ai.usage.output_tokens'] != '', SpanAttributes['gen_ai.usage.output_tokens'], SpanAttributes['gen_ai.usage.completion_tokens']))) AS OutputTokens,
    max(toDateTime64(StartTime, 3)) AS LastSeen,
    quantilesState(0.5, 0.95, 0.99)(DurationNano) AS QuantileState
FROM clickhousedb.spans_local
WHERE (mapContains(SpanAttributes, 'gen_ai.operation.name') OR mapContains(SpanAttributes, 'gen_ai.request.model') OR mapContains(SpanAttributes, 'gen_ai.response.model'))
  AND (SpanAttributes['gen_ai.operation.name'] IN ('chat', 'text_completion', 'generate_content', 'embeddings')
       OR (SpanAttributes['gen_ai.operation.name'] = ''
           AND if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model']) != ''))
GROUP BY TimeBucket, ServiceName, Provider, Model;
