-- IssueTrackers: per-service issue tracker config for the /errors "New Jira issue" link
-- (docs-internal/adr/0274-jira-new-issue-link.md). A service with no row gets no tracker menu
-- item - only configured services are stored. Per-installation config, not telemetry, hence
-- Identity's SQLite - same reasoning as SourceLinks. Provider is a column so a later tracker
-- (and a write token for issue creation) does not need a new table.
CREATE TABLE IF NOT EXISTS IssueTrackers
(
    ServiceName TEXT PRIMARY KEY,
    Provider TEXT NOT NULL,
    SiteUrl TEXT NOT NULL,
    ProjectId TEXT NOT NULL,
    IssueTypeId TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
