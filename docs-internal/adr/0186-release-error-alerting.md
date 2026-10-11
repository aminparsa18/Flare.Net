# ADR-0186: Release error alerting

Status: Accepted

Date: 2026-10-11

## Context

[ADR-0182](0182-release-tracking.md) derives the exception groups a release introduced, but nothing pages anyone when
a deploy goes wrong; the Releases page has to be opened.

## Decision

- **A new `AlertConditionKind.ReleaseErrors`**, following the discriminator pattern of
  [ADR-0022](0022-exception-count-alerting.md) and [ADR-0108](0108-slo-error-budgets.md). It is a count, so it reuses
  `AlertThreshold.Count` and `Comparator` and adds no `alert_events` columns.
- **No new persisted field, no migration.** The service scope is carried in the existing `ExceptionCondition`: only its
  `Filter.Services` is read (empty = every service) and its exception type and message are ignored. This keeps the
  stored shape, the hand-written MemoryPack companions and the wire types unchanged.
- **Semantics.** `WindowSeconds` is how long after a deploy the rule keeps watching. Each evaluation takes the releases
  of the scoped services deployed within that window and counts, per release, the exception groups first seen under its
  version (the ADR-0182 query, one scan per service); the largest count is compared with the threshold. With no release
  in the window the count is 0, so the rule is quiet between deploys and resolves once the window passes the deploy.
- **Not supported:** no-data alerting, anomaly source, and minimum data points (a count rule has no sample size); the API
  rejects them. A recovery threshold is allowed like any count rule.
- **Surfaces:** the alert form, `AlertRuleTable`, `flare alerts` (generic count display), the dry-run test endpoints,
  the notification text (links to `/releases`) and the incident-summary prompt.

## Consequences

- A rule scoped to a service whose release was never marked never fires; marking is the deploy pipeline's job.
- Every evaluation runs the ADR-0182 count query per service with a release in the window, bounded by the `Query__*`
  caps; use a long `EvaluationIntervalSeconds` on instances with many services.
- The notification does not name the release, only the service scope and count; the Releases page shows which.
- Overloading `ExceptionCondition` is a shortcut; if the condition grows its own settings (a minimum occurrence count, a
  type allow-list) it should become its own condition type with a migration.
