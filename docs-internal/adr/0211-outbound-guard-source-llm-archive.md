# ADR-0211: Extend the outbound guard to source fetch, the LLM endpoint and the S3 archive

Status: Accepted

Date: 2026-10-13

## Context

[ADR-0201](0201-outbound-ssrf-guard.md) guarded the alert notifiers and OTLP forwarding. The threat model found three more
places where an admin-typed URL becomes a server-side request: the source-repo fetch (which sends a stored access token),
the OpenAI-compatible LLM endpoint, and the S3 archive endpoint.

## Decision

- **Source-repo fetch and the LLM client** use `AddOutboundGuard(allowAutoRedirect: false)`. The guard's handler gained the
  `allowAutoRedirect` parameter so these clients keep their existing "never follow a redirect" behaviour (the repo token must not
  follow one off-host). The same `Outbound__Allow__N` and `Outbound__BlockPrivateNetworks` settings apply, on Flare.Api.
- **The S3 archive** cannot use the HTTP-client guard: ClickHouse makes the request (`INSERT INTO FUNCTION s3`), not Flare.
  `ArchiveWorker` instead runs `OutboundAddressPolicy.CheckUrlAsync` on the endpoint each poll and idles with a warning naming the
  setting when it is refused. This is best-effort, because ClickHouse resolves the name itself and could get a different answer;
  ClickHouse's `remote_url_allow_hosts` remains the hard limit and is the place to enforce it for a hostile network.
- Synthetic probes and Prometheus scrape targets stay exempt, as in ADR-0201.

## Consequences

A local Ollama (`localhost:11434`), a self-hosted GitLab on a private address and a MinIO on `10.x` stop working on upgrade
until allowed. The LLM and source errors surface the "Blocked outbound request" message that names the setting; the archive
logs it and shows as idle. The three how-to pages for these features say so.
