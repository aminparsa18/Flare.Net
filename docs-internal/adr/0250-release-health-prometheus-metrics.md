# ADR-0250: Release health as Prometheus metrics

Status: Accepted

Date: 2026-10-18

## Context

Crash-free sessions per version ([ADR-0175](0175-release-health.md), [ADR-0189](0189-release-health-trends.md)) only exist
behind Flare's own endpoints, so a team that lives in Grafana cannot put "2.2.0 crash-free rate" on a panel or alert on
it with the rules it already uses. Flare's Prometheus API ([ADR-0109](0109-prometheus-query-api.md)) is what a Grafana
data source reads, and it only serves stored OTel metrics.

## Decision

- **Four built-in metrics on the Prometheus API**, labelled `service_name` and `service_version`:
  - `flare_release_sessions_total` and `flare_release_crashed_sessions_total` (counters): sessions that started, and
    those that crashed, in each step. A crash-free rate is `1 - increase(crashed[1h]) / increase(sessions[1h])` over
    any window the caller picks, with the usual `sum by (...)` to fold services or versions.
  - `flare_release_users` and `flare_release_crashed_users` (gauges): distinct users with a session that started in the
    step, and with a crashed one.
- **Derived at query time, no table or migration**, like the rest of release health. A new
  `AppSessionQueryBuilder.BuildReleaseHealthSeries` uses the same session definition as `BuildReleaseHealth` (crashed =
  `app.unhandled_exception` with `exception.escaped = true`; a user = `user.id` on a span or resource) and counts each
  session once, in the epoch-aligned bucket of its first span (ADR-0189).
- **An overlay on the Prometheus API only.** `ReleaseHealthMetricQueryService` decorates `IMetricQueryService` for
  `PromEvaluator` alone and answers the `flare.release.*` names itself; everything else passes through. The Metrics page,
  dashboards and metric alerts never list or chart them, and the evaluator needed no change: the counters are the
  per-interval increases Flare already stores for any counter, so `rate`/`increase`, `sum by` and `a / b` just work.
- **A crashed series for every bucket that has sessions**, with 0 where nothing crashed. Without it `crashed / sessions`
  loses its numerator in a clean bucket and the rate disappears instead of reading 100%.
- **`service_version` is the only attribute.** An app that reports no version has no `service_version` label. The
  `flare.release.` prefix is reserved: a stored metric with such a name is shadowed for the Prometheus API.
- **Bounded:** at most 50,000 service x version x bucket rows per query; beyond that the query fails with a message to
  narrow it rather than returning part of the data. Series are capped at 200 like any selector.

## Consequences

- Crash-free **users** are per step, not per window: distinct users do not add up across buckets, so the gauges answer
  "users in this step", and a daily crash-free users panel needs a one-day step. An instant query has a one-minute step,
  which makes the gauges nearly useless there; alert on sessions, and chart users with a range query.
- A session that began before the queried range but has spans inside it is counted in the first bucket of the range,
  the edge effect ADR-0167 documents. Mobile sessions are short, so it shows only in the first bucket.
- The results are not cached: each Grafana refresh runs the span aggregation. `Query__*` caps bound it, and a long
  refresh interval on a long range is the control.
- Listing the metric names always includes the four, even on an instance with no mobile apps; they are empty there.
  `label_values(flare_release_sessions_total, service_name)` finds the services that have sessions; the unscoped
  `service_name` values call does not, because finding them costs a span scan.

## Not decided here

A rolling-window crash-free users gauge (a fixed 24 hours trailing, say), release health as a pre-aggregated table for
long ranges, and the Grafana data source plugin for what these metrics still cannot express (sessions, errors).
