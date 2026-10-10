-- Error screenshots from client apps, migration 0079 (see docs-internal/adr/0174-maui-error-screenshots.md).
--
-- One row per screenshot `Flare.Maui` sends with `POST /v1/screenshots` after an exception, tied to the
-- `app.unhandled_exception` span (TraceId/SpanId) and the app session. The image is stored base64-encoded in
-- a String (JPEG/PNG/WebP, capped at 512 KB on receipt) so the insert needs no binary-safe string path.
-- `StartTime` is the time of the error span; it is named like `spans.StartTime` so the trace retention TTL
-- (ADR-0143) applies to this table with the same column.
CREATE TABLE IF NOT EXISTS clickhousedb.app_screenshots
(
    StartTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SessionId String CODEC(ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    ContentType LowCardinality(String) CODEC(ZSTD(1)),
    ImageBase64 String CODEC(LZ4),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (ServiceName, SessionId, StartTime)
SETTINGS index_granularity = 256;
