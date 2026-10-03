# ADR-0099: JSM Ops notification channel

Status: accepted

## Context

Jira Service Management Operations (the former Opsgenie alert product) is a separate surface from
Jira issues (ADR-0097): it has on-call routing and its own alert lifecycle, so teams on JSM Ops
want an alert opened and closed there rather than a ticket.

## Decision

Add `JsmOps` to `NotificationChannelType`, appended after `IncidentIo`, with
`JsmOpsAlertNotifier` dispatched by `CompositeAlertNotifier`.

- **One credential**: the integration API key of a JSM Operations "API" integration, in a new
  `JsmOpsApiKey` field (migration 0046, `String DEFAULT ''`) sent as
  `Authorization: GenieKey <key>`. No site or region setting: requests go to the fixed gateway
  `https://api.atlassian.com/jsm/ops/integration/v2/alerts`. `ValidateDestination` requires the
  key and rejects other destination fields (and the key on other types).
- **Fire**: `POST /v2/alerts` with `alias = flare-alert-<ruleId>` (JSM Ops' dedup key, so a
  re-fire folds into the open alert and no lookup is needed), `message` (first line, capped at
  130), `description` (capped at 15,000), `priority` from severity (Critical P1, Error P2,
  Warning P3, Info P5), `source: Flare`, tags (`flare` plus `key:value` rule labels) and the rule
  name in `details`.
- **Recovery** (ADR-0064): `POST /v2/alerts/{alias}/close?identifierType=alias`. A fire after a
  close opens a new alert.
- A test send uses a one-off alias at P5 and never closes.
- Auth reuses `WebhookPost`, now taking an arbitrary `Authorization` header, so a rejection
  surfaces the response body in the history entry.

## Consequences

The API answers 202 and processes asynchronously, so a bad alias or a closed-by-hand alert is
not reported at send time. Updated values are not appended as notes to an open alert (the
description keeps the first fire). Description and notes are plain text. Not verified against a
live JSM Operations site; the requests are unit-tested only.
