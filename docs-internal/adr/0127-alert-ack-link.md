# ADR-0127: Acknowledge from the notification

Status: Accepted

Date: 2026-10-06

## Context

Acknowledging (ADR-0124) means opening the dashboard, or running `flare alerts ack` with a
rule id. A responder who gets paged on a phone has neither at hand, and escalation
(ADR-0125) fires precisely because nobody got that far. The roadmap item asks for ack from the
notification itself: a signed link, a Slack button, PagerDuty ack sync.

Slack buttons and PagerDuty ack sync both need an inbound integration (a Slack app with a
request-signing secret and a public interactivity URL; a PagerDuty webhook subscription), which
Flare has no per-instance setup for. A signed link needs only what a notification already has,
`Alerting:PublicUrl`, so it ships first and works in every channel.

## Decision

- **`{{ack_url}}` placeholder and an `Acknowledge:` line.** `Alerting:PublicUrl` plus `/ack?token=...`.
  The built-in text appends the line (after the data link, before the rule link) for a real
  firing or escalation send, not for a test or a resolved message. Teams gets an `Acknowledge`
  card action, the generic webhook gets an `ackUrl` field, PagerDuty carries it in
  `custom_details.ackUrl`. Without a public URL there is no link, as for the other links.
- **The token is ASP.NET Core Data Protection output**, time-limited, purpose
  `Flare.AlertAckLink.v1`, carrying the rule id and the time it was issued. It is
  authenticated and encrypted, so it cannot be forged or edited, and needs no new secret.
  `Flare.AlertWorker` signs it and `Flare.Api` redeems it, so the worker now registers Data
  Protection with the same application name and Redis key ring as Flare.Api. Lifetime is
  `Alerting:AckLinkLifetimeHours`, default 24; every notification carries a fresh link, so it only
  has to outlast the gap between two.
- **A link works for the incident it was sent for.** Redeeming it fails with 409 when the rule is
  not firing, or when the link was issued before the rule's latest resolution. An old message
  cannot acknowledge a later, unrelated incident.
- **A GET never acknowledges.** Mail scanners and chat unfurlers fetch every URL they see. The
  link opens a dashboard page, `/ack`, which calls `GET /api/alerts/ack-link` to show the rule's
  name and current state, and only the button calls `POST /api/alerts/ack-link`. Both are
  anonymous, mapped like `/api/auth/set-password`; the dashboard lists `/ack` as a public route.
- **Who acknowledged.** `AckedBy` is the signed-in user's name when the browser has a session, and
  `notification link` otherwise. The result is an ordinary `Ack` row (ADR-0124), so silencing,
  escalation and the rules table treat it like any other ack.
- **Only an ack.** The link does not snooze or clear.

## Consequences

- The link is a bearer credential for one ack of one incident: anyone who receives the
  notification can use it. That is the same audience that could open the rule in the dashboard.
  It grants nothing else, and expires.
- Rotating or losing the Data Protection key ring invalidates outstanding links; the next
  notification issues a working one. Changing the purpose suffix does the same on purpose.
- With several Flare.Api replicas or a separate worker the key ring must be shared. It already is
  (Redis); the worker previously had no Data Protection registration and now needs the same
  `redis` connection Flare.Api uses.
- No new table or migration.

## Not decided here

- **Slack button and PagerDuty ack sync** (inbound integrations, needing per-instance setup).
- **Multi-step policies** and **rotation overrides or time-of-day restrictions**, still on the roadmap.
