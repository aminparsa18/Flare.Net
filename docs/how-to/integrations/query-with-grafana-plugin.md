# How to chart Flare sessions, errors and release health in Grafana

The [Prometheus](query-with-prometheus-api.md), [Loki](query-with-loki-api.md) and [Tempo](query-with-tempo-api.md) APIs
let Grafana's own data sources read Flare's metrics, logs and traces. Some of what Flare knows has no place in those
query languages: the client app **sessions** behind a crash, the **error groups** and their triage state, per-version
**release health**, and the **releases** you marked. The Flare data source plugin reads those, so they can sit next to
your other panels.

It runs in the browser and calls Flare.Api through Grafana's data source proxy, so the **Grafana server** must reach
Flare, and your token never reaches the browser.

## Install the plugin

The plugin is not in the Grafana catalog yet, so install it from the repository. It needs Grafana 13.2 or later.

```bash
cd src/grafana-datasource
npm ci && npm run build
```

Copy the resulting `dist/` folder into Grafana's plugin directory as `apardev-flare-datasource`, allow it to load
unsigned, and restart Grafana:

```bash
cp -r dist /var/lib/grafana/plugins/apardev-flare-datasource
export GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource
```

With Docker, mount `dist/` at `/var/lib/grafana/plugins/apardev-flare-datasource` and set the same environment variable.

## Connect Grafana

1. Create a [personal access token](../admin/configure-authentication.md#personal-access-tokens).
2. In Grafana, add a **Flare** data source.
3. Set **Flare API URL** to your Flare API base, as the Grafana server reaches it (`http://flare-api:8080` on the Flare
   compose network).
4. Paste the token. Leave it empty only if Flare has authentication turned off.
5. Optionally set **Flare dashboard URL**, so a session's ID links to its page in Flare.
6. Click **Save & test**.

## Queries

| Query | Result | Options |
|---|---|---|
| **Sessions** | One row per client app session in the time range: start, last seen, ID, service, version, OS, device, spans, traces, errors and screens | Service, version, errors only |
| **Errors** | Exception groups ranked by occurrences, with first and last seen, affected services and owner | Service, top N |
| **Error issues** | Triage state of the error groups: status (open, resolved, ignored, regressed), assignee, regressed version and issue link | None |
| **Release health** | One row per app version: sessions, crashed and errored sessions, users, and the crash-free session and user rates | Service |
| **Release health trend** | One series per version over time | Service, series (crash-free sessions %, sessions or crashed sessions), bucket width |
| **Releases** | The deploys you marked, with commit, URL, notes and new-error and regression counts | Service |

**Service** takes one name or several separated by commas, and a multi-value variable works. Empty means every service.
Version and release health queries with several services run once per service and add a Service column or label.

## Template variables

Create a variable of type **Query**, choose the Flare data source, and pick what it lists: services with sessions, app
versions with sessions (optionally for one service), services with errors, or release versions. Use the variable in the
Service field of a panel, as in `$service`.

## Show deploys on a panel

Add an annotation query on the Flare data source with the **Releases** query. Grafana's annotation settings map the
`time`, `title` and `text` fields, which the query provides, so a dashed line marks each deploy. For release markers on
Prometheus panels, use the `flare_release_*` metrics and annotation metrics described in
[the Prometheus API how-to](query-with-prometheus-api.md#annotations).

## Limits

- **No alerting, public dashboards or server-side expressions.** The queries run in the browser, so Grafana shows
  alerting as "Not supported" for this data source. To alert on release health, use the `flare_release_*` metrics through
  the Prometheus data source, or Flare's own [alert rules](../../tutorials/first-alert.md).
- **At most 7 days per query.** A wider time range is cut to its last 7 days and the panel says so.
- **At most 500 sessions per query**, the most recent ones. Narrow the service, version or time range when the panel warns
  that the list was cut.
- **Release health follows Flare's session definition**: a session is crashed when it has an escaped
  `app.unhandled_exception`, and users are the distinct `user.id` values, the same as Flare's Releases view.

Design notes: [ADR-0254](../../../docs-internal/adr/0254-grafana-data-source-plugin.md).
