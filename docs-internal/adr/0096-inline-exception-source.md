# ADR-0096: Inline source for exception frames

Status: accepted

## Context

ADR-0095 links stack frames to the repo. Showing the failing lines in the dialog needs the file
content, which only the repo host has, often behind a credential.

## Decision

- **The API fetches, not the browser.** Host APIs need tokens and don't allow browser CORS
  with them, and a token must not reach the dashboard. `POST /api/source-links/snippet` (any
  signed-in user) takes service, ref, repo-relative path and line, and returns ±6 lines.
- **Optional per-service token, write-only.** `SourceLinks.AccessToken` (migration 0024), stored in
  plaintext in Identity SQLite like `LdapSettings.BindPassword`. The API never returns it, only
  `hasAccessToken`; a null on save keeps it and an empty string clears it. Use a read-only token.
- **Hosts:** GitHub (`contents` API; GitHub Enterprise via `/api/v3`), GitLab (`repository/files/raw`),
  Azure DevOps (`items`, commit or branch version type). Request shapes are a pure builder with tests.
- **Bounded fetch.** The service must be configured (so the request can only target an
  admin-set repo URL), path and ref are validated (no traversal, no URL syntax), redirects are not
  followed so the token can't leave the host, 10 s timeout, 2 MB body cap, and a private
  size-bounded cache (200k lines, 10 min).
- **On demand, throw site only.** The dashboard shows the first linkable frame behind a
  "Show source" button, so opening the dialog costs no host requests.

## Consequences

- Source text passes through Flare's process memory and cache, and a stored token grants read
  access to the repo; it's as sensitive as the LDAP bind password.
- Redaction isn't applied: this is the user's own code shown to signed-in Flare users. If the
  "explain this exception" LLM action is built, redaction belongs there.
- The 10-minute cache can show a moved branch's old lines; commit refs never go stale.
