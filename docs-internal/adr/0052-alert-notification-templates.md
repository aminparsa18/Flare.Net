# ADR-0052: Custom alert notification templates

Status: Accepted

Date: 2026-09-25

## Context

Every notification's wording was fixed by `AlertMessageFormatter.BuildText`:
one line per condition kind, followed by the matching-logs and rule links.
Teams routing alerts into shared channels want their own format: a severity
tag in the title, the service name up front, a runbook link, or just a
shorter line for a phone notification. Prior art:
[signoz#6282](https://github.com/SigNoz/signoz/commit/68d25a89894e141f208b62ca3c2c220a37cbc013)
added per-rule title/body templates with `$labels.*`-style variables.

## Decision

Two optional per-rule strings, `AlertRule.NotificationTitleTemplate` and
`NotificationBodyTemplate` (migration `0030_alert_notification_templates.sql`,
`String DEFAULT ''`). Empty keeps the built-in wording, so every existing
rule behaves exactly as before.

- **Substitution, not a template engine.** `AlertTemplateRenderer` replaces
  `{{name}}` tokens with one regex. It has no conditionals, loops, filters,
  expressions or escaping. A template can only read the fixed value set
  `AlertMessageFormatter.BuildTemplateValues` builds. That makes it safe to
  let any rule editor write one, and nothing new needs sandboxing.
- **A fixed name set plus `labels.<key>`.** The names are `rule_name`,
  `rule_id`, `description`, `status`, `condition_kind`, `value`, `threshold`,
  `comparator`, `window`, `window_seconds`, `metric`, `exception_type`,
  `baseline_mean`, `z_score`, `fired_at`, `rule_url`, `logs_url` and
  `message`. `message` is the built-in text without its links, so a template
  can wrap the default rather than rebuild it. Numbers are formatted the same
  way the built-in text formats them, including unit scaling for metrics.
- **Labels are the rule's own scope.** Alert rules evaluate one aggregate
  series and have no group-by, so there are no per-series group labels to
  expose. `{{labels.<key>}}` instead reads the condition's equality filters:
  `service.name` from the service filter, log attribute filters using
  `Equals`, and metric attribute filters. Dotted keys work because a name is
  matched as `[A-Za-z0-9_.-]+` rather than split on dots. A label the rule
  isn't scoped by renders as empty.
- **Unknown placeholders are rejected on save** (`AlertRuleRequest.ValidateTemplates`,
  called from `ValidateCondition`). A typo is far more likely than an
  intentional literal `{{x}}`, and it's better found in the form than in a
  real incident. The title is capped at 256 characters and the body at
  2,000, which leaves room under Telegram's 4,096-character message limit.
- **The body replaces the whole text, links included.** Appending the link
  lines after a custom body would defeat the point for users who want a
  short message. Anyone who wants the links places `{{rule_url}}` or
  `{{logs_url}}` themselves.
- **The title maps onto each channel's own subject field.** It is Email's
  subject and PagerDuty's `summary`, with the full text moving to
  `custom_details.message`. On webhook/Slack and Telegram, which have no
  subject field, it becomes the first line of the text. The generic webhook
  payload also gets a `title` field.
- **Telegram sends a custom message as plain text.** The built-in wording
  keeps `parse_mode: Markdown`. User-written text, or a rendered value such
  as a service name containing `_`, can't be guaranteed to be valid
  Telegram Markdown, and an invalid message fails the whole send.
- **Test sends use the template.** The template is what's being tested, but
  `[Test] ` is prefixed to the title (or to the body when there is no title)
  so nobody mistakes the message for a real incident.
- **The preview is rendered on the server.** `POST /api/alerts/notification-preview`
  builds the draft rule exactly as the draft send-test does (`ToDraftRule`)
  and renders it with illustrative values: a value sitting on the threshold,
  or a made-up baseline for anomaly rules. It never sends and never queries
  ClickHouse, so the form can call it on every debounced edit. The dashboard
  therefore has no second renderer that could drift from the real one.
  Template errors come back in the response body, so the form can show them
  next to the preview instead of treating them as a failed request.

## Alternatives considered

- **A real template engine (Scriban, Handlebars.Net, Go-template style).**
  Conditionals and loops would be nice, but they bring a dependency, a larger
  attack surface for anyone who can edit rules, and an expression language to
  document. Plain substitution covers the roadmap item's ask. An engine can
  be added later as an additive change.
- **Per-channel templates** (one for Slack, one for email, and so on). That
  multiplies the form and the storage for a need nobody has raised. One
  title and body mapped onto each channel's natural fields is enough.
- **Keeping the links appended after a custom body.** Rejected for the
  reason above; the placeholders make this an explicit choice for the author.
- **Leaving unknown placeholders as literal text at save time.** Rejected
  because typos would ship silently. At render time an unknown name is still
  left verbatim, which only matters if a future version removes a name that
  a saved rule uses.

## Consequences

- Rule create/update requests gain two optional fields. The hand-written
  MemoryPack companions (`$lib/memorypack/AlertRule.ts`, `AlertRuleRequest.ts`)
  append them after `minDataPoints`, so older payloads still deserialize.
- `IAlertNotifier` implementations call `AlertMessageFormatter.BuildMessage`
  instead of `BuildText`. `BuildText` remains the built-in wording.
- The preview endpoint takes an optional `?ruleId=`, which the form sends
  when editing a saved rule, so `{{rule_id}}`/`{{rule_url}}` preview the
  real rule. A new draft has no ID yet and previews the empty GUID.
