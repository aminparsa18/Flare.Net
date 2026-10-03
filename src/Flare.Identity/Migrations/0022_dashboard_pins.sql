-- Per-user dashboard pins (ADR-0089): a pinned dashboard floats to the top of that user's
-- dashboard list. DashboardId references a ClickHouse dashboard, so there is no foreign key;
-- a pin for a since-deleted dashboard is inert and the client ignores it. UserId is the
-- all-zero Guid when Flare's opt-in auth is off, making pins shared in that mode.
CREATE TABLE IF NOT EXISTS DashboardPins
(
    UserId TEXT NOT NULL,
    DashboardId TEXT NOT NULL,
    PinnedAt TEXT NOT NULL,
    PRIMARY KEY (UserId, DashboardId)
);
