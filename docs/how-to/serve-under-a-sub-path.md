# How to serve Flare under a sub-path

Serve Flare at `https://example.com/flare/` behind a reverse proxy instead of
giving it its own (sub)domain. The dashboard, the API, the SSO callbacks and the
links in alert notifications all pick up the one `FLARE_BASE_PATH` setting.

## Prerequisites

- The standalone Docker deployment ([run standalone](run-standalone.md)). The
  setting is read by the published images, so no rebuild is needed.
- A reverse proxy (nginx, Caddy, Traefik, …) in front of Flare that terminates
  TLS for `example.com`.

## Steps

1. Pick the prefix. It must start with `/`, must not end with one, and may
   have several segments: `/flare`, `/tools/flare`. Leaving it empty serves
   Flare at the root, as before.

2. Set it for both containers and point the other URLs at the public address,
   in a `docker-compose.override.yml` next to `docker-compose.yml`:

   ```yaml
   services:
     api:
       environment:
         Flare__BasePath: /flare
         Cors__AllowedOrigins__0: https://example.com
         Alerting__PublicUrl: https://example.com/flare
     alert-worker:
       environment:
         Alerting__PublicUrl: https://example.com/flare
     dashboard:
       environment:
         FLARE_BASE_PATH: /flare
         ORIGIN: https://example.com
         PUBLIC_API_URL: https://example.com/flare
   ```

   `PUBLIC_API_URL` is the prefix only: the dashboard appends `/api/...` to it,
   so the API ends up at `https://example.com/flare/api/`. Putting
   `FLARE_BASE_PATH=/flare` in `.env` sets the two base-path variables, but not
   the URLs, which are fixed in `docker-compose.yml`, so use the override file.

3. Route both prefixes in the proxy. Do **not** strip the prefix for the
   dashboard; for the API either way works. An nginx example:

   ```nginx
   location /flare/api/ {
       proxy_pass http://127.0.0.1:8080;
       proxy_http_version 1.1;
       proxy_set_header Upgrade $http_upgrade;     # live tail is a WebSocket
       proxy_set_header Connection $connection_upgrade;
       proxy_set_header Host $host;
   }
   location /flare/ {
       proxy_pass http://127.0.0.1:7777;
       proxy_set_header Host $host;
   }
   ```

   Without a trailing slash and URI on `proxy_pass`, nginx forwards the request
   path unchanged. If your proxy strips `/flare/api` before forwarding, the API
   copes: it assumes the prefix from `Flare__BasePath` either way.

4. Restart the stack and open `https://example.com/flare/`.

If you sign in with Entra ID or OpenID Connect, the redirect URI shown on the
authentication settings page now includes the prefix (for example
`https://example.com/flare/signin-oidc-generic`). Register that exact value
with your identity provider.

OTLP ingestion (ports 4317 and 4318) is not affected. Point your apps at those
ports as before.

## Troubleshooting

- **Every page 404s.** The dashboard was started without `FLARE_BASE_PATH`, or
  the proxy strips `/flare/` before forwarding to it. The dashboard needs the
  full path.
- **The page loads but every API call fails.** `PUBLIC_API_URL` should be
  `https://example.com/flare`, not `https://example.com/flare/api`, and
  `Cors__AllowedOrigins__0` must be the bare origin `https://example.com`.
- **Notification links point at the wrong place.** `Alerting__PublicUrl` must
  include the prefix.
- **The dashboard container reports unhealthy.** Its health check requests
  `<FLARE_BASE_PATH>/` inside the container; make sure the variable is set on
  the `dashboard` service, not only on `api`.
