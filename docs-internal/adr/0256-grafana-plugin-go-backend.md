# ADR-0256: A Go backend for the Grafana data source plugin

Status: Accepted

Date: 2026-10-19

Supersedes the "frontend-only" decision of [ADR-0254](0254-grafana-data-source-plugin.md); the rest of ADR-0254 and
[ADR-0255](0255-grafana-plugin-session-timeline-performance-dashboard.md) stands.

## Context

ADR-0254 shipped the plugin without a backend and named the cost: Grafana's alert rules, public dashboards, reports and
server-side expressions run queries on the Grafana server, where a frontend-only data source does not exist, so they showed
"Alerting: Not supported". Release health is the obvious thing to alert on, and the Prometheus route (`flare_release_*`) only
covers part of it.

## Decision

- **All querying moves into a Go backend** (`pkg/`, built on `grafana-plugin-sdk-go`), executable `gpx_flare`. The frontend
  keeps the query editors, the config editor and variable editor, and `DataSource` becomes a `DataSourceWithBackend` whose
  `applyTemplateVariables` interpolates the text fields (`csv` format, so a multi-value variable reads as a service list).
  The TypeScript request and frame builders are deleted, not kept beside the Go ones: two implementations of the same mapping
  would drift, and the Go tests now carry what the Jest tests did.
- **Same queries, same results.** Every query kind calls the endpoint ADR-0254 and ADR-0255 describe, builds the same frames
  (fields, display names, units, links, notices) and applies the same rules: the 5 minute to 7 day window clamp, one call per
  service for the single-service endpoints (now concurrent), the Service column or label only where it was there before.
- **The release health trend is a multi time series** (`FrameTypeTimeSeriesMulti`: a time field and one numeric field per
  version and service, labelled), nullable where a bucket has no sessions. That is the shape an alert rule evaluates with a
  Reduce and a Threshold expression; one alert instance per version falls out of the labels. Table queries remain tables and
  cannot be alert conditions, because Grafana's expressions need time series.
- **The backend holds the credentials.** It reads `url` and `dashboardUrl` from the data source's `jsonData` and the token from
  the decrypted secure settings and sends `Authorization: Bearer`. The proxy `routes` entry in `plugin.json` is gone, and
  the connection test is the backend's `CheckHealth` (`GET /api/releases`).
- **Errors keep their messages.** 401, 403, 404, gateway and unreachable become the same readable messages as before,
  returned per query as a downstream error, so one failing query does not fail the others in a panel.
- **Template variables use a resource.** `POST /variables` on the backend (`CallResource`) takes the kind, service and range
  and returns the values, replacing the frontend's direct calls.
- **Annotations** keep the standard mapping on the Releases frame (`annotations: true` in `plugin.json`).
- **Build and release.** `Magefile.go` uses the SDK's build package: `build:linux`, `build:darwin`, `build:windows` or all of
  them, written to `dist/` next to the frontend (build the frontend first, it empties `dist/`). CI vets, tests with the race
  detector and builds Linux; the release workflow builds every platform, signs everything in `dist/` when a token is set, and
  zips it. A release zip now holds about 28 MB of binary per platform.
- **Tested** with Go unit tests (92.8% statement coverage): the request builders, every frame builder, and `QueryData`,
  `CheckHealth` and the variables resource against a fake Flare.Api for every query kind, the bearer header, merged services,
  error messages and a failing query next to a good one. In Grafana 13.2.3 against a live Flare stack: every kind through
  `POST /api/ds/query` (which the frontend-only plugin could not be driven through), the resource route, the bundled dashboard
  with variables and release annotations, and a Grafana alert rule on crash-free sessions that evaluated and fired one
  instance for each version under the threshold and stayed normal for the others.

## Consequences

- **Installing needs a binary for the platform** (Linux, macOS, Windows; x64 or arm64). From a release this is invisible; from
  the repository it needs Go and a build step, and the how-to says so.
- **The queries no longer run in the browser**, so the Grafana server (not the browser) must reach Flare.Api, as the
  frontend-only version's proxy already required.
- **Not in the Grafana catalog yet.** Catalog submission needs a Grafana account and review; a signed build needs the access
  policy token. Neither is done here.
- **Only the trend query is alertable.** A scalar-per-version alert query (for example the latest crash-free rate as one
  value per version) would let a rule skip the Reduce step; it is not built.
- Grafana 13.2 or later, as before.

## Not decided here

Catalog submission, and an alertable scalar query shape.
