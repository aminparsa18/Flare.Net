# Flare data source for Grafana

A Grafana data source plugin for what Flare's Prometheus, Loki and Tempo compatible APIs cannot express: client app
**sessions** (and one session's timeline), **error groups** and their triage state, per-version **release health**, app start and
screen performance, and marked **releases**. A TypeScript frontend (editors) and a Go backend (`pkg/`) that calls Flare.Api
with the personal access token, so the queries also work in alert rules, public dashboards and reports and the browser never
sees the token.

User guide: [docs/how-to/integrations/query-with-grafana-plugin.md](../../docs/how-to/integrations/query-with-grafana-plugin.md).
Design: [ADR-0254](../../docs-internal/adr/0254-grafana-data-source-plugin.md),
[ADR-0255](../../docs-internal/adr/0255-grafana-plugin-session-timeline-performance-dashboard.md),
[ADR-0256](../../docs-internal/adr/0256-grafana-plugin-go-backend.md).

## Layout

| File | Role |
|---|---|
| `src/plugin.json` | Plugin manifest: backend executable `gpx_flare`, alerting, annotations, bundled dashboards |
| `src/datasource.ts` | `DataSourceWithBackend`: query defaults, template variable interpolation, the variables resource call |
| `src/components/` | Config, query and variable query editors |
| `src/dashboards/` | Bundled dashboards, declared under `includes` in `plugin.json`; built on the plugin's own queries |
| `pkg/main.go` | Backend entry point |
| `pkg/plugin/datasource.go` | `QueryData`, `CheckHealth`, `CallResource`; one `run` case per query kind |
| `pkg/plugin/query.go` | Pure: the query model, the time window clamp, Flare.Api request bodies |
| `pkg/plugin/frames.go` | Pure: Flare.Api responses to data frames |
| `pkg/plugin/client.go` | Flare.Api HTTP client (bearer token) and the readable error messages |
| `pkg/plugin/resources.go` | The `POST /variables` resource behind template variables |
| `Magefile.go` | Backend build for every platform, via the plugin SDK's build package |

`query.go` and `frames.go` stay free of I/O: they are what most of the Go tests cover. A new query kind needs a case in
`datasource.go`, a frame builder, an entry in `QueryKind` (`src/types.ts`) and the editor.

## Develop

Needs Node 22 or later and Go (the version in `go.mod`).

```bash
npm install
npm run build                                  # the frontend; it empties dist/
go run github.com/magefile/mage -v build:linux # the backend (build:darwin, build:windows, or no target for all)
npm run typecheck && npm run lint
go vet ./pkg/... && go test -race ./pkg/...
```

`npm run dev` rebuilds the frontend on change but empties `dist/`, so rebuild the backend after it. Run Grafana with the built
plugin mounted (use the Linux binary for Docker), pointed at a Flare.Api on the host:

```bash
docker run -d --name flare-grafana -p 3300:3000 \
  -v "$PWD/dist:/var/lib/grafana/plugins/apardev-flare-datasource" \
  -v "$PWD/provisioning:/etc/grafana/provisioning" \
  -e GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource \
  -e GF_AUTH_ANONYMOUS_ENABLED=true -e GF_AUTH_ANONYMOUS_ORG_ROLE=Admin \
  grafana/grafana:13.2.3
```

`provisioning/datasources/datasources.yml` adds a **Flare** data source on `http://host.docker.internal:8080`; put a token in
it if Flare has authentication on. The backend answers `curl` against Grafana's `POST /api/ds/query` (with
`{"queries":[{"refId":"A","datasource":{"uid":"<uid>"},"kind":"releaseHealth"}],"from":"now-7d","to":"now"}`) and
`POST /api/datasources/uid/<uid>/resources/variables`, which is the quickest way to try a query.

## Release

Push a `grafana-plugin-v<version>` tag whose version matches `package.json` and has a `## <version>` heading in `CHANGELOG.md`.
`.github/workflows/grafana-plugin-release.yml` tests the plugin and builds the frontend and the backend for every platform, signs it when the `GRAFANA_ACCESS_POLICY_TOKEN`
secret is set (private signatures need the `GRAFANA_PLUGIN_ROOT_URLS` repository variable), and attaches
`apardev-flare-datasource-<version>.zip` with its checksums to a GitHub release. Unsigned is what a catalog submission takes.

## Tests

Go tests (`go test ./pkg/...`) cover the request builders, every frame builder, and `QueryData`, `CheckHealth` and the
variables resource against a fake Flare.Api (`httptest`) for every query kind. The editors and `datasource.ts` are not
unit-tested; they are covered by running the plugin in Grafana against a live Flare stack: every kind through `/api/ds/query`,
the bundled dashboard imported through `/api/dashboards/import`, variables, the release annotation, the connection test, and
a Grafana alert rule on **Release health trend** (Reduce `Last`, Threshold below 99) that fires one instance per version. The
Grafana image version above is the one that was used.
