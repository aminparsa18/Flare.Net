# ADR-0148: Shared alert notification templates

Status: Accepted

Date: 2026-10-08

## Context

[ADR-0052](0052-alert-notification-templates.md) put the notification title and
body on each rule. Teams that want the same wording on many rules paste it into
every rule and edit it rule by rule.

## Decision

Named, reusable templates, extending ADR-0052 (nothing in it is superseded).

- **Storage.** A new `alert_templates` table (migration `0070`), the same
  `ReplacingMergeTree(UpdatedAt)` tombstone shape as `maintenance_windows`,
  read through `LatestVersionSql` (ADR-0074). `alert_rules` gains a nullable
  `NotificationTemplateId`. A template has a title, a fired body, an optional
  resolved body, an optional body per `NotificationChannelType`, and an
  `IsDefault` flag.
- **Wording precedence**, decided per field by `AlertTemplateResolver`:
  1. the rule's own inline title/body (ADR-0052 text stays an override);
  2. the template the rule references, else the instance default template;
  3. the built-in wording (`AlertMessageFormatter`), so existing rules are
     unchanged.

  For the body, a resolved send prefers the template's resolved body, a fired
  send prefers the entry for the channel's type, and both fall back to the
  fired body.
- **Applied at send time in `CompositeAlertNotifier`.** It copies the rule with
  its empty title/body filled in, so the formatter and the nine notifiers are
  unaware of shared templates. Editing a template therefore affects the next
  notification of every rule that uses it. A failed template lookup falls back
  to the rule's own wording rather than blocking a page.
- **One default.** Saving a template with `IsDefault` re-inserts the previous
  default as a non-default version.
- **Same validation as ADR-0052.** Each text goes through
  `AlertTemplateRenderer.Validate` with the same length caps. Channel-body keys
  must be `NotificationChannelType` names, and a template needs some text.
- **Delete is refused (409)** while rules reference the template; the message
  lists them. Names are unique, case-insensitive (`NameUniqueness`).
- **Rule save checks the reference exists**, but an unchanged reference to a
  since-deleted template is not rejected, so old rules stay editable. At send
  time a dangling reference falls through to the default.
- **Export/import** carries `templateName`; import resolves it on the target
  instance and reports an unknown name as an error. Audit-log entries come from
  the existing classifier (`alert-template` create/update/delete, ADR-0079).
- **Preview** (`/api/alerts/notification-preview`) applies the picked template
  with no channel type, so it shows the general bodies only.

## Alternatives considered

- **Store the template in the Identity store.** Rejected: alert config lives in
  ClickHouse config tables (ADR-0009), and the notifiers already read it there.
- **Copy template text into the rule on pick.** Rejected: it defeats "edit once".
- **Resolve inside each notifier.** Rejected: nine call sites for one decision.

## Consequences

- `AlertRule` / `AlertRuleRequest` gain a trailing `NotificationTemplateId`; the
  hand-written MemoryPack TS companions are updated to match, since they reject
  unknown trailing members.
- Each notification does one small read of `alert_templates`.
- Not covered: the Terraform provider (separate repo) does not yet manage
  templates, and the CLI has no `flare alert-templates` command.
