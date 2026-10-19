# ADR-0257: A release health value query for Grafana alert rules

Status: Accepted

Date: 2026-10-19

Builds on [ADR-0256](0256-grafana-plugin-go-backend.md), which named this as not built.

## Context

ADR-0256 made the Grafana data source alertable with one query, **Release health trend**: a multi time series per version.
Grafana's expressions cannot use a time series as a condition, so every rule needed a Reduce expression (`Last`, drop
non-numeric) between the query and the Threshold. For release health that step is noise: the rule wants one number per
version ("crash-free sessions over the last hour"), and the Reduce also hides which window the number covers, because it
takes the last bucket of whatever bucket width Grafana picked.

## Decision

- **A ninth query kind, `releaseHealthValue`**, answers one number per app version (and per service when several are named)
  over the query's time range. It calls the same `/api/app-sessions/release-health` endpoint as the Release health table and
  is built from the same counts, so the number matches the table.
- **The value** (`valueMetric`) is crash-free sessions %, crash-free users %, or a count (sessions, crashed sessions,
  errored sessions, users, crashed users). Crash-free rates are nullable: no sessions or users is no value, not 100%.
- **The frame is `numeric-multi`**: one frame per version with a single numeric field, the version (and service) as labels
  and no time field. Grafana's server-side expressions take that as a number, so a Threshold goes straight on the query and
  one alert instance per version falls out of the labels, as with the trend.
- **`TypeVersion` is `0.1`.** At `0.0` Grafana does not treat the frame as dataplane data, reads a frame without a time
  field as a legacy table and fails the rule with "input data must be a wide series but got type not". The first live run
  hit this; a Go test pins the version.
- **A minimum session count** (`minSessions`) leaves out versions with fewer sessions. A release a few minutes old has a
  crash-free rate of 0% or 100% from one or two sessions, which would fire or flap a rule on nothing. A version left out
  produces no instance, and an empty result is "No data", which the rule's own no-data setting handles.
- **The window is the rule's query time range**, clamped to Flare's 5 minute to 7 day range like every session query, so a
  rule judges "the last hour" by setting the query's relative time range. Nothing about it is hidden in a bucket width.
- **The trend stays.** It is still the right shape for a rule on how the rate moved inside the window (Reduce, then
  Threshold) and for drawing. The table queries remain tables.

## Consequences

- A release health rule is two expressions (query, Threshold) instead of three, and the how-to documents that form first.
- Plugin version 0.4.0.
- Verified in Grafana 13.2.3 against a live Flare stack: the query through `POST /api/ds/query` with a Threshold expression
  on it, and a stored alert rule (`minSessions` 20, Threshold below 95) that fired one instance for the one version under
  the threshold and left the others normal.
- Unit tests cover every value, nulls, the session floor, services and the dataplane version.
- Grafana catalog submission is still not done.

## Not decided here

Catalog submission.
