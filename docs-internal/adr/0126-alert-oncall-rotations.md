# ADR-0126: Alert on-call rotations

Status: Accepted

Date: 2026-10-05

## Context

ADR-0125 escalates an unacknowledged incident to a fixed set of channels. Teams that rotate
who is on call end up editing every rule each time the rotation changes. The roadmap item asks
for rotations that choose the escalation target.

## Decision

- **A rotation is a first-class config object.** `oncall_rotations` (migration 0055, plus the
  cluster variant): `ChannelIds` (ordered notification channels, one per person or team, a channel
  may appear twice), `ShiftHours` (1 to 8760) and `StartsAt` (start of the first shift). Same
  CRUD-via-tombstone `ReplacingMergeTree` and `LatestVersionSql` reads as maintenance windows
  (ADR-0055), under `/api/oncall-rotations`, Member/Admin only.
- **Who is on call is computed, never stored.** `OnCallSchedule.Resolve` takes shift
  `floor((now - StartsAt) / ShiftHours)` modulo the participant count; before `StartsAt` the first
  participant is on call. A handover is exact at the boundary, so there is no state to drift, no
  worker to fire handovers, and the dashboard and the worker agree by construction.
- **A rule points at a rotation.** `alert_rules.EscalationRotationId` (nullable). At escalation
  time `AlertEvaluationWorker` resolves the on-call channel and sends to the rule's fixed
  `EscalationChannelIds` plus that channel, without repeats. Resolving at send time, not at rule
  save time, is the point: a handover needs no rule edit.
- **Rotation or channels.** Escalation is valid with at least one fixed channel, a rotation, or
  both. `AlertEscalationPolicy.IsDue` treats a rotation as a target.
- **Rotations choose the escalation target only.** The first notification still goes to the
  rule's own channels, and "Resolved" is unchanged (ADR-0064, ADR-0125).
- **Dashboard:** Settings > Workspace > On-call rotations (table showing who is on call and until
  when, form with an ordered participant list), and a rotation picker in the rule form's
  escalation section. The list and read responses are JSON only, not MemoryPack'd, like other
  small config responses; the rule fields are MemoryPack'd like the other rule members.

## Consequences

- A deleted rotation (or one that no longer resolves) leaves its rules escalating to their fixed
  channels; with none, the worker logs a warning and skips, as it does for unresolvable channels.
- A participant channel that is later deleted is skipped when sending; the table shows "Unknown
  channel" for it.
- Shifts are fixed-length and back to back. Time zones do not matter (a shift is a duration), but a
  "9 to 5 weekdays" or follow-the-sun schedule is not expressible.
- Rotations are instance-wide, not project-scoped, like channels and maintenance windows.
- Rotations are not exported or imported with a rule: ids are per instance.

## Not decided here

- One-off overrides ("swap Tuesday") and time-of-day or weekday restrictions.
- Rotations that choose the first-notification target, and multi-step policies.
- Ack from the notification (signed link, Slack button, PagerDuty ack sync).
