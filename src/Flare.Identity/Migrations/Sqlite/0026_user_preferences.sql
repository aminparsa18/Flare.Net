-- Per-user UI preferences (ADR-0110): one opaque JSON document per user per key, so the
-- dashboard can add preference groups (appearance today) without a migration each time.
-- UserId is the all-zero Guid when Flare's opt-in auth is off, making the preferences shared
-- in that mode (same convention as DashboardPins).
CREATE TABLE IF NOT EXISTS UserPreferences
(
    UserId TEXT NOT NULL,
    Key TEXT NOT NULL,
    Value TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    PRIMARY KEY (UserId, Key)
);
