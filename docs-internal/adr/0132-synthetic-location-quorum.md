# ADR-0132: Quorum alerting across synthetic locations

Status: Accepted

Date: 2026-10-06

## Context

ADR-0130 left "down from N of M locations" open and its docs suggested grouping the rule by `location`.
Alert rules have no group-by: a metric rule evaluates one value over everything its filter matches.

## Decision

No new rule type. The `Last` aggregation for Gauge rules (ADR-0049) already takes each series' latest point
and averages across series, and every location is its own series (`location` is a data-point attribute,
ADR-0130). For `synthetic.up`, whose values are 0 and 1, that average is the fraction of locations that see
the monitor up, so a quorum is a threshold on it: below 1 is "any down" (same as `Min`), below 0.51 is "at
least half down", below 0.01 is "all down". The how-to page lists the recipes and removes the incorrect
group-by advice.

## Consequences

- The threshold depends on how many locations report, so adding a location shifts the fractions.
- A location whose worker stopped still contributes its last point while that point is inside the rule's
  window; an absent-data rule catches a silent monitor.
- A true "N of M" count with a per-location breakdown in the notification would need per-series evaluation,
  which no alert kind has today.
