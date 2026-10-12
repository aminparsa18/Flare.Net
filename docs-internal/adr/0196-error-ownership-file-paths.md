# ADR-0196: File-path ownership rules and CODEOWNERS import

Status: Accepted

Date: 2026-10-12

## Context

ADR-0195 added ownership rules over service names and exception types. The ownership most teams
have already written down is a CODEOWNERS file, which maps source paths to owners, and the groups
list carried no stack-frame information to match it against.

## Decision

- **The groups query returns a sample of file paths.** `POST /api/errors/groups` adds
  `anyIf(exception.stacktrace, <> '')` (first 16 000 characters) as a column, and
  `StackFramePaths.Extract` turns it into `ExceptionGroup.SamplePaths`: the distinct file paths
  in frame order. The stack trace itself isn't sent, so the payload grows by a few paths per
  group. Recognized formats are .NET (`in path:line N`), Node (`path:line:col`), Python
  (`File "path", line N`) and Java/Kotlin (file name only). The wire model gains a trailing
  member, which MemoryPack accepts from older readers only if they are updated together (the
  dashboard ships with the API).
- **A third rule kind, `Path`**, uses CODEOWNERS syntax: a slash inside a pattern anchors it at the
  repo root, otherwise it matches at any depth; a trailing `/` is a directory; `*`, `**`, `?`; a
  pattern naming a directory owns everything beneath it. It matches when any sample path does.
  Rules keep ADR-0195's first-match-wins order.
- **Checkout prefixes are not configured.** Build-machine paths (`/home/ci/build/src/pay/X.cs`)
  are not repo-relative, so a pattern is tried against every trailing run of segments of the path
  rather than against one derived relative path. This needs no per-service setting and works for
  paths the ADR-0095 prefix couldn't resolve, at the price of a root-anchored pattern also
  matching a deeper directory with the same tail (`src/pay/` would match `vendor/src/pay/`).
- **Import converts, it does not sync.** The dialog parses pasted CODEOWNERS text into `Path`
  rules, appended to the draft: first owner per line, leading `@` dropped, order reversed because
  CODEOWNERS is last-match-wins. Sections, comments and ownerless lines are skipped. Nothing is
  read from a repository, and owners such as `org/team` or an email only help if a Flare user is
  named the same, so the editor lets the user fix them before saving.

## Consequences

- Only the one sample occurrence per group is inspected, so a group whose sample lacks file
  information (no symbols) can't match path rules even if other occurrences have it.
- `anyIf` picks an arbitrary occurrence, so the sample may differ between refreshes of a group
  whose occurrences have different stacks.
- Matching stays in the dashboard, as in ADR-0195; moving it server-side is the prerequisite for
  using the owner in alerts.
