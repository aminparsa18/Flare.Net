# ADR-0125: Alert escalation

Status: Accepted

Date: 2026-10-05

## Context

ADR-0124 records who is handling a firing alert. Nothing yet acts on an alert that nobody
picks up: the same channel is re-notified each cooldown, however long it goes unanswered.
The roadmap item asks for "not acked after 15 minutes, notify channel B". On-call rotations,
which would choose that target channel, are a further layer and stay out of this change.

## Decision

- **Two per-rule fields:** `EscalateAfterMinutes` (0 = off, the default; max 10080) and
  `EscalationChannelIds` (notification channels, at least one when on, no repeats). Migration
  0054, plus the cluster variant. They are appended to `AlertRule` and `AlertRuleRequest`.
- **One escalation per incident.** `AlertEscalationPolicy.IsDue` is true when the rule has
  escalation on, someone was actually paged (`AlertFiringState.Notified`), the incident first
  notified at least `EscalateAfterMinutes` ago, it has not escalated, and it is not
  acknowledged. A rule stuck unacknowledged for a day escalates once, not every cooldown.
- **Only an ack stops it.** A snooze mutes re-notifications to the first channel, but the
  incident is still unowned, so it still escalates. A `Clear` makes it eligible again.
- **State is derived.** The escalation is an `alert_events` row with `Escalated = 1`
  (migration 0054 adds the column). `GetFiringStatesAsync` reads the incident's first
  notified fire and its escalation time (`IncidentNotifiedAt`, `EscalatedAt`), counting
  only events after the latest resolution. Escalation rows are excluded from "last fire",
  so they do not reset the cooldown or the firing check.
- **Worker.** `AlertEvaluationWorker.EscalateIfDueAsync` runs for a breached rule before the
  ack and cooldown early returns, because those silence re-notifications, not escalation.
  It sends to the escalation channels only, not the rule's own. A maintenance window defers it.
  The send renames the rule `[Escalated] <name>` so the receiver can tell it from the first
  page without a new flag through every notifier. PagerDuty and Jira dedupe on the rule id,
  so they see the same incident.
- **Resolution** is unchanged: it goes to the rule's own channels (ADR-0064). Escalation
  channels are not sent "Resolved".
- **Dashboard:** a "Escalate if not acknowledged" switch, minutes and channel picker in the
  rule form (channel-based rules only; legacy inline-channel rules cannot escalate).

## Consequences

- A rule that stops being breached (including the ADR-0076 hysteresis band) does not
  escalate; the check sits after the breach decision.
- Escalation is not exported or imported with a rule: channel ids are per instance, and
  export only carries channel names for the primary channels.
- Updating a rule from a client that does not send these fields turns escalation off, like
  the other optional request fields that default on update.

## Not decided here

- **On-call rotations** choosing the escalation target.
- **Multi-step policies** (escalate again after a further delay).
- **Ack from the notification** (Slack button, PagerDuty ack sync) and a `flare alerts ack` command.
