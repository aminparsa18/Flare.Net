# ADR-0077: Alert rule severity

Status: Accepted

Date: 2026-10-02

## Context

Alert rules had no notion of urgency. `PagerDutyAlertNotifier` hard-coded
`severity = "critical"` on every trigger, so a low-traffic warning and a
production outage paged identically, and the other channels' messages and the
alerts list gave no hint either. Prior art:
[signoz#9538](https://github.com/SigNoz/signoz/commit/9f089e0784eee3b0aec817a82fa55c7411f8e8c0).

## Decision

**A per-rule `AlertRule.Severity` enum: `Critical`, `Error`, `Warning`, `Info`.**

- `Critical` is the zero value and the column default (`alert_rules.Severity
  LowCardinality(String) DEFAULT 'Critical'`, migration 0039). That is exactly
  what every existing rule was sending to PagerDuty, so nothing changes until a
  rule opts into something else. MemoryPack serializes enums by number, so
  the intended default also has to be member 0.
- The names are PagerDuty Events API v2's `severity` values capitalized, so the
  PagerDuty mapping is a lowercase. A test send still reports `info`, as before,
  so it never looks like a real page.
- Other channels: a fired, no-data or anomaly text for a non-Critical rule is
  prefixed `[WARNING]` etc. Critical stays unlabeled so existing messages are
  byte-for-byte unchanged. Resolved and test texts are not tagged.
- Templates get a `{{severity}}` placeholder (lowercase), and PagerDuty's
  `custom_details` carries it.
- The alerts list has a Severity column, and the rule form a select.
- Severity is metadata only. It doesn't change evaluation, cooldown or routing.

## Alternatives considered

- **Per-channel severity or routing by severity.** More powerful, but a rule
  already picks its channels, and routing is a separate feature.
- **Always label the text, Critical included.** Consistent, but rewrites every
  existing notification.
- **Free-form string.** PagerDuty rejects values outside its four, so an enum
  moves that failure to validation time.

## Consequences

- One additive migration, `ReplicatedMergeTree` variant included.
- `AlertRule`/`AlertRuleRequest` gain a trailing field (MemoryPack
  versioning-safe) and the TS companions are updated by hand.
- `{{severity}}` is added to `AlertTemplateRenderer.Names`, so a rule saved
  with a template using it now validates.
