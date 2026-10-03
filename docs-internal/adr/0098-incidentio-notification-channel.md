# ADR-0098: incident.io notification channel

Status: accepted

## Context

incident.io ingests alerts through an HTTP alert source (Alert Events V2): a per-source URL plus
a bearer token. The generic webhook can't send the token or the required event shape.

## Decision

Add `IncidentIo` to `NotificationChannelType`, appended after `Jira`, with
`IncidentIoAlertNotifier` dispatched by `CompositeAlertNotifier`.

- **Destination** is the alert source URL in the existing `WebhookUrl` plus a new
  `IncidentIoToken` field (appended to `NotificationChannel` and `NotificationChannelRequest`;
  migration 0045, `String DEFAULT ''`). `ValidateDestination` requires both, an absolute http(s)
  URL, and rejects other destination fields (and the token on other types).
- **Payload**: `deduplication_key` (`flare-alert-<ruleId>`), `title`, `description` (the alert
  text, capped at 100,000 characters, well under incident.io's 512 KB limit), `status`
  (`firing`, or `resolved` on recovery, ADR-0064), `source_url` (the rule link) and `metadata`
  (rule labels plus `rule` and `severity`, so they can be mapped to incident.io attributes).
- Firing and resolved share the key, so a resolve closes the alert the fire opened; incident.io
  drops a duplicate firing event while one is open, so re-fires after cooldown are no-ops
  there. A test send uses a one-off key and always reports `firing`.
- Auth reuses `WebhookPost` with a new optional bearer token, so failures surface the response
  body in the history entry like Teams/Discord.
- The token is stored and returned like the other channel secrets; the dashboard input is masked.

## Consequences

Updated values aren't appended to an open incident.io alert (unlike Jira comments); the
description links back to Flare. Not verified against a live incident.io account; the payload is
unit-tested only.
