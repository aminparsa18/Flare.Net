-- Browser session replay, migration 0090 (see docs-internal/adr/0264-browser-session-replay.md).
--
-- One row per batch of rrweb events that `@flare/browser-replay` sends with `POST /v1/replay`. `Payload` is the
-- batch as a JSON array text (rrweb event objects, validated on receipt, at most 10 MB), compressed by the
-- column codec rather than by the application. `StartTime`/`EndTime` are the first and last event timestamps (the
-- browser's clock, clamped to the server's on receipt) and `StartTime` is named like `spans.StartTime` so the
-- trace retention TTL (ADR-0143) applies with the same column. The dashboard lists a session's chunks by `SessionId`
-- without reading `Payload`, then fetches each one by `ChunkId`.
CREATE TABLE IF NOT EXISTS clickhousedb.web_replay_chunks
(
    StartTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    EndTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SessionId String CODEC(ZSTD(1)),
    ChunkId UUID CODEC(ZSTD(1)),
    EventCount UInt32 CODEC(ZSTD(1)),
    Payload String CODEC(ZSTD(3)),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (ServiceName, SessionId, StartTime)
SETTINGS index_granularity = 256;
