# ADR-0254: A Grafana data source plugin for sessions, errors and release health

Status: Accepted

Date: 2026-10-19

## Context

Flare serves Prometheus, Loki and Tempo compatible APIs ([ADR-0109](0109-prometheus-query-api.md),
[ADR-0236](0236-loki-compatible-log-api.md), [ADR-0217](0217-tempo-compatible-trace-api.md)), so Grafana's own data sources read
metrics, logs and traces. Release health went through the Prometheus API as `flare_release_*` metrics
([ADR-0250](0250-release-health-prometheus-metrics.md)). What those query languages cannot carry is the shape of the rest:
client app sessions (a row per session, not a series), error groups with their triage state, and the releases a team marked.
The roadmap listed a data source plugin for exactly that.

## Decision

- **A frontend-only data source plugin**, `apardev-flare-datasource`, in `src/grafana-datasource` (TypeScript, React, the
  `@grafana/create-plugin` toolchain). It has no backend component and talks to Flare.Api through a **plugin route**
  (`plugin.json` `routes`): Grafana proxies `.../flare/*` to the configured URL and adds `Authorization: Bearer <token>` from
  the encrypted `secureJsonData`. The token never reaches the browser, no CORS setup is needed on Flare.Api, and the URL is
  the one the Grafana *server* reaches.
- **Six query kinds, each one existing endpoint, no new API.** Sessions (`POST /api/app-sessions/list`), Errors
  (`/api/errors/groups`), Error issues (`GET /api/errors/issues`), Release health (`/api/app-sessions/release-health`), Release
  health trend (`.../release-health/trend`) and Releases (`GET /api/releases`). Each becomes a data frame: tables for the first
  four and the last, one wide time series frame per version for the trend. Crash-free rates are computed in the plugin from the
  session counts, so they match the Releases view.
- **The dashboard time range maps onto Flare's window.** The session endpoints take a window ending at `endUnixMs` and clamp
  it to 5 minutes to 7 days; the plugin sends the range's length and end, and puts a warning notice on the frame when it had to
  cut a longer range, and when Flare capped the session list at 500 rows. Errors send `from`/`to`.
- **Services are a comma separated string**, which is what Grafana's `csv` interpolation of a multi-value variable produces. The
  session and release health endpoints take one service, so several become one call each, merged, with a Service column or
  label; the errors endpoint takes a list.
- **Template variables** through `CustomVariableSupport` (services with sessions, versions, services with errors, release
  versions), derived from the same endpoints (`services`/`versions` on the session list, `facet-values` for errors).
- **Releases double as annotations.** The frame has `time`, `title` and `text` fields, so Grafana's standard annotation
  mapping marks deploys with no custom annotation editor.
- **Connection test** reads `GET /api/releases`, the cheapest authenticated call, and turns 401, 403, 404 and a gateway error
  into messages that say what to check.
- **Tested at two levels.** Jest covers the pure request builders (`query.ts`) and frame builders (`frames.ts`). The plugin was
  also run in Grafana 13.2.3 against a live Flare stack: every query kind in Explore, variables, annotations and a dashboard
  panel, and the proxy route's header with a stand-in server.

## Consequences

- **No alerting.** A frontend-only data source cannot be used by Grafana alert rules, public dashboards, reports or server-side
  expressions, and Grafana shows "Alerting: Not supported" for it. Alerting on release health stays available through the
  `flare_release_*` metrics on a Prometheus data source and through Flare's own alert rules. A Go backend component would lift
  this at the price of a per-platform binary build and signing; it is the next step if the plugin sees use.
- **Not in the Grafana catalog.** The plugin is built from the repository and loaded unsigned
  (`GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS`). Signing, a release workflow and catalog submission need an account and
  review and are not done here.
- **Grafana 13.2 or later.** `grafanaDependency` follows the version it was built and tested against; the shared
  `@grafana/*` packages come from the host, so older versions are untested.
- **Sessions are at most 500 rows over at most 7 days per query**, Flare's own limits, surfaced rather than hidden.
- The plugin has its own `package.json` and is outside `Flare.slnx`; `.github/workflows/grafana-plugin.yml` type-checks, lints,
  tests and builds it when it changes.

## Not decided here

A backend component for alerting, signing and catalog submission, a session timeline query (one session's spans) and
performance (app start and screen load) queries, and a prebuilt dashboard shipped with the plugin.
