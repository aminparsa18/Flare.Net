# ADR-0137: On-call rotation overrides

Status: Accepted

Date: 2026-10-06

## Context

ADR-0126 computes who is on call from a fixed rotation. The only way to swap one shift ("Priya
covers Tuesday") was to edit the participant list, which shifts everyone after it and has to be
undone afterwards.

## Decision

- **Overrides belong to the rotation.** `oncall_rotations.Overrides` (migration 0062, plus the
  cluster variant) is a JSON array of `{startsAt, endsAt, channelId}`. A JSON `String` rather than
  parallel arrays or a table: overrides are read and written whole with their rotation and never
  filtered in SQL. Default `'[]'`, so existing rotations have none. Appended to `OnCallRotation`
  and `OnCallRotationRequest`; a malformed stored value reads as no overrides.
- **Resolution stays computed.** `OnCallSchedule.Resolve` first looks for an override with
  `startsAt <= now < endsAt` (latest-starting wins when they overlap), and returns its channel with
  `ShiftEndsAt = endsAt` and `IsOverride = true`; `NextChannelId` is whoever resolves at `endsAt`.
  Otherwise it resolves the schedule exactly as before. Escalation (ADR-0125) picks the override up
  with no worker change, because it already calls `Resolve` at send time.
- **Limits.** At most 100 per rotation. Each needs a channel and `endsAt > startsAt`. Past
  overrides are kept, not pruned; they are inert and show who covered when.
- **Dashboard:** an Overrides editor in the rotation form (channel, start, end, in the browser's
  zone) and an "Override" badge next to the on-call channel in the table.

## Consequences

- An override does not move the schedule: the underlying rotation carries on, so nobody after it
  is displaced.
- Overrides are not exported or imported with a rule, like the rotation itself.

## Not decided here

- Time-of-day and weekday restrictions.
- Overrides that apply to the first-notification target.
- Ack from the notification via a Slack button or PagerDuty ack sync.
