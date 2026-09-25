-- Alerting schema, migration 0031.
--
-- Planned maintenance windows (alert silencing) - see
-- `docs-internal/adr/0055-alert-maintenance-windows.md`.
--
-- `maintenance_windows` (Flare.Api.Model.MaintenanceWindowModels.cs): a one-off or recurring
-- time range during which `Flare.AlertWorker` still evaluates the covered rules (all of them
-- when `RuleIds` is empty) but records a breach as a suppressed `alert_events` row instead of
-- notifying. Same CRUD-via-tombstone shape as `alert_rules` (migration 0003) and
-- `notification_channels` (migration 0016), and for the same reason - see migration 0003's
-- comment for the ReplacingMergeTree/FINAL rationale; a handful of rows per instance.
--
-- `alert_events.SuppressedByWindow`: the name (snapshot, like `RuleName`) of the maintenance
-- window that suppressed this event's notification; '' for every normal event, which is also
-- the column default for every pre-existing row. Such an event's `NotificationStatus` is
-- 'Suppressed'.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0030, run this by hand via
-- `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.maintenance_windows
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- Covered alert rule IDs; empty = every rule.
    RuleIds Array(UUID),

    -- The first (or, for Recurrence = 'None', only) occurrence. A recurring window repeats
    -- this span at the same local time of day in TimeZone.
    StartsAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    EndsAt DateTime64(3) CODEC(Delta, ZSTD(1)),

    -- "None" | "Daily" | "Weekly" - MaintenanceWindowRecurrence's string names verbatim.
    Recurrence LowCardinality(String),

    -- System.DayOfWeek ordinals (0 = Sunday); meaningful only when Recurrence = 'Weekly'.
    DaysOfWeek Array(UInt8),

    -- Recurring only: no occurrence starts at or after this instant. NULL = repeats forever.
    RepeatUntil Nullable(DateTime64(3)),

    -- IANA zone id the recurrence's local time of day/weekdays are computed in.
    TimeZone LowCardinality(String),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS SuppressedByWindow String DEFAULT '';
