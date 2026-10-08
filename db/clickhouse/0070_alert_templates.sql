-- Alerting schema, migration 0070.
--
-- Shared alert notification templates - see
-- `docs-internal/adr/0148-shared-alert-notification-templates.md`.
--
-- `alert_templates` (Flare.Api.Model.AlertTemplateModels.cs): a named, reusable set of
-- ADR-0052 `{{placeholder}}` texts (title, fired body, resolved body, per-channel-type bodies).
-- A rule references one by id (`alert_rules.NotificationTemplateId`); at most one template is
-- the instance-wide default (`IsDefault`), which applies to rules that reference none. Same
-- CRUD-via-tombstone shape as `maintenance_windows` (migration 0031) - see migration 0003's
-- comment for the ReplacingMergeTree rationale; a handful of rows per instance.
--
-- `alert_rules.NotificationTemplateId`: NULL (every pre-existing rule) = the rule picks no
-- template, so it renders exactly as before unless a default template exists.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0069, run this by hand via
-- `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_templates
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    IsDefault UInt8 DEFAULT 0,

    TitleTemplate String CODEC(ZSTD(1)),
    BodyTemplate String CODEC(ZSTD(1)),
    ResolvedBodyTemplate String CODEC(ZSTD(1)),

    -- JSON object: NotificationChannelType name -> body template for that channel type.
    ChannelBodiesJson String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS NotificationTemplateId Nullable(UUID);
