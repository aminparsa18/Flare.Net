# ADR-0129: Synthetic HTTP request options and body assertions

Status: Accepted

Date: 2026-10-06

## Context

ADR-0128 shipped HTTP monitors that send a bare request and judge only the status code. Real endpoints
often need an `Authorization` header or a POST body, and a 200 with an error page in it is still down.

## Decision

- Four new columns on `synthetic_monitors` (migration 0057, additive): `RequestHeaders`, `RequestBody`,
  `BodyContains`, `BodyNotContains`. Empty means none, so existing monitors behave as before.
- **Headers** are stored as text, one `Name: value` per line (at most 20 lines, 4000 characters). Names must
  be valid HTTP tokens and values cannot contain line breaks. A `Content-Type` header applies to the body.
- **Body** is sent for `POST` only; setting one with another method is a validation error. At most 64 KB.
- **Assertions** are case-sensitive substring checks: the body must contain `BodyContains` and must not
  contain `BodyNotContains`. Only the first 1 MiB of the response is read. A monitor with no assertion still
  stops at the response headers, so its duration stays time to first response; with an assertion the
  duration includes reading the body.
- A failed assertion records `synthetic.up = 0` with the status code still reported.
- Substring, not regex: no catastrophic-backtracking risk inside the alert worker, and enough for
  "contains `ok`".

## Consequences

- Header values are stored and returned in clear text to any Member or Admin. A token placed in a header is visible to everyone who can open
  the page. Masking secret headers is a follow-up.
- Bodies are read into memory up to the cap, per probe, within `Synthetic:MaxConcurrency`.
