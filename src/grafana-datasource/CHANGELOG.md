# Changelog

## 0.3.0

- The queries now run in a Go backend inside the Grafana server, so they work in alert rules, public dashboards and
  reports. **Release health trend** is a numeric time series per version, so a rule can alert on crash-free sessions
  below a threshold, one alert instance per version.
- The plugin now ships a binary per platform (Linux, macOS, Windows; x64 and arm64). Install it from a release zip, or
  build it with Go.
- The query results, frames and error messages are unchanged.

## 0.2.0

- New queries: Session timeline (one session's spans), App starts (cold and warm start durations per version) and Screen
  performance (load time and slow or frozen frames per screen).
- A bundled "Flare app health" dashboard: crash-free sessions and rollout per version, release health, app start and
  screen performance, top errors, sessions with errors and error triage, with release markers as annotations. Import it
  from the data source's Dashboards tab.
- A release workflow that attaches the plugin zip to a GitHub release.

## 0.1.0

- First release: Sessions, Errors, Error issues, Release health, Release health trend and Releases queries, plus
  template variables for services and versions.
