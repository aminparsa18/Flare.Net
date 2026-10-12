-- User feedback from client apps, migration 0082 (see docs-internal/adr/0193-maui-user-feedback.md).
--
-- One row per "report a problem" message `Flare.Maui` sends with `POST /v1/feedback`. The session id links the
-- row to the session timeline and replay, so the report arrives with the context that led to it. `StartTime` is
-- the receive time, named like `spans.StartTime` so the Traces retention TTL (ADR-0143) covers this table.
-- `Email` and `Name` are stored only if the app chose to send them.
CREATE TABLE IF NOT EXISTS clickhousedb.app_feedback
(
    StartTime DateTime64(3) CODEC(Delta, ZSTD(1)),
    FeedbackId UUID,
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    SessionId String CODEC(ZSTD(1)),
    UserId String CODEC(ZSTD(1)),
    Name String CODEC(ZSTD(1)),
    Email String CODEC(ZSTD(1)),
    AppVersion LowCardinality(String) CODEC(ZSTD(1)),
    Message String CODEC(ZSTD(3)),
    IngestedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(StartTime)
ORDER BY (ServiceName, StartTime, FeedbackId)
SETTINGS index_granularity = 256;
