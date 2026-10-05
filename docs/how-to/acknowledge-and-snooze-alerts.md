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
