# Flare data source for Grafana

A Grafana data source plugin for what Flare's Prometheus, Loki and Tempo compatible APIs cannot express: client app
**sessions**, **error groups** and their triage state, per-version **release health**, and marked **releases**. Frontend only:
Grafana proxies the calls to Flare.Api and adds the personal access token, so the browser never sees it.

User guide: [docs/how-to/integrations/query-with-grafana-plugin.md](../../docs/how-to/integrations/query-with-grafana-plugin.md).
Design: [ADR-0254](../../docs-internal/adr/0254-grafana-data-source-plugin.md).

## Layout

| File | Role |
|---|---|
| `src/plugin.json` | Plugin manifest, including the `flare` proxy route that carries the token header |
| `src/datasource.ts` | `DataSourceApi`: runs each query kind, variables, connection test, error messages |
| `src/query.ts` | Pure: dashboard time range and query fields to Flare.Api request bodies |
| `src/frames.ts` | Pure: Flare.Api responses to Grafana data frames |
| `src/components/` | Config, query and variable query editors |

Keep `query.ts` and `frames.ts` free of Grafana runtime imports beyond `@grafana/data`: they are what the Jest tests cover.

## Develop

Needs Node 22 or later.

```bash
npm install
npm run dev            # rebuilds dist/ on change
npm run typecheck && npm run lint && npm run test:ci
```

Run Grafana with the built plugin mounted, pointed at a Flare.Api on the host:

```bash
docker run -d --name flare-grafana -p 3300:3000 \
  -v "$PWD/dist:/var/lib/grafana/plugins/apardev-flare-datasource" \
  -v "$PWD/provisioning:/etc/grafana/provisioning" \
  -e GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource \
  -e GF_AUTH_ANONYMOUS_ENABLED=true -e GF_AUTH_ANONYMOUS_ORG_ROLE=Admin \
  grafana/grafana:13.2.3
```

`provisioning/datasources/datasources.yml` adds a **Flare** data source on `http://host.docker.internal:8080`; put a token in
it if Flare has authentication on. The queries run in the browser, so they cannot be exercised with `curl` against Grafana's
`/api/ds/query`: use Explore or a dashboard. `curl http://localhost:3300/api/datasources/proxy/uid/<uid>/flare/api/releases`
does exercise the proxy route and the token header.

## Tests

Jest covers the request builders (`query.test.ts`) and the frame builders (`frames.test.ts`). The editors and `datasource.ts`
are not unit-tested; they are covered by running the plugin in Grafana against a live Flare stack (every query kind in
Explore, a dashboard with variables and a release annotation, and the connection test). The Grafana image version above is the one
that was used.
