# ADR-0095: Exception stack frames link to source code

Status: accepted

## Context

The Exceptions page shows a stack trace, but nothing connects a frame to the code that ran.
Telemetry already carries what's needed: .NET traces include `in /path/File.cs:line N`, and
SourceLink-enabled builds stamp the commit into `service.version` (`1.2.3+<sha>`) or a
`vcs.revision` resource attribute. What's missing is where each service's repository lives.

## Decision

- **Config is per service, in Identity SQLite** (`SourceLinks`, migration 0023): provider
  (GitHub, GitLab, Azure DevOps), repo URL, fallback ref, and a build-path prefix to strip. Only
  configured services have a row, as with Apdex thresholds (ADR-0032). `GET /api/source-links` is
  open to any signed-in user; `PUT`/`DELETE /api/source-links/{serviceName}` are Admin-only and
  audited, since they change where every user's links point.
- **The commit comes with each occurrence.** `ExceptionOccurrence.Revision` (a trailing
  MemoryPack member) is `vcs.ref.head.revision`, else `vcs.revision`, else `service.version`,
  read from the span's resource attributes in the existing occurrences query. The dashboard
  accepts a bare hex SHA or the part after `+`; anything else falls back to the service's
  configured ref, or no link.
- **URLs are built in the dashboard** (`$lib/errors/source-links.ts`), not the API, because the
  stack trace is rendered client-side by `StackTraceViewer`, which takes an optional `linkFor`
  callback. A frame links only when its path can be made repo-relative: after stripping the
  configured prefix (or `/_/`, which deterministic builds write), or when it is already a relative
  path with a directory. Otherwise it stays plain text, since a wrong link is worse than none.

## Consequences

- No source is fetched, so private repositories need no token in Flare: the link opens in the
  user's browser, which authenticates to the host itself.
- Showing the failing lines inline would need Flare to fetch from the host with a credential. That
  and an "explain this exception" LLM action stay on the roadmap.
- The occurrences query reads two more map lookups per row; it is capped at 50 rows.
