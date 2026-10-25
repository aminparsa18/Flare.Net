# ADR-0274: Prefilled new-issue link for Jira

Status: Accepted

Date: 2026-10-25

## Context

ADR-0195 and ADR-0273 give `/errors` groups a "New GitHub issue" / "New GitLab issue" menu item built
from the service's ADR-0095 source link: a prefilled URL, no token, no outbound call. Jira has no
equivalent configuration. A prefilled Jira create URL needs the site URL and the numeric project id,
and a team's tracker is independent of where its code lives (Jira for tickets, GitHub for code is the
common case), so it cannot ride on the source-link row.

## Decision

- **A separate per-service table.** `IssueTrackers` (Identity migration 0038 SQLite / 0013 Postgres) holds
  `ServiceName` (primary key), `Provider` (only `Jira` today), `SiteUrl`, `ProjectId` and `IssueTypeId`
  (empty = Jira's default type). Only configured services have a row, as with `SourceLinks`. A separate
  table keeps a Jira-only service from needing a fake repo, and gives a later write token for issue
  creation a place that is not the read-only source-link token (ADR-0096).
- **Same API shape as source links.** `GET /api/issue-trackers` for any signed-in user (the dashboard
  builds the link), `PUT`/`DELETE /api/issue-trackers/{serviceName}` Admin-only and audited
  (`issue-tracker` update/delete). The body is validated: absolute http(s) site URL, digits-only
  project id and issue type id. Nothing secret is stored.
- **The URL is built in the dashboard.** `buildJiraIssueUrl` returns
  `<site>/secure/CreateIssueDetails!init.jspa?pid=&issuetype=&summary=&description=` for the first
  affected service with a tracker. The numeric `pid` is required by that URL, which is why the key
  cannot be used. The description is plain text, since that endpoint takes Jira wiki markup and Markdown
  would show literally; the Flare link is left bare for Jira to link. Jira Server/Data Center and Cloud
  share the path; a context path belongs in the site URL.
- **Configured from the occurrence dialog.** A ticket icon beside the source-link icon opens a popover
  (Admin or auth off), the same pattern as `SourceLinkPopover`.
- **GitHub/GitLab and Jira can both appear** for one group; they are separate menu items.

## Consequences

- An admin has to look up the numeric project id (the number in the project's settings URL, or the `id`
  from `/rest/api/2/project/<KEY>`). The popover says so; there is no lookup because Flare does not call
  Jira.
- `CreateIssueDetails!init.jspa` is the long-standing prefilled-create URL; if Atlassian retires it, the
  fix is in one function.
- Creating the issue from Flare and linking it in one step (GitHub, GitLab and Jira) is still open; it
  needs a stored write-scoped credential and an outbound call, and would extend `IssueTrackers`.
