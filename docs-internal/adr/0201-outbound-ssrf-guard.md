# ADR-0201: SSRF guard on user-supplied outbound URLs

Status: Accepted

Date: 2026-10-12

## Context

Alert channels (webhook, Teams, Discord, incident.io, Jira) and OTLP forwarding targets send HTTP requests to a
URL an admin typed in. Nothing stopped one pointing at `http://169.254.169.254/` (cloud metadata),
`http://localhost:8123/` (ClickHouse) or another internal service, and the response status and a body excerpt come
back in the channel's send-test result.

## Decision

**A connect-time check in the HTTP client's `SocketsHttpHandler`, on by default.**

- `AddOutboundGuard()` (`Flare.ServiceDefaults/Outbound`) installs a `ConnectCallback` that resolves the host,
  rejects the request if **any** resolved address is non-public, and connects to the address it just checked.
  Checking at connect time, not when the URL is saved, is what covers redirects, DNS names that resolve to a
  private address, and rebinding between check and use.
- Blocked: loopback, `0.0.0.0/8`, RFC 1918, CGNAT `100.64/10`, link-local (`169.254/16`, `fe80::/10`), IPv6 ULA
  and site-local, multicast and reserved, benchmarking `198.18/15`, and IPv4-mapped / NAT64 forms of those.
- `Outbound__BlockPrivateNetworks=false` turns it off. `Outbound__Allow__N` exempts an IP, a CIDR, or a host name
  (exact or `*.suffix`). A host-name entry matches the name in the URL, so it should be a name the operator
  controls; the connection still goes to the address checked.
- Applied to the clients behind user-typed URLs: the five alert notifiers above (in Flare.Api and
  Flare.AlertWorker) and `OtlpForwarding` in Flare.Ingest. Status-page incident notifications go out through the
  same channels. Not applied to fixed vendor endpoints (PagerDuty, Telegram, Slack `response_url`), or to
  synthetic monitors and Prometheus scrape targets, which exist to reach internal services.

## Consequences

An existing webhook to an in-network receiver (a Mattermost on `10.x`, a local forwarder) stops working on
upgrade until it is allowed. Three things keep that from being silent: the send-test and the alert event show the
"Blocked outbound request" message naming the setting; saving a channel whose destination would be refused still
succeeds but logs a warning and returns an `X-Flare-Warning` header; and a forwarding target's failures already log
"not accepting {Url}: {Error}" with the same message.

With an `HTTP(S)_PROXY` configured, the connect callback sees only the proxy, so a delegating handler also checks the
target host up front whenever the proxy is not bypassed. That is best-effort: the proxy resolves the name itself, so
DNS rebinding on that path is only stopped by the proxy's own egress rules.
