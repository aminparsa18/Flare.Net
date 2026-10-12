# ADR-0197: Error owner in alert messages, and a "My errors" filter

Status: Accepted

Date: 2026-10-12

## Context

ADR-0195 and ADR-0196 derive a group's owner in the dashboard only, and both name the server-side
port as the prerequisite for using the owner anywhere else. Exception-count alerts said how many
times an error happened but not whose it is, and `/errors` could not be narrowed to the signed-in
user's groups.

## Decision

- **The matching is ported to C#, not moved.** `Flare.Api/Errors/ErrorOwnership.cs` reimplements
  `ownership.ts` (`globMatches`, `codeownersMatches`, `pathSuffixes`, `ownerFromRules`) including
  the try-every-trailing-run-of-segments behavior for `Path` rules. Each file carries a comment
  pointing at the other, and `ErrorOwnershipTests` holds the cases the TypeScript file documents.
  The dashboard keeps its copy because it needs the owner for every row, filtering and display, on
  data it already holds; the alert path needs it for three groups at fire time. Two copies can
  drift, so the C# result is the one to expose through the groups API if a third consumer appears,
  which would let the dashboard drop its copy.
- **Owners are resolved where the alert reads its top groups.** `AlertEvidenceQueryService.GetExceptionGroupsAsync`
  already runs a bounded top-N query under the alert-evaluation caps. It now also selects every
  affected service and a sample stack trace (the expression the Errors page uses), so no second
  lookup is needed, and resolves each group to the explicit `error_issues.Assignee`, else the first
  matching rule from `IErrorOwnershipQueryService`. A failure there leaves the groups unowned, as
  elsewhere in the evidence code.
- **Three groups, one string.** An exception-count fire looks at its top three groups
  (`AlertOwners.GroupCount`) and joins the distinct owners in rank order. That string is the new
  `{{owner}}` template variable (empty for other kinds, tests, no-data and resolved sends) and an
  `Owner:` line in the built-in message; it is threaded to the notifiers like `logSamples` and
  `ackUrl` (ADR-0052). The AI incident summary (ADR-0104) gets the owner per exception line from the
  same query. Escalation re-sends do not carry it.
- **"My errors" is a client filter.** `ErrorsExplorerState.mineOnly` keeps groups whose `ownerOf`
  user equals the signed-in username (case-insensitive). It is remembered in `localStorage`
  (`flare.errors.mineOnly`), and a `?mine=1`/`?mine=0` query parameter overrides the stored value
  so a link behaves the same for everyone. The control and the filter are off when auth is off,
  since there is no current user. An alert deep link clears it so the linked group is never hidden.
  The existing status filter is neither stored nor in the `?state=` link (that link mirrors the
  server's alert URLs), so `mine` is a separate query parameter rather than a new field in it.

## Consequences

- Every exception-count fire costs one extra read of `error_issues` and `error_ownership_rules`
  plus a wider select on a query that already ran for the summary. Both tables are small; the top-N
  query is capped by `QuerySafety.AlertEvaluation`.
- The owner in a notification is a sample: it comes from one stack trace per group, like the
  dashboard's, and only for the top three groups of the window.
- The CLI has no errors command, and the MCP `list_exceptions` tool reads `/api/errors/groups`
  without owners. Adding them there would need a third copy of the matcher in `Flare.Mcp` (which
  cannot reference `Flare.Api`); it waits for the groups API to return the owner.
- Changing the semantics of ownership matching now means changing two files.
