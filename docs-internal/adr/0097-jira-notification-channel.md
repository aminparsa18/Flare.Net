# ADR-0097: Jira notification channel

Status: accepted

## Context

Teams that track incidents in Jira had to put a webhook-to-Jira automation between Flare and
their project. An alert should open an issue, keep it current while the rule keeps firing, and
close it on recovery (ADR-0064).

## Decision

Add `Jira` to `NotificationChannelType`, appended after `Discord`, with `JiraAlertNotifier` dispatched by `CompositeAlertNotifier`. Jira Cloud only, REST API v3,
HTTP Basic with the Atlassian account email and an API token.

- **Five new channel fields**: `JiraBaseUrl`, `JiraEmail`, `JiraApiToken`, `JiraProjectKey`,
  `JiraIssueType` (empty means `Task`). They are appended to `NotificationChannel` and
  `NotificationChannelRequest` (MemoryPack versioning) and stored by migration 0044 as
  `String DEFAULT ''` columns. `ValidateDestination` requires the first four, an absolute
  http(s) base URL, and rejects Jira fields on other types and other fields on Jira.
- **Stateless issue tracking.** No issue key is stored. Each issue gets the label
  `flare-alert-<ruleId>`; the notifier looks the rule's open issue up with JQL
  (`project = X AND labels = "..." AND statusCategory != Done`) via `POST /rest/api/3/search/jql`.
  - Fire, no open issue: create one (summary = first line, ADF description, labels `flare` and the rule label).
  - Fire, open issue: add a comment (cooldown still limits how often this happens).
  - Recovery: comment, then run the first available transition whose target status is in the
    `done` category; no open issue is a successful no-op. A project with no such transition
    reports a failure in the history entry.
  - Test send: always creates a new issue under a one-off label, so it never touches a real one.
- A recovery of a ticket someone closed by hand, and a re-fire after it was closed, both fall
  out of the `statusCategory != Done` filter: the next fire opens a new issue.
- Custom templates (ADR-0052) render as plain-text ADF paragraphs, one per line.
- The token is stored and returned like the Telegram bot token (the existing channels API does
  not redact secrets); the dashboard form masks the input.

## Consequences

JSM Ops alerts and Jira Data Center (different auth and API paths) are not covered; JSM Ops stays
on the roadmap. The stateless lookup costs one search request per notification and depends on the
account being able to search the project. Not verified against a live Jira Cloud site; request
sequences are unit-tested against a scripted handler only.
