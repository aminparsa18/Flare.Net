# ADR-0136: Multi-step alert escalation

Status: Accepted

Date: 2026-10-06

## Context

ADR-0125 escalates an unacknowledged incident once. If the escalation target does not answer either,
nothing further happens. The roadmap item asks for "escalate again after a further delay".

## Decision

- **A second, optional step.** Two per-rule fields, `SecondEscalateAfterMinutes` (0 = none, the
  default; max 10080) and `SecondEscalationChannelIds`. Migration 0061, plus the cluster variant.
  Appended to `AlertRule` and `AlertRuleRequest`. Two steps, not an arbitrary list: it covers
  "on-call, then the manager" without a policy object, and a third step can be added the same way.
- **The delay counts from the first escalation**, not from the incident start, so the first step's
  delay can be changed without shifting the second.
- **State is still derived.** `alert_events.Escalated` already was a `UInt8`; it now holds the step
  number (1 or 2), so rows written by ADR-0125 read as step 1 and no column is added.
  `AlertFiringState.SecondEscalatedAt` is the latest step-2 row of the current incident.
  `AlertEscalationPolicy.NextStepDue` returns 1, 2 or 0, at most one per tick, so a long outage
  cannot send both at once. Step 2 requires step 1 to have fired, the second step to be configured,
  no ack and no active maintenance window; a snooze does not stop it (ADR-0125).
- **Targets.** Step 1 goes to the fixed channels plus the on-call rotation (ADR-0126). Step 2 goes to
  its own channels only; rotations stay a step-1 concept.
- **Send.** Same as step 1: the rule is renamed `[Escalated] <name>`, the ack link is included, and a
  history row is recorded with `EscalationStep`. "Resolved" is unchanged.
- **Dashboard:** a "Then escalate again" switch, delay and channel picker under the escalation
  section. Not exported or imported with a rule, like step 1.

## Consequences

- Updating a rule from a client that omits the new fields drops the second step, like the other
  optional request fields.
- Escalation rows with `Escalated = 2` are excluded from last-fire and cooldown by the existing
  `Escalated = 0` filters.
- Validation: a second step needs a first step, a delay and at least one distinct channel.

## Not decided here

- More than two steps, or per-step rotations.
- One-off rotation overrides and time-of-day restrictions.
- Ack from the notification via a Slack button or PagerDuty ack sync.
