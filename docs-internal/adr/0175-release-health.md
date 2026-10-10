# ADR-0175: Release health

Status: Accepted

Date: 2026-10-10

## Context

The Sessions page ([ADR-0167](0167-app-sessions-view.md)) lists launches, and `Flare.Maui` reports fatal exceptions as
`app.unhandled_exception` spans with `exception.escaped = true` ([ADR-0166](0166-maui-sdk.md)). What a mobile team
asks before and after a rollout is "is 2.3.0 crashing more than 2.2.0", which needs a rate per app version, not a
list of sessions.

## Decision

- **Still derived at query time.** `POST /api/app-sessions/release-health {windowMinutes, endUnixMs, service}` groups
  the window's spans by `session.id`, then groups those sessions by `service.version`. No table or migration, for
  the reason ADR-0167 gave; the rollup table stays on the roadmap for when long windows prove slow.
- **Crashed session** = a span named `app.unhandled_exception` with `exception.escaped = true`. Non-fatal
  exceptions (`RecordException`, unobserved task exceptions) are not crashes. **Errored session** = any span with an
  error status, returned as well so a caller can show a broader "error-free" rate.
- **Users** are distinct `user.id` values on a span or resource attribute (OTel semconv). `Flare.Maui` sets none
  today and ADR-0166 collects no device identifier on purpose, so the users columns show a dash until the app
  sets one (the `SetUser` API on the roadmap). A crashed user is one with a crashed session.
- **Counts only in the API;** the dashboard computes `1 - crashed / total`, and the table is red under 99%.
- **Service filter only.** The version filter is ignored because the point is to compare versions. The table sits above
  the session list and honours its service and window; a failure of this request leaves the session list working.

## Not decided here

Native crashes that kill the process before a span is flushed (delivered on next launch once the Android and iOS
crash reports exist), a per-version trend over time, and alerting on a crash-free threshold.
