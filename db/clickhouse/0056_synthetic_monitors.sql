-- Synthetic monitoring schema, migration 0056.
--
-- `synthetic_monitors` (Flare.Api.Model.SyntheticMonitorModels.cs): a scheduled HTTP, TCP or TLS-certificate
-- probe. Flare.AlertWorker runs each enabled monitor every `IntervalSeconds` and writes the result into
-- `metrics_gauge` (synthetic.up, synthetic.duration, synthetic.cert.expiry_days, synthetic.http.status_code),
-- so metric alerts, SLO-style charts and dashboards work on probes with no new alert type. See
-- `docs-internal/adr/0128-synthetic-monitoring.md`. Same CRUD-via-tombstone shape as `maintenance_windows`
-- (migration 0031) and `oncall_rotations` (0055), and for the same reason.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0055, run this by hand via `clickhouse-client`
-- against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.synthetic_monitors
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 1,

    -- 'Http' | 'Tcp' | 'Tls'
    Kind LowCardinality(String),

    -- Http: an absolute http(s) URL. Tcp/Tls: `host:port` (Tls defaults to :443 when the port is omitted).
    Target String CODEC(ZSTD(1)),

    -- Http only. Empty means GET.
    Method LowCardinality(String) DEFAULT 'GET',

    -- Http only: the status code that counts as up. 0 means any 2xx or 3xx.
    ExpectedStatus UInt16 DEFAULT 0,

    IntervalSeconds UInt32,
    TimeoutSeconds UInt32,

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
