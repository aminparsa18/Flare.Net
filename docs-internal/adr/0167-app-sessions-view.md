# ADR-0167: App sessions view

Status: Accepted

Date: 2026-10-09

## Context

`Flare.Maui` (ADR-0166) stamps a `session.id` on every span, one per app launch, so a mobile team can ask "what
happened in this user's launch". Until now the only way to use it was to filter Traces by the attribute by hand,
which needs the id already in hand. The roadmap's phase 3 for the MAUI SDK asked for scoped public ingest keys, a
Sessions view and app-version breakdowns on `/errors`. Two of the three already exist: ingest keys have an origin
allowlist (ADR-0149), a service allowlist (ADR-0150) and rate caps (ADR-0051), and the `/errors` facet sidebar
offers `service.version` by default.

## Decision

- **A session is derived, not stored.** `POST /api/app-sessions/list` groups the window's spans by
  `SpanAttributes['session.id']` at query time. No table, materialized view or migration. This is the same trade
  ADR-0071 made for external APIs: an on-demand investigative page can afford a window scan, and the
  `mapContains` guard lets the existing attribute-key index skip granules with no session attribute.
- **Per session:** service, `service.version`, OS, device model (taken with `any()`, since they are constant for
  a process start), first/last span time, span count, distinct trace count, error-span count and up to eight
  distinct `screen.name` values (unordered).
- **Filters:** service, app version and errors-only (`HAVING ErrorCount > 0`), over the same 5 minute to 24 hour
  presets as the other windowed pages (server clamp 5 minutes to 7 days). Service and version pickers are returned
  unfiltered so choosing one does not hide the rest.
- **Row cap of 500, most recently active first.** One extra row is fetched so the response says `truncated`.
- **Click-through is a Traces deep link** with a one-condition structural query on `session.id` over the session's
  time span, not a root-row filter, because a session's traces are often rooted at a server span without the
  attribute.
- **Plain JSON, no MemoryPack adapter.** Small flat rows, like SLOs and error issues.
- **Project scoping** is the ambient `ServiceScope`, as for every other span query.
- **Named `app-sessions`** in the API and `/sessions` in the dashboard to stay distinct from
  `/api/auth/sessions`, the caller's own login sessions.

## Alternatives considered

- **A `sessions` rollup table** fed by a materialized view. Rejected for now: it would be faster for long windows
  but adds a migration and a second copy of the data for a page used on demand. Revisit if the 7-day window is slow
  on real data.
- **A per-session timeline page.** The Traces deep link already lists the session's traces in time order; a
  dedicated timeline can follow if teams ask.
- **Hashing or persisting a device id** to group sessions per device. Rejected: ADR-0166 collects no stable device
  identifier on purpose.
