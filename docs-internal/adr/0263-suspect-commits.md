# ADR-0263: Suspect commits for new errors

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0182](0182-release-tracking.md) says which exception groups a release introduced, releases carry a commit, and
[ADR-0095](0095-exception-source-links.md) / [ADR-0096](0096-inline-exception-source.md) give each service a repository
config with an optional token. Nothing connected them: after "this release added `NullReferenceException` in `Cart`", a
person still opened the repository and read the diff to find who touched `Cart.cs`.

## Decision

- **The range is two releases.** For a new error on the Releases page the range is the release's commit and the commit of
  the next older release that has one. A release's commit is its marked `commit`, else the SHA after `+` in a
  SourceLink-style `service.version`. Without both there is no range and the UI says so; Flare does not guess from
  branches.
- **A suspect is a commit in the range that changed a file in the failing stack.** The dashboard turns the group's most
  recent occurrence in that version into repo-relative paths (the ADR-0095 prefix and `/_/` rules, throw site first) and
  posts them with the two refs to `POST /api/source-links/suspects`. The server asks the host for the range's commits,
  then for the files of each commit, and ranks by matched frames, weighting the throw site most, newest first on a
  tie. Five at most are returned, with the author, the first line of the message and the matched files. No blame, no
  line-level analysis: file overlap is cheap, explainable and available on all three hosts.
- **Host calls are bounded and mostly skipped.** GitHub and GitLab return the range's changed files with the compare;
  when that list is complete (fewer than 100 files) and contains none of the paths, no per-commit call is made. Otherwise
  the newest 40 commits are inspected, four at a time. Azure DevOps has no range file list, so it always inspects.
  The response says `truncated` when commits were left out. Results are cached 10 minutes: the refs are commits.
- **Same trust posture as the inline snippet.** The guarded no-redirect `source-fetch` client, the service's stored
  token, a 4 MB body cap, a 25 s deadline, `ServiceScope` on the service, any signed-in user. Refs and paths go through
  the snippet's `IsSafeRef`/`IsSafePath`.
- **Path matching is case-insensitive and includes rename sources**, since build paths come from Windows agents and a
  renamed file's old name may be what the stack still shows.
- **Surface:** the Releases page's new-errors list (a **Suspect commits** button per error). Nothing is stored.

## Consequences

- A GitLab token needs `read_api`, not just `read_repository`, because compare and commit-diff are API endpoints.
- A commit that touched a stack file is a candidate. A bug introduced by a dependency bump, or in a file the stack does
  not show (an inlined or generic frame), yields no suspect, and the UI says no commit changed a file in the stack.
- A range longer than 40 commits is searched newest first, so an old commit can be missed. The UI says so.
- Each lookup is two to forty-one host calls on a cold cache; a host's rate limit applies to the token.
- The error detail on the Errors page, alert notifications, the CLI and MCP do not show suspects yet. Each needs the
  introducing release resolved for the group, which the Releases page already has.
