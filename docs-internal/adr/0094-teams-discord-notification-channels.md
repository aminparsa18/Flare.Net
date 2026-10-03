# ADR-0094: Microsoft Teams and Discord notification channels

Status: accepted

## Context

Notification channels (ADR-0021) were Webhook, Telegram, Email and PagerDuty. The generic
webhook's top-level `text` works for Slack, but Microsoft Teams Workflows webhooks only accept
a `message` envelope carrying an Adaptive Card, and Discord requires a `content` field, so
neither worked through the generic type.

## Decision

Add `Teams` and `Discord` to `NotificationChannelType`, appended after `PagerDuty` (MemoryPack
encodes the enum as its integer). Each has its own notifier (`TeamsAlertNotifier`,
`DiscordAlertNotifier`) dispatched by `CompositeAlertNotifier`, so send-test, per-rule fan-out,
resolved notifications (ADR-0064) and custom templates (ADR-0052) work without further wiring.

- **Destination is the existing `WebhookUrl` field.** Both services are addressed by one URL,
  so no new column and no migration; `Type` is a `LowCardinality(String)` and stores the new
  names as-is. `ValidateDestination` requires `webhookUrl` and rejects other fields, as for Webhook.
- **Teams** sends a `message` with one Adaptive Card: optional bold title block, text block,
  and `Action.OpenUrl` buttons for the rule and the fired data (so the built-in text drops its
  trailing link lines). Teams answers 202.
- **Discord** sends `content` (truncated to the 2000-character API limit) with
  `allowed_mentions.parse = []`, because alert text carries untrusted service/log strings that
  must not be able to ping `@everyone`.
- Custom templates render as plain text on both: Adaptive Card `TextBlock` and Discord each
  interpret their own Markdown dialect, and mapping ADR-0090's subset to them is not worth it yet.
- Slack and Google Chat keep using the plain Webhook type.

## Consequences

Failures surface the service's response body (capped at 300 characters) in the history entry's
error, via the shared `WebhookPost` helper. Not verified against live Teams/Discord endpoints in
CI; payloads are unit-tested only.
