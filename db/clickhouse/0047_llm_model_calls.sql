-- Flare LLM model-call rollup, migration 0047.
--
-- Pre-aggregated per-(service, provider, model) call stats for the /llm page, computed at
-- span-flush time instead of re-scanned from `spans` on every page load - see ADR-0102.
-- Same "compute once at flush time" precedent as `service_metrics` (ADR-0030) and
-- `service_dependency_nodes` (ADR-0031).
--
-- The materialized view repeats LlmQueryBuilder's expressions verbatim: the model-call
-- condition (chat/text_completion/generate_content/embeddings, or no operation but a model),
-- provider/model fallbacks between current and older gen_ai.* names, and token attributes
-- read with toUInt64OrZero (ADR-0100). A semantic-convention change therefore needs a new
-- migration as well as a builder edit; LlmMetrics:Enabled=false falls back to the live
-- query in the meantime.
--
-- Service is a key dimension, so the unfiltered request and the toolbar's service filter
-- are both served from this table.
CREATE TABLE IF NOT EXISTS clickhousedb.llm_model_calls
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
ENGINE = AggregatingMergeTree
PARTITION BY toStartOfMonth(TimeBucket)
ORDER BY (Model, Provider, ServiceName, TimeBucket)
SETTINGS index_granularity = 8192;

CREATE MATERIALIZED VIEW IF NOT EXISTS clickhousedb.llm_model_calls_mv
TO clickhousedb.llm_model_calls
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
FROM clickhousedb.spans
WHERE (mapContains(SpanAttributes, 'gen_ai.operation.name') OR mapContains(SpanAttributes, 'gen_ai.request.model') OR mapContains(SpanAttributes, 'gen_ai.response.model'))
  AND (SpanAttributes['gen_ai.operation.name'] IN ('chat', 'text_completion', 'generate_content', 'embeddings')
       OR (SpanAttributes['gen_ai.operation.name'] = ''
           AND if(SpanAttributes['gen_ai.request.model'] != '', SpanAttributes['gen_ai.request.model'], SpanAttributes['gen_ai.response.model']) != ''))
GROUP BY TimeBucket, ServiceName, Provider, Model;
