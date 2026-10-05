# How to acknowledge or snooze a firing alert

A rule that stays breached notifies again after every cooldown period. When
someone is already working on it, those repeats are noise. Acknowledging or
snoozing the alert tells Flare to stop sending them.

## Acknowledge

On the **Alerts** page, a firing rule has an acknowledge button (a double
check mark) in its row. Open it, optionally add a note, and choose
**Acknowledge**. The rule's status shows who acknowledged it.

An acknowledgement lasts for the rest of that incident:

- No further "fired" notifications are sent while the rule stays breached.
- When the condition recovers, the **resolved** notification is still sent, so
  everyone who was paged hears it is fixed.
- If the rule later fires again as a new incident, the old acknowledgement does
  not apply.

## Snooze

In the same popover, choose 15 minutes, 1 hour, 4 hours or 1 day to silence
repeat notifications until then. The row shows when the snooze ends. If the
rule is still breached once the snooze expires, it notifies again on the next
evaluation after its cooldown.

## Clear it

While a rule is acknowledged or snoozed, the same button becomes **Clear
acknowledgement**. Clearing it lets the next breach notify normally.

## Use the CLI

```bash
flare alerts ack <rule-id> --note "Looking into the checkout DB"
flare alerts snooze <rule-id> --minutes 60
flare alerts unack <rule-id>
```

Find the id with `flare alerts list`. Each command exits with 1 if the rule
doesn't exist or isn't firing.

## Use the API

All three calls need write access to the rule's project and return `409` when
the rule is not firing.

```bash
# Acknowledge, with an optional note
curl -X POST -H 'Content-Type: application/json' \
  -d '{"note":"Looking into the checkout DB"}' \
  http://localhost:5080/api/alerts/<rule-id>/ack

# Snooze for 60 minutes (1 to 10080)
curl -X POST -H 'Content-Type: application/json' \
  -d '{"snoozeMinutes":60}' \
  http://localhost:5080/api/alerts/<rule-id>/snooze

# Clear
curl -X DELETE http://localhost:5080/api/alerts/<rule-id>/ack
```

`GET /api/alerts/states` includes an `ack` object (who, when, kind, snooze end,
note) for each acknowledged or snoozed rule. When Flare's authentication is off,
the "who" is empty.

Why it works this way: [ADR-0124](../../docs-internal/adr/0124-alert-acknowledgement-and-snooze.md).

## Escalate if nobody acknowledges

A rule can send an unacknowledged incident to a second set of channels. In the
rule form, turn on **Escalate if not acknowledged**, set the delay in minutes
and pick the channels. Over the API these are `escalateAfterMinutes` (1 to
10080, 0 turns it off) and `escalationChannelIds`.

Once an incident has been notified for that many minutes without an acknowledge,
Flare sends it once to the escalation channels, with `[Escalated]` in front of
the rule name, and records it in the rule's history. A snooze does not stop
this, only an acknowledge does. A maintenance window delays it. The "Resolved"
message still goes to the rule's own channels only.

Escalation needs channels from **Notification channels**, so it is not available
on rules that still use an inline webhook or e-mail address.

Why it works this way: [ADR-0125](../../docs-internal/adr/0125-alert-escalation.md).

## Escalate to whoever is on call

An on-call rotation is a list of notification channels that take turns, each for
a fixed shift. Create one under **Settings > Workspace > On-call rotations**:
pick the channels in shift order (one per person or team), the shift length in
hours (a day is 24, a week is 168) and when the first shift starts. The first
channel is on call from that moment, the next takes over after one shift, and
the list repeats after the last. The page shows who is on call now and until
when.

Then, in a rule's escalation settings, pick the rotation. When the incident
escalates, Flare sends it to the channel on call at that moment, plus any
escalation channels the rule also lists. Over the API this is
`escalationRotationId` on the rule, and the rotations themselves are under
`/api/oncall-rotations` (`channelIds`, `shiftHours`, `startsAt`).

A rotation only chooses the escalation target. The first notification still goes
to the rule's own channels. Deleting a rotation leaves its rules escalating to
their fixed channels only. There are no one-off overrides yet; to swap a shift,
edit the participant list.

Why it works this way: [ADR-0126](../../docs-internal/adr/0126-alert-oncall-rotations.md).
