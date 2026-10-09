# ADR-0165: Status page custom domain and branding

Status: Accepted

Date: 2026-10-09

## Context

A status page lives at `/status/{slug}` on the dashboard's host (ADR-0158). Teams want it at `status.acme.com`,
with their logo and colors, and a way for visitors to reach support.

## Decision

- **Four optional fields on the page** (migration 0078): `Domain`, `LogoUrl`, `AccentColor`, `SupportUrl`. On
  create/update a null value leaves the stored one as it is and an empty string clears it, like
  `subscriberChannelIds`, so the enable toggle and older clients never wipe branding.
- **Validation.** `Domain` is a lower-cased DNS name with at least two labels (no scheme, port, path or IP) and
  unique across pages (409). `AccentColor` is `#rrggbb`. `LogoUrl` must be https and `SupportUrl` https or
  `mailto:`; neither may carry credentials, and `javascript:` and `data:` are rejected.
- **Routing by Host happens in the dashboard**, not the API. Flare does not terminate TLS or manage DNS: the
  operator points DNS and a certificate (reverse proxy, load balancer) at the dashboard. Its server hook asks
  `GET /api/public/status/domain/{host}` which page, if any, is enabled on the request's host. The API answers
  from one cached map of all domains, and the dashboard caches each answer for 30 seconds (at most 500 hosts,
  because hosts come from the request). `API_INTERNAL_URL` overrides `PUBLIC_API_URL` for that server-side call.
- **A status domain serves only its page.** `/` redirects to `/status/{slug}`; the page itself, `/subscribe/*` and
  static assets are served; every other path, including other pages and the whole admin app, is a 404. Pointing
  a public hostname at the dashboard therefore does not expose the dashboard on it.
- **CORS.** The page's browser code calls the API from the custom origin. The public status endpoints (page,
  domain lookup, subscriptions) use a `StatusDomains` CORS policy: the configured `Cors:AllowedOrigins` plus the
  https origin of any enabled page's domain. It never allows credentials. All other endpoints keep the default
  policy.
- **Rendering.** The public page shows the logo above the title, uses the accent for the title border and
  subscribe button, and adds a "Contact support" link in the footer. The logo has no referrer.
- Subscription emails still link to `Alerting__PublicUrl` (ADR-0162), not to the custom domain.

## Alternatives considered

- **Automatic TLS and DNS verification.** Rejected: it would make Flare a certificate manager and need ports 80/443;
  every deployment already has a proxy that does this.
- **Resolving the host in the API with a catch-all route.** Rejected: the dashboard is what serves the page's HTML.
- **Custom CSS or HTML.** Rejected: a stored-XSS and phishing surface on an unauthenticated page; one validated
  color covers most of the need.

## Consequences

- If the dashboard's own host is set as a page domain, the admin app is unreachable on it; use a separate host.
- A domain change takes up to 30-60 seconds to be seen by the dashboard.
- The Terraform provider (separate repo) does not know the new fields yet; it cannot clear them but also will not
  clobber them, since it sends them as absent.

## Not decided here

- Per-page favicon, custom page title in the browser tab, automatic certificate issuance, and a custom domain for
  subscription email links.
