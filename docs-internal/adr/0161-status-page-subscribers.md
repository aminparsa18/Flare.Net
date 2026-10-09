# ADR-0161: Status page subscribers

Status: Accepted

Date: 2026-10-09

## Context

Incidents (ADR-0159) reach people only if they open the page. Operators want the page's announcements pushed
to where they already are: a Slack channel, a Telegram group, a mailing list. Flare already has saved
notification channels (alerting) that know how to deliver to all of those.

## Decision

- **A status page subscribes saved notification channels.** `status_pages` gains `SubscriberChannelIds
  Array(UUID)` (migration 0075). Opening an incident, or posting an update to one, sends a message to each.
  The field is `subscriberChannelIds` on the page API; on update, omitting it leaves the subscribers alone and
  an empty list clears them, so older clients that PUT a page without it do not wipe them.
- **Only announcement channels qualify**: webhook (Slack included), Telegram, email, Teams and Discord. A
  PagerDuty, Jira, incident.io or JSM Ops subscriber would page someone or open a ticket for a public
  message, so the API answers 400. Channels must exist; at most 20 per page.
- **Delivery reuses the alert notifiers.** The incident is dressed as an `AlertRule` whose title and body
  templates are literal text (`[Page] Status: Incident title`; then the update text, the affected
  components and `{PublicUrl}/status/{slug}`), sent through `CompositeAlertNotifier.SendAllAsync`. No
  channel needed incident-specific code. `{{` in admin text is broken to `{ {` so it is never read as an
  alert-template placeholder.
- **Best effort, after the write.** The incident is saved first; a failing or deleted channel is logged and
  never fails or delays the response beyond the send itself. There is no retry or delivery history.
- **The dashboard** adds an Incident notifications picker to the page editor and the CLI a repeatable
  `--subscriber <channel-id>` on `flare status-pages create` and `update`.

## Alternatives considered

- **Public sign-up (visitor email or webhook).** Rejected for now: it needs address verification,
  unsubscribe links, abuse limits and a sending reputation, none of which Flare has. Channels an admin
  configures are the part that works without them.
- **A new sender per channel type.** Rejected: it would duplicate the alert notifiers' formatting, secrets
  handling and retry behaviour.
- **Sending from a background worker.** Rejected: the volume is a few messages per incident, and doing it
  inline means a send failure is visible in the API log next to the write that caused it.

## Consequences

- A resolved update is announced like any other; there is no separate "resolved" template.
- Deleting a channel leaves its id on the page; it is skipped at send time and shown as "(deleted channel)"
  in the editor until removed.
- The link in the message needs `Alerting__PublicUrl`; without it the message has no link.

## Not decided here

- Public visitor subscriptions (email or webhook sign-up), digest messages, and delivery history.
