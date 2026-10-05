-- Alerting schema, migration 0055.
--
-- On-call rotations - see `docs-internal/adr/0126-alert-oncall-rotations.md`.
--
-- `oncall_rotations` (Flare.Api.Model.OnCallRotationModels.cs): an ordered list of notification
-- channels (`ChannelIds`, one per person or team) that take turns being on call. Each shift lasts
-- `ShiftHours`, the first one starts at `StartsAt`, and the list repeats. Who is on call at an
-- instant is computed (Flare.Api.Alerting.OnCallSchedule), never stored. Same CRUD-via-tombstone
-- shape as `maintenance_windows` (migration 0031) and for the same reason.
--
-- `alert_rules.EscalationRotationId`: when set, an escalation (ADR-0125) also goes to the channel
-- on call in that rotation at that moment, in addition to the rule's `EscalationChannelIds`.
-- NULL (every existing rule) means no rotation. There is no foreign key; a deleted rotation
-- simply resolves to nobody.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0054, run this by hand via
-- `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.oncall_rotations
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- Participants in shift order: notification channel ids.
    ChannelIds Array(UUID),

    -- Length of one shift, in hours (1 to 8760).
    ShiftHours UInt32,

    -- Start of the first shift (participant 0). Before it, participant 0 is on call.
    StartsAt DateTime64(3) CODEC(Delta, ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS EscalationRotationId Nullable(UUID);
