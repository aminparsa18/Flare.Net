-- Continuous-profiling sample storage, migration 0065 (see
-- docs-internal/adr/0141-continuous-profiling-ingest.md).
--
-- One row per OTLP profiles `Sample` (a stack plus its measured value), with the
-- export-wide dictionary (strings, functions, locations, stacks, links) already resolved
-- by `Flare.Ingest`, so a query never joins a dictionary table: the stack is stored as the
-- array of frame names. Flame graphs are `GROUP BY Stack` + `sum(Value)`.
CREATE TABLE IF NOT EXISTS clickhousedb.profile_samples
(
    -- Profile.time_unix_nano, or the earliest per-sample timestamp when the sample carries
    -- them. Same DateTime64(9) + tick-truncation note as spans.StartTime.
    Timestamp DateTime64(9) CODEC(Delta, ZSTD(1)),

    -- Profile.profile_id as lower hex; empty when the sender didn't set one. Groups the
    -- samples of one profile (one export of one profiler run).
    ProfileId String CODEC(ZSTD(1)),

    -- Profile.duration_nano - the window the profile covers.
    DurationNano UInt64 CODEC(ZSTD(1)),

    ServiceName LowCardinality(String) CODEC(ZSTD(1)),

    -- Profile.sample_type, e.g. ('cpu', 'nanoseconds') or ('alloc_space', 'bytes').
    SampleType LowCardinality(String) CODEC(ZSTD(1)),
    SampleUnit LowCardinality(String) CODEC(ZSTD(1)),

    -- Frame names, ROOT first and leaf last (folded-stack order, the reverse of the OTLP
    -- wire's leaf-first Stack.location_indices). Inlined functions are expanded into their
    -- own frames. Native frames without symbols are `<mapping file>+0x<addr>`.
    Stack Array(LowCardinality(String)) CODEC(ZSTD(1)),

    -- Hash of Stack, computed by ClickHouse on insert (not in the insert column list), so a
    -- flame-graph GROUP BY compares 8 bytes instead of the array.
    StackHash UInt64 MATERIALIZED cityHash64(Stack) CODEC(ZSTD(1)),

    -- Sum of Sample.values, or the timestamp count when the sample only has timestamps (each
    -- counts as 1). In SampleUnit.
    Value Int64 CODEC(ZSTD(1)),

    -- Sample.link_index resolved: the span that was active when the sample was taken, lower
    -- hex, empty when the sample has no link. This is the span-to-flame-graph join key.
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),

    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    SampleAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),

    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1)),

    INDEX idx_trace TraceId TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_key mapKeys(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1,
    INDEX idx_res_attr_value mapValues(ResourceAttributes) TYPE bloom_filter(0.01) GRANULARITY 1
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(Timestamp)
ORDER BY (ServiceName, SampleType, toStartOfMinute(Timestamp), StackHash)
SETTINGS index_granularity = 8192;
