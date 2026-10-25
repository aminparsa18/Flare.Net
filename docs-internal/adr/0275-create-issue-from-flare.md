# ADR-0275: Create an issue from Flare (GitHub, GitLab, Jira)

Status: Accepted

Date: 2026-10-25

## Context

ADR-0195, ADR-0273 and ADR-0274 give an `/errors` group a "New GitHub / GitLab / Jira issue" menu item that opens
the host's create page prefilled. Nothing is stored and Flare calls nobody: the user files the issue as
themselves and pastes its URL back into **Link issue…**. Doing both in one step needs a credential that can create
issues and an outbound call from the API.

The only token Flare stores for a repo is ADR-0096's `SourceLinks.AccessToken`, which is recommended read-only
(it fetches source). Reusing it for writes would make a token an admin scoped to "read code" quietly able to open
issues, and would push admins to widen a token that is also used on every source preview.

## Decision

- **Separate, write-only tokens, next to the config they belong to** (Identity migration 0039 SQLite / 0014 Postgres,
  `ALTER TABLE ... ADD COLUMN`):
  - GitHub and GitLab key off the service's source-link row, as the prefilled link does, so `SourceLinks` gets
    `IssueToken`. It is independent of `AccessToken`; neither is a fallback for the other. Azure DevOps has no
    create path.
  - Jira keys off `IssueTrackers` (ADR-0274), which gets `ApiToken` and `AccountEmail`. With an email it is Jira
    Cloud (REST v3, Basic auth, Atlassian Document Format body); without one, Server/Data Center (REST v2, Bearer
    personal access token, wiki-text body). The Jira request needs a numeric issue type id (the prefilled link can
    leave it to Jira's default; the REST API cannot), so creation is refused until one is set.
  - Tokens are encrypted at rest through the ADR-0212 store wrappers (`ProtectedSourceLinkStore` handles both
    source-link tokens, the new `ProtectedIssueTrackerStore` the Jira one, both with the startup backfill) and are
    write-only on the API: `null` keeps, `""` clears, only `hasIssueToken` / `hasApiToken` is returned.
- **`POST /api/errors/issues/create`** (Member/Admin, the same gate as `PUT /api/errors/issues`; audited as
  `error-issue` / `create-external`). The body names the target (`GitHub`, `GitLab`, `Jira`), the service, the group and
  a title and body. The dashboard supplies the title/body (the same text its prefilled links carry, Markdown for
  GitHub/GitLab, plain text for Jira), capped at 256 and 8000 characters. The host, path and credentials come only
  from stored config; a project-scoped user may only name services they can see.
- **The API makes the call, with ADR-0096's bounds:** a named `HttpClient` behind the outbound guard (ADR-0201, so
  private addresses need the usual `Outbound:Allow`), no redirects (the token must not follow one off-host), a 10 s
  timeout, a 256 KB response cap. The standard resilience retry is switched off for the send
  (`HttpRetryScope.SingleShot`): a retried create after a timeout could open a second issue.
- **The result is trusted only if it points back at the configured host.** The new issue's URL is read from the
  response (`html_url`, `web_url`, or `<site>/browse/<key>` for Jira) and discarded unless it is http(s) on the
  configured authority.
- **One step, no duplicates.** The same request stores the URL on the group (`error_issues.IssueUrl`) through the
  existing upsert. A group that already has a link is refused with 409 before any call; a create already in flight for
  the group is refused with 409 (an in-process set keyed by the group fingerprint, released in `finally`). The
  dashboard hides the create entries once a group has a link and disables them while a request is pending. If the host
  accepted the issue but storing the link failed, the error names the new issue's URL so it can be linked by hand.
- **Menu.** "Create GitHub / GitLab / Jira issue" appears next to the "New … issue" link only when the matching write
  token is stored (and, for Jira, an issue type id). The prefilled links stay: they need no token and file the issue as
  the signed-in person rather than as the token's owner. Admins enter the tokens in the existing source-link and
  Jira popovers.

## Consequences

- Issues created this way are authored by the token's account (a bot, ideally), not by the Flare user who clicked.
  The audit log records who clicked.
- The duplicate guard is per API process. Two API replicas receiving a double click at the same instant could each
  create an issue; a shared lock (Redis) would close that and is not built.
- GitLab has no scope narrower than `api` that can create issues, so a GitLab token for this is broader than the
  read-only source token. The docs say to use a project access token. GitHub needs a fine-grained token with
  Issues: read and write on the repository.
- A Member can cause an issue to be created with a token only an Admin can see or set; that is the same trust as a
  Member editing alert rules that call stored webhook URLs.
- The three request shapes follow each host's documented API and were exercised end to end against a local stub that
  mimics the endpoints (auth headers, bodies, redirects, failures), not against github.com, a GitLab or a Jira site.
  Jira Server/Data Center differs from Cloud in authentication and API version; both are built, neither was tried on a
  real site.
