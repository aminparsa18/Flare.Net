# ADR-0139: On-call rotation coverage windows

Status: Accepted

Date: 2026-10-06

## Context

Rotations (ADR-0126) page around the clock, with one-off overrides (ADR-0137). Teams that only
staff business hours, or only nights and weekends, could not say so: the rotation paged someone
at 03:00 whether or not anyone covers that time.

## Decision

- **A rotation may carry one weekly coverage window.** `oncall_rotations.Coverage` (migration
  0063, plus the cluster variant) is a JSON object `{timeZone, days, startMinute, endMinute}`, or
  `''` for always (every existing rotation). A JSON `String` for the same reason as `Overrides`.
  `days` are `DayOfWeek` numbers (0 = Sunday); the window is `startMinute <= local minute < endMinute`
  on those days, in the IANA zone `timeZone`. `endMinute < startMinute` wraps past midnight and
  belongs to the day it starts on (Friday 22:00 to 06:00 is Friday night). A start equal to the end
  is rejected; use 0 to 1440 for a whole day.
- **Outside the window nobody is on call.** `OnCallSchedule.Resolve` still computes the scheduled
  participant (so the shift and handover stay stable) but sets `InCoverage = false`, and
  `EscalationTargets` then returns only the rule's fixed channels. An escalation with no fixed
  channels finds no targets and is retried on the next evaluation, so it goes out when the window
  opens, as long as the incident is still unacknowledged.
- **Overrides ignore coverage.** An explicit override is a person agreeing to be paged, so it
  always pages and reports `InCoverage = true`.
- **The shift clock is wall time.** Coverage filters when the on-call channel is paged; it does not
  pause the shift. A weekly shift still rotates every 168 hours.
- **Zone handling.** The zone is validated on write against the host's tz database. A stored zone
  the host cannot resolve (a Windows or minimal image) counts as covered, and a malformed stored
  value reads as no restriction, so a rotation never silently stops paging.
- **Dashboard:** a Coverage section in the rotation form (weekday toggles, start and end time,
  zone defaulting to the browser's) and a "Outside coverage" badge in the table.

## Consequences

- "Business hours only" is expressible. A rule that needs round-the-clock paging should list a
  fixed escalation channel as the backstop.
- DST is handled by converting the instant to the zone, so a window is always local wall-clock time.

## Not decided here

- Follow-the-sun (different participants in different windows). It needs per-participant windows or
  several rotations on one rule; neither is built.
- A "resumes at" time in the API and table.
