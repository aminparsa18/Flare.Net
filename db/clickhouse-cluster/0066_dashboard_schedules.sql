-- Dashboard report schedules and their run history, migration 0066 (cluster variant).
-- See ../clickhouse/0066_dashboard_schedules.sql for the column notes.
CREATE TABLE IF NOT EXISTS clickhousedb.dashboard_schedules_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    DashboardId UUID,
    Name String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 1,
    Cron String CODEC(ZSTD(1)),
    TimeZone String CODEC(ZSTD(1)),
    Recipients String CODEC(ZSTD(1)),
    TimeRange String CODEC(ZSTD(1)),
    VariableQuery String CODEC(ZSTD(1)),
    Format String DEFAULT 'pdf' CODEC(ZSTD(1)),
    OwnerUserId Nullable(UUID),
    NextRunAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplicatedReplacingMergeTree('/clickhouse/tables/{shard}/clickhousedb/dashboard_schedules_local', '{replica}', UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.dashboard_schedules ON CLUSTER 'flare_cluster' AS clickhousedb.dashboard_schedules_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'dashboard_schedules_local', rand())
SETTINGS insert_distributed_sync = 1;

CREATE TABLE IF NOT EXISTS clickhousedb.dashboard_report_runs_local ON CLUSTER 'flare_cluster'
(
    Id UUID,
    ScheduleId UUID,
    DashboardId UUID,
    StartedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    DurationMs UInt32,
    Status LowCardinality(String),
    Error String CODEC(ZSTD(1)),
    RecipientCount UInt32,
    SizeBytes UInt64
)
ENGINE = ReplicatedMergeTree('/clickhouse/tables/{shard}/clickhousedb/dashboard_report_runs_local', '{replica}')
ORDER BY (ScheduleId, StartedAt)
TTL toDateTime(StartedAt) + INTERVAL 90 DAY
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.dashboard_report_runs ON CLUSTER 'flare_cluster' AS clickhousedb.dashboard_report_runs_local
ENGINE = Distributed('flare_cluster', 'clickhousedb', 'dashboard_report_runs_local', rand())
SETTINGS insert_distributed_sync = 1;
