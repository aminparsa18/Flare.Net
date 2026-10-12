# ADR-0200: Baseline security response headers

Status: Accepted

Date: 2026-10-12

## Context

Neither Flare.Api nor the dashboard's Node server set `X-Content-Type-Options`, `Referrer-Policy`, a CSP
(including `frame-ancestors`) or HSTS, so an internet-exposed instance depended entirely on the operator's
proxy for clickjacking and MIME-sniffing protection.

## Decision

**Both apps send a baseline set by default, configurable and switchable off.**

- **Flare.Api** (`Security:Headers:*`, `SecurityHeaders` middleware): it serves no renderable documents, so
  the CSP is `default-src 'none'; frame-ancestors <FrameAncestors>`. Skipped for CSP in Development so the
  OpenAPI document stays viewable.
- **Dashboard** (`hooks.server.ts` + `$lib/server/security-headers.ts`, `FLARE_CSP*`): the CSP is built at
  request time, not with SvelteKit's build-time `kit.csp`, because `connect-src` depends on `PUBLIC_API_URL`
  which is read per request (the reason for adapter-node). Inline scripts (SvelteKit bootstrap, mode-watcher,
  `app.html`) get a per-request nonce added to every `<script` tag in `transformPageChunk`; `style-src` keeps
  `'unsafe-inline'` because of inline style attributes. Not applied under `vite dev`.
- **HSTS** is sent only when the request was HTTPS, judged by `X-Forwarded-Proto` (the dashboard cannot use
  `event.url.protocol`: adapter-node assumes https unless `PROTOCOL_HEADER` is set). The header is not
  gated on trusted proxies; a spoofed value only changes the spoofer's own response. Default 180 days, no
  `includeSubDomains`, no `preload`, since HSTS is sticky and sub-domain pinning is the operator's call.
- An endpoint that already set a header keeps it. Static files served by adapter-node's sirv before the
  SvelteKit handler carry no headers.

## Consequences

Operators with a proxy that also sets these headers get duplicates until they disable one side; the how-to
documents it. A third-party origin the dashboard newly needs (a custom image host over http, an
analytics script) is blocked until added through `FLARE_CSP_*`.
