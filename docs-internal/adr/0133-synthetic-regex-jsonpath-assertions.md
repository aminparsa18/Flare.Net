# ADR-0133: Synthetic regex and JSON-path assertions

Status: Accepted

Date: 2026-10-06

## Context

ADR-0129 added case-sensitive substring assertions and left regex and JSON-path assertions as a follow-up.
Substring checks cannot say "`status` is `ok` in a JSON reply" or "the version matches `\d+\.\d+`".

## Decision

- Three new columns on `synthetic_monitors` (migration 0059, additive): `BodyMatchesRegex`, `JsonPath`,
  `JsonPathEquals`. Empty means no assertion, so existing monitors behave as before.
- **Regex** runs with `RegexOptions.NonBacktracking` and a one-second match timeout. ADR-0129 rejected regex
  over catastrophic backtracking inside the alert worker; the non-backtracking engine matches in linear time,
  which removes that risk. The price is that lookarounds and backreferences are unsupported, and the API
  rejects such patterns with a 400 when the monitor is saved.
- **JSON path** is a deliberately small subset: an optional leading `$`, then `.name`, `['name']` and `[index]`
  steps. No wildcards, filters or recursive descent. The path must exist; if `JsonPathEquals` is set, the value
  must equal it (strings compared unquoted, numbers/booleans/null/objects as their JSON text). A body that is
  not JSON fails the assertion.
- The assertions combine with ADR-0129's: every configured one must pass. Any body assertion makes the probe
  read the body (first 1 MiB), as before.
- Each limit is 1000 characters, like the substring assertions. `JsonPathEquals` without `JsonPath` is a 400.

## Consequences

- The migration is numbered 0059 in both `db/clickhouse` and `db/clickhouse-cluster`.
- Comparisons are exact: `1.0` does not equal `1`. Numeric tolerance and richer JSONPath are follow-ups.
