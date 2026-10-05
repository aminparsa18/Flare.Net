# ADR-0124: Alert acknowledgement and snooze

Status: Accepted

Date: 2026-10-05

## Context

A firing rule re-notifies once per cooldown for as long as it stays breached (ADR-0064 added
the matching "resolved" message). Nothing records that a person is already on it, so a
15-minute cooldown on a two-hour incident pages the channel eight times. The roadmap item
"Alert acknowledgement, escalation and on-call" has three parts: ack/snooze, escalation
policies, and on-call rotations. Escalation needs to know whether an incident was acked, and
rotations choose the escalation target, so ack/snooze ships first and alone.

## Decision

- **Append-only `alert_acknowledgements` table** (migration 0053, plus the cluster variant):
  `RuleId, AckedAt, AckedBy, Kind, SnoozedUntil, Note`, a plain `MergeTree` ordered by
  `(RuleId, AckedAt)`. `Kind` is `Ack`, `Snooze` or `Clear`. It is a log, not a state row, so
  the history of who handled an incident is kept.
- **State is derived, not stored**, same reasoning as ADR-0064. A rule's ack is its newest
  row, and it applies to the current incident only: a row at or before the rule's latest
  resolution is ignored, and a `Clear` row yields no ack. `AlertAckPolicy.Effective` is that
  rule. `IAlertQueryService.GetFiringStatesAsync` and `GetRuleStatusesAsync` attach it, so
  an ack is read in one extra grouped query per tick, only for rules that are firing.
- **An ack or an unexpired snooze silences re-notifications.** In
  `AlertEvaluationWorker`, a breached rule whose `AlertAckPolicy.Silences` is true returns
  before notifying, like the cooldown skip, and records nothing. The resolution is still
  sent: the people who were paged earlier are owed it (ADR-0064). When a snooze expires and
  the rule is still breached, it notifies on the next evaluation past the cooldown.
- **Ack is not stored on `alert_events`.** The roadmap sketch said "shown in `alert_events`";
  a separate table avoids widening that table again and lets a clear/re-ack not look like a
  fire. The rules list gets the state from `GET /api/alerts/states`, which now carries `ack`.
- **API:** `POST /api/alerts/{id}/ack` (optional `note`), `POST /api/alerts/{id}/snooze`
  (`snoozeMinutes`, 1 to 10080, optional `note`), `DELETE /api/alerts/{id}/ack`. All need
  write access to the rule's project, and return 409 when the rule isn't firing. `AckedBy` is
  `User.Identity.Name`, empty when Flare's opt-in auth is off.
- **Dashboard:** a Check-check popover on firing rows in the alerts table (note, Acknowledge,
  15m/1h/4h/1d snooze), a badge showing who or until when, and a Clear button.

## Not decided here

- **Escalation policies** ("not acked after N minutes, notify channel B") will read the same
  derived ack, and only a `Kind = Ack` stops them, not a snooze. They need a policy table and
  a worker pass that runs even while the rule is silenced, so they get their own ADR.
- **On-call rotations** are a further layer that picks a channel for an escalation step.
- **Ack from the notification itself** (Slack button, PagerDuty ack sync) is separate work.
