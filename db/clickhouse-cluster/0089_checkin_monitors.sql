-- Same columns/rationale as db/clickhouse/0089_checkin_monitors.sql.
-- `checkin_monitors` follows synthetic_monitors' cluster variant (0056); `checkin_events` is sharded by Slug so one
-- monitor's pings live on one shard.
CREATE TABLE IF NOT EXISTS clickhousedb.checkin_monitors_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    Slug String CODEC(ZSTD(1)),
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 1,
    CronExpression String CODEC(ZSTD(1)),
    IntervalSeconds UInt32 DEFAULT 0,
    TimeZone String DEFAULT 'UTC' CODEC(ZSTD(1)),
    GraceSeconds UInt32,
    MaxRuntimeSeconds UInt32 DEFAULT 0,
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/checkin_monitors_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.checkin_monitors ON CLUSTER 'flare_cluster' AS clickhousedb.checkin_monitors_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'checkin_monitors_local', rand())
SETTINGS insert_distributed_sync = 1;

CREATE TABLE IF NOT EXISTS clickhousedb.checkin_events_local ON CLUSTER 'flare_cluster'
(
    Slug String CODEC(ZSTD(1)),
    Time DateTime64(3) CODEC(Delta, ZSTD(1)),
    Status LowCardinality(String),
    Message String CODEC(ZSTD(3))
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/checkin_events_local', '{replica}')
PARTITION BY toStartOfMonth(Time)
ORDER BY (Slug, Time)
TTL toDateTime(Time) + INTERVAL 1 YEAR
SETTINGS index_granularity = 256;

CREATE TABLE IF NOT EXISTS clickhousedb.checkin_events ON CLUSTER 'flare_cluster' AS clickhousedb.checkin_events_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'checkin_events_local', cityHash64(Slug))
SETTINGS insert_distributed_sync = 1;
