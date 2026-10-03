# ADR-0088: Built-in dashboard templates

Status: Accepted

Date: 2026-10-03

## Context

Flare shipped no dashboards: a new user started empty or imported Grafana JSON, which
carries layout but not queries. The metrics a .NET app emits by default are well known, so
the first dashboard should be one click away.

## Decision

**Templates are client-side data that install as ordinary dashboards.**

- `$lib/dashboards/templates.ts` defines five templates (ASP.NET Core, HttpClient, .NET
  runtime, Host metrics, Kubernetes) as lists of Metrics panels. "From template" on the
  Dashboards page (`DashboardTemplatesDialog`) calls `DashboardsState.installTemplate`,
  which builds a fresh layout (new ids each time) and creates a normal dashboard through the
  same path as import. There is no API, no migration, and no link back to the template, so
  an installed dashboard is not "system" and never updates under the user.
- Metric names and types are the OTel semantic-convention ones the .NET instrumentation
  (AspNetCore/Http/Runtime, as the example shop uses) and the collector's hostmetrics and
  kubeletstats receivers send. A panel whose metric isn't emitted stays on "no metric".
- Panels set `selectedMetric` **without `serviceName`**. `applySavedViewState` already
  resolves that to the first picker entry with the name and type (the shape fired metric
  alerts use). `DashboardMetricsPanelBody` now folds the dashboard's Service variable into
  the saved `services` for such panels, so picking a service re-picks the metric from that
  service instead of staying on the first one.
- Every template carries one **Service** variable. A host variable is not offered: a Metrics
  panel has no attribute filter, so an `Attribute` variable would do nothing there.
- Group-by keys are data point attributes only (`DataPointAttributes`), so resource
  attributes such as `k8s.pod.name` aren't used; those panels show one line per attribute
  combination.
- A `Service` variable's options now also include services that only emit metrics (via the
  metric names endpoint), otherwise a hostmetrics or kubeletstats collector never appears
  in the picker. This applies to every dashboard; it is skipped for a chained variable.

## Consequences

- Panel titles are English; template names and descriptions are localized (en/ru/zh-CN).
- Adding a template is a data change in `templates.ts` plus two message keys.
- The Service dropdown costs one extra metric-names call.
