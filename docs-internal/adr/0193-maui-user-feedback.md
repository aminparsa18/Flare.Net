# ADR-0193: MAUI user feedback

Status: Accepted

Date: 2026-10-12

## Context

A crash report says what failed; it does not say what a user found broken without crashing ("the cart froze", "the price
is wrong"). Sentry's user feedback and Firebase's in-app report fill that gap. In Flare the missing piece is small: the
session already carries the timeline ([ADR-0170](0170-app-session-timeline.md)), breadcrumbs and, when enabled, replay frames
([ADR-0192](0192-maui-session-replay-lite.md)), so a message that names its session arrives with the context for free.

## Decision

- **A table, `app_feedback`** (migration 0082, single node and cluster): `StartTime` (receive time, named like
  `spans.StartTime` so the Traces retention TTL covers it, and listed in `RetentionSignals` next to `spans` and
  `app_screenshots`), `FeedbackId`, `ServiceName`, `SessionId`, `UserId`, `Name`, `Email`, `AppVersion`, `Message`. Feedback
  is text, so it does not go into `spans`, and it is not a log either: it needs its own list view, not log search.
- **`POST /v1/feedback` on Flare.Ingest**, a JSON body (`service`, `session_id`, `message` required; `user_id`, `name`,
  `email`, `app_version` optional; at most 16 KB, the message cut at 4,000 characters, other fields at 256). Under `/v1`, so
  the ingest-key middleware, its service allow-list and the browser CORS policy apply unchanged. A direct insert with no
  Redis stream, like screenshots ([ADR-0174](0174-maui-error-screenshots.md)): a user message is rare and the client
  reports a failure to the user, who can retry, which the at-least-once pipeline would only duplicate.
- **`POST /api/app-feedback/list`** returns the newest 200 messages in a window (default 60 minutes, clamped like the
  sessions list), optionally for one service or one session, under the existing service scope. A missing table (migration
  not applied) is an empty list.
- **Client:** `FlareMaui.SendFeedbackAsync(message, email, name)` returns whether the server stored it, and never throws.
  `FlareFeedback.PromptAsync(page)` is a built-in two-dialog prompt over it (message, then an optional e-mail), for apps
  that do not want to build a form. Name and e-mail are what the user typed for this report, so they are sent regardless of
  `SendDefaultPii`, which governs the identity the SDK stamps on its own. The user id comes from `SetUser`.
- **Dashboard:** `/sessions/feedback`, linked from the Sessions page: time, user, service and version, message, and an
  **Open session** button to that session's timeline and replay, with a 24 hour lookback so the whole session is in range.

## Not decided here

A screenshot attached to the report itself (the session's replay frames cover the common case, and a one-off capture needs
the same masking work as ADR-0192), a resolved/assigned state on a message (ClickHouse rows are immutable; it wants the
issue-workflow design on the roadmap), an alert or notification on new feedback, an offline queue for unsent reports, and
scrubbing of the free-text message.

## Consequences

- The message is free text and is stored as typed; an app that must not receive personal data should not offer the prompt
  or should pre-process the text before calling `SendFeedbackAsync`.
- Anyone with an ingest key for the service can post feedback, as with every other `/v1` endpoint; the 16 KB cap and the
  per-key limits ([ADR-0051](0051-per-ingest-key-ingestion-limits.md)) bound the abuse, and the service allow-list scopes it.
- Existing deployments must apply migration 0082 before the SDK sends feedback; until then uploads fail with 503 and
  `SendFeedbackAsync` returns false.
