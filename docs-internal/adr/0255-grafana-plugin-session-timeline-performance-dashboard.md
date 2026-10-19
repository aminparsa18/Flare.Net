# ADR-0255: Grafana plugin: session timeline, app performance, a bundled dashboard and a release workflow

Status: Accepted

Date: 2026-10-19

## Context

[ADR-0254](0254-grafana-data-source-plugin.md) shipped the frontend-only Grafana data source plugin with sessions, errors, error
issues, release health and releases, and left four follow-ups open. Three of them need no new Flare.Api endpoint and no backend
component: a session timeline query, app performance queries, a dashboard shipped with the plugin, and a release workflow. A Go
backend component and catalog submission stay open.

## Decision

- **Three more query kinds, each an existing endpoint.** *Session timeline* is `POST /api/app-sessions/timeline` (one session's
  spans), *App starts* and *Screen performance* are the two halves of `POST /api/app-sessions/performance` (`starts` and
  `screens`). No API change.
- **Timeline scans the dashboard time range.** The endpoint wants a window bounding its `spans` scan; the plugin sends the
  dashboard range as `fromUnixMs`/`toUnixMs` rather than inventing a padded window, so Grafana's range picker means what it
  does everywhere else. The cost is that a session older than the range returns nothing until the range is moved; the Session
  ID field's tooltip and the how-to say so. The Session ID field takes a variable. The frame is time-keyed (so Grafana's table, logs and
  state timeline panels can read it), with `ms` units, the trace ID linking to `/traces/<id>` when a dashboard URL is set, and
  the session's service, version, OS and device in `meta.custom`.
- **Performance frames are tables, with the slow and frozen share computed in the plugin** from `frames`, `slowFrames` and
  `frozenFrames` (null for a screen with no frames), like the crash-free rates in ADR-0254. Several services run one call each,
  as for release health, and add a Service column.
- **A bundled dashboard, `Flare app health`** (`src/dashboards/app-health.json`, declared under `includes` in `plugin.json`, so
  it shows on the data source's Dashboards tab and the build copies it). It uses `__inputs` for the data source and one
  multi-value `Service` query variable, and has crash-free sessions and sessions per version, release health, app starts, screen
  performance, top errors, sessions with errors and error triage, plus the Releases annotation. It is built only from the
  plugin's own queries.
- **A release workflow** (`.github/workflows/grafana-plugin-release.yml`) on a `grafana-plugin-v<version>` tag. It checks the tag
  against `package.json` and a `## <version>` changelog heading, runs typecheck, lint, tests and build, signs when the
  `GRAFANA_ACCESS_POLICY_TOKEN` secret exists (with `--rootUrls` from the `GRAFANA_PLUGIN_ROOT_URLS` variable if set), zips
  `dist/` under the plugin ID with MD5 and SHA1 files, and creates the GitHub release with the changelog section as notes. Without
  the secret the zip is unsigned, which is also what a catalog submission takes.
- **The plugin version is 0.2.0** (it was 1.0.0 in `package.json` against a 0.1.0 changelog; nothing had been released).
- **Tested** with Jest for the two new request builders and the three frame builders, and in Grafana 13.2.3 against a live Flare
  stack: the dashboard imported from the bundle through `/api/dashboards/import` with every panel showing data and no failed
  call, and the timeline query in Explore. The workflow was checked by running its version check, packaging and release-notes
  steps locally; it has not run on GitHub.

## Consequences

- The three queries and the dashboard still run in the browser, so none of them work in alert rules, public dashboards or
  reports; that needs the backend component.
- A signed or catalog build needs a Grafana account: the access policy token, and the catalog submission itself, are not done
  here.
- The dashboard's panels name queries by `kind`, so renaming a kind is a dashboard-breaking change.

## Not decided here

A Go backend component (alerting, public dashboards, reports) and Grafana catalog submission.
