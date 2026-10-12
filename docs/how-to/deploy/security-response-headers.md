# How to configure security response headers

Flare.Api and the dashboard send a baseline set of security headers. This page
shows what they are, how to change them and what to do when a reverse proxy
already sets them.

## What is sent

| Header | Flare.Api | Dashboard |
| --- | --- | --- |
| `X-Content-Type-Options` | `nosniff` | `nosniff` |
| `Referrer-Policy` | `no-referrer` | `same-origin` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | scripts from `self` plus a per-request nonce, connections to the dashboard and `PUBLIC_API_URL` origins, `frame-ancestors 'none'` |
| `Strict-Transport-Security` | HTTPS requests only, 180 days | HTTPS requests only, 180 days |

"HTTPS request" means the connection was HTTPS or the proxy sent
`X-Forwarded-Proto: https`. The dashboard needs that header from your proxy;
without it HSTS is not sent. Browsers ignore HSTS over plain HTTP anyway.

An endpoint that sets one of these headers itself keeps its own value.

## Change them

Flare.Api (`Security__Headers__*`):

| Variable | Default | Meaning |
| --- | --- | --- |
| `Security__Headers__Enabled` | `true` | `false` sends none of the headers. |
| `Security__Headers__HstsMaxAgeSeconds` | `15552000` | `0` disables HSTS. |
| `Security__Headers__HstsIncludeSubDomains` | `false` | Adds `includeSubDomains`. |
| `Security__Headers__FrameAncestors` | `'none'` | CSP `frame-ancestors` sources. |

Dashboard:

| Variable | Default | Meaning |
| --- | --- | --- |
| `FLARE_SECURITY_HEADERS` | on | `off` sends none of the headers. |
| `FLARE_CSP` | on | `off` drops only the CSP. |
| `FLARE_CSP_FRAME_ANCESTORS` | `'none'` | Who may embed the dashboard, e.g. `'self' https://wiki.example.com`. |
| `FLARE_CSP_CONNECT_SRC` | empty | Extra `connect-src` sources, space separated. |
| `FLARE_CSP_IMG_SRC` | empty | Extra `img-src` sources (images already allow `https:`). |
| `FLARE_HSTS_MAX_AGE` | `15552000` | `0` disables HSTS. |
| `FLARE_HSTS_INCLUDE_SUBDOMAINS` | `false` | Adds `includeSubDomains`. |

The dashboard policy is not applied by `npm run dev`, whose hot-reload client is
an inline script.

## Behind a reverse proxy

If nginx, Caddy or Traefik already adds these headers, you will get duplicates.
Either remove them at the proxy or turn Flare's off (`Security__Headers__Enabled=false`,
`FLARE_SECURITY_HEADERS=off`). Make sure the proxy passes `X-Forwarded-Proto`.

HSTS is remembered by browsers for the whole max-age. If you might serve the
hostname over plain HTTP again, set a short `FLARE_HSTS_MAX_AGE` first.

## Troubleshooting

- **The dashboard shows a blank page and the console reports a CSP violation.**
  A resource comes from an origin the policy does not list. Add it with
  `FLARE_CSP_CONNECT_SRC` or `FLARE_CSP_IMG_SRC`, or set `FLARE_CSP=off` while
  you investigate.
- **The dashboard cannot be embedded in another page.** Set
  `FLARE_CSP_FRAME_ANCESTORS` to the embedding origin.
