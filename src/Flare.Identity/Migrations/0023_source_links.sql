-- SourceLinks: per-service source repository config for the /errors stack-trace "open in
-- repo" links (docs-internal/adr/0095-exception-source-links.md). A service with no row
-- gets plain, unlinked stack traces - only configured services are stored. Per-installation
-- config, not telemetry, hence Identity's SQLite - same reasoning as ApdexThresholds.
CREATE TABLE IF NOT EXISTS SourceLinks
(
    ServiceName TEXT PRIMARY KEY,
    Provider TEXT NOT NULL,
    RepoUrl TEXT NOT NULL,
    DefaultRef TEXT NOT NULL,
    PathPrefix TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
