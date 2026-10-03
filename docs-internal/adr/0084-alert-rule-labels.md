# ADR-0084: User-defined alert rule labels and label-scoped maintenance windows

Status: Accepted

Date: 2026-10-03

## Context

A maintenance window (ADR-0055) covered either every rule or an explicit list of
rule ids. A rule created after the window was saved was never covered by a
"payments team" window, and picking rules by name doesn't scale past a few
dozen. Rules also had no user-set labels: `{{labels.<key>}}` in a notification
template (ADR-0052) only reflects the rule's own filter (`service.name`, equality
attribute filters), so a team or environment tag had nowhere to live. Prior art:
[signoz#11186](https://github.com/SigNoz/signoz/commit/6cf22e98ddc86f1f1a28eaebbd4d1cab2007a252).

## Decision

**`AlertRule.Labels` (key/value map) and `MaintenanceWindow.LabelMatchers` (key/value
map).** Both are stored as a JSON object in a `String` column
(`alert_rules.LabelsJson`, `maintenance_windows.LabelMatchersJson`, migration 0042,
default `'{}'`), the same shape as `ConditionJson`. Existing rules and windows are
unchanged.

- **Validation** (`AlertLabels`): at most 20 labels; keys 1-64 characters matching
  `[A-Za-z_][A-Za-z0-9_.-]*` (so `service.name` is fine); values 1-200 characters.
  Values are trimmed on save.
- **Coverage.** A window with neither `RuleIds` nor `LabelMatchers` covers every rule
  (as before). Otherwise it covers a rule that is listed by id **or** whose labels
  contain **every** matcher pair, with exact, case-sensitive equality
  (`MaintenanceWindowSchedule.Covers`). Union, not intersection: the two are
  alternative ways to pick rules, and a rule matched by either is silenced.
- **Templates and payloads.** User labels are merged into the
  `{{labels.<key>}}` values and win over a same-named condition-derived label.
  Webhook and PagerDuty payloads gain a `labels` object (empty when none).
- **Dashboard.** The rule form and the window form take labels as one
  `key=value, key=value` field. The rules list shows each rule's labels as
  badges and filters by them (`?label=team=payments`, rule must carry every
  pair). The "Muted" badge uses the same coverage rule as the server.

## Consequences

- Matching is exact equality only: no regex, no `!=`, no prefix. Enough for
  `team=`/`env=` scoping; richer matchers can be added later without a schema
  change since the matcher column is a JSON object.
- A rule's labels are read when the worker loads the rules each tick, so editing
  a label takes effect on the next evaluation, including for an already-active
  window.
- Label-based channel routing and a per-label alert-history filter are not part
  of this change; labels are now available for them.
