# ADR-0259: Custom error grouping rules and manual merges

Status: Accepted

Date: 2026-10-20

## Context

ADR-0258 made `/errors` group on a normalized message, which collapses ids, numbers, GUIDs, timestamps and quoted values.
That cannot help where the variable part is a word (a user name, a host name, a tenant), and it cannot be undone where it is
wrong (`HTTP 404` and `HTTP 500` are one group). People also want to fold two groups together by hand, as Sentry allows.

## Decision

This supersedes the "no switch to turn it off" line of ADR-0258: the built-in normalization can now be switched off.

- **Three layers, applied in this order to every exception event's message:** the built-in normalization (switchable), the
  custom `Replace` rules in list order, then the `Merge` rules. All of it compiles to one ClickHouse expression
  (`ErrorGrouping.Sql`), embedded by every query that keys on a group, the same as ADR-0258.
- **`Replace`** is an RE2 regular expression and a replacement (`\1` for a capture group), applied with `replaceRegexpAll` to
  every message. It is not scoped to an exception type; put the type-specific part in the pattern.
- **`Merge`** names an exception type, the group message to fold away and the group message it joins. All merges are one
  `transform` lookup over `type + char(31) + message`, so merging is linear in SQL size, and it is scoped to one exception type.
  Chains (A into B, B into C) resolve to A into C when the configuration is compiled, a repeated source keeps its first rule,
  and a cycle is dropped.
- **Idempotence is required.** A request may name a group by its own message or by a raw one, and the match normalizes both
  sides, so grouping must give the same result on its own output. Saving rejects a pattern that matches an empty string and a
  replacement that matches its own pattern, and ClickHouse is asked to compile every pattern, so a bad expression comes back as
  a 400 with its message rather than as a failing `/errors` page later.
- **Storage.** Rules are `error_grouping_rules` (migration 0088), replaced as a whole list like ownership rules. The built-in
  normalization switch is a row of `runtime_settings` (`errors.normalize_messages`) rather than a column, so there is one
  document in the API (`GET`/`PUT /api/errors/grouping-rules`, read by anyone signed in, written by Member/Admin) and no new
  settings plumbing.
- **Triage state follows the groups.** After a save, `ErrorIssueQueryService.RekeyAsync` runs every stored issue's
  `(type, message)` through the new grouping and moves it to the resulting group's fingerprint (`ErrorIssueRekey`, a pure
  planner). Issues that land on the same group collapse to the most recently updated one, the same rule migration 0087 used.
- **Manual merge is a `Merge` rule**, created from a group's row menu and listed in the grouping rules dialog, where deleting it
  splits the groups again. There is no second mechanism.
- **Short cache.** `ErrorGroupingQueryService.GetAsync` caches the compiled configuration for 10 seconds, because every group
  query and every alert evaluation needs it. The API process drops its copy on save; the alert worker sees a change within the TTL.

## Consequences

- Removing a rule, merging back out, or turning the normalization off separates groups, but does not give a merged-away
  group back the state it had: re-keying only moves state forward. The groups start Open.
- A merge names a group by its message at the time. If a later `Replace` rule or a change to the built-in rules renames that
  group, the merge no longer matches and sits unused until someone edits it.
- Merging across exception types is not supported: the type in the group key stays as recorded, and exception-count alert
  rules filter on the recorded type.
- `Replace` patterns run on every exception event inside the `ARRAY JOIN`, so a pattern that is expensive on long messages
  slows the page and alert evaluation. There is no per-rule timing.
- The expression grows with the rule count (at most 200 rules); a long list makes the SQL text long, though the cost is in the
  regular expressions, not in the text.
