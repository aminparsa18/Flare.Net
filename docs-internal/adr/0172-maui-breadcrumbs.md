# ADR-0172: MAUI breadcrumbs

Status: Accepted

Date: 2026-10-10

## Context

The per-session timeline ([ADR-0170](0170-app-session-timeline.md)) lists a session's spans. For a crash it shows
the failing span but little of what the user did before it. Sentry-style breadcrumbs (lifecycle, taps, pages,
log lines) fill that gap. The roadmap sketched them as span events on the session.

## Decision

- **Breadcrumbs are zero-duration `breadcrumb` spans**, with `breadcrumb.category` and `breadcrumb.message`
  attributes, stamped with `session.id` by the existing `SessionProcessor`. A long-lived session span with events
  was rejected: a span only exports when it ends, and a crash is exactly when it would not. The span name stays
  constant so the Traces page does not fragment.
- **Sources:** `lifecycle` (foreground/background), `ui.tap` (any `Button.Clicked`, via a handler mapper),
  `ui.page` (`Application.PageAppearing`), `log` (an `ILogger` record at or above `BreadcrumbLogLevel`, default
  Information), and `FlareMaui.AddBreadcrumb(category, message)` for the app's own.
- **PII:** element text and page/window titles are included only with `IncludeTextInBreadcrumbs` /
  `IncludeTitleInBreadcrumbs` (both off). Otherwise a tap records `AutomationId` or the control type and a page
  records its type name. Messages are cut at 256 characters. Log breadcrumbs carry the log text, which is already
  exported as a log record; set `BreadcrumbLogLevel` higher or `None` to limit it.
- **Timeline API** returns `breadcrumbCategory` and `breadcrumbMessage` per event (read from the span attributes,
  no migration); the session page shows them beside the name.
- `Breadcrumbs = false` turns all of it off.

## Consequences

Breadcrumbs count toward the timeline's 1,000-span cap, so a chatty session truncates sooner; a per-session
breadcrumb cap is the follow-up if that shows up. They also add spans to the sessions table's span count.
