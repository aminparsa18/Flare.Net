# ADR-0195: Error issue links and ownership rules

Status: Accepted

Date: 2026-10-12

## Context

ADR-0121 gave `/errors` groups a status and a manual assignee. Two gaps remained for a team
using it daily: a group can't point at the tracker issue that covers it, and every group has to
be assigned by hand even though "payments errors are Alice's" is a stable fact about the system.
Jira is out of scope here; GitHub is the tracker most self-hosters already point Flare at
(ADR-0095 stores a GitHub repo per service for stack-trace links).

## Decision

- **A group carries one external issue link.** `error_issues.IssueUrl` (migration 0083, plus the
  cluster variant) holds an absolute http(s) URL, validated server-side (`ErrorIssueRequest`),
  set through the existing `PUT /api/errors/issues` (`null` keeps, `""` clears). It is a plain
  URL, so GitLab, Jira or anything else works as a link. A group with only a link is a stored
  row; the tombstone rule becomes Open, unassigned **and** unlinked.
- **"New GitHub issue" is a prefilled URL, not an API call.** The dashboard builds
  `<repo>/issues/new?title=&body=` from the first affected service whose ADR-0095 source link is
  a GitHub repo. No token is stored or used, the user files the issue under their own identity,
  and Flare needs no write access to the repo. The user pastes the resulting URL back with
  "Link issue". Creating and linking in one step needs a repo-write token and an outbound call,
  and was left out.
- **Ownership rules are an ordered list of globs.** `error_ownership_rules` (migration 0083) holds
  `Kind` (`Service` or `ExceptionType`), `Pattern` (case-insensitive, `*` = any text) and `Owner`
  (a username). The first matching rule owns a group; an explicit assignee always wins. The
  rule list is read and replaced as one document (`GET`/`PUT /api/errors/ownership-rules`, read
  by any user, write Member/Admin) because it is small and its order is the meaning; a save
  tombstones the previous rows and inserts the new ones with fresh ids, the same
  `ReplacingMergeTree` + `LatestVersionSql` shape as `error_issues`.
- **The owner is derived at read time, never stored on the group.** The dashboard matches rules
  against each group's `exceptionType` and `affectedServices`, which the groups response already
  carries, and shows "Owner x (rule)". Editing a rule therefore moves every group it covers, and
  an explicit "Assign to me" is the only thing written to `error_issues.Assignee`.

## Consequences

- Matching lives in the dashboard (`errors/ownership.ts`). Anything that needs the owner outside
  the page (alert messages, the CLI, MCP) would need the same glob logic server-side; none does
  today.
- Rules can't match stack-frame paths, so a real CODEOWNERS file can't be imported: the groups
  list carries no frames. Path rules would need a sample frame per group, either stored on the
  group or looked up from an occurrence.
- Each rules save writes tombstones for every previous rule. The list is capped at 500 rules and
  edited rarely, so the table stays small.
- The link is one URL per group. Several issues for one group, or a status synced from the
  tracker, are not modelled.
