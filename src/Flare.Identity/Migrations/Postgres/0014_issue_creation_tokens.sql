-- Write-scoped credentials for creating issues from Flare (docs-internal/adr/0275-create-issue-from-flare.md).
-- Kept apart from SourceLinks.AccessToken, which is recommended read-only (ADR-0096). Write-only through the API: NULL = none.
-- SourceLinks.IssueToken: GitHub / GitLab token with permission to open issues on the service's source repo.
-- IssueTrackers.ApiToken + AccountEmail: Jira API token; with an email it is Cloud Basic auth, without one a Server/Data Center PAT.
ALTER TABLE SourceLinks ADD COLUMN IF NOT EXISTS IssueToken TEXT;
ALTER TABLE IssueTrackers ADD COLUMN IF NOT EXISTS ApiToken TEXT;
ALTER TABLE IssueTrackers ADD COLUMN IF NOT EXISTS AccountEmail TEXT NOT NULL DEFAULT '';
