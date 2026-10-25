# ADR-0273: Prefilled new-issue link for GitLab

Status: Accepted

Date: 2026-10-25

## Context

ADR-0195 added "New GitHub issue" to the `/errors` row menu as a prefilled URL, with no token and no
outbound call. The roadmap listed GitLab, Jira and in-Flare issue creation as follow-ups. GitLab
needs nothing the GitHub link doesn't already have: a service's ADR-0095 source link already
records the provider and repo URL.

## Decision

- **GitLab uses the same mechanism.** `buildNewIssueLink` (replacing `buildGitHubIssueUrl`) picks the
  first affected service whose source link is GitHub or GitLab and returns the provider with the URL.
  GitLab's form is `<repo>/-/issues/new?issue[title]=&issue[description]=` (brackets percent-encoded);
  the title and the Markdown description are the same as for GitHub, and the same 4000-character body
  cap keeps the URL under the host limit.
- **The menu label follows the provider** ("New GitHub issue" / "New GitLab issue"). No API, setting or
  migration changed.

## Consequences

- If one group spans a GitHub service and a GitLab service, the first affected service decides, as it
  did before.
- Self-managed GitLab works because the URL is built from the configured repo URL, and so does a
  project in a subgroup.
- Jira is not covered: a prefilled Jira URL needs a site URL and a numeric project id, which no existing
  setting holds, and creating issues from Flare needs a write-scoped credential (ADR-0096's token is
  recommended read-only). Both stay on the roadmap.
