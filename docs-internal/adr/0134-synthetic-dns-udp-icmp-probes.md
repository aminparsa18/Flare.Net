# ADR-0134: Synthetic DNS, UDP and ICMP probes

Status: Accepted

Date: 2026-10-06

## Context

ADR-0128 shipped HTTP, TCP and TLS-expiry probes and listed DNS, UDP and ICMP as missing.

## Decision

- Three new `Kind` values: `Dns`, `Udp`, `Icmp`. `Kind` is a string column, so they need no migration of their
  own; one additive column, `ExpectedAnswer` (migration 0060), holds what the answer must contain. Empty means
  any answer.
- **Dns** resolves `Target` (a bare host name or IP) through the system resolver, for `A` (default) or `AAAA`,
  held in the existing `Method` column. Up when at least one address comes back and, if `ExpectedAnswer` is an
  IP, the answer includes it. Only A and AAAA: .NET has no built-in resolver for MX, TXT or CNAME, and adding a
  DNS client library for them is left out.
- **Udp** sends `RequestBody` as one datagram to `host:port` on a connected socket and waits for one reply. Up
  when a reply arrives within the timeout and, if `ExpectedAnswer` is set, contains that text. A port-unreachable
  from the host surfaces as a socket error and is down. Because UDP has no handshake, silence is
  indistinguishable from loss, so this suits only services that always answer (DNS, NTP, a custom echo).
  The payload is capped at 1400 characters to stay within one datagram.
- **Icmp** sends one echo with the monitor timeout via `System.Net.NetworkInformation.Ping`. Up on `Success`.
- All three write the usual `synthetic.up` and `synthetic.duration`; no status code or certificate metric.

## Consequences

- ICMP needs the worker host to be allowed to send echo requests. In a container without `CAP_NET_RAW` and
  with `net.ipv4.ping_group_range` unset, every ICMP probe fails; the error is recorded as the failure reason.
- DNS results reflect the worker's resolver and its cache, not an authoritative lookup.
