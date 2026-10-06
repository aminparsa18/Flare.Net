# ADR-0131: Masking synthetic monitor header values

Status: Accepted

Date: 2026-10-06

## Context

ADR-0129 stores request headers as text and returns them verbatim to any Member or Admin, so a bearer token in
an `Authorization` header was readable by everyone who could open the page, and landed in audit entries.

## Decision

- The API replaces every header value with `********` in every response (create, list, get, update) and in the
  audit before/after of an update. Names stay visible.
- On update, a submitted line `Name: ********` keeps the stored value of the header with that name (matched
  case-insensitively, in order). A line with a new value replaces it, and a header left out is removed.
  The dashboard form and `flare synthetic-monitors update` need no change: they read masked text and send it back.
- All values are masked, not just well-known names such as `Authorization`: a name list cannot cover custom
  `X-*-Key` headers, and nothing in a probe header needs to be read back.
- The probe worker reads the stored text directly, so probes are unaffected. Values are still stored in clear
  text in ClickHouse; this limits exposure through the API, not at rest.

## Consequences

- To see or change a value, retype it. There is no reveal action.
- A header whose real value is literally `********` cannot be set.
