# How to chart Flare sessions, errors and release health in Grafana

The [Prometheus](query-with-prometheus-api.md), [Loki](query-with-loki-api.md) and [Tempo](query-with-tempo-api.md) APIs
let Grafana's own data sources read Flare's metrics, logs and traces. Some of what Flare knows has no place in those
query languages: the client app **sessions** behind a crash, the **error groups** and their triage state, per-version
**release health**, and the **releases** you marked. The Flare data source plugin reads those, so they can sit next to
your other panels.

Its backend runs inside the **Grafana server** and calls Flare.Api with your token, so the Grafana server must reach Flare, the token never reaches the browser, and the queries also work in alert rules, public dashboards and reports.

## Install the plugin

The plugin is not in the Grafana catalog yet. It needs Grafana 13.2 or later. It has a backend component, so the install must carry a binary for your platform (Linux, macOS or Windows; x64 or arm64).

**From a release.** Each `grafana-plugin-v*` [GitHub release](https://github.com/aminparsa18/Flare.Net/releases) carries `apardev-flare-datasource-<version>.zip` with the frontend and the backend for every platform. Unzip it into Grafana's plugin directory, allow it to load unsigned, and restart Grafana:

```bash
unzip apardev-flare-datasource-<version>.zip -d /var/lib/grafana/plugins
export GF_PLUGINS_ALLOW_LOADING_UNSIGNED_PLUGINS=apardev-flare-datasource
```

**From the repository.** This needs Node 22 and Go. Build the frontend first (it empties `dist/`), then the backend (`build:linux`, `build:darwin`, `build:windows`, or no target for all of them):

```bash
cd src/grafana-datasource
npm ci && npm run build
go run github.com/magefile/mage -v build:linux
```

Copy `dist/` into Grafana's plugin directory as `apardev-flare-datasource` and set the same environment variable as above. With Docker, mount `dist/` (or the unzipped release) at `/var/lib/grafana/plugins/apardev-flare-datasource`.

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
| **Session timeline** | One session's spans in start order: name, duration, screen, error, exception, breadcrumb and a link to the trace | Session ID |
| **Errors** | Exception groups ranked by occurrences, with first and last seen, affected services and owner | Service, top N |
| **Error issues** | Triage state of the error groups: status (open, resolved, ignored, regressed), assignee, regressed version and issue link | None |
| **Release health** | One row per app version: sessions, crashed and errored sessions, users, and the crash-free session and user rates | Service |
| **Release health trend** | One series per version over time | Service, series (crash-free sessions %, sessions or crashed sessions), bucket width |
| **Release health value** | One number per version over the time range: crash-free sessions or users %, or a count; made for alert rules | Service, value, minimum sessions |
| **Releases** | The deploys you marked, with commit, URL, notes and new-error and regression counts | Service |
| **App starts** | Cold and warm start duration (`app.start` spans): starts, p50 and p95 per version | Service, version |
| **Screen performance** | Per screen: loads, load p50 and p95 (`screen.load` spans), and the share of slow and frozen frames (`screen.frames` spans) | Service, version |

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

## Import the dashboard

The plugin bundles a **Flare app health** dashboard: crash-free sessions and sessions per version over time, release
health, app start and screen performance, top errors, sessions with errors and error triage, with your marked releases as
annotations. Open the data source's **Dashboards** tab and click **Import**, then pick the **Service** variable's services at
the top. The panels are ordinary Grafana panels on the queries above, so edit or copy them freely.

## Alert on crash-free sessions

**Release health value** returns one number per app version (and per service when you name several) over the query's time range, so a Grafana alert rule can compare it directly. Create a rule with these queries:

1. **A**: the Flare data source, query **Release health value**, your service, value **Crash-free sessions %**. Set the query's time range to the window to judge, for example the last hour (Flare answers 5 minutes to 7 days). Set **Min sessions** (for example `50`) so a release with a handful of sessions cannot fire the rule.
2. **B**: a **Threshold** expression on A, **Is below** `99`.

There is no Reduce step: the value is already a single number. Grafana evaluates one alert instance per version, labelled `version` (and `service`), so a bad release alerts on its own. The value can also be crash-free users %, sessions, crashed sessions, errored sessions, users or crashed users. A version left out by **Min sessions** produces no instance, and a range with no sessions at all is "No data", handled by the rule's no-data setting.

To alert on how crash-free sessions moved inside the window instead, use **Release health trend**, which returns a time series: add a **Reduce** expression on it (function **Last**, mode **Drop non-numeric**) and put the **Threshold** on that. The table queries (sessions, errors, release health, app performance) cannot be alerted on, because Grafana's expressions need numbers or time series; for those, use Flare's own [alert rules](../../tutorials/first-alert.md).

## Limits

- **Alert rules need a number or a time series.** Only **Release health value** (a number) and **Release health trend** (a time series) return one; the table queries show in dashboards, public dashboards and reports but not in alert conditions.
- **At most 7 days per query.** A wider time range is cut to its last 7 days and the panel says so.
- **At most 500 sessions per query**, the most recent ones. Narrow the service, version or time range when the panel warns
  that the list was cut.
- **A session timeline needs the session inside the time range.** The query scans the dashboard's range for that session's
  spans, so move the range to when the session ran (the Sessions query lists it).
- **Release health follows Flare's session definition**: a session is crashed when it has an escaped
  `app.unhandled_exception`, and users are the distinct `user.id` values, the same as Flare's Releases view.

Design notes: [ADR-0254](../../../docs-internal/adr/0254-grafana-data-source-plugin.md), [ADR-0255](../../../docs-internal/adr/0255-grafana-plugin-session-timeline-performance-dashboard.md), [ADR-0256](../../../docs-internal/adr/0256-grafana-plugin-go-backend.md).
