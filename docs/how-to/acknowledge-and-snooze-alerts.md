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

## Acknowledge from the notification

With `Alerting:PublicUrl` set, every notification for a firing or escalated alert
carries an **Acknowledge** link, also available to templates as `{{ack_url}}`
(Teams shows it as a button, the generic webhook as `ackUrl`). Opening it shows
the rule and an **Acknowledge** button; nothing happens until you press it, so
mail scanners and chat previews can't acknowledge an alert by fetching the link.
You don't need to be signed in: the link itself is the credential, and the
acknowledgement is recorded as `notification link` (or as your name, if you have
a session).

A link works for the incident it was sent for and expires after 24 hours
(`Alerting:AckLinkLifetimeHours`); each notification carries a fresh one. If the
alert has resolved in the meantime, the page says so.

Why it works this way: [ADR-0127](../../docs-internal/adr/0127-alert-ack-link.md).

## Acknowledge from Slack

A Slack channel can show an **Acknowledge** button under the alert. It needs a
Slack app, because Slack only delivers button clicks to an app:

1. Create a Slack app, add an incoming webhook to it and use that webhook URL in a
   notification channel (a `hooks.slack.com` URL, as before).
2. Under **Interactivity & Shortcuts**, turn interactivity on and set the Request URL
   to `https://<your-api-host>/api/alerts/slack-interactivity`. This is the address of
   Flare.Api, which Slack must be able to reach.
3. Copy the app's **Signing Secret** into `Alerting:SlackSigningSecret` on both
   Flare.Api and Flare.AlertWorker (`Alerting__SlackSigningSecret` as an environment
   variable). The worker only uses it to decide whether to add the button.

With the secret set, a firing notification to a Slack webhook carries the button.
Pressing it acknowledges the incident, recorded as `Slack: <username>`, and posts
"acknowledged by" in the channel. Flare checks Slack's request signature and
refuses anything older than five minutes. Without the secret the button is not
sent and the endpoint answers 404. A button on an old message that has expired or
whose alert has resolved replies only to the person who pressed it.

## Acknowledge from PagerDuty

If a rule notifies a PagerDuty channel, acknowledging the incident in PagerDuty
can acknowledge it in Flare too:

1. In PagerDuty, add a **V3 webhook subscription** (Integrations > Generic
   Webhooks) with the URL `https://<your-api-host>/api/alerts/pagerduty-webhook` and the
   events `incident.acknowledged` and `incident.unacknowledged`.
2. Copy the subscription's secret into `Alerting:PagerDutyWebhookSecret` on Flare.Api.

An acknowledge in PagerDuty is recorded in Flare as `PagerDuty: <name>`, so
re-notifications and escalation stop. If PagerDuty's acknowledgement times out or is
withdrawn, Flare clears the acknowledgement it recorded from PagerDuty, and leaves
one made in Flare alone. Only incidents Flare opened are matched, by their
`flare-alert-...` incident key. The sync goes one way: acknowledging in Flare does
not change the PagerDuty incident.

Why it works this way: [ADR-0138](../../docs-internal/adr/0138-alert-ack-slack-pagerduty.md).

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

## Escalate again if it is still unacknowledged

In the same escalation settings, turn on **Then escalate again**, set a further delay in minutes and pick the channels. If nobody has acknowledged the incident that long after the first escalation, Flare sends it once more to those channels, again with `[Escalated]` in front of the rule name. Over the API these are `secondEscalateAfterMinutes` (1 to 10080, 0 means no second step) and `secondEscalationChannelIds`. The second step needs the first one, and an acknowledge stops both. The on-call rotation applies to the first step only.

Why it works this way: [ADR-0136](../../docs-internal/adr/0136-alert-multi-step-escalation.md).

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
their fixed channels only.

To swap a shift once ("Priya covers Tuesday"), add an **override** in the rotation:
a channel and a start and end time. While it is in effect that channel is on call
instead of the scheduled participant, escalations go to it, and the page marks it
as an override. After it ends the schedule carries on unchanged. If overrides
overlap, the one that started last wins. Over the API they are `overrides`
(`channelId`, `startsAt`, `endsAt`) on the rotation.

To page only at set times ("business hours only"), turn on **Coverage** in the
rotation: weekdays, a start and end time, and a time zone. Outside the window nobody
on the rotation is paged, but the rule's fixed escalation channels and any override
still are, and the table shows "Outside coverage". An end earlier than the start runs
past midnight. If a rule must always reach someone, give it a fixed escalation
channel as a backstop. Over the API it is `coverage` (`timeZone`, `days` with 0 for
Sunday, `startMinute`, `endMinute`) on the rotation.

Why it works this way: [ADR-0126](../../docs-internal/adr/0126-alert-oncall-rotations.md).
