-- Projects (ADR-0123): a named boundary owning a set of service.name patterns, with its own
-- member roles. A project's patterns scope what its members can query; membership role is
-- independent of the global Users.Role. Pattern is an exact name or a trailing-* prefix.
CREATE TABLE IF NOT EXISTS Projects
(
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ProjectServicePatterns
(
    ProjectId TEXT NOT NULL REFERENCES Projects (Id) ON DELETE CASCADE,
    Pattern TEXT NOT NULL,
    PRIMARY KEY (ProjectId, Pattern)
);
CREATE TABLE IF NOT EXISTS ProjectMembers
(
    ProjectId TEXT NOT NULL REFERENCES Projects (Id) ON DELETE CASCADE,
    UserId TEXT NOT NULL REFERENCES Users (Id) ON DELETE CASCADE,
    Role TEXT NOT NULL CHECK (Role IN ('Admin', 'Member', 'Viewer')),
    PRIMARY KEY (ProjectId, UserId)
);
CREATE INDEX IF NOT EXISTS IX_ProjectMembers_UserId ON ProjectMembers (UserId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Projects_Name ON Projects (LOWER(Name));
