# ADR-0135: Synthetic DNS MX, TXT and CNAME records

Status: Accepted

Date: 2026-10-06

## Context

ADR-0134 limited DNS probes to A and AAAA because .NET's resolver API returns only addresses, and left MX, TXT
and CNAME out rather than add a DNS client dependency.

## Decision

- `Method` on a `Dns` monitor now also accepts `MX`, `TXT` and `CNAME`. No schema change.
- These types use a small in-repo DNS client (`SyntheticDnsWire`): one query over UDP to each name server the
  host has configured (first to answer wins), retried over TCP when the reply is truncated. No new package.
- `ExpectedAnswer` for these types is text, matched case-insensitively as a substring of any answer. MX answers
  are rendered `<preference> <exchange>`, TXT strings are concatenated, CNAME is the target without the trailing
  dot. For A and AAAA it is still an IP address.
- NXDOMAIN or an empty answer is down ("no MX records returned"); other server error codes are down with the
  RCODE as the reason.

## Consequences

- A and AAAA still go through the system resolver (and its cache); MX, TXT and CNAME go straight to the
  configured servers, so the two can disagree briefly after a record change.
- DNSSEC is not validated, and DNS-over-TLS/HTTPS resolvers are not used.
