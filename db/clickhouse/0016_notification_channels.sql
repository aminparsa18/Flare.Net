-- Alerting schema, migration 0016.
--
-- `NotificationChannel` (Flare.Api.Model.NotificationChannelModels.cs) - a saved,
-- reusable notification destination, referenced by ID from zero or more
-- `alert_rules.ChannelIds` (migration 0017) instead of being re-entered inline on every
-- rule that should reach it. See docs-internal/adr/0021-reusable-notification-channels.md
-- for the full design, including why this coexists with (rather than replaces)
-- `alert_rules`' own legacy inline WebhookUrl/Telegram*/EmailTo/PagerDutyRoutingKey
-- columns.
--
-- Same CRUD-via-tombstone shape as `alert_rules` (migration 0003) and for the same
-- reason - see that migration's comment for the full ReplacingMergeTree/FINAL rationale;
-- it applies here verbatim (an even smaller expected table: a handful to a few dozen
-- saved channels per self-hosted instance).
CREATE TABLE IF NOT EXISTS clickhousedb.notification_channels
(
    Id UUID,
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,

    -- "Webhook" | "Telegram" | "Email" | "PagerDuty" - NotificationChannelType's string
    -- names verbatim (the camelCase wire form is applied by NotificationChannelsJsonContext
    -- at the API boundary, not here). LowCardinality: a closed, tiny enum.
    Type LowCardinality(String),

    -- Only the column(s) matching Type are meaningful - same "column present, meaningful
    -- only for one mode" shape alert_rules' own notification-channel columns already use.
    WebhookUrl String CODEC(ZSTD(1)),
    TelegramBotToken String CODEC(ZSTD(1)),
    TelegramChatId String CODEC(ZSTD(1)),
    EmailTo String CODEC(ZSTD(1)),
    PagerDutyRoutingKey String CODEC(ZSTD(1)),

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;
