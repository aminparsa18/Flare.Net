# ADR-0252: Alerts and releases as Grafana annotations

Status: Accepted

Date: 2026-10-19

## Context

A Grafana dashboard on Flare's data sources shows the symptom (a latency spike, a crash-free dip) but not what Flare knows
about the cause: which alert rule fired and when it recovered, and which release was deployed. Grafana draws those as
annotations. The Prometheus data source builds them from a PromQL expression (each series' non-zero runs become events, the
series labels fill the title, text and tags), and Flare already serves a Prometheus API
([ADR-0109](0109-prometheus-query-api.md)) with synthetic overlay metrics ([ADR-0250](0250-release-health-prometheus-metrics.md)).
Grafana's other annotation source, the built-in Annotations HTTP API, is push-only and would need Flare to write into the
user's Grafana.

## Decision

- **Two more synthetic gauges on the Prometheus API**, answered by an `AnnotationMetricQueryService` that decorates the
  release-health overlay (so a stored metric under these names is shadowed, and the Metrics page, dashboards and metric alerts
  never list them):
  - `flare_alert_firing`, labelled `alertname` and `rule_id`: 1 in every epoch-aligned step that overlaps an incident.
  - `flare_release_deployed`, labelled `service_name`, `service_version` and, when set, `commit` and `url`: 1 in the step of
    the release's `DeployedAt`. One series per release, so each deploy keeps its own commit and link as labels.
- **An incident is a fire until the resolution that ends it.** `AlertIncidents.Build` walks each rule's transitions in time
  order: a fire opens an incident unless one is open (a re-fire after the cooldown is the same incident), a resolution closes
  it, and a resolution with nothing open is ignored. A rule still firing runs to the end of the range, capped at now. The
  incident's true start may precede the range, so the query also reads each rule's last event before `from`.
- **What counts as a fire matches `GetLastFiredAsync`:** `Escalated = 0`, and not `NotificationStatus = 'Suppressed'` (a
  maintenance window swallowed it and nobody was told), so the annotations show what the on-call saw.
- **Alert visibility follows project access (ADR-0123).** A restricted caller's rule ids are filtered to their projects before
  the events are read; an unrestricted caller reads every rule, deleted ones included (the event keeps the rule name). The
  overlay reads the caller from `IHttpContextAccessor` at call time, because `PromEvaluator` is a singleton. Releases are not
  project-owned; the stored release query already applies the service scope.
- **Releases are the marked ones (ADR-0182)**, not "a version first appeared in telemetry": a mark carries the deploy time,
  commit and link, and the CI that calls `flare releases mark` is the source of truth for when a deploy happened.
- **Bounded:** at most 50,000 points per query, beyond which it fails and asks for a larger step or a shorter range, as
  release health does. The evaluator's own step cap already limits ordinary range queries.

## Consequences

- Deploy and alert annotations need nothing installed in Grafana beyond the Flare Prometheus data source, and work in Grafana
  Cloud against a Flare reachable from it.
- A deploy shows once, at the marked time. An instance that never marks releases has no deploy annotations.
- `ListAsync` returns the newest 500 marked releases, so a range older than the 500th release shows none; the range a
  dashboard annotates is normally recent.
- Alert annotations read `alert_events` by `FiredAt`, which is ordered by `(RuleId, FiredAt)`; the read before `from` is a
  per-rule `GROUP BY` over earlier partitions, small at the volume this table has.
- Not cached, like the other overlay metrics.

## Not decided here

Flare-initiated pushes into Grafana's annotations API, SLO burn and maintenance windows as annotations, and the Grafana data
source plugin for sessions and errors.
