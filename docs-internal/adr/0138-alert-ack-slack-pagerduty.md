# ADR-0138: Acknowledge from Slack and PagerDuty

Status: Accepted

Date: 2026-10-06

## Context

ADR-0127 shipped a signed acknowledge link and left two integrations open: a Slack button and
PagerDuty ack sync. Both are inbound. Slack delivers a button click only to the interactivity URL
of a Slack app, and PagerDuty delivers acknowledgements only to a webhook subscription. Flare has
no per-instance place to register either, so each needs a secret an operator copies in.

## Decision

- **Two anonymous endpoints on Flare.Api**, mapped like `/api/alerts/ack-link`:
  `POST /api/alerts/slack-interactivity` and `POST /api/alerts/pagerduty-webhook`. The request
  signature is the credential. Each answers 404 while its secret is unset, so a deployment that
  does not use the feature exposes nothing, and 401 for a bad signature. Bodies over 64 KB are refused.
- **Config** in the existing `Alerting` section (`AlertLinkOptions`): `SlackSigningSecret` and
  `PagerDutyWebhookSecret`. Plain options rather than a settings page or a table, like
  `PublicUrl`; a UI for them can follow if operators ask.
- **Slack button.** For a real firing send to a `hooks.slack.com` webhook, when
  `SlackSigningSecret` is set, the webhook payload gains `blocks`: the message as a mrkdwn
  section and an Acknowledge button. `text` stays as the fallback. The button's value is the
  signed ack token from ADR-0127, so a click is redeemed with the same rules (expiry, incident
  staleness) as the link. Flare.AlertWorker needs the same secret, only to decide whether to add
  the button. The request is verified with Slack's v0 scheme (HMAC-SHA256 of
  `v0:{timestamp}:{body}`) and refused if its timestamp is more than five minutes off. The result
  is an ordinary `Ack` row with `AckedBy` = `Slack: <name>`. Flare answers through the payload's
  `response_url` (accepted only for `https://hooks.slack.com` and `hooks.slack-gov.com`): an
  in-channel "acknowledged by" on success, an ephemeral message otherwise. A repeat click on an
  already acknowledged incident writes nothing.
- **PagerDuty sync.** A V3 webhook subscription for `incident.acknowledged` and
  `incident.unacknowledged`. The signature is `X-PagerDuty-Signature` (`v1=` HMAC-SHA256 of the
  raw body, several values while a secret rotates). The rule is found from the incident's
  `incident_key`, which for Events API v2 is the `dedup_key` Flare already sends
  (`flare-alert-<rule id>`); incidents without that key (test sends, other integrations) are
  ignored. An acknowledge writes `Ack` with `AckedBy` = `PagerDuty: <agent>`. An unacknowledge
  (PagerDuty's own ack timeout, or a manual undo) writes `Clear` only when the current ack came
  from PagerDuty, so an ack made in Flare is never undone by PagerDuty. Every authentic request
  gets 200, whatever it holds, so PagerDuty does not retry or disable the subscription.
- **Shared resolution.** `AlertAckResolver` finds the live incident for both the signed link and
  these endpoints, so "only the firing incident" is one rule.
- **One direction.** Flare does not acknowledge the PagerDuty incident when a person acknowledges
  in Flare, and does not edit the Slack message.

## Consequences

- The Slack button needs a Slack app and a publicly reachable Flare.Api. Without them nothing
  changes: the link from ADR-0127 still works in every channel.
- A bearer of the signing secret can forge acknowledgements, as with any webhook secret. The
  button itself is as powerful as the link: whoever sees the message can use it.
- The Slack token rides in the button value and never leaves Slack's own payloads.
- `incident_key` is read from the V3 incident payload; if PagerDuty stops sending it, sync
  silently ignores events and the link and dashboard acks keep working.
- No table or migration.

## Not decided here

- **Updating the Slack message** to show who acknowledged, and a snooze button.
- **Flare to PagerDuty** acknowledgement.
- Other chat platforms (Teams buttons need a bot registration).
- A dashboard settings page for the two secrets.
